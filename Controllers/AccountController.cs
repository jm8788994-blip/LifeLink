using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Models.ViewModels;
using LifeLink.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
        private readonly IWebHostEnvironment _env;

        public AccountController(
            ApplicationDbContext context,
            IPasswordHasherService hasher,
            IOtpService otpService,
            IEmailSenderService emailSender,
            ILogger<AccountController> logger,
            IWebHostEnvironment env)
        {
            _context = context;
            _hasher = hasher;
            _otpService = otpService;
            _emailSender = emailSender;
            _logger = logger;
            _env = env;
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

            // Legacy pending_verification accounts are auto-activated so login always proceeds
            // (email OTP verification has been removed from registration).
            if (user.Status == "Pending_Verification")
            {
                user.Status = "Active";
                await _context.SaveChangesAsync();
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
                ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists. Please login instead.");
                return View(model);
            }

            // Every new account gets ONE unified role: "Donor & Receiver".
            // No selection, no confusion - you can request blood AND donate when eligible.
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Donor & Receiver")
                       ?? await _context.Roles.FirstAsync();

            bool isAdult = AgeHelper.IsAdult(model.DateOfBirth);

            var newUser = new User
            {
                Name = model.FullName.Trim(),
                Email = normalizedEmail,
                Phone = model.Phone.Trim(),
                PasswordHash = _hasher.HashPassword(model.Password),
                RoleId = role.RoleId,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            newUser.Role = role;

            // Donor profile is always kept (stores DOB/blood/location).
            // For minors Availability stays false so they never appear as an available donor.
            var donorProfile = new DonorProfile
            {
                UserId = newUser.UserId,
                BloodGroup = model.BloodGroup,
                DateOfBirth = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc),
                Location = model.Location,
                Availability = isAdult,
                CreatedAt = DateTime.UtcNow
            };
            _context.DonorProfiles.Add(donorProfile);
            await _context.SaveChangesAsync();

            // Welcome notification
            _context.Notifications.Add(new Notification
            {
                UserId = newUser.UserId,
                Message = "Welcome to LifeLink! Your account is active. You can request blood immediately and donate once you set your blood group.",
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            });
            await _context.SaveChangesAsync();

            // Sign the new user in immediately (no email OTP verification required)
            await SignInUserAsync(newUser, isPersistent: false);

            TempData["Success"] = $"Welcome to LifeLink, {newUser.Name}! Your account is now active.";
            return RedirectToRoleDashboard(newUser.Role.RoleName);
        }

        // ==================== VERIFY OTP ====================
        [HttpGet]
        public IActionResult VerifyOtp(string email, string purpose = "Registration", string? returnUrl = null, string? code = null)
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
                Code = code ?? string.Empty,
                DemoOtpHint = _otpService.GetLatestOtp(email, purpose) ?? TempData["OtpPreview"]?.ToString()
            };

            // When the user arrives via the "One-Click Verify" link from the email,
            // auto-submit the form once the code has been pre-filled.
            ViewBag.AutoVerify = !string.IsNullOrWhiteSpace(code);

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
                    // Donor capability still gated: minors never become "available" donors
                    user.DonorProfile.Availability = AgeHelper.IsAdult(user.DonorProfile.DateOfBirth);
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
                TempData["OtpPreview"] = newOtp;
                var verifyLink = BuildOtpLink(user.Email, purpose, newOtp);

                var emailBody = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                        <h2 style='color: #e53935;'>LifeLink New Verification Code</h2>
                        <p>Hello <strong>{user.Name}</strong>,</p>
                        <p>Here is your new one-time verification code:</p>
                        <div style='background: #fdf2f2; border: 2px dashed #e53935; padding: 15px; font-size: 28px; font-weight: bold; letter-spacing: 5px; text-align: center; color: #c62828; margin: 20px 0;'>
                            {newOtp}
                        </div>
                        {OtpVerifyButton(verifyLink)}
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
                TempData["OtpPreview"] = otp;
                var resetLink = BuildOtpLink(user.Email, "PasswordReset", otp);
                var body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                        <h2 style='color: #e53935;'>LifeLink Password Reset</h2>
                        <p>Hello <strong>{user.Name}</strong>,</p>
                        <p>Use the code below to reset your password:</p>
                        <div style='background: #fdf2f2; border: 2px dashed #e53935; padding: 15px; font-size: 28px; font-weight: bold; letter-spacing: 5px; text-align: center; color: #c62828; margin: 20px 0;'>
                            {otp}
                        </div>
                        {OtpVerifyButton(resetLink)}
                        <p>This code will expire in 5 minutes.</p>
                    </div>";
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

        // ==================== MY HISTORY (DONATIONS + REQUESTS) ====================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> MyHistory()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.DonorProfile)
                .Include(u => u.BloodRequests)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            // Donations made by this user (as a donor)
            List<DonationHistory> donations = new();
            if (user.DonorProfile != null)
            {
                donations = await _context.DonationHistory
                    .Include(d => d.Hospital)
                    .Include(d => d.BloodRequest)
                    .Where(d => d.DonorId == user.DonorProfile.DonorId)
                    .OrderByDescending(d => d.DonationDate)
                    .ToListAsync();
            }

            // Blood requests placed by this user (as a receiver)
            var requests = await _context.BloodRequests
                .Include(r => r.Hospital)
                .Where(r => r.ReceiverId == userId)
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            ViewBag.Donations = donations;
            ViewBag.Requests = requests;
            ViewBag.ProfileBloodGroup = user.DonorProfile?.BloodGroup;
            return View(user);
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

        // ==================== EDIT PROFILE (PDF 4.1 & 4.17) ====================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> EditProfile()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            var model = new ProfileEditViewModel
            {
                FullName = user.Name,
                Phone = user.Phone,
                BloodGroup = user.DonorProfile?.BloodGroup ?? string.Empty,
                Location = user.DonorProfile?.Location ?? string.Empty,
                DateOfBirth = user.DonorProfile?.DateOfBirth ?? DateTime.UtcNow.AddYears(-22),
                ExistingProfilePicture = user.ProfilePicturePath
            };

            ViewBag.IsMinor = !AgeHelper.IsAdult(model.DateOfBirth);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> EditProfile(ProfileEditViewModel model, IFormFile? profilePicture)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.IsMinor = !AgeHelper.IsAdult(model.DateOfBirth);
                return View(model);
            }

            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            // Save new profile picture (image upload - PDF 4.17)
            if (profilePicture != null && profilePicture.Length > 0)
            {
                string? savedPath = await SaveProfilePictureAsync(profilePicture);
                if (savedPath == null)
                {
                    ModelState.AddModelError(nameof(profilePicture), "Only image files (jpg, png, gif, webp) up to 2 MB are allowed.");
                    ViewBag.IsMinor = !AgeHelper.IsAdult(model.DateOfBirth);
                    return View(model);
                }

                if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
                {
                    DeleteUploadedFile(user.ProfilePicturePath);
                }

                user.ProfilePicturePath = savedPath;
            }

            user.Name = model.FullName.Trim();
            user.Phone = model.Phone.Trim();

            if (user.DonorProfile != null)
            {
                user.DonorProfile.BloodGroup = model.BloodGroup;
                user.DonorProfile.Location = model.Location.Trim();
                user.DonorProfile.DateOfBirth = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc);
                user.DonorProfile.Availability = AgeHelper.IsAdult(model.DateOfBirth);
            }

            await _context.SaveChangesAsync();

            // Re-issue cookie so the updated display name is reflected immediately
            await SignInUserAsync(user, isPersistent: true);

            TempData["Success"] = "Your profile has been updated successfully.";
            return RedirectToAction(nameof(EditProfile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user != null && !string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                DeleteUploadedFile(user.ProfilePicturePath);
                user.ProfilePicturePath = null;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Profile picture removed.";
            }

            return RedirectToAction(nameof(EditProfile));
        }

        private async Task<string?> SaveProfilePictureAsync(IFormFile file)
        {
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowed.Contains(ext) || file.Length > 2 * 1024 * 1024)
            {
                return null;
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"user_{GetCurrentUserId()}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/profiles/{fileName}";
        }

        private static void DeleteUploadedFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;

                var absolutePath = Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    "wwwroot",
                    path.TrimStart('/', '\\'));

                if (System.IO.File.Exists(absolutePath))
                {
                    System.IO.File.Delete(absolutePath);
                }
            }
            catch
            {
                // best-effort cleanup; never break the request for a media file
            }
        }

        // ==================== MY ROLES (SELF-SERVICE DONOR / RECEIVER TOGGLE) ====================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> MyRoles()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            var isAdult = AgeHelper.IsAdult(user.DonorProfile?.DateOfBirth);
            ViewBag.IsDonor = user.Role?.RoleName is "Donor" or "Donor & Receiver";
            ViewBag.IsMinor = !isAdult;
            ViewBag.IsEligible = isAdult && !string.IsNullOrWhiteSpace(user.DonorProfile?.BloodGroup);
            ViewBag.IsDonorActive = user.DonorProfile?.Availability == true;
            ViewBag.BloodGroup = user.DonorProfile?.BloodGroup;
            ViewBag.HasCompatible = user.Role?.RoleName is "Donor" or "Donor & Receiver";
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> UpdateRoles(bool donorActive)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            var currentRole = user.Role?.RoleName;
            if (currentRole is not ("Donor" or "Receiver" or "Donor & Receiver"))
            {
                TempData["Error"] = "Role control for this account is managed by the administrator.";
                return RedirectToAction(nameof(MyRoles));
            }

            // Under-18 users can never become an active donor (eligibility gate)
            if (donorActive && !AgeHelper.IsAdult(user.DonorProfile?.DateOfBirth))
            {
                TempData["Error"] = "You must be 18 years or older to donate blood.";
                return RedirectToAction(nameof(MyRoles));
            }

            if (donorActive && (user.DonorProfile == null || string.IsNullOrWhiteSpace(user.DonorProfile.BloodGroup)))
            {
                TempData["Error"] = "Please set your blood group on your donor profile before activating donor status.";
                return RedirectToAction(nameof(MyRoles));
            }

            if (donorActive)
            {
                user.DonorProfile!.Availability = true;
            }
            else if (user.DonorProfile != null)
            {
                user.DonorProfile.Availability = false;
            }

            await _context.SaveChangesAsync();

            // Re-issue auth cookie so the new role applies immediately
            await SignInUserAsync(user, isPersistent: true);

            TempData["Success"] = donorActive
                ? "Your donor status is now ACTIVE. You can receive donation requests and appear in the donor search."
                : "Your donor status is now INACTIVE. You can still request blood anytime.";
            return RedirectToAction(nameof(MyRoles));
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

        private int GetCurrentUserId()
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;
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
                "Donor & Receiver" => RedirectToAction("Dashboard", "Donor"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        // Builds an absolute one-click verification link for OTP emails.
        // The code travels in the query string so VerifyOtp can pre-fill + auto-submit.
        private string BuildOtpLink(string email, string purpose, string code)
        {
            var url = Url.Action(nameof(VerifyOtp), "Account", new { email, purpose, code });
            return $"{Request.Scheme}://{Request.Host}{url}";
        }

        private static string OtpVerifyButton(string link)
        {
            return $@"
                    <div style='text-align: center; margin: 24px 0;'>
                        <a href='{link}' style='background: #e53935; color: #ffffff; text-decoration: none; padding: 12px 28px; border-radius: 50px; font-size: 15px; font-weight: bold; display: inline-block;'>
                            One-Click Verify
                        </a>
                        <p style='font-size: 12px; color: #888; margin-top: 8px;'>Or manually enter the code below on the verification page.</p>
                    </div>";
        }
    }
}
