using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models
{
    public class BloodBag
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Blood Type")]
        public string BloodType { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Quantity (Units)")]
        public int Quantity { get; set; }

        [Required]
        [Display(Name = "Expiry Date")]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; }

        [Display(Name = "Status")]
        public string Status { get; set; } = "Available";

        [Display(Name = "Date Added")]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        [Display(Name = "Donor ID")]
        public int? DonorId { get; set; }

        public Donor? Donor { get; set; }
    }
}
