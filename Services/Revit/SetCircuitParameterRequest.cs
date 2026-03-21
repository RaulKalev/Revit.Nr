using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// External event request that lets the user pick electrical circuits
    /// and then writes a named parameter to each selected circuit.
    /// </summary>
    public sealed class SetCircuitParameterRequest : IExternalEventRequest
    {
        private readonly string _parameterName;
        private readonly string _value;
        private readonly bool _goDown;
        private readonly Action<string, string> _onComplete;

        public SetCircuitParameterRequest(string parameterName, string value, bool goDown, Action<string, string> onComplete)
        {
            _parameterName = parameterName;
            _value = value;
            _goDown = goDown;
            _onComplete = onComplete;
        }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                _onComplete?.Invoke("No active Revit document.", null);
                return;
            }

            var doc = uidoc.Document;

            // Pre-load all circuits once for the session — circuits don't change while picking.
            var allCircuits = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            string currentValue = _value;
            var pickLines = new List<string>();
            int totalSuccess = 0;

            // --- Continuous pick loop — Escape ends the session ------------------
            while (true)
            {
                Reference pickedRef;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        new ElectricalCircuitSelectionFilter(),
                        $"Select fixture [{_parameterName} = {currentValue}]  |  Escape to finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break; // User pressed Escape — end session
                }
                catch (Exception ex)
                {
                    pickLines.Add($"Selection error: {ex.Message}");
                    break;
                }

                var fixture = doc.GetElement(pickedRef.ElementId) as FamilyInstance;
                if (fixture == null) continue;

                ElectricalSystem circuit = allCircuits.FirstOrDefault(c => c.Elements.Contains(fixture));
                if (circuit == null)
                {
                    pickLines.Add($"[{currentValue}] {fixture.Name} — no circuit found, skipped.");
                    continue;
                }

                string circuitLabel = circuit.Name ?? $"id:{circuit.Id}";
                int successThisRound = 0;
                var errors = new List<string>();

                using (var trans = new Transaction(doc, $"Renumber: Set {_parameterName} = {currentValue}"))
                {
                    trans.Start();

                    // Write to circuit
                    Parameter circuitParam = circuit.LookupParameter(_parameterName);
                    if (circuitParam == null)
                        errors.Add($"circuit: param not found");
                    else if (circuitParam.IsReadOnly)
                        errors.Add($"circuit: read-only");
                    else if (WriteParameter(circuitParam, currentValue, out string e1))
                        successThisRound++;
                    else
                        errors.Add($"circuit: {e1}");

                    // Write to fixture if it also carries the parameter
                    Parameter fixtureParam = fixture.LookupParameter(_parameterName);
                    if (fixtureParam != null && !fixtureParam.IsReadOnly)
                    {
                        if (WriteParameter(fixtureParam, currentValue, out string e2))
                            successThisRound++;
                        else
                            errors.Add($"fixture: {e2}");
                    }

                    trans.Commit();
                }

                totalSuccess += successThisRound;

                string line = $"[{currentValue}] {fixture.Name} → {circuitLabel}";
                if (errors.Count > 0) line += $"  ({string.Join("; ", errors)})";
                pickLines.Add(line);

                // Advance value for the next pick
                if (int.TryParse(currentValue, out int iv))
                    currentValue = (iv + (_goDown ? -1 : 1)).ToString();
            }

            // next value to show in the box after the session ends
            string nextValue = currentValue != _value ? currentValue : null;

            // --- Build result summary -------------------------------------------
            var sb = new StringBuilder();
            if (pickLines.Count == 0)
            {
                sb.Append("No fixtures were processed.");
            }
            else
            {
                sb.AppendLine($"Assigned {totalSuccess} parameter write(s) across {pickLines.Count} fixture(s):");
                foreach (string line in pickLines)
                    sb.AppendLine($"  • {line}");
            }

            _onComplete?.Invoke(sb.ToString().TrimEnd(), nextValue);
        }

        // -------------------------------------------------------------------------
        private static bool WriteParameter(Parameter param, string value, out string error)
        {
            error = null;

            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    return true;

                case StorageType.Integer:
                    if (int.TryParse(value, out int intVal))
                    {
                        param.Set(intVal);
                        return true;
                    }
                    error = $"cannot parse '{value}' as integer.";
                    return false;

                case StorageType.Double:
                    if (double.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double dblVal))
                    {
                        param.Set(dblVal);
                        return true;
                    }
                    error = $"cannot parse '{value}' as a number.";
                    return false;

                case StorageType.ElementId:
                    error = "ElementId parameters are not supported by this tool.";
                    return false;

                default:
                    error = $"unsupported storage type '{param.StorageType}'.";
                    return false;
            }
        }
    }
}
