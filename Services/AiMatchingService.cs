using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Services.MlModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;

namespace LifeLink.Services
{
    public class AiMatchingService : IAiMatchingService
    {
        private readonly ApplicationDbContext _context;
        private readonly MLContext _mlContext;
        private readonly PredictionEngine<DonorFeatureInput, DonorMatchPrediction> _matchEngine;
        private readonly PredictionEngine<DonorResponseInput, DonorResponsePrediction> _responseEngine;

        public AiMatchingService(ApplicationDbContext context)
        {
            _context = context;
            _mlContext = new MLContext(seed: 42);

            // 1. Train ML.NET Smart Donor Matching Model
            _matchEngine = TrainDonorMatchingModel();

            // 2. Train ML.NET Donor Response Prediction Model
            _responseEngine = TrainDonorResponseModel();
        }

        private PredictionEngine<DonorFeatureInput, DonorMatchPrediction> TrainDonorMatchingModel()
        {
            var trainingData = GenerateDonorMatchingTrainingSamples();
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(DonorFeatureInput.BloodCompatibility),
                    nameof(DonorFeatureInput.DistanceKm),
                    nameof(DonorFeatureInput.Availability),
                    nameof(DonorFeatureInput.Eligibility),
                    nameof(DonorFeatureInput.UrgencyWeight),
                    nameof(DonorFeatureInput.HistoricalDonations),
                    nameof(DonorFeatureInput.ResponseReliability))
                .Append(_mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", featureColumnName: "Features", maximumNumberOfIterations: 100));

            var model = pipeline.Fit(dataView);
            return _mlContext.Model.CreatePredictionEngine<DonorFeatureInput, DonorMatchPrediction>(model);
        }

        private PredictionEngine<DonorResponseInput, DonorResponsePrediction> TrainDonorResponseModel()
        {
            var trainingData = GenerateDonorResponseTrainingSamples();
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(DonorResponseInput.MatchScore),
                    nameof(DonorResponseInput.DistanceKm),
                    nameof(DonorResponseInput.UrgencyWeight),
                    nameof(DonorResponseInput.HistoricalDonations))
                .Append(_mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", featureColumnName: "Features", maximumNumberOfIterations: 80));

            var model = pipeline.Fit(dataView);
            return _mlContext.Model.CreatePredictionEngine<DonorResponseInput, DonorResponsePrediction>(model);
        }

        public async Task<List<RankedDonorResult>> RankDonorsAsync(string neededBloodGroup, string recipientLocation, string urgencyLevel, int maxCount = 10)
        {
            // Fetch all donors with user profile and donation history
            var donors = await _context.DonorProfiles
                .Include(d => d.User)
                .Include(d => d.Donations)
                .ToListAsync();

            float urgencyWeight = urgencyLevel switch
            {
                "Critical" => 1.0f,
                "Urgent" => 0.8f,
                _ => 0.5f
            };

            var rankedResults = new List<RankedDonorResult>();

            foreach (var donor in donors)
            {
                if (donor.User == null) continue;

                // 1. Compatibility check
                bool compatible = IsBloodCompatible(donor.BloodGroup, neededBloodGroup);
                if (!compatible) continue; // Incompatible blood groups cannot be matched

                float compScore = (donor.BloodGroup == neededBloodGroup) ? 1.0f : 0.85f;

                // 2. Distance calculation
                float distance = CalculateDistance(recipientLocation, donor.Location);

                // 3. Availability
                float availability = donor.Availability ? 1.0f : 0.0f;

                // 4. Medical Eligibility (90-day rule)
                bool isMedicallyEligible = true;
                float eligibilityScore = 1.0f;
                if (donor.LastDonationDate.HasValue)
                {
                    double daysSince = (DateTime.UtcNow - donor.LastDonationDate.Value).TotalDays;
                    if (daysSince < 90)
                    {
                        isMedicallyEligible = false;
                        eligibilityScore = (float)Math.Clamp(daysSince / 90.0, 0.1, 0.7);
                    }
                }

                // 5. Historical reliability
                int donationsCount = donor.Donations?.Count ?? 0;
                float reliability = Math.Min(1.0f, 0.5f + (donationsCount * 0.1f));

                // Predict with ML.NET Smart Donor Matching Model
                var matchInput = new DonorFeatureInput
                {
                    BloodCompatibility = compScore,
                    DistanceKm = distance,
                    Availability = availability,
                    Eligibility = eligibilityScore,
                    UrgencyWeight = urgencyWeight,
                    HistoricalDonations = donationsCount,
                    ResponseReliability = reliability
                };

                var matchPred = _matchEngine.Predict(matchInput);
                int finalScore = (int)Math.Clamp(Math.Round(matchPred.MatchScore), 15, 99);

                // Predict with ML.NET Donor Response Model
                var responseInput = new DonorResponseInput
                {
                    MatchScore = finalScore,
                    DistanceKm = distance,
                    UrgencyWeight = urgencyWeight,
                    HistoricalDonations = donationsCount
                };

                var responsePred = _responseEngine.Predict(responseInput);
                int responseProb = (int)Math.Clamp(Math.Round(responsePred.ResponseProbability), 20, 98);

                // Assign Badges
                string badge = finalScore >= 90 ? "Top AI Match" : (finalScore >= 75 ? "Highly Recommended" : "Compatible Donor");
                string badgeClass = finalScore >= 90 ? "bg-danger text-white" : (finalScore >= 75 ? "bg-success text-white" : "bg-secondary text-white");

                rankedResults.Add(new RankedDonorResult
                {
                    DonorProfile = donor,
                    DistanceKm = distance,
                    MatchScore = finalScore,
                    ResponseProbability = responseProb,
                    RecommendationBadge = badge,
                    BadgeClass = badgeClass,
                    IsMedicallyEligible = isMedicallyEligible
                });
            }

            // Rank donors from highest match score to lowest
            return rankedResults.OrderByDescending(r => r.MatchScore)
                                .ThenByDescending(r => r.ResponseProbability)
                                .Take(maxCount)
                                .ToList();
        }

        public bool IsBloodCompatible(string donorGroup, string recipientGroup)
        {
            donorGroup = donorGroup.Trim().ToUpperInvariant();
            recipientGroup = recipientGroup.Trim().ToUpperInvariant();

            return recipientGroup switch
            {
                "AB+" => true, // AB+ is Universal Recipient
                "AB-" => donorGroup is "AB-" or "A-" or "B-" or "O-",
                "A+" => donorGroup is "A+" or "A-" or "O+" or "O-",
                "A-" => donorGroup is "A-" or "O-",
                "B+" => donorGroup is "B+" or "B-" or "O+" or "O-",
                "B-" => donorGroup is "B-" or "O-",
                "O+" => donorGroup is "O+" or "O-",
                "O-" => donorGroup is "O-", // O- can only receive O-
                _ => donorGroup == recipientGroup
            };
        }

        public float CalculateDistance(string loc1, string loc2)
        {
            loc1 = (loc1 ?? "").Trim().ToLowerInvariant();
            loc2 = (loc2 ?? "").Trim().ToLowerInvariant();

            if (loc1 == loc2 || loc1.Contains(loc2) || loc2.Contains(loc1))
            {
                // In the same city/area: approximate 2 - 5 km
                return 3.2f;
            }

            // Heuristic Distance matrix between major Bangladesh division centers
            var distances = new Dictionary<string, float>
            {
                { "dhaka-chittagong", 245f }, { "chittagong-dhaka", 245f },
                { "dhaka-sylhet", 235f }, { "sylhet-dhaka", 235f },
                { "dhaka-rajshahi", 245f }, { "rajshahi-dhaka", 245f },
                { "dhaka-khulna", 220f }, { "khulna-dhaka", 220f },
                { "chittagong-sylhet", 360f }, { "sylhet-chittagong", 360f },
                { "rajshahi-khulna", 215f }, { "khulna-rajshahi", 215f }
            };

            foreach (var kvp in distances)
            {
                var parts = kvp.Key.Split('-');
                if ((loc1.Contains(parts[0]) && loc2.Contains(parts[1])) || (loc1.Contains(parts[1]) && loc2.Contains(parts[0])))
                {
                    return kvp.Value;
                }
            }

            return 15.0f; // Default intra-district approximate distance
        }

        // --- Realistic Training Datasets for ML.NET ---
        private static List<DonorFeatureInput> GenerateDonorMatchingTrainingSamples()
        {
            return new List<DonorFeatureInput>
            {
                new() { BloodCompatibility = 1.0f, DistanceKm = 2f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 1f, HistoricalDonations = 5f, ResponseReliability = 0.9f, Label = 96f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 5f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 0.8f, HistoricalDonations = 3f, ResponseReliability = 0.8f, Label = 91f },
                new() { BloodCompatibility = 0.85f, DistanceKm = 8f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 0.8f, HistoricalDonations = 4f, ResponseReliability = 0.85f, Label = 84f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 20f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 0.5f, HistoricalDonations = 2f, ResponseReliability = 0.7f, Label = 76f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 3f, Availability = 0f, Eligibility = 1f, UrgencyWeight = 0.8f, HistoricalDonations = 3f, ResponseReliability = 0.8f, Label = 55f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 4f, Availability = 1f, Eligibility = 0.3f, UrgencyWeight = 0.8f, HistoricalDonations = 1f, ResponseReliability = 0.6f, Label = 52f },
                new() { BloodCompatibility = 0.85f, DistanceKm = 240f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 1f, HistoricalDonations = 6f, ResponseReliability = 0.9f, Label = 42f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 240f, Availability = 0f, Eligibility = 0.2f, UrgencyWeight = 0.5f, HistoricalDonations = 0f, ResponseReliability = 0.3f, Label = 18f },
                new() { BloodCompatibility = 1.0f, DistanceKm = 1f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 1f, HistoricalDonations = 8f, ResponseReliability = 0.95f, Label = 98f },
                new() { BloodCompatibility = 0.85f, DistanceKm = 4f, Availability = 1f, Eligibility = 1f, UrgencyWeight = 0.8f, HistoricalDonations = 2f, ResponseReliability = 0.75f, Label = 82f }
            };
        }

        private static List<DonorResponseInput> GenerateDonorResponseTrainingSamples()
        {
            return new List<DonorResponseInput>
            {
                new() { MatchScore = 95f, DistanceKm = 2f, UrgencyWeight = 1f, HistoricalDonations = 6f, Label = 94f },
                new() { MatchScore = 90f, DistanceKm = 5f, UrgencyWeight = 0.8f, HistoricalDonations = 4f, Label = 88f },
                new() { MatchScore = 82f, DistanceKm = 8f, UrgencyWeight = 0.8f, HistoricalDonations = 3f, Label = 80f },
                new() { MatchScore = 75f, DistanceKm = 15f, UrgencyWeight = 0.5f, HistoricalDonations = 2f, Label = 65f },
                new() { MatchScore = 55f, DistanceKm = 25f, UrgencyWeight = 0.5f, HistoricalDonations = 1f, Label = 45f },
                new() { MatchScore = 40f, DistanceKm = 200f, UrgencyWeight = 0.8f, HistoricalDonations = 1f, Label = 28f }
            };
        }
    }
}
