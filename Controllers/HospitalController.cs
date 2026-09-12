using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize(Roles = "Hospital,Admin")]
    public class HospitalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Services.IAiForecastingService _forecastingService;
        private readonly Services.INotificationService _notificationService;

        public HospitalController(
            ApplicationDbContext context, 
            Services.IAiForecastingService forecastingService,
            Services.INotificationService notificationService)
        {
            _context = context;
            _forecastingService = forecastingService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Dashboard(int? hospitalId)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";

            // Find hospital: by ID if provided (e.g. admin inspecting), or by matching user email/phone, or default first hospital
            Hospital? hospital = null;
            if (hospitalId.HasValue)
            {
                hospital = await _context.Hospitals.FindAsync(hospitalId.Value);
            }

            if (hospital == null)
            {
                hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.Contact.Contains(userEmail) || userEmail.Contains("dmc"))
                           ?? await _context.Hospitals.FirstOrDefaultAsync();
            }

            if (hospital == null)
            {
                return NotFound("No hospital found in the system.");
            }

            // 1. Hospital Blood Stock (all blood groups)
            var stocks = await _context.BloodStock
                .Where(s => s.HospitalId == hospital.HospitalId)
                .OrderBy(s => s.BloodGroup)
                .ToListAsync();

            ViewBag.Stocks = stocks;
            ViewBag.TotalBags = stocks.Sum(s => s.Quantity);
            ViewBag.LowStockCount = stocks.Count(s => s.Quantity < 10 || s.Status == "Low Stock");

            // 1.1 AI Demand Forecasting & Shortage Risk Alerts (ML.NET)
            var forecasts = await _forecastingService.GetHospitalDemandForecastAsync(hospital.HospitalId);
            ViewBag.AiForecasts = forecasts;
            ViewBag.CriticalShortageCount = forecasts.Count(f => f.ShortageRisk is "CRITICAL" or "HIGH");

            // 2. Incoming blood requests designated for this hospital
            var requests = await _context.BloodRequests
                .Include(r => r.Receiver)
                .Where(r => r.HospitalId == hospital.HospitalId)
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            ViewBag.Requests = requests;
            ViewBag.PendingCount = requests.Count(r => r.Status == "Pending" || r.Status == "Donor Accepted");

            // 3. Completed donations at this hospital
            var donations = await _context.DonationHistory
                .Include(d => d.Donor)
                .ThenInclude(dp => dp!.User)
                .Where(d => d.HospitalId == hospital.HospitalId)
                .OrderByDescending(d => d.DonationDate)
                .Take(10)
                .ToListAsync();

            ViewBag.Donations = donations;

            return View(hospital);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStock(int stockId, int quantity, string? status)
        {
            var stock = await _context.BloodStock.FindAsync(stockId);
            if (stock != null)
            {
                stock.Quantity = Math.Max(0, quantity);
                stock.Status = string.IsNullOrWhiteSpace(status) 
                    ? (stock.Quantity < 10 ? "Low Stock" : "Available") 
                    : status;
                stock.LastUpdated = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Stock for {stock.BloodGroup} updated to {stock.Quantity} unit(s).";
            }

            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FulfillRequest(int requestId)
        {
            var req = await _context.BloodRequests.FindAsync(requestId);
            if (req != null)
            {
                // Decrement stock if available
                var stock = await _context.BloodStock
                    .FirstOrDefaultAsync(s => s.HospitalId == req.HospitalId && s.BloodGroup == req.BloodGroup);

                if (stock != null && stock.Quantity >= req.Quantity)
                {
                    stock.Quantity -= req.Quantity;
                    if (stock.Quantity < 10) stock.Status = "Low Stock";
                }

                req.Status = "Fulfilled";
                await _context.SaveChangesAsync();

                // SignalR: Send real-time notification to patient
                await _notificationService.SendNotificationToUserAsync(
                    req.ReceiverId, 
                    "Blood Request Fulfilled!", 
                    $"Your blood request #{req.RequestId} ({req.BloodGroup}) has been fulfilled by the hospital!", 
                    "success", 
                    "/Receiver/Dashboard");

                TempData["Success"] = $"Blood request #{requestId} has been fulfilled and blood stock deducted.";
            }

            return RedirectToAction(nameof(Dashboard));
        }

        [HttpGet]
        public async Task<IActionResult> ExportStockCsv(int? hospitalId)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "";

            Hospital? hospital = null;
            if (hospitalId.HasValue)
            {
                hospital = await _context.Hospitals.FindAsync(hospitalId.Value);
            }

            if (hospital == null)
            {
                hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.Contact.Contains(userEmail) || userEmail.Contains("dmc"))
                           ?? await _context.Hospitals.FirstOrDefaultAsync();
            }

            if (hospital == null) return NotFound("Hospital not found.");

            var stocks = await _context.BloodStock
                .Where(s => s.HospitalId == hospital.HospitalId)
                .OrderBy(s => s.BloodGroup)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Hospital Name,Blood Group,Quantity (Units),Status,Expiry Date,Last Updated");

            foreach (var s in stocks)
            {
                var hospName = $"\"{hospital.Name}\"";
                var group = s.BloodGroup;
                var qty = s.Quantity;
                var status = s.Status;
                var expiry = s.ExpiryDate.HasValue ? s.ExpiryDate.Value.ToString("yyyy-MM-dd") : "N/A";
                var updated = s.LastUpdated.ToString("yyyy-MM-dd HH:mm");

                sb.AppendLine($"{hospName},{group},{qty},{status},{expiry},{updated}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"LifeLink_BloodStock_{hospital.Name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fileName);
        }
    }
}
