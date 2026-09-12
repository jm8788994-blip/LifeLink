using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LifeLink.Models
{
    public class BloodStock
    {
        [Key]
        public int StockId { get; set; }

        [Required]
        public int HospitalId { get; set; }

        [ForeignKey(nameof(HospitalId))]
        public Hospital? Hospital { get; set; }

        [Required]
        [StringLength(10)]
        public string BloodGroup { get; set; } = string.Empty;

        [Required]
        [Range(0, 10000)]
        public int Quantity { get; set; } = 0;

        public DateTime? ExpiryDate { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = "Available"; // Available, Low Stock, Reserved, Expired

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
