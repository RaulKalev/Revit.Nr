using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// ExternalEvent request that forces the active view to fully redraw.
    /// Works even with view templates applied by using a zoom-to-same-extents trick.
    /// </summary>
    public class RegenerateViewRequest : IExternalEventRequest
    {
        private readonly Action _callback;

        public RegenerateViewRequest(Action callback = null)
        {
            _callback = callback;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                var doc = uidoc.Document;

                // 1. Regenerate the document inside a transaction
                using (var t = new Transaction(doc, "DALI: Refresh View"))
                {
                    t.Start();
                    doc.Regenerate();
                    t.Commit();
                }

                // 2. Force visual redraw by re-zooming to the same extents.
                //    This works even with view templates applied (unlike detail level toggle).
                var uiViews = uidoc.GetOpenUIViews();
                var activeViewId = uidoc.ActiveView.Id;
                var uiView = uiViews.FirstOrDefault(v => v.ViewId == activeViewId);

                if (uiView != null)
                {
                    // Get current zoom corners (top-left and bottom-right in model coords)
                    var corners = uiView.GetZoomCorners();
                    if (corners != null && corners.Count >= 2)
                    {
                        // Re-zoom to exactly the same rectangle — forces a full repaint
                        uiView.ZoomAndCenterRectangle(corners[0], corners[1]);
                    }
                    else
                    {
                        uiView.ZoomToFit();
                    }
                }

                uidoc.RefreshActiveView();
                App.Logger?.Info("RegenerateView: view regenerated successfully.");
            }
            catch (Exception ex)
            {
                App.Logger?.Error($"RegenerateView: failed - {ex.Message}");
            }

            _callback?.Invoke();
        }
    }
}
