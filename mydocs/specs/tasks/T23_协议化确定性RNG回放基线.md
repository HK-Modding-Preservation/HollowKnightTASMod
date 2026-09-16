# Task Spec T23: 协议化确定性 RNG 回放基线

- **Status**: REOPENED_BY_T24
- **Gate**: G6 Product Completion / T16 blocker
- **Depends On**: T04, T06, T10, T15, T19
- **Produces**: manifest 绑定的 Unity RNG seed、完整回放起点重置、结构化审计与冷启动证据

## 0. Trigger Evidence

T16 的实时结构化控制 authoring 已在调谐假骑士上于 2422 个战斗 input tick
完成击杀，但同一完整 input movie 的首次冷启动回放失败。失败回放自身满足
`replayMismatchCount=0`、`physicalNoise=false`、`lastPlaybackFault=""`，因此不是
输入注入错误。

2026-08-02 新增 T24 原版执行语义等价硬门禁：本任务当前的 Mod 内
`UnityEngine.Random.InitState(...)` 方案会直接改写游戏 RNG 状态，不能进入
正常人工 TAS、AI TAS 或 T16 scored run。该实现仅保留为非验证实验，后续必须
改为让无 TAS 基准与 TAS 路径获得同一外部环境/随机来源，或证明无需控制 RNG；
在此之前本任务不得判 PASS。

两次 `rng-ledger.jsonl` 在 `movieTick=0` 已给出不同的
`UnityEngine.Random.State` SHA-256。T10 明确只实现诊断，并在 Future Boundary
要求任何 RNG playback 另写 Spec；本任务据此成为 T16 的新增前置。

## 1. Goal

为完整 canonical movie 提供一个轻量、显式、可审计的 Unity RNG 起点，使
authoring 与冷启动 replay 在相同 manifest 下从同一随机状态开始。该能力只
控制 Unity 官方 `Random.InitState(int)` API，不写任意内存、不强制 Boss 行为、
不回放预先篡改的 HP/FSM/PlayerData，也不伪装成 T10 尚未覆盖的全部随机源。

## 2. Contract

### 2.1 Configuration and Binding

- 新增 `ReplayDeterministicRngEnabled` 与 `ReplayDeterministicRngSeed` 设置。
- 两个字段进入 `TasGlobalSettings.ComputeCanonicalSha256()`，从而经
  `tasSettingsSha256` 进入 session manifest；movie 的 `manifest-sha256` 会拒绝
  不同 enabled/seed 的环境。
- 默认 profile ID 为 `unity-init-state-at-replay-and-scene-boundaries-v1`。
- 仅在 T10 的目标构建 codec resolution 为 `Ready` 时启用；build、MVID 或
  `Random.State s0..s3` layout 不匹配时，完整 replay fail closed。

### 2.2 Reset Boundary

- 在一个 top-level `startReplay` 已通过 manifest/baseline/scene 前置校验、但尚未
  消费第 0 个 movie sample 时调用一次 `UnityEngine.Random.InitState(rootSeed)`。
- 每次允许跨场景的 top-level replay 在 `BeforeSceneLoad` 以
  `(rootSeed, targetScene, nextSceneEpoch)` 的稳定 FNV-1a 派生 seed 重置一次，覆盖
  scene 初始化；在加载门禁即将放行首个可玩 movie sample 时用同一派生 seed 再
  重置一次，消除不同加载墙钟时长造成的全局 RNG 消耗漂移。
- `queueInputBatch`/单帧输入/多帧输入是在同一 authoring 时间线上追加输入，不得
  每批重置 RNG。
- T09 restore 与 movie seek 的短尾 replay 不得擅自重置 RNG；它们继续遵守自身
  baseline/恢复契约。本任务不把 seed 重置冒充完整 savestate。
- stop、lease 过期或 Companion 断线不再次写 RNG，也不得留下 held input、异常
  timeScale 或孤儿进程。

### 2.3 Audit and External State

Runtime status、SDK/CLI/MCP 共享语义状态至少公开：

- `replayDeterministicRngEnabled`
- `replayDeterministicRngSeed`
- `replayDeterministicRngProfile`
- `replayDeterministicRngStatus`
- `replayDeterministicRngResetCount`
- `lastReplayRngBeforeSha256`
- `lastReplayRngStateSha256`
- `lastReplayRngAppliedSeed`、`lastReplayRngBoundary`、`lastReplayRngScene`

每次成功重置都发出带 root/applied seed、boundary、scene、profile、before/after
SHA 和 reset count 的结构化
Runtime 事件。该事件属于确定性 replay 生命周期，不得设置
`NonVerifiableDebugMutation`。

## 3. Verification

1. 离线测试证明 seed/enabled 会改变 canonical settings hash，clone/normalize
   保留字段，场景派生 seed 跨进程稳定，禁用时不重置，input batch 不重置。
2. 目标构建实机对同一 seed 连续冷启动，首个 replay movie sample 的 RNG state
   SHA 一致；改变 seed 必须在第一个采样点产生结构化 RNG 差异。
3. 在启用 profile 后重新执行 T16 实时闭环 authoring，并冻结由 journal 返回的
   完整 movie；不得拼接已包含历史的录制件。
4. 同一冻结 movie 连续 5 个独立进程均从椅子真实起步、进入 Attuned 假骑士并
   击杀；同时满足 input mismatch 0、physical noise false、provider failure 0。
5. 每次运行后所有 `user1..4` 相关文件、Mods、设置、automation/replay store 与
   进程状态精确恢复。

## 4. PASS

- seed 由设置/manifest/movie 三者闭合绑定，错误 seed 不能静默运行。
- 每个 top-level replay 在起点重置一次，并在每个实际场景切换的 transition-start
  与 gameplay-ready 各重置一次；authoring input batch 和 restore 短尾不重置。
- T10 codec 不支持时 fail closed，不退化为未审计随机写入。
- T16 冻结 movie 达成 5/5 冷启动击杀，且每次 RNG 起点事实一致。
- 报告继续把 `System.Random`、其他 Mod RNG 和未覆盖 Unity 调用标为未覆盖；
  只用实际 5-run 结果证明本固定场景可复现。

## 5. FAIL

- 仅因为输入 ledger 匹配就宣称游戏状态可复现。
- 每个 adaptive input batch 都重新 seed，改变实时控制过程的自然 RNG 序列。
- seed 不进入 manifest 设置 hash，或换 seed 后旧 movie 仍可启动。
- 通过改 Boss HP、FSM、完成标记或失败后状态纠偏来获得 PASS。
- 单次成功、只有截图、未恢复存档，或把 partial coverage 写成完整 RNG 控制。

## 6. Rollback

关闭 `ReplayDeterministicRngEnabled` 即回到 T10 的只读诊断行为；实现不得改变
HK-TAS Movie v1 文本语法。移除本 profile 后 T16 必须重新判为未完成，不能继续
使用旧的 5-run 结论。
