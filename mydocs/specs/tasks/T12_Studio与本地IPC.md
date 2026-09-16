# Task Spec T12: Companion/Studio、本地 IPC 与 Mod 自动启动

- **Status**: REOPENED_BY_T24（原 IPC/自动启动证据保留；冷恢复重启监督器待实现）
- **Gate**: G4 External Process Safety / Editing Experience
- **Depends On**: T04, T07, T09, T11
- **Produces**: 随包 Windows Companion/Studio、Mod 自动启动器、版本化本地 IPC、单实例/生命周期管理、冷恢复重启监督器和证据时间线

## 0. Open Questions

- None。v1 明确为 Windows x64、离线随包部署；跨平台发布和在线自更新另立后续 Spec，不阻塞本任务。

## 1. Requirements

### Goal

交付一个由 Runtime Mod 自动发现、验证和启动的外部 `HollowKnightTAS.Companion.exe`。Companion 内含 Studio，用于编辑/校验 movie、控制 replay/pause/step、管理 T09 任意/定时重放存档、显示 Runtime 提供的 T14 恢复策略/回退状态、订阅 ledger/watch/desync，并作为 T13 NativeHost 的 capability broker。

Runtime 与 Companion 使用本地 IPC；Unity 主线程永不等待进程启动、握手或 IO。Companion 缺失、损坏、版本不兼容或崩溃时，Runtime 必须保留游戏内录制、重放和 T09 存档入口。

### CelesteTAS Reference Boundary

可借鉴：

- 游戏 Mod、独立 Studio 和共享通信协议作为一个产品发布。
- 提供随游戏启动 Studio 的设置。
- 启动前检查 Studio 版本/安装状态，已有实例不重复拉起。
- 通信协议具备版本、心跳、超时和断线恢复。

不照搬：

- 不使用全局固定名共享内存作为 v1 传输。
- 不通过 Explorer 或 shell 启动。
- v1 不在游戏进程内联网下载/更新可执行文件。
- 不按进程名枚举并强制关闭任意 Studio 实例。

### In-Scope

- `.NET 8` Windows Desktop Companion，WPF Studio。
- `win-x64` self-contained publish（Release 禁止 trimming），目标机器不要求预装 .NET Desktop Runtime。
- Companion 随 Mod release 包发布，不作为程序集 `EmbeddedResource`。
- Runtime 固定路径发现、签名 manifest + SHA-256 校验和异步自动启动。
- `AutoStartCompanion=true` 默认设置、游戏内手动启动/重连/禁用入口。
- 兼容实例复用、单实例、启动超时、指数退避和 crash-loop 熔断。
- Windows named pipe、当前用户 ACL、协议握手、session token、length-prefixed messages。
- Editor、format/validate、运行控制、T09 存档目录、事件时间线和首差异视图。
- 当 Runtime advertise T14 时显示 `FullReplay/KeyframeTail/FallbackToReplay` 计划、进度和失败原因；不自行捕获或应用关键帧。
- 后台 IO + 有界主线程 command queue。
- disconnect/fault 时安全停止回放并恢复物理输入。
- 为 T13 提供只读 capability discovery/broker 接口，但本任务不实现 NativeHost。
- 为 T15 提供 Companion 内部的 typed command/state broker seam；本任务不向任意外部客户端公开 Runtime pipe。
- 为 T09/T21 的 `VanillaEquivalentColdReplay` 提供 Companion 常驻、单次 intent claim、固定
  游戏重启和新旧 Runtime session lineage；不解析或修改 gameplay 状态。

### Out-of-Scope

- 远程网络控制、云同步、协作编辑。
- Companion 在线自更新或 Runtime 下载可执行文件。
- macOS/Linux Companion。
- Companion/Runtime 执行 movie、IPC 客户端或用户文本给出的任意程序、文件路径、shell、C#、反射或网络命令。
- Companion 直接读写游戏内存。
- T14 关键帧 eligibility、capture、adapter apply 和恢复判定。
- 面向 SDK/CLI/AI 的公开 automation endpoint、control lease、MCP server 和 movie proposal workflow；这些属于 T15。
- 进程级 savestate、系统时间 hook、线程/句柄恢复；这些属于 T13。
- 自行发明时钟 profile 或绕过 T13/T24 的 build/hash/evidence 门禁；T12 只编排已验证的固定启动链。

## 1.5 Code Map

```text
docs/protocol/IPC-v1.md
docs/deployment/CompanionBundle-v1.md
schemas/
  companion-manifest-v1.schema.json
  ipc-envelope-v1.schema.json
src/HollowKnightTAS.Core/Ipc/
  IpcEnvelope.cs
  IpcCommand.cs
  IpcEvent.cs
  IpcCodec.cs
  IpcValidator.cs
  ProtocolRange.cs
src/HollowKnightTAS.Runtime/Companion/
  CompanionManifest.cs
  CompanionBundleVerifier.cs
  CompanionLauncher.cs
  CompanionLaunchState.cs
  CompanionCrashLoopGuard.cs
  CompanionSettings.cs
src/HollowKnightTAS.Runtime/Ipc/
  NamedPipeRuntimeServer.cs
  RuntimeCommandQueue.cs
  RuntimeCommandDispatcher.cs
src/HollowKnightTAS.Companion/
  HollowKnightTAS.Companion.csproj
  Program.cs
  SingleInstanceCoordinator.cs
  SessionRegistry.cs
  App.xaml
  MainWindow.xaml
  ViewModels/
  Services/NamedPipeRuntimeClient.cs
  Services/CapabilityBroker.cs
  Services/ColdRestoreIntentStore.cs
  Services/ColdRestoreSupervisor.cs
  Services/VerifiedGameLauncher.cs
  Editor/
  Timeline/
  ReplaySaves/
packaging/
  companion.manifest.json
  Companion/win-x64/HollowKnightTAS.Companion.exe
  Companion/win-x64/*.dll
tests/HollowKnightTAS.Core.Tests/Ipc/
tests/HollowKnightTAS.Runtime.Tests/Companion/
tests/HollowKnightTAS.Companion.Tests/
artifacts/companion/<campaignId>/
  launch-matrix.json
  verification.json
  ipc-soak.json
  crash-loop.json
  t07-parity.json
```

## 2. Architecture

### 2.1 Release Layout

```text
HollowKnightTAS/
  HollowKnightTAS.dll
  companion.manifest.json
  Companion/
    win-x64/
      HollowKnightTAS.Companion.exe
      <pinned runtime files>
```

`companion.manifest.json` 至少包含：

```json
{
  "schemaVersion": 1,
  "product": "HollowKnightTAS.Companion",
  "version": "0.1.0",
  "rid": "win-x64",
  "runtimeProtocolMin": 1,
  "runtimeProtocolMax": 1,
  "entrypoint": "Companion/win-x64/HollowKnightTAS.Companion.exe",
  "files": [
    {
      "path": "Companion/win-x64/HollowKnightTAS.Companion.exe",
      "sha256": "<64 lowercase hex>"
    }
  ],
  "signature": "<base64 signature>"
}
```

约束：

- Runtime 内嵌 release 公钥；manifest 先移除 `signature` 字段并做 canonical UTF-8 序列化，再验证签名，最后验证所有声明文件 SHA-256。
- 所有路径必须 canonicalize 后仍位于 Mod 根目录；拒绝绝对路径、`..`、symlink/reparse point 逃逸和未声明文件作为 entrypoint。
- Debug 构建可通过本机未跟踪设置允许开发证书；Release 不提供“忽略签名”按钮。
- v1 只从安装包读取，不搜索 PATH、注册表、桌面、Steam 目录或用户自定义任意 exe。

### 2.2 Launch State Machine

```text
Disabled
  └─ AutoStart/ManualStart
       └─ Discover
            ├─ missing ───────────────► CompanionUnavailable
            └─ found ─► Verify
                         ├─ invalid ───► CompanionRejected
                         └─ valid ─────► AttachExisting
                                         ├─ attached ─► Handshake ─► Ready
                                         └─ none ─────► Launch ─────► Handshake
                                                          └─ fail ─► Backoff/Degraded
```

启动条件：

- Runtime 已完成 Mod 初始化、T01 session manifest 和日志初始化。
- 当前平台为 Windows x64，且不是测试 headless 模式。
- `AutoStartCompanion=true`，或用户在游戏内执行手动启动。
- 当前 release bundle 已通过全部验证。

启动方式：

- 在后台任务构造绝对 `FileName`，设置 `UseShellExecute=false`，禁止 shell verb。
- `WorkingDirectory` 固定为 Companion 目录。
- 参数仅含固定的 `--bootstrap-pipe=` 和 Runtime 生成的一次性随机 pipe 名；不得含 movie 文本、token 或任意用户路径。
- Unity Mono 未实现托管 named-pipe server 与 anonymous-pipe server 构造器；Runtime 因此通过 Win32 在子进程启动前创建带当前用户专用初始 DACL 的一次性 bootstrap pipe。256-bit session token 只经该 pipe 传递，不写入参数或日志，传递后立即关闭 bootstrap。
- 首次等待握手最长 10 秒；Runtime 不阻塞 Unity 主线程。
- 失败按 `1s, 2s, 5s, 15s, 30s` 退避；5 分钟内连续 5 次失败后熔断，直到用户手动重试或下次游戏启动。

### 2.3 Existing Instance and Ownership

- Companion 使用当前用户范围的 single-instance mutex 和 control pipe。
- Runtime 先连接 control pipe，核对产品版本与协议范围；兼容则注册新 game session，不创建第二实例。
- 不兼容实例不被强杀；Runtime 显示版本冲突并降级。
- Runtime 只追踪自己启动得到的 PID/进程句柄；绝不按进程名枚举并终止未知进程。
- 游戏退出时关闭本 session。Companion 默认保留以防丢失未保存编辑；用户可设置 `ExitCompanionWithGame`，此时也只通过握手请求由 Companion 自行退出。

### 2.4 IPC

- Pipe 名包含随机 session ID；Windows pipe ACL 只允许当前用户。
- 连接还需 256-bit 随机 token 和双方 nonce，握手绑定游戏 PID、Runtime session ID、Companion instance ID 和协议范围。
- 每条 message 为 `uint32 big-endian length + UTF-8 canonical JSON payload`。
- 单条最大 1 MiB；movie 内容分块且总上限 32 MiB。
- Runtime IO thread 只解析 framing、schema 和基础权限；通过验证的 command 进入 bounded queue。
- Unity 主线程在 T03 安全点按每帧时间预算消费队列，默认最多 2 ms。
- Companion 不在线时 Runtime 仍可用本地文件、按键和游戏内 T09 入口运行。

### 2.5 Cold Restore Supervisor

- Companion 默认在游戏 session 正常关闭后继续存活；只有认证 Runtime 提交的
  `ColdRestoreIntent-v1` 才能进入重启状态机。movie、Studio 文本、SDK 和 MCP 都不能提供
  exe、参数、工作目录、DLL 或 shell 命令。
- 状态固定为 `Prepared -> SourceQuiesced -> SourceExited -> Launching -> NewSessionAttached ->
  IntentClaimed -> BaselineReady -> ReplayingPrefix -> PausedAtTarget -> Completed/Failed`。
  每次迁移写入 operation sequence、前一记录 hash、PID/创建时间和 UTC 时间；乱序或重复迁移拒绝。
- 启动目标只能来自当前已认证 session 的 canonical game path，加上签名 release 中的固定 launcher/
  T13 verified startup profile；启动前后重新核对 game、UnityPlayer、Managed、Runtime、Companion、
  Bridge/Payload 和 profile hash。任何不匹配都不得创建或继续新游戏进程。
- `UseShellExecute=false`，不经 Steam `-applaunch`、Explorer、`cmd.exe` 或 PowerShell；不得按进程名
  终止游戏。只跟踪 intent 创建的精确进程句柄/PID/创建时间，超时仅终止该新建进程。
- 新 Runtime 必须通过既有 token/nonce/PID/session handshake 注册，并出示同一 intent ID 的单次
  claim。Companion 在 `Completed` 前保留 intent；失败/崩溃写终态并停止自动重启，后续只能由用户
  明确重试生成新 operation，不得复用旧 claim。
- Companion 不可用时，游戏内 T09 仍可创建/列出存档并排定“下次手动启动恢复”；自动即时回退
  明确报告 `ColdSupervisorUnavailable`，不得静默改走同进程路径。

## 3. Protocol and Interfaces

### 3.1 Command Whitelist

```text
hello
uploadMovieBegin / uploadMovieChunk / uploadMovieEnd
startReplay
stopReplay
pause
step
resume
subscribe
unsubscribe
requestSnapshot
createReplaySave
listReplaySaves
restoreReplaySave
setAutoSavePolicy
requestCapabilityCatalog
ping
```

Runtime 不接受客户端文件路径；Companion 上传 canonical movie bytes，Runtime 在内存或自己的 session 目录中校验。

`requestCapabilityCatalog` 只读取能力状态；T13 的启用/执行命令必须另加协议版本和显式设置，不由 movie 触发。

### 3.2 Events

```text
helloAck
commandAccepted / commandRejected
runtimeModeChanged
tickLedger
watchFrame
milestone
desync
replaySaveCreated
replaySaveCatalog
replaySaveRestoreProgress
restoreAccelerationStatus
capabilityCatalog
backpressure
fault
pong
```

### 3.3 Core Types

```csharp
public sealed class CompanionLaunchRequest
{
    public string SessionId { get; }
    public int GameProcessId { get; }
    public string PipeName { get; }
    public ProtocolRange RuntimeProtocol { get; }
}

public sealed class CompanionLauncher
{
    public Task<CompanionLaunchResult> EnsureConnectedAsync(
        CompanionLaunchRequest request,
        CancellationToken cancellationToken);
}

public sealed class IpcEnvelope
{
    public int ProtocolVersion { get; }
    public string SessionId { get; }
    public long Sequence { get; }
    public string MessageType { get; }
    public ReadOnlyMemory<byte> PayloadUtf8 { get; }
}

public sealed class RuntimeCommandQueue
{
    public bool TryEnqueue(ValidatedRuntimeCommand command);
    public int Drain(
        TimeSpan budget,
        Action<ValidatedRuntimeCommand> dispatch);
}
```

### 3.4 Disconnect Rule

Companion 断开不能隐式执行 Resume，也不能让已暂停的 Movie 前进。

1. 已处于完整帧 Paused 边界时，停止回放并恢复输入 bindings，保留暂停控制器、时钟租约和命令泵。重连后仍须显式 Step 或 Resume。
2. 清理连接的订阅、上传和未完成控制请求；已落盘存档及 T09 服务保留。不得执行已断连接遗留的命令。
3. 写 `companion-disconnected`、暂停是否保留及 stop result。重连读取同一帧的新鲜状态，不沿用客户端缓存。
4. 正在执行批次时，先标记中断，在完整帧结束后进入 Paused，再清理输入绑定，不生成成功的 StepResult。状态提供 `lastStepInterruptionReason`、`lastInterruptedStepRequestedTicks`、`lastInterruptedStepCommittedTicks`，当前 `movieTick` 表示实际停止点。`disconnectCleanupPending` 表示等待完整帧清理，不能据此声称已暂停。
5. 连续回放断线请求原版支持的暂停边界；场景切换等无法暂停的阶段停止输入并报告 `lastPlaybackFault`，该阶段的暂停保持仍未实现。普通未受 TAS 控制的游戏不因观察客户端断线而被接管。

当前修复已安装，单次实机验证暂停重连仍为同 session/65 帧且语义哈希相同；10000 帧空输入批次中断后只提交 9 帧，停在 75，绑定恢复 true，再正常输入离椅。轻量状态可读中断详情；完整状态白名单补丁已通过 SDK 检查、尚未安装。证据见 artifacts/interactive-origin-smoke/disconnect-boundary-20260914.md。连续回放及场景切换断线未验收。旧“恢复全部 time settings”的清理方式会释放暂停门，不能作为暂停场景的成功标准；既有 disconnect PASS 不整体继承。

v1 不允许“Companion 断开后继续无人值守回放”。

## 4. Independent Verification

### 4.1 Bundle and Launch Matrix

必须自动覆盖：

- 正常随包文件，自动启动并握手成功。
- 路径含空格、中文和非 ASCII 字符。
- entrypoint 缺失、改名、被截断。
- manifest 签名错误、exe SHA-256 错误、RID 错误、协议范围不相交。
- manifest 中的绝对路径、`..`、reparse point 逃逸。
- 已有兼容实例时只注册 session，不创建第二实例。
- 已有不兼容实例时不强杀、不启动风暴，Runtime 降级。
- Companion 启动超时、启动后立即退出、连续崩溃和手动重试。
- `AutoStartCompanion=false` 时零进程创建；手动启动仍可用。

### 4.2 Process Launch Security

- fuzz manifest、启动设置和 IPC；任何 movie 内容不得到达 `FileName`、`WorkingDirectory` 或启动参数构造。
- Release 构建无法通过设置忽略签名/hash。
- 启动日志不包含 session token、原始 bootstrap 数据或敏感环境变量。
- 使用进程审计确认没有 `cmd.exe`、PowerShell、Explorer 或 shell verb 中转。
- Companion 无法要求 Runtime 启动第二个任意程序。

### 4.3 Core IPC

- framing/canonical round-trip。
- length、version、token、nonce、PID/session binding、sequence、unknown command、malformed UTF-8 全部 fail closed。
- fuzz test 无 hang/OOM/未处理异常。
- 同一 game process 连接/断开 100 次。
- 故意发送超长、截断、乱序、重复、unknown message。
- 慢消费者触发 backpressure，游戏仍响应。
- 连续 60 分钟订阅 tick/watch，内存无无界增长。

### 4.4 End-to-End

1. 从干净安装启动游戏，确认 Mod 自动启动 Companion 并只出现一个实例。
2. Companion 打开 T04 fixture，format/validate。
3. 上传并启动 T07 已验证 movie。
4. 显示 milestone、watch、ledger。
5. 故意上传 manifest 不匹配 movie，启动前拒绝。
6. 回放中结束 Companion，确认 bindings/time settings 恢复且游戏内 T09 入口仍可用。
7. 创建手动存档并启用定时存档；重启游戏后由自动启动的 Companion 选择任意存档，观察重建进度并从目标点继续。
8. 分别在 `AutoStartCompanion=false/true` 下运行同一个 T07 oracle，milestone hash 必须完全一致。

### 4.5 T07 Dependency Oracle Mode

T12 的 Companion on/off 对照只验证同一个正常 T07 oracle 的
`milestoneId/movieTick/semanticSha256` 投影，不重复宣称 T07 自身的 deliberate
divergence 门禁通过。`Invoke-T07VerificationCampaign.ps1` 必须提供显式
`-SkipDeliberateDivergence` dependency 模式：

- 仍执行真实冷启动正常 run、CLI campaign 校验、证据导入和全部 Mods/设置/槽
  恢复；
- 不生成 deliberate-divergence run 或 comparison，并将 campaign level 标为
  `DEPENDENCY_ORACLE`、`deliberateDivergencePass=null`，不得冒充 T07
  `SMOKE_PASS/LOCAL_VERIFIED`；
- T07 独立执行的默认行为不变，仍必须运行并验证 deliberate divergence；
- T12 随后只比较 off/on 两个正常 run 的语义 milestone 投影。

当前失败证据：

- `artifacts/companion/t12-final-formal-20260731T074300026Z/` 已通过签名包、
  手动启动、自动启动和 existing-instance reuse，随后在 Companion-off 子 T07
  的独立 deliberate-divergence 自检处 fail closed，尚未进入 soak。
- comparison 的首差异为 baseline tick 0 的 `rng-state`，两边 semantic hash
  相同；T10 加入目标构建 Unity RNG state 后，partial coverage 且未提供 RNG
  playback 的两个冷启动可在注入 attack 前出现 RNG 差异，从而遮蔽原本预期的
  `checkpoint:divergence-probe`。该自检不属于 T12 on/off 语义投影。
- finally 已恢复设置、4 个普通槽、Mods 和 Replay Store；产品进程与
  `Mods.HKTAS-*` 恢复目录均为 0。修正 dependency 模式后必须从零重跑 T12，
  旧运行不得计为 acceptance。
- `artifacts/verification-smoke-t12-dependency-fix-20260731/` 已证明 dependency
  模式只生成正常冷启动 run，campaign level 为 `DEPENDENCY_ORACLE`，
  `deliberateDivergencePass=null` 且无 comparison 路径。
- `artifacts/companion/t12-dependency-fix-smoke-20260731T075000000Z/`
  已从零重跑三种 launch case 和 Companion off/on 对照；5 个
  `milestoneId/movieTick/semanticSha256` 投影逐项一致，矩阵 PASS。该 smoke
  明确跳过 subscription soak，只验证 harness 修正，不替代最终 60 分钟验收。
- `artifacts/companion/t12-final-formal-20260731T075258953Z/` 已通过三种 launch
  case 与修正后的 on/off parity，随后在 T11-backed soak 的首个 functional
  probe 超时，尚未进入 performance run。对应 ModLog 的实际根因是 T11
  matrix 接受 `PerformanceSeconds=[60,7200]` 且 T12 固定传入 3600，但 Runtime
  `InspectorProbeOptions` 仍拒绝大于 1200 的 duration，导致 Runtime host
  启动时报 `performance duration must be in [60, 1200]`，外层只能观察到 probe
  文件缺失。
- Runtime probe 的接受范围必须与已发布 T11/T12 驱动一致为 `[60,7200]`；
  默认 T11 acceptance 仍为 600 秒，T12 使用 3600 秒。超出 7200 仍 fail
  closed。修正后先做真实参数边界 smoke，再重建安装包并从零重跑 T12；旧失败
  不得计为 acceptance。该次 finally 同样恢复设置、4 个普通槽、Mods、
  Replay Store，且无产品进程或恢复目录残留。
- 将 Runtime duration 上限修正为 7200 后，前两次 1201 秒参数 smoke 均已证明
  参数被接受，但 FUNCTIONAL probe 在 180 秒内部超时。完整 session 事件证明
  `Menu_Title -> GG_Workshop` 已完成，后续外部 `requestSnapshot` 也成功；实际
  循环等待来自 T11 probe 把 `journal.LastCommittedMovieTick >= 0` 错误地作为
  跳转固定测试场景的前置。Godhome 起点尚未形成中性静止基线时，probe 因而
  永远不能离开起始场景。
- probe 启动路由现只要求 GameManager 处于 PLAYING、无场景切换且 active hero
  可用；进入固定测试场景后仍必须等待真实 journal tick、完整 Inspector provider
  集和全部原有功能门禁，未弱化验收。重新签名安装包的 manifest SHA-256 为
  `1cb9bab2f9c418113b2b757179ef8569134608a246ce9ed553bb719cb9a59fa1`，
  package SHA-256 为
  `ff0d39084a6b76177394a5a305dae291b797fe3d37e914df337dcef04702328e`。
- `artifacts/inspector/t11-duration-boundary-smoke-retry2-20260731T083100000Z/`
  已在同一 Godhome 槽位连续完成 2 次真实 FUNCTIONAL run；两次结果均记录
  `performanceSeconds=1201`、`runPass=true`、18 个 sample，stable identity
  一致且 matrix `fullGatePass=true`。设置、4 个普通槽 hash 完全恢复，产品进程
  与恢复目录均为 0。该证据只验证参数边界与启动路由修正，不替代最终 3600 秒
  T12 soak。
- `artifacts/companion/t12-final-formal-retry2-20260731T083500000Z/` 已从零
  完成正式矩阵并 `pass=true`：3 个 launch case、Companion off/on 的 5 个
  semantic milestone 投影、3,635.037 秒/364 样本 subscription soak 和嵌套
  T11 3,600 秒 PERFORMANCE 全部通过。Companion private bytes 前/后 10%
  均值增长 26,699,548 bytes，峰值 239,337,472 bytes；Inspector p95
  0.1719 ms、attributable average 0 bytes/frame。完整报告见
  `mydocs/evidence/T12_Studio与本地IPC验收报告.md`。

### 4.6 Cold Restore Relaunch

- 正常 intent：源进程退出后 Companion 只创建一个新游戏进程；新 session claim 同一 intent，
  T09 到达 `PausedAtTarget` 后完成，lineage 无缺口。
- source/target 都必须通过 T24 0.80 的实时 `StartupProfileAttestation`。source 还必须能追溯到
  Companion 的精确 PID/创建时间启动证据；target run ID 必须等于 operation ID。只有 bundle/hash
  匹配但 Runtime attestation 缺失、pending 或 faulted 时不得继续 cold restore。
- 普通 Steam 启动的 session 必须在 Studio 明确显示 `StartupUnverified`，严格回退入口给出经固定
  TAS 启动链安全重启的操作，不自动在 gameplay 中强退，也不降级到同进程恢复冒充成功。
- 分别篡改 intent、game/profile/Runtime hash，模拟 stale/duplicate claim、错误 PID 创建时间、源进程
  未退出、新 session 握手超时和启动器崩溃；全部 fail closed，且不出现启动风暴或孤儿进程。
- `AutoStartCompanion=false` 时不得自动重启；手动下次启动仍可 claim 合法 pending intent。
- T24 双进程 smoke 中，Companion/restart 开关本身不改变完整 normalized gameplay trace。
- `restoreReplaySave`、`seekMovieTick`、`applyBranchAndSeek` 共用单个 active operation、单次 source exit、单次
  target launch 和同一 claim/lineage。seek 与 branch 在 prepare 时附带 source movie-tick/scene-epoch CAS；
  Runtime 选择兼容 replay checkpoint 后，把实际 replay-save ID 写入 intent。
- `PausedAtTarget` 不能只按 phase 名放行。Supervisor 必须核对 intent target tick 和 target verification mode：
  saved restore 需要 exact expected/actual/strict；新分支需要空 expected、空 strict 结论和两个合法 actual hash。

### PASS

- 固定随包路径、签名、SHA-256、RID 与协议验证全部 fail closed。
- 正常自动启动、手动启动、已有实例复用、超时/熔断和关闭设置全部符合状态机。
- 非法消息被结构化拒绝，Runtime 不崩溃。
- Unity 主线程无同步进程/pipe 等待；command drain 不超过预算，超限生成 backpressure。
- 100 次 reconnect 与 60 分钟 soak 通过。
- Companion 开关不改变 T07 oracle。
- disconnect cleanup 全 PASS。
- Companion 与游戏内入口看到同一个 T09 存档目录；手动、自动和损坏状态标记一致。
- T14 未安装/未启用时 Studio 只显示 `FullReplay`，不影响 T09；启用时准确显示 keyframe candidate、tail 范围和 fallback 原因。
- cold restore 只接受认证、hash-bound、单次 intent；精确启动一个固定游戏进程并把新旧 session
  连成可审计 lineage，失败时停止且不降级冒充成功。

### FAIL

- Runtime 启动任意客户端/movie 提供的路径、shell 或程序。
- 签名/hash 不匹配仍执行 Companion。
- IO/launch thread 直接调用 Unity API，或 Unity 主线程同步等待外部进程。
- Companion 连接/自动启动改变 T07 hash。
- crash loop 无界创建进程。
- 通过进程名误杀非本 session 的程序。
- disconnect 留下输入锁或 `timeScale` 异常。
- Companion 故障导致 Runtime/T09 不可用。
- cold intent 能注入任意启动目标/参数，或 stale/duplicate/wrong-build intent 仍创建进程；
- 源进程未完成 quiesce 就重启、失败后自动循环重启、按进程名误杀或留下孤儿游戏进程。

## 5. Implementation Checklist

- [x] 写 Companion bundle v1、签名、路径和升级边界。
- [x] 实现 manifest schema、签名/SHA-256/RID/协议验证。
- [x] 实现异步 launcher、existing-instance attach、超时、退避和熔断。
- [x] 实现 `AutoStartCompanion`、手动启动/重连/禁用和游戏内状态提示。
- [x] 写 IPC v1 协议与威胁边界。
- [x] 实现 Core framing/codec/validator。
- [x] 实现 ACL/token/nonce named pipe server/client。
- [x] 实现 bounded queue、main-thread dispatcher、backpressure。
- [x] 实现 Companion single-instance/session registry。
- [x] 实现 WPF editor、validation、timeline、diff 和 T09 catalog。
- [x] 添加 bundle tamper、path escape、launch matrix、unit/fuzz/reconnect/soak tests。
- [x] 完成 Companion 开关与 T07 oracle 对照。
- [x] 完成端到端与 disconnect cleanup 验收。
- [x] 实现 `ColdRestoreIntentStore` 的原子状态迁移、单次 claim、过期/篡改拒绝和失败终态。
- [x] 实现 `ColdRestoreSupervisor` 的单次退出/启动/claim/handoff 状态机，并复用 T13 target startup profile。
- [x] 实现跨 source/new Runtime session lineage、超时/崩溃清理及无 crash-loop 保障。
- [x] 将 replay-save restore、movie seek 和 apply-branch-and-seek 接入同一 cold supervisor。
- [ ] 将固定 `VerifiedGameLauncher` 的可信 launch receipt 绑定到 source/target attestation。
- [ ] 实现 source/target 实时 startup attestation、可信 launch receipt 和普通用户安全启动/重启入口。
- [ ] 完成 4.6 定向负控和一次冻结后的 T24 双进程完整 smoke。

## 6. Rollback

### 2026-09-07 Studio 启动入口

- 主窗口提供“启动 TAS 游戏”，无需已有 Runtime session；用户选择 `hollow_knight.exe`。
- 由 App 注入启动服务，共享 cold supervisor 的凭据存储和固定 `ClockStartup` 目录。
- 已有游戏进程时要求用户先正常保存退出，不自动终止或重启现有游戏。
- 启动成功且凭据写入后解除启动监督；回退功能仍须等待启动和录制根证明，不因按钮成功直接宣称可用。
- 本批已通过 Companion 编译；发布包与实际操作流程尚未验收。

### 2026-09-07 发布与安装分离

- 已有发布脚本包含八个 ClockStartup 文件及外层签名清单，保留该实现。
- `Build-CompanionBundle.ps1 -StageOnly` 完成 Companion 包构建、签名、验证后返回，不进入安装目录删除或 Runtime 自动安装步骤。
- `-GameExecutable` 显式传给启动组件构建脚本；默认保持本机已配置的游戏路径。
- StageOnly 只表示 Companion 暂存包通过签名验证，不表示完整 Mod 安装包或实机验收成功。

- `AutoStartCompanion=false` 时不发现、不验证、不创建外部进程；游戏内核心功能保持可用。
- 删除 Companion sidecar 只会进入 `CompanionUnavailable`，不得阻止 Mod 加载。
- IPC 可独立关闭；关闭后不创建 pipe/thread。
- shutdown 顺序：停止接收 → 拒绝新命令 → cleanup runtime → flush evidence → dispose pipe → 关闭 session。
- 不删除用户 movie、T09 存档或 Companion 未保存的编辑内容。
