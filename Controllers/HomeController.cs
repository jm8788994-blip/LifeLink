using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace LifeLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
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

                var donorRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Donor");
                int roleId = donorRole?.RoleId ?? 2;

                if (existingUser == null)
                {
                    existingUser = new User
                    {
                        Name = model.FullName,
                        Email = model.Email,
                        Phone = model.PhoneNumber,
                        PasswordHash = "donor@123",
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
                        Availability = model.IsActive,
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
                    existingUser.DonorProfile.Availability = model.IsActive;
                    if (model.LastDonationDate.HasValue)
                    {
                        existingUser.DonorProfile.LastDonationDate = DateTime.SpecifyKind(model.LastDonationDate.Value, DateTimeKind.Utc);
                    }
                }

                // Add notification
                _context.Notifications.Add(new Notification
                {
                    UserId = existingUser.UserId,
                    Message = "Thank you for registering as a blood donor on LifeLink! Your profile is active.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                TempData["Success"] = "Donor registration successful! Thank you for joining LifeLink to save lives.";
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
                var receiverRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Receiver");
                int roleId = receiverRole?.RoleId ?? 3;

                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
