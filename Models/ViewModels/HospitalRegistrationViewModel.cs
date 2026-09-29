using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models.ViewModels
{
    public class HospitalRegistrationViewModel
    {
        [Required]
        [Display(Name = "Hospital Name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        [Display(Name = "Contact Number")]
        public string Contact { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Address { get; set; } = string.Empty;

        [Required]
        public string Location { get; set; } = string.Empty;
    }
}