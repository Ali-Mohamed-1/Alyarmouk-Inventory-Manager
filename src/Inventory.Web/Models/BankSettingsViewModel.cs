using System;
using System.ComponentModel.DataAnnotations;

namespace Inventory.Web.Models
{
    public class BankSettingsViewModel
    {
        [Display(Name = "Bank Base Balance (Initial Capital)")]
        [Required]
        public decimal BankBaseBalance { get; set; }

        public DateTimeOffset? LastUpdatedUtc { get; set; }
    }
}
