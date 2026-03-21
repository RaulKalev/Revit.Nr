using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Selection filter that restricts pick-selection to electrical fixture family instances
    /// (elements that can have an electrical circuit attached).
    /// The user selects fixtures; the plugin resolves their circuits.
    /// </summary>
    public sealed class ElectricalCircuitSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is FamilyInstance fi)
            {
                var mep = fi.MEPModel;
                return mep != null;
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
