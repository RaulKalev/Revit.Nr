using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that collects all FamilySymbol types from the included
    /// categories and reads their mapped DALI parameters. Returns a list of DTOs
    /// for display in the Batch Setup DataGrid.
    /// </summary>
    public class CollectBatchSetupRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly Action<List<BatchSetupRowDto>> _callback;

        /// <summary>
        /// Creates a new collection request.
        /// </summary>
        /// <param name="settings">Current settings (categories + parameter names).</param>
        /// <param name="callback">Invoked on the UI thread with the collected rows.</param>
        public CollectBatchSetupRequest(SettingsModel settings, Action<List<BatchSetupRowDto>> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var rows = new List<BatchSetupRowDto>();

            try
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    DispatchResults(rows);
                    return;
                }

                // Build a multi-category filter from included categories
                var categoryFilters = new List<ElementFilter>();
                foreach (var bic in _settings.IncludedCategories)
                {
                    categoryFilters.Add(new ElementCategoryFilter(bic));
                }

                if (categoryFilters.Count == 0)
                {
                    DispatchResults(rows);
                    return;
                }

                var combinedFilter = categoryFilters.Count == 1
                    ? categoryFilters[0]
                    : new LogicalOrFilter(categoryFilters);

                // Collect all FamilySymbols matching the categories
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WherePasses(combinedFilter)
                    .Cast<FamilySymbol>()
                    .OrderBy(s => s.Category?.Name ?? "")
                    .ThenBy(s => s.FamilyName)
                    .ThenBy(s => s.Name)
                    .ToList();

                foreach (var symbol in symbols)
                {
                    try
                    {
                        var row = CreateRowFromSymbol(symbol);
                        rows.Add(row);
                    }
                    catch
                    {
                        // Skip symbols that cannot be read (null, corrupt, etc.)
                    }
                }
            }
            catch
            {
                // If collection fails entirely, return whatever we have
            }

            DispatchResults(rows);
        }

        /// <summary>
        /// Creates a DTO row from a single FamilySymbol, reading the mapped parameters.
        /// </summary>
        private BatchSetupRowDto CreateRowFromSymbol(FamilySymbol symbol)
        {
            string categoryName = symbol.Category?.Name ?? "(no category)";
            string familyName = symbol.FamilyName ?? "(no family)";
            string typeName = symbol.Name ?? "(no name)";

            // Read ElementId as long for cross-version compatibility
#if NET48
            long symbolId = (long)symbol.Id.IntegerValue;
#else
            long symbolId = symbol.Id.Value;
#endif

            var row = new BatchSetupRowDto
            {
                Category = categoryName,
                FamilyName = familyName,
                TypeName = typeName,
                SymbolId = symbolId,
                Status = BatchRowStatus.OK
            };

            // Read mA Load parameter
            bool loadParamFound = TryReadDoubleParameter(symbol, _settings.Param_Load, out double? loadValue);

            // Read Address Count parameter
            bool addrParamFound = TryReadIntParameter(symbol, _settings.Param_AddressCount, out int? addrValue);

            // Set current values (null if parameter missing)
            row.Current_mA_Load = loadValue;
            row.Current_AddressCount = addrValue;

            // Initialize editable values to current values
            row.Editable_mA_Load = loadValue;
            row.Editable_AddressCount = addrValue;

            // Mark status if any mapped parameter is missing
            if (!loadParamFound || !addrParamFound)
            {
                row.Status = BatchRowStatus.MissingParam;
                var missing = new List<string>();
                if (!loadParamFound) missing.Add(_settings.Param_Load);
                if (!addrParamFound) missing.Add(_settings.Param_AddressCount);
                row.ErrorMessage = "Missing: " + string.Join(", ", missing);
            }

            return row;
        }

        /// <summary>
        /// Attempts to read a double (or int-stored) parameter value from a FamilySymbol.
        /// Returns false if the parameter does not exist on the element.
        /// </summary>
        private bool TryReadDoubleParameter(FamilySymbol symbol, string paramName, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter param = symbol.LookupParameter(paramName);
            if (param == null) return false;

            try
            {
                if (param.StorageType == StorageType.Double)
                {
                    value = param.AsDouble();
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    value = (double)param.AsInteger();
                }
                else if (param.StorageType == StorageType.String)
                {
                    string raw = param.AsString();
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        value = parsed;
                    }
                }
                return true; // Parameter exists, even if value is null/default
            }
            catch
            {
                return true; // Parameter exists but could not be read
            }
        }

        /// <summary>
        /// Attempts to read an integer parameter value from a FamilySymbol.
        /// Returns false if the parameter does not exist on the element.
        /// </summary>
        private bool TryReadIntParameter(FamilySymbol symbol, string paramName, out int? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter param = symbol.LookupParameter(paramName);
            if (param == null) return false;

            try
            {
                if (param.StorageType == StorageType.Integer)
                {
                    value = param.AsInteger();
                }
                else if (param.StorageType == StorageType.Double)
                {
                    value = (int)param.AsDouble();
                }
                else if (param.StorageType == StorageType.String)
                {
                    string raw = param.AsString();
                    if (int.TryParse(raw, out int parsed))
                    {
                        value = parsed;
                    }
                }
                return true; // Parameter exists
            }
            catch
            {
                return true; // Parameter exists but could not be read
            }
        }

        /// <summary>
        /// Dispatches results back to the UI thread via Application.Current.Dispatcher.
        /// </summary>
        private void DispatchResults(List<BatchSetupRowDto> rows)
        {
            _callback(rows);
        }
    }
}


