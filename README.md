# HollowKnightTASMod：以 Mod 实现 Hollow Knight TAS 的技术可行性

> 研究状态：2026-07-27。此仓库目前只保存技术调研与验证计划，**不包含 Mod 实现代码**。

## 结论摘要

**建议立项，但目标应是“面向 Hollow Knight 的语义级、可验证输入重放 TAS 工具”，而不是宣称纯游戏内 Mod 能完整替代 libTAS 的进程级能力。** `[推断/建议]`

- **值得做**：Hollow Knight 动作层输入录制、文本 movie 与编辑器、逐 tick 观测、FSM/碰撞箱/敌人 HP 检查器、RNG 诊断、快进、输入重放检查点、状态哈希与跨安装复放验证。
- **不应承诺**：通用即时 savestate、任意 RAM 搜索/写入、全局系统时间/文件/线程/网络/Steam 虚拟化、可回滚的音频设备状态，以及“与 libTAS 同等底层确定性”。`[推断]`
- **可能超过 libTAS 的地方**：通用工具无法天然理解 Hollow Knight；专用 Mod 可以按 Hero、PlayMaker FSM、Collider、场景、敌人 HP 和路线断言呈现状态。`[建议]`
- **不能超过的地方**：libTAS 位于 Linux 进程/系统调用拦截层；纯托管 Mod 没有同等级的地址空间、线程、文件描述符与原生 API 控制面。[S3](https://clementgallet.github.io/libTAS/guides/how/) `[事实 + 推断]`

## 阅读范围、证据等级与版本边界

本文所谓“**纯 Mod**”是指：通过 Hollow Knight Modding API 加载的托管 DLL，可使用 Modding API、MonoMod/On/IL hook、反射，以及与独立 Studio 的通信；但**不**替换 `Assembly-CSharp.dll`、不注入原生 DLL、也不使用外部进程快照器。这个范围是本研究的架构约束，不是 API 的官方定义。`[范围定义]`

截至 **2026-07-27**，Modding API 的 v77 发布页仍标明其面向 Hollow Knight `1.5.78.11833`，并给出 Windows/macOS/Linux 校验和。[S1](https://github.com/hk-modding/api/releases/tag/1.5.78.11833-77) `[事实]` 这不表示任何 1.5.x 构建都可无改动支持；未来实现必须把**游戏构建指纹、Modding API、Mod 列表与设置**写入 movie manifest。`[建议]`

| 标记 | 含义 | 处理方式 |
|---|---|---|
| `[事实]` | 来源直接可核对的描述或源代码行为 | 在相邻句子给出链接。 |
| `[推断]` | 根据来源和 Unity/Mod 运行时边界得出的工程判断 | 不当作官方保证；必须由原型验证。 |
| `[建议]` | 供后续设计取舍的方案 | 可因 Phase 0 的证据而调整。 |
| `[范围定义]` | 本仓库为研究设定的边界 | 不应误读为第三方规范。 |

14 个上一轮调研使用的原始 URL 均保留在[来源与证据索引](docs/来源与证据索引.md)；该索引也记录每条来源能证明什么、不能证明什么。

## 目标与边界

### 建议的产品定义

**HK-TAS Runtime + Studio**：运行时 Mod 只负责受控输入、tick 账本、状态观测与验证；Studio/文本 movie 负责编辑、标记、diff、轨迹与日志。二者以本地命名管道或 loopback 通信，运行时只在 Unity 主线程的安全点消费已验证命令。`[建议]`

### 非目标

- 不把游戏存档称为即时 savestate。
- 不开放任意 C#、任意反射写、原始地址读写、路径访问、进程启动或网络请求给 movie/脚本。
- 不以“某个旧版工具能替换 DLL”为依据，承诺当前 1.5.x 也可行。
- 不把“同一机器的一次回放成功”表述为确定性证明。`[建议]`

历史上的 HollowKnightTasInfo 是 Linux + libTAS 工具链，并会覆盖游戏的 `Assembly-CSharp.dll`；其 README 还明确说更侵入的功能不能和未修改游戏同步。[S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]` 它是 T-FT、RNG 日志和 HK 专用 OSD 的有价值先例，不是本项目的加载方式。`[推断]`

## libTAS 能力拆分与纯 Mod 对应关系

libTAS 通过 `LD_PRELOAD`、函数 hook 与 Unix socket，在进程/系统调用层干预输入、时间、文件、线程、状态保存和编码；其文档将呈现调用视为重要帧边界。[S3](https://clementgallet.github.io/libTAS/guides/how/) `[事实]`

下表中的“可实现”指在**限定的 HK 动作/语义层**可以实现；“部分可实现”表示有明确缺口，必须在 movie 中声明；“无法等价”指纯 Mod 没有可靠、通用的同层能力。

| 能力 | libTAS 侧能力 | 纯 Mod 结论 | Mod 内对应与边界 |
|---|---|---|---|
| 暂停与逐帧 | 以呈现/输入边界控制推进 | **部分可实现** | 可用暂停、输入 gate 与受控恢复做“游戏 tick 步进”；不能默认等同于 libTAS 的呈现级帧步进。 |
| 输入录制、编辑、回放 | movie、键鼠/手柄输入和编辑器 | **可实现** | 在 HK 动作层录制/替代输入；文本、RLE、持有态、标记与分支编辑可做得更符合游戏语义。 |
| 时间虚拟化 | 拦截时间查询、等待与相关系统调用 | **部分可实现** | 可控制 Unity 缩放时间和已知 hook；不能普遍重写 `DateTime`、`Stopwatch`、原生/第三方 Mod 时间源。 |
| RNG | 进程层随机源控制；HK 历史工具记录/播放调用 | **部分可实现** | 记录 `UnityEngine.Random` 状态和白名单调用点；未知 `System.Random` 实例、原生源、其他 Mod 仍可漏掉。 |
| Savestate | 保存进程地址空间、线程和部分 I/O 状态 | **无法等价** | MVP 用“基线加载 + 输入快进 + milestone hash”；语义快照只能登记并恢复有限对象。 |
| 快进 | 降低/跳过呈现并保持进程级控制 | **部分可实现** | 可提高受控推进、降低可选呈现/叠层；加载、物理追赶和音频必须实测，不可假定不影响同步。 |
| 视频/音频录制 | 截帧、内部混音、编码 | **部分可实现** | 可捕捉相机/RenderTexture；可靠逐 tick 画面与同步主混音、无丢帧编码需要专门捕获层。 |
| RAM watch/search | 原始地址、搜索、指针链、脚本读写 | **无法等价** | 应提供 `hero.pos`、`enemy.hp` 等语义 watch；不把任意地址扫描/写入暴露为产品能力。 |
| Lua/脚本 | Lua 可访问输入、GUI、内存、savestate 等 | **可实现（受限）** | 可用受限 DSL 或脚本 API；只允许已审核的输入、断言、相机和捕捉命令。 |
| 文件/网络/Steam/线程 | 对多类环境 API 的进程级拦截 | **无法等价** | Mod 只能控制自己和已知游戏调用点，不能通用决定线程调度或虚拟未知 I/O。 |
| 确定性验证 | 降低环境变量、记录 movie | **可实现（验证）** | 建立状态哈希、冷启动重放、manifest 和 desync 报告；不能证明未观测原生状态已经受控。 |

libTAS 自身也把线程视为严重且不能完全解决的非确定性来源，并为 Unity 提供专门支持说明。[S3](https://clementgallet.github.io/libTAS/guides/how/) [S4](https://clementgallet.github.io/libTAS/guides/unity/) `[事实]` 因此上表的“可实现”不等于无条件确定性。`[推断]`

## Hollow Knight / Unity 运行时约束

### 帧、物理步和 T-FT

Unity 的可变帧循环与固定时间步是两套系统：一个视觉帧可有零次、一次或多次固定物理步，取决于帧率和追赶情况。[S6](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html) `[事实]` HollowKnightTasInfo 把 `Time.time - Time.fixedTime` 作为 **T-FT** 展示，并说明其与多种细微行为有关。[S7](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/TimeInfo.cs) [S5](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md) `[事实]`

因此 movie 不能只记录“第 N 帧按了什么”。它至少要定义并记录：

- `visualTick`：观测/呈现边界；
- `fixedTickCount`：该 visual tick 覆盖的固定步数量；
- `inputPhase`：输入被采样/替代的实际相位；
- `tft`：T-FT 或与目标构建相匹配的等价时间指标；
- `sceneEvent`、随机调用计数和 milestone hash。`[建议]`

### 暂停、协程、场景与物理

`timeScale = 0` 时，Unity 2017.4 文档说明 `FixedUpdate` 不会调用；但它只是时间缩放，不能等同于冻结整个进程。[S8](https://docs.unity3d.com/es/2017.4/ScriptReference/Time-timeScale.html) `[事实 + 推断]` Unity 的一般时间文档也说明 `Update` 仍可被调用、未缩放时间可继续变化。[S6](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html) `[事实]`

- `WaitForSeconds` 使用缩放时间并在后续帧恢复；协程迭代器/闭包执行点不是普通字段快照可可靠复原的对象。[S9](https://docs.unity3d.com/ru/2017.4/ScriptReference/WaitForSeconds.html) `[事实 + 推断]`
- 异步场景加载在完成前存在生命周期与对象创建顺序敏感性；验证必须把加载开始/结束纳入账本。[S10](https://docs.unity3d.com/ja/current/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html) `[事实 + 建议]`
- `Rigidbody2D` 速度、接触、transform/physics 同步、Animator、PlayMaker FSM 当前 state/变量/pending event、静态字段、单例、对象池和其他 Mod hook 都可能分叉。`[推断]`

**实现前必须探针，不可猜测**：实际输入消费点、是否先于某个 `ListenFor*` FSM action、Tick 边界、目标构建里可恢复的状态集。HK Modding 的输入约束建议优先读原生动作链（`InputHandler.Instance?.inputActions` / `HeroActions`），而不是将 `UnityEngine.Input.GetAxisRaw()` 当作通用 gameplay 注入点。`[建议；本仓库的 Modding 规范]`

## 推荐架构

```text
Studio / 文本 movie
        │  本地命名管道或 loopback（非阻塞）
        ▼
解析器 + 静态校验 ──────────────► Runtime command queue
                                      │（只在 Unity 主线程安全点消费）
         ┌────────────────────────────┼────────────────────────────┐
         ▼                            ▼                            ▼
  Input substitute                Tick ledger                State inspector
  录制 / 回放 / gate        visual/fixed/T-FT/RNG      Hero/FSM/Collider/scene
         │                            │                            │
         └───────────────► Determinism guard ◄──────────────────────┘
                                  │
                    checkpoint = baseline + input range + hash
                                  │
                       Overlay / hitbox / capture / desync report
```

| 组件 | 责任 | 关键约束 |
|---|---|---|
| Runtime | Mod 生命周期、主线程安全点、执行队列 | 不在 Unity 主线程阻塞等待 Studio。 |
| Input substitute | 动作层录制/回放/注入 | 先通过 Phase 0 确认消费点；记录持有态和边沿。 |
| Tick ledger | 跨 visual/fixed 时间轴记录 | 所有时间字段有协议版本，绝不把“帧”留作歧义术语。 |
| Inspector | Hero、场景、FSM、Collider、敌人、RNG 的稳定语义路径 | 不把跨运行不稳定的 Unity instance ID 当作哈希主键。 |
| Determinism guard | 游戏/DLL/Mod/设置/存档 hash，随机与加载账本 | manifest 不匹配或出现未声明 Mod 时拒绝“验证模式”。 |
| Checkpoint | 基线 + 受控命令区间 + 里程碑 hash | 首版只做重载快进，不承诺瞬时全局 rewind。 |
| Studio | movie 编辑、标记、diff、日志、路线可视化 | 可借鉴 CelesteTAS 的专用 Studio 体验，不将其当作底层确定性证据。[S12](https://github.com/EverestAPI/CelesteTAS-EverestInterop) |

建议的**白名单命令**是 `input`、`frames`、`marker`、`checkpoint`、`assert`、`camera`、`capture`；动作必须在 manifest 允许的上下文内执行。`[建议]`

## 确定性、输入、逐帧与状态策略

### 确定性与 movie 格式

movie 头部应包含：协议版本、游戏与关键 DLL hash、Mod manifest、Mod 设置 hash、初始存档/基线 hash、目标平台和实验 profile。正文使用 RLE/持有态输入和白名单命令；里程碑记录排序后的语义 hash、场景、tick 账本摘要和 RNG 计数。`[建议]`

状态哈希应基于稳定的语义键，例如 `hero.position`、`hero.velocity`、可控 PlayerData 子集、场景、指定 FSM state、登记敌人的 HP/位置，而不是 Unity instance ID。`[建议]` 真正的通过标准是**干净 profile 冷启动后重复回放全部 milestone 一致**；跨独立安装再验证一次可显著提高证据强度。[S14](https://tasvideos.org/Glossary) `[建议，借鉴 TAS 的同步验证概念]`

### 输入录制/回放与近似逐帧

- 录制的是 HK action 层的“持有/按下/释放”和选择性模拟输入，不是 OS 扫描码。`[建议]`
- 单步按钮只请求推进到下一个**已定义 tick 边界**，并把实际产生的 visual/fixed 账本写回日志。`[建议]`
- 回放器应先以“观察模式”比对 hash，再允许“验证模式”；出现首个差异时输出最近的输入、T-FT、FSM、RNG、加载事件。`[建议]`

### Savestate、存档与检查点

| 方案 | 适用性 | 可恢复内容 | 不能保证的内容 |
|---|---|---|---|
| 输入重放检查点 | **MVP 首选** | 已知基线存档后的白名单输入/命令 | 回到点位需要重载并快进，且依赖同步。 |
| 语义状态序列化 | 后续、按房间适配 | Hero、PlayerData 子集、登记敌人、部分 FSM/场景变量 | 动态对象、协程、对象池、接触、未登记 Mod 状态。 |
| 反射全对象图 | 不建议作为产品承诺 | 理论上的部分托管字段 | Unity native object、循环引用、delegate、迭代器、实例身份和创建顺序。 |
| 进程级快照 | 纯 Mod 范围外 | 地址空间、线程、寄存器、部分 I/O | 需原生进程控制；GPU/音频仍是困难边界。 |

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

## 与 CelesteTAS 等路线的对照

| 维度 | HollowKnightTASMod 建议 | CelesteTAS 的可借鉴处 | 不应照搬之处 |
|---|---|---|---|
| 编辑体验 | 文本 DSL + Studio + 时间线/标记/diff | 专用 Studio 与 TAS 编写工作流。[S12](https://github.com/EverestAPI/CelesteTAS-EverestInterop) | 不据此推定 HK 的输入与时间边界相同。 |
| 游戏状态 | FSM、Collider、敌人、场景与 T-FT 为一等数据 | 游戏专用工具优于通用按键界面 | HK 的 PlayMaker/物理/加载需独立适配。 |
| 确定性 | movie manifest + 冷重放 + hash | 可重复 movie 的工作流 | 不能把另一游戏的同步性质迁移为 HK 事实。 |
| 快照 | 基线快进优先 | 以路线生产力为目标 | 不承诺原生 savestate。 |

## 分阶段 MVP 路线

| 阶段 | 交付目标 | 退出条件 |
|---|---|---|
| **0：探针** | 锁定一个目标构建；找到输入消费边界；记录 visual/fixed/T-FT、场景事件和最小状态 hash | 干净 profile 下同一短路线多次冷回放的账本可解释。 |
| **1：MVP** | action 输入录制/编辑/回放、暂停/近似单步、文本 movie、基础 Inspector、milestone hash | 目标路线在同一安装上重复冷回放全部 hash 一致。 |
| **2：实用工具** | Studio、FSM/hitbox overlay、RNG 日志、快进、输入重放检查点、分场景日志 | 加载/死亡/重生/菜单等回归矩阵通过。 |
| **3：受限语义快照** | 对固定路线/房间登记状态和恢复适配器 | 每种受支持场景有重载对照与明确降级路径。 |
| **4：原生研究线** | 若确需进程快照、时间/I/O/线程控制，则开发外部/原生工具 | 不再归类为“纯游戏内 Mod”；独立安全与兼容性评审。 |

MVP 明确排除：即时全局 savestate、通用 RAM search、通用环境虚拟化和逐样本音频录制。`[建议]`

## 主要风险与验证实验

| 风险 | 最小实验 | Go / No-Go 信号 |
|---|---|---|
| 输入相位错误 | 同一动作在候选 hook 相位逐一录制/回放 | **Go**：输入边沿和结果稳定；**No-Go**：无可重复消费边界。 |
| visual/fixed 分叉 | 记录 T-FT、每 visual tick 的 fixed count，施加高/低负载 | **Go**：差异可预测并纳入 ledger；**No-Go**：同输入无解释分叉。 |
| RNG 失步 | 对目标房间记录调用计数/状态，比较多次冷回放 | **Go**：计数与 hash 收敛；**No-Go**：白名单外频繁漂移。 |
| 场景/重生 | 覆盖加载、死亡、重生、菜单、存档读取 | **Go**：每个基线重载快进后命中 milestone；**No-Go**：检查点无法收敛。 |
| 其他 Mod 干扰 | 仅 API+TAS Mod 的干净 profile，与逐个加入其他 Mod 比较 | **Go**：manifest 能识别并隔离差异；**No-Go**：未知状态无法归因。 |
| 快进副作用 | 同路线上正常速度与快进后 hash 对照 | **Go**：关键 hash 一致；**No-Go**：快进改变可观察状态。 |

完整的可执行实验顺序、数据字段和结果模板见[验证实验清单](docs/验证实验清单.md)。任何“帧精确”“可验证”的对外说法，都应等到 Phase 0/1 的上述证据完成后才使用。`[建议]`

## 参考来源

完整、带用途说明的清单在[来源与证据索引](docs/来源与证据索引.md)。本页保留所有关键链接的就近引用；来源均于 **2026-07-27** 访问或自上一轮完整调研产物逐条保留。

- HK Modding API：[发布 v77](https://github.com/hk-modding/api/releases/tag/1.5.78.11833-77)、[源码](https://github.com/hk-modding/api)
- libTAS：[工作机制](https://clementgallet.github.io/libTAS/guides/how/)、[Unity 支持](https://clementgallet.github.io/libTAS/guides/unity/)
- HollowKnightTasInfo：[README](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/README.md)、[RandomInjection](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/RandomInjection.cs)、[TimeInfo](https://github.com/Jarlyk/HollowKnightTasInfo/blob/master/Source/TimeInfo.cs)
- Unity：[时间与帧管理](https://docs.unity3d.com/cn/2022.3/Manual/TimeFrameManagement.html)、[`Time.timeScale`](https://docs.unity3d.com/es/2017.4/ScriptReference/Time-timeScale.html)、[`WaitForSeconds`](https://docs.unity3d.com/ru/2017.4/ScriptReference/WaitForSeconds.html)、[`LoadSceneAsync`](https://docs.unity3d.com/ja/current/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html)
- 相关路线：[CelesteTAS-EverestInterop](https://github.com/EverestAPI/CelesteTAS-EverestInterop)、[TASVideos Glossary](https://tasvideos.org/Glossary)

---

下一步实现前，请先按[验证实验清单](docs/验证实验清单.md)完成 Phase 0，而不是直接承诺“完全替代 libTAS”。
