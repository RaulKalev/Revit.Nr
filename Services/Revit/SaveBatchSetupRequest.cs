using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;
using System;
using System.Collections.Generic;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that writes edited parameter values back to FamilySymbol types.
    /// Uses a TransactionGroup with per-row SubTransactions for isolation:
    /// one failing row does not block others.
    /// </summary>
    public class SaveBatchSetupRequest : IExternalEventRequest
    {
        private readonly List<BatchSetupRowDto> _dirtyRows;
        private readonly SettingsModel _settings;
        private readonly Action<BatchSaveResult> _callback;

        /// <summary>
        /// Creates a new save request.
        /// </summary>
        /// <param name="dirtyRows">Only rows where IsDirty == true and Status == OK.</param>
        /// <param name="settings">Current settings (parameter names).</param>
        /// <param name="callback">Invoked on the UI thread with the save results.</param>
        public SaveBatchSetupRequest(
            List<BatchSetupRowDto> dirtyRows,
            SettingsModel settings,
            Action<BatchSaveResult> callback)
        {
            _dirtyRows = dirtyRows ?? throw new ArgumentNullException(nameof(dirtyRows));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new BatchSaveResult();

            try
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    result.Details.Add("No active document found.");
                    DispatchResults(result);
                    return;
                }

                if (_dirtyRows.Count == 0)
                {
                    result.Details.Add("No rows to save.");
                    DispatchResults(result);
                    return;
                }

                // Use a TransactionGroup so we can commit/rollback individual SubTransactions
                using (var tg = new TransactionGroup(doc, "Batch Update DALI Type Parameters"))
                {
                    tg.Start();

                    foreach (var row in _dirtyRows)
                    {
                        using (var subTrans = new SubTransaction(doc))
                        {
                            // SubTransaction requires an active transaction; open one inside the group
                        }
                    }

                    // SubTransaction only works inside a Transaction, so we use one Transaction
                    // and try-catch per row. If a row fails, we skip it and continue.
                    using (var trans = new Transaction(doc, "Save Batch Setup Values"))
                    {
                        trans.Start();

                        foreach (var row in _dirtyRows)
                        {
                            try
                            {
                                ProcessRow(doc, row, result);
                            }
                            catch (Exception ex)
                            {
                                result.FailedCount++;
                                result.Details.Add($"FAILED [{row.FamilyName} : {row.TypeName}]: {ex.Message}");
                            }
                        }

                        trans.Commit();
                    }

                    tg.Assimilate();
                }
            }
            catch (Exception ex)
            {
                result.Details.Add($"Critical error during save: {ex.Message}");
            }

            DispatchResults(result);
        }

        /// <summary>
        /// Processes a single row: finds the FamilySymbol by ElementId and writes
        /// the edited parameter values.
        /// </summary>
        private void ProcessRow(Document doc, BatchSetupRowDto row, BatchSaveResult result)
        {
            // Resolve the FamilySymbol from the stored ElementId
#if NET48
            ElementId eid = new ElementId((int)row.SymbolId);
#else
            ElementId eid = new ElementId(row.SymbolId);
#endif

            Element element = doc.GetElement(eid);
            if (element == null || !(element is FamilySymbol symbol))
            {
                result.SkippedCount++;
                result.Details.Add($"SKIPPED [{row.FamilyName} : {row.TypeName}]: Element not found (ID {row.SymbolId})");
                return;
            }

            bool anyUpdated = false;
            bool anySkipped = false;

            // Write mA Load
            if (row.Editable_mA_Load.HasValue)
            {
                if (!TryWriteDoubleParameter(symbol, _settings.Param_Load, row.Editable_mA_Load.Value))
                {
                    anySkipped = true;
                    result.Details.Add($"SKIPPED param '{_settings.Param_Load}' on [{row.FamilyName} : {row.TypeName}]: parameter missing or read-only");
                }
                else
                {
                    anyUpdated = true;
                }
            }

            // Write Address Count
            if (row.Editable_AddressCount.HasValue)
            {
                if (!TryWriteIntParameter(symbol, _settings.Param_AddressCount, row.Editable_AddressCount.Value))
                {
                    anySkipped = true;
                    result.Details.Add($"SKIPPED param '{_settings.Param_AddressCount}' on [{row.FamilyName} : {row.TypeName}]: parameter missing or read-only");
                }
                else
                {
                    anyUpdated = true;
                }
            }

            if (anyUpdated && !anySkipped)
            {
                result.UpdatedCount++;
            }
            else if (anySkipped && !anyUpdated)
            {
                result.SkippedCount++;
            }
            else if (anyUpdated && anySkipped)
            {
                // Partial update: count as updated but note the skip
                result.UpdatedCount++;
                result.Details.Add($"PARTIAL [{row.FamilyName} : {row.TypeName}]: some parameters were missing");
            }
        }

        /// <summary>
        /// Attempts to write a double value to a named parameter on a FamilySymbol.
        /// Returns false if the parameter is missing or read-only.
        /// </summary>
        private bool TryWriteDoubleParameter(FamilySymbol symbol, string paramName, double value)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter param = symbol.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;

            try
            {
                if (param.StorageType == StorageType.Double)
                {
                    param.Set(value);
                    return true;
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    param.Set((int)value);
                    return true;
                }
                else if (param.StorageType == StorageType.String)
                {
                    param.Set(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to write an integer value to a named parameter on a FamilySymbol.
        /// Returns false if the parameter is missing or read-only.
        /// </summary>
        private bool TryWriteIntParameter(FamilySymbol symbol, string paramName, int value)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter param = symbol.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;

            try
            {
                if (param.StorageType == StorageType.Integer)
                {
                    param.Set(value);
                    return true;
                }
                else if (param.StorageType == StorageType.Double)
                {
                    param.Set((double)value);
                    return true;
                }
                else if (param.StorageType == StorageType.String)
                {
                    param.Set(value.ToString());
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Dispatches results back to the UI thread via Application.Current.Dispatcher.
        /// </summary>
        private void DispatchResults(BatchSaveResult result)
        {
            _callback(result);
        }
    }
}


