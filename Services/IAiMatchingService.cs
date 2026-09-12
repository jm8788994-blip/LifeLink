using LifeLink.Models;

namespace LifeLink.Services
{
    public interface IAiMatchingService
    {
        Task<List<RankedDonorResult>> RankDonorsAsync(string neededBloodGroup, string recipientLocation, string urgencyLevel, int maxCount = 10);
        float CalculateDistance(string loc1, string loc2);
        bool IsBloodCompatible(string donorGroup, string recipientGroup);
    }
}
