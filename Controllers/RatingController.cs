using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]/[action]")]
    [Route("api/[controller]/[action]")]
    public class RatingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RatingController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class RatingSubmissionRequest
        {
            public int TargetUserId { get; set; }
            public int RatingValue { get; set; }
            public string? Comment { get; set; }
            public int? RequestId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] RatingSubmissionRequest request)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int currentUserId))
            {
                return Unauthorized();
            }

            if (request == null || request.TargetUserId <= 0)
            {
                return BadRequest("Invalid target user.");
            }

            int stars = Math.Clamp(request.RatingValue, 1, 5);

            var rating = new Rating
            {
                UserId = currentUserId,
                TargetUserId = request.TargetUserId,
                RatingValue = stars,
                Comment = request.Comment?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Ratings.Add(rating);

            // Notify the rated user
            _context.Notifications.Add(new Notification
            {
                UserId = request.TargetUserId,
                RequestId = request.RequestId,
                Message = $"You received a {stars}-star rating: \"{request.Comment ?? "Thank you for your generous help!"}\"",
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            });

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Thank you for your feedback! Rating recorded." });
        }

        [HttpGet]
        public async Task<IActionResult> GetUserRatings(int userId)
        {
            var ratings = await _context.Ratings
                .Include(r => r.User)
                .Where(r => r.TargetUserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .Select(r => new
                {
                    reviewerName = r.User!.Name,
                    ratingValue = r.RatingValue,
                    comment = r.Comment,
                    date = r.CreatedAt.ToString("MMM dd, yyyy")
                })
                .ToListAsync();

            double avgRating = ratings.Any() ? Math.Round(ratings.Average(r => r.ratingValue), 1) : 5.0;

            return Json(new
            {
                averageRating = avgRating,
                totalReviews = ratings.Count,
                reviews = ratings
            });
        }
    }
}
