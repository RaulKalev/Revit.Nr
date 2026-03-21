using System;
using System.IO;
using Newtonsoft.Json;
using Renumber.Models;
using Renumber.Services.Core;

namespace Renumber.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly ILogger _logger;

        public SettingsService(ILogger logger)
        {
            _logger = logger;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "RKTools", "DALIManager");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "settings.json");
        }

        public event EventHandler<SettingsModel> OnSettingsSaved;

        public SettingsModel Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<SettingsModel>(json);
                    if (settings != null)
                    {
                        _logger.Info($"Loaded settings version: {settings.Version}");
                        if (settings.Version < 3)
                        {
                            settings.Version = 3;
                            settings.Param_Load = "Renumber mA";
                            settings.Param_AddressCount = "Renumber @";
                            settings.Param_LineId = "Renumber siin";
                            settings.Param_Controller = "Renumber kontroller";
                            Save(settings);
                            _logger.Info("Migrated settings to version 3.");
                        }

                        // Migrate flat SavedLines -> hierarchical SavedControllers
                        if (settings.SavedLines != null && settings.SavedLines.Count > 0
                            && (settings.SavedControllers == null || settings.SavedControllers.Count == 0))
                        {
                            _logger.Info("Migrating flat SavedLines to SavedControllers hierarchy...");
                            settings.SavedControllers = new System.Collections.Generic.List<ControllerDefinition>();

                            var grouped = new System.Collections.Generic.Dictionary<string, ControllerDefinition>();
                            foreach (var line in settings.SavedLines)
                            {
                                string ctrlKey = string.IsNullOrWhiteSpace(line.ControllerName)
                                    ? "Default Controller"
                                    : line.ControllerName.Trim();

                                if (!grouped.TryGetValue(ctrlKey, out var ctrl))
                                {
                                    ctrl = new ControllerDefinition { Name = ctrlKey, Lines = new System.Collections.Generic.List<LineDefinition>() };
                                    grouped[ctrlKey] = ctrl;
                                    settings.SavedControllers.Add(ctrl);
                                }
                                ctrl.Lines.Add(line);
                            }

                            settings.SavedLines.Clear();
                            Save(settings);
                            _logger.Info($"Migrated {settings.SavedControllers.Count} controller(s).");
                        }

                        // Migrate SavedControllers -> hierarchical SavedPanels
                        if (settings.Version < 4)
                        {
                            settings.Version = 4;
                            if (settings.SavedControllers != null && settings.SavedControllers.Count > 0
                                && (settings.SavedPanels == null || settings.SavedPanels.Count == 0))
                            {
                                _logger.Info("Migrating SavedControllers to SavedPanels hierarchy...");
                                settings.SavedPanels = new System.Collections.Generic.List<PanelDefinition>();
                                
                                var defaultPanel = new PanelDefinition { Name = "Panel 1", Controllers = new System.Collections.Generic.List<ControllerDefinition>(settings.SavedControllers) };
                                settings.SavedPanels.Add(defaultPanel);
                                
                                settings.SavedControllers.Clear();
                            }
                            Save(settings);
                            _logger.Info("Migrated settings to version 4.");
                        }

                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load settings. Using defaults.", ex);
            }

            return CreateDefaultIfMissing();
        }

        public void Save(SettingsModel settings)
        {
            try
            {
                // Ensure version is always current before saving
                settings.Version = 4; 
                _logger.Info($"Saving settings version: {settings.Version}");

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                _logger.Info("Settings saved successfully.");
                OnSettingsSaved?.Invoke(this, settings);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings.", ex);
            }
        }

        public SettingsModel CreateDefaultIfMissing()
        {
            var defaultSettings = new SettingsModel();
            Save(defaultSettings);
            return defaultSettings;
        }
    }
}
