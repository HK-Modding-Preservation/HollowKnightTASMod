# SDD Spec: HollowKnightTASMod 项目评估与任务拆解

## 0. Open Questions

- None。后续用户已授权按任务 Spec 顺序完整实现；T01-T15 的适用门禁已验证，当前执行最终 T16。
- 目标实现基线暂定为本机已安装的 Hollow Knight `1.5.78.11833` + Modding API `v77`；其他构建必须通过 manifest 与适配层显式支持。

## 1. Requirements (Context)

- **Goal**: 分析当前仓库的真实完成度与技术可行性，将宽泛的 Phase 0/1 路线拆成可独立实现、可独立验证、可判定 Go/No-Go 的任务，并为每个任务持久化一份 Spec。
- **In-Scope**:
  - 当前仓库代码地图与缺口分析。
  - Runtime Mod、自动启动 Companion/Studio 和可选 NativeHost 的能力边界与有条件立项结论。
  - 目标源码结构、依赖关系、阶段门禁和失败降级路径。
  - 16 份任务级 Spec、任务索引以及 README 入口。
- **Out-of-Scope**:
  - 创建 `.csproj`、C#/C++ 源码、测试代码、Companion/Studio 或 NativeHost。
  - 修改 Hollow Knight 安装、Mods 目录、存档或配置。
  - 宣称已经完成实机输入注入、逐 tick 控制或确定性验证。
  - 在没有 T13 单项实验的情况下宣称全局即时内存 savestate、任意 RAM 读写或系统调用/线程/文件/网络虚拟化已经可用。

## 1.1 Current-State Evidence

- Git：`main` 与 `origin/main` 当前均位于 `9addad8`，工作树在本轮开始时干净。
- 仓库：本轮评估开始时只有 7 个 Markdown 研究/过程文件；完善后仍是纯文档仓库，没有 `.sln`、`.csproj`、`.cs`、测试或构建脚本。
- 本机目标游戏：
  - `Assembly-CSharp` 源码内版本字符串为 `1.5.78.11833`。
  - Modding API 源码常量为 `77`，运行时版本格式为 `<game-version>-77`。
  - `Assembly-CSharp.dll` SHA-256 为 `5944411BD93830369390A4B51766EE68C4AB26195B299E25A07B5E7D0E00086D`。
  - 必需的 `PlayMaker.dll`、`MMHOOK_Assembly-CSharp.dll`、`MMHOOK_PlayMaker.dll`、`MonoMod.Utils.dll`、Unity 模块均存在。
- 本机构建环境：.NET SDK 8/9 与 .NET Framework 4.7.2 reference assemblies 均存在。
- 当前 `Managed/Mods` 含多个其他 Mod，不能直接作为确定性验收 profile；未来必须建立隔离 profile。
- 后续执行快照：T01-T11 已按各自门禁独立验收；当前 solution 包含
  Runtime/Core/CLI、129 项离线测试、T09 持久化重放存档、T10 RNG
  partial whitelist 和 T11 typed Inspector/overlay/exporter。

## 1.5 Code Map (Project Topology)

### 当前物理结构

- `README.md`: 可行性研究主文档、能力边界和阶段路线。
- `docs/来源与证据索引.md`: 14 个原始来源、16 个补充来源与断言边界。
- `docs/验证实验清单.md`: Phase 0/1 与外部能力的跨任务实验清单。
- `mydocs/codemap/2026-07-27_20-54_hollowknighttasmod项目总图.md`: 文档仓库总图。
- `mydocs/specs/2026-07-27_20-54_HollowKnightTASMod研究文档.md`: 上一轮研究仓库 Spec。
- `mydocs/specs/tasks/`: T01-T16 的任务级唯一事实源与索引。
- `memory/HISTORY.md`: 上一轮执行检查点。

### 目标实现结构

```text
HollowKnightTAS.sln
src/
  HollowKnightTAS.Core/       netstandard2.0；协议、movie、账本、哈希、diff
  HollowKnightTAS.Runtime/    net472；HK Mod 生命周期、输入、时间、场景、观测
  HollowKnightTAS.Cli/        net8.0；离线校验、ledger/hash diff
  HollowKnightTAS.Companion/  net8.0-windows；Studio、IPC、存档目录和能力编排
  HollowKnightTAS.Automation.Client/ net8.0；外部 typed automation SDK
  HollowKnightTAS.AgentBridge/ net8.0；本地 stdio MCP 与 AI 适配
native/
  HollowKnightTAS.NativeHost/ C++20 x64；可选进程级 capability host
  HollowKnightTAS.NativeBridge/ C ABI；仅已验证的进程内原生 hook
tests/
  HollowKnightTAS.Core.Tests/
  HollowKnightTAS.Runtime.Tests/
  HollowKnightTAS.Companion.Tests/
  HollowKnightTAS.Automation.Tests/
  HollowKnightTAS.AgentBridge.Tests/
  HollowKnightTAS.NativeHost.Tests/
schemas/
fixtures/
packaging/
  companion.manifest.json     签名的固定路径、版本、RID、SHA-256 与协议范围
  native.manifest.json        可选 NativeHost/Bridge、capability 与构建白名单
artifacts/                    gitignored；实机 ledger、报告、movie、manifest
```

### 已核对的关键运行时链路

- `InControl.InputManager.UpdateInternal()` 先更新 device/action set，最后触发公开 `OnUpdate`；它适合作为“本 tick 动作已提交”的观测点，不应未经实验当作同 tick 前置注入点。
- `HeroController.Update()` 先触发 `ModHooks.OnHeroUpdate()`，再执行原始 Update；原始输入处理读取 `InputHandler.inputActions` 的 `IsPressed`、`WasPressed`、`WasReleased`。
- `BindingSource` 可继承，`PlayerAction` 暴露 `AddBinding`、`RemoveBinding`、`ClearBindings`；T02 已用 30 次独立进程矩阵证明 gameplay 真实绑定可隔离、四类停止路径可等价恢复，并选定公开 `OnUpdate` 后预装下一 sample 的候选 A。
- `ModHooks` 已提供新游戏、存档加载、场景加载、Hero Update、退出等生命周期入口，可支撑 manifest、证据流和安全清理。

## 2. Architecture & Feasibility

### 2.1 Overall Verdict

**有条件 GO。**

- 研究层已经足以指导原型，不需要继续扩写“能否完全替代 libTAS”的泛化讨论。
- 实现层已完成 T01-T15 的适用门禁：T12 三种启动路径、Companion off/on 语义对照和 60 分钟 soak 已正式通过；T13 签名 NativeHost、只读 observe、native-off/on 各 10 次逐 run 原生门槛与语义对照已正式通过；T14 已完成 `ReplayOnly` 安全降级并明确未启用 RoomEntry；T15 的非视觉 Automation/AI 接口、权限、安全与 60 分钟耐久矩阵已独立验收。T16 尚未完成，因此仍不能描述为已通过最终 TAS 路线验收。
- 立项成立的产品定义是：**面向 Hollow Knight `1.5.78.11833` 的 Runtime Mod + 自动启动 Companion/Studio + 可选 NativeHost；核心提供动作语义级输入录制/重放、状态观测、持久化重放存档和可审计同步验证**。
- 立项不成立的产品定义是：**只因存在外部进程就宣称已经等价复刻 libTAS 的进程快照、系统时间、线程和 I/O 控制**。
- CelesteTAS 的 Mod/Studio/共享协议和随游戏启动 Studio 是产品分层参考；HK 的输入、时间、状态和 savestate 仍按自身门禁验证。

### 2.2 Capability Matrix

| 能力 | 当前判断 | 证据/约束 | 必须通过的任务 |
|---|---|---|---|
| 可重现构建与 Mod 加载 | 高 | 本机 DLL、SDK、net472 引用齐全 | T01 |
| 环境 manifest 与结构化日志 | 高 | ModHooks 和已加载 Mod 版本表可用 | T01 |
| 动作层输入观测 | 高 | HeroActions 与 InControl action state 可读 | T02 |
| 动作层输入隔离/注入 | 高 | T02 A/B/C 各 10 次矩阵通过；正式选择公开 `OnUpdate` 边界的 A | T02 |
| visual/fixed/input tick 账本 | 高/已验证 | 20-run 正式矩阵锁定 `InControlCommittedTick`；visual/fixed 独立，scene epoch/lifecycle 可解释 | T03 |
| 文本 movie 与离线编辑 | 高/已验证 | Movie v1、严格 parser/validator/canonical ID 与三个 CLI 子命令已通过 24 项 Movie tests | T04 |
| 稳定语义快照与哈希 | 高/已验证 | 固定 16-key schema、exact-bit canonical/hash/diff 与三组实机实验通过 | T05 |
| 输入录制与回放 | 高/已验证 | 16 进程录制—编辑—回放与停止清理矩阵通过 | T06 |
| 冷启动同步与 desync 归因 | 中高/本机已验证 | 锁定 oracle 10 次独立进程收敛；跨安装未运行 | T07 |
| 暂停与受控步进 | 中/已验证 | `Controlled Step` 通过；不承诺 libTAS 固定物理帧步进 | T08 |
| 任意/定时持久化重放存档 | 高/本机已验证 | 8 点共 80/80 次跨启动严格恢复；不做进程快照 | T09 |
| 持久化语义关键帧与短尾重放 | 中/按房间适配 | 只在安全 room-entry 捕获；显式 adapter manifest；任何不兼容或失步自动回退 T09 | T14 |
| RNG 诊断 | 中/已验证 partial | Unity RNG state 与两个目标构建白名单调用点通过；覆盖明确不完整 | T10 |
| HK 语义 Inspector | 高/已验证 | 10 次稳定身份/功能加载和 10 分钟性能门禁通过 | T11 |
| Companion/Studio、本地 IPC 与 Mod 自动启动 | 高 | 固定随包外部程序可由 Runtime 直接启动；必须做签名/哈希、单实例、ACL 和失败降级 | T12 |
| 外部/原生能力 host | 中 | Windows 用户态进程、构建白名单和 capability broker 可实现；具体能力需单独证明 | T13 |
| 同进程进程级即时 savestate | 低/实验性 | Windows PSS 可捕获诊断数据但无文档化完整恢复；native/线程/协程/句柄/GPU/音频均是恢复难点；不等同 T14 语义关键帧 | T13 |
| 跨启动任意存档 | 中高 | 不依赖原生快照；由 baseline + journal + cursor + hash 重建 | T09 |
| 外部状态观察与受控操作 | 中高 | T12 受信 IPC + Companion broker；稳定语义 DTO、capability、控制租约和审计 | T15 |
| AI 辅助 TAS 闭环 | 中 | stdio MCP/SDK 可把状态与 typed tools 提供给 AI；movie patch 仍需 T04/T07 校验和显式分支接受 | T15 |
| 自编端到端 TAS 验收 | 中/待验证 | 结构化非视觉接口辅助创作；最终计分运行只能使用 canonical input movie | T16 |

### 2.3 Hard Gates

1. **G0 输入门禁**：T02 必须证明一个可重复、无物理输入泄漏、异常时可恢复绑定的动作注入边界。
2. **G1 时间门禁（PASS）**：T03 已用 20-run 矩阵定义无歧义的 input/visual/fixed ledger，并锁定 `InControlCommittedTick` 为 movie 输入单位。
3. **G2 同步门禁**：T05 + T06 + T07 必须在隔离 profile 下完成至少 10 次独立进程冷启动回放并命中全部 milestone。
4. **G3 生产力门禁**：T08/T09 的策略只有在不改变 G2 hash 时才能进入“验证模式”；T14 还必须证明关键帧 + 短尾与 T09 full replay 一致，失败时可干净回退。
5. **G4 外部能力门禁**：T12/T13 必须证明固定签名路径、版本/构建白名单、当前用户 IPC、最小权限、单实例、崩溃隔离和 T07 hash 对照；任一 capability 独立失败只禁用该 capability。
6. **G5 自动化门禁**：T15 默认只读；控制必须经过用户策略、单一短期租约、typed schema、safe tick 和完整审计，且 SDK/CLI/MCP 与游戏内 oracle 一致。
7. **G6 最终端到端门禁**：T16 必须自行创作 input movie，从诸神堂椅子
   冷启动到击败调谐假骑士 5/5；计分运行不得使用视觉识别、人工中途输入、
   typed mutation、teleport 或强制场景。

### 2.4 Dependency Graph

```text
T01 基础工程/manifest/证据流
 ├─ T02 输入相位探针 ─┐
 ├─ T03 Tick 账本 ────┼─ T06 录制/回放 ──┐
 ├─ T04 Movie 协议 ───┘                 ├─ T07 冷启动验证/Desync
 └─ T05 语义快照/哈希 ──────────────────┘

T03 ── T08 暂停/步进
T06 + T07 + T08 ── T09 任意/定时持久化重放存档
T03 + T07 ── T10 RNG 诊断
T03 + T05 + T07 ── T11 Inspector
T09 + T10 ── T14 语义关键帧 / 短尾重放加速
T04 + T07 + T09 + T11 ── T12 Companion/Studio + IPC + Mod 自动启动
T07 + T12 ── T13 NativeHost / 进程级 capability
T10 + T12 ── T15 Automation API / AI AgentBridge
T07 + T09 + T12 + T15 ── T16 自编 TAS 最终验收
```

T01-T13 已按授权顺序独立验收；T14 已按 Spec 降级并验证为 `ReplayOnly`；T15 已独立验收。下一执行单元为最终 T16。每项仍以自己的验收产物独立判断 PASS/FAIL。

## 3. Detailed Design & Task Specs

任务级唯一事实源位于 `mydocs/specs/tasks/`：

1. [T01 基础工程、环境指纹与证据流](tasks/T01_基础工程环境指纹与证据流.md)
2. [T02 输入采样与注入相位探针](tasks/T02_输入采样与注入相位探针.md)
3. [T03 Tick 账本与时间模型探针](tasks/T03_Tick账本与时间模型探针.md)
4. [T04 Movie 协议、解析器与静态校验](tasks/T04_Movie协议解析器与静态校验.md)
5. [T05 语义状态快照与稳定哈希](tasks/T05_语义状态快照与稳定哈希.md)
6. [T06 输入录制与回放引擎](tasks/T06_输入录制与回放引擎.md)
7. [T07 冷启动确定性验证与 Desync 报告](tasks/T07_冷启动确定性验证与Desync报告.md)
8. [T08 暂停与受控步进](tasks/T08_暂停与受控步进.md)
9. [T09 持久化重放存档与定时检查点](tasks/T09_持久化重放存档与定时检查点.md)
10. [T10 RNG 诊断与构建白名单](tasks/T10_RNG诊断与构建白名单.md)
11. [T11 语义 Inspector 与叠层](tasks/T11_语义Inspector与叠层.md)
12. [T12 Companion/Studio、本地 IPC 与 Mod 自动启动](tasks/T12_Studio与本地IPC.md)
13. [T13 外部原生能力层与进程级验证](tasks/T13_外部原生能力层与进程级验证.md)
14. [T14 语义关键帧与短尾重放加速](tasks/T14_语义关键帧与短尾重放加速.md)
15. [T15 外部自动化与 AI 辅助接口](tasks/T15_外部自动化与AI辅助接口.md)
16. [T16 自编 TAS 端到端最终验收](tasks/T16_自编TAS端到端最终验收.md)

## 4. Documentation Checklist

- [x] 读取并核对当前全部仓库文档。
- [x] 核对本机游戏构建、Modding API、关键 DLL、程序集 hash 与 SDK。
- [x] 用实际 InControl/HeroController/ModHooks 源码校正输入可行性。
- [x] 将实现路线拆为 16 个有依赖但可独立验收的任务。
- [x] 为每个任务定义 scope、精确文件、接口、验收、失败降级与 checklist。
- [x] 将用户要求的任意时刻、跨启动选择和定时 savestate 固化为 T06 持续 journal + T09 持久化重放存档契约。
- [x] 参考 CelesteTAS，把外部 Companion/Studio、Mod 自动启动、版本化 IPC 和可选 NativeHost 固化为 T12/T13。
- [x] 核对 Celeste/Speedrun Tool 的实际 savestate 路径，把可持久化的 room-entry 语义关键帧、显式适配器、短尾重放和 T09 自动回退固化为 T14。
- [x] 将外部状态观察、受控操作和 AI 辅助 TAS 固化为 T15：Companion broker、SDK/CLI、stdio MCP、控制租约、白名单 typed debug mutation、movie 分支和审计。
- [x] 将“自行创作、从诸神堂椅子到击败调谐假骑士、只以非视觉结构化接口辅助且计分运行 input-only”固化为 T16。
- [x] 增加人类可读的实施任务地图和 README 入口。
- [x] 代码实现已由后续用户指令另行授权；T01-T15 的适用门禁已按各自 Spec 验收，当前继续执行 T16。

## 5. Review Verdict

- Spec status: ACTIVE EXECUTION。
- Implementation status: T01-T13 VERIFIED（T07 为 `VERIFIED_LOCAL`）；T14 `DOWNGRADED_REPLAY_ONLY_VERIFIED`；T15 VERIFIED；T16 IN PROGRESS。
- Blocking questions: None。
- Blocking experiments: T12/T13/T15 最终矩阵已通过；当前只剩 T16 最终自编 TAS 五次冷运行。
- Restore acceleration blockers: T14 因 RNG coverage、零验证 gate 和未登记 Mod profile 降级为 ReplayOnly；只禁用关键帧加速，不阻塞 T09/T15/T16。
- External capability blockers: T13 只验证了 `native.process.observe.v1`；其余能力和进程 checkpoint 明确 `unsupported`，不阻塞纯 Runtime/T09/T14。
- Automation blockers: None；T15 已通过，控制面故障时的 `ReadOnly/Disabled` 降级仍保留。
- Final acceptance blocker: T16 依赖 T07/T09/T12/T15，且计分运行必须保持 input-only。
- Recommended next execution unit: T16 非视觉自编 TAS 最终验收。
