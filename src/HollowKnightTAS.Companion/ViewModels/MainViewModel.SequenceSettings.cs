using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private string sequenceDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HollowKnightTAS", "Sequences");
        private string sequenceAutoSaveSeconds = "60";
        private string sequenceSaveStatus = "";
        private string activeSequenceDirectory = "";
        private int activeAutoSaveSeconds = 60;
        private string? sequenceSavePath;
        private string autoSaveName = Guid.NewGuid().ToString("N");
        private string lastAutoSaveText = "";
        private DateTime nextSequenceAutoSave = DateTime.UtcNow.AddSeconds(60);
        private bool sequenceSaving;
        private static string SequenceSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowKnightTAS", "studio-sequence-settings.json");
        public string SequenceDirectory { get => sequenceDirectory; set => Set(ref sequenceDirectory, value); }
        public string SequenceAutoSaveSeconds { get => sequenceAutoSaveSeconds; set => Set(ref sequenceAutoSaveSeconds, value); }
        public string SequenceSaveStatus { get => sequenceSaveStatus; private set => Set(ref sequenceSaveStatus, value); }
        public ICommand SaveSequenceSettingsCommand { get; private set; } = null!;
        public ICommand BrowseSequenceDirectoryCommand { get; private set; } = null!;

        private void InitializeSequenceSettings()
        {
            activeSequenceDirectory = SequenceDirectory;
            try
            {
                if (File.Exists(SequenceSettingsPath))
                {
                    var settings = JsonSerializer.Deserialize<SequenceSettings>(File.ReadAllText(SequenceSettingsPath)) ?? throw new InvalidDataException("序列设置无效。");
                    ValidateSequenceSettings(settings.Directory, settings.Seconds);
                    SequenceDirectory = activeSequenceDirectory = settings.Directory;
                    SequenceAutoSaveSeconds = settings.Seconds.ToString();
                    activeAutoSaveSeconds = settings.Seconds;
                }
            }
            catch (Exception ex) { SequenceSaveStatus = "序列设置读取失败：" + ex.Message; }
            SaveSequenceSettingsCommand = new RelayCommand(() =>
            {
                try
                {
                    if (!int.TryParse(SequenceAutoSaveSeconds, out var seconds)) throw new InvalidDataException("自动保存间隔必须为整数秒。");
                    ValidateSequenceSettings(SequenceDirectory, seconds);
                    var directory = Path.GetFullPath(SequenceDirectory);
                    Directory.CreateDirectory(directory);
                    Directory.CreateDirectory(Path.GetDirectoryName(SequenceSettingsPath)!);
                    File.WriteAllText(SequenceSettingsPath + ".new", JsonSerializer.Serialize(new SequenceSettings { Directory = directory, Seconds = seconds }));
                    File.Move(SequenceSettingsPath + ".new", SequenceSettingsPath, true);
                    activeSequenceDirectory = SequenceDirectory = directory;
                    activeAutoSaveSeconds = seconds;
                    nextSequenceAutoSave = DateTime.UtcNow.AddSeconds(seconds);
                    lastAutoSaveText = "";
                    SequenceSaveStatus = "序列保存设置已应用。";
                }
                catch (Exception ex) { SequenceSaveStatus = "序列保存设置失败：" + ex.Message; }
            });
            BrowseSequenceDirectoryCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFolderDialog();
                if (dialog.ShowDialog() == true) SequenceDirectory = dialog.FolderName;
            });
        }

        internal static void ValidateSequenceSettings(string directory, int seconds)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new InvalidDataException("请选择绝对保存路径。");
            if (seconds < 0 || seconds > 86400) throw new InvalidDataException("自动保存间隔为 0–86400 秒，0 表示关闭。");
            _ = Path.GetFullPath(directory);
        }

        private void ResetSequenceSaveTarget(string? path)
        {
            sequenceSavePath = path;
            autoSaveName = Guid.NewGuid().ToString("N");
            lastAutoSaveText = "";
            nextSequenceAutoSave = DateTime.UtcNow.AddSeconds(activeAutoSaveSeconds);
        }

        private async Task SaveSequenceAsync(bool saveAs)
        {
            if (sequenceSaving || gridApplying) return;
            sequenceSaving = true;
            try
            {
                var path = sequenceSavePath;
                if (saveAs || path == null)
                {
                    Directory.CreateDirectory(activeSequenceDirectory);
                    var dialog = new SaveFileDialog { Filter = "HK-TAS Movie (*.hktas)|*.hktas", AddExtension = true, DefaultExt = ".hktas", InitialDirectory = activeSequenceDirectory, FileName = path == null ? "sequence.hktas" : Path.GetFileName(path) };
                    if (dialog.ShowDialog() != true) return;
                    path = dialog.FileName;
                }
                await WriteSequenceFileAsync(path, MovieText);
                sequenceSavePath = path;
                SequenceSaveStatus = Status = "序列已保存：" + path;
            }
            catch (Exception ex) { SequenceSaveStatus = Status = "序列保存失败：" + ex.Message; }
            finally { sequenceSaving = false; }
        }

        public async Task AutoSaveSequenceAsync()
        {
            if (sequenceSaving || gridApplying || activeAutoSaveSeconds == 0 || DateTime.UtcNow < nextSequenceAutoSave) return;
            nextSequenceAutoSave = DateTime.UtcNow.AddSeconds(activeAutoSaveSeconds);
            var text = MovieText;
            if (string.IsNullOrWhiteSpace(text) || text == lastAutoSaveText) return;
            sequenceSaving = true;
            try
            {
                var directory = Path.Combine(activeSequenceDirectory, "Autosave");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "sequence-" + autoSaveName + ".hktas");
                await WriteSequenceFileAsync(path, text);
                lastAutoSaveText = text;
                SequenceSaveStatus = "序列自动保存：" + path;
            }
            catch (Exception ex) { SequenceSaveStatus = "序列自动保存失败：" + ex.Message; }
            finally { sequenceSaving = false; }
        }

        internal static async Task WriteSequenceFileAsync(string path, string text)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false, true));
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private sealed class SequenceSettings
        {
            public string Directory { get; set; } = "";
            public int Seconds { get; set; }
        }
    }
}
