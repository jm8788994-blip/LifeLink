using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace LifeLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasherService _hasher;

        public HomeController(ApplicationDbContext context, IPasswordHasherService hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        public async Task<IActionResult> Index()
        {
            // Dynamic statistics for landing page
            var totalDonations = await _context.DonationHistory.CountAsync(d => d.Status == "Completed");
            ViewBag.LivesSaved = (totalDonations * 3) + 150;
            ViewBag.TotalDonors = await _context.DonorProfiles.CountAsync();
            ViewBag.TotalBloodUnits = await _context.BloodStock.SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.TotalHospitals = await _context.Hospitals.CountAsync();

            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        public IActionResult DonorRegister()
        {
            return View(new Donor());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DonorRegister(Donor model)
        {
            if (ModelState.IsValid)
            {
                // Check if user already exists
                var existingUser = await _context.Users
                    .Include(u => u.DonorProfile)
                    .FirstOrDefaultAsync(u => u.Email == model.Email);

                bool isAdult = AgeHelper.IsAdult(model.DateOfBirth);
                bool donorActive = isAdult && model.IsActive;

                // Unified role: every account is "Donor & Receiver". Donor donation is gated by the 18+ rule.
                var role = await _context.Roles.FirstAsync(r => r.RoleName == "Donor & Receiver");
                int roleId = role.RoleId;

                if (existingUser == null)
                {
                    existingUser = new User
                    {
                        Name = model.FullName,
                        Email = model.Email,
                        Phone = model.PhoneNumber,
                        PasswordHash = _hasher.HashPassword("donor@123"),
                        RoleId = roleId,
                        Status = model.IsActive ? "Active" : "Inactive",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Users.Add(existingUser);
                    await _context.SaveChangesAsync();
                }

                if (existingUser.DonorProfile == null)
                {
                    var donorProfile = new DonorProfile
                    {
                        UserId = existingUser.UserId,
                        BloodGroup = model.BloodType,
                        DateOfBirth = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc),
                        Location = model.Address,
                        Availability = donorActive,
                        LastDonationDate = model.LastDonationDate.HasValue 
                            ? DateTime.SpecifyKind(model.LastDonationDate.Value, DateTimeKind.Utc) 
                            : null,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.DonorProfiles.Add(donorProfile);
                }
                else
                {
                    existingUser.DonorProfile.BloodGroup = model.BloodType;
                    existingUser.DonorProfile.Location = model.Address;
                    existingUser.DonorProfile.Availability = donorActive;
                    if (model.LastDonationDate.HasValue)
                    {
                        existingUser.DonorProfile.LastDonationDate = DateTime.SpecifyKind(model.LastDonationDate.Value, DateTimeKind.Utc);
                    }
                }

                // Add notification
                _context.Notifications.Add(new Notification
                {
                    UserId = existingUser.UserId,
                    Message = isAdult
                        ? "Thank you for registering as a blood donor on LifeLink! Your profile is active."
                        : "You are under 18, so your Donor role stays blocked. You can request blood now and become a donor when you turn 18.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = isAdult
                    ? "Donor registration successful! Thank you for joining LifeLink to save lives."
                    : "Registration successful! Under-18 accounts can request blood; donating will be unlocked when you turn 18.";
                return RedirectToAction("Index");
            }
            return View(model);
        }

        public IActionResult RecipientRegister(string? bloodType)
        {
            var model = new Recipient();
            if (!string.IsNullOrEmpty(bloodType))
            {
                model.BloodType = bloodType;
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientRegister(Recipient model)
        {
            if (ModelState.IsValid)
            {
                var bothRole = await _context.Roles.FirstAsync(r => r.RoleName == "Donor & Receiver");
                int roleId = bothRole.RoleId;

                var existingUser = await _context.Users.Include(u => u.DonorProfile).FirstOrDefaultAsync(u => u.Email == model.Email);
                if (existingUser == null)
                {
                    existingUser = new User
                    {
                        Name = model.FullName,
                        Email = model.Email,
                        Phone = model.PhoneNumber,
                        PasswordHash = "receiver@123",
                        RoleId = roleId,
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Users.Add(existingUser);
                    await _context.SaveChangesAsync();
                }

                // Donor profile kept for later use but Availability stays off until age 18 is confirmed
                if (existingUser.DonorProfile == null)
                {
                    _context.DonorProfiles.Add(new DonorProfile
                    {
                        UserId = existingUser.UserId,
                        BloodGroup = model.BloodType,
                        DateOfBirth = DateTime.UtcNow.AddYears(-25),
                        Location = string.IsNullOrWhiteSpace(model.Address) ? "Dhaka" : model.Address,
                        Availability = false,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Find or match hospital
                var hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.Name.ToLower().Contains(model.HospitalName.ToLower()))
                               ?? await _context.Hospitals.FirstOrDefaultAsync();

                var bloodRequest = new BloodRequest
                {
                    ReceiverId = existingUser.UserId,
                    BloodGroup = model.BloodType,
                    Quantity = 1,
                    HospitalId = hospital?.HospitalId,
                    Location = string.IsNullOrWhiteSpace(model.Address) ? (hospital?.Location ?? "Dhaka") : model.Address,
                    EmergencyLevel = "Urgent",
                    Reason = model.Reason,
                    PatientName = string.IsNullOrWhiteSpace(model.PatientName) ? model.FullName : model.PatientName,
                    DiseaseName = model.DiseaseName,
                    Hemoglobin = model.Hemoglobin,
                    RequestDate = DateTime.UtcNow,
                    RequiredDate = DateTime.SpecifyKind(model.RequiredDate, DateTimeKind.Utc),
                    Status = "Pending"
                };

                _context.BloodRequests.Add(bloodRequest);

                _context.Notifications.Add(new Notification
                {
                    UserId = existingUser.UserId,
                    Message = $"Your blood request for {model.BloodType} at {model.HospitalName} has been received and broadcasted to matching donors.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "Blood request submitted successfully! We are matching you with donors and nearby hospitals.";
                return RedirectToAction("Index");
            }
            return View(model);
        }

        public async Task<IActionResult> BloodAvailability(string? bloodGroup, string? search)
        {
            var query = _context.BloodStock.Include(s => s.Hospital).AsQueryable();

            if (!string.IsNullOrWhiteSpace(bloodGroup) && bloodGroup != "All")
            {
                query = query.Where(s => s.BloodGroup == bloodGroup);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(st => 
                    st.BloodGroup.ToLower().Contains(s) || 
                    (st.Hospital != null && (st.Hospital.Name.ToLower().Contains(s) || st.Hospital.Location.ToLower().Contains(s))));
            }

            var bloodStocks = await query.OrderBy(s => s.BloodGroup).ThenByDescending(s => s.Quantity).ToListAsync();

            ViewBag.SelectedBloodGroup = bloodGroup ?? "All";
            ViewBag.SearchQuery = search ?? "";

            return View(bloodStocks);
        }

        public async Task<IActionResult> FindDonors(string? search, string? bloodGroup)
        {
            var query = _context.DonorProfiles
                .Include(d => d.User)
                .Where(d => d.Availability && d.User != null && d.User.Status == "Active")
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(bloodGroup) && bloodGroup != "All")
            {
                query = query.Where(d => d.BloodGroup == bloodGroup);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(d => d.Location.ToLower().Contains(s));
            }

            var donors = (await query
                .OrderByDescending(d => d.DonorId)
                .ToListAsync())
                .Where(d => AgeHelper.IsAdult(d.DateOfBirth))
                .ToList();

            ViewBag.Search = search;
            ViewBag.BloodGroup = bloodGroup ?? "All";

            return View(donors);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // ==================== HOSPITAL REGISTRATION (PUBLIC) ====================
        public IActionResult HospitalRegister()
        {
            return View(new HospitalRegistrationViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HospitalRegister(HospitalRegistrationViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim().ToLower();
            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
                return View(model);
            }

            var hospitalRole = await _context.Roles.FirstAsync(r => r.RoleName == "Hospital");
            int hospitalRoleId = hospitalRole.RoleId;

            var hospitalUser = new User
            {
                Name = model.Name,
                Email = normalizedEmail,
                Phone = model.Contact,
                PasswordHash = _hasher.HashPassword(model.Password),
                RoleId = hospitalRoleId,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(hospitalUser);
            await _context.SaveChangesAsync();

            var hospital = new Hospital
            {
                Name = model.Name,
                Address = model.Address,
                Location = model.Location,
                Contact = model.Contact,
                VerificationStatus = "Pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.Hospitals.Add(hospital);

            // Notify all admins for review
            var adminRole = await _context.Roles.FirstAsync(r => r.RoleName == "Admin");
            var admins = await _context.Users.Where(u => u.RoleId == adminRole.RoleId).ToListAsync();
            foreach (var admin in admins)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = admin.UserId,
                    Message = $"New hospital registration pending review: {model.Name}.",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Hospital &quot;{model.Name}&quot; registered successfully! It will appear once the administrator verifies it.";
            return RedirectToAction("Index");
        }
    }
}
