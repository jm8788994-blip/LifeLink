using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Models.ViewModels;
using LifeLink.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasherService _hasher;
        private readonly IOtpService _otpService;
        private readonly IEmailSenderService _emailSender;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ApplicationDbContext context,
            IPasswordHasherService hasher,
            IOtpService otpService,
            IEmailSenderService emailSender,
            ILogger<AccountController> logger)
        {
            _context = context;
            _hasher = hasher;
            _otpService = otpService;
            _emailSender = emailSender;
            _logger = logger;
        }

        // ==================== LOGIN ====================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToRoleDashboard();
            }

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLower());

            if (user == null || !_hasher.VerifyPassword(user.PasswordHash, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Invalid email address or password.");
                return View(model);
            }

            if (user.Status == "Pending_Verification")
            {
                // Unverified account: redirect to OTP verification
                var otp = _otpService.GenerateOtp(user.Email, "Registration");
                TempData["Info"] = "Please verify your account with the OTP code sent to your email.";
                return RedirectToAction(nameof(VerifyOtp), new { email = user.Email, purpose = "Registration" });
            }

            if (user.Status == "Suspended" || user.Status == "Inactive")
            {
                ModelState.AddModelError(string.Empty, "Your account is currently inactive or suspended. Please contact administrator.");
                return View(model);
            }

            // If user logged in with legacy plain text password, automatically upgrade to PBKDF2 hash
            if (user.PasswordHash == model.Password)
            {
                user.PasswordHash = _hasher.HashPassword(model.Password);
                await _context.SaveChangesAsync();
            }

            // Issue Claims and sign in
            await SignInUserAsync(user, model.RememberMe);

            TempData["Success"] = $"Welcome back, {user.Name}!";

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToRoleDashboard(user.Role?.RoleName);
        }

        // ==================== REGISTER ====================
        [HttpGet]
        public IActionResult Register(string? role = "Donor")
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToRoleDashboard();
            }

            var model = new RegisterViewModel
            {
                Role = (role?.ToLower() == "receiver") ? "Receiver" : "Donor"
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim().ToLower();
            var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (existing != null)
            {
                if (existing.Status == "Pending_Verification")
                {
                    // Existing unverified user, regenerate OTP
                    var resendOtp = _otpService.GenerateOtp(existing.Email, "Registration");
                    TempData["Info"] = "Account already exists but pending verification. Enter the OTP code to activate.";
                    return RedirectToAction(nameof(VerifyOtp), new { email = existing.Email, purpose = "Registration" });
                }

                ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists. Please login instead.");
                return View(model);
            }

            var selectedRoleName = (model.Role?.ToLower() == "receiver") ? "Receiver" : "Donor";
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == selectedRoleName)
                       ?? await _context.Roles.FirstAsync();

            var newUser = new User
            {
                Name = model.FullName.Trim(),
                Email = normalizedEmail,
                Phone = model.Phone.Trim(),
                PasswordHash = _hasher.HashPassword(model.Password),
                RoleId = role.RoleId,
                Status = "Pending_Verification", // Requires OTP verification
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            if (selectedRoleName == "Donor")
            {
                var donorProfile = new DonorProfile
                {
                    UserId = newUser.UserId,
                    BloodGroup = model.BloodGroup,
                    DateOfBirth = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc),
                    Location = model.Location,
                    Availability = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DonorProfiles.Add(donorProfile);
                await _context.SaveChangesAsync();
            }

            // Generate OTP
            var otp = _otpService.GenerateOtp(newUser.Email, "Registration");

            // Dispatch Email asynchronously
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2 style='color: #e53935;'>LifeLink Verification Code</h2>
                    <p>Hello <strong>{newUser.Name}</strong>,</p>
                    <p>Thank you for registering on <strong>LifeLink</strong> - AI Powered Blood Donation System.</p>
                    <p>Your one-time verification code is:</p>
                    <div style='background: #fdf2f2; border: 2px dashed #e53935; padding: 15px; font-size: 28px; font-weight: bold; letter-spacing: 5px; text-align: center; color: #c62828; margin: 20px 0;'>
                        {otp}
                    </div>
                    <p>This code is valid for <strong>5 minutes</strong>. Do not share this code with anyone.</p>
                    <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'/>
                    <p style='font-size: 12px; color: #777;'>LifeLink - Saving lives through intelligent blood donation.</p>
                </div>";

            _ = _emailSender.SendEmailAsync(newUser.Email, "LifeLink - Verify Your Account", emailBody);

            TempData["Success"] = "Verification code generated! Please enter the 6-digit code to activate your account.";
            return RedirectToAction(nameof(VerifyOtp), new { email = newUser.Email, purpose = "Registration" });
        }

        // ==================== VERIFY OTP ====================
        [HttpGet]
        public IActionResult VerifyOtp(string email, string purpose = "Registration", string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(Login));
            }

            var model = new VerifyOtpViewModel
            {
                Email = email,
                Purpose = purpose,
                ReturnUrl = returnUrl,
                DemoOtpHint = _otpService.GetLatestOtp(email, purpose)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model)
        {
            // Populate hint in case model state has errors
            model.DemoOtpHint = _otpService.GetLatestOtp(model.Email, model.Purpose);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            bool isValid = _otpService.ValidateOtp(model.Email, model.Code, model.Purpose);
            if (!isValid)
            {
                ModelState.AddModelError(nameof(model.Code), "Invalid or expired verification code. Please request a new one.");
                return View(model);
            }

            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLower());

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User account not found.");
                return View(model);
            }

            if (model.Purpose == "Registration")
            {
                user.Status = "Active";
                if (user.DonorProfile != null)
                {
                    user.DonorProfile.Availability = true;
                }

                // Add welcome notification
                _context.Notifications.Add(new Notification
                {
                    UserId = user.UserId,
                    Message = "Your account has been verified successfully! Welcome to LifeLink.",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });

                await _context.SaveChangesAsync();

                // Sign in the newly verified user automatically
                await SignInUserAsync(user, isPersistent: true);

                TempData["Success"] = "Account verified successfully! Welcome to your LifeLink dashboard.";
                return RedirectToRoleDashboard(user.Role?.RoleName);
            }
            else if (model.Purpose == "PasswordReset")
            {
                return RedirectToAction(nameof(ResetPassword), new { email = model.Email, code = model.Code });
            }

            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string email, string purpose = "Registration")
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLower());
            if (user != null)
            {
                var newOtp = _otpService.GenerateOtp(user.Email, purpose);

                var emailBody = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                        <h2 style='color: #e53935;'>LifeLink New Verification Code</h2>
                        <p>Hello <strong>{user.Name}</strong>,</p>
                        <p>Here is your new one-time verification code:</p>
                        <div style='background: #fdf2f2; border: 2px dashed #e53935; padding: 15px; font-size: 28px; font-weight: bold; letter-spacing: 5px; text-align: center; color: #c62828; margin: 20px 0;'>
                            {newOtp}
                        </div>
                        <p>This code will expire in 5 minutes.</p>
                    </div>";

                _ = _emailSender.SendEmailAsync(user.Email, "LifeLink - New OTP Code", emailBody);
            }

            TempData["Success"] = "A new verification code has been dispatched to your email!";
            return RedirectToAction(nameof(VerifyOtp), new { email, purpose });
        }

        // ==================== FORGOT & RESET PASSWORD ====================
        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLower());
            if (user != null)
            {
                var otp = _otpService.GenerateOtp(user.Email, "PasswordReset");
                var body = $"<p>Your LifeLink password reset code is: <strong>{otp}</strong> (Expires in 5 minutes).</p>";
                _ = _emailSender.SendEmailAsync(user.Email, "LifeLink - Password Reset Code", body);
            }

            // Always redirect to VerifyOtp to prevent email enumeration
            TempData["Info"] = "If an account exists with this email, a reset code has been sent.";
            return RedirectToAction(nameof(VerifyOtp), new { email = model.Email, purpose = "PasswordReset" });
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string code)
        {
            return View(new ResetPasswordViewModel { Email = email, Code = code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLower());
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User account not found.");
                return View(model);
            }

            user.PasswordHash = _hasher.HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Password reset successfully! You can now log in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        // ==================== LOGOUT ====================
        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Info"] = "You have been logged out successfully.";
            return RedirectToAction("Index", "Home");
        }

        // ==================== ACCESS DENIED ====================
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ==================== HELPER METHODS ====================
        private async Task SignInUserAsync(User user, bool isPersistent)
        {
            var roleName = user.Role?.RoleName ?? "Donor";

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, roleName)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = isPersistent ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
        }

        private IActionResult RedirectToRoleDashboard(string? roleName = null)
        {
            roleName ??= User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            return roleName switch
            {
                "Admin" => RedirectToAction("Dashboard", "Admin"),
                "Donor" => RedirectToAction("Dashboard", "Donor"),
                "Receiver" => RedirectToAction("Dashboard", "Receiver"),
                "Hospital" => RedirectToAction("Dashboard", "Hospital"),
                _ => RedirectToAction("Index", "Home")
            };
        }
    }
}
