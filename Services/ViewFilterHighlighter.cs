using Autodesk.Revit.DB;
using Renumber.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Renumber.Services
{
    /// <summary>
    /// Creates and manages DALI line view filters and override graphics.
    /// All methods must be called from a Revit API context (ExternalEvent handler)
    /// and within an active Transaction.
    ///
    /// Filter naming convention: "DALI_Line_{LineName}"
    /// Filter rule: Instance parameter (Settings.Param_LineId) EQUALS LineName
    /// Override: Projection line color set to a deterministic palette color
    ///
    /// The service is stateless. Filter tracking is managed by HighlightRegistry.
    /// </summary>
    public class ViewFilterHighlighter
    {
        // -------------------------------------------------------
        // Curated palette of 24 vivid, high-contrast colors.
        // Avoids near-black, near-white, and desaturated tones.
        // Hash(LineName) selects an initial index; collision
        // avoidance rotates to the next unused color in the view.
        // -------------------------------------------------------
        private static readonly byte[][] Palette = new byte[][]
        {
            new byte[] { 255,  87,  34 }, // Deep Orange
            new byte[] {  33, 150, 243 }, // Blue
            new byte[] {  76, 175,  80 }, // Green
            new byte[] { 156,  39, 176 }, // Purple
            new byte[] { 255, 193,   7 }, // Amber
            new byte[] {   0, 188, 212 }, // Cyan
            new byte[] { 244,  67,  54 }, // Red
            new byte[] {  63,  81, 181 }, // Indigo
            new byte[] { 139, 195,  74 }, // Light Green
            new byte[] { 233,  30,  99 }, // Pink
            new byte[] { 255, 152,   0 }, // Orange
            new byte[] {   0, 150, 136 }, // Teal
            new byte[] { 103,  58, 183 }, // Deep Purple
            new byte[] { 205, 220,  57 }, // Lime
            new byte[] {   3, 169, 244 }, // Light Blue
            new byte[] { 255, 235,  59 }, // Yellow
            new byte[] { 121,  85,  72 }, // Brown
            new byte[] {  96, 125, 139 }, // Blue Grey
            new byte[] { 183,  28,  28 }, // Dark Red
            new byte[] {  27,  94,  32 }, // Dark Green
            new byte[] {  13,  71, 161 }, // Dark Blue
            new byte[] { 230,  81,   0 }, // Dark Orange
            new byte[] {  74,  20, 140 }, // Very Deep Purple
            new byte[] {   0, 131, 143 }, // Dark Cyan
        };

        /// <summary>
        /// Applies a view filter highlight for the given DALI line in the active view.
        /// Must be called inside a Transaction.
        ///
        /// Steps:
        /// 1. Resolve or create the ParameterFilterElement
        /// 2. Assign it to the view with override graphics
        /// 3. Track via HighlightRegistry
        /// </summary>
        /// <param name="doc">Active Revit Document.</param>
        /// <param name="view">Active View to apply the filter to.</param>
        /// <param name="settings">Settings containing IncludedCategories and Param_LineId.</param>
        /// <param name="lineName">The line name to filter for (e.g., "Line 1").</param>
        /// <param name="registry">Session-scoped registry for tracking applied filters.</param>
        /// <returns>Result with success status and applied color.</returns>
        public HighlightResult ApplyLineHighlight(
            Document doc,
            View view,
            SettingsModel settings,
            string controllerName,
            string lineName,
            HighlightRegistry registry,
            string overrideColorHex = null)
        {
            var result = new HighlightResult();

            // --- Determine target for filter application ---
            // If the view has a template that controls filters, apply to the template instead.
            View filterTarget = view;
            bool usingTemplate = false;

            if (!view.AreGraphicsOverridesAllowed()
                && view.ViewTemplateId != null
                && view.ViewTemplateId != ElementId.InvalidElementId)
            {
                var template = doc.GetElement(view.ViewTemplateId) as View;
                if (template != null)
                {
                    filterTarget = template;
                    usingTemplate = true;
                    App.Logger?.Info($"ViewFilter: View '{view.Name}' uses template '{template.Name}' — applying filter to template.");
                }
                else
                {
                    result.Message = $"View '{view.Name}' does not allow graphic overrides and template could not be resolved.";
                    return result;
                }
            }

            string filterName = $"{controllerName} - {lineName}";
            string paramLineIdName = settings.Param_LineId;
            string paramControllerName = settings.Param_Controller;

            // --- Step 1: Query elements currently on this line and controller ---
            var includedCategoryIds = new List<ElementId>();
            foreach (var bic in settings.IncludedCategories)
            {
                includedCategoryIds.Add(new ElementId(bic));
            }

            if (includedCategoryIds.Count == 0)
            {
                result.Message = "No categories available to collect elements.";
                return result;
            }

            var elementsOnLine = new HashSet<ElementId>();
            var collector = new FilteredElementCollector(doc);
            
            // Build a category filter to speed up collection
            ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(includedCategoryIds);
            collector.WherePasses(categoryFilter).WhereElementIsNotElementType();

            foreach (Element e in collector)
            {
                Parameter lineParam = e.LookupParameter(paramLineIdName);
                if (lineParam == null) continue;

                string elemLineName = lineParam.StorageType == StorageType.String ? lineParam.AsString() : lineParam.AsValueString();
                if (elemLineName != lineName) continue;

                // Also match controller if configured
                if (!string.IsNullOrWhiteSpace(paramControllerName))
                {
                    Parameter ctrlParam = e.LookupParameter(paramControllerName);
                    if (ctrlParam != null)
                    {
                        string elemCtrlName = ctrlParam.StorageType == StorageType.String ? ctrlParam.AsString() : ctrlParam.AsValueString();
                        if (elemCtrlName != controllerName) continue;
                    }
                }

                elementsOnLine.Add(e.Id);
            }


            // --- Step 2: Find or create the SelectionFilterElement ---
            SelectionFilterElement filterElement = FindExistingSelectionFilter(doc, filterName);

            if (filterElement == null)
            {
                try
                {
                    filterElement = SelectionFilterElement.Create(doc, filterName);
                }
                catch (Exception ex)
                {
                    result.Message = $"Cannot create SelectionFilterElement '{filterName}': {ex.Message}";
                    App.Logger?.Error($"ViewFilter: failed to create selection filter: {ex.Message}");
                    return result;
                }
            }

            // Replace contents of the selection filter
            try
            {
                filterElement.Clear();
                if (elementsOnLine.Count > 0)
                {
                    filterElement.AddSet(elementsOnLine);
                }
            }
            catch (Exception ex)
            {
                result.Message = $"Failed to update contents of '{filterName}': {ex.Message}";
                return result;
            }

            App.Logger?.Info($"ViewFilter: using selection filter '{filterElement.Name}' (Id {filterElement.Id}) for {elementsOnLine.Count} elements.");

            // --- Step 3: Add filter to view if not already present ---
#if NET48
            long viewIdVal = (long)view.Id.IntegerValue;
            long filterIdVal = (long)filterElement.Id.IntegerValue;
#else
            long viewIdVal = view.Id.Value;
            long filterIdVal = filterElement.Id.Value;
#endif

            result.ViewIdValue = viewIdVal;
            result.FilterIdValue = filterIdVal;

            bool filterAlreadyInTarget = false;
            try
            {
                var existingFilters = filterTarget.GetFilters();
                foreach (var fId in existingFilters)
                {
#if NET48
                    if (fId.IntegerValue == filterElement.Id.IntegerValue)
#else
                    if (fId.Value == filterElement.Id.Value)
#endif
                    {
                        filterAlreadyInTarget = true;
                        break;
                    }
                }
            }
            catch
            {
                // View/template might not support GetFilters (rare edge case)
            }

            if (!filterAlreadyInTarget)
            {
                try
                {
                    filterTarget.AddFilter(filterElement.Id);
                    App.Logger?.Info($"ViewFilter: added filter '{filterName}' to {(usingTemplate ? "template" : "view")} '{filterTarget.Name}'.");
                }
                catch (Exception ex)
                {
                    result.Message += $"Cannot add selection filter to {(usingTemplate ? "template" : "view")}: {ex.Message}";
                    return result;
                }
            }

            // --- Step 4: Set override graphics with a deterministic palette color or explicitly provided color ---
            Color color;
            if (!string.IsNullOrWhiteSpace(overrideColorHex))
            {
                try
                {
                    // Format #RRGGBB => media color to revit color
                    var mediaColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(overrideColorHex);
                    color = new Color(mediaColor.R, mediaColor.G, mediaColor.B);
                }
                catch
                {
                    // Fallback to palette if invalid hex
                    color = PickColor(filterName, filterTarget, filterElement.Id);
                }
            }
            else
            {
                color = PickColor(filterName, filterTarget, filterElement.Id);
            }

            result.ColorUsed = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            // Keep overrides minimal: only projection line color

            try
            {
                filterTarget.SetFilterOverrides(filterElement.Id, ogs);
                filterTarget.SetFilterVisibility(filterElement.Id, true);
            }
            catch (Exception ex)
            {
                result.Message += $"Selection filter added but override failed: {ex.Message}";
                return result;
            }

            // --- Step 5: Track in registry ---
            registry.Track(viewIdVal, filterIdVal);

            result.Success = true;
            result.ElementsOnLine = elementsOnLine;
            result.Message += $"Highlighted {elementsOnLine.Count} items in '{filterName}' with color {result.ColorUsed}";
            if (usingTemplate) result.Message += $" (applied to template '{filterTarget.Name}')";
            result.Message += ".";
            return result;
        }

        /// <summary>
        /// Resets override graphics for all tracked DALI filters in a view.
        /// Clears overrides to default (empty OverrideGraphicSettings) but
        /// does NOT remove the filter from the view.
        /// Must be called inside a Transaction.
        /// </summary>
        public ResetResult ResetHighlights(
            Document doc,
            View view,
            IEnumerable<long> filterIdValues,
            HighlightRegistry registry)
        {
            var result = new ResetResult { Success = true };
            int cleared = 0;

#if NET48
            long viewIdVal = (long)view.Id.IntegerValue;
#else
            long viewIdVal = view.Id.Value;
#endif

            foreach (long filterIdVal in filterIdValues)
            {
#if NET48
                var filterId = new ElementId((int)filterIdVal);
#else
                var filterId = new ElementId(filterIdVal);
#endif

                try
                {
                    // Verify the filter still exists in the document
                    Element filterElem = doc.GetElement(filterId);
                    if (filterElem == null) continue;

                    // Check if the filter is currently applied to this view
                    var viewFilters = view.GetFilters();
                    bool isInView = false;
                    foreach (var vf in viewFilters)
                    {
#if NET48
                        if (vf.IntegerValue == filterId.IntegerValue)
#else
                        if (vf.Value == filterId.Value)
#endif
                        {
                            isInView = true;
                            break;
                        }
                    }

                    if (!isInView) continue;

                    // Clear overrides to empty (removes color/fill overrides)
                    view.SetFilterOverrides(filterId, new OverrideGraphicSettings());
                    cleared++;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message += $"Error clearing filter {filterIdVal}: {ex.Message}. ";
                }
            }

            // Clear tracking for this view
            registry.ClearView(viewIdVal);

            result.ClearedCount = cleared;
            if (result.Success)
            {
                result.Message = $"Reset overrides in '{view.Name}': cleared {cleared} filter(s).";
            }

            return result;
        }

        // -------------------------------------------------------
        // Private Helpers
        // -------------------------------------------------------

        /// <summary>
        /// Finds an existing SelectionFilterElement by name.
        /// </summary>
        private static SelectionFilterElement FindExistingSelectionFilter(Document doc, string filterName)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(SelectionFilterElement));

            foreach (Element e in collector)
            {
                if (e.Name == filterName && e is SelectionFilterElement sfe)
                    return sfe;
            }
            return null;
        }

        /// <summary>
        /// Picks a deterministic color from the palette for a line name.
        /// Uses hash-based index with collision avoidance:
        /// if another DALI filter in the same view already uses that exact color,
        /// rotates to the next palette entry.
        /// </summary>
        private static Color PickColor(string lineName, View view, ElementId currentFilterId)
        {
            int hash = Math.Abs(lineName.GetHashCode());
            int startIndex = hash % Palette.Length;

            // Collect colors already used by other DALI filters in this view
            var usedColors = new HashSet<int>(); // packed RGB
            try
            {
                foreach (var filterId in view.GetFilters())
                {
#if NET48
                    if (filterId.IntegerValue == currentFilterId.IntegerValue) continue;
#else
                    if (filterId.Value == currentFilterId.Value) continue;
#endif

                    var existingOgs = view.GetFilterOverrides(filterId);
                    if (existingOgs != null)
                    {
                        var c = existingOgs.ProjectionLineColor;
                        if (c.IsValid)
                        {
                            usedColors.Add((c.Red << 16) | (c.Green << 8) | c.Blue);
                        }
                    }
                }
            }
            catch
            {
                // If we can't read existing overrides, proceed without collision avoidance
            }

            // Pick the first unused color starting from the hash index
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = (startIndex + i) % Palette.Length;
                byte r = Palette[idx][0], g = Palette[idx][1], b = Palette[idx][2];
                int packed = (r << 16) | (g << 8) | b;

                if (!usedColors.Contains(packed))
                {
                    return new Color(r, g, b);
                }
            }

            // All palette colors in use, fall back to the hash-determined color
            byte fr = Palette[startIndex][0];
            byte fg = Palette[startIndex][1];
            byte fb = Palette[startIndex][2];
            return new Color(fr, fg, fb);
        }
    }
}
