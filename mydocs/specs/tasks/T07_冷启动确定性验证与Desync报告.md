# Task Spec T07: 冷启动确定性验证与 Desync 报告

- **Status**: VERIFIED_LOCAL
- **Gate**: G2 Determinism
- **Depends On**: T05, T06
- **Produces**: 10-run 冷启动验证报告、run signature、首差异证据包

## 0. Open Questions

- None。MVP 的正式标签分为 `LOCAL_VERIFIED` 与 `PORTABLE_VERIFIED`，不得混写。

## 1. Requirements

### Goal

用独立游戏进程从同一 baseline 重放同一 movie，比较每个 milestone 的 ledger 摘要和 semantic hash；出现失步时输出第一处可操作差异，不做隐藏纠偏。

### In-Scope

- Run manifest locking、milestone capture、run signature。
- 10 次独立进程本机冷启动。
- 第二个独立安装或机器的 portability 验证。
- 首差异上下文：输入、tick、T-FT、scene、hash diff、近期事件。
- CLI 聚合与报告。

### Out-of-Scope

- 自动控制 Steam/进程启动。
- 用状态写入让失败路线强行收敛。
- 宣称未观测的 native/线程状态已经确定。

## 1.5 Code Map

```text
src/HollowKnightTAS.Core/Verification/
  MilestoneRecord.cs
  RunEvidence.cs
  RunSignature.cs
  RunComparator.cs
  DesyncReport.cs
src/HollowKnightTAS.Runtime/Verification/
  RuntimeVerificationSession.cs
  MilestoneCaptureController.cs
src/HollowKnightTAS.Cli/Commands/
  VerifyRunCommand.cs
  CompareRunsCommand.cs
tests/HollowKnightTAS.Core.Tests/Verification/
fixtures/verification/
  matching/
  first-diff/
artifacts/verification/<campaignId>/
  campaign.json
  run-01/...run-10/
  comparison.json
  report.md
```

## 2. Architecture

- 每个 run 必须有新 `sessionId` 和新进程启动证据。
- Campaign 锁定：movie ID、manifest hash、baseline hash、snapshot schema、ledger schema、milestone 列表。
- Milestone record 不只存 hash；还存 tick stamp、scene epoch、snapshot canonical bytes 的路径和 ledger window 摘要。
- Run signature 为按 milestone 顺序连接的 canonical record SHA-256。
- comparator fail fast 但报告第一个差异前后固定窗口，默认各 32 movie ticks。

## 3. Detailed Design

```csharp
public sealed class MilestoneRecord
{
    public string MilestoneId { get; }
    public long MovieTick { get; }
    public TickStamp TickStamp { get; }
    public string SceneName { get; }
    public string SemanticSha256 { get; }
    public string LedgerWindowSha256 { get; }
}

public sealed class RunEvidence
{
    public string SessionId { get; }
    public string ManifestSha256 { get; }
    public string BaselineSha256 { get; }
    public string MovieId { get; }
    public IReadOnlyList<MilestoneRecord> Milestones { get; }
    public string RunSignature { get; }
}

public static class RunComparator
{
    public static RunComparison Compare(
        RunEvidence expected,
        RunEvidence actual);
}
```

Desync report 必填：

- expected/actual manifest、baseline、movie ID。
- 第一处不同 milestone 与 movie tick。
- semantic key diff。
- input sample 和前后 32 tick 的 ledger/phase。
- scene/load 事件、T-FT/fixed count。
- RNG state hash 若 T10 已启用；未启用时明确写 `not-captured`。

## 4. Independent Verification

### Campaign A: Local

- 隔离 profile，仅 Modding API + HollowKnightTAS。
- 相同 baseline/movie，10 次独立进程启动。
- 每次完整退出并生成独立 session。

### Campaign B: Deliberate Divergence

- 在 movie 的一个无歧义位置修改 1 tick 输入。
- comparator 必须定位到首个受影响 milestone，并输出输入与语义 diff。

### Campaign C: Portable

- 第二个干净安装或第二台机器。
- DLL/Mod/settings/baseline hash 必须匹配。
- 至少 3 次独立进程回放。

### PASS Levels

- `LOCAL_VERIFIED`：
  - Campaign A 的 10 个 run signature 完全相同。
  - Campaign B 被正确判 FAIL，首差异报告完整。
- `PORTABLE_VERIFIED`：
  - 在 `LOCAL_VERIFIED` 基础上，Campaign C 的 signature 也一致。

manifest 不匹配时结果是 `INCOMPARABLE`，不能算 PASS 或 desync。

### FAIL

- 10 次中任一次 milestone/hash 不同。
- 报告只能给“hash 不同”，无法指出 key/tick/scene 上下文。
- 工具删除、覆盖或只保留最后一次原始证据。
- 使用自动状态纠偏后仍标为 verified。

## 5. Implementation Checklist

- [x] 实现 milestone/run/campaign 模型与 canonical signature。
- [x] 实现 runtime milestone capture。
- [x] 实现 CLI run validate、compare、campaign aggregate。
- [x] 添加 matching/first-diff/incomparable fixtures。
- [x] 创建隔离 profile 和锁定 baseline。
- [x] 执行 Campaign A/B。
- [x] Campaign C 未执行并明确标记为 `NOT_RUN`；未宣称 portable。
- [x] 将最终报告链接回 README，避免泛化到未验证环境。

## 5.5 Verification Result

- 等级：`LOCAL_VERIFIED`。
- Campaign A：10 个独立 session/process 的 run signature 全部为
  `cae096b6b774531d97f773478a10fac119e46f82c2b0ef583784451d3721712a`。
- Campaign B：movie tick 490 注入 `Attack` 后，首差异为
  `checkpoint:divergence-probe` / tick 491 /
  `hero.cState.attacking`。
- Campaign C：`NOT_RUN`，所以状态不是 `PORTABLE_VERIFIED`。
- 离线门禁：80/80 tests；Runtime build 0 warning / 0 error。
- 完整证据：
  [`T07_冷启动确定性与Desync验收报告.md`](../../evidence/T07_冷启动确定性与Desync验收报告.md)。

## 6. Failure Triage Order

1. 先比较 manifest/baseline/movie。
2. 再比较 input/tick mapping。
3. 再比较 scene/load 与 semantic keys。
4. 再启用 T10 RNG 诊断。
5. 最后才扩大状态观测范围。

不得从“hash 不同”直接推断是 RNG，也不得先加入隐藏状态写入。
