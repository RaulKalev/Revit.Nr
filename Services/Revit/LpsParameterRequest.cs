using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Describes a single LPS parameter and which numbering sequence it uses.
    /// </summary>
    public sealed class LpsParamSpec
    {
        public string Name { get; }
        public bool UseInnerRange { get; }
        public LpsParamSpec(string name, bool useInnerRange) { Name = name; UseInnerRange = useInnerRange; }
    }

    /// <summary>
    /// External event request for LPS (security) device numbering.
    /// Each active parameter advances through its own independent sequence.
    /// Alt held during a pick suppresses all counter increments for that pick.
    /// </summary>
    public sealed class LpsParameterRequest : IExternalEventRequest
    {
        private static readonly int[] InnerRangeSequence = { 1, 2, 9, 10, 17, 18, 25, 26 };

        private readonly IReadOnlyList<LpsParamSpec> _paramSpecs;
        private readonly string _startValue;
        private readonly bool _goDown;
        private readonly bool _freeze;
        private readonly Action<string, string> _onComplete;
        /// <summary>
        /// Optional — called after each successful pick with (paramName, nextValue) pairs and the
        /// total pick count so far. Runs on the WPF/UI thread, safe to update UI directly.
        /// </summary>
        private readonly Action<IEnumerable<(string name, string value)>, int> _onStatusUpdate;
        private readonly Action<Action<int>> _registerNudge;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_MENU = 0x12;
        private const int VK_UP   = 0x26;
        private const int VK_DOWN = 0x28;

        public LpsParameterRequest(
            IEnumerable<LpsParamSpec> paramSpecs,
            string startValue,
            bool goDown,
            bool freeze,
            Action<string, string> onComplete,
            Action<IEnumerable<(string name, string value)>, int> onStatusUpdate = null,
            Action<Action<int>> registerNudge = null)
        {
            _paramSpecs      = paramSpecs?.ToList() ?? throw new ArgumentNullException(nameof(paramSpecs));
            _startValue      = startValue;
            _goDown          = goDown;
            _freeze          = freeze;
            _onComplete      = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _onStatusUpdate  = onStatusUpdate;
            _registerNudge   = registerNudge;
        }

        private sealed class ParamState
        {
            public string Name;
            public bool UseInnerRange;
            public string CurrentValue;
            public int IrIndex;
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

            var states = _paramSpecs.Select(p => new ParamState
            {
                Name          = p.Name,
                UseInnerRange = p.UseInnerRange,
                CurrentValue  = _startValue,
                IrIndex       = GetInnerRangeStartIndex(_startValue)
            }).ToList();

            string DisplayValue() => states.Count > 0 ? states[0].CurrentValue : _startValue;

            var pickLines = new List<string>();
            int totalWrites = 0;

            // Register nudge handler — fires when user clicks ▲/▼ on the status window during picking
            _registerNudge?.Invoke(delta =>
            {
                foreach (var st in states)
                {
                    if (int.TryParse(st.CurrentValue, out int sv))
                        st.CurrentValue = (sv + delta).ToString();
                }
                _onStatusUpdate?.Invoke(states.Select(s => (s.Name, s.CurrentValue)), pickLines.Count);
            });

            while (true)
            {
                Reference pickedRef;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new SecurityDeviceSelectionFilter(),
                        $"[LPS] Select security device  [{DisplayValue()}]  |  Alt = hold numbers  |  Escape to finish");
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

                var fixture = doc.GetElement(pickedRef.ElementId) as FamilyInstance;
                if (fixture == null) continue;

                int successThisRound = 0;
                var errors = new List<string>();

                using (var trans = new Transaction(doc, $"LPS Renumber: {DisplayValue()}"))
                {
                    trans.Start();

                    foreach (var st in states)
                    {
                        Parameter param = fixture.LookupParameter(st.Name);
                        if (param == null)    { errors.Add($"'{st.Name}': not found"); continue; }
                        if (param.IsReadOnly) { errors.Add($"'{st.Name}': read-only"); continue; }

                        if (WriteParameter(param, st.CurrentValue, out string writeError))
                            successThisRound++;
                        else
                            errors.Add($"'{st.Name}': {writeError}");
                    }

                    trans.Commit();
                }

                totalWrites += successThisRound;

                string valsSummary = string.Join(", ", states.Select(s => $"{s.Name}={s.CurrentValue}"));
                string fixtureName = fixture.Name ?? $"id:{fixture.Id}";
                string line = $"[{valsSummary}] {fixtureName}";
                if (errors.Count > 0) line += $"  ({string.Join("; ", errors)})";
                pickLines.Add(line);

                if (!altHeld && !_freeze)
                {
                    foreach (var st in states)
                        st.CurrentValue = Advance(st.CurrentValue, st.UseInnerRange, ref st.IrIndex, _goDown);
                }

                // Arrow-key nudge: ↑/↓ held at pick time adjusts the base value by ±1, independent of freeze
                bool arrowUp   = (GetAsyncKeyState(VK_UP)   & 0x8000) != 0;
                bool arrowDown = (GetAsyncKeyState(VK_DOWN) & 0x8000) != 0;
                if (arrowUp || arrowDown)
                {
                    int delta = arrowUp ? 1 : -1;
                    foreach (var st in states)
                    {
                        if (int.TryParse(st.CurrentValue, out int sv))
                            st.CurrentValue = (sv + delta).ToString();
                    }
                }

                // Notify status window with the upcoming values and total pick count so far
                _onStatusUpdate?.Invoke(
                    states.Select(s => (s.Name, s.CurrentValue)),
                    pickLines.Count);
            }

            string nextValue = states.Count > 0 && states[0].CurrentValue != _startValue
                ? states[0].CurrentValue
                : null;

            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No devices were processed.");
            }
            else
            {
                sb.AppendLine($"LPS: wrote {totalWrites} parameter value(s) across {pickLines.Count} device(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  • {line}");
            }

            _onComplete(sb.ToString().TrimEnd(), nextValue);
        }

        private static string Advance(string currentValue, bool useInnerRange, ref int irIndex, bool goDown)
        {
            if (useInnerRange)
            {
                irIndex = goDown
                    ? (irIndex - 1 + InnerRangeSequence.Length) % InnerRangeSequence.Length
                    : (irIndex + 1) % InnerRangeSequence.Length;
                return InnerRangeSequence[irIndex].ToString();
            }
            if (int.TryParse(currentValue, out int iv))
                return (iv + (goDown ? -1 : 1)).ToString();
            return currentValue;
        }

        private static int GetInnerRangeStartIndex(string value)
        {
            if (!int.TryParse(value, out int iv)) return 0;
            for (int i = 0; i < InnerRangeSequence.Length; i++)
                if (InnerRangeSequence[i] == iv) return i;
            return 0;
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
                case StorageType.ElementId:
                    error = "ElementId parameters are not supported."; return false;
                default:
                    error = $"unsupported storage type '{param.StorageType}'."; return false;
            }
        }
    }
}
