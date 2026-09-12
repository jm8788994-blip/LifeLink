using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models
{
    public class Recipient
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Blood Type Needed")]
        public string BloodType { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Hospital Name")]
        public string HospitalName { get; set; } = string.Empty;

        [Required]
        public string Address { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Required Date")]
        [DataType(DataType.Date)]
        public DateTime RequiredDate { get; set; }

        [Display(Name = "Registration Date")]
        public DateTime RegistrationDate { get; set; } = DateTime.Now;

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;
    }
}
