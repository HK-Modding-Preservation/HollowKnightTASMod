# 导出诊断日志

在 Studio 的“设置”页滚动到底部，点击“导出诊断日志”，选择文件夹。完成后页面显示 ZIP 路径、收集文件数和提示数。将 ZIP 连同复现步骤、出问题的帧号或截图发给维护者。

导出收集磁盘上仍保留的全部历史诊断会话，不只收集最近一次启动：

- 游戏的 ModLog.txt、Player.log 及存在的前次日志。
- 游戏 Old ModLogs 目录中仍保留的 ModLog 历史归档。
- 普通游戏数据目录和各个 save-shadows 运行目录下的 sessions、diagnostics 中的日志、环境清单及诊断数据。
- 上述诊断文件的 .incomplete 标记，包括对应日志已不存在时仍保留的标记，用于识别截断或丢失事件。
- Studio 的历史游戏日志快照、performance 下的当前及历史耗时报告。
- cold-restore/store 下的 failure.txt、slot-recovery-failure.txt。

ZIP 内的 export-report.json 列出实际导出的文件和读取失败信息。运行中日志按打开时的长度取快照；文件被占用或无法读取时会记录提示。导出在后台执行，同一按钮在导出完成前不能重复启动。文件名包含时间和随机标识，不覆盖旧 ZIP；写入失败不会留下成功命名的 ZIP。

不收集存档、序列文件、启动描述符和凭据，也不遍历日志目录之外的磁盘位置。涉及某个序列的问题，可另外发送该序列。日志本身可能包含本机路径和 Mod 信息。

从本次版本开始，Studio 启动、退出和受控游戏启动前会将现有游戏日志留在 `%LOCALAPPDATA%\HollowKnightTAS\diagnostic-history`；耗时报告除了 last 文件，也会逐次保存在 performance/history。此前已经被覆盖或删除的日志无法恢复；游戏完全独立于 Studio 运行时，不保证每次游戏日志都留有快照。导出不会删除历史日志。

Studio 自己保存的 diagnostic-history 与 performance/history 合计上限为 256 MiB，保留最近 7 天：每次留档或写入耗时报告后先清理超过 7 天的文件，再按修改时间从旧到新清理到容量上限。空快照目录会移除。被占用或不可访问的文件会跳过并在后续清理时重试，期间容量可能暂时超过上限。此策略不删除游戏原始日志、Old ModLogs、Runtime sessions、save-shadows、存档、序列或已导出的 ZIP；Modding API 仍自行管理 Old ModLogs。

## 本次验证

- 多次历史运行一起导出、存档和凭据排除、占用文件提示、重复导出不覆盖、耗时历史保留，以及 Old ModLogs / .incomplete 内容完整和源文件不变：5 项定向测试通过（含原耗时报告测试）。
- 独立 WPF 测试：实际 MainWindow 设置页绑定、900×600 窗口底部滚动可见及截图通过。
- 旧 StudioThemeTests 在菜单数量断言处失败，尚未执行到设置页；不计为本次通过。
- Release publish 成功，本机 Studio 安装后 492 文件签名验证通过，原 Runtime/Core 哈希未改变。
- 未执行人工文件夹选择对话框点击验收；导出服务通过真实临时文件 ZIP 测试，界面通过 WPF 绑定与渲染检查。

2026-09-28 修复验证：7 项导出、历史容量/期限、文件占用及数据保护定向测试通过。
