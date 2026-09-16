# Task Spec T15: 外部自动化与 AI 辅助接口

- **Status**: IN_PROGRESS
- **Gate**: G5 External Automation / AI Safety
- **Depends On**: T10, T12
- **Produces**: 版本化 Automation API、外部 SDK/CLI、stdio MCP bridge、控制租约、显式 typed state mutation adapters、AI 辅助 TAS 闭环与完整审计证据

## 0. Open Questions

- None。v1 固定为本机单用户、Companion broker、当前用户 ACL named pipe 和可选 stdio MCP bridge；不开放远程网络服务。

## 1. Requirements

### Goal

让外部调试工具和 AI 客户端能够：

1. 读取 Runtime 当前模式、manifest、tick、movie cursor、语义状态、FSM/watch、RNG、checkpoint、恢复策略和 desync 证据。
2. 查询当前构建真正支持的命令、参数 schema、前置条件和副作用。
3. 在显式策略与短期控制租约允许时，执行暂停、恢复、单步、运行到条件、录制/回放、创建/恢复存档和 movie 分支修改。
4. 在独立调试模式下，通过编译期白名单 writer adapter 对少量语义状态执行带前置条件、原子回滚和完整审计的 typed mutation，而不是任意内存/反射写入。
5. 以“观察 → 提议 movie patch → 离线校验 → 受控应用 → 重放 → 比较结果”的闭环辅助制作 TAS。
6. 让每个外部动作可归因、可重放、可撤销，并且不会绕过 T04/T07/T09/T12 的安全与确定性门禁。

### Product Contract

- T12 的 Runtime pipe 是内部受信传输，不直接公开给任意外部进程。
- Companion 是唯一 automation broker；外部客户端不能直接调用 Unity/HK API。
- v1 默认 `ReadOnly`。控制能力必须由用户设置启用，并取得单一、短期、可撤销的 control lease。
- 所有控制命令必须是编译期登记的 typed command，带 schema、capability、expected session/mode/tick 和 idempotency key。
- Runtime 只在 T12 的 Unity 主线程安全队列消费已验证命令。
- AI 与普通调试客户端使用同一语义协议；“AI”不获得额外隐藏权限。
- 外部接口不承诺 AI 决策正确；可验证性来自 movie、hash、ledger、desync 和审计证据。
- typed state mutation 只服务调试和探索；首次成功应用后，当前运行永久标记为 `NonVerifiableDebugMutation`，不得生成 T07/T16 通过结论。

### Access Modes

| Mode | 权限 | 默认 |
|---|---|---|
| `Disabled` | 不启动 automation endpoint；所有客户端拒绝 | 否 |
| `ReadOnly` | 状态、资源、事件、schema、离线 validate/diff | **是** |
| `ApprovedControl` | 在用户批准的 scope 和 control lease 内执行白名单控制 | 否 |

设置从 `Disabled/ReadOnly` 升到 `ApprovedControl` 必须由游戏内或 Companion UI 的用户操作完成。movie、MCP prompt、外部请求和 NativeHost 无权自行提权。

### In-Scope

- `automation-v1` request/response/event/schema。
- Companion 内的 current-user automation broker 与独立客户端认证。
- 只读状态快照、增量事件和有界 timeline window。
- capability/scopes discovery。
- 单控制者租约、TTL、续租、撤销、断线释放和幂等执行。
- 外部 .NET SDK 与 CLI。
- `HollowKnightTAS.AgentBridge`：stdio MCP server，把 MCP resources/tools 映射到 automation API。
- AI-safe movie branch/patch/validate/apply 工作流。
- 显式 `setHeroPose`、`setPlayerResources` writer adapters；只在暂停安全点、独占租约和 `DebugMutationEnabled` 用户批准下可用。
- mutation 的 compare-and-set 前置、范围校验、before/after snapshot、失败回滚、审计与非验证标记。
- 所有调用的 actor、scope、request hash、tick、result、side effect 和 correlation ID 审计。
- 离线 mock Runtime、scripted agent 和实机 oracle 验收。

### Out-of-Scope

- 任意内存地址读写、通用反射、C#/Lua/shell 执行。
- 任意 watch key 写入、FSM state 强跳、任意 PlayerData 字段、任意组件属性、RNG state 或场景对象创建/销毁。
- 任意文件路径、进程/DLL、网络 URL 或环境变量访问。
- 远程 HTTP、云账户、OAuth、互联网暴露或遥测。
- 由 AI 删除 T09 存档、覆盖普通槽位、启用 T13 native capability、修改安全策略或退出/强杀游戏。
- 让客户端提交原始 `UnityEngine.Object`、FSM action 类型或跨运行 instance ID。
- 把自然语言直接当作 Runtime 命令；自然语言必须先变成可校验的 typed proposal。
- 自动接受 AI 生成的 movie patch。

## 1.5 Code Map

```text
docs/protocol/
  Automation-v1.md
docs/ai/
  MCP-v1.md
  AI-TAS-Workflow-v1.md
schemas/
  automation-envelope-v1.schema.json
  automation-capability-v1.schema.json
  automation-state-v1.schema.json
  automation-command-v1.schema.json
  movie-patch-v1.schema.json
src/HollowKnightTAS.Core/Automation/
  AutomationScope.cs
  AutomationMode.cs
  AutomationCapability.cs
  AutomationStateEnvelope.cs
  AutomationCommandEnvelope.cs
  AutomationResultEnvelope.cs
  ControlLeaseDescriptor.cs
  MoviePatchProposal.cs
  StateMutationProposal.cs
  StateMutationResult.cs
src/HollowKnightTAS.Companion/Automation/
  AutomationBroker.cs
  AutomationPipeServer.cs
  AutomationSessionAuthenticator.cs
  AutomationCapabilityCatalog.cs
  ControlLeaseManager.cs
  AutomationCommandRouter.cs
  AutomationAuditSink.cs
  MoviePatchWorkspace.cs
  StateMutationCoordinator.cs
src/HollowKnightTAS.Runtime/Automation/Mutation/
  IStateMutationAdapter.cs
  HeroPoseMutationAdapter.cs
  PlayerResourcesMutationAdapter.cs
  StateMutationTransaction.cs
src/HollowKnightTAS.Automation.Client/
  HollowKnightTAS.Automation.Client.csproj
  AutomationClient.cs
  AutomationSubscription.cs
src/HollowKnightTAS.AgentBridge/
  HollowKnightTAS.AgentBridge.csproj
  Program.cs
  McpStdioServer.cs
  McpResourceCatalog.cs
  McpToolCatalog.cs
  AutomationMcpMapper.cs
src/HollowKnightTAS.Cli/Commands/Automation/
  AutomationStatusCommand.cs
  AutomationWatchCommand.cs
  AutomationCallCommand.cs
tests/HollowKnightTAS.Automation.Tests/
tests/HollowKnightTAS.AgentBridge.Tests/
fixtures/automation/
artifacts/automation/<sessionId>/
  clients/
  audit/
  scripted-agent/
  security/
  verdict.md
```

## 2. Architecture

### 2.1 Trust Boundaries

```text
HK Runtime (Unity main thread)
  │ T12 authenticated internal named pipe
  ▼
Companion Automation Broker
  ├─ current-user named pipe ── SDK / CLI / debug client
  └─ current-user named pipe ── AgentBridge
                                  │ MCP stdio only
                                  ▼
                              AI/MCP client
```

- Runtime 不监听公开 automation endpoint。
- Companion 根据 Runtime handshake 绑定 `sessionId + game PID/create time + manifest hash`。
- AgentBridge 不保存 Runtime token；启动时从受限 bootstrap 文件/句柄取得一次性 Companion credential。
- MCP v1 只使用 stdio，不监听端口。stdout 只写合法 MCP JSON-RPC，日志只写 stderr。
- Companion 与 AgentBridge 均由 T12/T15 签名 bundle 和精确 hash 管理；movie 不能指定可执行路径或参数。

### 2.2 State Model

外部状态必须是稳定语义 DTO，不是对象图：

```csharp
public sealed class AutomationStateEnvelope
{
    public int SchemaVersion { get; }
    public string SessionId { get; }
    public string ManifestSha256 { get; }
    public string RuntimeMode { get; }
    public long MovieTick { get; }
    public string TickPhase { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public long AgeMilliseconds { get; }
    public string SemanticSnapshotSha256 { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }
    public IReadOnlyList<string> ActiveCapabilities { get; }
}
```

每个响应必须标明：

- 捕获 tick/phase 和数据新鲜度；
- manifest/session；
- canonical snapshot hash；
- 字段是否 `available/unsupported/redacted/stale`；
- 允许的下一步命令及其前置条件。

不得用“当前”掩盖陈旧缓存；超过 capability 声明的 freshness budget 时返回 `StaleState`。

### 2.3 Read Resources

Automation URI：

```text
hktas://session/current/status
hktas://session/current/manifest
hktas://session/current/capabilities
hktas://session/current/state/summary
hktas://session/current/watch/<stable-path>
hktas://session/current/timeline?from=<tick>&count=<n>
hktas://session/current/desync/latest
hktas://session/current/replay-saves
hktas://session/current/movie
```

资源分页、有长度上限并返回 canonical JSON。原始存档 bytes、绝对路径、session token、用户名和任意进程信息不得暴露。

### 2.4 Scopes and Capabilities

v1 scope 不使用 wildcard：

- `observe.status`
- `observe.state.summary`
- `observe.state.deep`
- `observe.timeline`
- `observe.desync`
- `observe.replay-saves`
- `movie.read`
- `movie.propose`
- `movie.validate`
- `movie.apply-branch`
- `control.playback`
- `control.step`
- `control.run-until`
- `control.replay-save`
- `control.recording`
- `debug.state.pose`
- `debug.state.resources`

Capability catalog 对每项提供：

- 输入/输出 JSON Schema；
- 是否只读；
- 所需 scope；
- 是否需要 control lease；
- 允许的 Runtime mode/phase；
- deterministic side-effect 描述；
- timeout、idempotency 和 rollback 语义；
- 当前 build/profile 的 `available/experimental/unsupported`。

### 2.5 Control Lease

`ApprovedControl` 下仍必须取得 control lease：

```csharp
public sealed class ControlLeaseDescriptor
{
    public string LeaseId { get; }
    public string ClientId { get; }
    public IReadOnlyList<string> Scopes { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public string SessionId { get; }
    public string ManifestSha256 { get; }
}
```

- 同一 Runtime session 最多一个 write lease。
- 默认 TTL `30s`，最长 `5min`；客户端必须续租。
- 断线、Companion fault、session/manifest 改变、用户撤销或超时立即失效。
- lease 不是认证凭据；每个请求仍需独立认证与 scope 检查。
- 控制命令必须携带 `expectedMode` 和可选 `expectedMovieTick`，不匹配时返回 `PreconditionFailed`。

### 2.6 Command Safety

允许的控制命令映射到既有服务：

| 外部命令 | Runtime 事实源 |
|---|---|
| pause/resume/step/run-until | T08 |
| start/stop recording、start/stop replay | T06 |
| create/list/restore/approve/cancel/resume replay save | T09 |
| get RNG ledger/profile | T10 |
| get semantic watch/timeline/desync | T05/T07/T11 |
| validate/propose/apply movie branch | T04 + Companion workspace |
| get restore strategy/status | T14（存在时） |
| set hero pose / velocity | T15 `HeroPoseMutationAdapter`，仅调试模式 |
| set health / soul | T15 `PlayerResourcesMutationAdapter`，仅调试模式 |

任何未登记 command ID、unknown field、超长 payload、重复 sequence、过期 idempotency key 或 mode/tick 前置不满足均 fail closed。

### 2.7 Typed State Mutation

mutation 不是通用属性编辑器。v1 只注册两个命令：

```text
setHeroPose {
  expectedSnapshotSha256,
  expectedMovieTick,
  position: { x, y },
  velocity: { x, y }
}

setPlayerResources {
  expectedSnapshotSha256,
  expectedMovieTick,
  health,
  soul
}
```

执行约束：

1. `ExternalAutomationMode=ApprovedControl` 且用户单独启用
   `DebugMutationEnabled`。
2. 客户端持有相应 `debug.state.*` scope 的独占 write lease。
3. Runtime 已由 T08 暂停并到达登记的 mutation safe point；回放、场景过场、死亡/重生、存档恢复和验证模式中拒绝。
4. `expectedSnapshotSha256 + expectedMovieTick` 必须与当前状态一致。
5. adapter 先读取 before snapshot，完整校验 finite/range/invariant，再一次性应用；任一步失败必须恢复 before snapshot。
6. 成功后强制重新采样 T05/T11，返回 before/after hash 与 typed diff，并写审计。
7. 当前 session 与后续从该状态派生的 movie 运行标记
   `NonVerifiableDebugMutation`；只能通过无 mutation 的干净冷启动重新获得验证资格。

v1 数值范围必须在协议中冻结；不得接受 `NaN/Infinity`。资源修改不得超过当前
合法上限，位姿修改不得跨到未加载 scene，也不得发送 FSM event、改 RNG、改
Boss HP 或直接设置完成标志。

### 2.8 AI TAS Workflow

AI 辅助流程必须形成可审计分支：

1. 读取 manifest、capabilities、movie 和目标附近 timeline/state。
2. 生成 `MoviePatchProposal`，包含 base movie hash、修改区间、理由和预期 milestone。
3. Companion 在独立 workspace 创建分支，不覆盖当前 movie。
4. T04 离线 parse/validate/canonicalize。
5. 无 control lease 时只返回验证报告；不得运行。
6. 有 lease 时由 T06/T07 执行候选分支。
7. 返回目标 hash、时间、首差异和与基线的结构化 diff。
8. 用户或客户端可继续迭代；只有显式 `apply-branch` 才更新当前工作分支。

AI 客户端不能直接修改 expected hash、baseline、manifest 或验证 verdict。
调试 mutation 可以用于探索和复现，但任何候选要进入正式验证，必须从干净
起点重新生成纯输入 movie；mutation 记录不能被“清除”或伪装成合法输入。

### 2.9 MCP Mapping

MCP 只是一层适配，不是 Runtime 权限源：

- Resources 映射 2.3 的只读 URI。
- Tools 映射 capability catalog 中允许的 typed operations。
- 每个 tool 都有明确 input/output schema，返回 `structuredContent` 和兼容文本摘要。
- Tool annotations 仅供 UI，Companion 仍独立做授权和前置检查。
- AgentBridge 不提供 prompts 来自动扩大权限。
- MCP client 关闭时 stdio server 退出并释放 automation 连接；write lease 由 Companion 超时/断线回收。

首批 tools：

```text
hktas_get_state
hktas_get_timeline
hktas_get_desync
hktas_propose_movie_patch
hktas_validate_movie_patch
hktas_acquire_control
hktas_release_control
hktas_pause
hktas_resume
hktas_step
hktas_run_until
hktas_create_replay_save
hktas_restore_replay_save
hktas_approve_replay_save_overwrite
hktas_cancel_replay_save_restore
hktas_resume_replay_save_restore
hktas_apply_movie_branch
hktas_set_hero_pose
hktas_set_player_resources
```

## 3. Detailed Design

### 3.1 Command Envelope

```csharp
public sealed class AutomationCommandEnvelope
{
    public int SchemaVersion { get; }
    public string RequestId { get; }
    public string IdempotencyKey { get; }
    public string ClientId { get; }
    public string SessionId { get; }
    public string ManifestSha256 { get; }
    public string CommandId { get; }
    public string RequiredScope { get; }
    public string LeaseId { get; }
    public string ExpectedRuntimeMode { get; }
    public long? ExpectedMovieTick { get; }
    public ReadOnlyMemory<byte> CanonicalArgumentsUtf8 { get; }
}
```

### 3.2 Client Interface

```csharp
public interface IAutomationClient : IAsyncDisposable
{
    Task<AutomationHandshake> ConnectAsync(
        AutomationConnectOptions options,
        CancellationToken cancellationToken);

    Task<AutomationStateEnvelope> GetStateAsync(
        StateQuery query,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AutomationEventEnvelope> SubscribeAsync(
        AutomationSubscription subscription,
        CancellationToken cancellationToken);

    Task<AutomationResultEnvelope> ExecuteAsync(
        AutomationCommandEnvelope command,
        CancellationToken cancellationToken);
}
```

### 3.3 Audit Record

每次 read/control 都写：

```text
schemaVersion, timestampUtc, correlationId, clientIdHash,
transport, requestId, idempotencyKeyHash, sessionId,
manifestSha256, commandOrResource, scope, leaseIdHash,
requestedAtTick, acceptedAtTick, completedAtTick,
resultCode, sideEffectSummary, responseSha256
```

不记录 credential、原始 token、用户名、自然语言 prompt、原始存档或绝对路径。`clientId`/lease/idempotency 只记录 session-salted hash。

### 3.4 Typed Mutation Real-Matrix Driver

`scripts/Invoke-T15AutomationMatrix.ps1` 增加独立入口：

```powershell
param(
    [switch]$MutationOnly
)

function Invoke-TypedMutationParity
function Invoke-CleanEligibilityRestart
function Get-SemanticNativeValue
function Assert-MutationCommitted
function Assert-RejectedMutationUnchanged
```

- `MutationOnly` 与 Discovery/ApprovedControl-only 互斥，复用同一 Mods、设置、
  Replay Store、automation workspace 和普通槽位 `finally` 恢复协议。
- mutation session 固定
  `ApprovedControl + DebugMutationEnabled=true + InputNeutralGameplay`。
- SDK 先覆盖无租约、错误 scope、非 Paused、stale tick、stale hash、
  `NaN/Infinity`、位置/资源越界；每个已到 Runtime safe point 的失败都重新读取
  canonical state，要求 before/after SHA-256 完全相同。
- SDK、CLI、MCP 对 `setHeroPose` 与 `setPlayerResources` 各执行
  `$ParityIterations` 次；每次核对返回的 before/after hash、typed diff、非验证
  标记和非视觉读回值。
- SDK 连接在持有租约时直接断开，CLI 随后必须能取得新租约，证明 disconnect
  回收；MCP 结束时显式释放其 bridge-owned lease。
- mutation session 结束后，`Invoke-CleanEligibilityRestart` 用新进程、
  `DebugMutationEnabled=false` 冷启动，要求
  `verificationEligibility=Eligible` 且两个 mutation tools/capabilities 均隐藏。

### 3.5 Stable Runtime Rejection Codes

`src/HollowKnightTAS.Runtime/Ipc/RuntimeCommandDispatcher.cs` 不得把 CLR
exception type 作为公开协议结果码。新增内部 typed rejection：

```csharp
private sealed class RuntimeCommandRejectionException : Exception
{
    public string ErrorCode { get; }
}

private static string GetCommandErrorCode(Exception exception);
```

映射固定为：

- 显式 typed rejection：原样返回登记的安全 `ErrorCode`；
- `ArgumentException` / `InvalidDataException` / `FormatException` /
  `OverflowException`：`InvalidArguments`；
- 其他可预期 `InvalidOperationException`：`RuntimeRejected`；
- 未分类异常：`RuntimeFault`。

mutation 的 stale tick/hash 必须显式返回 `PreconditionFailed`，busy 返回
`MutationBusy`，非安全 phase 返回 `UnsafePhase`。detail 可提供清洗后的诊断，
但客户端分支只能依赖稳定 code，不能依赖异常类名或英文 message。

### 3.6 Real State/Timeline Durability Driver

`scripts/Invoke-T15AutomationMatrix.ps1` 增加互斥入口 `-StateSoakOnly`：

- 使用 `ApprovedControl + DebugMutationEnabled=false + InputNeutralGameplay`
  启动一个真实 Runtime；`-Smoke` 只做 100 reads、5 reconnects、5 秒订阅，
  正式运行固定为 10,000 reads、100 reconnects、3,600 秒订阅。
- 一个 SDK 长连接连续读取 fresh semantic state；逐次验证 session、manifest、
  tick/phase、canonical hash、`ageMilliseconds <= 5000`，并记录吞吐与首末状态。
- 另建 100 个 SDK 连接，各完成认证、状态读取和释放；任一重连失败即整项失败。
- 同时用已安装 CLI 的 `automation watch` 调用
  `SubscribeTimelineAsync`，每 250ms 拉取有界 timeline。所有 JSONL 必须合法、
  成功，sequence 严格递增且不重复；窗口淘汰只能通过
  `gapBeforeWindow=true` 显式出现。
- CLI `automation watch` 必须支持互斥的
  `--iterations=<1..100000>` 与 `--duration-seconds=<1..86400>`。未指定时保持
  单次读取兼容行为；duration 模式在连接成功后使用单调时钟，只有实际经过至少
  请求时长后取得并输出一页结果才可正常退出。禁止用“时长 ÷ 理想轮询间隔”
  近似换算迭代数，因为 IPC、JSON 序列化和 stdout 写入均会增加每轮耗时。
- 正式驱动必须使用 `--duration-seconds=$SubscriptionSeconds`，不得再生成
  `$SubscriptionSeconds * 4` 的固定迭代数；watch 总完成预算仍为请求时长
  加 120 秒，超时必须 fail closed。
- 每 5 秒记录游戏、Companion 和 watch client 的 working set/private bytes。
  Companion 在 warm-up 后的 private bytes 峰值不得增长超过 256 MiB，结束值
  不得增长超过 64 MiB；watch 必须至少持续请求的订阅时长，且不能提前退出。
- `memory-samples.json` 必须在内存阈值断言之前原子落盘；即使阈值失败，也要
  保留完整采样轨迹，不能只留下异常文本。
- Broker timeline 同时受条目数与实际 canonical payload 字节数约束：
  最多 5,000 条且 payload 合计最多 8 MiB，任一先到即淘汰最旧项；淘汰游标只
  保留 sequence/movie tick，不得继续持有已淘汰 payload。分页必须继续以
  `gapBeforeWindow=true` 明示淘汰。
- 只读 observation/validation 命令不保留完整 idempotency 响应，因为重算没有
  副作用；有副作用的 lease/control/proposal/mutation 命令保留最近 2,048 项、
  最多 8 MiB 的结果。被结果缓存淘汰的 key 转入最多 65,536 项的 session
  tombstone，重用时返回稳定 `IdempotencyExpired`，历史容量耗尽则在执行前
  fail closed，不能因观察流量淘汰控制命令后再次执行。
- 任何失败或中断都必须终止 watch、游戏和 helper，并复用 T15 的设置、普通槽、
  Mods、Replay Store 与 automation workspace 恢复协议。

### 3.7 Real MCP Scripted-Agent Driver

增加互斥入口 `-AiOnly`，通过真实 AgentBridge stdio MCP（不直接调用 SDK
执行 workflow）完成 `$ParityIterations` 轮：

1. 读取 non-visual state、timeline、desync 和 current movie；若无 movie，先通过
   MCP record/stop 生成一份 input-neutral 基线。
2. 从 current canonical movie 派生一个包含唯一 marker/checkpoint 的合法候选，
   同时生成一个含未登记 action 的非法候选。
3. 非法候选必须在 `validate` 阶段返回 `MovieInvalid`；合法候选经
   `propose` 写入隔离 branch。apply 前再次读取 current movie，ID 必须仍等于
   base movie ID。
4. 只有显式 `apply-branch` 才允许 current movie ID 变为 proposal hash；随后
   start replay，并从 timeline 观察本轮唯一 checkpoint milestone，读取 desync
   证据且不得出现结构化 desync。
5. 每轮记录 base/branch/current ID、非法拒绝、milestone、状态 hash 与审计
   correlation；10 轮结束后释放 lease，验证 `verificationEligibility=Eligible`。

该驱动本身作为兼容 MCP client smoke；stdio 的每个 stdout 行仍必须是合法
JSON-RPC，workflow 不能借助屏幕截图、OCR、像素或视觉识别。

### 3.8 MCP Input and Cleanup Hardening

- AgentBridge 不得先用无界 `ReadLineAsync` 分配完整攻击者输入；使用有界、
  可恢复的行读取器，最多保留 1 MiB。超限行排空到换行并返回 `-32700`，随后
  仍能处理下一条合法请求。
- tool validator 必须实际执行 schema 的 string `maxLength`、array
  `minItems/maxItems/uniqueItems`、item type、integer bounds 和 finite-number
  检查，不能假定 MCP client 已预校验。
- 未分类异常只向 stdout 返回固定 `Internal error.`；异常类型可写 stderr，
  但不得回显 credential、原始请求、绝对路径或异常 message。
- 增加互斥入口 `-SecurityOnly`：真实枚举 tool/resource catalog，fuzz
  `file/http/path traversal` URI、shell/process/DLL/URL/reflection/address/raw
  save/native-enable tool 名、unknown field、超长 string、重复/超量 scope；
  全部必须在 AgentBridge/Companion 边界 fail closed 且状态 hash 不变。
- AgentBridge 持有 lease 时被终止后，替代 SDK client 必须在 5 秒内获得 lease；
  结束矩阵后 Runtime 为 Running/Idle、recording inactive，无 helper、bridge 或
  watch orphan。

## 4. Independent Verification

### 4.1 Offline Protocol

- 所有 schema 有 canonical vectors；同输入重复 100 次字节/hash 一致。
- unknown command/scope/field、重复 key、malformed UTF-8、超长 payload、深层 JSON、NaN/Infinity 全部 fail closed。
- SDK、CLI、AgentBridge 对同一 fixture 产生相同 canonical command/result。
- MCP stdout 逐行均为合法协议消息；日志只出现在 stderr。
- 正式矩阵驱动必须在目标 PowerShell 7 上先完成自检；测试输出追加使用有效
  参数集，驱动自身的参数/日志错误必须在启动游戏前结构化失败，不得误记为
  Runtime 或接口 verdict。
- 驱动按 case 使用对应 probe schema：Disabled/ReadOnly 的 input-phase probe
  不读取 ApprovedControl input-neutral readiness 专属字段；严格模式下缺失字段
  必须归因为 harness schema 错误，而不是产品权限失败。

### 4.2 Authentication and Lease

- 未认证、错 session/PID/manifest、重放 nonce、伪造 client、过期 token 全拒绝。
- 两客户端竞争 write lease 只有一个成功。
- TTL、断线、用户撤销、Runtime 重启、manifest 变化均在预算内回收 lease。
- 已接受的 idempotency key 重试只返回同一结果，不重复执行控制动作。
- `ReadOnly` 下所有 write tools 不出现在可用 capability 或返回明确拒绝。

### 4.3 State and Backpressure

- 状态响应的 tick/phase/freshness/hash 与 Runtime 原始证据一致。
- 10,000 次 read、100 次 reconnect、60 分钟事件订阅无无界增长。
- 慢客户端只丢弃/合并允许降采样的观察事件；不能阻塞 Unity 主线程。
- timeline 分页无跳号/重复；发生 gap 时显式标记。

当前失败证据与修正：

- `artifacts/automation/t15-state-soak-formal-20260729T102427294Z/` 在
  304/3600 秒时遭外部中断并以 broken pipe 结束；无 `matrix.json`，不计为
  acceptance。finally 已恢复设置、普通槽、Mods、Replay Store 和 automation
  workspace。
- `artifacts/automation/t15-state-soak-formal-20260731T040157792Z/` 连续运行至
  3720 秒硬上限后 fail closed。CLI 仅完成 13,311/14,402 次轮询；根因是驱动把
  3600 秒按理想 250ms 间隔换算为固定迭代数，却未计入每轮 IPC、序列化和写盘
  耗时。该证据只能证明故障回退与资源恢复，不满足 60 分钟 PASS。
- 修正后必须以 CLI 原生 `--duration-seconds=3600` 从零重跑；旧运行的时长、
  页数或内存样本不得拼接复用。
- `artifacts/automation/t15-state-duration-fix-smoke-20260731T051449672Z/`
  已验证修正后的 duration 语义：请求 5 秒，CLI 在实际 5.802 秒后正常退出，
  输出 20 个合法页面和 24 条严格递增事件；100 reads、5 reconnects、最大状态
  年龄 2ms、Companion 峰值/结束增长均 1.5 MiB，最终
  `verificationEligibility=Eligible`。审计泄漏为 0，设置、4 个普通槽、Mods、
  Replay Store 与 automation workspace 均恢复。该 smoke 不替代正式 60 分钟
  acceptance。
- `artifacts/automation/t15-state-soak-formal-20260731T051559368Z/`
  已用原生 duration 模式完整运行 3,600 秒并正常结束 watch，输出 13,577 个
  合法页面；随后因 Companion private bytes 峰值增长 182,116,352 bytes、
  结束增长 162,762,752 bytes 而 fail closed，超过结束值 64 MiB 预算。
  该次失败没有 `matrix.json`，不得计为 acceptance；设置、4 个普通槽、Mods、
  Replay Store、automation workspace 与产品进程均已恢复。对 213,217,114-byte
  timeline 输出和 Broker 实现的复核表明，5,000 条 timeline 与 2,048 条完整
  响应此前都只有条目数上限、没有字节上限，保留规模与增长量一致。修复必须
  采用上述双重字节预算，并先落盘内存采样，再从零重跑。
- `artifacts/automation/t15-state-memory-fix-diagnostic-20260731T063041326Z/`
  已在修复后以真实游戏完成 10,000 reads、100 reconnects 和 302.389 秒
  duration watch：1,130 个页面、867 条事件、最大状态年龄 6ms，Companion
  warm-up 后峰值增长 9,105,408 bytes、结束增长 8,200,192 bytes；最后 12 个
  采样（约 55 秒）仅增长 98,304 bytes。最终为
  `verificationEligibility=Eligible`，11,232 条 audit 泄漏扫描为 0，设置、
  4 个普通槽、Mods、Replay Store、automation workspace 与产品进程均恢复。
  该 300 秒诊断证明保留区已进入稳定平台，但不替代从零 3,600 秒正式验收。
- `artifacts/automation/t15-state-soak-formal-20260731T063713629Z/`
  已从零完成正式 acceptance：10,000 reads、100 reconnects、3,602.028 秒
  CLI duration watch、13,556 个合法页面和 10,103 条严格递增事件，最大状态年龄
  5ms。Companion warm-up private bytes 为 212,365,312，峰值增长
  25,985,024 bytes、结束增长 16,969,728 bytes，分别低于 256 MiB/64 MiB
  预算；最终 `verificationEligibility=Eligible`。同次正式驱动完成 Core
  196 passed / 1 skipped、Companion 13 passed、AgentBridge 7 passed；
  9 个 audit 文件共 36,015 条且泄漏扫描为 0。设置、4 个普通槽、Mods、
  Replay Store、automation workspace 与产品进程全部恢复，故本节 PASS。

### 4.4 Control Parity

对 pause/resume/step/run-until/record/replay/save/restore 分别：

1. 通过游戏内入口执行 oracle。
2. 通过 SDK/CLI 执行相同 typed command。
3. 通过 MCP tool 执行相同 typed command。
4. 每条路径各 10 次比较 ledger、cursor、hash、cleanup 和审计记录。

外部控制不得改变 T07 milestone，也不得在 T08/T09 不安全 phase 强行执行。

当前实机回归证据：

- `artifacts/automation/t15-replay-save-grounded-semantic-smoke-20260729T092316274Z/`
  已证明 SDK、CLI、MCP 各 1/1 完成
  `create → restore → Paused(strictSemanticEquivalent=true) → resume →
  Completed`。
- 同一进程内由三条外部渠道触发的四次原始槽冷加载，T09 grounded baseline
  canonical SHA-256 均为
  `1f7a3940c8c290193cfb17d81ce7fb85a8935f2ebe9fccb1c267e6e04cff15e6`。
- 该证据仅是 smoke；仍须完成本节规定的每渠道 10/10 正式矩阵后才满足 PASS。
- `artifacts/automation/t15-full-control-parity-20260729T093154858Z/`
  已完成正式矩阵：Disabled/ReadOnly 权限隔离通过；SDK、CLI、MCP 的
  pause/step/run-until、record/replay、Replay Save 各 10/10。30 次
  Replay Save 均严格恢复、暂停并继续到 Completed，最终
  `verificationEligibility=Eligible`。
- 正式矩阵共生成 14,559 条关联审计记录；二次扫描未发现 credential、token
  或用户绝对路径。退出后设置、4 个用户槽、Mods、Replay Store 与 automation
  workspace 均逐项恢复。

### 4.5 Typed Mutation

- ReadOnly、无 lease、错误 scope、未暂停、过场、回放、验证模式和
  `DebugMutationEnabled=false` 全部拒绝且状态 hash 不变。
- 两个 adapter 各做合法边界、越界、NaN/Infinity、stale tick、stale hash、
  中途故障和断线矩阵；失败后 before hash 必须逐位恢复。
- SDK、CLI、MCP 对同一 mutation 产生相同 canonical command/result。
- 成功 mutation 后 T05/T11 读回值与 after snapshot 一致，审计完整，并立刻
  标记 `NonVerifiableDebugMutation`。
- 从该 session 请求 T07 verdict 或 T16 scored run 必须结构化拒绝；新进程从
  原始专用槽冷启动后验证资格恢复，普通槽和 T09 已有存档不被改写。

当前实机回归证据：

- `artifacts/automation/t15-mutation-smoke-20260729T095243451Z/` 已完成首次
  实机闭环：SDK、CLI、MCP 对 `setHeroPose` 和 `setPlayerResources` 各
  1/1，9 类拒绝均未改变 canonical state hash，SDK 持有 lease 断线后 5 秒内
  可重新取得 lease；独立 `DebugMutationEnabled=false` 冷启动恢复
  `verificationEligibility=Eligible` 并隐藏两个 mutation capability/tool。
- `artifacts/automation/t15-mutation-formal-20260729T095512074Z/` 已完成正式
  10 轮矩阵：SDK、CLI、MCP 对两个 adapter 共 60 次成功 commit，逐次核对
  before/after hash、typed diff、非视觉读回值和
  `NonVerifiableDebugMutation`；9 类拒绝保持状态不变，disconnect lease
  回收与干净冷启动隔离均通过。
- 正式矩阵同时运行 Core 187 passed / 1 skipped、Companion 11 passed
  （含 10,000 reads / 100 reconnects）和 AgentBridge 5 passed。两个实机
  session 共生成 8 个 audit 文件、10,484 条记录；扫描未发现 credential、
  token 或用户绝对路径。退出后设置、4 个用户槽、Mods、Replay Store 与
  automation workspace 均逐项恢复。

### 4.6 Scripted AI Loop

使用无模型、确定性的 scripted agent fixture 完整执行至少 10 次：

1. 读取 movie/state/timeline。
2. 提议一个合法 patch 和一个非法 patch。
3. 合法 patch 进入独立分支并通过 T04；非法 patch 在执行前拒绝。
4. 运行合法候选并返回 T07 对照。
5. 不接受分支时原 movie hash 不变；显式接受后 current branch 精确匹配 proposal hash。

再使用至少一个兼容 MCP client 做人工 smoke test；该 smoke test 不替代 scripted acceptance。

当前实机回归证据：

- `artifacts/automation/t15-ai-smoke-20260729T101817505Z/` 已通过真实
  AgentBridge stdio MCP 完成首次非视觉闭环：非法候选在执行前拒绝，合法候选
  仅写入隔离分支，显式 apply 后才改变 current movie，并在 replay timeline
  观察到唯一 checkpoint；无结构化 desync。
- `artifacts/automation/t15-ai-formal-20260729T102003036Z/` 已完成正式
  10/10 scripted-agent 矩阵及兼容 MCP client smoke。非法候选拒绝、隔离 proposal、
  显式 apply 和 replay milestone 均为 10/10；全程仅使用 MCP 结构化状态、
  movie 和 timeline，`structuredDesyncCount=0`，最终
  `verificationEligibility=Eligible` 且 lease 已释放。
- 正式 AI 矩阵同时完成 Core 187 passed / 1 skipped、Companion 11 passed、
  AgentBridge 7 passed；7 个 audit 文件共 10,458 条记录，扫描未发现
  credential、token 或用户绝对路径。退出后设置、4 个用户槽、Mods、Replay
  Store 与 automation workspace 均逐项恢复。

### 4.7 Security Matrix

- 尝试任意路径、shell、进程、DLL、URL、反射、地址、原始存档和 native enable，全部不存在对应 capability 或被 schema 拒绝。
- fuzz resource URI，不能目录遍历或读取 automation workspace 外文件。
- token/credential 不出现在 stdout、events、audit、crash report。
- 结束 AgentBridge/Companion/游戏时无控制锁、输入 held、暂停 lease 或孤儿进程。

当前实机回归证据：

- `artifacts/automation/t15-security-smoke-20260729T101913946Z/` 已证明
  8 个危险 URI、8 个未登记高危 tool 和 4 类越界 schema 输入全部 fail
  closed；超过 1 MiB 的输入行返回 `-32700` 后，同一 AgentBridge 可继续
  响应合法 ping。攻击前后 grounded semantic SHA-256 均为
  `1f7a3940c8c290193cfb17d81ce7fb85a8935f2ebe9fccb1c267e6e04cff15e6`。
- `artifacts/automation/t15-security-formal-20260729T102147505Z/` 已在完整离线
  测试后重跑正式实机安全矩阵：8/8 URI 与 8/8 tool 以 `-32602` 拒绝，4/4
  invalid arguments 返回 `InvalidToolInput`，超长行拒绝及后续恢复均通过。
  AgentBridge 持有 lease 时被终止后，替代 client 在 5 秒内取得并释放 lease；
  最终为 Running/Idle、recording inactive、`verificationEligibility=Eligible`。
- 正式安全矩阵完成 Core 187 passed / 1 skipped、Companion 11 passed、
  AgentBridge 7 passed；7 个 audit 文件共 10,266 条记录且泄漏扫描为 0。
  独立复核确认设置与 4 个用户槽 SHA-256 恢复、无产品孤儿进程或
  `Mods.HKTAS-T15-*` 恢复目录。

### PASS

- `Disabled/ReadOnly/ApprovedControl` 三种模式严格符合权限矩阵。
- 状态接口覆盖 T05/T07/T10/T11 与 T09/T14 状态，并标明 tick/freshness/availability。
- SDK/CLI/MCP 三条路径使用同一 schema 和 Companion authorization。
- 控制 parity 每项 10/10 与游戏内 oracle 一致。
- scripted AI loop 10/10，movie 分支不会静默覆盖。
- typed mutation 的权限、前置、原子回滚、读回、非验证标记和冷启动隔离矩阵全部通过。
- auth/lease/idempotency/backpressure/security matrix 全通过。
- 所有外部动作有可关联审计记录且不泄露 credential/个人信息。

### FAIL / Downgrade

- 外部客户端能直连 Runtime pipe 或调用 Unity API。
- AI/MCP 获得普通客户端没有的隐藏权限。
- session ID/lease ID 被当作唯一认证。
- 任意字符串被当作脚本、路径、反射成员或进程参数。
- ReadOnly 能引起 Runtime/movie/T09 状态改变。
- 未经 `DebugMutationEnabled`、safe point、compare-and-set 或 writer adapter 就能修改状态。
- mutation session 能生成 T07/T16 通过结论，或能直接修改 Boss/完成标志。
- write lease 断线后不释放，或两个客户端同时控制。
- movie proposal 覆盖原文件或改变 expected hash。
- 外部控制改变 T07 hash、绕过安全 tick 或缺少审计。

失败时默认降为 `ReadOnly`；若认证或数据边界失败则全局 `Disabled`。Runtime/T09/T12 核心功能必须保持可用。

## 5. Implementation Checklist

- [x] 写 Automation/MCP/AI workflow 协议与 schemas。
- [x] 实现 Core automation DTO、canonicalization 和 capability model。
- [x] 实现 Companion automation broker、当前用户 pipe、认证与 scope。
- [x] 实现 control lease、前置条件、幂等表和断线 cleanup。
- [x] 实现状态资源、timeline 分页和 backpressure。
- [x] 实现 movie proposal/branch/validate/apply workspace。
- [x] 实现两个 typed mutation adapters、transaction rollback、读回与非验证标记。
- [x] 实现审计 sink 和 credential redaction。
- [x] 实现 .NET Automation Client 与 CLI 命令。
- [x] 实现 stdio AgentBridge、MCP resources/tools 和 stdout/stderr 隔离。
- [x] 完成 protocol/fuzz/auth/lease/backpressure tests。
- [x] 完成 SDK/CLI/MCP 控制 parity。
- [x] 完成 scripted AI loop 与兼容 MCP client smoke。
- [x] 完成 typed mutation SDK/CLI/MCP parity、失败回滚与干净冷启动隔离矩阵。
- [x] 完成 security matrix、故障回退与 verdict。

## 6. Rollback

- `ExternalAutomationMode=Disabled` 时 Companion 不建立 automation endpoint，AgentBridge 连接失败但 Runtime/T12 正常。
- 卸载 AgentBridge 只失去 MCP 接口；Studio、CLI 文件操作和 Runtime 不受影响。
- 撤销 write lease 立即停止后续控制命令，已接受命令按其原子/取消语义完成并留证据。
- movie 分支可按 content hash 丢弃；原 movie、T09 save 和 oracle 证据不自动删除。
- automation fault 时：拒绝新命令 → 撤销 lease → T12 安全停止/释放输入和时间 → flush audit。

## 7. External Protocol Evidence

- MCP Server primitives（prompts/resources/tools）：<https://modelcontextprotocol.io/specification/2025-11-25/server/index>
- MCP transports（stdio、Streamable HTTP 与本地 HTTP 安全要求）：<https://modelcontextprotocol.io/specification/2025-11-25/basic/transports>
- MCP tools 与 input/output schema：<https://modelcontextprotocol.io/specification/2025-11-25/server/tools>
- MCP Security Best Practices：<https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices>

这些来源支持“本地 v1 优先 stdio、工具需要明确 schema、HTTP 需要额外认证/Origin 防护、session ID 不能代替认证、应使用最小权限与用户同意”等协议边界；它们不证明本项目的 Runtime 控制已经安全。最终支持声明只由本 Spec 的认证、租约、parity、AI loop 和 security matrix 决定。
