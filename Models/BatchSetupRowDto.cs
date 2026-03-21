using Renumber.UI.ViewModels;

namespace Renumber.Models
{
    /// <summary>
    /// Describes the validation status of a single Batch Setup row.
    /// </summary>
    public enum BatchRowStatus
    {
        /// <summary>Row is valid and ready for saving.</summary>
        OK,
        /// <summary>One or more mapped parameters are missing on this type.</summary>
        MissingParam,
        /// <summary>User-entered value failed validation (e.g. negative number).</summary>
        InvalidValue
    }

    /// <summary>
    /// Data-transfer object for a single row in the Batch Setup DataGrid.
    /// Each row represents one FamilySymbol (type) from the included categories.
    /// Extends BaseViewModel so property changes are reflected in the DataGrid.
    /// </summary>
    public class BatchSetupRowDto : BaseViewModel
    {
        // ---- Identity (read-only in the grid) ----

        private string _category;
        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        private string _familyName;
        public string FamilyName
        {
            get => _familyName;
            set => SetProperty(ref _familyName, value);
        }

        private string _typeName;
        public string TypeName
        {
            get => _typeName;
            set => SetProperty(ref _typeName, value);
        }

        /// <summary>
        /// Serialized ElementId of the FamilySymbol. Using long for .NET 8 compatibility
        /// (Revit 2025+ uses long ElementIds).
        /// </summary>
        public long SymbolId { get; set; }

        // ---- Current values (snapshot from Revit, read-only) ----

        private double? _current_mA_Load;
        public double? Current_mA_Load
        {
            get => _current_mA_Load;
            set => SetProperty(ref _current_mA_Load, value);
        }

        private int? _current_AddressCount;
        public int? Current_AddressCount
        {
            get => _current_AddressCount;
            set => SetProperty(ref _current_AddressCount, value);
        }

        // ---- Editable values (user modifies these in the grid) ----

        private double? _editable_mA_Load;
        public double? Editable_mA_Load
        {
            get => _editable_mA_Load;
            set
            {
                if (SetProperty(ref _editable_mA_Load, value))
                {
                    ValidateAndUpdateStatus();
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        private int? _editable_AddressCount;
        public int? Editable_AddressCount
        {
            get => _editable_AddressCount;
            set
            {
                if (SetProperty(ref _editable_AddressCount, value))
                {
                    ValidateAndUpdateStatus();
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        // ---- Status ----

        private BatchRowStatus _status = BatchRowStatus.OK;
        public BatchRowStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// True when either editable value differs from the current (Revit) value.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                bool loadChanged = Editable_mA_Load.HasValue != Current_mA_Load.HasValue
                    || (Editable_mA_Load.HasValue && Current_mA_Load.HasValue
                        && !Editable_mA_Load.Value.Equals(Current_mA_Load.Value));

                bool addrChanged = Editable_AddressCount.HasValue != Current_AddressCount.HasValue
                    || (Editable_AddressCount.HasValue && Current_AddressCount.HasValue
                        && Editable_AddressCount.Value != Current_AddressCount.Value);

                return loadChanged || addrChanged;
            }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        // ---- Validation ----

        /// <summary>
        /// Re-evaluates the Status and ErrorMessage based on current editable values.
        /// Called automatically when editable values change.
        /// </summary>
        private void ValidateAndUpdateStatus()
        {
            // If already marked as MissingParam by the collector, do not override.
            if (Status == BatchRowStatus.MissingParam) return;

            if (Editable_mA_Load.HasValue && Editable_mA_Load.Value < 0)
            {
                Status = BatchRowStatus.InvalidValue;
                ErrorMessage = "mA Load must be >= 0";
                return;
            }

            if (Editable_AddressCount.HasValue && Editable_AddressCount.Value < 0)
            {
                Status = BatchRowStatus.InvalidValue;
                ErrorMessage = "Address Count must be >= 0";
                return;
            }

            Status = BatchRowStatus.OK;
            ErrorMessage = null;
        }

        /// <summary>
        /// After a successful save, updates the "current" values to match
        /// the saved editable values so IsDirty resets to false.
        /// </summary>
        public void CommitValues()
        {
            Current_mA_Load = Editable_mA_Load;
            Current_AddressCount = Editable_AddressCount;
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
