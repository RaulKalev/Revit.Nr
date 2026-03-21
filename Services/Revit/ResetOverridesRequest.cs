using Autodesk.Revit.UI;
using Renumber.Models;
using System;
using System.Linq;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that resets DALI view filter override graphics
    /// in the active view. Clears overrides to default for all tracked
    /// filters without removing the filters from the view.
    ///
    /// Threading: Execute() runs on the Revit API thread.
    /// The callback is marshalled to the WPF UI thread via Dispatcher.Invoke.
    /// </summary>
    public class ResetOverridesRequest : IExternalEventRequest
    {
        private readonly HighlightRegistry _registry;
        private readonly Action<ResetResult> _callback;

        public ResetOverridesRequest(HighlightRegistry registry, Action<ResetResult> callback)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Execute(UIApplication app)
        {
            var result = new ResetResult();

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    result.Message = "No active document.";
                    DispatchResult(result);
                    return;
                }

                var doc = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null)
                {
                    result.Message = "No active view.";
                    DispatchResult(result);
                    return;
                }

                // Get the view ID for registry lookup
#if NET48
                long viewIdVal = (long)view.Id.IntegerValue;
#else
                long viewIdVal = view.Id.Value;
#endif

                var trackedFilters = _registry.GetFiltersForView(viewIdVal);
                if (trackedFilters == null || trackedFilters.Count == 0)
                {
                    result.Success = true;
                    result.Message = $"No DALI overrides tracked in view '{view.Name}'.";
                    App.Logger?.Info($"ResetOverrides: no tracked filters in view '{view.Name}'.");
                    DispatchResult(result);
                    return;
                }

                App.Logger?.Info($"ResetOverrides: clearing {trackedFilters.Count} tracked filter(s) in view '{view.Name}'.");

                // Execute the reset inside a transaction
                var highlighter = new ViewFilterHighlighter();
                using (var trans = new Autodesk.Revit.DB.Transaction(doc, "DALI: Reset Overrides"))
                {
                    trans.Start();
                    result = highlighter.ResetHighlights(doc, view, trackedFilters, _registry);
                    trans.Commit();
                    App.Logger?.Info($"ResetOverrides: completed. Cleared: {result.ClearedCount}.");
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Error during reset: {ex.Message}";
                App.Logger?.Error("ResetOverrides: exception.", ex);
            }

            DispatchResult(result);
        }

        /// <summary>Marshals the result back to the WPF UI thread.</summary>
        private void DispatchResult(ResetResult result)
        {
            _callback(result);
        }
    }
}


