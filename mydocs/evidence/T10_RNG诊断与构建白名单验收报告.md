# T10 RNG 诊断与构建白名单验收报告

## 结论

T10 为 **PASS / VERIFIED**，能力边界固定为：

```text
Unity Random.State：已捕获、可稳定指纹化
Unity 调用点：2 个目标构建白名单方法
coverage：unity-random-partial-whitelist-v1
System.Random：not-covered
其他 Mod RNG：not-covered
RNG playback/结果强制：未实现、未授权
```

正式证据位于：

```text
artifacts/rng/final-5-match/
```

本结论证明 RNG 差异现在可以被记录和归因；它不把局部白名单描述为
“Hollow Knight 的全部随机性已受控”。

## 交付能力

- 目标构建专用 `Random.State` codec：严格校验 CoreModule SHA-256、
  MVID 和 `s0..s3` 四个 Int32 字段，再编码为 16 字节 canonical payload。
- Core 层 `RngStateFingerprint`、`RngWhitelist`、`RngTrace/RngDiff`。
- build resolver 同时核对 Assembly-CSharp SHA-256、MVID、signature、
  metadata token、原始 IL SHA-256、随机 API 和静态调用数。
- `Helper.GetRandomVector2InRange` 与 `HeroController.TakeDamage` 的
  method-boundary 只读 hook；记录 tick、全局/站点序号和调用前后 state。
- 始终开启但有界的 `rng-ledger.jsonl`、`whitelist-resolution.json`、
  milestone state hash、call count 和结构化 fault/summary。
- Environment manifest schema v2 中的 `rngCodecId` / `rngCoverage`；
  旧 schema v1 fixture 继续产生原 canonical bytes。
- T07 比较器的 `rng-state` 优先首差异原因，以及 expected/actual state、
  call-site 和 before/after state 上下文。

## 锁定构建与白名单

```text
game = 1.5.78.11833

UnityEngine.CoreModule.dll
sha256 = 0f0bec6f864da12ea7fe2d068af73c04d8c4dc267b21490e5c3ca1e730e59b14
mvid   = 047294ce-45cf-4b9a-9441-e48d2b8435e4
codec  = unity-random-state-1.5.78.11833-s0-s3-be-v1

Assembly-CSharp.dll
sha256 = 5944411bd93830369390a4b51766ee68c4ab26195b299e25a07b5e7d0e00086d
mvid   = 2b54cdaf-7a93-4f0e-b297-d50c4090ea9f
```

| Call-site ID | Token | IL SHA-256 | Unity RNG API / 静态调用数 |
|---|---:|---|---|
| `helper.random-vector2` | `0x06001BC0` | `6fe187f1d6cd4badb112c5b4269f171ade2e9cc38d561ad3095d121d0c7bcb38` | float `Range` / 2 |
| `hero.take-damage` | `0x06000578` | `f5003dd615f7e330adb220543ba9582d40b4c8b6a575f63cebd788dd3512310e` | int `Range` / 7 |

错误 assembly/MVID 会得到 `AssemblyMismatch/ModuleMismatch`；变异 token
fixture 得到 `CallSiteMismatch`。Runtime 只有在 codec 与两个调用点都
精确解析时才安装 hook。

## 离线验证

```text
dotnet build .\HollowKnightTAS.sln -c Debug
0 warning / 0 error

dotnet test .\tests\HollowKnightTAS.Core.Tests\
  HollowKnightTAS.Core.Tests.csproj -c Debug --no-build
119 passed / 0 failed
```

测试覆盖 100 次 fingerprint 稳定性、malformed hash、assembly/MVID/token
fail-closed、首 state/call 差异、codec 不可比较、manifest schema v2 和
T07 `rng-state` 优先诊断。

## 正式 7 进程矩阵

`Invoke-T10RngMatrix.ps1` 隔离为 Modding API + HollowKnightTAS，依次运行
5 个 MATCH、1 个 NOHOOK 和 1 个 DIVERGE 进程。每个进程均完成 codec
round-trip、进入 `GG_Vengefly`、受控调用、60 movie tick 观察、清理并退出。

### MATCH 5/5

五次受控段完全一致：

```text
rngTraceSha256      = 3ddaee19ab5a6d2755bc97c5d868d05e018f14737af2841b95504d6aa6e25255
semanticTraceSha256 = aa7fc744eaa31706dc24075e6c77a7ffeb6af84435ebc7b0460a1be56eeb1644
callTraceSha256     = 248b7c32fcbeac9d0fed8095735d23e7c5c4f95a781f04b7435aac5c71978986
globalCallDelta     = 2
state records       = 92/run
call records        = 2/run
dropped records     = 0
```

受控段在同一 safe tick 内完成 seed、Helper 调用、Hero damage 调用和
milestone 采样，避免把房间中未纳入白名单的环境 RNG 误判为这两个 hook
自身的不确定性。

### NOHOOK parity

关闭两个 hook 后：

- 受控 RNG trace 与 MATCH 相同；
- semantic trace 与 MATCH 相同；
- `globalCallDelta=0`，call records 为 0。

因此读取 state 与 method-boundary 记录没有改变本路线的语义结果。

### Deliberate divergence

DIVERGE 使用不同 seed，并在后续 milestone 故意改变一项语义状态：

```text
firstRngDifferenceMilestoneIndex      = 0
firstSemanticDifferenceMilestoneIndex = 4
divergencePass                        = true
```

RNG 诊断在语义结果分叉前给出首差异，满足 T07 集成门禁。

## 真实覆盖缺口

5 个 MATCH 在 60-tick ambient endpoint 得到 4 个唯一 RNG state hash，
因此正式矩阵写入：

```text
ambientEndpointConverged = false
unwhitelistedUnityRngObserved = true
```

这不是测试失败，而是 T10 要求显式暴露的事实：目标房间在两个最小白名单
方法之外仍会消费 Unity RNG。当前结果只能支持“受控样本可重复、白名单
hook 不改语义、差异可提前定位”；不能支持“整个房间 RNG 已确定”。

若后续 T11/T14/TAS 路线需要端点 RNG 收敛，应从 ledger 首差异附近扩充
逐构建白名单并重新跑本矩阵；不得直接开启旧 token，也不得在普通模式
写回 `Random.state`。`System.Random`、原生随机源和其他 Mod RNG 仍未覆盖。

## 数据与槽安全

Harness 在每轮前后核对四个 save 与 modded sidecar 的长度/hash，最终状态：

```text
gameProcesses = 0
Mods = 18 entries (17 directories + HkVoiceMod.zip)
VerificationModeRequested = false
replay-saves/v1 exists = false

slot1 = 413613FF631E479EAAF7E5E4B2026354478A0E9DC20C1AA7F50AA82055C8B389
slot2 = 1ACD6214B8DACAF112A2FBAE0E5E7AEB3D487C9365F5F955E4AEF8E51459DADE
slot3 = 9ABF27172D1003B607E543B88FD8759DCB0FD9668AD3D57A3A286C9BF4DB4855
slot4 = A6F09F2E7DE8F2B2CD25C8EB923F8F6728120646F97671A1296C00896186B062
```

codec round-trip 和 DIVERGE 的临时 RNG/health 变更均在退出前恢复；证据
目录不包含用户存档原始 bytes。

## 证据完整性

```text
E47B726BC4F3388505C26BD82FB67DDF126220E54C1BF8A83A40E7D2CC60D70F  docs/compatibility/rng/1.5.78.11833.json
4A24995D237EE7E769E21D39D4AB18DC76229EC1656E5E1A904ABD117C131C36  rng-matrix.json
0754AD2EF5DA5319D66FA08CFA75FFA553C9C7F953425F59FCB886959C87670C  verdict.md
DA52640922C3E57DD7EF37DF261FC6E137BAB1CDBA5FA6F20307A710AEE95CBF  match-01/whitelist-resolution.json
4B3E771ABA0997339AD0D27B359015502E6CADDF53014810F251487DC1BC4C44  match-01/probe/result.json
90D88194CBB5CB6E8153A28A9F063C8B62C07005D9CAEA9BD920DDD7BC4D5B83  match-01/rng-ledger.jsonl
8AE3A7A32B017773EE2C1F5597B4108B3EB3B60983B91C89CDDC836097F4A890  match-01/manifest.json
C79AEC0C034597E3041ED6F16FE7C1E093E88AF423DF24768B0EB2C8F796A629  diverge-01/probe/result.json
```

## 支持声明边界

- 仅验证本机锁定 HK `1.5.78.11833` / Modding API v77 / 当前程序集。
- manifest 的 `coverage=partial` 是功能契约，不是临时文案。
- 未知构建保留 state-only 或 disabled 状态；不会复用旧白名单 hook。
- 本任务只诊断，不提供 RNG playback、结果强制或整体确定性证明。
