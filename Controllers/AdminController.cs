using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasherService _hasher;

        public AdminController(ApplicationDbContext context, IPasswordHasherService hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        public async Task<IActionResult> Dashboard()
        {
            ViewBag.TotalDonors = await _context.DonorProfiles.CountAsync();
            ViewBag.TotalRecipients = await _context.BloodRequests.CountAsync();
            ViewBag.PendingRequests = await _context.BloodRequests.CountAsync(r => r.Status == "Pending");
            ViewBag.TotalBloodBags = await _context.BloodStock.SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.AvailableBags = await _context.BloodStock.Where(s => s.Status == "Available").SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.LowStockCount = await _context.BloodStock.CountAsync(s => s.Status == "Low Stock" || s.Quantity < 10);
            ViewBag.TotalHospitals = await _context.Hospitals.CountAsync();

            ViewBag.RecentDonors = await _context.DonorProfiles
                .Include(d => d.User)
                .OrderByDescending(d => d.CreatedAt)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentRequests = await _context.BloodRequests
                .Include(r => r.Receiver)
                .Include(r => r.Hospital)
                .OrderByDescending(r => r.RequestDate)
                .Take(5)
                .ToListAsync();

            ViewBag.StockByGroup = await _context.BloodStock
                .GroupBy(s => s.BloodGroup)
                .Select(g => new { BloodGroup = g.Key, TotalQuantity = g.Sum(s => s.Quantity) })
                .OrderBy(g => g.BloodGroup)
                .ToListAsync();

            return View();
        }

        // --- Donors Management ---
        public async Task<IActionResult> Donors(string? search, string? bloodType, int page = 1)
        {
            var query = _context.DonorProfiles.Include(d => d.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(bloodType) && bloodType != "All")
            {
                query = query.Where(d => d.BloodGroup == bloodType);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(d => 
                    d.User != null && (d.User.Name.ToLower().Contains(s) || d.User.Email.ToLower().Contains(s) || d.User.Phone.Contains(s)) ||
                    d.Location.ToLower().Contains(s) ||
                    d.BloodGroup.ToLower().Contains(s));
            }

            const int pageSize = 10;
            int totalCount = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var donors = await query
                .OrderByDescending(d => d.DonorId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.BloodType = bloodType;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            return View(donors);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DonorAdd(string fullName, string email, string phone, string bloodType, string address, DateTime dateOfBirth)
        {
            var compositeRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Donor & Receiver");
            var user = new User
            {
                Name = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = _hasher.HashPassword("donor@123"),
                RoleId = compositeRole?.RoleId ?? 2,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var profile = new DonorProfile
            {
                UserId = user.UserId,
                BloodGroup = bloodType,
                DateOfBirth = DateTime.SpecifyKind(dateOfBirth, DateTimeKind.Utc),
                Location = address,
                Availability = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.DonorProfiles.Add(profile);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Donor added successfully!";
            return RedirectToAction(nameof(Donors));
        }

        public async Task<IActionResult> DonorEdit(int id)
        {
            var donor = await _context.DonorProfiles
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DonorId == id);

            if (donor == null)
            {
                return NotFound();
            }

            ViewBag.DonorId = id;
            return View(donor);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DonorEdit(int id, string name, string email, string phone, string bloodGroup, string location, bool availability, DateTime? lastDonationDate)
        {
            var donor = await _context.DonorProfiles
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DonorId == id);

            if (donor == null)
            {
                return NotFound();
            }

            if (donor.User != null)
            {
                donor.User.Name = name;
                donor.User.Email = email;
                donor.User.Phone = phone;
                donor.User.Status = availability ? "Active" : "Inactive";
            }

            donor.BloodGroup = bloodGroup;
            donor.Location = location;
            donor.Availability = availability;
            if (lastDonationDate.HasValue)
            {
                donor.LastDonationDate = DateTime.SpecifyKind(lastDonationDate.Value, DateTimeKind.Utc);
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Donor updated successfully!";
            return RedirectToAction(nameof(Donors));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DonorDelete(int id)
        {
            var donor = await _context.DonorProfiles
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.DonorId == id);

            if (donor != null)
            {
                if (donor.User != null)
                {
                    _context.Users.Remove(donor.User);
                }
                else
                {
                    _context.DonorProfiles.Remove(donor);
                }
                await _context.SaveChangesAsync();
                TempData["Success"] = "Donor removed successfully!";
            }
            return RedirectToAction(nameof(Donors));
        }

        // --- Recipients / Blood Requests Management ---
        public async Task<IActionResult> Recipients(string? search, string? status, int page = 1)
        {
            var query = _context.BloodRequests
                .Include(r => r.Receiver)
                .Include(r => r.Hospital)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
            {
                query = query.Where(r => r.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(r => 
                    (r.Receiver != null && (r.Receiver.Name.ToLower().Contains(s) || r.Receiver.Email.ToLower().Contains(s) || r.Receiver.Phone.Contains(s))) ||
                    (r.Hospital != null && r.Hospital.Name.ToLower().Contains(s)) ||
                    r.BloodGroup.ToLower().Contains(s));
            }

            const int pageSize = 10;
            int totalCount = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var requests = await query
                .OrderByDescending(r => r.RequestId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientAdd(string fullName, string email, string phone, string bloodType, string hospitalName, string address, DateTime requiredDate, string reason, string patientName, string? diseaseName, string? hemoglobin)
        {
            var compositeRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Donor & Receiver");
            var user = new User
            {
                Name = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = _hasher.HashPassword("receiver@123"),
                RoleId = compositeRole?.RoleId ?? 2,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.Name.ToLower().Contains(hospitalName.ToLower()))
                           ?? await _context.Hospitals.FirstOrDefaultAsync();

            var bloodRequest = new BloodRequest
            {
                ReceiverId = user.UserId,
                BloodGroup = bloodType,
                Quantity = 1,
                HospitalId = hospital?.HospitalId,
                Location = address,
                EmergencyLevel = "Urgent",
                Reason = reason,
                PatientName = string.IsNullOrWhiteSpace(patientName) ? fullName : patientName,
                DiseaseName = diseaseName,
                Hemoglobin = hemoglobin,
                RequestDate = DateTime.UtcNow,
                RequiredDate = DateTime.SpecifyKind(requiredDate, DateTimeKind.Utc),
                Status = "Pending"
            };
            _context.BloodRequests.Add(bloodRequest);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Blood request added successfully!";
            return RedirectToAction(nameof(Recipients));
        }

        public async Task<IActionResult> RecipientEdit(int id)
        {
            var request = await _context.BloodRequests
                .Include(r => r.Receiver)
                .Include(r => r.Hospital)
                .FirstOrDefaultAsync(r => r.RequestId == id);

            if (request == null)
            {
                return NotFound();
            }

            ViewBag.RecipientId = id;
            ViewBag.Hospitals = await _context.Hospitals.ToListAsync();
            return View(request);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientEdit(int id, string bloodGroup, int quantity, int? hospitalId, string emergencyLevel, string status, DateTime? requiredDate)
        {
            var request = await _context.BloodRequests
                .Include(r => r.Receiver)
                .FirstOrDefaultAsync(r => r.RequestId == id);

            if (request == null)
            {
                return NotFound();
            }

            request.BloodGroup = bloodGroup;
            request.Quantity = quantity;
            request.HospitalId = hospitalId;
            request.EmergencyLevel = emergencyLevel;
            request.Status = status;
            if (requiredDate.HasValue)
            {
                request.RequiredDate = DateTime.SpecifyKind(requiredDate.Value, DateTimeKind.Utc);
            }

            // Create notification for receiver
            _context.Notifications.Add(new Notification
            {
                UserId = request.ReceiverId,
                RequestId = request.RequestId,
                Message = $"Your blood request #{request.RequestId} for {request.BloodGroup} is now marked as {request.Status}.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = "Blood request status updated!";
            return RedirectToAction(nameof(Recipients));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientApprove(int id)
        {
            var request = await _context.BloodRequests.FindAsync(id);
            if (request != null)
            {
                request.Status = "Approved";
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Request #{id} has been approved!";
            }
            return RedirectToAction(nameof(Recipients));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientDelete(int id)
        {
            var request = await _context.BloodRequests.FindAsync(id);
            if (request != null)
            {
                _context.BloodRequests.Remove(request);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Blood request removed successfully!";
            }
            return RedirectToAction(nameof(Recipients));
        }

        // --- Blood Bags / Blood Stock Management ---
        public async Task<IActionResult> BloodBags(string? search, string? bloodType, int page = 1)
        {
            ViewBag.TotalBags = await _context.BloodStock.SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.AvailableBags = await _context.BloodStock.Where(s => s.Status == "Available").SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.LowStockBags = await _context.BloodStock.Where(s => s.Status == "Low Stock" || s.Quantity < 10).SumAsync(s => (int?)s.Quantity) ?? 0;
            ViewBag.ExpiredBags = await _context.BloodStock.Where(s => s.Status == "Expired" || (s.ExpiryDate.HasValue && s.ExpiryDate.Value < DateTime.UtcNow)).SumAsync(s => (int?)s.Quantity) ?? 0;

            var query = _context.BloodStock.Include(s => s.Hospital).AsQueryable();

            if (!string.IsNullOrWhiteSpace(bloodType) && bloodType != "All")
            {
                query = query.Where(s => s.BloodGroup == bloodType);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(b => 
                    b.BloodGroup.ToLower().Contains(s) || 
                    (b.Hospital != null && (b.Hospital.Name.ToLower().Contains(s) || b.Hospital.Location.ToLower().Contains(s))));
            }

            const int pageSize = 10;
            int totalCount = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var stocks = await query
                .OrderBy(s => s.BloodGroup)
                .ThenByDescending(s => s.Quantity)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Hospitals = await _context.Hospitals.ToListAsync();
            ViewBag.Search = search;
            ViewBag.BloodType = bloodType;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            return View(stocks);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BloodBagAdd(int hospitalId, string bloodType, int quantity, DateTime expiryDate, string status)
        {
            var stock = new BloodStock
            {
                HospitalId = hospitalId,
                BloodGroup = bloodType,
                Quantity = quantity,
                ExpiryDate = DateTime.SpecifyKind(expiryDate, DateTimeKind.Utc),
                Status = string.IsNullOrWhiteSpace(status) ? (quantity < 10 ? "Low Stock" : "Available") : status,
                LastUpdated = DateTime.UtcNow
            };
            _context.BloodStock.Add(stock);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Blood bag added to inventory successfully!";
            return RedirectToAction(nameof(BloodBags));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BloodBagDelete(int id)
        {
            var stock = await _context.BloodStock.FindAsync(id);
            if (stock != null)
            {
                _context.BloodStock.Remove(stock);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Blood bag inventory entry removed!";
            }
            return RedirectToAction(nameof(BloodBags));
        }

        // --- Users & Role Management ---
        public IActionResult AddUser()
        {
            ViewBag.Roles = _context.Roles.OrderBy(r => r.RoleId).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(string fullName, string email, string phone, string password, string roleName, string? bloodGroup, string status)
        {
            fullName = (fullName ?? "").Trim();
            email = (email ?? "").Trim().ToLower();
            phone = (phone ?? "").Trim();
            password = string.IsNullOrWhiteSpace(password) ? "life@123" : password;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Roles = _context.Roles.OrderBy(r => r.RoleId).ToList();
                ViewBag.Error = "Name and email are required.";
                return View();
            }

            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                ViewBag.Roles = _context.Roles.OrderBy(r => r.RoleId).ToList();
                ViewBag.Error = $"A user with email '{email}' already exists.";
                return View();
            }

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null) role = await _context.Roles.FirstOrDefaultAsync();

            var user = new User
            {
                Name = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = _hasher.HashPassword(password),
                RoleId = role?.RoleId ?? 2,
                Status = string.IsNullOrWhiteSpace(status) ? "Active" : status,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // If they can act as donor and a blood group was given, create a donor profile.
            if (!string.IsNullOrWhiteSpace(bloodGroup) && role?.RoleName is "Donor" or "Donor & Receiver")
            {
                var profile = new DonorProfile
                {
                    UserId = user.UserId,
                    BloodGroup = bloodGroup,
                    Location = "Bangladesh",
                    Availability = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DonorProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = $"User '{fullName}' added successfully. Login password: {password}";
            return RedirectToAction(nameof(Users));
        }

        public async Task<IActionResult> Users(string? search, int page = 1)
        {
            var query = _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u =>
                    u.Name.ToLower().Contains(s) ||
                    u.Email.ToLower().Contains(s) ||
                    u.Phone.Contains(s) ||
                    (u.Role != null && u.Role.RoleName.ToLower().Contains(s)));
            }

            const int pageSize = 10;
            int totalCount = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var users = await query
                .OrderBy(u => u.UserId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Roles = await _context.Roles.OrderBy(r => r.RoleId).ToListAsync();
            ViewBag.Search = search;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserChangeRole(int userId, string roleName, string? bloodGroup)
        {
            var target = await _context.Users
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);

            if (target == null || role == null)
            {
                TempData["Success"] = "User or role not found.";
                return RedirectToAction(nameof(Users));
            }

            // Prevent administrator from removing their own Admin role (would lock them out)
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int currentAdminId);
            if (target.UserId == currentAdminId && role.RoleName != "Admin")
            {
                TempData["Success"] = "You cannot change your own Administrator role.";
                return RedirectToAction(nameof(Users));
            }

            if (target.RoleId == role.RoleId)
            {
                TempData["Success"] = $"{target.Name} is already a {role.RoleName}.";
                return RedirectToAction(nameof(Users));
            }

            // Under-18 users can never be assigned a donor-capable role
            if (role.RoleName is "Donor" or "Donor & Receiver" && !AgeHelper.IsAdult(target.DonorProfile?.DateOfBirth))
            {
                TempData["Success"] = $"{target.Name} is under 18, so the Donor role stays blocked. They can still be a Receiver.";
                return RedirectToAction(nameof(Users));
            }

            target.RoleId = role.RoleId;

            // Role side-effects
            bool donorCapable = role.RoleName is "Donor" or "Donor & Receiver";
            if (donorCapable)
            {
                // Switching TO Donor-capable role: make sure a DonorProfile exists
                if (target.DonorProfile == null)
                {
                    _context.DonorProfiles.Add(new DonorProfile
                    {
                        UserId = target.UserId,
                        BloodGroup = string.IsNullOrWhiteSpace(bloodGroup) ? "O+" : bloodGroup,
                        DateOfBirth = DateTime.UtcNow.AddYears(-25),
                        Location = "Dhaka",
                        Availability = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (target.DonorProfile != null)
            {
                // Switching AWAY from donor capability: remove profile so they no longer appear as a donor
                _context.DonorProfiles.Remove(target.DonorProfile);
            }

            _context.Notifications.Add(new Notification
            {
                UserId = target.UserId,
                Message = $"Your account role has been updated to {role.RoleName} by the administrator.",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"{target.Name}'s role has been changed to {role.RoleName}.";
            return RedirectToAction(nameof(Users));
        }

        // --- Hospitals Management (public registrations + approval workflow) ---
        public async Task<IActionResult> Hospitals(string? status, int page = 1)
        {
            var query = _context.Hospitals.AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
            {
                query = query.Where(h => h.VerificationStatus == status);
            }

            const int pageSize = 10;
            int totalCount = await query.CountAsync();
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var hospitals = await query
                .OrderByDescending(h => h.HospitalId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var hospitalUsers = await _context.Users
                .Where(u => u.Role != null && u.Role.RoleName == "Hospital")
                .ToListAsync();

            var hospitalEmails = new Dictionary<int, string>();
            foreach (var h in hospitals)
            {
                var email = hospitalUsers
                                .Where(u => u.Name == h.Name && u.Phone == h.Contact)
                                .Select(u => u.Email)
                                .FirstOrDefault()
                            ?? hospitalUsers
                                .Where(u => u.Name == h.Name)
                                .Select(u => u.Email)
                                .FirstOrDefault();
                hospitalEmails[h.HospitalId] = email ?? "";
            }
            ViewBag.HospitalEmails = hospitalEmails;

            ViewBag.Status = status;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PendingCount = await _context.Hospitals.CountAsync(h => h.VerificationStatus == "Pending");
            ViewBag.VerifiedCount = await _context.Hospitals.CountAsync(h => h.VerificationStatus == "Verified");
            ViewBag.RejectedCount = await _context.Hospitals.CountAsync(h => h.VerificationStatus == "Rejected");
            return View(hospitals);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HospitalApprove(int id)
        {
            var hospital = await _context.Hospitals.FindAsync(id);
            if (hospital != null)
            {
                hospital.VerificationStatus = "Verified";
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Hospital &quot;{hospital.Name}&quot; has been verified and activated.";
            }
            return RedirectToAction(nameof(Hospitals));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HospitalReject(int id)
        {
            var hospital = await _context.Hospitals.FindAsync(id);
            if (hospital != null)
            {
                hospital.VerificationStatus = "Rejected";
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Hospital &quot;{hospital.Name}&quot; registration has been rejected.";
            }
            return RedirectToAction(nameof(Hospitals));
        }

        [HttpGet]
        public async Task<IActionResult> ExportDonorsCsv()
        {
            var donors = await _context.DonorProfiles
                .Include(d => d.User)
                .OrderBy(d => d.DonorId)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Donor ID,Full Name,Email,Phone,Blood Group,Location,Availability,Last Donation Date,Registration Date");

            foreach (var d in donors)
            {
                var id = d.DonorId;
                var name = $"\"{d.User?.Name ?? "N/A"}\"";
                var email = d.User?.Email ?? "N/A";
                var phone = d.User?.Phone ?? "N/A";
                var bg = d.BloodGroup;
                var loc = $"\"{d.Location.Replace("\"", "\"\"")}\"";
                var avail = d.Availability ? "Active" : "Inactive";
                var lastDon = d.LastDonationDate.HasValue ? d.LastDonationDate.Value.ToString("yyyy-MM-dd") : "Never";
                var reg = d.CreatedAt.ToString("yyyy-MM-dd");

                sb.AppendLine($"{id},{name},{email},{phone},{bg},{loc},{avail},{lastDon},{reg}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_All_Donors_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportBloodStockCsv()
        {
            var stocks = await _context.BloodStock
                .Include(s => s.Hospital)
                .OrderBy(s => s.Hospital != null ? s.Hospital.Name : "")
                .ThenBy(s => s.BloodGroup)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Hospital Name,Blood Group,Quantity (Units),Status,Expiry Date,Last Updated");

            foreach (var s in stocks)
            {
                var hospName = $"\"{s.Hospital?.Name ?? "General Center"}\"";
                var bg = s.BloodGroup;
                var qty = s.Quantity;
                var st = s.Status;
                var exp = s.ExpiryDate.HasValue ? s.ExpiryDate.Value.ToString("yyyy-MM-dd") : "N/A";
                var upd = s.LastUpdated.ToString("yyyy-MM-dd HH:mm");

                sb.AppendLine($"{hospName},{bg},{qty},{st},{exp},{upd}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Master_BloodStock_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public IActionResult Reports()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ExportRequestsCsv()
        {
            var requests = await _context.BloodRequests
                .Include(r => r.Receiver)
                .OrderBy(r => r.RequestId)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Request ID,Patient Name,Receiver,Phone,Blood Group,Quantity,Location,Emergency,Required Date,Status,Disease,Hemoglobin,Reason,Created");

            foreach (var r in requests)
            {
                var patient = $"\"{(string.IsNullOrWhiteSpace(r.PatientName) ? r.Receiver?.Name ?? "N/A" : r.PatientName).Replace("\"", "\"\"")}\"";
                var receiver = $"\"{r.Receiver?.Name ?? "N/A"}\"";
                var phone = $"\"{r.Receiver?.Phone ?? "N/A"}\"";
                var loc = $"\"{r.Location.Replace("\"", "\"\"")}\"";
                var reqDate = r.RequiredDate.HasValue ? r.RequiredDate.Value.ToString("yyyy-MM-dd") : "N/A";
                var disease = $"\"{(r.DiseaseName ?? "N/A").Replace("\"", "\"\"")}\"";
                var hb = r.Hemoglobin ?? "N/A";

                sb.AppendLine($"{r.RequestId},{patient},{receiver},{phone},{r.BloodGroup},{r.Quantity},{loc},{r.EmergencyLevel},{reqDate},{r.Status},{disease},{hb},\"{r.Reason.Replace("\"", "\"\"")}\",{r.RequestDate:yyyy-MM-dd}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Blood_Requests_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportDonationsCsv()
        {
            var donations = await _context.DonationHistory
                .Include(d => d.Donor!)
                    .ThenInclude(dp => dp.User)
                .Include(d => d.Hospital)
                .OrderBy(d => d.DonationDate)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Donation ID,Donor,Blood Group,Hospital,Quantity,Donation Date,Status");

            foreach (var d in donations)
            {
                var donor = $"\"{d.Donor?.User?.Name ?? "N/A"}\"";
                var hosp = $"\"{d.Hospital?.Name ?? "N/A"}\"";
                sb.AppendLine($"{d.DonationId},{donor},{d.Donor?.BloodGroup ?? "N/A"},{hosp},{d.BloodRequest?.Quantity ?? 1},{d.DonationDate:yyyy-MM-dd},{d.Status}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Donation_History_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportHospitalsCsv()
        {
            var hospitals = await _context.Hospitals.OrderBy(h => h.Name).ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Hospital ID,Name,Address,Location,Contact,Verification Status,Created");

            foreach (var h in hospitals)
            {
                sb.AppendLine($"{h.HospitalId},\"{h.Name.Replace("\"", "\"\"")}\",\"{h.Address.Replace("\"", "\"\"")}\",\"{h.Location.Replace("\"", "\"\"")}\",\"{h.Contact.Replace("\"", "\"\"")}\",{h.VerificationStatus},{h.CreatedAt:yyyy-MM-dd}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Hospitals_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportUsersCsv()
        {
            var users = await _context.Users
                .Include(u => u.Role)
                .OrderBy(u => u.UserId)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("User ID,Full Name,Email,Phone,Status,Role,Registered");

            foreach (var u in users)
            {
                sb.AppendLine($"{u.UserId},\"{u.Name.Replace("\"", "\"\"")}\",{u.Email},\"{u.Phone ?? "N/A"}\",{u.Status},\"{u.Role?.RoleName ?? "N/A"}\",{u.CreatedAt:yyyy-MM-dd}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Users_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportRecipientsCsv()
        {
            var requests = await _context.BloodRequests
                .Include(r => r.Receiver)
                .OrderBy(r => r.RequestDate)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Request ID,Patient Name,Requester,Phone,Blood Group,Quantity,Emergency,Status,Request Date,Required Date");

            foreach (var r in requests)
            {
                var patient = $"\"{(string.IsNullOrWhiteSpace(r.PatientName) ? r.Receiver?.Name ?? "N/A" : r.PatientName).Replace("\"", "\"\"")}\"";
                var requester = $"\"{r.Receiver?.Name ?? "N/A"}\"";
                var phone = $"\"{r.Receiver?.Phone ?? "N/A"}\"";
                var reqDate = r.RequiredDate.HasValue ? r.RequiredDate.Value.ToString("yyyy-MM-dd") : "N/A";

                sb.AppendLine($"{r.RequestId},{patient},{requester},{phone},{r.BloodGroup},{r.Quantity},{r.EmergencyLevel},{r.Status},{r.RequestDate:yyyy-MM-dd},{reqDate}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Recipients_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportRatingsCsv()
        {
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Include(r => r.TargetUser)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Rating ID,Rated By,Target User,Rating,Comment,Date");

            foreach (var r in ratings)
            {
                sb.AppendLine($"{r.RatingId},\"{r.User?.Name ?? "N/A"}\",\"{r.TargetUser?.Name ?? "N/A"}\",{r.RatingValue}/5,\"{(r.Comment ?? "N/A").Replace("\"", "\"\"")}\",{r.CreatedAt:yyyy-MM-dd}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"LifeLink_Ratings_{DateTime.UtcNow:yyyyMMdd}.csv");
        }
    }
}
