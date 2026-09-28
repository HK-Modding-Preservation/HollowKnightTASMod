using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.ViewModels;

public sealed partial class MainViewModel
{
    private string diagnosticExportStatus = "";
    public string DiagnosticExportStatus { get => diagnosticExportStatus; private set => Set(ref diagnosticExportStatus, value); }
    public ICommand ExportDiagnosticLogsCommand { get; private set; } = null!;

    private async Task ExportDiagnosticLogsAsync()
    {
        var dialog = new OpenFolderDialog { Title = UiText.T("选择诊断日志导出目录") };
        if (dialog.ShowDialog() != true) return;
        DiagnosticExportStatus = "正在打包诊断日志…";
        try
        {
            var result = await Task.Run(() => DiagnosticLogExporter.Export(dialog.FolderName));
            DiagnosticExportStatus = $"已导出 {result.Files} 个文件，{result.Warnings} 项提示：{result.Path}";
        }
        catch (Exception ex) { DiagnosticExportStatus = "诊断日志导出失败：" + ex.Message; }
    }
}
