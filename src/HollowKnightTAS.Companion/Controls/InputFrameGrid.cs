using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.Controls
{
    // DataGrid's item automation tree can expand the entire virtual movie.
    // Expose only realized rows; never enumerate Items or offer virtual-item lookup.
    public sealed class InputFrameGrid : DataGrid
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameGridPeer(this);

        private sealed class FrameGridPeer : FrameworkElementAutomationPeer
        {
            public FrameGridPeer(InputFrameGrid owner) : base(owner) { }
            protected override string GetClassNameCore() => nameof(InputFrameGrid);
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetNameCore() => "输入帧表格（仅可见行；使用键盘或滚动浏览）";
            protected override List<AutomationPeer> GetChildrenCore()
            {
                var result = new List<AutomationPeer>();
                var pending = new Stack<DependencyObject>();
                pending.Push(Owner);
                var visited = 0;
                while (pending.Count > 0 && visited++ < 4096 && result.Count < 128)
                {
                    var item = pending.Pop();
                    if (item is DataGridRow row)
                    {
                        if (row.IsVisible) result.Add(new FrameRowPeer(row));
                        continue;
                    }
                    for (var i = VisualTreeHelper.GetChildrenCount(item) - 1; i >= 0; i--)
                        pending.Push(VisualTreeHelper.GetChild(item, i));
                }
                return result;
            }
        }

        private sealed class FrameRowPeer : FrameworkElementAutomationPeer
        {
            public FrameRowPeer(DataGridRow owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override string GetNameCore() => ((DataGridRow)Owner).Item is InputGridRow row
                ? $"{row.Current} Frame {row.Tick}, FPS {row.FramesPerSecond}, {row.Input.HeldActions}, Submit {row.Submit}, Cancel {row.Cancel}"
                : string.Empty;
            protected override List<AutomationPeer> GetChildrenCore() => new();
        }
    }
}
