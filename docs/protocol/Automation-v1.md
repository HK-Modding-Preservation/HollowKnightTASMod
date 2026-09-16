# Automation v1

Automation v1 是 HollowKnightTAS 面向调试器、脚本和 AI 的本机语义接口。它只提供非视觉状态 DTO 和编译期登记的 typed command，不提供 Unity 对象、反射、内存地址、脚本、任意路径、进程或网络访问。

## 信任边界

```text
Hollow Knight Runtime
  └─ T12 authenticated internal pipe
      └─ Companion automation broker
          ├─ current-user named pipe -> .NET SDK / CLI
          └─ current-user named pipe -> AgentBridge -> MCP stdio
```

- Runtime pipe 从不作为公开 API。
- Companion 是唯一 broker。
- automation pipe 使用 `PipeOptions.CurrentUserOnly`，bootstrap 文件使用仅当前 Windows SID 的受保护 ACL，再以 256-bit token 和一次性 client nonce 认证。
- session、manifest、client 与严格递增 sequence 全部绑定；nonce 重放、unknown field、unknown command/scope 和非 canonical payload 均 fail closed。
- v1 没有监听端口。

## 模式

| 模式 | 行为 |
|---|---|
| `Disabled` | 不创建 pipe，不保留 bootstrap |
| `ReadOnly` | 默认；只读状态与离线 movie validate/propose |
| `ApprovedControl` | 用户设置启用；写命令还必须持有短期独占 lease |

`ExternalAutomationMode` 非法值归一化为 `ReadOnly`。`DebugMutationEnabled` 是独立开关，不能由客户端、movie、MCP 或 NativeHost 修改。

## 传输与 canonicalization

外层复用 T12 的长度前缀 canonical JSON envelope，协议版本为 `1`。payload 必须是最多 128 项的扁平 string map；复杂参数通过已登记字段或 canonical bytes 的 base64 表达。单帧最大 1 MiB。

握手顺序：

1. client 读取 `%LOCALAPPDATA%\HollowKnightTAS\automation\automation-v1.json`。
2. client 发送 sequence 0 `automationHello`，绑定 client/session/manifest、32-byte nonce 和 token。
3. broker 拒绝重复 nonce，返回 sequence 0 `automationHelloAck`。
4. command 从 sequence 1 严格递增；响应复用请求 sequence。

## 请求

`AutomationCommandEnvelope` 固定包含：

```text
schemaVersion, requestId, idempotencyKey, clientId,
sessionId, manifestSha256, commandId, requiredScope,
leaseId, expectedRuntimeMode, expectedMovieTick,
argumentsBase64
```

同一 client 的同一 idempotency key：

- 请求 hash 相同：返回第一次的精确结果，不重复副作用；
- 请求 hash 不同：`IdempotencyConflict`。

每个命令的参数集合是 closed schema。任何多余字段都会得到 `InvalidArguments`。

## 状态资源

```text
hktas://session/current/status
hktas://session/current/manifest
hktas://session/current/capabilities
hktas://session/current/state/summary
hktas://session/current/timeline
hktas://session/current/desync/latest
hktas://session/current/replay-saves
hktas://session/current/movie
hktas://session/current/restore-strategy
```

`state/summary` 每次向 Runtime 请求新的 T05 snapshot，返回 capture UTC、phase、movie tick、semantic hash、control/playback mode 和 `verificationEligibility`。timeline 最多保留 5000 项、单次最多返回 200 项，并显式返回 `gapBeforeWindow`。broker 自动订阅 T11 watch 与 ledger，因此 FSM/watch/RNG 观察项通过同一非视觉 timeline 进入外部接口。

## Lease

- 同一 Runtime 最多一个 write lease。
- 默认 30 秒，最大 300 秒。
- scope 无 wildcard。
- 断线、超时、session/manifest 变化、Companion fault 或用户撤销立即回收。
- lease 不是认证凭据；每条请求仍做认证、binding、scope、mode/tick 前置和 schema 校验。

## Movie 分支

`proposeMoviePatch` 要求 base movie ID 与当前 movie 精确相等；无当前 movie 时使用 `none`。Companion 先执行 T04 parse/validate/canonicalize，再按候选 movie hash 写入独立 content-addressed branch。只有持有 `movie.apply-branch` lease 的显式 `applyMovieBranch` 才会把该分支上传 Runtime。提议和校验永不覆盖当前 movie。

## Replay Save 恢复生命周期

`createReplaySave`、`restoreReplaySave`、
`approveReplaySaveOverwrite`、`cancelReplaySaveRestore` 和
`resumeReplaySaveRestore` 共用 `control.replay-save` 独占租约。恢复采用显式状态机：

1. `restoreReplaySave` 只启动恢复，不隐式批准专用 TAS 槽覆盖。
2. `replaySaveRestorePhase=AwaitingOverwriteApproval` 时，必须用
   `approveReplaySaveOverwrite { approved: true|false }` 作明确决定。
3. `replaySaveRestorePhase=Paused` 表示目标语义 hash 已校验且停在下一 movie
   tick；只能用 `resumeReplaySaveRestore` 完成恢复。
4. 验证目标前可用 `cancelReplaySaveRestore` 取消；普通 `resume` 不操作恢复协调器。

`state/summary.fields` 公开 active、phase、status、current/target/next tick、进度、
覆盖批准需求、语义/验证 hash 与恢复等价性，均来自 Runtime 结构化状态，不依赖
截图、OCR 或像素识别。

## Typed mutation

v1 只有：

- `setHeroPose`：position x/y 与 Rigidbody/current velocity x/y；
- `setPlayerResources`：health 与 soul。

前置条件固定为 `ApprovedControl + DebugMutationEnabled + 相应 scope lease + Paused + Idle playback + 无过场/死亡/restore + 非 verification mode + expected tick/hash`。position 绝对值不超过 10000、单次位移不超过 20、速度绝对值不超过 100；所有浮点必须 finite。health/soul 不得超出当前合法上限。

adapter 在 Unity 主线程读取 before、应用、重新采样 after；任何中途失败都回滚并要求 before hash 逐位恢复。首次成功后当前进程永久变为 `NonVerifiableDebugMutation`，T07/T16 evidence 创建与完成都会拒绝；只有无 mutation 的新进程可恢复资格。

## 审计与数据边界

每次调用写入 JSONL 审计，包括 correlation、hashed client/idempotency/lease、session、manifest、scope、tick、result、side effect 和 response hash。token、用户名、自然语言 prompt、绝对路径、原始存档和 credential 不进入响应、event 或 audit。

对应 schemas 位于 `schemas/automation-*-v1.schema.json` 与 `schemas/movie-patch-v1.schema.json`。
