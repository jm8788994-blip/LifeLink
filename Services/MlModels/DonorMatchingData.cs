using Microsoft.ML.Data;

namespace LifeLink.Services.MlModels
{
    public class DonorFeatureInput
    {
        public float BloodCompatibility { get; set; } // 1.0 = exact/universal, 0.7 = partial, 0.0 = incompatible
        public float DistanceKm { get; set; } // Distance in kilometers
        public float Availability { get; set; } // 1.0 = active, 0.0 = inactive
        public float Eligibility { get; set; } // 1.0 = >=90 days, 0.2 = <90 days
        public float UrgencyWeight { get; set; } // 1.0 = Critical, 0.75 = Urgent, 0.5 = Normal
        public float HistoricalDonations { get; set; } // Total count of donations
        public float ResponseReliability { get; set; } // 0.0 - 1.0 based on response track record

        public float Label { get; set; } // Match Score (0 - 100)
    }

    public class DonorMatchPrediction
    {
        [ColumnName("Score")]
        public float MatchScore { get; set; }
    }

    public class DonorResponseInput
    {
        public float MatchScore { get; set; }
        public float DistanceKm { get; set; }
        public float UrgencyWeight { get; set; }
        public float HistoricalDonations { get; set; }

        public float Label { get; set; } // Response Probability (0 - 100)
    }

    public class DonorResponsePrediction
    {
        [ColumnName("Score")]
        public float ResponseProbability { get; set; }
    }

    public class BloodDemandInput
    {
        [LoadColumn(0)]
        public string BloodGroup { get; set; } = string.Empty;

        [LoadColumn(1)]
        public float Month { get; set; }

        [LoadColumn(2)]
        public float HistoricalRequests { get; set; }

        [LoadColumn(3)]
        public float Label { get; set; } // Expected demand in units
    }

    public class BloodDemandPrediction
    {
        [ColumnName("Score")]
        public float PredictedUnits { get; set; }
    }
}
