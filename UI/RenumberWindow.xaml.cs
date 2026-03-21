using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion

        #region Fields

        private readonly WindowResizer _windowResizer;
        private bool _isDarkMode = true;
        private bool _isDataLoaded;
        private readonly UIApplication _uiApplication;
        private readonly Services.Revit.RevitExternalEventService _externalEventService;

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

            LoadThemeState();
            LoadWindowState();
            LoadParameterNameState();

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
                if (config.TryGetValue(ParameterNameKey, out var rawName) && rawName is string s && !string.IsNullOrEmpty(s))
                    ParameterNameBox.Text = s;
                if (config.TryGetValue(ValueKey, out var rawVal) && rawVal is string v && !string.IsNullOrEmpty(v))
                    ValueBox.Text = v;
            }
            catch { }
        }

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
}
