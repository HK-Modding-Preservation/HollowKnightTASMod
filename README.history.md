> 历史归档。仅按需检索，不作为当前进度或执行顺序。当前入口：[CURRENT.md](CURRENT.md).

# HollowKnightTASMod：Mod + Companion 实现 Hollow Knight TAS 的技术可行性

> 研究与实施状态：2026-08-08。T01-T13 与 T15 已通过各自历史离线/实机门禁；
> T07 为本机 `LOCAL_VERIFIED`、跨安装 `NOT_RUN`；T08 的旧
> `Controlled Step` 已因 T24 发现的零 timeScale 额外 Update 问题重新打开，
> T09 已以 80/80 次严格恢复通过，T10 已交付目标构建
> RNG state 与 partial call-site 诊断；T12/T13 已通过 Companion、IPC、
> 签名只读 NativeHost 和 10+10 原生/语义矩阵；T14 按 ReplayOnly 降级验证，
> 当前先执行 T24 无 Mod 逐 tick 等价硬门禁，T16 保持阻塞。

## 结论摘要

**建议按“游戏内 Runtime Mod + 可自动启动的外部 Companion/Studio + Automation/AI AgentBridge + 按能力启用的 NativeHost”立项。** Runtime 负责 HK 语义和确定性边界，Companion 负责编辑、编排、存档目录与外部能力托管；T15 以默认只读的语义接口服务调试客户端和 AI，只有纯 Mod 不能安全完成且实验通过的能力才进入 NativeHost。`[推断/建议]`

- **值得做**：Hollow Knight 动作层输入录制、文本 movie 与编辑器、逐 tick 观测、FSM/碰撞箱/敌人 HP 检查器、RNG 诊断、快进、支持任意时刻和定时创建的持久化重放存档、状态哈希、跨安装复放验证，以及供外部调试/AI 使用的版本化状态与受控操作接口。
- **不应预先承诺**：通用即时内存 savestate、任意 RAM 搜索/写入、全局系统时间/文件/线程/网络/Steam 虚拟化、可回滚的音频设备状态，以及“与 libTAS 同等底层确定性”。这些能力进入独立外部/原生门禁，只有逐项实验证明后才能标为支持。`[推断]`
- **可能超过 libTAS 的地方**：通用工具无法天然理解 Hollow Knight；专用 Mod 可以按 Hero、PlayMaker FSM、Collider、场景、敌人 HP 和路线断言呈现状态。`[建议]`
- **不能超过的地方**：libTAS 位于 Linux 进程/系统调用拦截层；纯托管 Mod 没有同等级的地址空间、线程、文件描述符与原生 API 控制面。[S3](https://clementgallet.github.io/libTAS/guides/how/) `[事实 + 推断]`

## 当前代码与本机构建

当前 solution 包含：

- `HollowKnightTAS.Core`：canonical JSON、SHA-256、manifest、input sample、tick ledger、HK-TAS Movie v1、Semantic Snapshot v1、录制/回放、验证/控制、Replay Save v1、RNG fingerprint/白名单/diff，以及 typed Inspector watch 契约。
- `HollowKnightTAS.Runtime`：`net472` Mod、输入/时间/状态探针、录制/回放、暂停/受控步进、始终开启的 journal、任意/定时持久化重放存档、目标构建 Unity RNG state/partial call-site 诊断，以及同源文本/Collider overlay 与 JSONL Inspector。
- `HollowKnightTAS.Cli`：离线 `manifest`、`movie` 与 `verification validate/compare/campaign`。
- `HollowKnightTAS.Core.Tests`：Core、CLI、输入、ledger、Movie、Semantic Snapshot、Playback、Verification、Control、Replay Save、RNG 与 Inspector 契约/鲁棒性测试；T11 正式构建为 129/129 通过。

首次构建先复制本地配置模板并填写本机路径；真实路径文件已被 Git 忽略：

```powershell
Copy-Item .\LocalBuildProperties.props.example .\LocalBuildProperties.props
# 编辑 HKManagedDir 与 HKModsDir
dotnet build .\HollowKnightTAS.sln -c Debug
dotnet test .\tests\HollowKnightTAS.Core.Tests\HollowKnightTAS.Core.Tests.csproj -c Debug
```

Debug build 默认安装到 `$(HKModsDir)\HollowKnightTAS`，同时生成 `HollowKnightTAS.zip` 和 `SHA256.txt`。只需编译、不安装时使用：

```powershell
dotnet build .\HollowKnightTAS.sln -c Debug /p:SkipHKTASInstall=true
```

验证游戏 session manifest：

```powershell
dotnet run --project .\src\HollowKnightTAS.Cli -- manifest validate <manifest-path>
```

真实构建与实机证据见 [T01](mydocs/evidence/T01_验收报告.md)、[T02](mydocs/evidence/T02_输入相位验收报告.md)、[T03](mydocs/evidence/T03_Tick账本验收报告.md)、[T04](mydocs/evidence/T04_Movie协议验收报告.md)、[T05](mydocs/evidence/T05_语义状态快照验收报告.md)、[T06](mydocs/evidence/T06_输入录制与回放验收报告.md)、[T07](mydocs/evidence/T07_冷启动确定性与Desync验收报告.md)及 [T08 验收报告](mydocs/evidence/T08_暂停与受控步进验收报告.md)。正式协议见 [HK-TAS Movie v1](docs/protocol/HK-TAS-Movie-v1.md)和 [Semantic Snapshot v1](docs/protocol/Semantic-Snapshot-v1.md)。

## 阅读范围、证据等级与版本边界

本文所谓“**纯 Mod**”是指：通过 Hollow Knight Modding API 加载的托管 DLL，可使用 Modding API、MonoMod/On/IL hook、反射，以及与独立 Companion 的通信；但**不**替换 `Assembly-CSharp.dll`、不注入原生 DLL、也不使用外部进程快照器。最终产品允许超出这个研究子范围，但每个外部/原生能力必须由单独的 capability、manifest、版本白名单与验收报告管理。`[范围定义]`

截至 **2026-07-27**，Modding API 的 v77 发布页仍标明其面向 Hollow Knight `1.5.78.11833`，并给出 Windows/macOS/Linux 校验和。[S1](https://github.com/hk-modding/api/releases/tag/1.5.78.11833-77) `[事实]` 这不表示任何 1.5.x 构建都可无改动支持；未来实现必须把**游戏构建指纹、Modding API、Mod 列表与设置**写入 movie manifest。`[建议]`

| 标记 | 含义 | 处理方式 |
|---|---|---|
| `[事实]` | 来源直接可核对的描述或源代码行为 | 在相邻句子给出链接。 |
| `[推断]` | 根据来源和 Unity/Mod 运行时边界得出的工程判断 | 不当作官方保证；必须由原型验证。 |
| `[建议]` | 供后续设计取舍的方案 | 可因 Phase 0 的证据而调整。 |
| `[范围定义]` | 本仓库为研究设定的边界 | 不应误读为第三方规范。 |

14 个上一轮调研使用的原始 URL 和本轮 16 个 CelesteTAS/Speedrun Tool/Unity/Windows/MCP 补充来源均保留在[来源与证据索引](docs/来源与证据索引.md)；该索引也记录每条来源能证明什么、不能证明什么。

## 目标与边界

### 建议的产品定义

**HK-TAS Runtime + Companion/Studio + Automation/AgentBridge + 可选 NativeHost**：

- Runtime Mod 负责受控输入、tick 账本、状态观测、游戏内存档入口和确定性验证。
- 外部 Companion 承载 Studio、movie 编辑、标记/diff、存档目录、日志和外部能力编排；Runtime 可在初始化完成后自动启动它。
- T15 Automation broker 向 SDK/CLI 与本地 stdio MCP AgentBridge 提供稳定语义状态和 typed control，使外部调试器或 AI 可以观察、提议 movie patch、验证并在授权租约内执行。
- NativeHost 只承载已经证明“纯 Mod 做不到或会污染确定性”的 Windows 进程级能力，并由 Companion 按 capability 启动；它不是默认依赖。

Runtime 与 Companion 使用版本化本地 IPC，Runtime 只在 Unity 主线程安全点消费已验证命令。`[建议]`

### 非目标

- 不把原版游戏存档或持久化重放存档描述为进程内存快照。
- 不开放任意 C#、任意反射写、原始地址读写、路径访问、进程启动或网络请求给 movie/脚本；**仅 Runtime 自身的固定、签名验证过的 Companion 启动器可以创建外部进程**。
- 不把自然语言直接当作 Runtime 命令，也不让 AI 绕过 typed schema、用户策略、控制租约、安全 tick、movie 分支或审计。
- 不以“某个旧版工具能替换 DLL”为依据，承诺当前 1.5.x 也可行。
- 不把“同一机器的一次回放成功”表述为确定性证明。`[建议]`

历史上的 HollowKnightTasInfo 是 Linux + libTAS 工具链，并会覆盖游戏的 `Assembly-CSharp.dll`；其 README 还明确说更侵入的功能不能和未修改游戏同步。[S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]` 它是 T-FT、RNG 日志和 HK 专用 OSD 的有价值先例，不是本项目的加载方式。`[推断]`

## libTAS 能力拆分与分层实现关系

libTAS 通过 `LD_PRELOAD`、函数 hook 与 Unix socket，在进程/系统调用层干预输入、时间、文件、线程、状态保存和编码；其文档将呈现调用视为重要帧边界。[S3](https://clementgallet.github.io/libTAS/guides/how/) `[事实]`

下表中的“可实现”指在**限定的 HK 动作/语义层**可以实现；“部分可实现”表示有明确缺口，必须在 movie 中声明；“外部候选”表示进入 T13 单独实验，不代表已经可用。

| 能力 | libTAS 侧能力 | 分层结论 | 对应实现与边界 |
|---|---|---|---|
| 暂停与逐帧 | 以呈现/输入边界控制推进 | **部分可实现** | 可用暂停、输入 gate 与受控恢复做“游戏 tick 步进”；不能默认等同于 libTAS 的呈现级帧步进。 |
| 输入录制、编辑、回放 | movie、键鼠/手柄输入和编辑器 | **可实现** | 在 HK 动作层录制/替代输入；文本、RLE、持有态、标记与分支编辑可做得更符合游戏语义。 |
| 时间虚拟化 | 拦截时间查询、等待与相关系统调用 | **Mod 部分可实现；外部候选** | Runtime 控制 Unity 缩放时间和已知 hook；未知原生时间源只能由 T13 逐调用点验证，不能宣称全局覆盖。 |
| RNG | 进程层随机源控制；HK 历史工具记录/播放调用 | **部分可实现** | 记录 `UnityEngine.Random` 状态和白名单调用点；未知 `System.Random` 实例、原生源、其他 Mod 仍可漏掉。 |
| Savestate | 保存进程地址空间、线程和部分 I/O 状态 | **持久化重放可实现；语义关键帧按房间适配；进程快照为外部实验** | T09 保证任意/定时保存与跨启动重建；T14 用 room-entry 关键帧 + 短尾重放做纯 Mod 加速；T13 才研究同进程进程级快照。两种加速失败都必须回退 T09。 |
| 快进 | 降低/跳过呈现并保持进程级控制 | **部分可实现** | 可提高受控推进、降低可选呈现/叠层；加载、物理追赶和音频必须实测，不可假定不影响同步。 |
| 视频/音频录制 | 截帧、内部混音、编码 | **Mod 部分可实现；外部候选** | Runtime 可捕捉相机/RenderTexture；编码、磁盘写入和可选系统捕获由 Companion/NativeHost 隔离，仍需逐 tick 同步验收。 |
| RAM watch/search | 原始地址、搜索、指针链、脚本读写 | **产品不开放通用写；外部仅诊断** | 优先提供 `hero.pos`、`enemy.hp` 等语义 watch；NativeHost 只允许构建白名单内的内部诊断，不向 movie 暴露任意地址。 |
| Lua/脚本 | Lua 可访问输入、GUI、内存、savestate 等 | **可实现（受限）** | 可用受限 DSL 或脚本 API；只允许已审核的输入、断言、相机和捕捉命令。 |
| 文件/网络/Steam/线程 | 对多类环境 API 的进程级拦截 | **纯 Mod 无法等价；外部逐项门禁** | Runtime 只控制自己和已知游戏调用点；T13 可以逐项实验，但不以单个成功 hook 推导“环境已完全虚拟化”。 |
| 确定性验证 | 降低环境变量、记录 movie | **可实现（验证）** | 建立状态哈希、冷启动重放、manifest 和 desync 报告；不能证明未观测原生状态已经受控。 |

libTAS 自身也把线程视为严重且不能完全解决的非确定性来源，并为 Unity 提供专门支持说明。[S3](https://clementgallet.github.io/libTAS/guides/how/) [S4](https://clementgallet.github.io/libTAS/guides/unity/) `[事实]` 因此上表的“可实现”不等于无条件确定性。`[推断]`

## Hollow Knight / Unity 运行时约束

### 帧、物理步和 T-FT

Unity 的可变帧循环与固定时间步是两套系统：一个视觉帧可有零次、一次或多次固定物理步，取决于帧率和追赶情况。[S6](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html) `[事实]` HollowKnightTasInfo 把 `Time.time - Time.fixedTime` 作为 **T-FT** 展示，并说明其与多种细微行为有关。[S7](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/TimeInfo.cs) [S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]`

T03 的 20 次独立进程正式矩阵进一步实测确认：相邻 visual boundary 之间确实可能有 0、1 或多个 fixed step；P30/PLOAD 会发生 fixed catch-up，场景退出/重载时也会出现更高追赶数。四个 profile 的主 phase 顺序均稳定为 `VisualUpdateBegin > InControlCommitted > HeroUpdateBeforeOriginal > LateUpdateEnd`。因此 v1 movie 已锁定：

- `inputTick`：一次已提交的 InControl update tick，是 movie sample 的唯一消费单位；
- `visualTick`：观测/呈现边界，只作账本坐标；
- `fixedTickCount`：该 visual tick 覆盖的固定步数量，允许 0/N；
- `inputPhase`：输入被采样/替代的实际相位；
- `tft`：保留十进制与 exact bits 的诊断字段，不单独作为同步 oracle；
- `sceneEvent`、随机调用计数和 milestone hash。`[建议]`

### 暂停、协程、场景与物理

`timeScale = 0` 时，Unity 2017.4 文档说明 `FixedUpdate` 不会调用；但它只是时间缩放，不能等同于冻结整个进程。[S8](https://docs.unity3d.com/es/2017.4/ScriptReference/Time-timeScale.html) `[事实 + 推断]` Unity 的一般时间文档也说明 `Update` 仍可被调用、未缩放时间可继续变化。[S6](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html) `[事实]`

- `WaitForSeconds` 使用缩放时间并在后续帧恢复；协程迭代器/闭包执行点不是普通字段快照可可靠复原的对象。[S9](https://docs.unity3d.com/ru/2017.4/ScriptReference/WaitForSeconds.html) `[事实 + 推断]`
- 异步场景加载在完成前存在生命周期与对象创建顺序敏感性；验证必须把加载开始/结束纳入账本。[S10](https://docs.unity3d.com/ja/current/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html) `[事实 + 建议]`
- `Rigidbody2D` 速度、接触、transform/physics 同步、Animator、PlayMaker FSM 当前 state/变量/pending event、静态字段、单例、对象池和其他 Mod hook 都可能分叉。`[推断]`

**实现前必须探针，不可猜测**：实际输入消费点、是否先于某个 `ListenFor*` FSM action、Tick 边界、目标构建里可恢复的状态集。HK Modding 的输入约束建议优先读原生动作链（`InputHandler.Instance?.inputActions` / `HeroActions`），而不是将 `UnityEngine.Input.GetAxisRaw()` 当作通用 gameplay 注入点。`[建议；本仓库的 Modding 规范]`

## 推荐架构

```text
HollowKnightTAS Runtime Mod
  ├─ Input / Tick / Inspector / Determinism / Replay savestate
  ├─ 可选 Semantic keyframe / short-tail restore accelerator
  ├─ 游戏内最小控制与失败降级
  └─ 固定路径 + 签名/哈希验证的自动启动器
                         │
                         ▼
HollowKnightTAS Companion / Studio（外部 .NET 进程）
  ├─ movie editor / timeline / diff / 存档目录
  ├─ 当前用户 ACL 的本地命名管道；非阻塞 command queue
  ├─ automation broker ── SDK / CLI
  │                       └─ AgentBridge（可选 stdio MCP）
  └─ native capability broker
                          │  仅在 capability 被启用且版本匹配时
                          ▼
 HollowKnightTAS NativeHost（可选 Windows x64 用户态进程）
   └─ 原生时间/捕获/进程快照实验；逐项 PASS，失败回退 Runtime
```

| 组件 | 责任 | 关键约束 |
|---|---|---|
| Runtime | Mod 生命周期、主线程安全点、执行队列和 Companion 启动器 | 不在 Unity 主线程阻塞等待外部进程；外部工具失败不阻止游戏内核心功能。 |
| Input substitute | 动作层录制/回放/注入 | 先通过 Phase 0 确认消费点；记录持有态和边沿。 |
| Tick ledger | 跨 visual/fixed 时间轴记录 | 所有时间字段有协议版本，绝不把“帧”留作歧义术语。 |
| Inspector | Hero、场景、FSM、Collider、敌人、RNG 的稳定语义路径 | 不把跨运行不稳定的 Unity instance ID 当作哈希主键。 |
| Determinism guard | 游戏/DLL/Mod/设置/存档 hash，随机与加载账本 | manifest 不匹配或出现未声明 Mod 时拒绝“验证模式”。 |
| Replay savestate | baseline bundle + 连续输入日志 + cursor + 里程碑 hash | 任意/定时持久化；下次启动自动重建并校验，不承诺瞬时进程 rewind。 |
| Semantic keyframe accelerator | 安全 room-entry 关键帧 + 显式 adapter manifest + journal tail | 只加速 T09；缺适配器、不兼容或 hash 失配时干净重载并回退完整重放。 |
| Companion/Studio | movie 编辑、标记、diff、日志、路线可视化、外部能力编排 | 随 Mod 发布；默认可自动启动；只能执行固定 manifest 中的程序，movie 无进程启动权。 |
| Automation/AgentBridge | 稳定状态 DTO、capability discovery、SDK/CLI、stdio MCP、typed control 和 AI movie 分支 | 默认只读；控制需用户批准、单一短期租约、safe tick 和审计；无任意路径/内存/反射/进程接口。 |
| NativeHost | 纯 Mod 无法覆盖的 Windows 用户态能力 | 默认关闭、无内核驱动、精确构建白名单；每个 capability 有独立验收和关闭开关。 |

Movie 内建议的**白名单命令**是 `input`、`frames`、`marker`、`checkpoint`、`assert`、`camera`、`capture`；T15 外部自动化另使用版本化 typed commands。两者都必须在 manifest/capability 允许的上下文内执行，不能接受任意脚本。`[建议]`

外部工具是一个明确的发布产物例外，而不是普通内嵌资源：Release 包固定包含 `Companion/win-x64/HollowKnightTAS.Companion.exe` 和签名 manifest；Runtime 不遍历目录、不读取 movie 给出的路径，也不在 v1 自动下载可执行文件。启动前校验签名、SHA-256、RID 和 IPC 协议范围，已有兼容实例则连接而不重复启动；缺失、篡改、超时或崩溃时进入 `CompanionUnavailable`，保留游戏内录制、重放和 T09 存档入口。`[建议]`

## 确定性、输入、逐帧与状态策略

### 确定性与 movie 格式

movie 头部应包含：协议版本、游戏与关键 DLL hash、Mod manifest、Mod 设置 hash、初始存档/基线 hash、目标平台和实验 profile。正文使用 RLE/持有态输入和白名单命令；里程碑记录排序后的语义 hash、场景、tick 账本摘要和 RNG 计数。`[建议]`

状态哈希应基于稳定的语义键，例如 `hero.position`、`hero.velocity`、可控 PlayerData 子集、场景、指定 FSM state、登记敌人的 HP/位置，而不是 Unity instance ID。`[建议]` 真正的通过标准是**干净 profile 冷启动后重复回放全部 milestone 一致**；跨独立安装再验证一次可显著提高证据强度。[S14](https://tasvideos.org/Glossary) `[建议，借鉴 TAS 的同步验证概念]`

### 输入录制/回放与近似逐帧

- 录制的是 HK action 层的“持有/按下/释放”和选择性模拟输入，不是 OS 扫描码。`[建议]`
- 单步按钮只请求推进到下一个**已定义 tick 边界**，并把实际产生的 visual/fixed 账本写回日志。`[建议]`
- 回放器应先以“观察模式”比对 hash，再允许“验证模式”；出现首个差异时输出最近的输入、T-FT、FSM、RNG、加载事件。`[建议]`

### Savestate、存档与检查点

本项目把用户可见的 `savestate` 定义为**持久化重放存档**：

- 使用者可以在任意时刻发出保存请求；Runtime 在下一个已定义的安全 tick 固化该点，并同时记录“请求时刻”和“实际生效 tick”。
- TAS 模式从 baseline 建立后始终维护连续输入日志，因此不要求使用者预先手动开始录制才能保存。
- 支持按有效 movie tick 定时保存；暂停期间 movie cursor 不推进，因此不会反复产生同一个存档。
- 下次启动时可以列出并选择任意手动或自动存档。系统加载其 baseline、自动重放前缀并校验目标 hash，成功后停在该点等待继续可见重放。
- 手动存档不自动删除；自动存档使用可配置的保留数量和原子写入。
- T14 可把目标点关联到最近的兼容 room-entry 语义关键帧，只重放该入口到目标点的短尾；关键帧不是存档真相源，任何失败都自动回到上述 baseline 全量重放。

原版 `GameManager.SaveGame` 主要持久化 `PlayerData + SceneData`，不能恢复当前 Hero transform、物理接触、FSM、协程和其他瞬时对象，所以它只能作为 baseline 来源，不能单独满足上述 savestate 契约。`[事实 + 推断；目标构建源码需在实现时再次核对]`

| 方案 | 适用性 | 可恢复内容 | 不能保证的内容 |
|---|---|---|---|
| 持久化重放存档 | **核心需求** | 任意/定时 checkpoint、baseline、输入前缀、cursor、目标 hash；可跨启动选择 | 恢复需要重载并重放前缀，耗时取决于路线长度和安全快进。 |
| 语义关键帧 + 短尾重放 | T14 可选、按房间适配 | room-entry 持久状态、Hero/RNG/账本白名单状态，再重放到任意目标点 | 动态对象、协程、对象池、接触、未登记 Mod 状态；失败必须回退 T09。 |
| 反射全对象图 | 不建议作为产品承诺 | 理论上的部分托管字段 | Unity native object、循环引用、delegate、迭代器、实例身份和创建顺序。 |
| 进程级快照 | T13 外部实验 | 地址空间、线程、寄存器、部分 I/O 的候选采集 | Windows PSS 面向捕获/诊断且没有文档化的“恢复整个目标进程”操作；可恢复性、GPU/音频和跨启动持久化均需另证。[S18](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/proc_snap/overview-of-process-snapshotting) |

Modding API 的 Mod 设置/保存 hook 表明 Mod 可以持久化自己的配置，但这不能推出“整个游戏运行时已经被保存”。[S11](https://github.com/hk-modding/api/blob/master/Assembly-CSharp/Mod.cs) `[事实 + 推断]` 游戏存档本身也不等于当前场景的一切 transient 状态。`[推断]`

### RNG、渲染与音频

HollowKnightTasInfo 的历史实现会按场景记录并回放 RNG 调用，且维护了针对不同构建的随机数注入路径。[S13](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/RandomInjection.cs) [S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]` 这支持“RNG 工具很有价值”，但也说明 hook 点版本敏感；新 Mod 应先记录/诊断，后在**逐构建白名单**上选择性干预。`[推断/建议]`

渲染可先做 overlay、hitbox 与可选相机/RenderTexture 捕获；音频缓冲、设备时钟与编码同步不应进入第一阶段确定性或回滚承诺。`[建议]` 快进应默认记录“何时启用、呈现/音频做了什么降级”，并用同一条路线的正常速度回放验证是否影响 hash。`[建议]`

### 调试与可视化

专用 Inspector 是本项目最有机会高于通用 TAS 工具的部分：

- `hero.pos`、`hero.velocity`、`hero.state`、`scene`；
- `fsm[stable-path].state`、变量和最后事件；
- `enemy[stable-id].hp`、位置、速度；
- `collider[stable-path]`、命中箱/触发器；
- tick/T-FT、RNG 调用计数、场景加载和 milestone hash。`[建议]`

HollowKnightTasInfo 已展示 T-FT、RNG、敌人 HP/位置/速度和多类 hitbox 的 OSD 价值。[S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]` 未来实现应把日志作为一等调试接口，并在发生 desync 时输出可比较的结构化快照。`[建议]`

T15 将上述 Inspector/ledger/desync 数据封装为带 `session + manifest + tick + freshness + semantic hash` 的外部资源，并提供暂停、单步、运行到条件、录制/回放、T09 存档和 movie 分支的受控命令。调试态只允许白名单的 typed pose/resource writer，在暂停安全点、compare-and-set 和完整审计下执行；成功写入会把运行标记为 `NonVerifiableDebugMutation`，不能进入 T07/T16。SDK/CLI 与 AI/MCP 客户端共享同一权限和审计模型；AI 不获得隐藏入口。`[建议]`

## 与 CelesteTAS 等路线的对照

CelesteTAS 的官方仓库把游戏内 Mod、独立 Studio 和共享 `StudioCommunication` 放在同一产品中；README 说明 Studio 会随工具安装，并提供 `Launch Studio at Boot` 设置。[S12](https://github.com/EverestAPI/CelesteTAS-EverestInterop) [S15](https://raw.githubusercontent.com/EverestAPI/CelesteTAS-EverestInterop/master/README.md) `[事实]` 其 `StudioHelper` 还实现了版本检查、下载包 checksum、临时目录安装、已有实例检测和按平台启动；通信层使用协议版本、心跳、mutex 与 memory-mapped files。[S16](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/CelesteTAS-EverestInterop/Source/EverestInterop/StudioHelper.cs) [S17](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/StudioCommunication/CommunicationAdapterBase.cs) `[事实]`

CelesteTAS 本身的 `SavestateManager` 保存 TAS controller clone/checksum/slot，并把实际 save/load 委托给 Speedrun Tool。[S20](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/f3f848473463e66d38b453b3f04215ba71dbc0c9/CelesteTAS-EverestInterop/Source/Playback/SavestateManager.cs) Speedrun Tool 会深拷贝 `Level`、`SaveData` 和 transition iterator，并用显式动作恢复静态状态、RNG、输入、音频及其他 Mod；克隆器对 Scene、图形/FMOD 资源、弱引用等设有特殊规则，并会拒绝 Lua/过场等高失步风险状态。[S21](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/StateManager.cs) [S22](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/SaveLoadAction.cs) [S23](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/Implements/DeepClonerUtils.cs) [S24](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/Implements/DesyncRiskAnalyzer.cs) `[事实]` 这是一套**同进程、游戏专用的托管对象图 + 恢复适配器**方案，不是可跨启动的完整进程快照。`[推断]`

本项目借鉴的是“Mod + 专用编辑器 + 共享协议 + 可随游戏启动”的分层，不复制其具体传输和更新决策：HK v1 采用当前用户 ACL 的 named pipe、离线随包 Companion、签名/SHA-256 校验、无 shell 启动和显式失败降级。`[建议]`

| 维度 | HollowKnightTASMod 建议 | CelesteTAS 的可借鉴处 | 不应照搬之处 |
|---|---|---|---|
| 产品分层 | Runtime Mod + 自动启动 Companion/Studio + 可选 NativeHost | 游戏 Mod 与专用 Studio 一体发布、可在游戏启动时拉起。[S15](https://raw.githubusercontent.com/EverestAPI/CelesteTAS-EverestInterop/master/README.md) | 不把 Studio 在线当作 Runtime 正确性的前提。 |
| 编辑体验 | 文本 DSL + Studio + 时间线/标记/diff | 专用 Studio 与 TAS 编写工作流。[S12](https://github.com/EverestAPI/CelesteTAS-EverestInterop) | 不据此推定 HK 的输入与时间边界相同。 |
| 通信 | named pipe + ACL/token + bounded queue | 独立共享协议、版本号、心跳和断线检测。[S17](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/StudioCommunication/CommunicationAdapterBase.cs) | 不直接复制全局共享内存名和单一实例假设。 |
| 游戏状态 | FSM、Collider、敌人、场景与 T-FT 为一等数据 | 游戏专用工具优于通用按键界面 | HK 的 PlayMaker/物理/加载需独立适配。 |
| 确定性 | movie manifest + 冷重放 + hash | 可重复 movie 的工作流 | 不能把另一游戏的同步性质迁移为 HK 事实。 |
| 快照 | T09 基线重放为保证；T14 语义关键帧为纯 Mod 加速；T13 进程级实验更后置 | CelesteTAS + Speedrun Tool 展示了游戏专用 deep clone、显式恢复 action 与不安全状态拒绝。[S20](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/f3f848473463e66d38b453b3f04215ba71dbc0c9/CelesteTAS-EverestInterop/Source/Playback/SavestateManager.cs) [S21](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/StateManager.cs) | 不复制全对象图，也不把同进程引用缓存当作跨启动存档。 |

## 分阶段 MVP 路线

| 阶段 | 交付目标 | 退出条件 |
|---|---|---|
| **0：探针** | 锁定一个目标构建；找到输入消费边界；记录 visual/fixed/T-FT、场景事件和最小状态 hash | 干净 profile 下同一短路线多次冷回放的账本可解释。 |
| **1：MVP** | action 输入录制/编辑/回放、暂停/近似单步、文本 movie、基础 Inspector、milestone hash | 目标路线在同一安装上重复冷回放全部 hash 一致。 |
| **2：实用工具** | 任意/定时持久化重放存档、自动启动的 Companion/Studio、FSM/hitbox overlay、RNG 日志、安全快进、分场景日志 | 任意 checkpoint 跨启动恢复；Companion 安装/校验/单实例/断线回归通过；外部进程不改变验证 hash。 |
| **3：T14 语义关键帧加速** | 在安全 room-entry 捕获版本化关键帧，对固定路线/房间登记状态和恢复适配器，再短尾重放到目标点 | 每种受支持场景的 full replay/keyframe restore 各 10 次一致，后续 600 tick 一致，所有失败可回退 T09。 |
| **4：外部/原生能力线** | 开发 NativeHost 和 capability broker；逐项实现纯 Mod 不能覆盖的时间、捕获、进程观测或即时快照能力 | 每项 capability 有目标构建白名单、权限边界、T07 对照、崩溃隔离与自动回退；未通过项保持 `unsupported`。 |
| **5：外部自动化与 AI** | 交付 T15 Automation API、SDK/CLI、stdio MCP、控制租约、movie proposal 分支和审计 | 三种访问模式、SDK/CLI/MCP parity、scripted AI loop、越权/并发/断线/模糊输入安全矩阵全部通过。 |
| **6：最终自编 TAS** | 仅通过结构化非视觉状态与 typed control 自行编写 movie，从诸神堂椅子进入调谐假骑士并击败 | 干净冷启动计分运行 5/5 通过，且无状态写入、teleport、强制场景、视觉识别或人工中途输入。 |

MVP 明确排除：即时全局内存 savestate、通用 RAM search、通用环境虚拟化和逐样本音频录制；这些不是永久拒绝，而是不得绕过 T13 独立证据门禁。T09 的 baseline + replay 持久化重放存档始终是跨启动保证和 NativeHost 失败时的回退。`[建议]`

## 实施规格与硬门禁

2026-07-29 已把上述阶段拆成 16 个可独立验收的任务。完整的当前状态评估、依赖图、Go/No-Go 规则见[实施可行性与任务地图](docs/实施可行性与任务地图.md)，逐任务契约见[任务 Spec 索引](mydocs/specs/tasks/README.md)。

最先执行的顺序是：

1. T01 已建立可重现构建、环境 manifest 和结构化证据流。
2. T02-T11 已完成输入、时间、协议、快照、录制回放、确定性、步进、任意/定时存档、RNG 与 Inspector 门禁；T12 已完成 Companion/Studio、IPC 与 60 分钟耐久；T13 已完成签名只读 NativeHost、10+10 逐 run 原生/语义对照；T15 已完成外部 Automation/AI 接口，当前执行 T16。
3. T07 已达到 `LOCAL_VERIFIED`，只允许对锁定的本机 manifest/baseline/movie/oracle 使用“本机已验证重放”；跨安装仍为 `NOT_RUN`。
4. T09 已支持任意/定时持久化重放存档，并以 5 个手动点、3 个自动点各 10 次严格 hash 一致通过；暂停、步进和恢复快进仍只有在不改变 T07/T09 hash 时才能进入验证模式。
5. T10 已在锁定构建上记录 `Random.State` 与两个严格白名单调用点；受控 5-run 收敛且 NOHOOK 语义等价，但 60-tick 环境端点明确观察到白名单外 Unity RNG，因此 coverage 固定为 partial。
6. T14 已按安全门禁降级验证为 ReplayOnly；零个 RoomEntry adapter 被伪报为已支持，恢复继续回退 T09。
7. T12 已验证 Companion/Studio、版本化 IPC 和 Mod 自动启动；T13 只验证只读 `native.process.observe.v1`，其余原生能力与 process checkpoint 保持 unsupported 并回退 Runtime/T09。
8. T15 在 T10/T12 后提供外部状态与控制；默认只读，SDK/CLI/MCP 必须经过同一 broker、租约、safe tick 和审计。
9. T16 是最终完成门禁：由本项目结构化非视觉接口辅助、自行创作 movie，并从诸神堂椅子真实冷启动到击败调谐假骑士，连续 5 次通过。

## 主要风险与验证实验

| 风险 | 最小实验 | Go / No-Go 信号 |
|---|---|---|
| 输入相位错误 | 同一动作在候选 hook 相位逐一录制/回放 | **Go**：输入边沿和结果稳定；**No-Go**：无可重复消费边界。 |
| visual/fixed 分叉 | 记录 T-FT、每 visual tick 的 fixed count，施加高/低负载 | **Go**：差异可预测并纳入 ledger；**No-Go**：同输入无解释分叉。 |
| RNG 失步 | 对目标房间记录调用计数/状态，比较多次冷回放 | **Go**：受控白名单轨迹收敛、hook-off 不改变语义，并明确报告覆盖缺口；**No-Go**：错误构建仍 hook、诊断改变语义或把 partial 冒充完整控制。 |
| 持久化重放存档 | 在任意 tick 手动保存、按间隔自动保存，并跨进程选择恢复 | **Go**：目标点重建后 hash 一致且目录原子可恢复；**No-Go**：连续日志缺口或任意 checkpoint 无法收敛。 |
| 语义关键帧失配 | 对同一存档分别做 T09 full replay 与 T14 keyframe + tail，并注入损坏/缺 adapter | **Go**：目标和后续 600 tick 一致，失败自动重载并回退；**No-Go**：部分应用后继续、修改 expected hash 或破坏 T09。 |
| 其他 Mod 干扰 | 仅 API+TAS Mod 的干净 profile，与逐个加入其他 Mod 比较 | **Go**：manifest 能识别并隔离差异；**No-Go**：未知状态无法归因。 |
| 快进副作用 | 同路线上正常速度与快进后 hash 对照 | **Go**：关键 hash 一致；**No-Go**：快进改变可观察状态。 |
| Companion 启动与篡改 | 缺失/改名/改 hash/版本不匹配/已有实例/连续崩溃矩阵 | **Go**：只启动签名 manifest 的固定路径，单实例且失败可降级；**No-Go**：movie 可控路径、启动风暴或游戏被外部故障拖死。 |
| NativeHost 污染确定性 | 同一 oracle 分别关闭/启用单一 capability | **Go**：权限最小、hash 一致、崩溃可回退；**No-Go**：未知写入、线程悬挂、跨构建误启用或无法恢复 Runtime。 |
| 外部/AI 控制越权或竞态 | 两客户端抢租约、过期/断线、错 session/tick、任意路径/命令、SDK/CLI/MCP parity | **Go**：默认只读、单写租约、typed fail-closed、审计完整且 T07 hash 一致；**No-Go**：AI 有隐藏权限、两个写者或可绕过安全点。 |

完整的可执行实验顺序、数据字段和结果模板见[验证实验清单](docs/验证实验清单.md)。任何“帧精确”“可验证”的对外说法，都应等到 Phase 0/1 的上述证据完成后才使用。`[建议]`

## 参考来源

完整、带用途说明的清单在[来源与证据索引](docs/来源与证据索引.md)。本页保留所有关键链接的就近引用；来源均于 **2026-07-27** 访问或自上一轮完整调研产物逐条保留。

- HK Modding API：[发布 v77](https://github.com/hk-modding/api/releases/tag/1.5.78.11833-77)、[源码](https://github.com/hk-modding/api)
- libTAS：[工作机制](https://clementgallet.github.io/libTAS/guides/how/)、[Unity 支持](https://clementgallet.github.io/libTAS/guides/unity/)
- HollowKnightTasInfo：[README](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md)、[RandomInjection](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/RandomInjection.cs)、[TimeInfo](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/TimeInfo.cs)
- Unity：[时间与帧管理](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html)、[`Time.timeScale`](https://docs.unity3d.com/es/2017.4/ScriptReference/Time-timeScale.html)、[`WaitForSeconds`](https://docs.unity3d.com/ru/2017.4/ScriptReference/WaitForSeconds.html)、[`LoadSceneAsync`](https://docs.unity3d.com/ja/current/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html)、[managed/native object 边界](https://docs.unity3d.com/2022.2/Documentation/Manual/overview-of-dot-net-in-unity.html)、[serialization 限制](https://docs.unity3d.com/2018.4/Documentation/Manual/script-Serialization.html)
- 相关路线：[CelesteTAS-EverestInterop](https://github.com/EverestAPI/CelesteTAS-EverestInterop)、[SavestateManager](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/f3f848473463e66d38b453b3f04215ba71dbc0c9/CelesteTAS-EverestInterop/Source/Playback/SavestateManager.cs)、[Speedrun Tool StateManager](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/StateManager.cs)、[SaveLoadAction](https://github.com/DemoJameson/Celeste.SpeedrunTool/blob/6b1765655c52522cacfc8e8cfc572a5406e01f1c/SpeedrunTool/Source/SaveLoad/SaveLoadAction.cs)、[StudioHelper](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/CelesteTAS-EverestInterop/Source/EverestInterop/StudioHelper.cs)、[StudioCommunication](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/StudioCommunication/CommunicationAdapterBase.cs)、[TASVideos Glossary](https://tasvideos.org/Glossary)
- Windows 外部能力：[Process Snapshotting](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/proc_snap/overview-of-process-snapshotting)、[`PSS_CAPTURE_FLAGS`](https://learn.microsoft.com/en-us/windows/win32/api/processsnapshot/ne-processsnapshot-pss_capture_flags)、[`ProcessStartInfo.UseShellExecute`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=netframework-4.8.1)
- 外部 AI 接口：[MCP Server primitives](https://modelcontextprotocol.io/specification/2025-11-25/server/index)、[transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)、[tools/schema](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)、[security best practices](https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices)

---

T01 Foundation、T02 G0、T03 G1、T04 Offline Protocol、T05 Semantic
State、T06 Playback MVP、T07 本机确定性门禁、T08 Pause/Controlled
Step、T09 Persistent Replay Save、T10 RNG Diagnostics 与 T11 Semantic
Inspector 均已通过。
T09 的 8 个存档点共 80/80 次恢复得到原始 semantic SHA-256 完全一致；
T10 的 5 次受控 MATCH、NOHOOK parity 和 deliberate divergence 通过，
同时保留 `coverage=partial`；T11 10/10 功能加载通过，默认 120-tick
profile 的 10 分钟采样 p95 为 0.1874 ms、归因分配为 959.12 B/frame。
能力仍限定为锁定本机 manifest，不外推为跨安装验证。下一实现单元是
T12 Companion/Studio、本地 IPC 与 Mod 自动启动。
