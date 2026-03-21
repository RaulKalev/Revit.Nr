using Autodesk.Revit.DB;
using Renumber.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services
{
    /// <summary>
    /// Computes aggregate DALI parameter totals for a set of selected Revit elements.
    /// Must be called from inside a Revit External Event handler (Revit API context).
    /// 
    /// Threading: This service is stateless and runs entirely on the Revit API thread.
    /// It does NOT touch UI elements. Results must be marshalled to UI thread by the caller.
    /// </summary>
    public class SelectionTotalsService
    {
        /// <summary>
        /// Computes selection totals for the given element IDs.
        /// </summary>
        /// <param name="doc">Active Revit Document (must be accessed from Revit API thread).</param>
        /// <param name="selectedIds">ElementIds from the current UIDocument.Selection.</param>
        /// <param name="settings">Current settings with category filters and parameter names.</param>
        /// <returns>Aggregated totals and warnings.</returns>
        public SelectionTotalsResult ComputeSelectionTotals(
            Document doc,
            ICollection<ElementId> selectedIds,
            SettingsModel settings)
        {
            var result = new SelectionTotalsResult();

            if (doc == null || selectedIds == null || selectedIds.Count == 0)
            {
                return result;
            }

            // Build a HashSet of included BuiltInCategory integers for fast lookup
            var includedCategoryIds = new HashSet<int>();
            foreach (var bic in settings.IncludedCategories)
            {
                includedCategoryIds.Add((int)bic);
            }

            // Cache: FamilySymbol ElementId -> (loadValue, addrValue, warningMsg)
            // Avoids repeated LookupParameter calls for instances sharing the same type.
            var typeCache = new Dictionary<long, CachedTypeParams>();

            foreach (var eid in selectedIds)
            {
                Element element;
                try
                {
                    element = doc.GetElement(eid);
                }
                catch
                {
                    result.SkippedElementCount++;
                    result.Warnings.Add($"Could not resolve element ID {eid}.");
                    continue;
                }

                if (element == null)
                {
                    result.SkippedElementCount++;
                    continue;
                }

                // --- Filter: skip element types (only process instances) ---
                if (element is ElementType)
                {
                    result.SkippedElementCount++;
                    continue;
                }

                // --- Filter: skip linked model elements ---
                if (element is RevitLinkInstance)
                {
                    result.SkippedElementCount++;
                    result.Warnings.Add($"Skipped linked element: {element.Name}");
                    continue;
                }

                // --- Filter: check category is in IncludedCategories ---
                Category cat = element.Category;
                if (cat == null)
                {
                    result.SkippedElementCount++;
                    result.Warnings.Add($"Skipped element without category: ID {eid}");
                    continue;
                }

#if NET48
                int catIdInt = cat.Id.IntegerValue;
#else
                int catIdInt = (int)cat.Id.Value;
#endif

                if (!includedCategoryIds.Contains(catIdInt))
                {
                    result.SkippedElementCount++;
                    // Not a warning -- simply not in included categories
                    continue;
                }

                // --- Resolve the FamilySymbol (type) ---
                ElementId typeId = element.GetTypeId();
#if NET48
                if (typeId == null || typeId == ElementId.InvalidElementId)
#else
                if (typeId == ElementId.InvalidElementId)
#endif
                {
                    result.SkippedElementCount++;
                    result.Warnings.Add($"Element '{element.Name}' (ID {eid}) has no type.");
                    continue;
                }

                // --- Use the type cache for parameter reading ---
#if NET48
                long typeIdKey = (long)typeId.IntegerValue;
#else
                long typeIdKey = typeId.Value;
#endif

                if (!typeCache.TryGetValue(typeIdKey, out var cached))
                {
                    cached = ReadTypeParams(doc, typeId, settings);
                    typeCache[typeIdKey] = cached;
                }

                // If this type had a warning, accumulate it once
                if (cached.Warning != null && !result.Warnings.Contains(cached.Warning))
                {
                    result.Warnings.Add(cached.Warning);
                }

                if (!cached.IsValid)
                {
                    result.SkippedElementCount++;
                    continue;
                }

                // Accumulate totals
                result.TotalLoadmA += cached.LoadmA;
                result.TotalAddressCount += cached.AddressCount;
                result.ValidElementCount++;
            }

            return result;
        }

        /// <summary>
        /// Reads DALI parameters from a FamilySymbol and caches the result.
        /// </summary>
        private CachedTypeParams ReadTypeParams(Document doc, ElementId typeId, SettingsModel settings)
        {
            var cached = new CachedTypeParams();

            Element typeElement = doc.GetElement(typeId);
            if (typeElement == null || !(typeElement is FamilySymbol symbol))
            {
                cached.Warning = $"Type ID {typeId} is not a FamilySymbol.";
                return cached;
            }

            string typeName = $"{symbol.FamilyName} : {symbol.Name}";
            bool hasLoad = false;
            bool hasAddr = false;

            // --- Read mA Load ---
            if (!string.IsNullOrWhiteSpace(settings.Param_Load))
            {
                Parameter loadParam = symbol.LookupParameter(settings.Param_Load);
                if (loadParam == null)
                {
                    cached.Warning = $"Type '{typeName}': missing parameter '{settings.Param_Load}'.";
                }
                else
                {
                    hasLoad = true;
                    cached.LoadmA = ReadDoubleValue(loadParam, settings.Param_Load, typeName, cached);
                }
            }

            // --- Read Address Count ---
            if (!string.IsNullOrWhiteSpace(settings.Param_AddressCount))
            {
                Parameter addrParam = symbol.LookupParameter(settings.Param_AddressCount);
                if (addrParam == null)
                {
                    string msg = $"Type '{typeName}': missing parameter '{settings.Param_AddressCount}'.";
                    cached.Warning = cached.Warning != null ? cached.Warning + " | " + msg : msg;
                }
                else
                {
                    hasAddr = true;
                    cached.AddressCount = ReadIntValue(addrParam, settings.Param_AddressCount, typeName, cached);
                }
            }

            // Mark valid only if at least one parameter was found
            cached.IsValid = hasLoad || hasAddr;

            return cached;
        }

        /// <summary>
        /// Safely reads a double value from a parameter, handling various storage types.
        /// </summary>
        private double ReadDoubleValue(Parameter param, string paramName, string typeName, CachedTypeParams cached)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Double:
                        return param.AsDouble();
                    case StorageType.Integer:
                        return (double)param.AsInteger();
                    case StorageType.String:
                        string raw = param.AsString();
                        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                            return parsed;
                        cached.Warning = $"Type '{typeName}': '{paramName}' has non-numeric string value.";
                        return 0.0;
                    default:
                        cached.Warning = $"Type '{typeName}': '{paramName}' has unsupported storage type '{param.StorageType}'.";
                        return 0.0;
                }
            }
            catch
            {
                cached.Warning = $"Type '{typeName}': error reading '{paramName}'.";
                return 0.0;
            }
        }

        /// <summary>
        /// Safely reads an integer value from a parameter, handling various storage types.
        /// </summary>
        private int ReadIntValue(Parameter param, string paramName, string typeName, CachedTypeParams cached)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Integer:
                        return param.AsInteger();
                    case StorageType.Double:
                        return (int)param.AsDouble();
                    case StorageType.String:
                        string raw = param.AsString();
                        if (int.TryParse(raw, out int parsed))
                            return parsed;
                        cached.Warning = $"Type '{typeName}': '{paramName}' has non-numeric string value.";
                        return 0;
                    default:
                        cached.Warning = $"Type '{typeName}': '{paramName}' has unsupported storage type '{param.StorageType}'.";
                        return 0;
                }
            }
            catch
            {
                cached.Warning = $"Type '{typeName}': error reading '{paramName}'.";
                return 0;
            }
        }

        /// <summary>
        /// Internal cache entry for a single FamilySymbol's DALI parameter values.
        /// </summary>
        private class CachedTypeParams
        {
            public bool IsValid { get; set; }
            public double LoadmA { get; set; }
            public int AddressCount { get; set; }
            public string Warning { get; set; }
        }
    }
}
