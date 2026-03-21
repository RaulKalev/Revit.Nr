using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;
using System;
using System.Collections.Generic;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that scans ALL elements in the active document,
    /// groups them by their DALI_Line_ID instance parameter value, and
    /// accumulates load (mA) and address counts per line.
    /// 
    /// This is used on startup to populate line/controller gauges without
    /// requiring the user to manually select elements first.
    /// 
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// </summary>
    public class ScanModelTotalsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly Action<ModelScanResult> _callback;

        public ScanModelTotalsRequest(SettingsModel settings, Action<ModelScanResult> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new ModelScanResult();

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Warnings.Add("No active document — cannot scan model.");
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;

                // --- Build category filter ---
                var includedCategoryIds = new HashSet<int>();
                foreach (var bic in _settings.IncludedCategories)
                    includedCategoryIds.Add((int)bic);

                if (includedCategoryIds.Count == 0)
                {
                    result.Warnings.Add("No categories configured — skipping model scan.");
                    DispatchResult(result);
                    return;
                }

                string lineIdParamName = _settings.Param_LineId;
                if (string.IsNullOrWhiteSpace(lineIdParamName))
                {
                    result.Warnings.Add("DALI_Line_ID parameter not configured — skipping model scan.");
                    DispatchResult(result);
                    return;
                }

                // --- Type-level param cache: typeId -> (loadmA, addressCount) ---
                var typeCache = new Dictionary<long, CachedType>();

                // --- Collect all non-type elements in the document ---
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                App.Logger?.Info("ScanModelTotals: starting full document scan...");
                int scanned = 0, matched = 0;

                foreach (Element element in collector)
                {
                    scanned++;

                    // Skip type elements & links
                    if (element is ElementType || element is RevitLinkInstance)
                        continue;

                    // Category filter
                    Category cat = element.Category;
                    if (cat == null) continue;

#if NET48
                    int catIdInt = cat.Id.IntegerValue;
#else
                    int catIdInt = (int)cat.Id.Value;
#endif

                    if (!includedCategoryIds.Contains(catIdInt)) continue;

                    // Read DALI_Line_ID from instance
                    Parameter lineParam = element.LookupParameter(lineIdParamName);
                    if (lineParam == null) continue;

                    string lineIdValue = lineParam.AsString()?.Trim();
                    if (string.IsNullOrEmpty(lineIdValue)) continue;

                    string ctrlValue = string.Empty;
                    if (!string.IsNullOrWhiteSpace(_settings.Param_Controller))
                    {
                        Parameter ctrlParam = element.LookupParameter(_settings.Param_Controller);
                        if (ctrlParam != null)
                        {
                            ctrlValue = ctrlParam.StorageType == StorageType.String ? ctrlParam.AsString()?.Trim() : ctrlParam.AsValueString()?.Trim();
                        }
                    }
                    if (ctrlValue == null) ctrlValue = string.Empty;

                    // Read mA + addresses directly from instance, fallback to type
                    var data = ReadParams(doc, element);

                    // Accumulate into the line bucket using composite key: "Controller||Line"
                    string compositeKey = $"{ctrlValue}||{lineIdValue}";
                    if (!result.ByLine.TryGetValue(compositeKey, out var totals))
                    {
                        totals = new ModelScanResult.LineTotals();
                        result.ByLine[compositeKey] = totals;
                    }

                    totals.LoadmA += data.LoadmA;
                    totals.AddressCount += data.AddressCount;
                    totals.ElementCount++;
                    matched++;
                }

                App.Logger?.Info($"ScanModelTotals: scanned {scanned} elements, matched {matched} into {result.ByLine.Count} distinct controller/line combo(s).");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error scanning model: {ex.Message}");
                App.Logger?.Error("ScanModelTotals: exception.", ex);
            }

            DispatchResult(result);
        }

        private CachedType ReadParams(Document doc, Element instanceElement)
        {
            var cached = new CachedType();

            // 1. Try LoadmA
            if (!string.IsNullOrWhiteSpace(_settings.Param_Load))
            {
                Parameter pInstance = instanceElement.LookupParameter(_settings.Param_Load);
                if (pInstance != null && pInstance.HasValue) 
                {
                    cached.LoadmA = ReadDouble(pInstance);
                }
                else
                {
                    Element typeElement = doc.GetElement(instanceElement.GetTypeId());
                    if (typeElement is FamilySymbol symbol)
                    {
                        Parameter pType = symbol.LookupParameter(_settings.Param_Load);
                        if (pType != null) cached.LoadmA = ReadDouble(pType);
                    }
                }
            }

            // 2. Try AddressCount
            if (!string.IsNullOrWhiteSpace(_settings.Param_AddressCount))
            {
                Parameter pInstance = instanceElement.LookupParameter(_settings.Param_AddressCount);
                if (pInstance != null && pInstance.HasValue)
                {
                    cached.AddressCount = ReadInt(pInstance);
                }
                else
                {
                    Element typeElement = doc.GetElement(instanceElement.GetTypeId());
                    if (typeElement is FamilySymbol symbol)
                    {
                        Parameter pType = symbol.LookupParameter(_settings.Param_AddressCount);
                        if (pType != null) cached.AddressCount = ReadInt(pType);
                    }
                }
            }

            return cached;
        }

        private static double ReadDouble(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return (double)p.AsInteger();
                case StorageType.String:
                    return double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0.0;
                default: return 0.0;
            }
        }

        private static int ReadInt(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return (int)p.AsDouble();
                case StorageType.String:
                    return int.TryParse(p.AsString(), out int v) ? v : 0;
                default: return 0;
            }
        }

        private void DispatchResult(ModelScanResult result)
        {
            // ExternalEvent.Execute() already runs on the main UI thread.
            // Using Dispatcher.BeginInvoke crashes Revit 2026 because Revit
            // suspends WPF Dispatcher processing during ExternalEvent execution.
            _callback(result);
        }

        private class CachedType
        {
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
        }
    }
}


