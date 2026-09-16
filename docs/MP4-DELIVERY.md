# 序列导出 MP4

本包包含支持视频导出的 TAS Mod、Studio、外部控制工具，以及一段已生成的调谐假骑士视频。直接播放 `video/false-knight.mp4` 即可查看成片，无需启动游戏。

## 安装与导出自己的序列

1. 备份游戏存档，正常关闭游戏和 Studio。将包内 `HollowKnightTAS.zip` 解压到游戏的 `hollow_knight_Data/Managed/Mods/HollowKnightTAS`，保持内部目录结构。需要已安装 Modding API 的 Windows x64 版游戏；本包不含游戏、Modding API 或 FFmpeg。
2. 打开安装目录内 `Companion/win-x64/HollowKnightTAS.Companion.exe`，点击“启动 TAS 游戏”。不要把普通 Steam 启动视为已配置 TAS 时钟。
3. 恢复到输入序列对应的合法起点并暂停。在 Movie Editor 中 Open 或粘贴 `.hktas`。输入文件本身不包含完整游戏起点，上传它不会自动回档。
4. 点击“导出 MP4”。工具优先查找 PATH 中的 FFmpeg，否则提示选择本机 `ffmpeg.exe`；再选择一个尚不存在的输出文件。
5. 等待状态变为 `Completed`。工具自动重放全部输入并封装 H.264 / AAC 视频，不需要手动计算录制时长。

录制期间扬声器暂时静音，游戏声音仍写入视频，结束后恢复设备输出。使用 Control & State 的 Pause / Resume 可以暂停和继续；暂停等待不增加成片时长。需要终止时点击“取消 MP4 导出”。导出中不要修改序列、回档或改变游戏分辨率。已有文件不会被覆盖，失败或取消不会发布为成功视频。

## 包内示范与存档

`demo/false-knight.hktas` 是 3043 个输入帧的记录；`demo/false-knight.hktas-save` 包含起点和输入记录，目标是击败假骑士后的第 3043 帧。视频还保留了场景切换画面，共 3282 帧，50 fps，65.64 秒，800×450，含 48 kHz 双声道声音。

这份示范须使用本包配套的 Mod，不与旧 ea42 / 3042 示范混用。输入文件的执行环境标识是 `ca2351361eca60de168e5380e2ca934c89113352172ba163cc0258c48823a6e5`，不要手工改写标识来绕过兼容检查。

如需在游戏里恢复示范，先关闭游戏和 Studio，在解压目录用 PowerShell 7 执行下列命令，将 `<游戏目录>` 替换为实际路径：

```powershell
pwsh -File scripts/Transfer-ReplaySave.ps1 -Mode Import `
  -ArchivePath demo/false-knight.hktas-save `
  -CoreAssemblyPath "<游戏目录>/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/HollowKnightTAS.Core.dll" `
  -StoreRoot "$env:USERPROFILE/AppData/LocalLow/Team Cherry/Hollow Knight/HollowKnightTAS/replay-saves/v1" `
  -ExpectedArchiveSha256 c0c78be49e0e2792c5e7e60e3e5c86493c3e947ffe562d25bff91a2bd6972217
```

返回 `Imported` 或 `AlreadyPresent` 后，启动 Studio 和 TAS 游戏，在 Replay Saves 刷新，恢复 `save-20260916T0526048550472Z-00000001`。这会从归档起点重放到击杀帧，并非直接修改 Boss 状态。导入只增加 TAS 库条目；实际恢复使用第 4 槽，仍需经过槽位备份和覆盖授权。确认备份后再批准覆盖；不要反复提交恢复请求。此流程在其他机器上尚未验证。

## 外部控制

安装目录的 `Companion/win-x64/Tools` 包含 CLI、MCP 与 SDK。人工和外部客户端使用同一个导出服务，需要 `control.playback` 控制租约。

- 开始：`startVideoExport`，参数 `ffmpegPath`、`outputPath`、`maximumFrames`、`replayLoadedMovie=true`；先准备起点、上传序列并暂停。
- 查询：`getState` 的 `videoExport.*` 字段。核对 `operationId`、`state`、`frames`、`outputPath` 和 `detail`。汇总 `getStatus` 的完整 Runtime 数据在 `runtimeStatusJson`。
- 取消：`cancelVideoExport`，携带同一 `operationId`。高级手动采集可用 `finishVideoExport` 结束；完整序列模式自动结束。
- SDK 入口为 `StartVideoExportAsync`；MCP 入口为 `hktas_start_video_export`。CLI 通用调用的开始结果中，操作 ID 在解码后的 `data.detail`。

开始回执超时不代表操作未启动：先查询同一会话的操作状态，不重复提交。

## 已验证范围

已完成完整假骑士导出并收到两个原生死亡事件；Studio 按钮完成 300 帧短导出；另一次 180 帧采集中暂停后帧数保持不变，继续后音视频均为 3.6 秒。编码格式、样本累计、取消清理和已有文件保护已有定向检查。

完整视频已无错误地完整解码。开头、中段、末段的动作画面与音频瞬态已对照，抽样未发现明显错位或累计漂移。此检查基于逐帧画面和音频波形，不是逐音效的精确延迟保证或主观听感评价；具体数据见包内 VERIFICATION.md。编码器异常退出时的失败与清理检查也已通过。SHA256SUMS.txt 用于核对包内文件，验证范围不等于所有机器、场景和音频设备均已测试。
