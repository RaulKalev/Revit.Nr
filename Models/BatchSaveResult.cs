using System.Collections.Generic;

namespace Renumber.Models
{
    /// <summary>
    /// Result summary returned after a batch save operation to Revit types.
    /// Contains counts and per-row detail messages for UI display.
    /// </summary>
    public class BatchSaveResult
    {
        /// <summary>Number of types that were successfully updated.</summary>
        public int UpdatedCount { get; set; }

        /// <summary>Number of types skipped (e.g. missing parameters).</summary>
        public int SkippedCount { get; set; }

        /// <summary>Number of types that failed due to exceptions.</summary>
        public int FailedCount { get; set; }

        /// <summary>Per-row detail messages for logging or user display.</summary>
        public List<string> Details { get; set; } = new List<string>();
    }
}
