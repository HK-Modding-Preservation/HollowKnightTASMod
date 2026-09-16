# T03 Tick 账本与时间模型验收报告

- **Task**: T03
- **Gate**: G1 Time Model
- **Verdict**: PASS
- **Target**: Hollow Knight `1.5.78.11833` + Modding API `v77`
- **Date**: 2026-07-28 CST

## 1. 离线验收

| 检查 | 结果 |
|---|---|
| `dotnet build .\HollowKnightTAS.sln -c Debug` | PASS，4 个项目，0 warning，0 error |
| `dotnet test .\tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj -c Debug --no-build` | PASS，25/25 |
| `valid-zero-fixed.jsonl` | PASS |
| `valid-multi-fixed.jsonl` | PASS |
| `invalid-nonmonotonic.jsonl` | 按预期 FAIL，并定位首个倒退 tick |
| sequence 缺失、重复、倒退与 scene 无事件切换 | 均由 validator 按首错拒绝 |
| exact-bit JSON round-trip | PASS |
| 有界异步 ledger sink | PASS |
| 两个 PowerShell harness 的语法解析 | 0 error |

`TickLedgerValidator` 接受 visual 区间内 0、1 或多个 fixed step，但拒绝计数倒退、scene 未经事件变化、metadata 漂移、sequence 缺失/重复/倒退和 `LedgerGap`。

## 2. 正式实机矩阵

正式证据根为 `artifacts/timing/`。汇总器只读取其直接子目录；开发烟测没有混入门禁。

| Profile | 独立进程 | input 观测要求 | fixed steps / visual 汇总 | phase signature | Gate |
|---|---:|---|---|---|---|
| P60 | 5 | 每次至少 1,000 | `0:809, 1:4186`，min/max `0/1` | 一致 | PASS |
| P30 | 5 | 每次至少 1,000 | `0:2, 1:1626, 2:3366, 3:1`，min/max `0/3` | 一致 | PASS |
| PLOAD | 5 | 每次至少 1,000 | `1:3520, 2:1473, 3:2`，min/max `1/3` | 一致 | PASS |
| PSCENE | 5 | 生命周期完成且至少 1,000 | `0:5884, 1:3796, 2:6, 3:5, 12:5`，min/max `0/12` | 一致 | PASS |

20/20 个 run 均满足：

- `runPass=true`、`validatorPass=true`、`droppedCount=0`；
- `restoreEquivalent=true`；
- `ledger.jsonl` 每行均可解析，`sequence` 从 1 连续递增；
- ledger 行数与 `result.recordCount` 完全一致；
- `ledger.jsonl`、`profile.json`、`invariants.json`、`result.json`、`verdict.md` 和 `probe.ready` 均存在。

共同 phase 顺序为：

```text
VisualUpdateBegin>InControlCommitted>HeroUpdateBeforeOriginal>LateUpdateEnd
```

SHA-256：

```text
1f960fafc8a94616130c33876472b42b7cb1e18273d511b123fb1402e71129bc
```

P30 与 PLOAD 均观测到 fixed catch-up；P60/PSCENE 观测到 visual 区间零 fixed step。因此 visual、fixed 与 input 必须保持独立计数，不能互相假定为 1:1。

## 3. 场景与生命周期

PSCENE 的 5/5 run 均实际执行并观测到：

1. `HeroController.TakeDamage(..., hazardType=2)` 请求危险伤害；
2. `hazardDeath` 进入；
3. `respawning` 进入并完成；
4. `ReturnToMainMenu(DontSave)`；
5. `GG_Workshop -> Quit_To_Menu -> Menu_Title -> GG_Workshop`；
6. `sceneEpoch` 随 `activeSceneChanged` 从 0 单调增加到 3。

测试没有把 `HeroController.acceptingInput` 当作 gameplay 生命周期真相源；该字段在测试槽 `GG_Workshop` 中可持续为 `false`，但真实 input、Hero update、死亡/重生和场景生命周期仍正常推进。

## 4. 正式术语判定

- **movie input tick**：一次已提交的 InControl update tick；每个展开后的 movie input sample 消耗一个这种 tick。
- **visual tick**：Runtime driver 的一次 `Update` 边界，只作为账本坐标。
- **fixed tick**：Runtime driver 的一次 `FixedUpdate` 边界；相邻 visual tick 之间允许 0 或多次。
- **scene epoch**：仅在 `activeSceneChanged` 时递增。
- **T-FT**：`Time.time - Time.fixedTime`，保留十进制与 IEEE 754 exact bits，仅作诊断字段，不单独充当同步 oracle。

因此 T04 的 `tick-unit input` 被锁定为 `InControlCommittedTick`，T06 的 movie cursor 每个符合条件的 committed InControl tick 恰好推进一次；raw InControl tick 可从任意绝对值开始。

## 5. 设置恢复与产物

- 每个 run 都保存并等价恢复 `Application.targetFrameRate`、`QualitySettings.vSyncCount`、`Time.timeScale` 和 `Time.fixedDeltaTime`。
- 正式样本中的原始 `targetFrameRate=400`，完成后恢复为 400；`timeScale=1`、`fixedDeltaTime=0.02` exact bits 不变。
- 游戏进程已退出；普通 Mods profile 已恢复为 18 个顶层条目；无 `Mods.HKTAS-T03-*` 交换目录残留。
- `HollowKnightTASMod.GlobalSettings.json` 中 `VerificationModeRequested=false`。
- 当前安装包 SHA-256 为 `89402f46cd10c421d227b4c2384d6e36f57788a224f6449d0871367186ee3200`，与 `SHA256.txt` 一致。

正式汇总文件：

| 文件 | SHA-256 |
|---|---|
| `artifacts/timing/timing-matrix.json` | `e1c91b3e927c53696a9230836b32ffffedae2a4ebe977a3e431f5a4fd049e146` |
| `artifacts/timing/terminology.md` | `c2b1021f5957a0e8e567e479938fdb9b8b8bf5328b44ae632f3d414ba50996a1` |
| `artifacts/timing/verdict.md` | `6f010a8cb3e40866d0f6435668edff9156f282dd75f6cab3b90e28353b72e75a` |

## 6. 门禁结论

**T03 G1 PASS。** 当前构建存在可解释、可验证的 input/visual/fixed/scene ledger，并可把 `InControlCommittedTick` 提升为 movie 输入单位。

该结论只锁定时间模型与账本，不等价于冷启动确定性证明；“已验证重放”仍须通过 T05、T06 与 T07。
