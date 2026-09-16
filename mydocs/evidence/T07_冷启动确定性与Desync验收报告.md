# T07 冷启动确定性与 Desync 验收报告

## 结论

T07 **PASS / LOCAL_VERIFIED**。在隔离为 Modding API +
HollowKnightTAS 的本机 profile 中，同一 baseline 和 movie 完成了 10 次
独立 Steam 冷启动回放；10 个 session ID、进程实例和原始证据目录均独立，
5 个 milestone 组成的 run signature 全部严格等于：

```text
cae096b6b774531d97f773478a10fac119e46f82c2b0ef583784451d3721712a
```

故意在 movie tick 490 注入 1 tick `Attack` 后，比较器在首个受影响
milestone `checkpoint:divergence-probe`（movie tick 491）判定
`Desync`，并定位到：

```text
hero.cState.attacking expected=false actual=true
expected held=0
actual held=32 (Attack)
```

第二个独立安装或机器的 Campaign C **未执行**，因此本结论不得标记为
`PORTABLE_VERIFIED`。

## 交付

- Core：
  `VerificationLedgerEntry`、`MilestoneRecord`、`RunEvidence`、
  `RunSignature`、`RunComparator`、`DesyncReport` 和
  `VerificationSnapshotNormalizer`。
- Runtime：
  `RuntimeVerificationSession`、`MilestoneCaptureController`，以及
  `VERIFY` / `DIVERGENCE` 实机实验入口。
- CLI：
  `verification validate`、`verification compare` 和
  `verification campaign`；分别以 0/4/5 表示匹配、desync 和
  incomparable。
- Fixtures：
  `matching`、`first-diff`、`incomparable` 三组严格静态证据。
- Harness：
  `scripts/Invoke-T07VerificationCampaign.ps1`，支持隔离 profile、
  独立 Steam 启动、断点续跑、原始证据不可变复制和环境恢复。

## 离线验收

在正式实机 campaign 使用的代码上：

```text
dotnet test .\tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj -c Debug --no-build
80 passed / 0 failed

dotnet build .\src\HollowKnightTAS.Runtime\HollowKnightTAS.Runtime.csproj -c Debug /p:SkipHKTASInstall=true
0 warning / 0 error

git diff --check
clean（仅 Git 行尾提示）
```

已验证：

- session/process ID、绝对 input/visual/fixed tick 原点和绝对 T-FT 余量只作
  诊断，不进入跨进程 run signature。
- manifest、baseline、movie、schema、milestone 顺序、scene epoch/phase、
  语义状态、因果 ledger window 和 RNG hash 进入 signature。
- metadata 不匹配返回 `Incomparable`，不会误报为 PASS 或 desync。
- semantic、ledger 或 metadata 的首个差异都能输出相邻输入、tick、
  fixed count、T-FT、scene/event、RNG 和语义 key diff。
- Campaign 聚合要求 10 份独立 session/process 证据，不能复制同一次结果
  充数。

## 验证投影与确定性边界

T05 的原始 snapshot 仍保留 exact IEEE-754 bit；T07 run signature 使用显式
版本化投影 `v1-float32-decimal-4`，只在比较层把浮点值归一到 4 位小数。
该投影消除静止 Hero 的 sub-ULP 位置抖动，但不写入或纠正游戏状态。投影
版本是 campaign metadata 的一部分，改变投影后旧证据不可直接比较。

正式 oracle 使用 `GG_Vengefly` 中归一化后的 grounded/idle 静止路线：
490 个 neutral tick、`pre-divergence` checkpoint、1 个声明 neutral tick、
`divergence-probe` checkpoint、120 个 neutral tick 和最终 scene assertion；
含释放观察共 612 条 observation。验证 profile 只为隔离路线停用非 Hero
`HealthManager`，数量写入每次结果，本次均为 1。

首版移动路线没有通过：相同 movie 在 milestone 前分别出现 111 和 113 个
fixed tick，Hero X 随之分叉。这与 T06 已观察到的 input tick 与 physics
tick 无固定比例一致，因此 T07 缩小 oracle 为静止状态，并保留失败证据；
没有通过隐藏状态写入让移动路线强行收敛。移动、加载、重生和 RNG 路线仍
需由后续 T09/T10 等任务扩展验证。

## 实机 Campaign

命令：

```powershell
.\scripts\Invoke-T07VerificationCampaign.ps1 `
  -EvidenceRoot .\artifacts\verification-final `
  -MaxRunSeconds 180
```

锁定值：

| 字段 | SHA-256 |
|---|---|
| Manifest | `f9966db6db0f9f31958f000959301fd4fc54f5282d07c3afe2b94a93f3baee53` |
| Baseline | `8b8c7995768198cdac8b44a563e875a50bb47efc70a25eab2e7afabccbd262b9` |
| Movie ID | `d1e0932bc0a31c8857e227539dbd04cbd7c3b3a025ee2753f47b8cb88b87c385` |
| Run signature | `cae096b6b774531d97f773478a10fac119e46f82c2b0ef583784451d3721712a` |

| Run | Session ID | Process instance |
|---|---|---|
| 01 | `20260728T053216.7964095Z-310e7cb348b84c5784f3da4230244b14` | `8748-639208135313323201` |
| 02 | `20260728T053237.4538843Z-e15497b8878a42c1860f5134f928c269` | `38000-639208135539295846` |
| 03 | `20260728T053257.2790041Z-7ed141efaece4b558da0183905bfbf6e` | `13496-639208135738438922` |
| 04 | `20260728T053317.4721058Z-f6d3faa030b341f2ba18c86af586788c` | `17376-639208135944975744` |
| 05 | `20260728T053340.1997466Z-583c47d645e74912b16004b02186bb1b` | `36808-639208136148966132` |
| 06 | `20260728T054632.2912176Z-bcf0ee151ff349cd963399b9e420e474` | `23108-639208143872806965` |
| 07 | `20260728T054653.7718057Z-738b0db9b627402895d0bedd89750c50` | `41968-639208144103167574` |
| 08 | `20260728T054715.3076322Z-8552d852ba38421aa3a664c031c30841` | `28440-639208144317859508` |
| 09 | `20260728T054737.1600305Z-f27d6d442d0643bc82c0d00d4fabf1d1` | `16248-639208144537084626` |
| 10 | `20260728T054758.4852835Z-dd6018f5ec2b40719b30d5e6af12d8ee` | `43164-639208144750619234` |

## 原始证据与恢复

- `artifacts/verification-final/campaign.json`：
  `5ac820b39f05c52cb8371e4915ca28d4961cbbf4295477db4adf0786e1a62c8e`。
- `artifacts/verification-final/comparison.json`：
  `e136996457ca3622f02759493f56226a57c9d3e14fbcb4bc8e2cdb0060948fa0`。
- `artifacts/verification-final/report.md`：
  `54a71a63b2f14ed18fefd600496192e0b0c306ff1ab08de08379ae602503780f`。
- 正式安装 ZIP 与 `SHA256.txt`：
  `4ce4d4088bec90b19e8aceb342300b1f85d750e697d72a0e4d8f4b52cbe77a03`。
- `verification-smoke` 至 `verification-smoke-3` 保留 MAX_PATH、移动路线
  调度分叉和 sub-ULP 浮点噪声的失败证据；`verification-smoke-4` 为通过
  的正式路线预演。
- Campaign 中途遇到 Steam Cloud 同步警告时没有绕过或强制继续，而是
  取消启动；Cloud 后续自行恢复并报告已同步到 change number 2040，随后
  使用 harness 断点续跑剩余独立进程。
- 实验后游戏进程为 0，普通 Mods profile 恢复为 18 项，无
  `Mods.HKTAS-*` 交换目录残留，`VerificationModeRequested=false`。

