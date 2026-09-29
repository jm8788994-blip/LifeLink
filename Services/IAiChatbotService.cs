using LifeLink.Models;
using LifeLink.Models.ViewModels;

namespace LifeLink.Services
{
    public interface IAiChatbotService
    {
        Task<ChatbotMessageResponse> ProcessQueryAsync(string userMessage, ChatUserContext? user = null);
    }
}