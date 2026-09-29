using System;
using System.ComponentModel.DataAnnotations;

namespace LifeLink.Models.ViewModels
{
    public class ProfileEditViewModel
    {
        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string Phone { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Blood Group")]
        public string BloodGroup { get; set; } = string.Empty;

        [Required]
        public string Location { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        public DateTime DateOfBirth { get; set; }
       public string? ExistingProfilePicture { get; set; }

    }
}
