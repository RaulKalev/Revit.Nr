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
    /// External event request for the Üld (general) mode.
    /// Lets the user pick elements of any category and write a named parameter
    /// with a value that auto-increments (integer +1) per pick.
    /// The written value = prefix + currentValue + suffix.
    /// Alt held suppresses incrementing for that pick.
    /// </summary>
    public sealed class UldParameterRequest : IExternalEventRequest
    {
        private readonly BuiltInCategory _category;
        private readonly string _parameterName;
        private readonly string _startValue;
        private readonly string _prefix;
        private readonly string _suffix;
        private readonly bool _goDown;
        private readonly Action<string, string> _onComplete;
        private readonly Action<IEnumerable<(string name, string value)>, int> _onStatusUpdate;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_MENU = 0x12;

        public UldParameterRequest(
            BuiltInCategory category,
            string parameterName,
            string startValue,
            string prefix,
            string suffix,
            bool goDown,
            Action<string, string> onComplete,
            Action<IEnumerable<(string name, string value)>, int> onStatusUpdate = null)
        {
            _category      = category;
            _parameterName = parameterName;
            _startValue    = startValue;
            _prefix        = prefix  ?? string.Empty;
            _suffix        = suffix  ?? string.Empty;
            _goDown        = goDown;
            _onComplete    = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onStatusUpdate = onStatusUpdate;
        }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                _onComplete("No active Revit document.", null);
                return;
            }

            var doc = uidoc.Document;
            string currentValue = _startValue;
            var pickLines = new List<string>();
            int totalWrites = 0;

            var filter = new GenericCategorySelectionFilter(_category);

            while (true)
            {
                Reference pickedRef;
                try
                {
                    string written = _prefix + currentValue + _suffix;
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        filter,
                        $"[Üld] Pick element  [{written}]  |  Alt = hold  |  Escape to finish");
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

                bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                var elem = doc.GetElement(pickedRef.ElementId);
                if (elem == null) continue;

                string written2 = _prefix + currentValue + _suffix;
                string elemName = elem.Name ?? $"id:{elem.Id}";

                using (var trans = new Transaction(doc, $"Üld Renumber: {written2}"))
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
                    else if (WriteParameter(param, written2, out string err))
                    {
                        line = $"{elemName}: {_parameterName} = {written2}";
                        totalWrites++;
                    }
                    else
                    {
                        line = $"{elemName}: {err}";
                    }

                    trans.Commit();
                    pickLines.Add(line);
                }

                if (!altHeld)
                {
                    if (int.TryParse(currentValue, out int iv))
                        currentValue = (iv + (_goDown ? -1 : 1)).ToString();
                }

                // Notify status window with the upcoming value
                _onStatusUpdate?.Invoke(
                    new[] { (_parameterName, _prefix + currentValue + _suffix) },
                    pickLines.Count);
            }

            string nextValue = currentValue != _startValue ? currentValue : null;

            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No elements were processed.");
            }
            else
            {
                sb.AppendLine($"Üld: wrote {totalWrites} value(s) across {pickLines.Count} element(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  • {line}");
            }

            _onComplete(sb.ToString().TrimEnd(), nextValue);
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
