# TASMod 假骑士示范包

这个包包含 Windows x64 的 TAS Mod、配套 Studio、CLI/MCP/SDK，以及从诸神堂椅子出发的假骑士输入记录。示范在第 3042 帧确认调谐假骑士死亡，剩余 2 血；冻结后两次独立进程完整回放均通过目标校验。

1. 先备份游戏存档，正常关闭空洞骑士和 TAS Studio。游戏须已安装兼容的 Hollow Knight Modding API；本包不包含游戏或 Modding API。
2. 将包内 `HollowKnightTAS.zip` 解压到游戏的 `hollow_knight_Data/Managed/Mods/HollowKnightTAS`。确认该目录下直接包含 `HollowKnightTAS.dll` 和 `Companion`，不要多套一层目录，也不要混用旧 DLL。
3. 按 [使用说明](docs/USER-GUIDE.md#使用假骑士示范) 导入 `demo/false-knight-ea42-3042.hktas-save`。导入脚本在 `scripts/Transfer-ReplaySave.ps1`，需要 PowerShell 7。
4. 启动安装目录下的 `Companion/win-x64/HollowKnightTAS.Companion.exe`，点击“启动 TAS 游戏”，再从 Replay Saves 恢复 `save-20260916T0057427801258Z-00000001`。等待完成后，游戏暂停在示范的击杀帧。

恢复会从归档起点重放全部输入，需要等待，不是瞬间读取进程快照。根基线使用第 4 槽；出现覆盖确认时核对槽号和备份信息。导入本身只增加 TAS 存档库条目，实际恢复另行经过槽位保护。其他机器和不同 Mod 组合尚未验收，环境不匹配时请保留错误，不修改文件身份字段。

创作自己的 TAS，参见 [暂停、输入与存档](docs/USER-GUIDE.md#暂停和输入)。外部 AI 可用 [非视觉创作流程](docs/ai/AI-TAS-Workflow-v1.md) 和 [MCP 接入](docs/ai/MCP-v1.md)，与人工共用控制路径。文件校验值见 `SHA256SUMS.txt`；输入文件不能单独替代起点存档。

已验证功能及范围限制见 [交付验收范围](docs/DELIVERY-STATUS.md)。配套工具包含 MCP 操作 ID 取消与恢复状态查询修复；游戏执行程序集和示范输入保持不变。
