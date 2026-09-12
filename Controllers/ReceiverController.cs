using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize(Roles = "Receiver")]
    public class ReceiverController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Services.IAiMatchingService _aiMatchingService;
        private readonly Services.INotificationService _notificationService;

        public ReceiverController(
            ApplicationDbContext context, 
            Services.IAiMatchingService aiMatchingService,
            Services.INotificationService notificationService)
        {
            _context = context;
            _aiMatchingService = aiMatchingService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Dashboard()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users
                .Include(u => u.BloodRequests)
                .ThenInclude(r => r.Hospital)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound();
            }

            // Get all hospitals for the "New Request" modal
            ViewBag.Hospitals = await _context.Hospitals.OrderBy(h => h.Name).ToListAsync();

            // Find ML.NET Smart Ranked Donors for the user's latest request
            var latestRequest = user.BloodRequests.OrderByDescending(r => r.RequestDate).FirstOrDefault();
            string searchGroup = latestRequest?.BloodGroup ?? "O+";
            string searchLoc = latestRequest?.Location ?? "Dhaka";
            string urgency = latestRequest?.EmergencyLevel ?? "Urgent";

            var rankedDonors = await _aiMatchingService.RankDonorsAsync(searchGroup, searchLoc, urgency, maxCount: 6);
            ViewBag.RankedDonors = rankedDonors;
            ViewBag.SearchBloodGroup = searchGroup;

            // Notifications
            ViewBag.Notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRequest(string bloodGroup, int quantity, int hospitalId, string location, string emergencyLevel, DateTime requiredDate)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var req = new BloodRequest
            {
                ReceiverId = userId,
                BloodGroup = bloodGroup,
                Quantity = quantity > 0 ? quantity : 1,
                HospitalId = hospitalId,
                Location = location,
                EmergencyLevel = string.IsNullOrWhiteSpace(emergencyLevel) ? "Urgent" : emergencyLevel,
                RequestDate = DateTime.UtcNow,
                RequiredDate = DateTime.SpecifyKind(requiredDate, DateTimeKind.Utc),
                Status = "Pending"
            };

            _context.BloodRequests.Add(req);
            await _context.SaveChangesAsync();

            var hospital = await _context.Hospitals.FindAsync(hospitalId);
            string hospitalName = hospital?.Name ?? "General Hospital";

            // SignalR: Broadcast emergency request in real time to all compatible donors!
            await _notificationService.BroadcastEmergencyRequestAsync(req.BloodGroup, hospitalName, req.Location, req.EmergencyLevel, req.RequestId);

            TempData["Success"] = "Blood request submitted successfully! Real-time emergency alert broadcasted to compatible donors.";
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRequest(int id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var req = await _context.BloodRequests.FirstOrDefaultAsync(r => r.RequestId == id && r.ReceiverId == userId);
            if (req != null)
            {
                req.Status = "Cancelled";
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Blood request #{id} has been cancelled.";
            }

            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRequest(int id, int? donorId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var req = await _context.BloodRequests
                .Include(r => r.Hospital)
                .FirstOrDefaultAsync(r => r.RequestId == id && r.ReceiverId == userId);

            if (req != null)
            {
                req.Status = "Completed";

                // Resolve donor profile
                DonorProfile? donorProfile = null;
                if (donorId.HasValue)
                {
                    donorProfile = await _context.DonorProfiles
                        .Include(d => d.User)
                        .FirstOrDefaultAsync(d => d.DonorId == donorId.Value || d.UserId == donorId.Value);
                }

                if (donorProfile == null)
                {
                    // Match first compatible donor if none passed explicitly
                    donorProfile = await _context.DonorProfiles
                        .Include(d => d.User)
                        .FirstOrDefaultAsync(d => d.BloodGroup == req.BloodGroup);
                }

                if (donorProfile != null)
                {
                    // 1. Automatically record in DonationHistory
                    var donationRecord = new DonationHistory
                    {
                        DonorId = donorProfile.DonorId,
                        HospitalId = req.HospitalId,
                        RequestId = req.RequestId,
                        DonationDate = DateTime.UtcNow,
                        Status = "Completed"
                    };
                    _context.DonationHistory.Add(donationRecord);

                    // 2. Automatically update donor's LastDonationDate (resets 90-day medical interval!)
                    donorProfile.LastDonationDate = DateTime.UtcNow;

                    // 3. Send real-time congratulations and thank-you notification to donor
                    await _notificationService.SendNotificationToUserAsync(
                        donorProfile.UserId,
                        "Donation Confirmed!",
                        $"Recipient has marked request #{req.RequestId} as Completed. Thank you for your heroic donation! Your 90-day recovery interval is now started.",
                        "success",
                        "/Donor/Dashboard");

                    // 4. Prompt recipient to rate the donor
                    TempData["RateDonorId"] = donorProfile.UserId;
                    TempData["RateDonorName"] = donorProfile.User?.Name ?? "Blood Donor";
                    TempData["RateRequestId"] = req.RequestId;
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Blood request #{id} successfully marked as Completed! Your life-saving feedback is appreciated.";
            }

            return RedirectToAction(nameof(Dashboard));
        }
    }
}
