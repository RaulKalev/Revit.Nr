using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Renumber.Models;

namespace Renumber.Services.Revit
{
    public class UpdateLineColorRequest : IExternalEventRequest
    {
        private readonly SettingsModel _settings;
        private readonly string _controllerName;
        private readonly string _lineName;
        private readonly string _hexColor;
        private readonly HighlightRegistry _highlightRegistry;

        public UpdateLineColorRequest(
            SettingsModel settings, 
            string controllerName,
            string lineName, 
            string hexColor,
            HighlightRegistry highlightRegistry)
        {
            _settings = settings;
            _controllerName = controllerName;
            _lineName = lineName;
            _hexColor = hexColor;
            _highlightRegistry = highlightRegistry;
        }

        public string GetName() => "Update Line Color";

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            var view = app.ActiveUIDocument?.ActiveGraphicalView;
            if (doc == null || view == null) return;

            using (Transaction t = new Transaction(doc, "DALI: Update Line Color"))
            {
                t.Start();
                new ViewFilterHighlighter().ApplyLineHighlight(doc, view, _settings, _controllerName, _lineName, _highlightRegistry, _hexColor);
                t.Commit();
            }
        }
    }
}
