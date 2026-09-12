using LifeLink.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // 10 Entities from ER Diagram
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<DonorProfile> DonorProfiles { get; set; } = null!;
        public DbSet<Hospital> Hospitals { get; set; } = null!;
        public DbSet<BloodRequest> BloodRequests { get; set; } = null!;
        public DbSet<DonationHistory> DonationHistory { get; set; } = null!;
        public DbSet<BloodStock> BloodStock { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<Rating> Ratings { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User - Role
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            // DonorProfile - User (One-to-One / Unique)
            modelBuilder.Entity<DonorProfile>()
                .HasOne(d => d.User)
                .WithOne(u => u.DonorProfile)
                .HasForeignKey<DonorProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // BloodRequest - Receiver (User)
            modelBuilder.Entity<BloodRequest>()
                .HasOne(br => br.Receiver)
                .WithMany(u => u.BloodRequests)
                .HasForeignKey(br => br.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            // BloodRequest - Hospital
            modelBuilder.Entity<BloodRequest>()
                .HasOne(br => br.Hospital)
                .WithMany(h => h.BloodRequests)
                .HasForeignKey(br => br.HospitalId)
                .OnDelete(DeleteBehavior.SetNull);

            // DonationHistory - DonorProfile
            modelBuilder.Entity<DonationHistory>()
                .HasOne(dh => dh.Donor)
                .WithMany(dp => dp.Donations)
                .HasForeignKey(dh => dh.DonorId)
                .OnDelete(DeleteBehavior.Cascade);

            // DonationHistory - BloodRequest
            modelBuilder.Entity<DonationHistory>()
                .HasOne(dh => dh.BloodRequest)
                .WithMany(br => br.Donations)
                .HasForeignKey(dh => dh.RequestId)
                .OnDelete(DeleteBehavior.SetNull);

            // DonationHistory - Hospital
            modelBuilder.Entity<DonationHistory>()
                .HasOne(dh => dh.Hospital)
                .WithMany(h => h.Donations)
                .HasForeignKey(dh => dh.HospitalId)
                .OnDelete(DeleteBehavior.SetNull);

            // BloodStock - Hospital
            modelBuilder.Entity<BloodStock>()
                .HasOne(bs => bs.Hospital)
                .WithMany(h => h.BloodStocks)
                .HasForeignKey(bs => bs.HospitalId)
                .OnDelete(DeleteBehavior.Cascade);

            // Notification - User
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Notification - BloodRequest
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.BloodRequest)
                .WithMany(br => br.Notifications)
                .HasForeignKey(n => n.RequestId)
                .OnDelete(DeleteBehavior.SetNull);

            // Rating - User (Reviewer) and TargetUser (Reviewee)
            modelBuilder.Entity<Rating>()
                .HasOne(r => r.User)
                .WithMany(u => u.RatingsGiven)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Rating>()
                .HasOne(r => r.TargetUser)
                .WithMany(u => u.RatingsReceived)
                .HasForeignKey(r => r.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // ChatMessage - Sender and Receiver
            modelBuilder.Entity<ChatMessage>()
                .HasOne(cm => cm.Sender)
                .WithMany(u => u.SentMessages)
                .HasForeignKey(cm => cm.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ChatMessage>()
                .HasOne(cm => cm.Receiver)
                .WithMany(u => u.ReceivedMessages)
                .HasForeignKey(cm => cm.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
