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

            var response = await _chatbotService.ProcessQueryAsync(request.Message);
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
