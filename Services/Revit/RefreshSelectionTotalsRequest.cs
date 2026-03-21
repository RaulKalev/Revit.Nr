using Autodesk.Revit.UI;
using Renumber.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that reads the current UIDocument selection,
    /// computes DALI parameter totals via SelectionTotalsService, and
    /// returns the result to the UI thread via a dispatcher callback.
    /// 
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// </summary>
    public class RefreshSelectionTotalsRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly Action<SelectionTotalsResult> _callback;

        /// <summary>
        /// Creates a new refresh request.
        /// </summary>
        /// <param name="settings">Current settings with category filters and parameter names.</param>
        /// <param name="callback">Invoked on the UI thread with the computed result.</param>
        public RefreshSelectionTotalsRequest(SettingsModel settings, Action<SelectionTotalsResult> callback)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            SelectionTotalsResult result;

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result = new SelectionTotalsResult();
                    result.Warnings.Add("No active document.");
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;

                // Read the current selection from the UIDocument
                ICollection<Autodesk.Revit.DB.ElementId> selectedIds = uidoc.Selection.GetElementIds();

                App.Logger?.Info($"RefreshSelection: reading {selectedIds.Count} selected element(s).");

                // Compute totals using the stateless service
                var service = new SelectionTotalsService();
                result = service.ComputeSelectionTotals(doc, selectedIds, _settings);

                App.Logger?.Info($"RefreshSelection: done. Valid: {result.ValidElementCount}, Skipped: {result.SkippedElementCount}, Load: {result.TotalLoadmA:N1} mA, Addr: {result.TotalAddressCount}.");
            }
            catch (Exception ex)
            {
                result = new SelectionTotalsResult();
                result.Warnings.Add($"Error reading selection: {ex.Message}");
                App.Logger?.Error($"RefreshSelection: exception.", ex);
            }

            DispatchResult(result);
        }

        /// <summary>
        /// Marshals the result back to the WPF UI thread.
        /// </summary>
        private void DispatchResult(SelectionTotalsResult result)
        {
            _callback(result);
        }
    }
}


