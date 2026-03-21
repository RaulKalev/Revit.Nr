using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Renumber.Models
{
    public class SettingsModel
    {
        // 0. Settings Version for Migration
        public int Version { get; set; } = 4;

        // 1. Included Categories
        public List<BuiltInCategory> IncludedCategories { get; set; } = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_ElectricalFixtures
        };

        // 2. Parameter Mappings
        public string Param_Load { get; set; } = "Renumber mA";
        public string Param_AddressCount { get; set; } = "Renumber @";
        public string Param_LineId { get; set; } = "Renumber siin";
        public string Param_Controller { get; set; } = "Renumber kontroller";

        // 3. DALI Limits
        public double ControllerMaxLoadmA { get; set; } = 250.0;
        public int ControllerMaxAddressCount { get; set; } = 64;
        public double LineMaxLoadmA { get; set; } = 250.0;
        public int LineMaxAddressCount { get; set; } = 64;

        // 4. Persistence
        // DEPRECATED: Old flat list (kept for migration)
        public List<LineDefinition> SavedLines { get; set; } = new List<LineDefinition>();

        // NEW: Hierarchical structure
        public List<ControllerDefinition> SavedControllers { get; set; } = new List<ControllerDefinition>();

        // NEW: Top-level Panels
        public List<PanelDefinition> SavedPanels { get; set; } = new List<PanelDefinition>();
    }
}
