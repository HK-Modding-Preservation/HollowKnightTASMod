# AI TAS Workflow v1

AI 与普通调试脚本使用相同的 Automation v1 权限。下面说明存档确认和输入编辑流程。

## 可执行的单帧和多帧输入

交付包的 `scripts/Invoke-AuthoringBatch.ps1` 可单独使用，不依赖假骑士控制器。先通过 Studio 启动游戏，等待起点 Ready 并暂停。以下命令在 PowerShell 7 中运行，工作目录为解压后的交付包：

```powershell
$cli = '<安装目录>/Companion/win-x64/Tools/HollowKnightTAS.Cli.exe'
function Read-TasState {
    $r = & $cli automation call getState observe.state.summary statusOnly=true | ConvertFrom-Json
    if ($r.success -ne 'true') { throw "$($r.resultCode): $($r.detail)" }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
$s = Read-TasState
./scripts/Invoke-AuthoringBatch.ps1 -CliPath $cli -ExpectedTick $s.movieTick -Count 1 -Hold right
$s = Read-TasState
./scripts/Invoke-AuthoringBatch.ps1 -CliPath $cli -ExpectedTick $s.movieTick -Count 12 -Hold '-'
```

第一条输入向右一帧，第二条松开全部按键并推进 12 帧。脚本从当前记录读取身份，不改写旧示范身份；提交时检查暂停模式、帧号和场景编号，结束时检查准确帧数及观察字段的新鲜度。输出为 JSON，只依赖内部状态，不需要窗口焦点或截图。CLI 使用当前用户已批准的控制权限；权限不足时会拒绝。

`-Hold 'right,attack'` 可提交组合输入，`-ObserveKeys 'hero.position.x','hero.position.y','player.health'` 选择输出字段。连续相同按键表示持续按住；需要再次按下攻击或跳跃时先安排松开帧。此示例支持内联 movie；超出内联大小时在提交前拒绝，完整导出使用下文的分块接口。

执行过程中如果观察超时，不重复提交输入。先读取同一会话状态确认实际帧号；超时不代表执行已经停止。保存使用 `createReplaySave`，恢复使用 `restoreReplaySave`；两者都需等待实际完成，具体确认方式见下文。

## 存档、恢复和历史编辑

`getStatus` 返回 `slotRecovery.operationId/status/detail` 时，先核对操作 ID。`WaitingForExit` 表示尚未获得槽恢复所需的退出条件，`Pending` 表示文件或进程条件阻止恢复，`Complete` 仅表示该操作的槽恢复完成，不是 TAS 成功。游戏退出后仍可查询这组状态；正常 Runtime 写命令仍需连接。启动器会先处理遗留 pending，冲突会阻止新游戏启动并返回原因。源端重读档的 `nativeReloadCanExit=true` 则允许调用既有 `quitGame`，前置模式使用刚查询的实际模式，不必强行暂停挂起的原生加载。

覆盖确认有两种计划，需读取返回 detail 后决定：基线槽批准会核对、备份并安装基线；生命周期槽批准只保存精确授权，目标进程在对应原生读档前才临时安装。二者均通过 `approveReplaySaveOverwrite`，批准后需重新提交恢复。生命周期授权绑定根、输入、计划、目标帧和各槽原文件哈希，两分钟后过期；目标或文件改变时不能复用。客户端不得自动无条件批准，也不得将一次批准当作永久写槽权限。

生命周期区间编辑返回 `undoBranchMovieId`。第一次编辑时它指向额外保留的原始记录分支，后续编辑时指向父分支。外部客户端可以保存这些 ID，通过 `applyBranchAndSeek` 回到所选分支的目标帧；仅选择 ID 不会改变游戏状态。Studio 的 Undo/Redo 同样只选择分支，和普通 movie 文本的历史分开。人工连续编辑、撤销/重做和恢复提交已实机验证。

包含原生读槽事件时，调用 `getMovie`（`movie.read`）并传 `includeLifecycle=true`，使用返回的 `movieId` 作为首次编辑的 `baseMovieId`。`insertInputRange`、`replaceInputRange`、`deleteInputRange`（`movie.edit`）也传 `includeLifecycle=true`。保存分支后，通过 `applyBranchAndSeek`（`movie.apply-branch`）提交 `branchMovieId` 和 `targetMovieTick`，恢复完成后才能继续操作。

后续区间编辑的 `baseMovieId` 可直接使用上一次返回的 `branchMovieId`，无须先回放或重新读取当前游戏记录。坐标始终相对于该分支。返回的新分支包含全部编辑操作和恢复依赖，旧 ID 不变；保存成功不代表新目标已验证。SDK 使用 `EditLifecycleInputRangeAsync`，MCP 使用相应区间编辑工具并传同一标志。

`getMovie(includeLifecycle=true)` 返回 `exportId`（完整 HKLE-v1 字节的 SHA-256）、`exportBytes`、`exportChunks` 和 `exportChunkBytes`；`movieInline=false` 时不能把空 `movieBase64` 当作空输入。用同一只读命令传 `includeLifecycle=true, exportId=<原值>, exportChunk=<从 0 开始的块号>`，或使用 SDK 的 `GetLifecycleMovieChunkAsync`。安装版已验证单块重组及会话变化后的 ExportExpired；多块覆盖来自编解码测试。

新分支持久保存于 `%LOCALAPPDATA%/HollowKnightTAS/automation/movie-library`；旧会话分支按精确 ID 从原目录读取，继续编辑时先校验并保存到持久库，不移动原文件。客户端保留 `branchMovieId`，在新会话重新取得控制租约和当前帧/场景 CAS 后，可直接提交同一分支 ID。执行环境不兼容仍会拒绝，不自动改写 movie 身份。安装版已验证旧会话分支的选择、读取和继续编辑。

分支目录查询：`getMovie`（`movie.read`）传 `listBranches=true, branchOffset=0`，返回 `branchesJson` 和 `nextOffset`。每页最多 50 项，`nextOffset` 为空表示末页；继续查询时使用该偏移，不混用 includeLifecycle/exportId 参数。列表只含 ID、记录类型、时间、大小和 `NotChecked` 标记，不能当作回放成功证据；编辑期间目录可能变化，客户端按 ID 去重或重新刷新。SDK 为 `ListMovieBranchesAsync(offset)`，MCP 为 `hktas_get_movie` 的同名可选参数，CLI 示例：`automation call getMovie movie.read listBranches=true branchOffset=0`。安装版实测目录为 29 项，分页边界由定向测试覆盖。

按块号拼接各响应的 `chunkBase64` 解码字节，核对每块的 ID、块号、尺寸及最终总长度，再用 Core 的 `MovieLifecycleExport.Decode(bytes, exportId)` 校验并解析输入、根基线和读槽对象。每块最多 384 KiB，完整导出最多 128 MiB。缓存只保留一份；收到 `ExportExpired` 时放弃本次所有块，重新请求完整导出，不混用旧块。下载期间避免其他客户端刷新 movie，分块请求本身不会重新采集游戏状态。

存档命令的 `Ok` 只表示请求已接受，不等于落盘成功。轮询 `automation status` 中的
`runtime.replaySavePendingCount`、`runtime.replaySaveLastRequestId`、`runtime.replaySaveLastStatus`、
`runtime.replaySaveLastId`、`runtime.replaySaveLastEffectiveMovieTick`、`runtime.replaySaveLastError`；
最近请求必须对应本次请求，成功后再通过 `getReplaySaves` 确认条目。多个客户端有并发请求时，
最近结果可能被覆盖，不可将其他请求的成功当成本次成功。暂停时不得为让存档完成而偷偷推进游戏帧。

1. 读取 `status`、`manifest`、`capabilities`、当前 `movie`。
2. 读取新的 `state/summary` 和目标附近的 `timeline`；只使用 semantic JSON、watch、FSM、RNG/ledger、milestone 与 desync，不依赖截图识别。
3. 生成完整 canonical movie 候选及 `baseMovieId/reason/expectedMilestone`。
4. 调用 `validateMoviePatch`；非法候选在 Runtime 执行前停止。
5. 调用 `proposeMoviePatch` 创建独立 content-addressed branch；此时 current movie 不变。
6. 当前版本固定为 `ApprovedControl`；客户端仍需显式申请所需 scope 的短 lease。
7. 显式 `applyMovieBranch`，再从干净的 T09 起点 start replay。
8. 读取 milestone、endpoint semantic hash、ledger、RNG 与 desync；与基线做结构化比较。
9. 若未达到目标，基于首个差异提出新分支；不得修改 expected hash、manifest、baseline 或 verdict。

调试 mutation 可用于探索角色位姿或 health/soul，但一旦成功，整个进程标记为 `NonVerifiableDebugMutation`。该进程只能用于调试，不能产生正式 T07/T16 PASS。正式候选必须冷启动，从原始专用槽仅靠输入 movie 重放。

## 确定性 scripted agent

离线验收 agent 不使用语言模型，固定执行：

```text
read movie/state/timeline
  -> propose valid branch
  -> propose malformed branch and expect pre-execution rejection
  -> verify current movie hash unchanged
  -> acquire scoped lease
  -> apply valid branch
  -> replay and collect result
  -> release lease
```

修改输入分支后，按改动范围检查 proposal hash、canonical bytes、result code 与审计关联。实机目标是从诸神堂椅子开始，只通过非视觉 API 制作输入 movie，击败调谐假骑士并稳定重放；当前证据与剩余任务见 `CURRENT.md`。
## 启动起点诊断（开发版）

`HollowKnightTAS.Cli.exe automation startup` 通过 Companion 的 `getStartupProfile` 命令查询 Runtime，权限为 `observe.status`，只读且无需控制租约。CLI 输出直接可读的 JSON 字段，不需要自行解码 Base64。现有 SDK 也可通过通用命令接口调用 `getStartupProfile`。

重点检查 `status`、`rootStatus` 及 RNG、realtime、phase 故障码。查询成功只代表收到了诊断，不代表起点通过；必须检查返回状态。该命令不创建根、不修改时钟或游戏状态，失败时也不会重新启动游戏。

## 自动开启新的录制会话（开发脚本）

需要放弃当前运行位置、从已有游戏槽重新开始创作时，使用 PowerShell 7：

```powershell
./scripts/Start-TasGame.ps1 -CompanionDirectory '<安装目录>/Companion/win-x64' -GamePath '<游戏目录>/hollow_knight.exe' -Slot 4 -Restart
```

`-Restart` 会校验唯一游戏进程的路径、PID、启动时间和 Runtime 身份，通过同一 SDK 连接申请控制租约、暂停并正常退出，再启动一个新进程读取指定槽位。它不强杀进程，退出或观察超时后也不重复提交。已有输入记录由正常退出流程保存；需要保留当前恢复点时，先创建 replay save 并确认 `Ready`。

该命令读取槽位中已经保存的游戏进度，不会将当前局内状态写成普通游戏存档，也不能代替 `restoreReplaySave`。返回 `RecordingOriginReady` 后游戏仍在运行，创作前可调用 `pause`。

共享服务入口已完成源码接线，安装验证状态见 `CURRENT.md`：使用 `restartRecordingSession`，权限为 `control.playback`，参数为 `slot` 和当前模式前置条件。接受结果中的 `operationId` 对应 `automation status` 的 `recordingRestart.operationId/phase/slot/detail`；只有 `Ready` 表示新录制起点已验证并暂停。`Failed`、`Cancelled` 是终态，不应重提同一次操作。

取消使用 `cancelRecordingRestart`，携带相同 `operationId` 和控制租约，不要求源 Runtime 的帧前置条件。SDK 提供同名异步方法，MCP 提供 `hktas_restart_recording_session` 和 `hktas_cancel_recording_restart`，Studio 提供“从此槽重新开始录制”和“取消重新开始”按钮。
