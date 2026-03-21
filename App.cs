using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using System;
using System.IO;
using Renumber.Commands;

namespace Renumber
{
    [AppLoader]
    public class App : IExternalApplication
    {
        public static Services.Revit.RevitExternalEventService ExternalEventService { get; private set; }
        public static Services.SettingsService SettingsService { get; private set; }
        public static Services.ParameterResolver ParameterResolver { get; private set; }
        public static Services.Core.SessionLogger Logger { get; private set; }
        private RibbonPanel ribbonPanel;

        private static readonly string CrashLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RK Tools", "Renumber", "crash.log");

        public Result OnStartup(UIControlledApplication application)
        {
            // Initialize Logger (SessionLogger retains entries in memory for diagnostics)
            Logger = new Services.Core.SessionLogger();
            ExternalEventService = new Services.Revit.RevitExternalEventService(Logger);
            SettingsService = new Services.SettingsService(Logger);
            ParameterResolver = new Services.ParameterResolver(Logger);

            // ---- CRASH DIAGNOSTICS ----
            // Catch ALL unhandled exceptions and write full stack trace to crash.log
            // This prevents the endless popup loop AND gives us diagnostic info.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException += (s, e) =>
                {
                    LogCrash("Dispatcher.UnhandledException", e.Exception);
                    e.Handled = true; // Prevent the endless popup loop
                };
            }
            // ---- END CRASH DIAGNOSTICS ----

            // Define the custom tab name
            string tabName = "RK Tools";

            // Try to create the custom tab (avoid exception if it already exists)
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists; continue without throwing an error
            }

            // Create Ribbon Panel on the custom tab
            ribbonPanel = application.CreateOrSelectPanel(tabName, "Tools");

            // Create PushButton with embedded resource
            var duplicateSheetsButton = ribbonPanel.CreatePushButton<RenumberCommand>()
                .SetLargeImage("pack://application:,,,/Renumber;component/Assets/Renumber.tiff")
                .SetText("Renumber")
                .SetToolTip("Manage sheet duplication and batch renaming.")
                .SetLongDescription("Renumber allows you to duplicate sheets in bulk, rename with find/replace, prefixes/suffixes, and preview changes before applying.");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

        /// <summary>
        /// Writes crash details to a log file for post-mortem analysis.
        /// </summary>
        public static void LogCrash(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath));
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SOURCE: {source}\n" +
                               $"TYPE: {ex?.GetType().FullName}\n" +
                               $"MESSAGE: {ex?.Message}\n" +
                               $"STACK:\n{ex?.StackTrace}\n" +
                               $"INNER: {ex?.InnerException?.Message}\n" +
                               $"INNER STACK:\n{ex?.InnerException?.StackTrace}\n" +
                               new string('=', 80) + "\n";
                File.AppendAllText(CrashLogPath, entry);
            }
            catch { /* Can't log — silently fail */ }
        }
    }
}
