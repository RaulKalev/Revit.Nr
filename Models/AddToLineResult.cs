using System.Collections.Generic;

namespace Renumber.Models
{
    /// <summary>
    /// Result of the "Add to Line" operation that writes DALI_Line_ID
    /// to selected instance elements after limit validation.
    /// </summary>
    public class AddToLineResult
    {
        /// <summary>True if the transaction committed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable summary of the operation outcome.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Number of elements whose instance parameter was successfully written.</summary>
        public int UpdatedCount { get; set; }

        /// <summary>Number of elements skipped (missing param, wrong category, etc.).</summary>
        public int SkippedCount { get; set; }

        /// <summary>Number of elements where writing failed (read-only, exception).</summary>
        public int FailedCount { get; set; }

        /// <summary>Number of elements that had a previous non-empty line ID (overwritten).</summary>
        public int ReassignedCount { get; set; }

        /// <summary>Total mA load computed from the selection (for UI refresh).</summary>
        public double TotalLoadmA { get; set; }

        /// <summary>Total address count computed from the selection (for UI refresh).</summary>
        public int TotalAddressCount { get; set; }

        /// <summary>Detailed per-element messages (capped at 20 to avoid excessive output).</summary>
        public List<string> Details { get; set; } = new List<string>();
    }
}
