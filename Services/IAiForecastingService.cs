using LifeLink.Models;

namespace LifeLink.Services
{
    public interface IAiForecastingService
    {
        Task<List<DemandForecastResult>> GetHospitalDemandForecastAsync(int hospitalId);
        Task<List<DemandForecastResult>> GetSystemWideDemandForecastAsync();
    }
}
