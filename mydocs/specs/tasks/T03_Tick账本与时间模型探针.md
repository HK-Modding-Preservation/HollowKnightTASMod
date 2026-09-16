# Task Spec T03: Tick 账本与时间模型探针

- **Status**: VERIFIED
- **Gate**: G1 Time Model
- **Depends On**: T01
- **Produces**: 可解释的 input/visual/fixed ledger、负载实验矩阵、正式 tick 术语

## 0. Open Questions

- None。正式矩阵已将一次已提交的 InControl update tick 锁定为 movie input tick。

## 1. Requirements

### Goal

观测并定义 Hollow Knight 目标构建中 InControl 更新、Unity Update/FixedUpdate/LateUpdate、Hero update、场景事件与 T-FT 的关系，形成逐条可核对的账本。

### In-Scope

- 单调计数：`inputTick`、`visualTick`、`fixedTick`、`sceneEpoch`。
- Unity 时间字段的 exact-bit 与人类可读值。
- 正常 60 FPS、限制 30 FPS、高负载三种 profile。
- 场景进入、退出、死亡/重生边界。

### Out-of-Scope

- 暂停、单步、快进。
- 假定一个 visual tick 恰好一个 fixed tick。
- 用 `DateTime` 或墙钟作为 simulation 顺序。

## 1.5 Code Map

```text
src/HollowKnightTAS.Core/Ledger/
  TickPhase.cs
  TickStamp.cs
  TickLedgerRecord.cs
  TickLedgerRecordJson.cs
  TickLedgerJsonlSink.cs
  TickLedgerValidator.cs
src/HollowKnightTAS.Runtime/Timing/
  RuntimeClockProbe.cs
  InControlClockProbe.cs
  SceneEpochTracker.cs
tests/HollowKnightTAS.Core.Tests/Ledger/
  TickLedgerValidatorTests.cs
fixtures/ledger/
  valid-zero-fixed.jsonl
  valid-multi-fixed.jsonl
  invalid-nonmonotonic.jsonl
scripts/
  Invoke-T03TimingMatrix.ps1
  Summarize-T03Timing.ps1
artifacts/timing/<sessionId>-<runId>/
  ledger.jsonl
  profile.json
  invariants.json
  result.json
  verdict.md
artifacts/timing/
  timing-matrix.json
  terminology.md
  verdict.md
mydocs/evidence/
  T03_Tick账本验收报告.md
```

## 2. Architecture

- `inputTick` 以 InControl 的 `ulong updateTick` 为源，不自行猜测。
- `visualTick` 由持久 Runtime driver 的 `Update` 边界计数。
- `fixedTick` 由同一 driver 的 `FixedUpdate` 计数。
- `HeroUpdateBeforeOriginal`、`LateUpdate` 和 scene hooks 作为 phase event，不另称“帧”。
- 每个 float 同时记录：
  - invariant-culture round-trip 文本；
  - IEEE 754 bit pattern。
- T-FT 初始记录 `Time.time - Time.fixedTime`，但只有实验后才决定是否成为验证字段。
- 探针只在显式命令行参数存在时启用，普通启动零行为变化：

```text
--hktas-timing-probe=P60|P30|PLOAD|PSCENE
--hktas-timing-probe-run=<run-id>
--hktas-timing-probe-slot=<1..4>
--hktas-timing-probe-input-ticks=<count>
--hktas-timing-probe-exit
```

Runtime 原始证据写入 T01 session 下的 `timing/<runId>/`；harness 在进程正常退出后复制到仓库忽略的 evidence root。

## 3. Detailed Design

### 3.1 Types

```csharp
public enum TickPhase : byte
{
    InControlCommitted,
    VisualUpdateBegin,
    HeroUpdateBeforeOriginal,
    FixedUpdateBegin,
    LateUpdateEnd,
    SceneLoadRequested,
    ActiveSceneChanged,
    HeroHazardDeathRequested,
    HeroHazardDeathEntered,
    HeroHazardRespawnEntered,
    HeroHazardRespawnCompleted,
    ProfileApplied,
    ProfileRestored,
    LedgerGap
}

public readonly struct TickStamp
{
    public ulong InputTick { get; }
    public long VisualTick { get; }
    public long FixedTick { get; }
    public int SceneEpoch { get; }
    public TickPhase Phase { get; }
}

public sealed class TickLedgerRecord
{
    public long Sequence { get; }
    public string SessionId { get; }
    public string ManifestSha256 { get; }
    public string RunId { get; }
    public string Profile { get; }
    public TickStamp Stamp { get; }
    public int FixedStepsSincePreviousVisual { get; }
    public int TimeBits { get; }
    public int FixedTimeBits { get; }
    public int TimeMinusFixedTimeBits { get; }
    public int DeltaTimeBits { get; }
    public int UnscaledDeltaTimeBits { get; }
    public int TimeScaleBits { get; }
    public int RealtimeSinceStartupBits { get; }
    public string SceneName { get; }
    public string Detail { get; }
}

public static class TickLedgerValidator
{
    public static LedgerValidationReport Validate(
        IEnumerable<TickLedgerRecord> records);
}
```

`FixedStepsSincePreviousVisual` 只在 `VisualUpdateBegin` 有意义；其他 phase 固定写 `-1`。每条 ledger line 都携带 session、manifest、run/profile 标识，不能只靠文件名隐式关联。

### 3.2 Invariants

- 各计数在 session 内不倒退。
- `fixedStepsSincePreviousVisual` 等于相邻 visual boundary 之间的 fixed 事件数。
- 每个 scene epoch 单调递增；scene 名称只在明示事件后切换。
- 每个 phase event 能映射到一个完整 session 和 manifest。
- 允许某 visual 区间 0、1 或多个 fixed step；不得把这些样本判为格式错误。
- 日志队列溢出必须生成 `ledger-gap`，不得静默丢样本。
- `sequence` 从 1 严格递增；缺行、重复或倒退必须报告第一处错误。若有 `LedgerGap`，该 run 明确 FAIL，不得用内存中的完整列表掩盖文件丢样。

### 3.3 Experiment Profiles

| Profile | 设置 | 目的 |
|---|---|---|
| P60 | 目标 60 FPS，正常负载 | 基线 |
| P30 | 目标 30 FPS | 观察 fixed catch-up |
| PLOAD | 可重复的人为渲染/CPU 负载 | 观察 0/N fixed step 与日志完整性 |
| PSCENE | 固定路线跨场景、死亡、重生 | 验证 epoch 与生命周期 |

每个 profile 至少 5 次独立进程运行，每次至少 1,000 个 input tick；原始 ledger 不合并覆盖。

- P60/P30/PSCENE 将 `vSyncCount` 临时设为 0，分别将 `targetFrameRate` 设为 60/30/60。
- PLOAD 使用 60 FPS target，并在 `LateUpdate` 记录之后施加固定时长 CPU busy load；墙钟只决定负载持续时间，不作为 simulation 排序依据。
- PSCENE 在测试槽 gameplay 稳定后触发真实 hazard death/respawn，再执行 `ReturnToMainMenu(DontSave)` 并重载同一槽；必须观察到死亡、重生、离开和重新进入场景。
- 每个 run 保存并恢复 `targetFrameRate`、`vSyncCount`、`timeScale` 与 `fixedDeltaTime`；后两者不主动修改。

## 4. Independent Verification

### Offline

- 三个 fixtures 分别得到预期 PASS/PASS/FAIL。
- validator 对缺行、重复 sequence、倒退 tick、scene 无事件切换给出精确首错位置。

### In-Game PASS

- 所有 profile 无静默 gap。
- 计数与 phase 顺序可由 validator 完整解释。
- P30/PLOAD 出现的 0/N fixed step 被正确计入，不导致账本歧义。
- 相同 profile 的 phase-order signature 在 5 次运行内一致；数值时间可不同，但映射规则一致。
- 产出一份正式术语表，明确 movie 输入单位与 visual/fixed 的关系。
- `Summarize-T03Timing.ps1` 只汇总 evidence root 的直接子目录，要求 P60/P30/PLOAD/PSCENE 各 5 次、共 20 次；开发烟测必须另存或归档，不能混入正式门禁。

### FAIL / Downgrade

- 关键 phase 顺序跨运行随机变化且无法记录/归因。
- 固定步事件无法可靠计数。
- 账本在合理负载下频繁溢出。

T03 FAIL 时，不得使用“逐 tick”“帧精确”或“确定性账本”表述；T06 只能降级为 best-effort input playback。

## 5. Implementation Checklist

- [x] 实现 ledger Core 类型与 validator。
- [x] 实现持久 runtime、InControl、Hero、scene phase 探针。
- [x] 实现 exact-bit float 记录与 gap 事件。
- [x] 创建 fixtures 与 unit tests。
- [x] 执行 P60/P30/PLOAD/PSCENE。
- [x] 生成 phase-order signature、统计和术语判定。
- [x] 把经证据确认的 tick 定义同步到 T04/T06。

## 5.5 Verification Record

- 验收报告：[`mydocs/evidence/T03_Tick账本验收报告.md`](../../evidence/T03_Tick账本验收报告.md)。
- 正式矩阵：P60/P30/PLOAD/PSCENE 各 5 次独立进程，共 20/20 PASS。
- Movie input unit：`InControlCommittedTick`。
- Phase-order signature SHA-256：`1f960fafc8a94616130c33876472b42b7cb1e18273d511b123fb1402e71129bc`。
- `timing-matrix.json` SHA-256：`e1c91b3e927c53696a9230836b32ffffedae2a4ebe977a3e431f5a4fd049e146`。
- 离线验证：solution 0 warning/0 error，25/25 tests。

## 6. Rollback

- 探针关闭后不改变 `Time.timeScale`、`fixedDeltaTime`、targetFrameRate 或 quality settings。
- profile 临时设置必须在 `finally` 中恢复并写恢复事件。
