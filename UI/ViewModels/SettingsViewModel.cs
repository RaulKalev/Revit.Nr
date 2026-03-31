using Renumber.Models;
using Renumber.Services;
using Renumber.Services.Revit;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace Renumber.UI.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly SettingsService _settingsService;
        private readonly ParameterResolver _parameterResolver;
        private readonly RevitExternalEventService _eventService;
        private SettingsModel _settings;
        private string _validationStatus;

        public SettingsViewModel(SettingsService settingsService, ParameterResolver parameterResolver, RevitExternalEventService eventService)
        {
            _settingsService = settingsService;
            _parameterResolver = parameterResolver;
            _eventService = eventService;

            LoadSettingsCommand = new RelayCommand(_ => LoadSettings());
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            ValidateSettingsCommand = new RelayCommand(_ => ValidateSettings());
            
            // Initial Load
            LoadSettings();
        }

        public SettingsModel Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public string ValidationStatus
        {
            get => _validationStatus;
            set => SetProperty(ref _validationStatus, value);
        }

        public ObservableCollection<string> ValidationMessages { get; } = new ObservableCollection<string>();

        // Category Check Properties (Helper for UI Binding)
        public bool IsLightingFixturesSelected
        {
            get => Settings.IncludedCategories.Contains(BuiltInCategory.OST_LightingFixtures);
            set
            {
                if (value && !Settings.IncludedCategories.Contains(BuiltInCategory.OST_LightingFixtures))
                    Settings.IncludedCategories.Add(BuiltInCategory.OST_LightingFixtures);
                else if (!value)
                    Settings.IncludedCategories.Remove(BuiltInCategory.OST_LightingFixtures);
                OnPropertyChanged();
            }
        }

        public bool IsElectricalFixturesSelected
        {
            get => Settings.IncludedCategories.Contains(BuiltInCategory.OST_ElectricalFixtures);
            set
            {
                if (value && !Settings.IncludedCategories.Contains(BuiltInCategory.OST_ElectricalFixtures))
                    Settings.IncludedCategories.Add(BuiltInCategory.OST_ElectricalFixtures);
                else if (!value)
                    Settings.IncludedCategories.Remove(BuiltInCategory.OST_ElectricalFixtures);
                OnPropertyChanged();
            }
        }
        
        public bool IsLightingDevicesSelected
        {
            get => Settings.IncludedCategories.Contains(BuiltInCategory.OST_LightingDevices);
            set
            {
                if (value && !Settings.IncludedCategories.Contains(BuiltInCategory.OST_LightingDevices))
                    Settings.IncludedCategories.Add(BuiltInCategory.OST_LightingDevices);
                else if (!value)
                    Settings.IncludedCategories.Remove(BuiltInCategory.OST_LightingDevices);
                OnPropertyChanged();
            }
        }

        public bool IsDataDevicesSelected
        {
            get => Settings.IncludedCategories.Contains(BuiltInCategory.OST_DataDevices);
            set
            {
                if (value && !Settings.IncludedCategories.Contains(BuiltInCategory.OST_DataDevices))
                    Settings.IncludedCategories.Add(BuiltInCategory.OST_DataDevices);
                else if (!value)
                    Settings.IncludedCategories.Remove(BuiltInCategory.OST_DataDevices);
                OnPropertyChanged();
            }
        }

        // --- Numeric limit helpers (TextBox can't bind directly to double/int without a converter) ---

        // --- Numeric limit helpers (TextBox can't bind directly to double/int without a converter) ---

        // Controller Limits
        public string ControllerMaxLoadmA_Text
        {
            get => Settings.ControllerMaxLoadmA.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                {
                    Settings.ControllerMaxLoadmA = parsed;
                    OnPropertyChanged();
                }
            }
        }

        public string ControllerMaxAddressCount_Text
        {
            get => Settings.ControllerMaxAddressCount.ToString();
            set
            {
                if (int.TryParse(value, out int parsed) && parsed > 0)
                {
                    Settings.ControllerMaxAddressCount = parsed;
                    OnPropertyChanged();
                }
            }
        }

        // Line Limits
        public string LineMaxLoadmA_Text
        {
            get => Settings.LineMaxLoadmA.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                {
                    Settings.LineMaxLoadmA = parsed;
                    OnPropertyChanged();
                }
            }
        }

        public string LineMaxAddressCount_Text
        {
            get => Settings.LineMaxAddressCount.ToString();
            set
            {
                if (int.TryParse(value, out int parsed) && parsed > 0)
                {
                    Settings.LineMaxAddressCount = parsed;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand LoadSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ValidateSettingsCommand { get; }

        private void LoadSettings()
        {
            Settings = _settingsService.Load();
            ValidationStatus = "Settings loaded.";
            ValidationMessages.Clear();
            RefreshCategoryProperties();
        }

        private void SaveSettings()
        {
            _settingsService.Save(Settings);
            ValidationStatus = "Settings saved.";
        }

        private void ValidateSettings()
        {
            ValidationStatus = "Validating...";
            ValidationMessages.Clear();
            
            var request = new ValidateSettingsRequest(_parameterResolver, Settings, (result) =>
            {
                // ExternalEvent.Execute() already runs on the main UI thread.
                // No Dispatcher needed - direct callback is safe and avoids Revit 2026 crash.
                ValidationStatus = result.IsValid ? "Validation Passed" : "Validation Failed";
                foreach (var msg in result.Messages)
                {
                    ValidationMessages.Add(msg);
                }
            });

            _eventService.Raise(request);
        }

        private void RefreshCategoryProperties()
        {
            OnPropertyChanged(nameof(IsLightingFixturesSelected));
            OnPropertyChanged(nameof(IsElectricalFixturesSelected));
            OnPropertyChanged(nameof(IsLightingDevicesSelected));
            OnPropertyChanged(nameof(ControllerMaxLoadmA_Text));
            OnPropertyChanged(nameof(ControllerMaxAddressCount_Text));
            OnPropertyChanged(nameof(LineMaxLoadmA_Text));
            OnPropertyChanged(nameof(LineMaxAddressCount_Text));
        }
    }
}

