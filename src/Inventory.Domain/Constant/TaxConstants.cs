using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Domain.Constant
{
    public static class TaxConstants
    {
        public const decimal VatRate = 0.14m; // 14%
        public const decimal ManufacturingTaxRate = 0.01m; // 1%
        public const decimal CombinedTaxRate = VatRate + ManufacturingTaxRate; // 15%
    }
}
