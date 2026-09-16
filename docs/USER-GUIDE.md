# TAS Mod 使用说明

本文介绍 TAS Mod 的安装、帧级输入、存档回档及外部接口。当前示范已完成两次独立完整回放；首次使用前请备份游戏存档。

## 安装和启动

当前版本面向 Windows x64。使用成套匹配的 Runtime、签名 Companion 和 ClockStartup 文件，不能只替换其中一个 DLL。交付包的功能和验证边界见 [交付验收范围](DELIVERY-STATUS.md)，开发者构建方法见 [打包说明](../packaging/README.md)。

安装目录位于游戏的 `hollow_knight_Data/Managed/Mods/HollowKnightTAS`。关闭游戏后更新文件；不要在运行中覆盖 DLL。Studio 入口是其下的 `Companion/win-x64/HollowKnightTAS.Companion.exe`。

打开 Studio，点击“启动 TAS 游戏”，再在游戏内选择存档。这个入口负责启动配套的时钟支持。直接从 Steam 启动后，不能假定已具备同样的 TAS 运行环境。等待录制起点就绪后再操作；状态为 Preparing 时不要连续重试输入命令。

也可以在新启动的主菜单中，通过 **Control & State** 的“游戏槽（1–4）”选择槽号，点击“读取已有游戏槽”。外部客户端使用相同入口：

```powershell
HollowKnightTAS.Cli.exe automation call loadGameSlot control.playback slot=2 --expected-mode=Running
HollowKnightTAS.Cli.exe automation call getState observe.state.summary statusOnly=true
```

SDK 对应 `LoadGameSlotAsync(slot, leaseId)`，MCP 对应 `hktas_load_game_slot`，参数为 `slot` 和 `expectedRuntimeMode="Running"`。命令被接受只表示原版读档已发起；等待 `recordingOriginStatus=Ready` 后再暂停或输入。此入口只读取已有槽，不创建或覆盖存档；已开始录制起点准备的会话需要重新启动后才能换槽。槽号由使用者选择，本机第 2 槽的诸神堂起点不代表其他用户也使用第 2 槽。

需要从已有槽重新开始创作时，先保存要保留的 TAS 点并确认 `Ready`，再在 **Control & State** 选择槽号，点击“从此槽重新开始录制”。Studio 会正常退出当前游戏，在新进程读取该槽；成功后自动暂停。界面显示本次重启阶段，等待 `Ready` 后再输入。“取消重新开始”可取消尚未就绪的任务；旧游戏已经退出时不会重新恢复旧进程。

这个操作读取槽位中已经保存的游戏进度，不会把当前局内位置保存到普通游戏槽，也不代替 TAS 回档。外部客户端调用 `restartRecordingSession`，权限为 `control.playback`，参数为 `slot` 和当前模式前置条件；查询 `automation status` 的 `recordingRestart.*` 字段时核对返回的 `operationId`。取消命令为 `cancelRecordingRestart`，携带该 ID 和控制租约。

源码工作区还提供 `scripts/Start-TasGame.ps1`，供人工命令行或 AI 从无游戏进程的状态启动。使用 PowerShell 7，传入安装后的 `Companion/win-x64` 目录和 `hollow_knight.exe` 完整路径；可用 `-Slot 1` 到 `-Slot 4` 加载已有槽，不传则停留在普通启动流程。例如：

```powershell
pwsh -File scripts/Start-TasGame.ps1 -CompanionDirectory "<安装后的 Companion/win-x64>" -GamePath "<游戏目录>/hollow_knight.exe" -Slot 2
```

启动脚本复用 Studio 的验证启动器。输出 `Launched` 只证明启动成功；带槽位时等待 `RecordingOriginReady`。已有游戏会被拒绝，需先保存并正常退出；超时后游戏保留运行，请查询同一会话，不重复启动。带槽位流程需要提供 `isStableTitleMenu` 状态字段的新版 Runtime。当前安装版已单次完成脚本启动、第 2 槽自动读档及起点 Ready；已有游戏拒绝启动的分支也已实测，不代表完整回退或战斗验收通过。

## 使用假骑士示范

当前示范文件为 `false-knight-ea42-3042.hktas` 和 `false-knight-ea42-3042.hktas-save`。前者是输入脚本；后者包含诸神堂起点、输入记录和击杀目标的校验对象。只有输入文件不足以恢复示范起点。这份示范绑定 Runtime SHA `ea42a9c4caf5ddb3861697a30a71a29e8e90b48adf303d03cbae603a55708541`，执行环境 SHA `391ad9b41f15cae7c58166ba1a871a83de6bb71142783a76f08c062c33aaf30d`。冻结后两次独立新进程完整回放均在第 3042 帧确认调谐假骑士死亡、剩余 2 血，并通过严格目标校验。旧 `release` 和 `delivery-2982` 示范属于其他环境，不与当前包混用。

先安装与示范匹配的 Mod 包。正常关闭空洞骑士和 TAS Studio，然后在 PowerShell 7 中运行随示范提供的 `Transfer-ReplaySave.ps1`。将下面路径替换为实际位置：

```powershell
pwsh -File "<工具目录>/Transfer-ReplaySave.ps1" -Mode Import `
  -ArchivePath "<示范目录>/false-knight-ea42-3042.hktas-save" `
  -CoreAssemblyPath "<游戏目录>/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/HollowKnightTAS.Core.dll" `
  -StoreRoot "$env:USERPROFILE/AppData/LocalLow/Team Cherry/Hollow Knight/HollowKnightTAS/replay-saves/v1" `
  -ExpectedArchiveSha256 a33d2951b19e206288c7967deb34e5693f36c762c1799c52dd0b3b6ec7fd9a14
```

返回 `Imported` 或 `AlreadyPresent` 后，从 Studio 正常启动 TAS 游戏。在 **Replay Saves** 刷新并选择 `save-20260916T0057427801258Z-00000001`，使用恢复入口。恢复会重启游戏，从归档根基线重放全部输入，最后暂停在击杀帧；它不是直接把 Boss 改成死亡。等待恢复完成再操作，不重复提交。若环境不匹配或槽位替换缺少授权，先处理界面报告的原因，不修改 movie 的 manifest 或基线字段强行通过。

导入仅增加 TAS 存档库条目，不写 `user*.dat`，不覆盖同 ID 的不同内容。示范根基线使用第 4 槽；实际恢复仍经过原有槽位备份、授权和回滚检查。在其他机器或不同存档槽内容上的完整恢复尚未验证。当前工具仅接受手动保留的存档，避免自动存档的保留策略影响目标库。

只检查文件时将 `-Mode Import` 改成 `-Mode Verify`，可省略 `-StoreRoot`。导出自己的手动存档时用 `-Mode Export`，指定 `-StoreRoot`、`-ReplaySaveId` 和一个尚不存在的 `-ArchivePath`。归档包含游戏保存进度和 Mod 设置，分享前确认内容适合公开。

## 暂停和输入

在 **Control & State** 点击 **Pause**，确认状态为 Paused。**Step 1 Movie Tick** 使用当前输入推进一帧；若要指定输入，切到 **Frame Authoring**：

1. 在 **Hold** 填动作名，例如 `right`、`jump`、`right,attack`；`-` 表示松开全部动作。
2. 点击 **Step 1 With Input** 执行一帧，或填写 **Ticks** 后点击 **Run Input Batch** 执行多帧。
3. 等待状态重新变为 Paused，并确认目标帧号，再提交下一次操作。

可用动作名为 `left,right,up,down,jump,attack,dash,cast,quickcast,superdash,dreamnail`。相邻批次按连续按键处理；要重新按下一次攻击或跳跃，应先安排松开帧，而不是连续提交相同按键。

**Resume** 会恢复连续运行，不是单帧操作。请求被接受不等于整段执行完毕；仍显示 Stepping 时不要重复提交。

执行中需要提前结束时，点击 **Stop**。它会在完整帧边界中断当前输入批次并释放输入绑定；等待 Paused/Idle 后再操作。实际执行帧数可能少于请求值，以返回状态为准。外部调用对应 `automation call stopReplay control.playback --expected-mode=Stepping`。`cancelInputBatch` 用于取消尚未提交的分块输入事务，不是停止已执行批次的入口。

切场景中停止后，若新角色尚未加载，输入批次会报告 Hero 不可用。此时可用 **Step 1 Movie Tick** 让原生加载继续到下一个输入帧，再提交按键；加载期间的墙钟等待不算额外输入帧。不要通过改坐标或强行设置 FSM 跳过加载。

## 手动和定时存档

在 **Replay Saves** 点击 **Create Manual Save**。等待状态为 Ready 后，再将其视为可用存档。**Refresh** 更新列表，**Restore Selected** 请求恢复选中的存档。

定时存档先点击“读取当前策略”，再设置“自动存档”、间隔帧数和保留数量，最后点击“应用自动存档策略”。主菜单也可以设置。间隔按实际推进的 Movie 帧计算，暂停时不累计；默认间隔为 18000 帧，保留 20 个自动存档。策略在正常退出时保存。自动存档的保留策略不淘汰手动存档。

当前恢复方式是在新游戏进程中从保存的起点重放，不是瞬间读取内存快照。恢复时等待同一操作的进度，不要反复点击 Restore。出现覆盖确认时，核对目标槽和备份信息后决定；不要无条件点击 **Approve Overwrite**。

当前开发安装版会在退出游戏前停下，显示专用槽号与目标 baseline。批准计划有效期为两分钟；**Approve Overwrite** 会先核对文件是否变化，再备份原字节并安装 baseline，成功后需再次点击 **Restore Selected**。拒绝或取消不写槽位、不重启。文件发生变化或计划过期时，重新请求恢复并核对新提示，不沿用旧审批。AI 使用同一 `approveReplaySaveOverwrite` 命令及 `approved=true/false` 参数，取消使用 `cancelReplaySaveRestore`。源侧提示返回 `ColdRestoreSlotApprovalRequired`，结合 `replaySaveRestoreRequiresOverwriteApproval` 和 detail 核对所需审批。第 4 槽经授权备份、安装和短样本冷恢复已实机验证；这不代表完整战斗回退验收通过。

如果完整记录还包含其他槽的原生读档，可能另行提示生命周期槽授权。这种批准只记录当前恢复计划的授权，不立即写槽；再次请求同一恢复后，目标进程才在对应读档前备份并临时安装数据。授权绑定原槽文件、输入、根基线、执行计划和目标帧，有效期两分钟；文件或计划变化需重新确认。不要把基线覆盖批准与生命周期槽批准视为同一操作。未经批准不会覆盖其他槽。

跨启动恢复时，从 Studio 启动 TAS 游戏并停留在主菜单，不必先加载游戏槽。在 **Replay Saves** 刷新列表、选择同一执行构建的存档，再点击 **Restore Selected**。当前已单次验证主菜单恢复到保存点后继续步进；不同执行指纹的旧存档会被拒绝，不会自动转换。

## 录制和修改过去输入

分支选择器位于 **Frame Authoring**：点击 **刷新已存分支**，按时间、记录类型和短 ID 选择分支；点击 **更多分支** 读取下一页，每页最多 50 项。选择只填写 **Branch movie ID**，完整记录同时选中继续编辑模式，不会改变游戏。列表的“未校验”表示只读取了目录信息，应用时才检查内容、执行环境和恢复条件。也可以继续手动填写保存的完整 ID。安装版已验证从新会话选择旧分支并继续编辑。

在 **Frame Authoring** 使用 **Start Recording**、输入工具和 **Stop Recording**。停止录制后，**Movie Editor** 显示完整时间轴，可用 **Save** 另存 Movie 文件。Movie 文件只保存输入脚本，不替代带起点数据的 TAS 存档。

修改历史前，先保存希望保留的分支或 TAS 存档。在 **Edit past input** 填写 **Start tick**、**Delete ticks**、**Replacement hold** 和 **Replacement ticks**，然后点击 **Replace Range**。插入和删除分别使用 **Insert Range**、**Delete Range**；帧坐标从 0 开始。

编辑会产生独立分支。核对 **Branch movie ID**，填写 **Seek target tick**，再点击 **Apply Branch + Seek**，以修改后的输入重建目标帧。修改脚本文字本身不会倒转当前游戏状态。完整局内记录分支包含根基线和执行计划，可以从根重新播放到目标，不要求事先存在兼容目标检查点；普通 movie 路径仍需可用的兼容恢复来源。编辑后重建的目标不是已验证检查点，成功执行后才能判断结果。

记录中包含返回菜单或重新读槽时，勾选 **编辑完整局内记录（保留读槽事件）** 再编辑。该路径保留输入之间的原生操作；保存分支不等于已经回放成功。单次编辑后恢复、继续单步及再次存档已完成实机检查。

要继续修改同一条时间轴，同时勾选 **继续编辑 Branch ID 指定的分支**，将 **Branch movie ID** 填为上一次返回的分支 ID，再进行替换、插入或删除。这里的帧坐标属于选中的分支，不是当前游戏记录。每次成功后 ID 更新为新分支；原分支仍保留，记下旧 ID 即可重新选择。取消此复选框会重新读取游戏的完整记录作为编辑源。

完整局内记录模式下，**Undo / Redo** 切换本次窗口会话的分支选择，最多保留 100 步撤销；第一次编辑也能撤销回原始记录。它们不会推进或回退游戏。选择完成后，仍需点击 **Apply Branch + Seek** 才会恢复游戏。撤销后创建新编辑会清除重做路径；重新打开窗口或手动切换到历史栈之外的分支时，请通过保存的分支 ID 重新选择。安装版已实际完成连续编辑、两次撤销/重做及恢复到第 0 帧。

## 导出 MP4

新版已完成 Studio 300 帧导出和完整假骑士视频导出；旧 3042 交付包不含此功能。新版配套文件、安装和验证范围见 [MP4 使用说明](MP4-DELIVERY.md)。下文的旧 ea42 示范仍对应旧包，不与新版混用。

1. 先恢复到所选序列的合法起点并暂停，打开 Movie Editor 中的序列。仅上传文本不会恢复游戏状态。
2. 点击“导出 MP4”。工具优先使用 PATH 中的 FFmpeg，找不到时选择本机 `ffmpeg.exe`，再选择一个尚不存在的 `.mp4` 文件。
3. 工具上传当前编辑文本并重放，显示阶段、帧数和输出路径。序列结束后自动封装；看到 `Completed` 才表示文件已发布。
4. 需要等待时使用 Control & State 的 Pause/Resume；提前终止请点击“取消 MP4 导出”，不要使用 Stop 或修改输入。

导出包含游戏声音，但采集期间扬声器静音，结束后恢复。暂停等待不会计入视频时长。分辨率保持不变；导出期间调整分辨率会报错。已有文件不会被覆盖，失败原因显示在状态栏。

AI 可通过 SDK 的 `StartVideoExportAsync`，或 MCP 的 `hktas_start_video_export` 设置 `replayLoadedMovie=true`。`getState` 返回 `videoExport.*` 状态；取消必须携带对应 `operationId`，并持有 `control.playback` 租约。

## 外部 AI 和错误处理

人工界面与 AI 共用控制服务；AI 需要控制租约，不能抢占另一个控制者。只读状态无需控制租约。**Structured State** 读取语义快照，**Combat State** 读取最新详细状态；调用者应检查字段新鲜度和采样失败信息。

CLI 和 MCP 工具位于 `Companion/win-x64/Tools`，SDK 位于其 `SDK` 子目录。接入说明见 [MCP v1](ai/MCP-v1.md)。调试状态修改不能混入正式 TAS 输入。

遇到 PreconditionFailed 时，重新读取状态和帧号，确认上一操作已结束；不要盲目重发旧请求。恢复出现 Faulted 或 hash mismatch 时保留错误和存档，不通过修改角色状态继续冒充成功。

恢复被取消或失败后，Studio 会显示槽恢复状态：`WaitingForExit` 正在等待目标游戏退出，`Complete` 表示该操作的临时槽恢复已处理完，`Pending` 表示仍有错误需要处理。这里的 Complete 只说明槽恢复，不表示 TAS 回放成功。工具异常退出时，下一次通过 Studio 启动 TAS 游戏会先检查遗留恢复记录；发现仍有游戏在运行或槽文件与记录不符时会拒绝启动，保留当前文件和备份。不要删除恢复记录或覆盖冲突文件来消除提示。

普通重读槽已经失败、且超过 120 秒仍等不到原生结束时，`nativeReloadCanExit=true` 表示可以使用原有退出按钮结束该游戏进程，再重新启动录制。正常加载期间不会开放这个例外；已完成的 TAS 存档保留，失败的录制根不能继续作为可靠起点。

退出前先保存需要保留的 TAS 点并确认 Ready。在 **Control & State** 点击“退出暂停游戏”，可以直接从 Paused/Idle 正常退出，无需 Resume。输入批次、存档或恢复仍在进行时，请先等待结束。SDK 对应 `QuitGameAsync()`，CLI 命令是 `automation call quitGame control.playback --expected-mode=Paused --expected-tick=<当前帧>`，MCP 为 `hktas_quit_game`。

这个命令已经过一次实机退出验证，但不代表直接点击游戏窗口关闭或 Alt+F4 的暂停阻塞问题已经修复。
