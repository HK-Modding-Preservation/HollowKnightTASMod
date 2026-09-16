# Task Spec T10: RNG 诊断与构建白名单

- **Status**: VERIFIED
- **Gate**: Diagnostics
- **Depends On**: T03, T07
- **Produces**: RNG state ledger、版本化 call-site 白名单、首随机差异

## 0. Open Questions

- None。v1 只诊断，不强制回放 RNG 调用结果。

## 1. Requirements

### Goal

在目标构建上记录 Unity RNG state 的稳定指纹，并对少量经源码确认的游戏调用点计数，使 T07 的失步可以判断“随机状态何时首次不同”，而不是凭猜测归因于 RNG。

### In-Scope

- `UnityEngine.Random.state` 的目标构建 codec 与 hash。
- milestone/tick 采样。
- 基于 assembly hash + method token/signature/IL hash 的调用点白名单。
- build mismatch fail closed。
- 未覆盖 `System.Random`/其他 Mod RNG 的 capability 声明。

### Out-of-Scope

- 拦截所有随机源。
- 在 v1 中写回/强制 RNG 结果。
- 对未知构建复用旧 token/hook。
- 把 RNG state 一致当作整体确定性证明。

## 1.5 Code Map

```text
docs/compatibility/rng/1.5.78.11833.json
src/HollowKnightTAS.Core/Rng/
  RngStateFingerprint.cs
  RngCallRecord.cs
  RngWhitelist.cs
  RngDiff.cs
src/HollowKnightTAS.Runtime/Rng/
  IUnityRandomStateCodec.cs
  UnityRandomStateCodec_1_5_78_11833.cs
  RuntimeRngProbe.cs
  RngCallSiteHooks.cs
  RuntimeRngProbeExperiment.cs
tests/HollowKnightTAS.Core.Tests/Rng/
fixtures/rng/
scripts/Invoke-T10RngMatrix.ps1
artifacts/rng/<campaignId>/
  rng-ledger.jsonl
  whitelist-resolution.json
  verdict.md
```

## 2. Architecture

- Codec 与游戏/Unity build 明确绑定；manifest 记录 codec ID。
- Codec canonicalize `Random.State` 的实际字段布局，字段顺序固定，不使用运行时 `GetHashCode()`。
- State 只读取；诊断测试若临时调用 `InitState`，必须保存并恢复原 state。
- Call-site hook 只有在 assembly SHA、MVID、method signature 和 IL hash 全匹配时启用。
- 白名单之外只报告 `coverage=partial`，不推断不存在其他随机源。

## 3. Detailed Design

```csharp
public readonly struct RngStateFingerprint
{
    public string CodecId { get; }
    public string Sha256 { get; }
}

public interface IUnityRandomStateCodec
{
    string CodecId { get; }
    byte[] Encode(UnityEngine.Random.State state);
}

public sealed class RngCallRecord
{
    public TickStamp TickStamp { get; }
    public string CallSiteId { get; }
    public long GlobalCallIndex { get; }
    public long CallSiteIndex { get; }
    public string BeforeStateSha256 { get; }
    public string AfterStateSha256 { get; }
}

public sealed class RngWhitelist
{
    public string AssemblySha256 { get; }
    public IReadOnlyList<RngCallSiteDescriptor> CallSites { get; }
    public WhitelistResolution Resolve(AssemblyIdentity actual);
}
```

白名单 descriptor 必填：

- stable call-site ID。
- declaring type、method signature。
- metadata token。
- method IL SHA-256。
- hook 类型与预期调用 API。

## 4. Independent Verification

### Codec Tests

- 同一个 state 编码 100 次 bytes/hash 一致。
- 捕获 state → 调用 `InitState` → hash 改变 → 恢复 state → 原 hash 恢复。
- build/codec 不匹配时拒绝启用。

### Call-Site Tests

- 白名单中每个方法在目标 assembly 精确解析。
- 故意修改一份 fixture 的 token/IL hash，resolution 必须 FAIL。
- 在一个已知随机行为房间执行重复路线，记录 call count/state。

### T07 Integration

- 对 matching runs，RNG milestone hash/counter 一致或明确标记未命中白名单。
- 对 deliberate seed divergence，报告在 semantic desync 之前或同一 milestone 指出首个 RNG state 差异。

### PASS

- Codec deterministic，恢复测试通过。
- 白名单不在错误 build 上启用。
- RNG diff 提供 tick/call-site/state 上下文。
- 报告明确区分 Unity RNG、白名单调用、未知 `System.Random` 与其他 Mod。

### FAIL

- 使用 `Random.State.GetHashCode()` 作为持久指纹。
- token/IL 不匹配仍继续 hook。
- 未覆盖随机源被表述为“没有随机性”。
- 诊断逻辑改变正常回放 hash。

## 5. Implementation Checklist

- [x] 研究目标 build 的 Random.State 字段并写 versioned codec。
- [x] 实现 Core fingerprint/whitelist/diff。
- [x] 选择最小调用点白名单并保存源码证据。
- [x] 实现严格 build resolver 与 hooks。
- [x] 完成 codec、mismatch、known-room、T07 集成测试。
- [x] 在 manifest/报告中写 coverage。

## 6. Future Boundary

任何“RNG playback/强制结果”必须另写 Spec，并重新评审是否会把工具从输入重放变成状态纠偏。T10 完成不授权该功能。

## 7. Target-Build Evidence

- 游戏构建：`1.5.78.11833`。
- `UnityEngine.CoreModule.dll`：
  SHA-256 `0f0bec6f864da12ea7fe2d068af73c04d8c4dc267b21490e5c3ca1e730e59b14`，
  MVID `047294ce-45cf-4b9a-9441-e48d2b8435e4`。
- `UnityEngine.Random.State` 在锁定构建上恰有四个按 metadata token 排序的
  private instance `Int32` 字段：`s0`、`s1`、`s2`、`s3`。codec 以
  4 × big-endian Int32 编码，不使用 `GetHashCode()`。
- `Assembly-CSharp.dll`：
  SHA-256 `5944411bd93830369390a4b51766ee68c4ab26195b299e25a07b5e7d0e00086d`，
  MVID `2b54cdaf-7a93-4f0e-b297-d50c4090ea9f`。
- v1 最小白名单：
  - `helper.random-vector2`：
    `Helper::GetRandomVector2InRange(Vector2,Vector2)`，
    token `0x06001BC0`，IL SHA-256
    `6fe187f1d6cd4badb112c5b4269f171ade2e9cc38d561ad3095d121d0c7bcb38`。
  - `hero.take-damage`：
    `HeroController::TakeDamage(GameObject,CollisionSide,Int32,Int32)`，
    token `0x06000578`，IL SHA-256
    `f5003dd615f7e330adb220543ba9582d40b4c8b6a575f63cebd788dd3512310e`。
- resolver 同时核对 assembly SHA、MVID、signature、token、IL hash、
  目标 `UnityEngine.Random` API 与静态调用数；任一不符即 fail closed。

## 8. Verification Result

- 正式证据：`artifacts/rng/final-5-match/`。
- 离线门禁：`119/119` tests，solution build `0 warning / 0 error`。
- codec 在每个实机进程中完成同一 state 100 次稳定编码、
  `InitState` 后 hash 改变以及原 state 精确恢复。
- 5 个独立 MATCH 进程的受控轨迹完全一致：
  - RNG trace：
    `3ddaee19ab5a6d2755bc97c5d868d05e018f14737af2841b95504d6aa6e25255`；
  - semantic trace：
    `aa7fc744eaa31706dc24075e6c77a7ffeb6af84435ebc7b0460a1be56eeb1644`；
  - call trace：
    `248b7c32fcbeac9d0fed8095735d23e7c5c4f95a781f04b7435aac5c71978986`；
  - 每次记录 2 个白名单 method-boundary call，零记录丢弃。
- NOHOOK 控制进程得到相同受控 RNG/semantic trace，且 call count 为 0，
  证明诊断 hook 没有改变该受控路线的语义 hash。
- DIVERGE 进程在 milestone index `0` 首先报告 RNG state 差异，
  deliberate semantic divergence 到 index `4` 才出现。
- MATCH 的 60-tick ambient endpoint 只得到 4 个唯一 RNG hash / 5 次；
  该结果明确标记 `unwhitelistedUnityRngObserved=true`。它证明当前
  coverage 是 `unity-random-partial-whitelist-v1`，而不是完整 RNG 控制；
  `System.Random` 与其他 Mod RNG 均保持 `not-covered`。
- manifest schema v2 写入 `rngCodecId` 与 `rngCoverage`；旧 schema v1
  canonical fixture 保持字节兼容。T07 comparator 会优先以
  `rng-state` 输出首差异，再附带 tick/call-site/before/after state 上下文。
- 收尾：游戏进程 0、普通 Mods 18 项、四槽原始/Mod 数据均未改变、
  `VerificationModeRequested=false`、隔离 `replay-saves/v1` 已清理。
