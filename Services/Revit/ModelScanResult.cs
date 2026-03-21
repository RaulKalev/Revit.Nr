using System.Collections.Generic;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Returned by ScanModelTotalsRequest.
    /// Maps DALI_Line_ID values -> per-line aggregate load (mA) and address count.
    /// </summary>
    public class ModelScanResult
    {
        /// <summary>key = DALI_Line_ID value (trimmed), value = accumulated load+addresses</summary>
        public Dictionary<string, LineTotals> ByLine { get; } = new Dictionary<string, LineTotals>();

        public List<string> Warnings { get; } = new List<string>();

        public class LineTotals
        {
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
            public int ElementCount { get; set; }
        }
    }
}
