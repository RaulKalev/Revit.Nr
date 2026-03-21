using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;
using Renumber.Services.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that writes the ActiveLineName to the DALI_Line_ID
    /// instance parameter on all valid selected elements, with strict limit validation.
    /// 
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// 
    /// Safety: Re-computes totals inside the handler (does NOT trust UI values)
    /// and validates limits BEFORE opening a transaction.
    /// </summary>
    public class AddToLineRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _activeLineName;
        private readonly string _controllerName;
        private readonly double _maxLoadmA;
        private readonly int _maxAddressCount;
        private readonly HighlightRegistry _highlightRegistry;
        private readonly Action<AddToLineResult> _callback;
        private readonly string _colorHex;

        /// <summary>Maximum number of detail messages to collect (avoids unbounded output).</summary>
        private const int MaxDetailMessages = 20;

        public AddToLineRequest(
            SettingsModel settings,
            string activeLineName,
            string controllerName,
            double maxLoadmA,
            int maxAddressCount,
            HighlightRegistry highlightRegistry,
            Action<AddToLineResult> callback,
            string colorHex = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _activeLineName = activeLineName;
            _controllerName = controllerName;
            _maxLoadmA = maxLoadmA;
            _maxAddressCount = maxAddressCount;
            _highlightRegistry = highlightRegistry;
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _colorHex = colorHex;
        }

        public void Execute(UIApplication app)
        {
            var result = new AddToLineResult();
            var log = App.Logger;

            try
            {
                log?.Info($"AddToLine: starting for line '{_activeLineName}'.");

                // --- Validate inputs ---
                if (string.IsNullOrWhiteSpace(_activeLineName))
                {
                    result.Message = "Cannot add to line: line name is empty.";
                    DispatchResult(result);
                    return;
                }

                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Message = "No active document.";
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;
                ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();

                if (selectedIds == null || selectedIds.Count == 0)
                {
                    result.Message = "No elements selected.";
                    DispatchResult(result);
                    return;
                }

                // --- Build category filter set ---
                var includedCategoryIds = new HashSet<int>();
                foreach (var bic in _settings.IncludedCategories)
                {
                    includedCategoryIds.Add((int)bic);
                }

                // --- Phase 1: Filter elements and compute totals (read-only, no transaction) ---
                var validElements = new List<Element>();
                var typeCache = new Dictionary<long, CachedTypeData>();
                string lineIdParamName = _settings.Param_LineId;
                string controllerParamName = _settings.Param_Controller;
                string trimmedLineName = _activeLineName.Trim();
                string trimmedControllerName = _controllerName?.Trim();

                foreach (var eid in selectedIds)
                {
                    Element element;
                    try
                    {
                        element = doc.GetElement(eid);
                    }
                    catch
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Could not resolve element ID {eid}.");
                        continue;
                    }

                    if (element == null)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Skip element types (only instances)
                    if (element is ElementType)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Skip linked elements
                    if (element is RevitLinkInstance)
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Skipped linked element: {element.Name}");
                        continue;
                    }

                    // Check category
                    Category cat = element.Category;
                    if (cat == null)
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Skipped element without category: ID {eid}");
                        continue;
                    }

#if NET48
                    int catIdInt = cat.Id.IntegerValue;
#else
                    int catIdInt = (int)cat.Id.Value;
#endif

                    if (!includedCategoryIds.Contains(catIdInt))
                    {
                        result.SkippedCount++;
                        // Not in included categories -- silent skip
                        continue;
                    }

                    // Resolve FamilySymbol for type parameter reading
                    ElementId typeId = element.GetTypeId();
#if NET48
                    if (typeId == null || typeId == ElementId.InvalidElementId)
#else
                    if (typeId == ElementId.InvalidElementId)
#endif
                    {
                        result.SkippedCount++;
                        AddDetail(result, $"Element '{element.Name}' has no type -- cannot validate.");
                        continue;
                    }

                    // Get totals from Instance, fallback to Type
                    var data = ReadData(doc, element);

                    // If type is missing required parameters, BLOCK this element
                    if (!data.IsValid)
                    {
                        result.SkippedCount++;
                        if (data.Warning != null)
                        {
                            AddDetail(result, data.Warning);
                        }
                        continue;
                    }

                    // --- Deduplication Check ---
                    // If the element is already assigned to this exact line and controller, skip it so we don't double-count
                    Parameter lineIdParam = element.LookupParameter(lineIdParamName);
                    Parameter controllerParam = element.LookupParameter(controllerParamName);
                    
                    string elemLineName = lineIdParam?.StorageType == StorageType.String ? lineIdParam.AsString() : lineIdParam?.AsValueString();
                    string elemCtrlName = controllerParam?.StorageType == StorageType.String ? controllerParam.AsString() : controllerParam?.AsValueString();

                    // Compare names (consider nulls)
                    if (elemLineName == trimmedLineName && 
                        (string.IsNullOrWhiteSpace(trimmedControllerName) || elemCtrlName == trimmedControllerName))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Accumulate totals from valid elements
                    result.TotalLoadmA += data.LoadmA;
                    result.TotalAddressCount += data.AddressCount;
                    validElements.Add(element);
                }

                // --- Phase 2: Validate limits ---
                if (validElements.Count == 0)
                {
                    result.Message = $"No valid elements to assign. Skipped: {result.SkippedCount}.";
                    log?.Warning($"AddToLine: blocked -- no valid elements. Skipped: {result.SkippedCount}.");
                    DispatchResult(result);
                    return;
                }

                log?.Info($"AddToLine: Phase 1 complete. Valid: {validElements.Count}, Skipped: {result.SkippedCount}, Load: {result.TotalLoadmA:N1} mA, Addr: {result.TotalAddressCount}.");

                if (result.TotalLoadmA > _maxLoadmA)
                {
                    result.Message = $"BLOCKED: Total load {result.TotalLoadmA:N1} mA exceeds limit of {_maxLoadmA:N0} mA.";
                    DispatchResult(result);
                    return;
                }

                if (result.TotalAddressCount > _maxAddressCount)
                {
                    result.Message = $"BLOCKED: Total address count {result.TotalAddressCount} exceeds limit of {_maxAddressCount}.";
                    DispatchResult(result);
                    return;
                }

                // --- Phase 3: Write DALI_Line_ID and DALI_Controller to instance elements ---
                // Use variables previously declared in Phase 1

                if (string.IsNullOrWhiteSpace(lineIdParamName))
                {
                    result.Message = "Instance parameter name (DALI_Line_ID) is not configured in Settings.";
                    DispatchResult(result);
                    return;
                }

                using (var trans = new Transaction(doc, "DALI: Add to Line"))
                {
                    trans.Start();

                    foreach (var element in validElements)
                    {
                        try
                        {
                            // 1. Write Line ID
                            Parameter lineParam = element.LookupParameter(lineIdParamName);
                            if (lineParam == null)
                            {
                                result.FailedCount++;
                                AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): missing instance parameter '{lineIdParamName}'.");
                                continue;
                            }
                            if (lineParam.IsReadOnly)
                            {
                                result.FailedCount++;
                                AddDetail(result, $"Element '{element.Name}' (ID {element.Id}): '{lineIdParamName}' is read-only.");
                                continue;
                            }
                            lineParam.Set(trimmedLineName);

                            // 2. Write Controller Name (if configured/available)
                            if (!string.IsNullOrWhiteSpace(controllerParamName) && !string.IsNullOrWhiteSpace(trimmedControllerName))
                            {
                                Parameter ctrlParam = element.LookupParameter(controllerParamName);
                                if (ctrlParam != null && !ctrlParam.IsReadOnly)
                                {
                                    ctrlParam.Set(trimmedControllerName);
                                }
                            }

                            result.UpdatedCount++;
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            AddDetail(result, $"Error writing to '{element.Name}' (ID {element.Id}): {ex.Message}");
                        }
                    }

                    if (result.UpdatedCount > 0)
                    {
                        trans.Commit();
                        result.Success = true;
                    }
                    else
                    {
                        trans.RollBack();
                        result.Success = false;
                    }
                }

                // --- Phase 4: Summarize ---
                if (result.Success)
                {
                    result.Message = $"Added {result.UpdatedCount} element(s) to '{trimmedLineName}'.";
                    if (result.FailedCount > 0)
                        result.Message += $" Failed: {result.FailedCount}.";
                    if (result.SkippedCount > 0)
                        result.Message += $" Skipped: {result.SkippedCount}.";

                    log?.Info($"AddToLine: success. Added {result.UpdatedCount}, Failed {result.FailedCount}, Skipped {result.SkippedCount}.");
                }
                else
                {
                    result.Message = $"Failed to assign elements to '{trimmedLineName}'. See details.";
                    log?.Warning("AddToLine: failed.");
                }

                // --- Phase 5: Apply view filter highlight (non-blocking) ---
                // Runs in a separate transaction after the write commits.
                // Highlight failures do NOT roll back the parameter writes.
                try
                {
                    var view = doc.ActiveView;
                    if (view != null && _highlightRegistry != null)
                    {
                        var highlighter = new ViewFilterHighlighter();
                        using (var highlightTrans = new Transaction(doc, "DALI: Apply Highlight"))
                        {
                            highlightTrans.Start();
                            var hlResult = highlighter.ApplyLineHighlight(
                                doc, view, _settings, trimmedControllerName, trimmedLineName, _highlightRegistry, _colorHex);
                            
                            if (hlResult.Success && hlResult.ElementsOnLine != null)
                            {
                                app.ActiveUIDocument?.Selection.SetElementIds(hlResult.ElementsOnLine);
                                app.ActiveUIDocument?.RefreshActiveView();
                            }

                            highlightTrans.Commit();

                            if (hlResult.Success)
                            {
                                result.Message += $" {hlResult.Message}";
                            }
                            else if (!string.IsNullOrEmpty(hlResult.Message))
                            {
                                AddDetail(result, $"Highlight: {hlResult.Message}");
                            }
                        }
                    }
                }
                catch (Exception hlEx)
                {
                    // Highlight failure is non-blocking
                    AddDetail(result, $"Highlight failed: {hlEx.Message}");
                    log?.Warning($"AddToLine: highlight failed: {hlEx.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Error during Add to Line: {ex.Message}";
            }

            // Force Revit to recalculate and redraw the view with updated overrides
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc != null)
                {
                    uidoc.Document.Regenerate();
                    uidoc.RefreshActiveView();
                }
            }
            catch { }

            DispatchResult(result);
        }

        /// <summary>
        /// Reads DALI parameters (mA Load, Address Count) from Instance, falling back to Type.
        /// </summary>
        private CachedTypeData ReadData(Document doc, Element instanceElement)
        {
            var data = new CachedTypeData();
            bool hasLoad = false;
            bool hasAddr = false;

            // Read load
            if (!string.IsNullOrWhiteSpace(_settings.Param_Load))
            {
                Parameter pInstance = instanceElement.LookupParameter(_settings.Param_Load);
                if (pInstance != null && pInstance.HasValue) 
                {
                    hasLoad = true;
                    data.LoadmA = SafeReadDouble(pInstance);
                }
                else
                {
                    Element typeElement = doc.GetElement(instanceElement.GetTypeId());
                    if (typeElement is FamilySymbol symbol)
                    {
                        Parameter pType = symbol.LookupParameter(_settings.Param_Load);
                        if (pType != null)
                        {
                            hasLoad = true;
                            data.LoadmA = SafeReadDouble(pType);
                        }
                        else
                        {
                            data.Warning = $"Missing '{_settings.Param_Load}' parameter.";
                        }
                    }
                }
            }

            // Read Address Count
            if (!string.IsNullOrWhiteSpace(_settings.Param_AddressCount))
            {
                Parameter pInstance = instanceElement.LookupParameter(_settings.Param_AddressCount);
                if (pInstance != null && pInstance.HasValue)
                {
                    hasAddr = true;
                    data.AddressCount = SafeReadInt(pInstance);
                }
                else
                {
                    Element typeElement = doc.GetElement(instanceElement.GetTypeId());
                    if (typeElement is FamilySymbol symbol)
                    {
                        Parameter pType = symbol.LookupParameter(_settings.Param_AddressCount);
                        if (pType != null)
                        {
                            hasAddr = true;
                            data.AddressCount = SafeReadInt(pType);
                        }
                        else
                        {
                            string msg = $"Missing '{_settings.Param_AddressCount}' parameter.";
                            data.Warning = data.Warning != null ? data.Warning + " | " + msg : msg;
                        }
                    }
                }
            }

            data.IsValid = hasLoad || hasAddr;
            return data;
        }

        private static double SafeReadDouble(Parameter param)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Double: return param.AsDouble();
                    case StorageType.Integer: return (double)param.AsInteger();
                    case StorageType.String:
                        if (double.TryParse(param.AsString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double v))
                            return v;
                        return 0.0;
                    default: return 0.0;
                }
            }
            catch { return 0.0; }
        }

        private static int SafeReadInt(Parameter param)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Integer: return param.AsInteger();
                    case StorageType.Double: return (int)param.AsDouble();
                    case StorageType.String:
                        if (int.TryParse(param.AsString(), out int v)) return v;
                        return 0;
                    default: return 0;
                }
            }
            catch { return 0; }
        }

        /// <summary>Adds a detail message if under the cap.</summary>
        private static void AddDetail(AddToLineResult result, string msg)
        {
            if (result.Details.Count < MaxDetailMessages)
            {
                result.Details.Add(msg);
            }
        }

        /// <summary>Marshals the result back to the WPF UI thread.</summary>
        private void DispatchResult(AddToLineResult result)
        {
            _callback(result);
        }

        /// <summary>Internal cache for type-level DALI parameter values.</summary>
        private class CachedTypeData
        {
            public bool IsValid { get; set; }
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
            public string Warning { get; set; }
        }
    }
}


