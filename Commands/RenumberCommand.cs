using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Runtime.InteropServices;

namespace Renumber.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RenumberCommand : IExternalCommand
    {
        private static UI.RenumberWindow _window;
        private static bool _pendingShow;

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // If window already exists, just surface it
                if (_window != null && _window.IsLoaded)
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                    if (_window.WindowState == System.Windows.WindowState.Minimized)
                        ShowWindow(hwnd, SW_RESTORE);

                    _window.Activate();
                    _window.Focus();
                    SetForegroundWindow(hwnd);
                    return Result.Succeeded;
                }

                // Force load MaterialDesign assemblies
                try { var dummy = new MaterialDesignThemes.Wpf.PaletteHelper(); } catch { }

                // REVIT 2026 FIX: Do NOT create or show the WPF window here.
                // Revit suspends the WPF Dispatcher during IExternalCommand.Execute(),
                // so any WPF rendering (window creation, layout, ObservableCollection updates)
                // will crash with "Dispatcher processing has been suspended".
                //
                // Instead, subscribe to the Idling event. Revit fires Idling when it is
                // truly idle and the Dispatcher is running normally — safe for WPF.
                if (!_pendingShow)
                {
                    _pendingShow = true;
                    commandData.Application.Idling += OnRevitIdling;
                }

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void OnRevitIdling(object sender, IdlingEventArgs e)
        {
            // Unsubscribe immediately — we only need this once
            var uiApp = sender as UIApplication;
            if (uiApp != null)
                uiApp.Idling -= OnRevitIdling;

            _pendingShow = false;

            try
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return;
                }

                _window = new UI.RenumberWindow(uiApp, App.ExternalEventService);
                var owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                new System.Windows.Interop.WindowInteropHelper(_window) { Owner = owner };

                _window.Closed += (s, ev) => { _window = null; };
                _window.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to create Renumber window in Idling handler", ex);
            }
        }
    }
}
