using LifeLink.Models;

namespace LifeLink.Models
{
    public class RankedDonorResult
    {
        public DonorProfile DonorProfile { get; set; } = null!;
        public User User => DonorProfile.User!;
        public float DistanceKm { get; set; }
        public int MatchScore { get; set; } // 0 - 100%
        public int ResponseProbability { get; set; } // 0 - 100%
        public string RecommendationBadge { get; set; } = string.Empty; // "Top Match (AI)", "Recommended", "Available"
        public string BadgeClass { get; set; } = "bg-success";
        public bool IsMedicallyEligible { get; set; }
        public string DistanceDisplay => $"{DistanceKm:F1} km away";
    }

    public class DemandForecastResult
    {
        public string BloodGroup { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public int ForecastedDemand { get; set; }
        public string DemandLevel { get; set; } = "Medium"; // "High", "Medium", "Low"
        public string ShortageRisk { get; set; } = "NORMAL"; // "CRITICAL", "HIGH", "MODERATE", "NORMAL"
        public string RiskBadgeClass => ShortageRisk switch
        {
            "CRITICAL" => "bg-danger",
            "HIGH" => "bg-warning text-dark",
            "MODERATE" => "bg-info text-dark",
            _ => "bg-success"
        };
        public string Recommendation { get; set; } = string.Empty;
    }

    public class ChatbotMessageRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public class ChatbotMessageResponse
    {
        public string Answer { get; set; } = string.Empty;
        public List<string> QuickReplies { get; set; } = new();
        public bool IsEmergency { get; set; }
        public string? ActionUrl { get; set; }
    }
}
