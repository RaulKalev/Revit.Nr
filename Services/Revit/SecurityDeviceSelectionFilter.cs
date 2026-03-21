using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Selection filter that restricts pick-selection to security device family instances
    /// (elements in the OST_SecurityDevices built-in category).
    /// </summary>
    public sealed class SecurityDeviceSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is FamilyInstance fi && fi.Category != null)
            {
                return fi.Category.Id.Value == (long)BuiltInCategory.OST_SecurityDevices;
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
