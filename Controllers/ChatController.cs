using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ChatController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? withUserId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int currentUserId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Find all unique users current user has exchanged messages with
            var messageUserIds = await _context.ChatMessages
                .Where(m => m.SenderId == currentUserId || m.ReceiverId == currentUserId)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.SenderId == currentUserId ? m.ReceiverId : m.SenderId)
                .Distinct()
                .ToListAsync();

            var recentContacts = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.DonorProfile)
                .Where(u => messageUserIds.Contains(u.UserId))
                .ToListAsync();

            // If withUserId specified and not in recent contacts, add them
            User? activeContact = null;
            if (withUserId.HasValue && withUserId.Value != currentUserId)
            {
                activeContact = await _context.Users
                    .Include(u => u.Role)
                    .Include(u => u.DonorProfile)
                    .FirstOrDefaultAsync(u => u.UserId == withUserId.Value);

                if (activeContact != null && !recentContacts.Any(c => c.UserId == activeContact.UserId))
                {
                    recentContacts.Insert(0, activeContact);
                }
            }

            if (activeContact == null && recentContacts.Any())
            {
                activeContact = recentContacts.First();
            }

            // Load message thread with active contact
            var conversation = new List<ChatMessage>();
            if (activeContact != null)
            {
                conversation = await _context.ChatMessages
                    .Where(m => (m.SenderId == currentUserId && m.ReceiverId == activeContact.UserId) ||
                                (m.SenderId == activeContact.UserId && m.ReceiverId == currentUserId))
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();

                // Mark unread messages as read
                var unread = conversation.Where(m => m.ReceiverId == currentUserId && !m.IsRead).ToList();
                if (unread.Any())
                {
                    foreach (var msg in unread)
                    {
                        msg.IsRead = true;
                    }
                    await _context.SaveChangesAsync();
                }
            }

            ViewBag.CurrentUserId = currentUserId;
            ViewBag.ActiveContact = activeContact;
            ViewBag.RecentContacts = recentContacts;
            ViewBag.Conversation = conversation;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int currentUserId)) return Unauthorized();

            var unread = await _context.ChatMessages
                .CountAsync(m => m.ReceiverId == currentUserId && !m.IsRead);

            return Json(new { count = unread });
        }

        [HttpGet]
        [Route("api/[controller]/[action]")]
        [Route("[controller]/[action]")]
        public async Task<IActionResult> GetMessages(int otherUserId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int currentUserId)) return Unauthorized();

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == currentUserId && m.ReceiverId == otherUserId) ||
                            (m.SenderId == otherUserId && m.ReceiverId == currentUserId))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.ChatMessageId,
                    senderId = m.SenderId,
                    isMe = m.SenderId == currentUserId,
                    message = m.Message,
                    timestamp = m.CreatedAt.ToString("hh:mm tt"),
                    isRead = m.IsRead
                })
                .ToListAsync();

            return Json(messages);
        }
    }
}
