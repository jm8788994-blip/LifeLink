using LifeLink.Data;
using LifeLink.Hubs;
using LifeLink.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<LifeLinkHub> _hubContext;
        private readonly IAiMatchingService _matchingService;

        public NotificationService(
            ApplicationDbContext context,
            IHubContext<LifeLinkHub> hubContext,
            IAiMatchingService matchingService)
        {
            _context = context;
            _hubContext = hubContext;
            _matchingService = matchingService;
        }

        public async Task SendNotificationToUserAsync(int userId, string title, string message, string type = "info", string? url = null)
        {
            // 1. Save in database
            var notif = new Notification
            {
                UserId = userId,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.Notifications.Add(notif);
            await _context.SaveChangesAsync();

            // 2. Real-time push via SignalR
            var payload = new
            {
                notificationId = notif.NotificationId,
                title = title,
                message = message,
                type = type, // "success", "danger", "warning", "info"
                url = url,
                timestamp = notif.CreatedAt.ToString("hh:mm tt")
            };

            await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", payload);
        }

        public async Task BroadcastEmergencyRequestAsync(string bloodGroup, string hospitalName, string location, string urgency, int requestId, string? patientName = null, string? diseaseName = null, string? hemoglobin = null)
        {
            // Find all active donors whose blood group can donate to this request
            var allDonors = await _context.DonorProfiles
                .Include(d => d.User)
                .Where(d => d.Availability)
                .ToListAsync();

            var compatibleDonors = allDonors
                .Where(d => _matchingService.IsBloodCompatible(d.BloodGroup, bloodGroup))
                .ToList();

            var patientInfo = string.IsNullOrWhiteSpace(patientName)
                ? ""
                : $" Patient: {patientName}{(string.IsNullOrWhiteSpace(diseaseName) ? "" : $" ({diseaseName})")}{(string.IsNullOrWhiteSpace(hemoglobin) ? "" : $", Hb {hemoglobin} g/dL")}.";

            var payload = new
            {
                requestId = requestId,
                bloodGroup = bloodGroup,
                hospitalName = hospitalName,
                location = location,
                urgency = urgency,
                patientName = patientName,
                diseaseName = diseaseName,
                hemoglobin = hemoglobin,
                title = $"URGENT: {bloodGroup} Blood Needed!",
                message = $"Emergency request for {bloodGroup} at {hospitalName} ({location}).{patientInfo} Are you able to help?",
                url = $"/Donor/Dashboard",
                timestamp = DateTime.UtcNow.ToString("hh:mm tt")
            };

            // Save individual notifications and push to each compatible donor group
            foreach (var donor in compatibleDonors)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = donor.UserId,
                    RequestId = requestId,
                    Message = $"Emergency: {bloodGroup} needed at {hospitalName} ({location}).{patientInfo}",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });

                await _hubContext.Clients.Group($"user_{donor.UserId}").SendAsync("ReceiveEmergencyAlert", payload);
            }

            await _context.SaveChangesAsync();
        }
    }
}
