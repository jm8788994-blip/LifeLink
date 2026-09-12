using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
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
        public async Task<IActionResult> Donors(string? search, string? bloodType)
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

            var donors = await query.OrderByDescending(d => d.DonorId).ToListAsync();
            ViewBag.Search = search;
            ViewBag.BloodType = bloodType;
            return View(donors);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DonorAdd(string fullName, string email, string phone, string bloodType, string address, DateTime dateOfBirth)
        {
            var donorRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Donor");
            var user = new User
            {
                Name = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = "donor@123",
                RoleId = donorRole?.RoleId ?? 2,
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
        public async Task<IActionResult> Recipients(string? search, string? status)
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

            var requests = await query.OrderByDescending(r => r.RequestId).ToListAsync();
            ViewBag.Search = search;
            ViewBag.Status = status;
            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecipientAdd(string fullName, string email, string phone, string bloodType, string hospitalName, string address, DateTime requiredDate)
        {
            var receiverRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Receiver");
            var user = new User
            {
                Name = fullName,
                Email = email,
                Phone = phone,
                PasswordHash = "receiver@123",
                RoleId = receiverRole?.RoleId ?? 3,
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
        public async Task<IActionResult> BloodBags(string? search, string? bloodType)
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

            var stocks = await query.OrderBy(s => s.BloodGroup).ThenByDescending(s => s.Quantity).ToListAsync();
            ViewBag.Hospitals = await _context.Hospitals.ToListAsync();
            ViewBag.Search = search;
            ViewBag.BloodType = bloodType;
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
    }
}
