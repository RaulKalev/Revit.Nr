using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// External event request for the Heli mode.
    /// Lets the user pick elements of a chosen category and alternately writes
    /// Value1 and Value2 to the specified parameter with each pick.
    /// </summary>
    public sealed class HeliParameterRequest : IExternalEventRequest
    {
        private readonly BuiltInCategory _category;
        private readonly string _parameterName;
        private readonly string _value1;
        private readonly string _value2;
        private readonly Action<string> _onComplete;
        private readonly Action<IEnumerable<(string name, string value)>, int> _onStatusUpdate;

        public HeliParameterRequest(
            BuiltInCategory category,
            string parameterName,
            string value1,
            string value2,
            Action<string> onComplete,
            Action<IEnumerable<(string name, string value)>, int> onStatusUpdate = null)
        {
            _category        = category;
            _parameterName   = parameterName;
            _value1          = value1  ?? string.Empty;
            _value2          = value2  ?? string.Empty;
            _onComplete      = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onStatusUpdate  = onStatusUpdate;
        }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                _onComplete("No active Revit document.");
                return;
            }

            var doc = uidoc.Document;
            var filter = new GenericCategorySelectionFilter(_category);
            var pickLines = new List<string>();
            int totalWrites = 0;
            bool useValue1 = true;

            while (true)
            {
                string currentValue = useValue1 ? _value1 : _value2;
                Reference pickedRef;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        filter,
                        $"[Heli] Pick element  [{currentValue}]  |  Escape to finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    pickLines.Add($"Selection error: {ex.Message}");
                    break;
                }

                var elem = doc.GetElement(pickedRef.ElementId);
                if (elem == null) continue;

                string elemName = elem.Name ?? $"id:{elem.Id}";

                using (var trans = new Transaction(doc, $"Heli: {_parameterName} = {currentValue}"))
                {
                    trans.Start();

                    Parameter param = elem.LookupParameter(_parameterName);
                    string line;

                    if (param == null)
                    {
                        line = $"{elemName}: parameter '{_parameterName}' not found";
                    }
                    else if (param.IsReadOnly)
                    {
                        line = $"{elemName}: parameter '{_parameterName}' is read-only";
                    }
                    else if (WriteParameter(param, currentValue, out string err))
                    {
                        line = $"{elemName}: {_parameterName} = {currentValue}";
                        totalWrites++;
                        useValue1 = !useValue1;
                    }
                    else
                    {
                        line = $"{elemName}: {err}";
                    }

                    trans.Commit();
                    pickLines.Add(line);
                }

                // Notify status window with the next value
                string nextDisplay = useValue1 ? _value1 : _value2;
                _onStatusUpdate?.Invoke(
                    new[] { (_parameterName, nextDisplay) },
                    pickLines.Count);
            }

            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No elements were processed.");
            }
            else
            {
                sb.AppendLine($"Heli: wrote {totalWrites} value(s) across {pickLines.Count} pick(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  • {line}");
            }

            _onComplete(sb.ToString().TrimEnd());
        }

        private static bool WriteParameter(Parameter param, string value, out string error)
        {
            error = null;
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    return true;
                case StorageType.Integer:
                    if (int.TryParse(value, out int intVal)) { param.Set(intVal); return true; }
                    error = $"cannot parse '{value}' as integer."; return false;
                case StorageType.Double:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
                    { param.Set(dblVal); return true; }
                    error = $"cannot parse '{value}' as a number."; return false;
                default:
                    error = $"unsupported storage type '{param.StorageType}'."; return false;
            }
        }
    }
}
