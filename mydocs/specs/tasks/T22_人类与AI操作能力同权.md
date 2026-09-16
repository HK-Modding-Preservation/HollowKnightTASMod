# Task Spec T22: 人类与 AI 操作能力同权

- **Status**: IN_PROGRESS
- **Gate**: G6 Product Completion
- **Depends On**: T12, T15, T21
- **Produces**: 单一控制服务、UI/API 能力矩阵、SDK/CLI/MCP 等价入口

## 1. Goal

普通用户在 Mod/Companion UI 中拥有的 TAS 操作，外部 AI 必须通过版本化非视觉接口
完整获得；外部 AI 获得的非调试 TAS 操作，也必须能由人类 UI 调用。二者使用同一
业务服务、状态机、校验、lease、审计和错误码，不维护两套行为近似但不一致的实现。

## 2. Required Parity Matrix

至少覆盖：

| 能力 | Human UI | SDK | CLI | MCP |
|---|---|---|---|---|
| 读取结构化 Hero/Boss/scene/tick | 必须 | 必须 | 必须 | 必须 |
| 设置下一帧输入并步进 | 必须 | 必须 | 必须 | 必须 |
| 设置/执行多帧输入 | 必须 | 必须 | 必须 | 必须 |
| 暂停、继续、step、run-until | 必须 | 必须 | 必须 | 必须 |
| 创建、列出、选择、恢复存档 | 必须 | 必须 | 必须 | 必须 |
| 配置自动存档开关、帧间隔、自动保留数量 | 必须 | 必须 | 必须 | 必须 |
| seek 到过去/未来 movie tick | 必须 | 必须 | 必须 | 必须 |
| replace/insert/delete 过去输入 | 必须 | 必须 | 必须 | 必须 |
| branch apply、undo/redo、校验、导出 | 必须 | 必须 | 必须 | 必须 |

仅限调试且会失去验证资格的 typed mutation 可以不放在普通 UI 主流程，但 UI 和 API
都必须清楚显示它的权限、风险和 `NonVerifiableDebugMutation` 状态。

## 3. Architecture Contract

- 抽取 `TasAuthoringService`（或等价接口）作为唯一业务入口；
- Companion UI command、Automation broker、CLI 和 MCP adapter 只做参数绑定与展示；
- 任何 surface 不得直接操作 Runtime 私有字段、movie 文件或 save store；
- 能力目录由同一注册表生成，包含 availability、preconditions、side effects、
  checkpoint kind 与最大 payload；
- UI 禁用状态必须来自 capability/precondition，而不是复制判断；
- 每个 surface 返回同一 command/result schema、错误码和 correlation ID。

### 3.1 当前实现落点（2026-08-01 反向同步）

- `getCombatState` 必须按请求在 Runtime 主线程边界重新采样，不能返回最后一次订阅缓存冒充当前状态。暂停时不推进帧；返回 `movieTick`、`sequence`、`capturedAtUtc`、`ageMilliseconds` 及逐字段 freshness/provider failures。提供者失败不夹带该提供者的旧缓存，过期响应拒绝。UI 的刷新和 SDK/CLI/MCP 共用此路径；普通 watch 订阅仍可按频率采样。
- 自动存档公共命令 `setAutoSavePolicy`，scope=`control.replay-save`，需要 lease、当前 mode 校验，提供 tick 时额外校验；参数 `enabled`（true/false）、`intervalMovieTicks`（1–1000000000）、`retentionCount`（1–1000）。SDK `SetAutoSavePolicyAsync`、MCP `hktas_set_auto_save_policy` 与 Studio Replay Saves 配置区共用 Broker。立即应用并写回 Mod 原有全局设置对象，正常退出保存；已单次实机确认下次启动读回。不保证异常强杀前未保存的设置。策略不参与执行指纹，不放宽时钟/输入/游戏逻辑的身份校验。当前实际策略通过 Runtime 状态的 autoSaveEnabled/autoSaveIntervalMovieTicks/autoSaveRetentionCount 读取，UI 有显式“读取当前策略”。主菜单使用关联请求的控制状态预检，不要求 Hero 存在，仍校验租约和 mode/tick；读取通过 `getState statusOnly=true`，默认完整游戏快照契约不变。

- `AutomationBroker.ExecuteHumanAsync(...)` 是 UI 的进程内薄适配入口；它构造与外部
  SDK/CLI/MCP 相同的 `AutomationCommandEnvelope`，再调用同一个 `ExecuteAsync(...)`。
- UI 写操作使用固定主体 `companion-ui` 的短期独占 lease；若 AI 已持有冲突 lease，
  UI 返回同一 `LeaseBusy`，不得越权抢占或绕过。
- `MainViewModel` 中 pause/resume/step、单帧输入、多帧输入、record、run-until、
  replay save、typed timeline edit、branch apply 与 seek 均经 `AutomationBroker`；
  Runtime 原始 IPC 仅保留订阅、展示与 capability 刷新等会话管道职责。
- 超长多帧输入使用 `beginInputBatch` / `appendInputBatch` / `commitInputBatch` /
  `cancelInputBatch` 事务。事务绑定 client、connection、lease、session、manifest、
  起始 movie tick 与 scene epoch；按零基连续 chunk 提交，展开上限为 10,000,000 tick。
- 历史修改由 content-addressed branch 表示；`seekMovieTick` 使用最近的精确前缀兼容
  replay save 加确定性 tail 重放，`applyBranchAndSeek` 负责原子应用分支后启动该流程。
- 人类 UI 的 `Upload` 也必须先 propose content-addressed branch 再 apply，禁止直接绕过
  Broker 向 Runtime 发送 `UploadMovie*`。
- `stopRecording` 返回的 `MovieDocument` 是新的完整 journal movie；Broker 必须在向调用方
  返回成功前原子更新 `currentMovieId/currentMovieBytes`。否则随后 replace/insert/delete 会
  错把最后一个短 input batch 当作父 movie，属于同权链路中的状态分叉。

## 4. Verification

- 对矩阵中每个操作跑 contract test：UI command adapter 与 API adapter 输入同一请求，
  产生相同 service call、结果码和状态变化；
- 任何新增非调试 TAS command 若缺少四个 surface 中任一个适配器，构建/测试失败；
- 使用 SDK 完成一段、CLI 完成一段、MCP 完成一段、UI 自动化完成一段，合并后的
  canonical input ledger 等价；
- ReadOnly/Disabled/lease expiry 在所有 surface 上表现一致；
- 用户文档从能力注册表或测试过的示例生成/校验，不能记录不存在的快捷键或命令。

### 4.1 实现清单

- [x] 单帧输入、step、多帧输入、pause/resume/run-until 四 surface 入口。
- [x] 创建、列出、选择、恢复 replay save 的 UI/SDK/CLI/MCP 入口。
- [x] replace/insert/delete、branch apply、seek、apply+seek 的 UI/SDK/CLI/MCP 入口。
- [x] start/stop recording 与读取当前 canonical movie 的 UI/SDK/CLI/MCP 入口。
- [x] MCP 分块输入事务与 10,000,000 tick 上限。
- [x] UI/AI 共用 lease、CAS、审计、错误码的集成测试。
- [x] 真游戏验证 replay save → 修改过去输入 → apply+seek → 确定性 tail 重放。
- [x] UI、SDK、CLI、MCP 各执行一段并核对合并后的 canonical input ledger。
- [ ] 普通用户与外部 AI 文档同步并通过示例校验。

真游戏验证由 `scripts/Invoke-T16FinalTas.ps1` 的 `Adaptive` 阶段执行，新增
`Invoke-AuthoringParityProbe(...)`（或将现有 `Invoke-AdaptiveProbe(...)` 扩展为等价
职责）：在同一隔离存档试验中依次调用 start recording、create/list save、
queue multi-frame input、step with input、plain step、stop recording、typed past edit、
apply branch + seek、explicit restore/resume，并把每条 command/result、movie tick、
scene epoch、save ID、branch ID 和 terminal progress 写入 `authoring-parity.json`。

Running/Idle 下的 `startRecording` 与 `createReplaySave` 只绑定 expected mode，不绑定
易变化的 expected movie tick；二者不直接改变输入，且 T15 的既有实测入口采用同一
契约。进入 Paused 后的 input/step/edit/apply/seek 仍必须使用严格 movie tick + scene
epoch CAS。首轮真游戏探针以 `PreconditionFailed` 证明了在 Running 下复用旧 tick 会
产生竞态，故不得通过重试陈旧 tick 或放宽 Paused 写操作 CAS 来规避。

`movieSeekProgress` 通过 timeline 分页读取时固定使用最多 5 条/页：单个 watch frame
包含完整 typed combat/statue JSON，100 条页会使外层 `entries` 超过 automation-v1
900,000 字符字段上限。分页游标必须持续推进，并以原始 seek requestId 过滤终态。

### 3.2 真游戏发现的 replay baseline/journal 一致性修复

第三轮真游戏验证在 `applyBranchAndSeek` 内部 restore 阶段得到确定性失败：baseline
语义期望 `hero.position.x=55.5925827`，从 raw slot bundle 重载后实际为 `11.18`。同期
recorded movie 从 `dreamnail` 前才开始，缺少从椅子离开的前缀，并把 scene transition
期间 replay 明确跳过的 raw input updates 记录成额外 neutral frames。根因是：

1. journal baseline 只允许 `hero.acceptingInput + idle + grounded`，导致坐椅启动时一直
   不建 baseline，直到玩家已走到雕像旁才把“当前语义”错误配给“原始坐椅存档 bundle”；
2. unified replay 时 journal 和 replayer 各自订阅 `InputManager.OnUpdate`，journal 按 raw
   update 记录，而 replayer 按 gameplay gate 只消费授权 movie tick，二者时间轴分叉。

修复契约：

- `RuntimeReplayJournal.IsSupportedBaselineAnchor()` 接受稳定的原版坐椅状态
  (`PlayerData.atBench=true`、Hero 不接收输入、无场景切换、刚体静止)，以便 raw slot
  bundle 与 baseline semantic snapshot 对齐；仍要求连续 3 帧 canonical hash 稳定。
- unified `RuntimeControlService.StartReplay()` 在 journal 可用时调用
  `BeginPlaybackCapture()`；此后 raw journal handler 不再重复记录。
- `RuntimeReplayRestoreCoordinator` 的 prefix replay 也必须加入同一 capture 协议；恢复后
  journal 应当重新表示“刚载入的原始 baseline 到已验证目标 tick”的已消费前缀，从而
  允许继续编辑、再存档、再回退，而不是只保证第一次 restore 可用。
- 恢复坐椅 baseline 时不得调用 `HeroController.RegainControl()`。协调器应保留
  `PlayerData.atBench=true && !hero.acceptingInput`，等待 journal 完成三帧稳定基线后启动
  replay，由原版 bench FSM 消费首个输入并完成起身；只有非坐椅的异常失控状态才使用
  既有 control reacquire 路径。
- 同一 raw slot 冷载入的坐椅 Transform/Rigidbody 存在原版低频初值分支；这不能通过
  量化目标哈希、四舍五入坐标或写回 Hero/Transform/Rigidbody 来“修复”。T24 已覆盖
  原版冷启动目录，并要求权威 Rigidbody position/velocity 逐位匹配某个合法无 Mod
  baseline；任何只在候选/恢复路径出现的坐标仍 fail closed。
- 2026-08-11 起明确废止旧的“把坐椅 Hero 同步到 0.001 锚点”方案。普通记录、回放、
  保存和恢复路径不得调用 Hero control/pose/physics writer；restore coordinator 只能加载
  冻结的原版存档 baseline，再由原版输入管线重放 canonical 前缀。若磁盘 baseline 的
  原版非确定性使目标无法严格重现，本次恢复必须报告 desync；后续只能另立完整进程快照
  或等价外部快照 Spec，并证明恢复后的下一完整 tick 与无 Mod 基准一致，不能回填字段。
- 每个非 release-boundary 的 `ReplayInputObservation`（仅 replayer 真正消费的 movie
  input）用其 expected typed input 和 raw input tick 追加到 journal；replayer 为恢复绑定而
  自动注入的最终 neutral release 不属于 movie，不得推进 authoritative movie tick。停止、
  失败、dispose 时必须调用
  `EndPlaybackCapture()` 并重置 raw-tick 连续性。
- scene transition/warmup 被 replayer gate 跳过的 raw update 不得进入 journal；这样
  save package movie 与实际 consumed replay timeline 同构，而不是把 transition 等待
  时间重复编码成输入帧。
- 添加日志覆盖 baseline anchor kind、playback-capture begin/end、append 失败；任一
  append gap 使 replay save 明确不可用，不得静默生成不可恢复存档。
- journal 的 playback capture 状态由 `RuntimeReplayJournal` 全局持有，避免 control replay
  与 restore replay 两个 owner 互相误报或误结束；`getState` 同时公开
  `journalPlaybackCaptureActive` 与
  `journalPlaybackCaptureError`；人类 UI 与外部 AI 通过同一状态面判断当前 unified replay
  是否仍在按已消费帧写入可恢复 journal，不允许仅靠日志文本猜测。

### 4.2 验证证据（2026-08-01）

- 真游戏隔离试验：
  `artifacts/final-tas/t16-adaptive-20260801T053459857Z-87267bf4/`，最终
  `DISCOVERY_PASS`，其中 `authoring/discovery/authoring-parity.json` 为
  `AUTHORING_PARITY_PASS`、`visualRecognitionUsed=false`、共享路径为
  `AutomationBroker.ExecuteAsync`。
- 非空历史修改：父 movie
  `ef1b3d67b331c8d9f8ae95b560a7045761a2b1e26ffe32b5885e879c1efe3469` 的
  tick 756 原为 neutral；`replaceInputRange` 写入一帧 `left` 后得到不同的 branch
  `e7dcc35b90e346a0632f5ab73b9bf0555638bbaebefd694c90bb54b906c021ff`。
  `authoring-recorded.hktas` 与 `authoring-edited.hktas` 保留了可直接 diff 的 canonical
  证据。
- branch apply + seek：从 replay save tick 755 执行确定性短尾，在 tick 777 以
  `phase=Completed`、`terminal=true` 精确结束；之后再次显式 restore，在 resume 前记录
  `restorePausedMovieTick=755`、`restoreTargetMovieTick=755`、
  `replaySaveRestoreStrictSemanticEquivalent=true`，resume 后 cursor 为 756。
- 同一 session 的 `automation-v1.jsonl` 共 1225 条审计记录，覆盖
  `stepWithInput`、分块 multi-frame transaction、`step`、`runUntil`、record、replay save、
  `replaceInputRange`、`applyBranchAndSeek`、restore/resume，transport 为当前用户 named
  pipe；未使用视觉识别或调试 mutation。
- `AutomationPipeIntegrationTests.UiSdkCliAndMcpAppendCanonicalInputThroughOneRuntimePath`
  真实启动 SDK client、CLI 进程和 stdio MCP AgentBridge，并调用 UI 的进程内 human
  adapter；四者分别提交 right/left/jump/attack 单帧，fake Runtime 仅在统一
  `RunInputBatch` IPC 入口记录 canonical movie，最终合并输入序列逐项相等。
- 本轮签名 Release：Companion manifest
  `104c50e6a43d9f6cb23be50cd51fe6e2be2725632cee300376b129f57daf4814`，安装包
  `ae25971b787c17c45b79ca6db673e22cecbac0aac2fa9a2b1853c51cad328d3c`；试验退出后设置、
  `.bak`、user1-user4、Mods、replay store、automation workspace 与进程均恢复基线。

## 5. PASS

普通用户和 AI 对所有非调试 TAS authoring 能力同权，差异只在交互呈现，不在功能。
历史输入编辑的验收必须产生与 parent 不同的 content-addressed branch ID；把原帧替换成
相同输入的 no-op 不得计为“已证明可修改过去”。恢复验收必须在 resume 前记录 Paused
状态的 target tick 与 `replaySaveRestoreStrictSemanticEquivalent=true`。

## 6. FAIL

- AI 只能读取或整段回放，不能做 UI 可做的逐帧编辑；
- UI 不能访问 AI 可用的 branch/seek/save 功能；
- 任一 surface 使用独立实现导致 tick、权限或错误行为不同。

## 7. Rollback

各 surface 是统一服务的薄适配器；单个适配器故障可禁用该 surface，但 T22 验收失败，
不得降级宣称同权。Runtime input/save/movie 数据保持不变。
