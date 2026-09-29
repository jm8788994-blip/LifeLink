namespace LifeLink.Models.ViewModels
{
    public class ChatUserContext
    {
        public int? UserId { get; set; }
        public string? Role { get; set; }
        public string? Name { get; set; }
        public bool IsAuthenticated { get; set; }

        public ChatUserContext(
            int? userId,
            string? role,
            string? name,
            bool isAuthenticated)
        {
            UserId = userId;
            Role = role;
            Name = name;
            IsAuthenticated = isAuthenticated;
        }
    }
}