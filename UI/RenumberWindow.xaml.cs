using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Renumber.UI
{
    public partial class RenumberWindow : Window
    {
        #region Constants / PInvoke

        private const string ConfigFilePath   = @"C:\ProgramData\RK Tools\Renumber\config.json";
        private const string WindowLeftKey     = "RenumberWindow.Left";
        private const string WindowTopKey      = "RenumberWindow.Top";
        private const string WindowWidthKey    = "RenumberWindow.Width";
        private const string WindowHeightKey   = "RenumberWindow.Height";
        private const string ParameterNameKey  = "RenumberWindow.ParameterName";
        private const string ValueKey           = "RenumberWindow.Value";
        // LPS config keys
        private const string LpsModeKey        = "RenumberWindow.LpsMode";
        private const string LpsParamsKey       = "RenumberWindow.LpsParams";
        private const string LpsValueKey        = "RenumberWindow.LpsValue";
        // Üld config keys
        private const string UldModeKey         = "RenumberWindow.UldMode";
        private const string UldCategoryKey     = "RenumberWindow.UldCategory";
        private const string UldParamNameKey    = "RenumberWindow.UldParamName";
        private const string UldValueKey        = "RenumberWindow.UldValue";
        private const string UldPrefixKey       = "RenumberWindow.UldPrefix";
        private const string UldSuffixKey       = "RenumberWindow.UldSuffix";
        // Heli config keys
        private const string HeliModeKey        = "RenumberWindow.HeliMode";
        private const string HeliCategoryKey    = "RenumberWindow.HeliCategory";
        private const string HeliParamNameKey   = "RenumberWindow.HeliParamName";
        private const string HeliValue1Key      = "RenumberWindow.HeliValue1";
        private const string HeliValue2Key      = "RenumberWindow.HeliValue2";
        // ATS config keys
        private const string AtsModeKey         = "RenumberWindow.AtsMode";
        private const string AtsCategoryKey     = "RenumberWindow.AtsCategory";
        private const string AtsParamNameKey    = "RenumberWindow.AtsParamName";
        private const string AtsValueKey        = "RenumberWindow.AtsValue";
        private const string AtsCharCountKey    = "RenumberWindow.AtsCharCount";
        private const string AtsPrefixKey       = "RenumberWindow.AtsPrefix";
        private const string AtsSuffixKey       = "RenumberWindow.AtsSuffix";
        private const string AtsParamName2Key   = "RenumberWindow.AtsParamName2";
        private const string AtsFixedValueKey   = "RenumberWindow.AtsFixedValue";
        private const string AtsParam2EnabledKey = "RenumberWindow.AtsParam2Enabled";
        // Direction config keys
        private const string ElDirectionKey     = "RenumberWindow.ElDirection";
        private const string LpsDirectionKey    = "RenumberWindow.LpsDirection";
        private const string UldDirectionKey    = "RenumberWindow.UldDirection";
        private const string AtsDirectionKey    = "RenumberWindow.AtsDirection";
        // Freeze config keys
        private const string ElFreezeKey        = "RenumberWindow.ElFreeze";
        private const string LpsFreezeKey       = "RenumberWindow.LpsFreeze";
        private const string UldFreezeKey       = "RenumberWindow.UldFreeze";
        private const string AtsFreezeKey       = "RenumberWindow.AtsFreeze";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion

        #region Fields

        private readonly WindowResizer _windowResizer;
        private bool _isDarkMode = true;
        private bool _isDataLoaded;
        private readonly UIApplication _uiApplication;
        private readonly Services.Revit.RevitExternalEventService _externalEventService;
        // LPS state
        private ObservableCollection<LpsParamEntry> _lpsParams;

        // Üld category list entry
        private sealed class UldCategoryItem
        {
            public string Label { get; }
            public Autodesk.Revit.DB.BuiltInCategory Category { get; }
            public bool IsTextNote { get; }
            public UldCategoryItem(string label, Autodesk.Revit.DB.BuiltInCategory cat, bool isTextNote = false)
            { Label = label; Category = cat; IsTextNote = isTextNote; }
            public override string ToString() => Label;
        }

        #endregion

        public RenumberWindow(UIApplication app, Services.Revit.RevitExternalEventService externalEventService)
        {
            _uiApplication      = app;
            _externalEventService = externalEventService;

            InitializeComponent();

            DataContext = this;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DeferWindowShow();

            _windowResizer = new WindowResizer(this);
            Closed += MainWindow_Closed;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;

            // Initialise LPS param collection before loading config
            _lpsParams = new ObservableCollection<LpsParamEntry>();

            LoadThemeState();
            LoadWindowState();
            LoadParameterNameState();

            // Wire up the LPS param list once controls are ready
            LpsParamList.ItemsSource = _lpsParams;

            // Populate Üld category dropdown, then restore saved selection
            PopulateUldCategories();
            if (_pendingUldCategory != null)
            {
                foreach (var item in UldCategoryCombo.Items)
                {
                    if (item is UldCategoryItem ci && ci.Label == _pendingUldCategory)
                    {
                        UldCategoryCombo.SelectedItem = ci;
                        break;
                    }
                }
            }

            // Populate Heli category dropdown, then restore saved selection
            PopulateHeliCategories();
            if (_pendingHeliCategory != null)
            {
                foreach (var item in HeliCategoryCombo.Items)
                {
                    if (item is UldCategoryItem ci && ci.Label == _pendingHeliCategory)
                    {
                        HeliCategoryCombo.SelectedItem = ci;
                        break;
                    }
                }
            }

            // Populate ATS category dropdown, then restore saved selection
            PopulateAtsCategories();
            if (_pendingAtsCategory != null)
            {
                foreach (var item in AtsCategoryCombo.Items)
                {
                    if (item is UldCategoryItem ci && ci.Label == _pendingAtsCategory)
                    {
                        AtsCategoryCombo.SelectedItem = ci;
                        break;
                    }
                }
            }

            _isDataLoaded = true;
            TryShowWindow();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            SaveWindowState();
        }

        private void LoadParameterNameState()
        {
            try
            {
                var config = LoadConfig();

                // EL state
                if (config.TryGetValue(ParameterNameKey, out var rawName) && rawName is string s && !string.IsNullOrEmpty(s))
                    ParameterNameBox.Text = s;
                if (config.TryGetValue(ValueKey, out var rawVal) && rawVal is string v && !string.IsNullOrEmpty(v))
                    ValueBox.Text = v;

                // LPS mode flag
                if (TryGetBool(config, LpsModeKey, out bool isLps) && isLps)
                {
                    ElModeCheck.IsChecked  = false;
                    LpsModeCheck.IsChecked = true;
                    ElPanel.Visibility  = Visibility.Collapsed;
                    LpsPanel.Visibility = Visibility.Visible;
                }

                // LPS value
                if (config.TryGetValue(LpsValueKey, out var rawLpsVal) && rawLpsVal is string lv && !string.IsNullOrEmpty(lv))
                    LpsValueBox.Text = lv;

                // LPS param rows
                _lpsParams.Clear();
                if (config.TryGetValue(LpsParamsKey, out var rawParams))
                {
                    JArray arr = null;
                    if (rawParams is JArray ja) arr = ja;
                    else if (rawParams is string ps) arr = JArray.Parse(ps);

                    if (arr != null)
                    {
                        foreach (var tok in arr)
                        {
                            var entry = new LpsParamEntry
                            {
                                Name           = tok["name"]?.Value<string>() ?? string.Empty,
                                IsChecked      = tok["checked"]?.Value<bool>() ?? false,
                                UseInnerRange  = tok["innerRange"]?.Value<bool>() ?? false
                            };
                            entry.PropertyChanged += (_, __) => SaveLpsParams();
                            _lpsParams.Add(entry);
                        }
                    }
                }

                // Default: one blank row if list is empty
                if (_lpsParams.Count == 0)
                    AddLpsParamRow(string.Empty, isChecked: true, useInnerRange: false);

                // Üld mode flag (checked after LPS so last-saved mode wins)
                if (TryGetBool(config, UldModeKey, out bool isUld) && isUld)
                {
                    ElModeCheck.IsChecked  = false;
                    LpsModeCheck.IsChecked = false;
                    UldModeCheck.IsChecked = true;
                    ElPanel.Visibility  = Visibility.Collapsed;
                    LpsPanel.Visibility = Visibility.Collapsed;
                    UldPanel.Visibility = Visibility.Visible;
                }

                // Üld field values (restored after category dropdown is populated)
                if (config.TryGetValue(UldParamNameKey, out var rawUldParam) && rawUldParam is string upn)
                    UldParamNameBox.Text = upn;
                if (config.TryGetValue(UldValueKey, out var rawUldVal) && rawUldVal is string uv)
                    UldValueBox.Text = uv;
                if (config.TryGetValue(UldPrefixKey, out var rawUldPfx) && rawUldPfx is string upfx)
                    UldPrefixBox.Text = upfx;
                if (config.TryGetValue(UldSuffixKey, out var rawUldSfx) && rawUldSfx is string usfx)
                    UldSuffixBox.Text = usfx;
                if (config.TryGetValue(UldCategoryKey, out var rawUldCat) && rawUldCat is string ucl)
                {
                    // Match by label — items not populated yet; defer to Loaded
                    _pendingUldCategory = ucl;
                }

                // Heli mode flag
                if (TryGetBool(config, HeliModeKey, out bool isHeli) && isHeli)
                {
                    ElModeCheck.IsChecked   = false;
                    LpsModeCheck.IsChecked  = false;
                    UldModeCheck.IsChecked  = false;
                    HeliModeCheck.IsChecked = true;
                    ElPanel.Visibility   = Visibility.Collapsed;
                    LpsPanel.Visibility  = Visibility.Collapsed;
                    UldPanel.Visibility  = Visibility.Collapsed;
                    HeliPanel.Visibility = Visibility.Visible;
                }

                // Heli field values (restored after category dropdown is populated)
                if (config.TryGetValue(HeliParamNameKey, out var rawHeliParam) && rawHeliParam is string hpn)
                    HeliParamNameBox.Text = hpn;
                if (config.TryGetValue(HeliValue1Key, out var rawHeliV1) && rawHeliV1 is string hv1)
                    HeliValue1Box.Text = hv1;
                if (config.TryGetValue(HeliValue2Key, out var rawHeliV2) && rawHeliV2 is string hv2)
                    HeliValue2Box.Text = hv2;
                if (config.TryGetValue(HeliCategoryKey, out var rawHeliCat) && rawHeliCat is string hcl)
                {
                    // Match by label — items not populated yet; defer to Loaded
                    _pendingHeliCategory = hcl;
                }

                // ATS mode flag
                if (TryGetBool(config, AtsModeKey, out bool isAts) && isAts)
                {
                    ElModeCheck.IsChecked   = false;
                    LpsModeCheck.IsChecked  = false;
                    UldModeCheck.IsChecked  = false;
                    HeliModeCheck.IsChecked = false;
                    AtsModeCheck.IsChecked  = true;
                    ElPanel.Visibility   = Visibility.Collapsed;
                    LpsPanel.Visibility  = Visibility.Collapsed;
                    UldPanel.Visibility  = Visibility.Collapsed;
                    HeliPanel.Visibility = Visibility.Collapsed;
                    AtsPanel.Visibility  = Visibility.Visible;
                }

                // ATS field values (restored after category dropdown is populated)
                if (config.TryGetValue(AtsParamNameKey, out var rawAtsParam) && rawAtsParam is string atsPN)
                    AtsParamNameBox.Text = atsPN;
                if (config.TryGetValue(AtsValueKey, out var rawAtsVal) && rawAtsVal is string atsV)
                    AtsValueBox.Text = atsV;
                if (config.TryGetValue(AtsCharCountKey, out var rawAtsCC) && rawAtsCC is string atsCC)
                    AtsCharCountBox.Text = atsCC;
                if (config.TryGetValue(AtsPrefixKey, out var rawAtsPfx) && rawAtsPfx is string atsPfx)
                    AtsPrefixBox.Text = atsPfx;
                if (config.TryGetValue(AtsSuffixKey, out var rawAtsSfx) && rawAtsSfx is string atsSfx)
                    AtsSuffixBox.Text = atsSfx;
                if (config.TryGetValue(AtsParamName2Key, out var rawAtsPN2) && rawAtsPN2 is string atsPN2)
                    AtsParamName2Box.Text = atsPN2;
                if (config.TryGetValue(AtsFixedValueKey, out var rawAtsFV) && rawAtsFV is string atsFV)
                    AtsFixedValueBox.Text = atsFV;
                if (TryGetBool(config, AtsParam2EnabledKey, out bool atsP2) && atsP2)
                {
                    AtsParam2EnabledCheck.IsChecked = true;
                    AtsParam2Panel.Visibility = Visibility.Visible;
                }
                if (config.TryGetValue(AtsCategoryKey, out var rawAtsCat) && rawAtsCat is string atsCL)
                {
                    // Match by label — items not populated yet; defer to Loaded
                    _pendingAtsCategory = atsCL;
                }

                // Direction state
                if (TryGetBool(config, ElDirectionKey, out bool elDown) && elDown)
                { ElDirectionUpCheck.IsChecked = false; ElDirectionDownCheck.IsChecked = true; }
                if (TryGetBool(config, LpsDirectionKey, out bool lpsDown) && lpsDown)
                { LpsDirectionUpCheck.IsChecked = false; LpsDirectionDownCheck.IsChecked = true; }
                if (TryGetBool(config, UldDirectionKey, out bool uldDown) && uldDown)
                { UldDirectionUpCheck.IsChecked = false; UldDirectionDownCheck.IsChecked = true; }
                if (TryGetBool(config, AtsDirectionKey, out bool atsDown) && atsDown)
                { AtsDirectionUpCheck.IsChecked = false; AtsDirectionDownCheck.IsChecked = true; }
                // Freeze state
                if (TryGetBool(config, ElFreezeKey,  out bool elFreeze)  && elFreeze)  ElFreezeCheck.IsChecked  = true;
                if (TryGetBool(config, LpsFreezeKey, out bool lpsFreeze) && lpsFreeze) LpsFreezeCheck.IsChecked = true;
                if (TryGetBool(config, UldFreezeKey, out bool uldFreeze) && uldFreeze) UldFreezeCheck.IsChecked = true;
                if (TryGetBool(config, AtsFreezeKey, out bool atsFreeze) && atsFreeze) AtsFreezeCheck.IsChecked = true;
            }
            catch { }
        }

        // Saved category label to restore after PopulateUldCategories runs
        private string _pendingUldCategory;
        // Saved category label to restore after PopulateHeliCategories runs
        private string _pendingHeliCategory;
        // Saved category label to restore after PopulateAtsCategories runs
        private string _pendingAtsCategory;

        #region Direction Toggle Handlers

        private void ElDirectionUpCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElDirectionDownCheck != null) ElDirectionDownCheck.IsChecked = false;
            try { var c = LoadConfig(); c[ElDirectionKey] = false; SaveConfig(c); } catch { }
        }

        private void ElDirectionDownCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElDirectionUpCheck != null) ElDirectionUpCheck.IsChecked = false;
            try { var c = LoadConfig(); c[ElDirectionKey] = true; SaveConfig(c); } catch { }
        }

        private void LpsDirectionUpCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (LpsDirectionDownCheck != null) LpsDirectionDownCheck.IsChecked = false;
            try { var c = LoadConfig(); c[LpsDirectionKey] = false; SaveConfig(c); } catch { }
        }

        private void LpsDirectionDownCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (LpsDirectionUpCheck != null) LpsDirectionUpCheck.IsChecked = false;
            try { var c = LoadConfig(); c[LpsDirectionKey] = true; SaveConfig(c); } catch { }
        }

        private void UldDirectionUpCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (UldDirectionDownCheck != null) UldDirectionDownCheck.IsChecked = false;
            try { var c = LoadConfig(); c[UldDirectionKey] = false; SaveConfig(c); } catch { }
        }

        private void UldDirectionDownCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (UldDirectionUpCheck != null) UldDirectionUpCheck.IsChecked = false;
            try { var c = LoadConfig(); c[UldDirectionKey] = true; SaveConfig(c); } catch { }
        }

        private void ElFreezeCheck_Checked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[ElFreezeKey]  = true;  SaveConfig(c); } catch { } }
        private void ElFreezeCheck_Unchecked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[ElFreezeKey]  = false; SaveConfig(c); } catch { } }

        private void LpsFreezeCheck_Checked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[LpsFreezeKey] = true;  SaveConfig(c); } catch { } }
        private void LpsFreezeCheck_Unchecked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[LpsFreezeKey] = false; SaveConfig(c); } catch { } }

        private void UldFreezeCheck_Checked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[UldFreezeKey] = true;  SaveConfig(c); } catch { } }
        private void UldFreezeCheck_Unchecked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[UldFreezeKey] = false; SaveConfig(c); } catch { } }

        private void AtsDirectionUpCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (AtsDirectionDownCheck != null) AtsDirectionDownCheck.IsChecked = false;
            try { var c = LoadConfig(); c[AtsDirectionKey] = false; SaveConfig(c); } catch { }
        }

        private void AtsDirectionDownCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (AtsDirectionUpCheck != null) AtsDirectionUpCheck.IsChecked = false;
            try { var c = LoadConfig(); c[AtsDirectionKey] = true; SaveConfig(c); } catch { }
        }

        private void AtsFreezeCheck_Checked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[AtsFreezeKey] = true;  SaveConfig(c); } catch { } }
        private void AtsFreezeCheck_Unchecked(object sender, RoutedEventArgs e)
        { try { var c = LoadConfig(); c[AtsFreezeKey] = false; SaveConfig(c); } catch { } }

        private void AtsParam2EnabledCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (AtsParam2Panel != null) AtsParam2Panel.Visibility = Visibility.Visible;
            try { var c = LoadConfig(); c[AtsParam2EnabledKey] = true; SaveConfig(c); } catch { }
        }

        private void AtsParam2EnabledCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (AtsParam2Panel != null) AtsParam2Panel.Visibility = Visibility.Collapsed;
            try { var c = LoadConfig(); c[AtsParam2EnabledKey] = false; SaveConfig(c); } catch { }
        }

        #endregion

        private void SaveLpsParams()
        {
            try
            {
                var cfg = LoadConfig();
                var arr = new JArray(_lpsParams.Select(p =>
                    new JObject(
                        new JProperty("name",       p.Name),
                        new JProperty("checked",    p.IsChecked),
                        new JProperty("innerRange", p.UseInnerRange))));
                cfg[LpsParamsKey] = arr;
                SaveConfig(cfg);
            }
            catch { }
        }

        private void AddLpsParamRow(string name, bool isChecked, bool useInnerRange = false)
        {
            var entry = new LpsParamEntry { Name = name, IsChecked = isChecked, UseInnerRange = useInnerRange };
            entry.PropertyChanged += (_, __) => SaveLpsParams();
            _lpsParams.Add(entry);
        }

        #region Mode Toggle

        private void ElModeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (LpsModeCheck == null) return;
            LpsModeCheck.IsChecked  = false;
            UldModeCheck.IsChecked  = false;
            HeliModeCheck.IsChecked = false;
            AtsModeCheck.IsChecked  = false;
            ElPanel.Visibility   = Visibility.Visible;
            LpsPanel.Visibility  = Visibility.Collapsed;
            UldPanel.Visibility  = Visibility.Collapsed;
            HeliPanel.Visibility = Visibility.Collapsed;
            AtsPanel.Visibility  = Visibility.Collapsed;
            try
            {
                var cfg = LoadConfig();
                cfg[LpsModeKey]  = false;
                cfg[UldModeKey]  = false;
                cfg[HeliModeKey] = false;
                cfg[AtsModeKey]  = false;
                SaveConfig(cfg);
            }
            catch { }
        }

        private void LpsModeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElModeCheck == null) return;
            ElModeCheck.IsChecked   = false;
            UldModeCheck.IsChecked  = false;
            HeliModeCheck.IsChecked = false;
            AtsModeCheck.IsChecked  = false;
            ElPanel.Visibility   = Visibility.Collapsed;
            LpsPanel.Visibility  = Visibility.Visible;
            UldPanel.Visibility  = Visibility.Collapsed;
            HeliPanel.Visibility = Visibility.Collapsed;
            AtsPanel.Visibility  = Visibility.Collapsed;
            try
            {
                var cfg = LoadConfig();
                cfg[LpsModeKey]  = true;
                cfg[UldModeKey]  = false;
                cfg[HeliModeKey] = false;
                cfg[AtsModeKey]  = false;
                SaveConfig(cfg);
            }
            catch { }
        }

        private void UldModeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElModeCheck == null) return;
            ElModeCheck.IsChecked   = false;
            LpsModeCheck.IsChecked  = false;
            HeliModeCheck.IsChecked = false;
            AtsModeCheck.IsChecked  = false;
            ElPanel.Visibility   = Visibility.Collapsed;
            LpsPanel.Visibility  = Visibility.Collapsed;
            UldPanel.Visibility  = Visibility.Visible;
            HeliPanel.Visibility = Visibility.Collapsed;
            AtsPanel.Visibility  = Visibility.Collapsed;
            try
            {
                var cfg = LoadConfig();
                cfg[LpsModeKey]  = false;
                cfg[UldModeKey]  = true;
                cfg[HeliModeKey] = false;
                cfg[AtsModeKey]  = false;
                SaveConfig(cfg);
            }
            catch { }
        }

        private void HeliModeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElModeCheck == null) return;
            ElModeCheck.IsChecked  = false;
            LpsModeCheck.IsChecked = false;
            UldModeCheck.IsChecked = false;
            AtsModeCheck.IsChecked = false;
            ElPanel.Visibility   = Visibility.Collapsed;
            LpsPanel.Visibility  = Visibility.Collapsed;
            UldPanel.Visibility  = Visibility.Collapsed;
            HeliPanel.Visibility = Visibility.Visible;
            AtsPanel.Visibility  = Visibility.Collapsed;
            try
            {
                var cfg = LoadConfig();
                cfg[LpsModeKey]  = false;
                cfg[UldModeKey]  = false;
                cfg[HeliModeKey] = true;
                cfg[AtsModeKey]  = false;
                SaveConfig(cfg);
            }
            catch { }
        }

        private void AtsModeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (ElModeCheck == null) return;
            ElModeCheck.IsChecked   = false;
            LpsModeCheck.IsChecked  = false;
            UldModeCheck.IsChecked  = false;
            HeliModeCheck.IsChecked = false;
            ElPanel.Visibility   = Visibility.Collapsed;
            LpsPanel.Visibility  = Visibility.Collapsed;
            UldPanel.Visibility  = Visibility.Collapsed;
            HeliPanel.Visibility = Visibility.Collapsed;
            AtsPanel.Visibility  = Visibility.Visible;
            try
            {
                var cfg = LoadConfig();
                cfg[LpsModeKey]  = false;
                cfg[UldModeKey]  = false;
                cfg[HeliModeKey] = false;
                cfg[AtsModeKey]  = true;
                SaveConfig(cfg);
            }
            catch { }
        }

        #endregion

        #region LPS Param List

        private void LpsAddParam_Click(object sender, RoutedEventArgs e)
        {
            AddLpsParamRow(string.Empty, isChecked: false, useInnerRange: false);
            SaveLpsParams();
        }

        private void LpsRemoveParam_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is LpsParamEntry entry)
            {
                _lpsParams.Remove(entry);
                SaveLpsParams();
            }
        }

        #endregion

        #region LPS Select Button

        private void LpsSelectButton_Click(object sender, RoutedEventArgs e)
        {
            var activeSpecs = _lpsParams
                .Where(p => p.IsChecked && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new Services.Revit.LpsParamSpec(p.Name.Trim(), p.UseInnerRange))
                .ToList();

            if (activeSpecs.Count == 0)
            {
                LpsResultText.Text = "Please check at least one parameter row and enter its name.";
                return;
            }

            string value = LpsValueBox.Text;

            // Persist value
            try
            {
                var cfg = LoadConfig();
                cfg[LpsValueKey] = value;
                SaveConfig(cfg);
            }
            catch { }

            LpsResultText.Text        = string.Empty;
            LpsSelectButton.IsEnabled = false;

            this.Hide();

            // Show the floating status window next to the main window
            var statusWindow = new LpsStatusWindow();
            statusWindow.UpdateStatus(activeSpecs.Select(s => (s.Name, value)), 0);
            statusWindow.Show();
            statusWindow.PositionNear(this.Left, this.Top, this.Width, this.Height);

            var request = new Services.Revit.LpsParameterRequest(
                activeSpecs,
                value,
                LpsDirectionDownCheck.IsChecked == true,
                LpsFreezeCheck.IsChecked == true,
                (result, nextValue) =>
                {
                    statusWindow.Close();

                    this.Show();
                    this.Activate();
                    LpsResultText.Text        = result;
                    LpsSelectButton.IsEnabled = true;

                    if (nextValue != null)
                    {
                        LpsValueBox.Text = nextValue;
                        try
                        {
                            var cfg = LoadConfig();
                            cfg[LpsValueKey] = nextValue;
                            SaveConfig(cfg);
                        }
                        catch { }
                    }
                },
                onStatusUpdate: (paramValues, pickCount) =>
                    statusWindow.UpdateStatus(paramValues, pickCount),
                registerNudge: handler => statusWindow.NudgeRequested = handler);

            _externalEventService.Raise(request);
        }

        #endregion

        #region Üld Mode

        private void PopulateUldCategories()
        {
            var items = new[]
            {
                new UldCategoryItem("Communication Devices",    Autodesk.Revit.DB.BuiltInCategory.OST_CommunicationDevices),
                new UldCategoryItem("Conduit",                   Autodesk.Revit.DB.BuiltInCategory.OST_Conduit),
                new UldCategoryItem("Data Devices",              Autodesk.Revit.DB.BuiltInCategory.OST_DataDevices),
                new UldCategoryItem("Detail Items",               Autodesk.Revit.DB.BuiltInCategory.OST_DetailComponents),
                new UldCategoryItem("Doors",                     Autodesk.Revit.DB.BuiltInCategory.OST_Doors),
                new UldCategoryItem("Electrical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalEquipment),
                new UldCategoryItem("Electrical Fixtures",       Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalFixtures),
                new UldCategoryItem("Fire Alarm Devices",        Autodesk.Revit.DB.BuiltInCategory.OST_FireAlarmDevices),
                new UldCategoryItem("Floors",                    Autodesk.Revit.DB.BuiltInCategory.OST_Floors),
                new UldCategoryItem("Furniture",                 Autodesk.Revit.DB.BuiltInCategory.OST_Furniture),
                new UldCategoryItem("Generic Models",            Autodesk.Revit.DB.BuiltInCategory.OST_GenericModel),
                new UldCategoryItem("Lighting Fixtures",         Autodesk.Revit.DB.BuiltInCategory.OST_LightingFixtures),
                new UldCategoryItem("Mechanical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_MechanicalEquipment),
                new UldCategoryItem("Pipes",                     Autodesk.Revit.DB.BuiltInCategory.OST_PipeCurves),
                new UldCategoryItem("Rooms",                     Autodesk.Revit.DB.BuiltInCategory.OST_Rooms),
                new UldCategoryItem("Security Devices",          Autodesk.Revit.DB.BuiltInCategory.OST_SecurityDevices),
                new UldCategoryItem("Structural Columns",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralColumns),
                new UldCategoryItem("Structural Framing",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFraming),
                new UldCategoryItem("Text Notes",                 Autodesk.Revit.DB.BuiltInCategory.OST_TextNotes,          isTextNote: true),
                new UldCategoryItem("Walls",                     Autodesk.Revit.DB.BuiltInCategory.OST_Walls),
                new UldCategoryItem("Windows",                   Autodesk.Revit.DB.BuiltInCategory.OST_Windows),
            };
            UldCategoryCombo.ItemsSource = items;
            UldCategoryCombo.SelectedIndex = 0;
        }

        private void UldCategoryCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isDataLoaded) return;
            bool isTextNote = (UldCategoryCombo.SelectedItem as UldCategoryItem)?.IsTextNote == true;
            var vis = isTextNote ? Visibility.Collapsed : Visibility.Visible;
            UldParamNameLabel.Visibility = vis;
            UldParamNameBox.Visibility   = vis;
        }

        private void UldSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(UldCategoryCombo.SelectedItem is UldCategoryItem catItem))
            {
                UldResultText.Text = "Please select a category.";
                return;
            }

            bool isTextNote = catItem.IsTextNote;
            string paramName = null;
            if (!isTextNote)
            {
                paramName = UldParamNameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(paramName))
                {
                    UldResultText.Text = "Please enter a parameter name.";
                    return;
                }
            }

            string value  = UldValueBox.Text;
            string prefix = UldPrefixBox.Text;
            string suffix = UldSuffixBox.Text;

            // Persist state
            try
            {
                var cfg = LoadConfig();
                cfg[UldCategoryKey]  = catItem.Label;
                cfg[UldParamNameKey] = paramName ?? string.Empty;
                cfg[UldValueKey]     = value;
                cfg[UldPrefixKey]    = prefix;
                cfg[UldSuffixKey]    = suffix;
                SaveConfig(cfg);
            }
            catch { }

            UldResultText.Text        = string.Empty;
            UldSelectButton.IsEnabled = false;

            this.Hide();

            // Show floating status window
            string statusLabel = isTextNote ? "Text" : paramName;
            var statusWindow = new LpsStatusWindow();
            statusWindow.UpdateStatus(new[] { (statusLabel, prefix + value + suffix) }, 0);
            statusWindow.Show();
            statusWindow.PositionNear(this.Left, this.Top, this.Width, this.Height);

            var request = new Services.Revit.UldParameterRequest(
                catItem.Category,
                paramName,
                value,
                prefix,
                suffix,
                UldDirectionDownCheck.IsChecked == true,
                UldFreezeCheck.IsChecked == true,
                (result, nextValue) =>
                {
                    statusWindow.Close();

                    this.Show();
                    this.Activate();
                    UldResultText.Text        = result;
                    UldSelectButton.IsEnabled = true;

                    if (nextValue != null)
                    {
                        UldValueBox.Text = nextValue;
                        try
                        {
                            var cfg = LoadConfig();
                            cfg[UldValueKey] = nextValue;
                            SaveConfig(cfg);
                        }
                        catch { }
                    }
                },
                onStatusUpdate: (paramValues, pickCount) =>
                    statusWindow.UpdateStatus(paramValues, pickCount),
                registerNudge: handler => statusWindow.NudgeRequested = handler);

            _externalEventService.Raise(request);
        }

        #endregion

        #region Heli Mode

        private void PopulateHeliCategories()
        {
            var items = new[]
            {
                new UldCategoryItem("Communication Devices",    Autodesk.Revit.DB.BuiltInCategory.OST_CommunicationDevices),
                new UldCategoryItem("Conduit",                   Autodesk.Revit.DB.BuiltInCategory.OST_Conduit),
                new UldCategoryItem("Data Devices",              Autodesk.Revit.DB.BuiltInCategory.OST_DataDevices),
                new UldCategoryItem("Detail Items",               Autodesk.Revit.DB.BuiltInCategory.OST_DetailComponents),
                new UldCategoryItem("Doors",                     Autodesk.Revit.DB.BuiltInCategory.OST_Doors),
                new UldCategoryItem("Electrical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalEquipment),
                new UldCategoryItem("Electrical Fixtures",       Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalFixtures),
                new UldCategoryItem("Fire Alarm Devices",        Autodesk.Revit.DB.BuiltInCategory.OST_FireAlarmDevices),
                new UldCategoryItem("Floors",                    Autodesk.Revit.DB.BuiltInCategory.OST_Floors),
                new UldCategoryItem("Furniture",                 Autodesk.Revit.DB.BuiltInCategory.OST_Furniture),
                new UldCategoryItem("Generic Models",            Autodesk.Revit.DB.BuiltInCategory.OST_GenericModel),
                new UldCategoryItem("Lighting Fixtures",         Autodesk.Revit.DB.BuiltInCategory.OST_LightingFixtures),
                new UldCategoryItem("Mechanical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_MechanicalEquipment),
                new UldCategoryItem("Pipes",                     Autodesk.Revit.DB.BuiltInCategory.OST_PipeCurves),
                new UldCategoryItem("Rooms",                     Autodesk.Revit.DB.BuiltInCategory.OST_Rooms),
                new UldCategoryItem("Security Devices",          Autodesk.Revit.DB.BuiltInCategory.OST_SecurityDevices),
                new UldCategoryItem("Structural Columns",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralColumns),
                new UldCategoryItem("Structural Framing",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFraming),
                new UldCategoryItem("Walls",                     Autodesk.Revit.DB.BuiltInCategory.OST_Walls),
                new UldCategoryItem("Windows",                   Autodesk.Revit.DB.BuiltInCategory.OST_Windows),
            };
            HeliCategoryCombo.ItemsSource = items;
            HeliCategoryCombo.SelectedIndex = 0;
        }

        private void HeliCategoryCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isDataLoaded) return;
            if (HeliCategoryCombo.SelectedItem is UldCategoryItem catItem)
            {
                try
                {
                    var cfg = LoadConfig();
                    cfg[HeliCategoryKey] = catItem.Label;
                    SaveConfig(cfg);
                }
                catch { }
            }
        }

        private void HeliSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(HeliCategoryCombo.SelectedItem is UldCategoryItem catItem))
            {
                HeliResultText.Text = "Please select a category.";
                return;
            }

            string paramName = HeliParamNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(paramName))
            {
                HeliResultText.Text = "Please enter a parameter name.";
                return;
            }

            string value1 = HeliValue1Box.Text;
            string value2 = HeliValue2Box.Text;

            // Persist state
            try
            {
                var cfg = LoadConfig();
                cfg[HeliCategoryKey]  = catItem.Label;
                cfg[HeliParamNameKey] = paramName;
                cfg[HeliValue1Key]    = value1;
                cfg[HeliValue2Key]    = value2;
                SaveConfig(cfg);
            }
            catch { }

            HeliResultText.Text        = string.Empty;
            HeliSelectButton.IsEnabled = false;

            this.Hide();

            var statusWindow = new LpsStatusWindow();
            statusWindow.UpdateStatus(new[] { (paramName, value1) }, 0);
            statusWindow.Show();
            statusWindow.PositionNear(this.Left, this.Top, this.Width, this.Height);

            var request = new Services.Revit.HeliParameterRequest(
                catItem.Category,
                paramName,
                value1,
                value2,
                result =>
                {
                    statusWindow.Close();

                    this.Show();
                    this.Activate();
                    HeliResultText.Text        = result;
                    HeliSelectButton.IsEnabled = true;
                },
                onStatusUpdate: (paramValues, pickCount) =>
                    statusWindow.UpdateStatus(paramValues, pickCount));

            _externalEventService.Raise(request);
        }

        #endregion

        #region ATS Mode

        private void PopulateAtsCategories()
        {
            var items = new[]
            {
                new UldCategoryItem("Communication Devices",    Autodesk.Revit.DB.BuiltInCategory.OST_CommunicationDevices),
                new UldCategoryItem("Conduit",                   Autodesk.Revit.DB.BuiltInCategory.OST_Conduit),
                new UldCategoryItem("Data Devices",              Autodesk.Revit.DB.BuiltInCategory.OST_DataDevices),
                new UldCategoryItem("Detail Items",               Autodesk.Revit.DB.BuiltInCategory.OST_DetailComponents),
                new UldCategoryItem("Doors",                     Autodesk.Revit.DB.BuiltInCategory.OST_Doors),
                new UldCategoryItem("Electrical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalEquipment),
                new UldCategoryItem("Electrical Fixtures",       Autodesk.Revit.DB.BuiltInCategory.OST_ElectricalFixtures),
                new UldCategoryItem("Fire Alarm Devices",        Autodesk.Revit.DB.BuiltInCategory.OST_FireAlarmDevices),
                new UldCategoryItem("Floors",                    Autodesk.Revit.DB.BuiltInCategory.OST_Floors),
                new UldCategoryItem("Furniture",                 Autodesk.Revit.DB.BuiltInCategory.OST_Furniture),
                new UldCategoryItem("Generic Models",            Autodesk.Revit.DB.BuiltInCategory.OST_GenericModel),
                new UldCategoryItem("Lighting Fixtures",         Autodesk.Revit.DB.BuiltInCategory.OST_LightingFixtures),
                new UldCategoryItem("Mechanical Equipment",      Autodesk.Revit.DB.BuiltInCategory.OST_MechanicalEquipment),
                new UldCategoryItem("Pipes",                     Autodesk.Revit.DB.BuiltInCategory.OST_PipeCurves),
                new UldCategoryItem("Rooms",                     Autodesk.Revit.DB.BuiltInCategory.OST_Rooms),
                new UldCategoryItem("Security Devices",          Autodesk.Revit.DB.BuiltInCategory.OST_SecurityDevices),
                new UldCategoryItem("Structural Columns",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralColumns),
                new UldCategoryItem("Structural Framing",        Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFraming),
                new UldCategoryItem("Text Notes",                 Autodesk.Revit.DB.BuiltInCategory.OST_TextNotes,          isTextNote: true),
                new UldCategoryItem("Walls",                     Autodesk.Revit.DB.BuiltInCategory.OST_Walls),
                new UldCategoryItem("Windows",                   Autodesk.Revit.DB.BuiltInCategory.OST_Windows),
            };
            AtsCategoryCombo.ItemsSource = items;
            AtsCategoryCombo.SelectedIndex = 0;
        }

        private void AtsCategoryCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isDataLoaded) return;
            bool isTextNote = (AtsCategoryCombo.SelectedItem as UldCategoryItem)?.IsTextNote == true;
            var vis = isTextNote ? Visibility.Collapsed : Visibility.Visible;
            AtsParamNameLabel.Visibility = vis;
            AtsParamNameBox.Visibility   = vis;
        }

        private void AtsSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(AtsCategoryCombo.SelectedItem is UldCategoryItem catItem))
            {
                AtsResultText.Text = "Please select a category.";
                return;
            }

            bool isTextNote = catItem.IsTextNote;
            string paramName = null;
            if (!isTextNote)
            {
                paramName = AtsParamNameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(paramName))
                {
                    AtsResultText.Text = "Please enter a parameter name.";
                    return;
                }
            }

            string value     = AtsValueBox.Text;
            string charCountText = AtsCharCountBox.Text.Trim();
            string prefix    = AtsPrefixBox.Text;
            string suffix    = AtsSuffixBox.Text;
            string paramName2   = (AtsParam2EnabledCheck.IsChecked == true) ? AtsParamName2Box.Text.Trim() : string.Empty;
            string fixedValue   = AtsFixedValueBox.Text;

            int charCount = 0;
            if (!string.IsNullOrEmpty(charCountText) && !int.TryParse(charCountText, out charCount))
            {
                AtsResultText.Text = "Char Count must be a whole number.";
                return;
            }

            // Persist state
            try
            {
                var cfg = LoadConfig();
                cfg[AtsCategoryKey]  = catItem.Label;
                cfg[AtsParamNameKey] = paramName ?? string.Empty;
                cfg[AtsValueKey]     = value;
                cfg[AtsCharCountKey] = charCountText;
                cfg[AtsPrefixKey]    = prefix;
                cfg[AtsSuffixKey]    = suffix;
                cfg[AtsParamName2Key]  = paramName2;
                cfg[AtsFixedValueKey]  = fixedValue;
                cfg[AtsParam2EnabledKey] = AtsParam2EnabledCheck.IsChecked == true;
                SaveConfig(cfg);
            }
            catch { }

            AtsResultText.Text        = string.Empty;
            AtsSelectButton.IsEnabled = false;

            this.Hide();

            // Show floating status window
            string statusLabel = isTextNote ? "Text" : paramName;
            var statusWindow = new LpsStatusWindow();
            statusWindow.UpdateStatus(new[] { (statusLabel, FormatAtsValue(value, charCount, prefix) + suffix) }, 0);
            statusWindow.Show();
            statusWindow.PositionNear(this.Left, this.Top, this.Width, this.Height);

            var request = new Services.Revit.AtsParameterRequest(
                catItem.Category,
                paramName,
                value,
                charCount,
                prefix,
                suffix,
                paramName2,
                fixedValue,
                AtsDirectionDownCheck.IsChecked == true,
                AtsFreezeCheck.IsChecked == true,
                (result, nextValue) =>
                {
                    statusWindow.Close();

                    this.Show();
                    this.Activate();
                    AtsResultText.Text        = result;
                    AtsSelectButton.IsEnabled = true;

                    if (nextValue != null)
                    {
                        AtsValueBox.Text = nextValue;
                        try
                        {
                            var cfg = LoadConfig();
                            cfg[AtsValueKey] = nextValue;
                            SaveConfig(cfg);
                        }
                        catch { }
                    }
                },
                onStatusUpdate: (paramValues, pickCount) =>
                    statusWindow.UpdateStatus(paramValues, pickCount),
                registerNudge: handler => statusWindow.NudgeRequested = handler);

            _externalEventService.Raise(request);
        }

        /// <summary>
        /// Formats a numeric value with left-padding using the first character of <paramref name="fillStr"/>
        /// to reach <paramref name="charCount"/> total characters.
        /// </summary>
        private static string FormatAtsValue(string value, int charCount, string fillStr)
        {
            if (charCount <= 0 || string.IsNullOrEmpty(fillStr))
                return value;
            return value.PadLeft(charCount, fillStr[0]);
        }

        #endregion

        #region Select Button

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            string paramName = ParameterNameBox.Text.Trim();
            string value     = ValueBox.Text;   // keep whitespace in value intentionally

            if (string.IsNullOrWhiteSpace(paramName))
            {
                ResultText.Text = "Please enter a parameter name.";
                return;
            }

            // Persist parameter name and starting value
            try
            {
                var cfg = LoadConfig();
                cfg[ParameterNameKey] = paramName;
                cfg[ValueKey]         = value;
                SaveConfig(cfg);
            }
            catch { }

            ResultText.Text        = string.Empty;
            SelectButton.IsEnabled = false;

            // Hide the window so Revit's selection mode is not obstructed.
            this.Hide();

            var request = new Services.Revit.SetCircuitParameterRequest(
                paramName,
                value,
                ElDirectionDownCheck.IsChecked == true,
                ElFreezeCheck.IsChecked == true,
                (result, nextValue) =>
                {
                    // Execute() runs on Revit's main thread — safe to update WPF directly.
                    this.Show();
                    this.Activate();
                    ResultText.Text        = result;
                    SelectButton.IsEnabled = true;
                    if (nextValue != null)
                    {
                        ValueBox.Text = nextValue;
                        try
                        {
                            var cfg = LoadConfig();
                            cfg[ValueKey] = nextValue;
                            SaveConfig(cfg);
                        }
                        catch { }
                    }
                });

            _externalEventService.Raise(request);
        }

        #endregion

        #region Window chrome / resize handlers

        private void TitleBar_Loaded(object sender, RoutedEventArgs e) { }

        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e)         => Cursor = Cursors.SizeWE;
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e)        => Cursor = Cursors.SizeWE;
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e)       => Cursor = Cursors.SizeNS;
        private void Edge_MouseLeave(object sender, MouseEventArgs e)             => Cursor = Cursors.Arrow;
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNESW;
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e)=> Cursor = Cursors.SizeNWSE;

        private void Window_MouseMove(object sender, MouseEventArgs e)                               => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)                 => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)             => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)            => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)           => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)     => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)    => _windowResizer.StartResizing(e, ResizeDirection.BottomRight);

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) { }
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)           { }
        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)             { }

        #endregion

        #region Window Startup

        private void DeferWindowShow()
        {
            Opacity = 0;
            Loaded += RenumberWindow_Loaded;
        }

        private void RenumberWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TryShowWindow();
        }

        private void TryShowWindow()
        {
            if (!_isDataLoaded) return;
            Opacity = 1;
        }

        #endregion

        #region Theme

        private ResourceDictionary _currentThemeDictionary;

        private void LoadTheme()
        {
            try
            {
                var themeUri = new Uri(_isDarkMode
                    ? "pack://application:,,,/Renumber;component/UI/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/Renumber;component/UI/Themes/LightTheme.xaml",
                    UriKind.Absolute);

                var newDict = new ResourceDictionary { Source = themeUri };

                if (_currentThemeDictionary != null)
                    Resources.MergedDictionaries.Remove(_currentThemeDictionary);

                Resources.MergedDictionaries.Add(newDict);
                _currentThemeDictionary = newDict;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading theme: {ex.Message}");
            }

        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = ThemeToggleButton.IsChecked == true;
            LoadTheme();
            SaveThemeState();

            var icon = ThemeToggleButton?.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                       as MaterialDesignThemes.Wpf.PackIcon;
            if (icon != null)
            {
                icon.Kind = _isDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }
        }

        private void LoadThemeState()
        {
            try
            {
                var config = LoadConfig();
                if (TryGetBool(config, "IsDarkMode", out bool isDark))
                    _isDarkMode = isDark;
            }
            catch { }

            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.IsChecked = _isDarkMode;
                var icon = ThemeToggleButton.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                           as MaterialDesignThemes.Wpf.PackIcon;
                if (icon != null)
                {
                    icon.Kind = _isDarkMode
                        ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                        : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
                }
            }

            LoadTheme();
        }

        private void SaveThemeState()
        {
            try
            {
                var config = LoadConfig();
                config["IsDarkMode"] = _isDarkMode;
                SaveConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Window State

        private void LoadWindowState()
        {
            try
            {
                var config   = LoadConfig();
                bool hasLeft  = TryGetDouble(config, WindowLeftKey,   out double left);
                bool hasTop   = TryGetDouble(config, WindowTopKey,    out double top);
                bool hasWidth = TryGetDouble(config, WindowWidthKey,  out double width);
                bool hasHeight= TryGetDouble(config, WindowHeightKey, out double height);

                bool hasSize = hasWidth && hasHeight && width > 0 && height > 0;
                bool hasPos  = hasLeft  && hasTop   && !double.IsNaN(left) && !double.IsNaN(top);

                if (!hasSize && !hasPos) return;

                WindowStartupLocation = WindowStartupLocation.Manual;

                if (hasSize)
                {
                    Width  = Math.Max(MinWidth,  width);
                    Height = Math.Max(MinHeight, height);
                }

                if (hasPos)
                {
                    Left = left;
                    Top  = top;
                }
            }
            catch { }
        }

        private void SaveWindowState()
        {
            try
            {
                var config = LoadConfig();
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                config[WindowLeftKey]   = bounds.Left;
                config[WindowTopKey]    = bounds.Top;
                config[WindowWidthKey]  = bounds.Width;
                config[WindowHeightKey] = bounds.Height;

                SaveConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save window state: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Config Helpers

        private Dictionary<string, object> LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json   = File.ReadAllText(ConfigFilePath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (config != null) return config;
                }
            }
            catch { }

            return new Dictionary<string, object>();
        }

        private void SaveConfig(Dictionary<string, object> config)
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private static bool TryGetBool(Dictionary<string, object> config, string key, out bool value)
        {
            value = false;
            if (!config.TryGetValue(key, out var raw) || raw == null) return false;

            if (raw is bool boolVal)                                   { value = boolVal; return true; }
            if (raw is JToken t && t.Type == JTokenType.Boolean)      { value = t.Value<bool>(); return true; }
            if (raw is string s && bool.TryParse(s, out var parsed))   { value = parsed; return true; }

            return false;
        }

        private static bool TryGetDouble(Dictionary<string, object> config, string key, out double value)
        {
            value = 0;
            if (!config.TryGetValue(key, out var raw) || raw == null) return false;

            switch (raw)
            {
                case double d:   value = d;         return true;
                case float  f:   value = f;         return true;
                case decimal m:  value = (double)m; return true;
                case long   l:   value = l;         return true;
                case int    i:   value = i;         return true;
                case JToken t when t.Type == JTokenType.Float || t.Type == JTokenType.Integer:
                    value = t.Value<double>(); return true;
                case string s when double.TryParse(s, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var p):
                    value = p; return true;
            }

            return false;
        }

        #endregion
    }

    /// <summary>
    /// Represents a single parameter row in the LPS parameters list.
    /// </summary>
    public sealed class LpsParamEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _isChecked;
        private bool _useInnerRange;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } }
        }

        public bool UseInnerRange
        {
            get => _useInnerRange;
            set { if (_useInnerRange != value) { _useInnerRange = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
