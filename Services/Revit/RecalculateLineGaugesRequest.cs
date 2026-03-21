using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;
using Renumber.Services.Core;
using System;
using System.Collections.Generic;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that scans elements for a specific line name
    /// and recalculates the total load (mA) and address count on-demand.
    /// Used when a line is renamed in the UI to instantly pull existing elements.
    /// </summary>
    public class RecalculateLineGaugesRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _targetLineName;
        private readonly string _targetControllerName;
        private readonly Action<double, int> _callback;

        public RecalculateLineGaugesRequest(
            SettingsModel settings,
            string targetLineName,
            string targetControllerName,
            Action<double, int> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _targetLineName = targetLineName;
            _targetControllerName = targetControllerName;
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            double totalLoad = 0;
            int totalAddress = 0;

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    DispatchResult(0, 0);
                    return;
                }

                var doc = uidoc.Document;

                var includedCategoryIds = new List<ElementId>();
                foreach (var bic in _settings.IncludedCategories)
                    includedCategoryIds.Add(new ElementId(bic));

                if (includedCategoryIds.Count == 0 || string.IsNullOrWhiteSpace(_settings.Param_LineId))
                {
                    DispatchResult(0, 0);
                    return;
                }

                var collector = new FilteredElementCollector(doc);
                ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(includedCategoryIds);
                collector.WherePasses(categoryFilter).WhereElementIsNotElementType();

                var typeCache = new Dictionary<long, CachedType>();

                foreach (Element element in collector)
                {
                    if (element is RevitLinkInstance) continue;

                    // Match Line Name
                    Parameter lineParam = element.LookupParameter(_settings.Param_LineId);
                    if (lineParam == null) continue;

                    string elemLineName = lineParam.StorageType == StorageType.String ? lineParam.AsString() : lineParam.AsValueString();
                    if (!string.Equals(elemLineName?.Trim(), _targetLineName?.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Match Controller Name (if configured)
                    if (!string.IsNullOrWhiteSpace(_settings.Param_Controller) && !string.IsNullOrWhiteSpace(_targetControllerName))
                    {
                        Parameter ctrlParam = element.LookupParameter(_settings.Param_Controller);
                        if (ctrlParam != null)
                        {
                            string elemCtrlName = ctrlParam.StorageType == StorageType.String ? ctrlParam.AsString() : ctrlParam.AsValueString();
                            if (!string.Equals(elemCtrlName?.Trim(), _targetControllerName?.Trim(), StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                    }

                    // Get totals from Instance, fallback to Type
                    var data = ReadParams(doc, element);

                    totalLoad += data.LoadmA;
                    totalAddress += data.AddressCount;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error($"RecalculateLineGaugesRequest failed for '{_targetLineName}': {ex.Message}");
            }

            DispatchResult(totalLoad, totalAddress);
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

        private void DispatchResult(double totalLoad, int totalAddress)
        {
            _callback(totalLoad, totalAddress);
        }

        private class CachedType
        {
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
        }
    }
}


