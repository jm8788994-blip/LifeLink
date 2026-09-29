namespace LifeLink.Services
{
    public interface INotificationService
    {
        Task SendNotificationToUserAsync(int userId, string title, string message, string type = "info", string? url = null);
        Task BroadcastEmergencyRequestAsync(string bloodGroup, string hospitalName, string location, string urgency, int requestId, string? patientName = null, string? diseaseName = null, string? hemoglobin = null);
    }
}
