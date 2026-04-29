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
    /// External event request for ATS mode.
    /// Same as Üld but the written number is left-padded with a fill character
    /// to a fixed total character width before the suffix is appended.
    /// Example: value=1, charCount=3, prefix='0', suffix="" → writes "001".
    /// </summary>
    public sealed class AtsParameterRequest : IExternalEventRequest
    {
        private readonly BuiltInCategory _category;
        private readonly string _parameterName;
        private readonly string _startValue;
        private readonly int    _charCount;
        private readonly string _fillStr;
        private readonly string _suffix;
        private readonly string _parameterName2;
        private readonly string _fixedValue;
        private readonly bool   _goDown;
        private readonly bool   _freeze;
        private readonly Action<string, string> _onComplete;
        private readonly Action<IEnumerable<(string name, string value)>, int> _onStatusUpdate;
        private readonly Action<Action<int>> _registerNudge;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_MENU = 0x12;
        private const int VK_UP   = 0x26;
        private const int VK_DOWN = 0x28;

        public AtsParameterRequest(
            BuiltInCategory category,
            string parameterName,
            string startValue,
            int charCount,
            string fillStr,
            string suffix,
            string parameterName2,
            string fixedValue,
            bool goDown,
            bool freeze,
            Action<string, string> onComplete,
            Action<IEnumerable<(string name, string value)>, int> onStatusUpdate = null,
            Action<Action<int>> registerNudge = null)
        {
            _category       = category;
            _parameterName  = parameterName;
            _startValue     = startValue;
            _charCount      = charCount;
            _fillStr        = fillStr        ?? string.Empty;
            _suffix         = suffix         ?? string.Empty;
            _parameterName2 = parameterName2 ?? string.Empty;
            _fixedValue     = fixedValue     ?? string.Empty;
            _goDown         = goDown;
            _freeze         = freeze;
            _onComplete    = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onStatusUpdate = onStatusUpdate;
            _registerNudge  = registerNudge;
        }

        /// <summary>
        /// Pads <paramref name="numericValue"/> on the left with the first character of
        /// <paramref name="fillStr"/> to reach <paramref name="charCount"/> total characters,
        /// then appends <paramref name="suffix"/>.
        /// </summary>
        private string FormatValue(string numericValue)
        {
            string padded = (_charCount > 0 && !string.IsNullOrEmpty(_fillStr))
                ? numericValue.PadLeft(_charCount, _fillStr[0])
                : numericValue;
            return padded + _suffix;
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
            bool isTextNote = string.IsNullOrEmpty(_parameterName);
            string currentValue = _startValue;
            var pickLines = new List<string>();
            int totalWrites = 0;

            var filter = new GenericCategorySelectionFilter(_category);

            // Mutable wrapper so the nudge closure can update currentValue in place
            string[] curVal = { currentValue };
            string statusNameForNudge = isTextNote ? "Text" : _parameterName;
            _registerNudge?.Invoke(delta =>
            {
                if (int.TryParse(curVal[0], out int sv))
                {
                    curVal[0] = (sv + delta).ToString();
                    _onStatusUpdate?.Invoke(
                        new[] { (statusNameForNudge, FormatValue(curVal[0])) },
                        pickLines.Count);
                }
            });

            while (true)
            {
                currentValue = curVal[0];
                Reference pickedRef;
                try
                {
                    string written = FormatValue(currentValue);
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        filter,
                        $"[ATS] Pick element  [{written}]  |  Alt = hold  |  Escape to finish");
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

                currentValue = curVal[0];
                bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                var elem = doc.GetElement(pickedRef.ElementId);
                if (elem == null) continue;

                string written2 = FormatValue(currentValue);
                string elemName = elem.Name ?? $"id:{elem.Id}";

                using (var trans = new Transaction(doc, $"ATS Renumber: {written2}"))
                {
                    trans.Start();

                    string line;
                    if (isTextNote)
                    {
                        if (elem is TextNote tn)
                        {
                            tn.Text = written2;
                            line = $"id:{elem.Id}: Text = {written2}";
                            totalWrites++;
                        }
                        else
                        {
                            line = $"{elemName}: element is not a TextNote";
                        }

                        // Write second parameter with fixed value if configured
                        if (!string.IsNullOrEmpty(_parameterName2))
                        {
                            Parameter param2 = elem.LookupParameter(_parameterName2);
                            if (param2 != null && !param2.IsReadOnly)
                                WriteParameter(param2, _fixedValue, out _);
                        }
                    }
                    else
                    {
                        Parameter param = elem.LookupParameter(_parameterName);
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

                        // Write second parameter with fixed value if configured
                        if (!string.IsNullOrEmpty(_parameterName2))
                        {
                            Parameter param2 = elem.LookupParameter(_parameterName2);
                            if (param2 != null && !param2.IsReadOnly)
                                WriteParameter(param2, _fixedValue, out _);
                        }
                    }

                    trans.Commit();
                    pickLines.Add(line);
                }

                if (!altHeld && !_freeze)
                {
                    if (int.TryParse(currentValue, out int iv))
                        currentValue = (iv + (_goDown ? -1 : 1)).ToString();
                }

                // Arrow-key nudge: ↑/↓ held at pick time adjusts by ±1, independent of freeze
                if ((GetAsyncKeyState(VK_UP) & 0x8000) != 0 && int.TryParse(currentValue, out int upVal))
                    currentValue = (upVal + 1).ToString();
                else if ((GetAsyncKeyState(VK_DOWN) & 0x8000) != 0 && int.TryParse(currentValue, out int dnVal))
                    currentValue = (dnVal - 1).ToString();

                string statusName = isTextNote ? "Text" : _parameterName;
                curVal[0] = currentValue;
                _onStatusUpdate?.Invoke(
                    new[] { (statusName, FormatValue(currentValue)) },
                    pickLines.Count);
            }

            string nextValue = curVal[0] != _startValue ? curVal[0] : null;

            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No elements were processed.");
            }
            else
            {
                sb.AppendLine($"ATS: wrote {totalWrites} value(s) across {pickLines.Count} element(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  \u2022 {line}");
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
