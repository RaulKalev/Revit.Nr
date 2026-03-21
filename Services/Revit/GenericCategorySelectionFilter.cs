using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Selection filter that restricts pick-selection to elements belonging to a specific Revit category.
    /// </summary>
    public sealed class GenericCategorySelectionFilter : ISelectionFilter
    {
        private readonly long _categoryId;

        public GenericCategorySelectionFilter(BuiltInCategory category)
        {
            _categoryId = (long)category;
        }

        public bool AllowElement(Element elem)
        {
            return elem?.Category != null && elem.Category.Id.Value == _categoryId;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
