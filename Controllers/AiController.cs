using LifeLink.Models.ViewModels;
using System.Security.Claims;
using LifeLink.Models;
using LifeLink.Services;
using Microsoft.AspNetCore.Mvc;

namespace LifeLink.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    [Route("api/[controller]/[action]")]
    public class AiController : Controller
    {
        private readonly IAiMatchingService _matchingService;
        private readonly IAiForecastingService _forecastingService;
        private readonly IAiChatbotService _chatbotService;

        public AiController(
            IAiMatchingService matchingService,
            IAiForecastingService forecastingService,
            IAiChatbotService chatbotService)
        {
            _matchingService = matchingService;
            _forecastingService = forecastingService;
            _chatbotService = chatbotService;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatbotMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return Json(new ChatbotMessageResponse
                {
                    Answer = "Please ask me a question about LifeLink, blood stocks, or donation!",
                    QuickReplies = new() { "Check blood stock", "How to register?", "Eligibility rules" }
                });
            }

            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int currentUserId);
            var userContext = new ChatUserContext(
                currentUserId > 0 ? currentUserId : null,
                User.FindFirstValue(ClaimTypes.Role),
                User.Identity?.Name,
                User.Identity?.IsAuthenticated == true);

            var response = await _chatbotService.ProcessQueryAsync(request.Message, userContext);
            return Json(response);
        }

        [HttpGet]
        public async Task<IActionResult> MatchDonors(string bloodGroup, string location, string urgency = "Urgent")
        {
            if (string.IsNullOrWhiteSpace(bloodGroup))
            {
                return BadRequest("Blood group is required.");
            }

            var rankedDonors = await _matchingService.RankDonorsAsync(bloodGroup, location, urgency);
            return Json(rankedDonors.Select(d => new
            {
                donorId = d.DonorProfile.DonorId,
                name = d.User.Name,
                phone = d.User.Phone,
                bloodGroup = d.DonorProfile.BloodGroup,
                location = d.DonorProfile.Location,
                distanceKm = d.DistanceKm,
                matchScore = d.MatchScore,
                responseProbability = d.ResponseProbability,
                badge = d.RecommendationBadge,
                badgeClass = d.BadgeClass,
                isEligible = d.IsMedicallyEligible
            }));
        }

        [HttpGet]
        public async Task<IActionResult> Forecast(int? hospitalId)
        {
            var forecasts = hospitalId.HasValue
                ? await _forecastingService.GetHospitalDemandForecastAsync(hospitalId.Value)
                : await _forecastingService.GetSystemWideDemandForecastAsync();

            return Json(forecasts);
        }
    }
}
