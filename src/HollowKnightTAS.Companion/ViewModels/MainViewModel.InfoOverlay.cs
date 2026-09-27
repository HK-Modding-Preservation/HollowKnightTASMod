using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private InfoOverlaySettings infoSettings = InfoOverlaySettings.Defaults();
        private InfoOverlayController? infoController;
        private readonly DispatcherTimer infoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly HashSet<InfoOverlayItem> watchedInfoItems = new();
        private string infoStatus = "", infoSearch = "";
        private bool infoAdjusting;
        private bool infoSettingsInvalid;
        public InfoOverlaySettings InfoSettings => infoSettings;
        public string InfoStatus { get => infoStatus; private set => Set(ref infoStatus, value); }
        public string InfoSearch { get => infoSearch; set { Set(ref infoSearch, value); OnPropertyChanged(nameof(InfoFields)); } }
        public IEnumerable<InfoField> InfoFields => InfoOverlayModel.Fields.Where(f =>
            (f.Name + UiText.T(f.Name) + f.Id).Contains(infoSearch, StringComparison.OrdinalIgnoreCase));
        public bool InfoAdjusting
        {
            get => infoAdjusting;
            set { Set(ref infoAdjusting, value); if (value) InfoSettings.Enabled = true; infoController?.SetAdjusting(value); }
        }
        public Key ConfiguredInfoKey => InfoSettings.Hotkey switch { "F11" => Key.F11, "F12" => Key.F12, _ => Key.None };
        internal static string? InfoOverlaySettingPathOverride { get; set; }
        private static string InfoPath => InfoOverlaySettingPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-info-overlay.json");
        private void InitializeInfoOverlay()
        {
            try { infoSettings = InfoOverlaySettings.Load(InfoPath); }
            catch (Exception error) { infoSettingsInvalid = true; infoSettings = InfoOverlaySettings.Defaults(); infoSettings.Enabled = false; InfoStatus = "信息设置读取失败：" + error.Message; }
            infoController = new InfoOverlayController(() => SelectedSession?.Client, RequestRuntimeAsync,
                () => IsRestorePresentationFrozen, message => { if (!infoSettingsInvalid) InfoStatus = message; },
                (x, y) => { InfoSettings.MarginX = Math.Round(x); InfoSettings.MarginY = Math.Round(y); });
            infoSaveTimer.Tick += SaveInfoSettings;
            AttachInfoSettings(); infoController.Configure(infoSettings);
        }
        private void AttachInfoSettings()
        {
            infoSettings.PropertyChanged += OnInfoChanged;
            infoSettings.Items.CollectionChanged += OnInfoCollectionChanged;
            SynchronizeInfoItems();
        }
        private void SynchronizeInfoItems()
        {
            foreach (var item in watchedInfoItems) item.PropertyChanged -= OnInfoChanged;
            watchedInfoItems.Clear();
            foreach (var item in infoSettings.Items) { item.PropertyChanged += OnInfoChanged; watchedInfoItems.Add(item); }
        }
        private void OnInfoCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        { SynchronizeInfoItems(); OnInfoChanged(sender, new PropertyChangedEventArgs("Items")); }
        private void OnInfoChanged(object? sender, PropertyChangedEventArgs e)
        {
            infoSaveTimer.Stop(); infoSaveTimer.Start();
            if (e.PropertyName == nameof(InfoOverlaySettings.Hotkey)) OnPropertyChanged(nameof(ConfiguredInfoKey));
            if (!infoSettings.Enabled) { InfoAdjusting = false; infoController?.Configure(Disabled(InfoOverlaySettings.Defaults())); }
        }
        private static InfoOverlaySettings Disabled(InfoOverlaySettings settings) { settings.Enabled = false; return settings; }
        private void SaveInfoSettings(object? sender, EventArgs e)
        {
            infoSaveTimer.Stop();
            try { infoSettings.Save(InfoPath); infoSettingsInvalid = false; infoController?.Configure(infoSettings); InfoStatus = "信息设置已保存。"; }
            catch (Exception error) { infoSettingsInvalid = true; InfoStatus = "信息设置未保存：" + error.Message; }
        }
        public void AddInfoItem(InfoField? field)
        { if (field != null && infoSettings.Items.Count < 32) infoSettings.Items.Add(InfoOverlayModel.NewItem(field.Id)); }
        public void RemoveInfoItem(InfoOverlayItem? item) { if (item != null) infoSettings.Items.Remove(item); }
        public void MoveInfoItem(InfoOverlayItem? item, int target)
        { if (item != null && target >= 0 && target < infoSettings.Items.Count) infoSettings.Items.Move(infoSettings.Items.IndexOf(item), target); }
        public void ResetInfoSettings()
        {
            infoSettings.PropertyChanged -= OnInfoChanged;
            infoSettings.Items.CollectionChanged -= OnInfoCollectionChanged;
            infoSettings = InfoOverlaySettings.Defaults(); AttachInfoSettings();
            OnPropertyChanged(nameof(InfoSettings)); OnPropertyChanged(nameof(ConfiguredInfoKey));
            OnInfoChanged(this, new PropertyChangedEventArgs("Reset"));
        }
        internal void DisposeInfoOverlay()
        {
            if (infoSaveTimer.IsEnabled) SaveInfoSettings(this, EventArgs.Empty);
            infoSaveTimer.Stop(); infoSaveTimer.Tick -= SaveInfoSettings;
            infoSettings.PropertyChanged -= OnInfoChanged; infoSettings.Items.CollectionChanged -= OnInfoCollectionChanged;
            foreach (var item in watchedInfoItems) item.PropertyChanged -= OnInfoChanged;
            watchedInfoItems.Clear(); infoController?.Dispose(); infoController = null;
        }
    }
}
