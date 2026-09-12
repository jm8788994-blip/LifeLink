using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models
{
    public class Hospital
    {
        [Key]
        public int HospitalId { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Address { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Location { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Contact { get; set; } = string.Empty;

        [StringLength(30)]
        public string VerificationStatus { get; set; } = "Verified";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<BloodRequest> BloodRequests { get; set; } = new List<BloodRequest>();
        public ICollection<BloodStock> BloodStocks { get; set; } = new List<BloodStock>();
        public ICollection<DonationHistory> Donations { get; set; } = new List<DonationHistory>();
    }
}
