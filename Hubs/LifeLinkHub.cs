using System.Security.Claims;
using LifeLink.Data;
using LifeLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Hubs
{
    [Authorize]
    public class LifeLinkHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public LifeLinkHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                // Join personal user group for targeted notifications and direct messages
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        // --- 1-on-1 Live Chat (PDF 4.11) ---
        public async Task SendMessage(int targetUserId, string message)
        {
            var senderIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var senderName = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "User";

            if (!int.TryParse(senderIdStr, out int senderId) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // 1. Save ChatMessage in PostgreSQL
            var chatMsg = new ChatMessage
            {
                SenderId = senderId,
                ReceiverId = targetUserId,
                Message = message.Trim(),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.ChatMessages.Add(chatMsg);
            await _context.SaveChangesAsync();

            var payload = new
            {
                messageId = chatMsg.ChatMessageId,
                senderId = senderId,
                senderName = senderName,
                receiverId = targetUserId,
                message = chatMsg.Message,
                timestamp = chatMsg.CreatedAt.ToString("hh:mm tt")
            };

            // 2. Deliver in real-time to recipient group
            await Clients.Group($"user_{targetUserId}").SendAsync("ReceiveMessage", payload);

            // 3. Acknowledge back to sender
            await Clients.Caller.SendAsync("MessageSentAck", payload);
        }

        // --- Typing Indicator ---
        public async Task UserTyping(int targetUserId, bool isTyping)
        {
            var senderIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(senderIdStr, out int senderId))
            {
                await Clients.Group($"user_{targetUserId}").SendAsync("UserTypingStatus", new { senderId, isTyping });
            }
        }
    }
}
