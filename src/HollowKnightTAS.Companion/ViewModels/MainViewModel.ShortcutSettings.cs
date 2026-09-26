using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private bool globalHotkeysEnabled;
        private string globalHotkeyStatus = "";
        private static string GlobalHotkeysPath => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-global-hotkeys.txt");
        public string GlobalHotkeyStatus { get => globalHotkeyStatus; private set => Set(ref globalHotkeyStatus, value); }
        public void ReportGlobalHotkeys(string message) => GlobalHotkeyStatus = message;
        public bool GlobalHotkeysEnabled
        {
            get => globalHotkeysEnabled;
            set
            {
                if (value == globalHotkeysEnabled) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(GlobalHotkeysPath)!);
                    File.WriteAllText(GlobalHotkeysPath + ".new", value ? "enabled" : "disabled");
                    File.Move(GlobalHotkeysPath + ".new", GlobalHotkeysPath, true);
                    Set(ref globalHotkeysEnabled, value);
                }
                catch (Exception ex)
                {
                    GlobalHotkeyStatus = "全局热键设置保存失败：" + ex.Message;
                    OnPropertyChanged();
                }
            }
        }

        private Key playShortcut = Key.P;
        private Key configuredPlay = Key.P;
        public Key PlayShortcut { get => playShortcut; set => Set(ref playShortcut, value); }
        public Key ConfiguredPlay => configuredPlay;
        private Key pauseShortcut = Key.Pause;
        private Key advanceShortcut = Key.V;
        private Key configuredPause = Key.Pause;
        private Key configuredAdvance = Key.V;
        private bool shortcutSettingsValid = true;
        private string shortcutSettingsStatus = "仅 Studio 获得焦点时生效；文本框内字母与空格留给文本输入。";
        private static string ShortcutSettingsPath => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-shortcuts.json");
        public Key[] ShortcutKeys { get; } = new[] { Key.Pause, Key.Space, Key.OemPeriod }
            .Concat(Enumerable.Range((int)Key.A, (int)Key.Z - (int)Key.A + 1).Select(v => (Key)v)).ToArray();
        public Key PauseShortcut { get => pauseShortcut; set => Set(ref pauseShortcut, value); }
        public Key AdvanceShortcut { get => advanceShortcut; set => Set(ref advanceShortcut, value); }
        public Key ConfiguredPause => configuredPause;
        public Key ConfiguredAdvance => configuredAdvance;
        public string ShortcutSettingsStatus { get => shortcutSettingsStatus; private set => Set(ref shortcutSettingsStatus, value); }
        public ICommand SaveShortcutSettingsCommand { get; private set; } = null!;
        public ICommand ResetShortcutSettingsCommand { get; private set; } = null!;

        private void InitializeShortcutSettings()
        {
            try
            {
                if (File.Exists(GlobalHotkeysPath))
                {
                    if (new FileInfo(GlobalHotkeysPath).Length > 32) throw new InvalidDataException("配置过大");
                    var value = File.ReadAllText(GlobalHotkeysPath).Trim();
                    if (value != "enabled" && value != "disabled") throw new InvalidDataException("快捷键配置无效");
                    globalHotkeysEnabled = value == "enabled";
                }
            }
            catch (Exception ex) { GlobalHotkeyStatus = "全局热键设置读取失败：" + ex.Message; }

            try
            {
                if (File.Exists(ShortcutSettingsPath))
                {
                    if (new FileInfo(ShortcutSettingsPath).Length > 4096) throw new InvalidDataException("配置过大");
                    var keys = JsonSerializer.Deserialize<Key[]>(File.ReadAllText(ShortcutSettingsPath));
                    if (keys == null || (keys.Length != 2 && keys.Length != 3) || keys.Distinct().Count() != keys.Length || keys.Any(k => !ShortcutKeys.Contains(k)))
                        throw new InvalidDataException("快捷键配置无效");
                    configuredPause = PauseShortcut = keys[0];
                    configuredAdvance = AdvanceShortcut = keys[1];
                    configuredPlay = PlayShortcut = keys.Length == 3 ? keys[2] : (keys.Contains(Key.P) ? ShortcutKeys.First(k => !keys.Contains(k)) : Key.P);
                }
            }
            catch (Exception e)
            {
                shortcutSettingsValid = false;
                ShortcutSettingsStatus = "使用默认键位，未覆盖损坏配置：" + e.Message;
            }
            SaveShortcutSettingsCommand = new RelayCommand(() =>
            {
                try
                {
                    if (!shortcutSettingsValid) throw new InvalidOperationException("原配置无法读取，拒绝覆盖：" + ShortcutSettingsPath);
                    if (new[] { PauseShortcut, AdvanceShortcut, PlayShortcut }.Distinct().Count() != 3 || !ShortcutKeys.Contains(PlayShortcut) || !ShortcutKeys.Contains(PauseShortcut) || !ShortcutKeys.Contains(AdvanceShortcut))
                        throw new InvalidOperationException("播放/暂停、继续播放与逐帧必须选择不同按键。");
                    Directory.CreateDirectory(Path.GetDirectoryName(ShortcutSettingsPath)!);
                    var temp = ShortcutSettingsPath + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonSerializer.Serialize(new[] { PauseShortcut, AdvanceShortcut, PlayShortcut }));
                        File.Move(temp, ShortcutSettingsPath, true);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                    configuredPlay = PlayShortcut;
                    OnPropertyChanged(nameof(ConfiguredPlay));
                    configuredPause = PauseShortcut;
                    configuredAdvance = AdvanceShortcut;
                    OnPropertyChanged(nameof(ConfiguredPause));
                    OnPropertyChanged(nameof(ConfiguredAdvance));
                    ShortcutSettingsStatus = "已应用并保存。仅 Studio 窗口内生效；Ctrl+O/S 与 F1…F10 固定。";
                }
                catch (Exception e) { ShortcutSettingsStatus = e.Message; }
            });
            ResetShortcutSettingsCommand = new RelayCommand(() =>
            {
                PlayShortcut = Key.P;
                PauseShortcut = Key.Pause;
                AdvanceShortcut = Key.V;
                SaveShortcutSettingsCommand.Execute(null);
            });
        }
    }
}
