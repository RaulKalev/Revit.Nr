namespace Renumber.Models
{
    /// <summary>
    /// Result of resetting override graphics for DALI filters in a view.
    /// </summary>
    public class ResetResult
    {
        /// <summary>True if overrides were cleared without errors.</summary>
        public bool Success { get; set; }

        /// <summary>Human-readable status message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Number of filters whose overrides were cleared.</summary>
        public int ClearedCount { get; set; }
    }
}
