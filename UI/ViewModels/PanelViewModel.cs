using Renumber.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Renumber.UI.ViewModels
{
    public class PanelViewModel : BaseViewModel
    {
        private readonly PanelDefinition _model;
        private readonly Action<PanelViewModel> _addControllerAction;
        private readonly Action<PanelViewModel> _deleteAction;

        public PanelViewModel(
            PanelDefinition model,
            Action<PanelViewModel> addControllerAction,
            Action<PanelViewModel> deleteAction,
            Func<ControllerDefinition, ControllerViewModel> createControllerVM)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _addControllerAction = addControllerAction;
            _deleteAction = deleteAction;

            Controllers = new ObservableCollection<ControllerViewModel>();
            if (model.Controllers != null)
            {
                foreach (var ctrlDef in model.Controllers)
                {
                    Controllers.Add(createControllerVM(ctrlDef));
                }
            }

            AddControllerCommand = new RelayCommand(_ => _addControllerAction?.Invoke(this));
            DeleteCommand = new RelayCommand(_ => _deleteAction?.Invoke(this));
        }

        public PanelDefinition Model => _model;

        public ObservableCollection<ControllerViewModel> Controllers { get; }

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                    // Optional: trigger name changed callback for saving if needed
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                SetProperty(ref _isExpanded, value);
            }
        }

        public ICommand AddControllerCommand { get; }
        public ICommand DeleteCommand { get; }

        public ControllerViewModel AddNewController(Func<ControllerDefinition, ControllerViewModel> createControllerVM)
        {
            var def = new ControllerDefinition { Name = $"Controller {Controllers.Count + 1}" };
            def.Lines.Add(new LineDefinition { Name = "Line 1", ControllerName = def.Name });
            
            _model.Controllers.Add(def);
            var vm = createControllerVM(def);
            Controllers.Add(vm);
            
            return vm;
        }

        public void RemoveController(ControllerViewModel ctrl)
        {
            if (Controllers.Contains(ctrl))
            {
                Controllers.Remove(ctrl);
                _model.Controllers.Remove(ctrl.Model);
            }
        }
    }
}
