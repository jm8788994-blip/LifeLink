using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeLink.Models
{
    public class BloodRequest
    {
        [Key]
        public int RequestId { get; set; }

        [Required]
        public int ReceiverId { get; set; }

        [ForeignKey(nameof(ReceiverId))]
        public User? Receiver { get; set; }

        [Required]
        [StringLength(10)]
        public string BloodGroup { get; set; } = string.Empty;

        [Required]
        [Range(1, 100)]
        public int Quantity { get; set; } = 1;

        public int? HospitalId { get; set; }

        [ForeignKey(nameof(HospitalId))]
        public Hospital? Hospital { get; set; }

        [Required]
        [StringLength(200)]
        public string Location { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string EmergencyLevel { get; set; } = "Normal";

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        public DateTime? RequiredDate { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [StringLength(150)]
        public string? PatientName { get; set; }

        [StringLength(200)]
        public string? DiseaseName { get; set; }
        public string? Hemoglobin { get; set; }
       
        [StringLength(500)]
        public string? Reason { get; set; }

        public ICollection<DonationHistory> Donations { get; set; } = new List<DonationHistory>();

        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}