using Autodesk.Revit.DB;
using Renumber.Models;
using Renumber.Services.Core;
using System;
using System.Linq;

namespace Renumber.Services
{
    public class ParameterResolver
    {
        private readonly ILogger _logger;

        public ParameterResolver(ILogger logger)
        {
            _logger = logger;
        }

        public ValidationResult ValidateSettings(Document doc, SettingsModel settings)
        {
            var result = new ValidationResult { IsValid = true };

            try
            {
                // Validate Type Parameters
                ValidateParameter(doc, settings.Param_Load, true, settings, result);
                ValidateParameter(doc, settings.Param_AddressCount, true, settings, result);

                // Validate Instance Parameter
                ValidateParameter(doc, settings.Param_LineId, false, settings, result);
                ValidateParameter(doc, settings.Param_Controller, false, settings, result);
            }
            catch (Exception ex)
            {
                result.AddError($"Validation failed with exception: {ex.Message}");
                _logger.Error("Validation error", ex);
            }

            return result;
        }

        private void ValidateParameter(Document doc, string paramName, bool isTypeParam, SettingsModel settings, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(paramName))
            {
                result.AddError($"Parameter name cannot be empty.");
                return;
            }

            bool found = false;

            // Check if parameter exists on any of the included categories
            foreach (var categoryEnum in settings.IncludedCategories)
            {
                var category = Category.GetCategory(doc, categoryEnum);
                if (category == null) continue;

                var collector = new FilteredElementCollector(doc).OfCategoryId(category.Id);
                
                if (isTypeParam)
                {
                    collector.WhereElementIsElementType();
                }
                else
                {
                    collector.WhereElementIsNotElementType();
                }

                var firstElement = collector.FirstElement();
                if (firstElement == null) continue;

                Parameter param = firstElement.LookupParameter(paramName);
                if (param != null)
                {
                    found = true;
                    // Optional: Check StorageType here if required strict type validation
                    break;
                }
            }

            if (found)
            {
                result.AddSuccess($"Parameter '{paramName}' found.");
            }
            else
            {
                result.AddError($"Parameter '{paramName}' not found on any included categories (IsType: {isTypeParam}).");
            }
        }
    }
}
