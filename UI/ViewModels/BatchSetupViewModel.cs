using Renumber.Models;
using Renumber.Services;
using Renumber.Services.Revit;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Renumber.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Batch Setup tab. Manages loading, editing, and saving
    /// of DALI type parameters for FamilySymbols in the active Revit document.
    /// </summary>
    public class BatchSetupViewModel : BaseViewModel
    {
        private readonly SettingsService _settingsService;
        private readonly RevitExternalEventService _eventService;

        public BatchSetupViewModel(SettingsService settingsService, RevitExternalEventService eventService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));

            Rows = new ObservableCollection<BatchSetupRowDto>();

            ReloadCommand = new RelayCommand(_ => ReloadFromModel(), _ => !IsBusy);
            SaveCommand = new RelayCommand(_ => SaveToTypes(), _ => !IsBusy && HasDirtyRows);
        }

        // ---- Collections ----

        /// <summary>
        /// Rows displayed in the Batch Setup DataGrid.
        /// </summary>
        public ObservableCollection<BatchSetupRowDto> Rows { get; }

        // ---- State Properties ----

        private bool _isBusy;
        /// <summary>
        /// True while a Revit operation (read or write) is in progress.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    // Force re-evaluation of CanExecute on commands
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _statusMessage = "Click 'Reload' to load types from the model.";
        /// <summary>
        /// Status message displayed at the bottom of the Batch Setup tab.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _validationBanner;
        /// <summary>
        /// Error banner displayed when settings are invalid or missing.
        /// Null/empty when settings are OK.
        /// </summary>
        public string ValidationBanner
        {
            get => _validationBanner;
            set => SetProperty(ref _validationBanner, value);
        }

        /// <summary>
        /// True if any row has been modified by the user.
        /// </summary>
        public bool HasDirtyRows => Rows.Any(r => r.IsDirty && r.Status == BatchRowStatus.OK);

        // ---- Commands ----

        public ICommand ReloadCommand { get; }
        public ICommand SaveCommand { get; }

        // ---- Reload from Model ----

        /// <summary>
        /// Initiates a Revit read operation to collect FamilySymbol types and their parameter values.
        /// </summary>
        private void ReloadFromModel()
        {
            var settings = _settingsService.Load();

            // Validate settings before proceeding
            if (settings.IncludedCategories == null || settings.IncludedCategories.Count == 0)
            {
                ValidationBanner = "No categories selected in Settings. Please configure categories first.";
                return;
            }
            if (string.IsNullOrWhiteSpace(settings.Param_Load) && string.IsNullOrWhiteSpace(settings.Param_AddressCount))
            {
                ValidationBanner = "No parameter mappings configured in Settings.";
                return;
            }

            ValidationBanner = null;
            IsBusy = true;
            StatusMessage = "Loading types from model...";

            var request = new CollectBatchSetupRequest(settings, OnCollectionComplete);
            _eventService.Raise(request);
        }

        /// <summary>
        /// Callback invoked on the UI thread after collection completes.
        /// </summary>
        private void OnCollectionComplete(System.Collections.Generic.List<BatchSetupRowDto> rows)
        {
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
                // Subscribe to PropertyChanged on each row to track dirty state
                row.PropertyChanged += Row_PropertyChanged;
            }

            IsBusy = false;

            int total = rows.Count;
            int missingCount = rows.Count(r => r.Status == BatchRowStatus.MissingParam);

            if (total == 0)
            {
                StatusMessage = "No types found in the selected categories.";
            }
            else if (missingCount > 0)
            {
                StatusMessage = $"Loaded {total} types. {missingCount} have missing parameters.";
            }
            else
            {
                StatusMessage = $"Loaded {total} types.";
            }

            OnPropertyChanged(nameof(HasDirtyRows));
        }

        /// <summary>
        /// Tracks changes in individual rows to update the HasDirtyRows property.
        /// </summary>
        private void Row_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchSetupRowDto.IsDirty)
                || e.PropertyName == nameof(BatchSetupRowDto.Status))
            {
                OnPropertyChanged(nameof(HasDirtyRows));
                // Force re-evaluation of SaveCommand.CanExecute
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        // ---- Save to Types ----

        /// <summary>
        /// Initiates a Revit write operation for all dirty rows with OK status.
        /// </summary>
        private void SaveToTypes()
        {
            var settings = _settingsService.Load();

            var dirtyOkRows = Rows
                .Where(r => r.IsDirty && r.Status == BatchRowStatus.OK)
                .ToList();

            if (dirtyOkRows.Count == 0)
            {
                StatusMessage = "No modified rows to save.";
                return;
            }

            IsBusy = true;
            StatusMessage = $"Saving {dirtyOkRows.Count} modified types...";

            var request = new SaveBatchSetupRequest(dirtyOkRows, settings, OnSaveComplete);
            _eventService.Raise(request);
        }

        /// <summary>
        /// Callback invoked on the UI thread after the save operation completes.
        /// Updates row state and displays the summary message.
        /// </summary>
        private void OnSaveComplete(BatchSaveResult result)
        {
            IsBusy = false;

            // Commit values on successfully saved rows so IsDirty resets
            var dirtyOkRows = Rows
                .Where(r => r.IsDirty && r.Status == BatchRowStatus.OK)
                .ToList();

            // Commit the first N rows corresponding to UpdatedCount
            // (rows are processed in order, so this is a reasonable heuristic)
            int committed = 0;
            foreach (var row in dirtyOkRows)
            {
                if (committed >= result.UpdatedCount) break;
                row.CommitValues();
                committed++;
            }

            // Build summary message
            var parts = new System.Collections.Generic.List<string>();
            if (result.UpdatedCount > 0) parts.Add($"Updated: {result.UpdatedCount}");
            if (result.SkippedCount > 0) parts.Add($"Skipped: {result.SkippedCount}");
            if (result.FailedCount > 0) parts.Add($"Failed: {result.FailedCount}");

            StatusMessage = parts.Count > 0
                ? string.Join(" | ", parts)
                : "Save complete.";

            OnPropertyChanged(nameof(HasDirtyRows));
        }
    }
}
