using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Renumber.Models;
using Renumber.Services.Core;
using System;
using System.Collections.Generic;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Interactive request that lets the user pick devices one by one.
    /// Updates the UI immediately after each pick.
    /// Loop runs until user cancels (Esc).
    /// </summary>
    public class AddDevicesInteractiveRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _activeLineName;
        private readonly string _controllerName;
        private readonly HighlightRegistry _highlightRegistry;
        private readonly Action<string> _statusCallback;
        private readonly Action<double, int> _updateCallback;
        private readonly Action _completedCallback;
        private readonly string _colorHex;

        public AddDevicesInteractiveRequest(
            SettingsModel settings,
            string activeLineName,
            string controllerName,
            HighlightRegistry highlightRegistry,
            Action<string> statusCallback,
            Action<double, int> updateCallback,
            Action completedCallback,
            string colorHex = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _activeLineName = activeLineName;
            _controllerName = controllerName;
            _highlightRegistry = highlightRegistry;
            _statusCallback = statusCallback;
            _updateCallback = updateCallback;
            _completedCallback = completedCallback;
            _colorHex = colorHex;
        }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            // 1. Ensure view filter is active for this line (so user sees what they add)
            ApplyMetadataHighlight(doc, app.ActiveUIDocument.ActiveView);

            // 2. Build category filter for selection
            var includedCategoryIds = new HashSet<int>();
            foreach (var bic in _settings.IncludedCategories)
            {
                includedCategoryIds.Add((int)bic);
            }
            ISelectionFilter selectionFilter = new RenumberCategorySelectionFilter(includedCategoryIds);

            // 3. Selection Loop
            bool continueLoop = true;
            DispatchStatus($"Click to add devices to '{_activeLineName}'. Press ESC to finish.");

            while (continueLoop)
            {
                try
                {
                    Reference pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        selectionFilter,
                        $"Add to '{_activeLineName}': Select a device (ESC to stop)");

                    if (pickedRef != null)
                    {
                        ProcessPickedElement(doc, pickedRef);
                        uidoc.RefreshActiveView();
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // User pressed Esc
                    continueLoop = false;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error($"Interactive selection error: {ex.Message}");
                    continueLoop = false;
                }
            }

            // 4. Update highlights and selection to show all devices on this line
            try
            {
                using (var t = new Transaction(doc, "DALI: Update Highlight And Selection"))
                {
                    t.Start();
                    var highlighter = new ViewFilterHighlighter();
                    var hlResult = highlighter.ApplyLineHighlight(
                        doc, app.ActiveUIDocument.ActiveView, _settings, _controllerName, _activeLineName, _highlightRegistry, _colorHex);
                    
                    if (hlResult.Success && hlResult.ElementsOnLine != null)
                    {
                        uidoc.Selection.SetElementIds(hlResult.ElementsOnLine);
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning($"Highlight and selection update failed: {ex.Message}");
            }

            // Force Revit to recalculate and redraw the view with updated overrides
            try
            {
                doc.Regenerate();
                uidoc.RefreshActiveView();
            }
            catch { }

            DispatchCompleted();
        }

        private void ProcessPickedElement(Document doc, Reference pickedRef)
        {
            Element element = doc.GetElement(pickedRef);
            if (element == null) return;

            string lineIdParamName = _settings.Param_LineId;
            string controllerParamName = _settings.Param_Controller;
            string trimmedLineName = _activeLineName.Trim();
            string trimmedControllerName = _controllerName?.Trim();

            double addedLoad = 0;
            int addedAddr = 0;
            bool success = false;

            using (var trans = new Transaction(doc, "DALI: Add Device"))
            {
                trans.Start();

                try
                {
                    // --- Deduplication Check ---
                    // If element already belongs to this line exactly, do not add it or inflate gauges
                    Parameter lineIdParam = element.LookupParameter(lineIdParamName);
                    Parameter controllerParam = (!string.IsNullOrWhiteSpace(controllerParamName)) ? element.LookupParameter(controllerParamName) : null;
                    
                    string elemLineName = lineIdParam?.StorageType == StorageType.String ? lineIdParam.AsString() : lineIdParam?.AsValueString();
                    string elemCtrlName = controllerParam?.StorageType == StorageType.String ? controllerParam.AsString() : controllerParam?.AsValueString();

                    // Compare names
                    if (elemLineName == trimmedLineName && 
                        (string.IsNullOrWhiteSpace(trimmedControllerName) || elemCtrlName == trimmedControllerName))
                    {
                        // Already on this line
                        trans.RollBack();
                        return;
                    }

                    // Write Line ID
                    Parameter lineParam = element.LookupParameter(lineIdParamName);
                    if (lineParam != null && !lineParam.IsReadOnly)
                    {
                        // Check if we are overwriting a different line? 
                        // For now we just overwrite. 
                        // (Ideally we might subtract from the old line if we tracked it, but that's complex cross-line logic).
                        
                        lineParam.Set(trimmedLineName);
                        success = true;
                    }

                    // Write Controller
                    if (!string.IsNullOrWhiteSpace(controllerParamName) && !string.IsNullOrWhiteSpace(trimmedControllerName))
                    {
                        Parameter ctrlParam = element.LookupParameter(controllerParamName);
                        if (ctrlParam != null && !ctrlParam.IsReadOnly)
                        {
                            ctrlParam.Set(trimmedControllerName);
                        }
                    }

                    // Read Data for UI Update
                    if (success)
                    {
                        var data = ReadData(doc, element);
                        addedLoad = data.LoadmA;
                        addedAddr = data.AddressCount;
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    App.Logger?.Error($"Failed to process picked element: {ex.Message}");
                    success = false;
                }
            }

            if (success)
            {
                DispatchUpdate(addedLoad, addedAddr);
            }
        }

        private void ApplyMetadataHighlight(Document doc, View view)
        {
            if (_highlightRegistry == null) return;
            try
            {
                using (var t = new Transaction(doc, "DALI: Ensure Highlight"))
                {
                    t.Start();
                    new ViewFilterHighlighter().ApplyLineHighlight(doc, view, _settings, _controllerName, _activeLineName, _highlightRegistry, _colorHex);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning($"Highlight setup failed: {ex.Message}");
            }
        }

        // --- Helpers ---

        private void DispatchStatus(string msg)
        {
            _statusCallback?.Invoke(msg);
        }

        private void DispatchUpdate(double load, int addr)
        {
             _updateCallback?.Invoke(load, addr);
        }

        private void DispatchCompleted()
        {
             _completedCallback?.Invoke();
        }

        // --- Duplicated Parameter Reading Logic ---
        private CachedTypeData ReadData(Document doc, Element instanceElement)
        {
            var data = new CachedTypeData();
            
            // Read load
            if (!string.IsNullOrWhiteSpace(_settings.Param_Load))
            {
                Parameter pInstance = instanceElement.LookupParameter(_settings.Param_Load);
                if (pInstance != null && pInstance.HasValue) 
                {
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
                            data.LoadmA = SafeReadDouble(pType);
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
                            data.AddressCount = SafeReadInt(pType);
                        }
                    }
                }
            }

            return data;
        }

        private class CachedTypeData { public double LoadmA; public int AddressCount; }

        private static double SafeReadDouble(Parameter param)
        {
            if (param.StorageType == StorageType.Double) return param.AsDouble();
            if (param.StorageType == StorageType.Integer) return param.AsInteger();
            return 0;
        }
        private static int SafeReadInt(Parameter param)
        {
            if (param.StorageType == StorageType.Integer) return param.AsInteger();
            if (param.StorageType == StorageType.Double) return (int)param.AsDouble();
            return 0;
        }

        // --- Selection Filter ---
        public class RenumberCategorySelectionFilter : ISelectionFilter
        {
            private readonly HashSet<int> _categoryIds;
            public RenumberCategorySelectionFilter(HashSet<int> ids) { _categoryIds = ids; }

            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
#if NET48
                return _categoryIds.Contains(elem.Category.Id.IntegerValue);
#else
                return _categoryIds.Contains((int)elem.Category.Id.Value);
#endif
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}


