using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models
{
    public class Donation
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Donor ID")]
        public int DonorId { get; set; }

        public Donor? Donor { get; set; }

        [Required]
        [Display(Name = "Blood Bag ID")]
        public int BloodBagId { get; set; }

        public BloodBag? BloodBag { get; set; }

        [Required]
        [Display(Name = "Donation Date")]
        [DataType(DataType.Date)]
        public DateTime DonationDate { get; set; } = DateTime.Now;

        [Display(Name = "Status")]
        public string Status { get; set; } = "Completed";
    }
}
