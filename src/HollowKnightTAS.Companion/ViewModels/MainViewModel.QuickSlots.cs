using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Automation;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        private StudioQuickSlots? quickSlots;
        private bool quickSlotBusy;
        private int selectedQuickSlot;
        private string quickSlotStatus = "窗口内 Shift+F1…F10 保存，F1…F10 恢复；恢复需要输入重放。";
        private string quickSlotCatalog = "[]";
        private bool ShowsTimelineQuickSlots => fullRunMovies?.IsPending == true
            || worldlines != null;
        public ObservableCollection<string> QuickSlotLabels { get; } = new ObservableCollection<string>();
        public int SelectedQuickSlot { get => selectedQuickSlot; set { Set(ref selectedQuickSlot, value); OnPropertyChanged(nameof(BindTimelineQuickSlotCommand)); } }
        public string QuickSlotStatus { get => quickSlotStatus; private set => Set(ref quickSlotStatus, value); }
        public ICommand SaveQuickSlotCommand { get; private set; } = null!;
        public ICommand LoadQuickSlotCommand { get; private set; } = null!;
        public ICommand RefreshQuickSlotsCommand { get; private set; } = null!;
        public ICommand ForgetPendingQuickSlotCommand { get; private set; } = null!;

        private void InitializeQuickSlots()
        {
            try
            {
                quickSlots = new StudioQuickSlots();
            }
            catch (Exception e) { QuickSlotStatus = "快捷槽不可用：" + e.Message; }
            SaveQuickSlotCommand = Command(() => RunQuickSlotAsync(true), allowStartupContinue: true);
            LoadQuickSlotCommand = Command(() => RunQuickSlotAsync(false), allowStartupContinue: true, allowCompletedReplay: true);
            RefreshQuickSlotsCommand = Command(RefreshQuickSlotCatalogAsync);
            ForgetPendingQuickSlotCommand = new RelayCommand(() =>
            {
                if (quickSlotBusy || quickSlots == null || SelectedQuickSlot < 0) return;
                var slot = quickSlots.Slots[SelectedQuickSlot];
                if (slot.PendingLabel.Length == 0) return;
                if (MessageBox.Show("停止跟踪这次保存？这不会取消 Runtime 操作或删除存档。旧槽引用保留；之后完成的保存仍可在完整存档管理中找到。",
                    "解除待确认关联", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                try
                {
                    quickSlots.Set(SelectedQuickSlot, slot.SaveId, slot.Tick, "");
                    QuickSlotStatus = "已解除待确认关联；没有取消操作或删除存档。";
                    RenderQuickSlots();
                }
                catch (Exception e) { QuickSlotStatus = e.Message; }
            });
            RenderQuickSlots();
        }

        private void RenderQuickSlots()
        {
            var selection = SelectedQuickSlot;
            using var catalog = JsonDocument.Parse(quickSlotCatalog);
            for (var i = 0; i < 10; i++)
            {
                if (ShowsTimelineQuickSlots && worldlines != null)
                {
                    var timelineLabel = $"F{i + 1} · 空时间线槽";
                    if (worldlines.Library.QuickSlots.TryGetValue(i, out var reference))
                    {
                        var parts = reference.Split(':');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out var nodeId))
                        {
                            var tree = worldlines.Library.Trees.FirstOrDefault(t => t.Id == parts[0]);
                            var node = tree?.Nodes.FirstOrDefault(n => n.Id == nodeId);
                            if (node != null) timelineLabel = $"F{i + 1} · {tree!.Name} · {node.Label}"
                                + (parts.Length >= 3 ? " · 世界线 " + parts[2] : "");
                        }
                    }
                    if (QuickSlotLabels.Count <= i) QuickSlotLabels.Add(timelineLabel); else QuickSlotLabels[i] = timelineLabel;
                    continue;
                }
                var slot = quickSlots?.Slots[i];
                var availability = slot == null || slot.SaveId.Length == 0 ? "空槽" : "未在当前目录确认";
                if (slot != null)
                    foreach (var entry in catalog.RootElement.EnumerateArray())
                        if (entry.GetProperty("id").GetString() == slot.SaveId)
                            availability = entry.GetProperty("status").GetString() ?? "未知";
                var label = $"F{i + 1} · " + (slot?.SaveId.Length > 0 ? $"帧 {slot.Tick} · " : "") + availability;
                if (slot?.PendingLabel.Length > 0) label += " · 保存待确认（Refresh）";
                if (QuickSlotLabels.Count <= i) QuickSlotLabels.Add(label); else QuickSlotLabels[i] = label;
            }
            SelectedQuickSlot = selection;
        }

        private void UpdateQuickSlotCatalog(string json)
        {
            try
            {
                quickSlots?.Reconcile(json);
                quickSlotCatalog = json;
                RenderQuickSlots();
            }
            catch (Exception e) { QuickSlotStatus = "快捷槽目录更新失败：" + e.Message; }
        }

        private async Task RefreshQuickSlotCatalogAsync()
        {
            var session = SelectedSession?.Client;
            var result = await ExecuteHumanResultAsync(AutomationCommandIds.GetReplaySaves,
                AutomationScope.ObserveReplaySaves, expectedModeRequired: false);
            RequireAutomationSuccess(result);
            if (session != SelectedSession?.Client) throw new InvalidOperationException("会话已切换，请刷新当前快捷槽目录。");
            UpdateQuickSlotCatalog(RequireResultField(result, "entriesJson"));
        }

        private async Task RunQuickSlotAsync(bool save)
        {
            if (ShowsTimelineQuickSlots)
            {
                await RunTimelineQuickSlotAsync(save);
                return;
            }
            if (quickSlotBusy) { QuickSlotStatus = "快捷槽操作处理中，请勿重复提交。"; return; }
            if (quickSlots == null) throw new InvalidOperationException(QuickSlotStatus);
            var index = SelectedQuickSlot;
            if (index < 0 || index >= 10) return;
            var session = SelectedSession?.Client;
            quickSlotBusy = true;
            try
            {
                await RefreshQuickSlotCatalogAsync();
                var slot = quickSlots.Slots[index];
                if (save)
                {
                    if (slot.PendingLabel.Length > 0)
                    {
                        QuickSlotStatus = "该槽之前的保存尚未确认，已查询同一保存；没有重新提交。可在完整存档管理中检查。";
                        return;
                    }
                    var label = $"Studio-slot-{index + 1}-" + Guid.NewGuid().ToString("N");
                    RequireCurrentControlMode();
                    // Persist correlation before dispatch, so a lost reply never creates another save on retry.
                    quickSlots.Set(index, slot.SaveId, slot.Tick, label);
                    RenderQuickSlots();
                    QuickSlotStatus = $"正在保存 F{index + 1}；旧槽引用保留到新存档 Ready。";
                    var result = await ExecuteHumanResultAsync(AutomationCommandIds.CreateReplaySave,
                        AutomationScope.ControlReplaySave, Fields("label", label));
                    if (!result.Success)
                    {
                        // A failed envelope can also mean an observation timeout: retain the correlation.
                        throw new InvalidOperationException(result.ResultCode + ": " + result.Detail);
                    }
                    var deadline = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < deadline && session == SelectedSession?.Client)
                    {
                        await RefreshQuickSlotCatalogAsync();
                        if (quickSlots.Slots[index].PendingLabel.Length == 0)
                        {
                            QuickSlotStatus = $"F{index + 1} 已保存 · 帧 {quickSlots.Slots[index].Tick}。";
                            return;
                        }
                        await Task.Delay(500);
                    }
                    QuickSlotStatus = "保存观察结束，结果仍待确认；按 Refresh 查询，不重复创建。";
                }
                else
                {
                    if (slot.SaveId.Length == 0) { QuickSlotStatus = $"F{index + 1} 是空槽。"; return; }
                    // Broker remains authoritative for compatibility, overwrite permission and cold restore.
                    QuickSlotStatus = $"正在请求恢复 F{index + 1} · 帧 {slot.Tick}；进度与取消见存档管理。";
                    await ExecuteHumanAsync(AutomationCommandIds.RestoreReplaySave,
                        AutomationScope.ControlReplaySave, Fields("replaySaveId", slot.SaveId));
                }
            }
            catch (Exception e) { QuickSlotStatus = e.Message; throw; }
            finally { quickSlotBusy = false; RenderQuickSlots(); }
        }
    }
}
