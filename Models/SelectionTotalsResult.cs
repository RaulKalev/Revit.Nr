using System.Collections.Generic;

namespace Renumber.Models
{
    /// <summary>
    /// Result of computing selection totals from the current Revit selection.
    /// Returned by SelectionTotalsService and consumed by GroupingViewModel.
    /// </summary>
    public class SelectionTotalsResult
    {
        /// <summary>Sum of DALI_mA_Load across all valid selected element types.</summary>
        public double TotalLoadmA { get; set; }

        /// <summary>Sum of DALI_Address_Count across all valid selected element types.</summary>
        public int TotalAddressCount { get; set; }

        /// <summary>Number of elements that contributed to the totals.</summary>
        public int ValidElementCount { get; set; }

        /// <summary>Number of elements skipped (missing params, wrong category, etc.).</summary>
        public int SkippedElementCount { get; set; }

        /// <summary>
        /// Human-readable warning messages for elements that could not be processed.
        /// Includes missing parameters, wrong storage types, linked elements, etc.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
