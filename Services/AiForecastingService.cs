using LifeLink.Data;
using LifeLink.Models;
using LifeLink.Services.MlModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;

namespace LifeLink.Services
{
    public class AiForecastingService : IAiForecastingService
    {
        private readonly ApplicationDbContext _context;
        private readonly MLContext _mlContext;
        private readonly PredictionEngine<BloodDemandInput, BloodDemandPrediction> _demandEngine;

        public AiForecastingService(ApplicationDbContext context)
        {
            _context = context;
            _mlContext = new MLContext(seed: 123);
            _demandEngine = TrainDemandForecastingModel();
        }

        private PredictionEngine<BloodDemandInput, BloodDemandPrediction> TrainDemandForecastingModel()
        {
            var trainingData = GenerateDemandTrainingSamples();
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = _mlContext.Transforms.Categorical.OneHotEncoding("BloodGroupEncoded", nameof(BloodDemandInput.BloodGroup))
                .Append(_mlContext.Transforms.Concatenate("Features", "BloodGroupEncoded", nameof(BloodDemandInput.Month), nameof(BloodDemandInput.HistoricalRequests)))
                .Append(_mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", featureColumnName: "Features", maximumNumberOfIterations: 60));

            var model = pipeline.Fit(dataView);
            return _mlContext.Model.CreatePredictionEngine<BloodDemandInput, BloodDemandPrediction>(model);
        }

        public async Task<List<DemandForecastResult>> GetHospitalDemandForecastAsync(int hospitalId)
        {
            var stocks = await _context.BloodStock
                .Where(s => s.HospitalId == hospitalId)
                .ToListAsync();

            return CalculateForecasts(stocks);
        }

        public async Task<List<DemandForecastResult>> GetSystemWideDemandForecastAsync()
        {
            var aggregatedStocks = await _context.BloodStock
                .GroupBy(s => s.BloodGroup)
                .Select(g => new BloodStock
                {
                    BloodGroup = g.Key,
                    Quantity = g.Sum(s => s.Quantity)
                })
                .ToListAsync();

            return CalculateForecasts(aggregatedStocks);
        }

        private List<DemandForecastResult> CalculateForecasts(List<BloodStock> stocks)
        {
            var allGroups = new[] { "O+", "O-", "A+", "A-", "B+", "B-", "AB+", "AB-" };
            int currentMonth = DateTime.UtcNow.Month;
            var results = new List<DemandForecastResult>();

            foreach (var bg in allGroups)
            {
                var stockItem = stocks.FirstOrDefault(s => s.BloodGroup == bg);
                int currentQty = stockItem?.Quantity ?? 0;

                // Typical baseline historical request weight by blood group
                float baseRequests = bg switch
                {
                    "O+" => 28f,
                    "B+" => 24f,
                    "A+" => 20f,
                    "AB+" => 12f,
                    "O-" => 15f,
                    "B-" => 10f,
                    "A-" => 9f,
                    "AB-" => 6f,
                    _ => 15f
                };

                var input = new BloodDemandInput
                {
                    BloodGroup = bg,
                    Month = currentMonth,
                    HistoricalRequests = baseRequests
                };

                var prediction = _demandEngine.Predict(input);
                int expectedDemand = Math.Max(5, (int)Math.Round(prediction.PredictedUnits));

                string demandLevel = expectedDemand >= 20 ? "High" : (expectedDemand >= 12 ? "Medium" : "Low");

                // Evaluate Shortage Risk (PDF 4.7)
                string shortageRisk;
                string recommendation;

                if (currentQty <= 5 && expectedDemand >= 15)
                {
                    shortageRisk = "CRITICAL";
                    recommendation = $"Urgent shortage alert! Trigger emergency donor campaign for {bg}.";
                }
                else if (currentQty < 10 && demandLevel is "High" or "Medium")
                {
                    shortageRisk = "HIGH";
                    recommendation = $"Stock below safe threshold. Broadcast request to nearby {bg} eligible donors.";
                }
                else if (currentQty < expectedDemand)
                {
                    shortageRisk = "MODERATE";
                    recommendation = $"Approaching low reserve. Plan upcoming blood donation drive.";
                }
                else
                {
                    shortageRisk = "NORMAL";
                    recommendation = $"Adequate stock level maintained for expected seasonal demand.";
                }

                results.Add(new DemandForecastResult
                {
                    BloodGroup = bg,
                    CurrentStock = currentQty,
                    ForecastedDemand = expectedDemand,
                    DemandLevel = demandLevel,
                    ShortageRisk = shortageRisk,
                    Recommendation = recommendation
                });
            }

            return results.OrderByDescending(r => r.ShortageRisk == "CRITICAL")
                          .ThenByDescending(r => r.ShortageRisk == "HIGH")
                          .ThenBy(r => r.BloodGroup)
                          .ToList();
        }

        private static List<BloodDemandInput> GenerateDemandTrainingSamples()
        {
            return new List<BloodDemandInput>
            {
                new() { BloodGroup = "O+", Month = 1f, HistoricalRequests = 26f, Label = 28f },
                new() { BloodGroup = "O+", Month = 6f, HistoricalRequests = 30f, Label = 32f },
                new() { BloodGroup = "B+", Month = 2f, HistoricalRequests = 22f, Label = 24f },
                new() { BloodGroup = "A+", Month = 3f, HistoricalRequests = 18f, Label = 20f },
                new() { BloodGroup = "AB+", Month = 4f, HistoricalRequests = 10f, Label = 11f },
                new() { BloodGroup = "O-", Month = 5f, HistoricalRequests = 14f, Label = 16f },
                new() { BloodGroup = "B-", Month = 7f, HistoricalRequests = 8f, Label = 9f },
                new() { BloodGroup = "A-", Month = 8f, HistoricalRequests = 8f, Label = 9f },
                new() { BloodGroup = "AB-", Month = 9f, HistoricalRequests = 5f, Label = 6f }
            };
        }
    }
}
