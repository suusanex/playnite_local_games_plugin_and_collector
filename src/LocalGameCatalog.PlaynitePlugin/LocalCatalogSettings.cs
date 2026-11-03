using System;
using System.Collections.Generic;
using System.ComponentModel;
using Playnite.SDK;

namespace LocalGameCatalog.PlaynitePlugin
{
    public sealed class LocalCatalogSettings : INotifyPropertyChanged
    {
        private string catalogPath = "";

        public string CatalogPath
        {
            get => catalogPath;
            set
            {
                var next = value ?? "";
                if (catalogPath == next)
                {
                    return;
                }

                catalogPath = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CatalogPath)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class LocalCatalogSettingsViewModel : ISettings
    {
        private readonly LocalCatalogPlugin plugin;
        private LocalCatalogSettings? editingClone;

        public LocalCatalogSettingsViewModel(LocalCatalogPlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<LocalCatalogSettings>() ?? new LocalCatalogSettings();
        }

        public LocalCatalogSettings Settings { get; private set; }

        public void BeginEdit()
        {
            editingClone = new LocalCatalogSettings { CatalogPath = Settings.CatalogPath };
        }

        public void CancelEdit()
        {
            if (editingClone != null)
            {
                Settings.CatalogPath = editingClone.CatalogPath;
            }
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
