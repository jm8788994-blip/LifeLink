using LifeLink.Models;

namespace LifeLink.Services
{
    public interface IAiChatbotService
    {
        Task<ChatbotMessageResponse> ProcessQueryAsync(string userMessage);
    }
}
