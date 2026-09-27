# AI 控制指南

[English](AI-CONTROL.en.md)

AI、脚本和 Studio 共用受保护的游戏控制路径。先按[玩家指南](USER-MANUAL.md)安装完整配套包，并通过 Studio 启动 TAS 游戏。普通游戏不提供控制会话。

## 接入与权限

工具位于安装目录的 `Companion/win-x64/Tools`：

- `HollowKnightTAS.Cli.exe`：命令行查询和控制。
- `HollowKnightTAS.AgentBridge.exe`：stdio MCP 服务，由 MCP 客户端作为子进程启动。
- `SDK`：.NET 自动化客户端及依赖，使用 `AutomationClient` 连接。

工具默认读取 `%LOCALAPPDATA%/HollowKnightTAS/automation/automation-v1.json` 中的会话连接信息（automation bootstrap）。CLI 和 MCP 可传 `--bootstrap=<完整路径>` 指定会话入口。连接信息和认证凭据仅供本机使用，不应随序列分发。

MCP 客户端配置示例，路径按实际安装位置填写：

```json
{
  "mcpServers": {
    "hollow-knight-tas": {
      "command": "D:/Games/Hollow Knight/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/Tools/HollowKnightTAS.AgentBridge.exe",
      "args": []
    }
  }
}
```

MCP 使用按行分帧的 UTF-8 JSON-RPC，先完成 `initialize` 和初始化通知，再调用 `tools/list`。stdout 为协议输出，诊断写入 stderr。

会话采用 `ApprovedControl` 模式。只读查询不需要租约；写命令需要绑定客户端、连接、会话及 scope 的短期独占租约。MCP 使用 `hktas_acquire_control`（`scopes` 数组，`ttlSeconds` 为 1–300）和 `hktas_release_control`。CLI 对单次写调用自动申请和释放租约；SDK 调用者自行管理租约。不要把一个短连接取得的租约交给另一个连接复用。

## 先识别当前会话

```powershell
$cli = 'D:/Games/Hollow Knight/hollow_knight_Data/Managed/Mods/HollowKnightTAS/Companion/win-x64/Tools/HollowKnightTAS.Cli.exe'
& $cli automation status
& $cli automation call getCapabilities observe.status
```

以 `getCapabilities` 返回的命令、scope 和 availability 为准。当前 Studio 使用全流程 v2，会话从原生启动第 0 帧开始。Studio 时间线、检查点和快捷槽绑定仅在本次 Studio 会话内保留，跨会话保留输入需保存序列文件。全流程控制命令通过 CLI 或 SDK 的 `CreateCommand` / `ExecuteAsync` 调用；MCP 提供世界观察及视频工具，但没有独立的 `fullRun*` 控制工具。

CLI 返回结果封套，`success` 是字符串 `"true"` 或 `"false"`。`dataBase64` 解码为 UTF-8 JSON 字符串映射，部分字段还包含嵌套 JSON。以下函数检查请求是否成功并解码：

```powershell
function Invoke-Tas([string[]]$CommandArgs) {
    $raw = & $cli @CommandArgs
    $exitCode = $LASTEXITCODE
    $r = $raw | ConvertFrom-Json
    if ($exitCode -ne 0 -or $r.success -ne 'true') {
        throw "$($r.resultCode): $($r.detail)"
    }
    [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($r.dataBase64)) | ConvertFrom-Json
}
$s = Invoke-Tas @('automation', 'call', 'fullRunStatus', 'observe.status')
$s
```

`nativeFrame` 是原生循环计数，Movie 帧是输入序列位置。加载阶段可以只推进前者。每次写操作前读取当前模式和原生帧，使用返回的数值作为前置条件，不用表格行号替代 `expectedNativeFrame`。

## 全流程录制与播放

| 命令 | scope | 参数与条件 |
| --- | --- | --- |
| `fullRunStatus` | `observe.status` | 查询原生帧、模式、连接、故障和存档保护状态 |
| `beginFullRunRecording` | `control.recording` | 启动第 0 帧暂停；`expectedNativeFrame=0`、`mouseEnabled=true/false`、可选 `fps` |
| `beginFullRunReplay` | `control.playback` | 启动第 0 帧暂停；`expectedNativeFrame=0`、`movieBase64`、可选 `pauseAtFrame` |
| `fullRunStep` | `control.step` | 暂停；精确 `expectedNativeFrame`，推进一个原生帧 |
| `fullRunPlay` | `control.playback` | 暂停；精确 `expectedNativeFrame`，持续执行 |
| `fullRunPause` | `control.playback` | 运行中；携带已观察到的 `expectedNativeFrame` |
| `fullRunMovie` | `movie.read` | Runtime 连接后读取 Movie 文件位置 |
| `fullRunSnapshot` | `movie.read` | 暂停边界保存完整 Movie 快照并返回 `path` |
| `fullRunUpdateMovie` | `control.playback` | 暂停；`moviePath` 必须在当前受保护影子目录内，携带精确 `expectedNativeFrame` |
| `fullRunSeek` | `control.playback` | 暂停；`targetFrame` 是 Movie 目标，另传精确 `expectedNativeFrame`；设置向前播放的暂停目标 |
| `fullRunStop` | `control.playback` | 暂停；精确 `expectedNativeFrame`，停止并返回文档 |
| `quitGame` | `control.playback` | 在暂停边界正常退出 |

在尚未开始录制或回放的启动第 0 帧，可以执行以下示例。它会开始录制并推进一个原生帧：

```powershell
Invoke-Tas @('automation','call','beginFullRunRecording','control.recording',
    'expectedNativeFrame=0','mouseEnabled=false','fps=50','--expected-mode=Paused')
$s = Invoke-Tas @('automation','call','fullRunStatus','observe.status')
Invoke-Tas @('automation','call','fullRunStep','control.step',
    "expectedNativeFrame=$($s.nativeFrame)",'--expected-mode=Paused')
```

播放用相同方式调用 `fullRunPlay`；暂停传 `--expected-mode=Running`。不要与人工 Studio 操作同时写入。状态改变导致前置条件失败时，重新观察并判断，不循环重试旧帧号。

`beginFullRunReplay` 接收 canonical v2 Movie 的 UTF-8 字节 Base64，不接收 `.hktaspack` 容器本身。序列包应通过 Studio 加载以应用绑定的初始存档。完整 Movie 应通过 Core 的 `MovieV2Codec` 解析并规范化，保持真实环境头及输入通道顺序；较长文档可用 SDK 避免命令行长度限制，但仍受协议的 900000 字符字段上限及 1 MiB 消息上限约束；SDK 不会自动把全流程回放请求分块。超出请求大小时，通过 Studio 打开文件。

全流程输入通道包含 `hero`、`preMenu`、`binder`、`mouseInControl` 和 `mouseHollowKnight`。菜单和角色通道分别记录，不能只修改角色方向就假定菜单也会移动。连续帧保持相同按键表示持续按住，需要新按下边沿时先安排松开帧。输入结构及动作顺序以 `src/HollowKnightTAS.Core/Movie/MovieProtocolV2.cs` 和 `src/HollowKnightTAS.Core/Movie/MovieV2Codec.cs` 为准。

`fullRunSnapshot` 返回的 `path` 指向当前影子目录中的 Movie 快照，可读取并在同目录另存候选。修改未来输入后提交 `fullRunUpdateMovie`，保持已执行前缀一致。`fullRunSeek` 设置目标后，还需调用 `fullRunPlay` 开始向前运行。回到过去需要从绑定起点重启重放，Studio 的重算和时间线负责该流程；`fullRunSeek` 不能代替向过去回档。共享文件必须是当前会话的影子目录内文件，不修改真实用户存档。

## 非视觉世界观察

Runtime 连接后，全流程 v2 提供以下只读查询，暂停时不会推进游戏、时钟或随机数：

```powershell
Invoke-Tas @('automation','call','getWorldSnapshot','observe.state.deep',
    'view=world','includeInactive=false','offset=0','limit=64')
```

MCP 对应 `hktas_get_world_snapshot`，SDK 为 `GetWorldSnapshotAsync`；`GetWorldSnapshotJsonAsync` 自动翻页。`view` 可取 `world`、`all` 或 `colliders`，`limit` 为 1–128。`snapshotJson` 包含 `metadata`、`objects`、`total`、`nextOffset`；后续页必须携带同一个 `snapshotId`，`nextOffset=-1` 表示结束。

对象包括角色、敌人、变换、资源、护符、FSM 状态与变量、碰撞形状等。`all` 配合 `includeInactive=true` 查询未激活对象。对象标识与会话、场景及实例绑定，场景切换后重新查询。

需要完整组件和 FSM 详情时调用：

```powershell
Invoke-Tas @('automation','call','getObjectDetails','observe.state.deep',
    'objectId=<概览返回的对象ID>','expectedNativeFrame=<当前暂停原生帧>',
    'cursor=0','maxCharacters=100000')
```

MCP 对应 `hktas_get_object_details`；SDK 的 `GetObjectDetailsJsonAsync` 自动拼接并校验 SHA-256。手动分页时固定 `objectId/detailsId`，按 `cursor/nextCursor` 顺序拼接 `detailsJson`，核对 `totalCharacters` 和完整 UTF-8 `sha256` 后再解析。末段 `nextCursor=-1` 且 `complete=true`。缓存过期后丢弃旧页并重新采集。

读取 `errors`、`omitted`、`omittedCount` 和范围元数据。缺失字段不能当作零或 false，几何范围不能当作精确碰撞结果。屏幕投影采用游戏视口归一化坐标，不是桌面坐标。概览有预算限制，复杂 Mod 的全部内部规则不一定可观察。

## 视频、完成状态与失败处理

`startVideoExport` / `hktas_start_video_export` 使用 `control.playback`，需要暂停的固定 50 fps 回放，默认使用内置编码器。`ffmpegPath` 可选，用于指定其他编码器路径（SDK 传 `null` 使用内置版本）。必填不存在的 `outputPath`、正整数 `maximumFrames` 和 `expectedRuntimeMode=Paused`。`maximumFrames` 是采集上限，必须严格大于所选区间的 Movie 帧数，并为加载画面留出余量；可传 `endMovieFrame` 指定 Movie 终点。全流程导出从当前回放位置录制剩余区间并自动结束；完整导出或区间准备可直接使用 Studio。`cancelVideoExport` 携带该次 `operationId`。全流程不使用手动 finish 收尾。

请求成功只表示接受。读取 `fullRunStatus` / `getStatus` 的返回数据，检查实际帧、故障、回放停止原因以及视频状态；以匹配操作的 `Completed` 为完成，`Failed` 和 `Cancelled` 均不代表成功。嵌套 Runtime 状态按返回字段解析。

超时后先查询原会话、原操作，不重复提交输入、恢复或导出。重启导致会话失效时，重新读取 bootstrap 并建立连接，再核对会话和操作身份。回放环境不匹配时恢复匹配环境，不改写序列的环境指纹来冒充兼容。

## 局内会话接口

能力目录若返回局内控制命令，可以使用 `hktas_get_state`、`hktas_step_with_input`、`hktas_queue_input_batch`、输入事务、分支编辑和 replay save 工具。它们采用 `expectedRuntimeMode`、`expectedMovieTick`，输入提交还校验 `expectedSceneEpoch`。这些不是全流程 `expectedNativeFrame` 的替代接口。

局内保存的请求接受不等于完成，需核对保存请求 ID、最终状态及 `getReplaySaves` 条目。恢复以返回的 `operationId` 查询 `coldRestore` 状态，完成后重新连接；取消必须携带同一个操作 ID。收到存档覆盖要求时，需要针对具体文件与目标的授权。初始存档包、Studio 时间线和局内 replay save 是不同的数据对象，不混用其 ID 或文件格式。

接口定义可查 `src/HollowKnightTAS.Core/Automation/AutomationCommandIds.cs`、`src/HollowKnightTAS.Companion/Automation/AutomationCapabilityCatalog.cs`、`src/HollowKnightTAS.AgentBridge/McpCatalog.cs` 和 `src/HollowKnightTAS.Automation.Client/AutomationClient.cs`。

[玩家安装与使用](USER-MANUAL.md) · [项目构建指南](BUILD.md)
