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
    /// External event request for Side mode.
    /// Writes a named parameter with an incrementing, optionally padded value (like ATS).
    /// When Double mode is enabled two values are written together separated by a divider
    /// (e.g. "01/02") and both increment by 2 per pick so the next pair is "03/04".
    /// Alt held at pick time suppresses incrementing for that pick.
    /// </summary>
    public sealed class SideParameterRequest : IExternalEventRequest
    {
        private readonly BuiltInCategory _category;
        private readonly string _parameterName;
        private readonly string _startValue;
        private readonly bool   _isDouble;
        private readonly string _startValue2;
        private readonly string _divider;
        private readonly int    _charCount;
        private readonly string _fillStr;
        private readonly string _suffix;
        private readonly int    _circuitLimit;
        private readonly bool   _goDown;
        private readonly bool   _freeze;
        private readonly Action<string, string, string> _onComplete;
        private readonly Action<IEnumerable<(string name, string value)>, int> _onStatusUpdate;
        private readonly Action<Action<int>> _registerNudge;
        private readonly Action<Action<bool>> _registerDoubleToggle;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_MENU = 0x12;
        private const int VK_UP   = 0x26;
        private const int VK_DOWN = 0x28;

        public SideParameterRequest(
            BuiltInCategory category,
            string parameterName,
            string startValue,
            bool isDouble,
            string startValue2,
            string divider,
            int charCount,
            string fillStr,
            string suffix,
            int circuitLimit,
            bool goDown,
            bool freeze,
            Action<string, string, string> onComplete,
            Action<IEnumerable<(string name, string value)>, int> onStatusUpdate = null,
            Action<Action<int>> registerNudge = null,
            Action<Action<bool>> registerDoubleToggle = null)
        {
            _category      = category;
            _parameterName = parameterName;
            _startValue    = startValue;
            _isDouble      = isDouble;
            _startValue2   = startValue2 ?? string.Empty;
            _divider       = divider      ?? string.Empty;
            _charCount     = charCount;
            _fillStr       = fillStr      ?? string.Empty;
            _suffix        = suffix       ?? string.Empty;
            _circuitLimit  = circuitLimit;
            _goDown        = goDown;
            _freeze        = freeze;
            _onComplete    = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onStatusUpdate = onStatusUpdate;
            _registerNudge  = registerNudge;
            _registerDoubleToggle = registerDoubleToggle;
        }

        private string FormatSingle(string numericValue)
        {
            string padded = (_charCount > 0 && !string.IsNullOrEmpty(_fillStr))
                ? numericValue.PadLeft(_charCount, _fillStr[0])
                : numericValue;
            return padded + _suffix;
        }

        private string FormatDisplay(string v1, string v2, bool isDouble)
            => isDouble
                ? FormatSingle(v1) + _divider + FormatSingle(v2)
                : FormatSingle(v1);

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                _onComplete("No active Revit document.", null, null);
                return;
            }

            var doc = uidoc.Document;
            var pickLines = new List<string>();
            int totalWrites = 0;

            var filter = new GenericCategorySelectionFilter(_category);

            // Mutable wrappers so the nudge closure can update values in place
            string[] curVal  = { _startValue };
            string[] curVal2 = { _startValue2 };
            bool[]   curDouble = { _isDouble };

            // Register double toggle callback
            _registerDoubleToggle?.Invoke(isNowDouble =>
            {
                curDouble[0] = isNowDouble;
                _onStatusUpdate?.Invoke(
                    new[] { (_parameterName, FormatDisplay(curVal[0], curVal2[0], curDouble[0])) },
                    pickLines.Count);
            });

            _registerNudge?.Invoke(delta =>
            {
                if (int.TryParse(curVal[0], out int sv))
                {
                    curVal[0] = (sv + delta).ToString();
                    if (curDouble[0] && int.TryParse(curVal2[0], out int sv2))
                        curVal2[0] = (sv2 + delta).ToString();
                    _onStatusUpdate?.Invoke(
                        new[] { (_parameterName, FormatDisplay(curVal[0], curVal2[0], curDouble[0])) },
                        pickLines.Count);
                }
            });

            while (true)
            {
                string currentValue  = curVal[0];
                string currentValue2 = curVal2[0];
                bool   isDouble      = curDouble[0];
                int    step          = isDouble ? 2 : 1;

                Reference pickedRef;
                try
                {
                    string preview = FormatDisplay(currentValue, currentValue2, isDouble);
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        filter,
                        $"[Side] Pick element  [{preview}]  |  Alt = hold  |  Escape to finish");
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

                // Re-read after pick (user may have toggled double via checkbox)
                currentValue  = curVal[0];
                currentValue2 = curVal2[0];
                isDouble      = curDouble[0];
                step          = isDouble ? 2 : 1;
                bool altHeld  = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                var elem = doc.GetElement(pickedRef.ElementId);
                if (elem == null) continue;

                string written  = FormatDisplay(currentValue, currentValue2, isDouble);
                string elemName = elem.Name ?? $"id:{elem.Id}";

                using (var trans = new Transaction(doc, $"Side: {written}"))
                {
                    trans.Start();

                    Parameter param = elem.LookupParameter(_parameterName);
                    string line;
                    if (param == null)
                        line = $"{elemName}: parameter '{_parameterName}' not found";
                    else if (param.IsReadOnly)
                        line = $"{elemName}: parameter '{_parameterName}' is read-only";
                    else if (WriteParameter(param, written, out string err))
                    {
                        line = $"{elemName}: {_parameterName} = {written}";
                        totalWrites++;
                    }
                    else
                        line = $"{elemName}: {err}";

                    trans.Commit();
                    pickLines.Add(line);
                }

                if (!altHeld && !_freeze)
                {
                    // v1 always increments; v2 also increments in background (stays in sync for re-enabling double)
                    if (int.TryParse(curVal[0], out int iv))
                    {
                        int next = iv + (_goDown ? -step : step);
                        if (_circuitLimit > 0 && !_goDown && next > _circuitLimit)
                            next = 1;
                        else if (_circuitLimit > 0 && _goDown && next < 1)
                            next = _circuitLimit;
                        curVal[0] = next.ToString();
                    }
                    if (int.TryParse(curVal2[0], out int iv2))
                    {
                        int next2 = iv2 + (_goDown ? -step : step);
                        if (_circuitLimit > 0 && !_goDown && next2 > _circuitLimit)
                            next2 = isDouble ? 2 : 1;
                        else if (_circuitLimit > 0 && _goDown && next2 < 1)
                            next2 = _circuitLimit - (isDouble ? 1 : 0);
                        curVal2[0] = next2.ToString();
                    }
                }

                // Arrow-key nudge held at pick time adjusts ±1 on both values
                if ((GetAsyncKeyState(VK_UP) & 0x8000) != 0)
                {
                    if (int.TryParse(curVal[0], out int u))  curVal[0]  = (u + 1).ToString();
                    if (int.TryParse(curVal2[0], out int u2)) curVal2[0] = (u2 + 1).ToString();
                }
                else if ((GetAsyncKeyState(VK_DOWN) & 0x8000) != 0)
                {
                    if (int.TryParse(curVal[0], out int d))  curVal[0]  = (d - 1).ToString();
                    if (int.TryParse(curVal2[0], out int d2)) curVal2[0] = (d2 - 1).ToString();
                }

                _onStatusUpdate?.Invoke(
                    new[] { (_parameterName, FormatDisplay(curVal[0], curVal2[0], curDouble[0])) },
                    pickLines.Count);
            }

            string nextVal1 = curVal[0]  != _startValue  ? curVal[0]  : null;
            string nextVal2 = curVal2[0] != _startValue2 ? curVal2[0] : null;

            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No elements were processed.");
            }
            else
            {
                sb.AppendLine($"Side: wrote {totalWrites} value(s) across {pickLines.Count} element(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  \u2022 {line}");
            }

            _onComplete(sb.ToString().TrimEnd(), nextVal1, nextVal2);
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
