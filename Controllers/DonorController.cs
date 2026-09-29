using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize(Roles = "Donor,Donor & Receiver")]
    public class DonorController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Services.INotificationService _notificationService;

        public DonorController(ApplicationDbContext context, Services.INotificationService notificationService)
        {
            _context = context;
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
                .Include(u => u.DonorProfile)
                .ThenInclude(dp => dp!.Donations)
                .ThenInclude(dh => dh.Hospital)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null || user.DonorProfile == null)
            {
                return NotFound("Donor profile not found.");
            }

            var profile = user.DonorProfile;

            // 1. Calculate 90-Day Medical Eligibility
            bool isEligible = true;
            int daysRemaining = 0;
            DateTime? nextEligibleDate = null;

            if (profile.LastDonationDate.HasValue)
            {
                var daysSinceDonation = (DateTime.UtcNow - profile.LastDonationDate.Value).TotalDays;
                if (daysSinceDonation < 90)
                {
                    isEligible = false;
                    daysRemaining = (int)Math.Ceiling(90 - daysSinceDonation);
                    nextEligibleDate = profile.LastDonationDate.Value.AddDays(90);
                }
            }

            ViewBag.IsEligible = isEligible;
            ViewBag.DaysRemaining = daysRemaining;
            ViewBag.NextEligibleDate = nextEligibleDate;

            // Donor criteria gate: must be 18+, have a blood group set, and be available
            bool isOfAge = AgeHelper.IsAdult(profile.DateOfBirth);
            bool hasBloodGroup = !string.IsNullOrWhiteSpace(profile.BloodGroup);
            ViewBag.IsOfAge = isOfAge;
            ViewBag.HasBloodGroup = hasBloodGroup;
            ViewBag.DonorGateOk = isOfAge && hasBloodGroup && profile.Availability;

            // 2. Compatible Blood Groups for this donor
            var compatibleNeedGroups = GetCompatibleRecipientGroups(profile.BloodGroup);

            // 3. Find Emergency & Active Blood Requests matching donor's blood compatibility
            var matchingRequests = await _context.BloodRequests
                .Include(r => r.Receiver)
                .Include(r => r.Hospital)
                .Where(r => r.Status == "Pending" && compatibleNeedGroups.Contains(r.BloodGroup))
                .OrderByDescending(r => r.EmergencyLevel == "Critical")
                .ThenByDescending(r => r.RequestDate)
                .Take(10)
                .ToListAsync();

            ViewBag.MatchingRequests = matchingRequests;

            // 4. Donor Notifications
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync();

            ViewBag.Notifications = notifications;

            // 5. Donor Ratings & Feedback (PDF 4.19)
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Where(r => r.TargetUserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.AvgRating = ratings.Any() ? Math.Round(ratings.Average(r => r.RatingValue), 1) : 5.0;
            ViewBag.TotalReviews = ratings.Count;
            ViewBag.RecentReviews = ratings.Take(5).ToList();

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAvailability()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var profile = await _context.DonorProfiles.FirstOrDefaultAsync(dp => dp.UserId == userId);
            if (profile != null)
            {
                if (profile.Availability == false && !AgeHelper.IsAdult(profile.DateOfBirth))
                {
                    TempData["Error"] = "You must be 18 years or older to become an active donor.";
                    return RedirectToAction(nameof(Dashboard));
                }

                if (profile.Availability == false && string.IsNullOrWhiteSpace(profile.BloodGroup))
                {
                    TempData["Error"] = "Set your blood group before becoming an active donor.";
                    return RedirectToAction(nameof(Dashboard));
                }

                profile.Availability = !profile.Availability;
                await _context.SaveChangesAsync();
                TempData["Success"] = profile.Availability 
                    ? "Your status is now ACTIVE. You can receive blood donation requests."
                    : "Your status is now INACTIVE. You will not receive emergency requests.";
            }

            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptRequest(int requestId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId)) return Unauthorized();

            var request = await _context.BloodRequests
                .Include(r => r.Receiver)
                .FirstOrDefaultAsync(r => r.RequestId == requestId);

            var donor = await _context.Users
                .Include(u => u.DonorProfile)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (request != null && donor != null)
            {
                // Donor eligibility gate: 18+ required to commit a donation
                if (!AgeHelper.IsAdult(donor.DonorProfile?.DateOfBirth))
                {
                    TempData["Error"] = "You must be 18 years or older to accept a blood donation request.";
                    return RedirectToAction(nameof(Dashboard));
                }

                // Update request status
                request.Status = "Donor Accepted";
                await _context.SaveChangesAsync();

                // Push real-time notification to the patient!
                await _notificationService.SendNotificationToUserAsync(
                    request.ReceiverId, 
                    "Donor Accepted Your Request!", 
                    $"Donor {donor.Name} ({donor.DonorProfile?.BloodGroup}) has accepted your blood request. You can now chat or call them.", 
                    "success", 
                    "/Receiver/Dashboard");

                TempData["Success"] = "Thank you! You have accepted this request. The patient has received a real-time alert.";
            }

            return RedirectToAction(nameof(Dashboard));
        }

        [HttpGet]
        [Authorize(Roles = "Donor,Admin,Hospital")]
        public async Task<IActionResult> Certificate(int donationId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var donation = await _context.DonationHistory
                .Include(d => d.Hospital)
                .Include(d => d.Donor)
                    .ThenInclude(dp => dp!.User)
                .FirstOrDefaultAsync(d => d.DonationId == donationId);

            if (donation == null)
            {
                return NotFound("Donation record not found.");
            }

            // Verify authorization: current donor, admin, or hospital
            if (donation.Donor?.UserId != userId && !User.IsInRole("Admin") && !User.IsInRole("Hospital"))
            {
                return Forbid();
            }

            return View(donation);
        }

        // Helper: Blood Compatibility Matrix (Donor can donate to...)
        private static List<string> GetCompatibleRecipientGroups(string donorGroup)
        {
            return donorGroup switch
            {
                "O-" => new List<string> { "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+" }, // Universal Donor
                "O+" => new List<string> { "O+", "A+", "B+", "AB+" },
                "A-" => new List<string> { "A-", "A+", "AB-", "AB+" },
                "A+" => new List<string> { "A+", "AB+" },
                "B-" => new List<string> { "B-", "B+", "AB-", "AB+" },
                "B+" => new List<string> { "B+", "AB+" },
                "AB-" => new List<string> { "AB-", "AB+" },
                "AB+" => new List<string> { "AB+" },
                _ => new List<string> { donorGroup }
            };
        }
    }
}
