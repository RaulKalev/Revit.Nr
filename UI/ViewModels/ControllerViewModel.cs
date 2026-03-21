using Renumber.Models;
using Renumber.Services.Revit;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Renumber.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a DALI Controller card in the Grouping tab.
    /// Contains a collection of LineViewModels and tracks aggregate load/address totals.
    /// Only one controller can be expanded at a time (accordion behaviour enforced by GroupingViewModel).
    /// </summary>
    public class ControllerViewModel : BaseViewModel
    {
        private readonly ControllerDefinition _model;
        private readonly Action<ControllerViewModel> _addLineAction;
        private readonly Action<ControllerViewModel> _deleteAction;
        private readonly Action<LineViewModel> _addToLineAction;
        private readonly Action<LineViewModel> _interactiveAddAction;
        private readonly Action<ControllerViewModel> _onExpanded; // Called so parent can collapse siblings
        private readonly Action<ControllerViewModel> _onNameChanged;
        private readonly Action<LineViewModel> _onLineNameChanged;
        private readonly Action<LineViewModel> _changeColorAction;

        public ControllerViewModel(
            ControllerDefinition model,
            Action<ControllerViewModel> addLineAction,
            Action<ControllerViewModel> deleteAction,
            Action<LineViewModel> addToLineAction,
            Action<LineViewModel> interactiveAddAction,
            Action<ControllerViewModel> onExpanded,
            Action<ControllerViewModel> onNameChanged = null,
            Action<LineViewModel> onLineNameChanged = null,
            Action<LineViewModel> changeColorAction = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _addLineAction = addLineAction;
            _deleteAction = deleteAction;
            _addToLineAction = addToLineAction;
            _interactiveAddAction = interactiveAddAction;
            _onExpanded = onExpanded;
            _onNameChanged = onNameChanged;
            _onLineNameChanged = onLineNameChanged;
            _changeColorAction = changeColorAction;

            Lines = new ObservableCollection<LineViewModel>();
            foreach (var lineDef in model.Lines)
            {
                Lines.Add(CreateLineVM(lineDef));
            }

            AddLineCommand = new RelayCommand(_ => _addLineAction?.Invoke(this));
            DeleteCommand = new RelayCommand(_ => _deleteAction?.Invoke(this));
        }

        public ControllerDefinition Model => _model;

        public ObservableCollection<LineViewModel> Lines { get; }

        // ---- Controller Name ----

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                    // Sync controller name to all child lines
                    foreach (var line in Lines)
                        line.ControllerName = value;
                    _onNameChanged?.Invoke(this);
                }
            }
        }

        // ---- Accordion expansion (only one controller open at a time) ----

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    _onExpanded?.Invoke(this); // parent collapses siblings
                }
            }
        }

        // ---- Aggregate Gauge Data (sum of all lines in this controller) ----

        private double _totalLoadmA;
        public double TotalLoadmA
        {
            get => _totalLoadmA;
            set
            {
                if (SetProperty(ref _totalLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsOverLoad));
                }
            }
        }

        private int _totalAddressCount;
        public int TotalAddressCount
        {
            get => _totalAddressCount;
            set
            {
                if (SetProperty(ref _totalAddressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        private double _maxLoadmA = 250.0;
        public double MaxLoadmA
        {
            get => _maxLoadmA;
            set
            {
                if (SetProperty(ref _maxLoadmA, value))
                {
                    OnPropertyChanged(nameof(LoadRatio));
                    OnPropertyChanged(nameof(IsOverLoad));
                }
            }
        }

        private int _maxAddressCount = 64;
        public int MaxAddressCount
        {
            get => _maxAddressCount;
            set
            {
                if (SetProperty(ref _maxAddressCount, value))
                {
                    OnPropertyChanged(nameof(AddressRatio));
                    OnPropertyChanged(nameof(IsOverAddress));
                }
            }
        }

        public double LoadRatio => _maxLoadmA > 0 ? _totalLoadmA / _maxLoadmA : 0.0;
        public double AddressRatio => _maxAddressCount > 0 ? (double)_totalAddressCount / _maxAddressCount : 0.0;
        public bool IsOverLoad => LoadRatio > 1.0;
        public bool IsOverAddress => AddressRatio > 1.0;

        /// <summary>Recalculates aggregate totals by summing child line gauges.</summary>
        public void RecalcTotals()
        {
            double load = 0;
            int addr = 0;
            foreach (var line in Lines) { load += line.LoadmA; addr += line.AddressCount; }
            TotalLoadmA = load;
            TotalAddressCount = addr;
        }

        // ---- Line management ----

        public LineViewModel AddNewLine()
        {
            var def = new LineDefinition { Name = $"Line {Lines.Count + 1}", ControllerName = _model.Name };
            _model.Lines.Add(def);
            var vm = CreateLineVM(def);
            vm.IsExpanded = true;
            Lines.Add(vm);
            
            // Instantly trigger a model scan for this newly created line so it picks up pre-existing devices.
            _onLineNameChanged?.Invoke(vm);
            
            return vm;
        }

        public void DeleteLine(LineViewModel line)
        {
            if (Lines.Remove(line))
                _model.Lines.Remove(line.Model);
        }

        /// <summary>Called by GroupingViewModel after a line's Add to Line completes.</summary>
        public void OnLineAddComplete(LineViewModel line, AddToLineResult result)
        {
            line.UpdateGauges(result);
            RecalcTotals();
        }

        private LineViewModel CreateLineVM(LineDefinition def)
        {
            return new LineViewModel(
                def,
                line => _addToLineAction?.Invoke(line),
                line => DeleteLine(line),
                line => _interactiveAddAction?.Invoke(line),
                line => { _onLineNameChanged?.Invoke(line); },
                line => { _changeColorAction?.Invoke(line); });
        }

        public ICommand AddLineCommand { get; }
        public ICommand DeleteCommand { get; }
    }
}
