using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeLink.Models
{
    public class DonationHistory
    {
        [Key]
        public int DonationId { get; set; }

        [Required]
        public int DonorId { get; set; }

        [ForeignKey(nameof(DonorId))]
        public DonorProfile? Donor { get; set; }

        public int? RequestId { get; set; }

        [ForeignKey(nameof(RequestId))]
        public BloodRequest? BloodRequest { get; set; }

        public int? HospitalId { get; set; }

        [ForeignKey(nameof(HospitalId))]
        public Hospital? Hospital { get; set; }

        public DateTime DonationDate { get; set; } = DateTime.UtcNow;

        [StringLength(30)]
        public string Status { get; set; } = "Completed"; // Scheduled, Completed, Cancelled
    }
}
