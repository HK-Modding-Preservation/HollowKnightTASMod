# 执行检查点

## 2026-07-27 20:54 CST

- **目标**：把 Hollow Knight TAS Mod 可行性调研整理为中文研究仓库，并推送名为 `HollowKnightTASMod` 的 GitHub private 仓库。
- **已完成**：读取三项完整原始产物；核验 HK Modding API v77、HollowKnightTasInfo、libTAS 与 Unity 时间文档；创建 SDD context/codemap/spec。
- **当前状态**：独立 Git 仓库已建立在 `main`，初始文档提交为 `8221727`；GitHub private 仓库 `windplusflower/HollowKnightTASMod` 已创建、`origin` 已关联、初始提交已推送且远端/本地 SHA 一致。文档、来源和仓库验证均通过。
- **下一步**：提交本次 SDD Review 记录并推送，再做最终远端 SHA 验证；后续功能工作从 `docs/验证实验清单.md` 的 Phase 0 开始。
- **回滚**：本地提交可用 Git revert 回退；远端已创建且为 private，删除远端属于额外破坏性操作，必须获得用户明确授权。

## 2026-07-28 06:40 CST

- **目标**：复核当前研究仓库的实施可行性，把 Phase 0/1 拆成可独立验证的任务，并为每个任务写持久化 Spec。
- **已完成**：核对仓库、Git、本机 `1.5.78.11833` + Modding API v77、关键 DLL/SDK 与 InControl/HeroController 输入链路；新增实施可行性任务图、总 Spec 和 T01-T12 共 12 份任务 Spec；README 与实验清单已增加入口。
- **当前状态**：研究结论为“有条件 GO”；仓库仍没有 Mod 实现代码。T02 输入相位、T03 tick 账本、T07 冷启动一致性是硬门禁。
- **下一步**：获得代码执行授权后先实现 T01；T01 通过后可并行推进 T02/T03/T04/T05。
- **回滚**：本轮只有 Markdown 变更，可按文件或未来提交执行 Git revert；未修改游戏安装、Mods、存档或系统配置。

## 2026-07-28 07:14 CST

- **需求修订**：用户要求 savestate 支持任意时刻保存、下次启动从任意存档点开始重放，并支持定时存档。
- **设计结论**：将 savestate 定义为持久化重放存档；从已知 baseline 开始始终维护输入日志，手动或按 movie tick 固化 cursor/hash，下次启动加载 baseline、自动重放前缀并校验后停在目标点。仍不承诺进程内存快照。
- **影响范围**：README、实施任务图、实验清单、总 Spec、T06、T09、T12 和任务索引。

## 2026-07-28 07:33 CST

- **需求修订**：TAS 实现参考 CelesteTAS；纯 Mod 不足时同步开发外部工具，并要求可由 Mod 自动启动。
- **设计结论**：产品调整为 Runtime Mod + 随包 Companion/Studio + 可选 NativeHost。T12 负责固定签名路径、SHA-256、Mod 自动启动、单实例、版本化 named pipe 和故障降级；T13 负责构建白名单下的外部/原生 capability，并以 T07 对照和 T09 回退作为硬门禁。
- **CelesteTAS 参考**：已核对官方 README、`StudioHelper` 和 `StudioCommunication`；借鉴 Mod/Studio 一体发布、随游戏启动、版本/心跳机制，不复制其共享内存、shell 启动或在线下载决策。
- **当前状态**：任务总数由 T01-T12 扩为 T01-T13；仍只有 Markdown 规格，没有创建或启动任何 Companion/NativeHost，也没有修改游戏安装。

## 2026-07-28 08:36 CST

- **需求修订**：在比较 Celeste 与 Hollow Knight savestate 后，确认采用更轻量的“语义关键帧 + 短尾重放”作为纯 Mod 恢复加速。
- **设计结论**：新增 T14，只在已验证的 room-entry safe tick 捕获版本化语义 DTO 与 adapter manifest；任意用户存档仍由 T09 固化，恢复时可从最近兼容关键帧重放到目标点。
- **硬边界**：T09 是唯一跨启动真相源；T14 缺失、损坏、不兼容、adapter 失败或 hash 失配时必须干净回退 T09。T13 保留为更后置的同进程进程级快照实验，不与 T14 混用支持声明。
- **Celeste 参考**：实际实现是 CelesteTAS 调用 Speedrun Tool，由后者深拷贝游戏专用 `Level`/`SaveData` 图并执行 RNG/输入/静态状态等显式恢复动作；本项目借鉴适配器和安全门禁，不复制通用全对象深拷贝。
- **当前状态**：任务总数由 T01-T13 扩为 T01-T14；仍只有 Markdown 规格，没有实现或运行关键帧、Companion 或 NativeHost。

## 2026-07-28 08:53 CST

- **需求修订**：用户要求提供外部接口了解/控制内部状态，用于实现期调试，并在完整版中让 AI 辅助制作 TAS。
- **设计结论**：新增 T15。T12 Runtime pipe 保持内部受信；Companion 提供 automation broker，外部 SDK/CLI 与 stdio MCP AgentBridge 读取稳定语义 DTO，并只通过 typed command 控制 Runtime。
- **权限边界**：默认 `ReadOnly`；写控制必须由用户启用 `ApprovedControl`、取得单一短期 lease，并携带 session/manifest/mode/tick/idempotency。AI 不获得隐藏权限，不开放任意内存、反射、路径、进程或自然语言直执行。
- **AI 闭环**：observe → movie proposal → 独立分支 → T04 校验 → T06/T07 运行与 diff → 显式接受；不自动覆盖原 movie/expected hash。
- **当前状态**：任务总数由 T01-T14 扩为 T01-T15；已经开始按顺序执行，当前实现单元为 T01。

## 2026-07-28 09:26 CST

- **实现完成**：T01 建立 `netstandard2.0` Core、`net472` Runtime、`net8.0` CLI/Tests，加入真实 HK 引用校验、Debug 自动安装/打包、canonical environment manifest、有界 JSONL 证据流和验证预检。
- **离线证据**：solution build 0 warning/0 error；Core/CLI 11/11 tests；ZIP SHA-256 与 `SHA256.txt` 一致；CLI 对真实 manifest 返回 0、对 malformed fixture 返回 3。
- **实机证据**：普通 profile 正确识别 15 个启用 Mod；请求验证时因 14 个 unexpected Mods 明确拒绝但正常退出；仅 API + TAS 的隔离 profile 中验证模式允许。每次有效 session 均有完整的 start/manifest/preflight/stop 四条事件且 dropped=0。
- **缺陷修正**：实机发现 v77 的 `LoadedModsWithVersions` 启动时为空；改为 `ModHooks.GetAllMods(onlyEnabled: true, allowLoadError: false)`，并让 manifest/preflight 使用同一快照。
- **恢复状态**：普通 `Mods` profile 已原样恢复，验证设置已回到 false，临时目录已清理。T01 Foundation gate PASS，下一任务为 T02。

## 2026-07-28 10:17 CST

- **实现完成**：T02 增加动作样本/固定 fixture、自定义 TAS binding、按对象引用与顺序恢复的 `BindingLease`、独立 F8 紧急停止、A/B/C 三个输入相位探针及隔离 profile 矩阵脚本。
- **离线证据**：solution build 0 warning/0 error；Core/CLI/Input tests 17/17；PowerShell harness 与统一 summarizer 语法通过。
- **实机证据**：A/B/C 各执行 10 个独立进程，30/30 `RunPass=true`；24 个完整 fixture 均零 input/Hero mismatch，6/6 物理噪声被检测但未污染 TAS 输入，紧急停止/真实场景切换/adapter 异常均正确结束。
- **边沿一致性**：每个候选的 8 个完整语义用例归一化 tick→edge 映射一致，三者 SHA-256 均为 `ee34f4e811d50931320b4e3b8f4485cd5a77ba7913d378f67f1f51db400a7c1c`。
- **方案选择**：按预设优先级提升公开 `InputManager.OnUpdate` 后预装下一 sample 的 A 为正式 `HeroInputAdapter`；B/C 只保留为显式测试探针。
- **恢复状态**：30/30 binding 均按原对象引用与顺序等价恢复；普通 `Mods` profile 恢复为 18 项，无交换目录残留，游戏已退出。T02 G0 PASS，下一任务为 T03。

## 2026-07-28 11:07 CST

- **实现完成**：T03 增加 `inputTick/visualTick/fixedTick/sceneEpoch` ledger、exact-bit Unity 时间字段、严格 sequence/metadata/scene/gap validator、有界异步 sink，以及 P60/P30/PLOAD/PSCENE runtime probe 与正式矩阵 harness。
- **离线证据**：solution build 0 warning/0 error；Core/CLI/Input/Ledger tests 25/25；零 fixed、多 fixed 与倒退 fixtures 得到预期 PASS/PASS/FAIL；两个 PowerShell 脚本语法为 0 error。
- **实机证据**：P60/P30/PLOAD/PSCENE 各 5 个独立进程，共 20/20 `runPass=true`、`validatorPass=true`、`droppedCount=0`、`restoreEquivalent=true`；所有 ledger 行数、连续 sequence 和必需证据文件复核通过。
- **时间模型**：四个 profile 的主 phase signature 一致，SHA-256 为 `1f960fafc8a94616130c33876472b42b7cb1e18273d511b123fb1402e71129bc`；正式锁定 `InControlCommittedTick` 为 movie input unit，visual/fixed 仅作独立账本坐标，T-FT 仅作 exact-bit 诊断。
- **生命周期证据**：PSCENE 5/5 实际观测 hazard death/respawn 和 `GG_Workshop -> Quit_To_Menu -> Menu_Title -> GG_Workshop`，scene epoch 增至 3；确认 `acceptingInput` 不能作为 gameplay 生命周期必要条件。
- **恢复状态**：游戏已退出；普通 Mods profile 为 18 项，无交换目录残留；验证设置为 false；所有临时 frame/vsync/timeScale/fixedDeltaTime 设置等价恢复。T03 G1 PASS，下一任务为 T04。

## 2026-07-28 11:36 CST

- **实现完成**：T04 交付 `HK-TAS Movie v1` 文档、Runtime-independent AST/source span/diagnostic、严格 parser/validator、canonical writer/Movie ID，以及 `movie validate/format/inspect` CLI。
- **协议边界**：`tick-unit input` 固定为 T03 的 `InControlCommittedTick`；只允许 frames/marker/checkpoint/assert 白名单，不含任意 C#、反射、文件、路径、进程或网络能力。Parser/CLI 有 source/line/marker/expanded-tick 上限。
- **离线证据**：solution build 0 warning/0 error；Movie tests 24/24；全量 tests 49/49；2,000 个固定种子随机畸形输入无未处理异常；canonical round-trip 连续 100 次字节与 ID 一致。
- **Fixtures**：2 个 valid、11 个 invalid、1 个 golden；11/11 invalid 首错 code/line/column 与预期一致。Minimal Movie ID 为 `c793c2ec62cdb93a738da9d535f825a128c798b5164979375a401297b47e8dea`，actions/golden 为 `453889fc8b9ee0b53d9b9a849cf9dd03ae2571e17a8afcebd1652b353aa45114`。
- **CLI 证据**：valid/format-check/inspect 均返回 0；unknown command、manifest mismatch、BOM 和 noncanonical check 均在 Runtime 前返回 3。
- **当前状态**：T04 Offline Protocol PASS，下一任务为 T05 语义状态快照与稳定哈希。

## 2026-07-28 11:48 CST

- **实现完成**：T05 增加固定 16-key `SemanticSnapshotSchemaV1`、typed value、严格 big-endian canonical binary、SHA-256、exact-bit diff、严格 decoder，以及 Scene/Game/Hero/PlayerData Runtime probes。
- **离线证据**：solution build 0 warning/0 error；全量 tests 57/57；T05 定向 8/8；golden snapshot SHA-256 为 `d41218d92af9d54d613d12232860d076e475bed68277f2631a5e35a76f76685d`。插入顺序、locale 不改变 hash，signed zero/NaN payload/单 bit 浮点差异均保留。
- **实机证据**：STABLE/HEALTH/SCENE 三个隔离进程 3/3 `runPass=true`、汇总 `gatePass=true`。冻结状态 100 次 capture 只有一个 hash；Hero X 与 health 分别只产生预期单 key diff；`GG_Workshop -> GG_Vengefly` 得到 `sceneEpoch=1`。
- **失败语义**：故障注入明确输出 `snapshotPresent=false`、`canonicalBytesPresent=false`、`sha256Present=false`，没有默认值、旧状态或伪 hash。
- **恢复状态**：游戏已退出；普通 Mods profile 恢复为 18 项，无交换目录残留；验证设置保持 false。T05 G2 prerequisite PASS，下一任务为 T06。

## 2026-07-28 13:02 CST

- **实现完成**：T06 增加 Core playback mode/cursor/RLE/recording/journal，Runtime HeroActions 回放 lease、独立 emergency stop、全路径 binding 恢复和从 baseline 起始终开启的分段原子 shadow journal。
- **离线证据**：正式构建 0 warning/0 error；全量 tests 72/72，Playback 定向 10/10。
- **实机证据**：正式 `artifacts/playback-final` 共 16 个独立进程；ORIGINAL/EDITED 各 5 次，PHYSICAL/MANUAL/EMERGENCY/SCENE/FAULT/SHADOW 各 1 次，全部 `runPass=true`、汇总 `gatePass=true`、输入 mismatch 总数 0。
- **安全与恢复**：反向物理 binding 被检测但不泄漏；手动、紧急、真实场景变化和受控异常均先全释放再按原引用/顺序恢复；shadow journal 所有 run 无 gap 且完成路径持久化。
- **确定性边界**：语义终点 12/12 通过，但 ORIGINAL/EDITED exact endpoint snapshot 各有 3 个 hash；未隐藏或强行纠偏，明确转交 T07 冷启动 milestone/signature 门禁。
- **恢复状态**：游戏已退出；普通 Mods profile 为 18 项，无交换目录残留；验证设置为 false。T06 Playback MVP PASS，下一任务为 T07。

## 2026-07-28 13:50 CST

- **实现完成**：T07 增加 milestone/run signature、版本化 semantic verification projection、首差异 comparator/report、Runtime capture、严格 verification CLI、三组 fixtures 和可断点续跑的独立 Steam 进程 campaign harness。
- **离线证据**：全量 tests 80/80；Runtime build 0 warning/0 error；metadata mismatch 为 `Incomparable`，semantic/ledger 首差异均输出 32-tick 上下文。
- **实机证据**：`artifacts/verification-final` 的 10 个独立 session/process 全部得到 `cae096b6b774531d97f773478a10fac119e46f82c2b0ef583784451d3721712a`；故意在 movie tick 490 注入 Attack 后于 milestone `checkpoint:divergence-probe` / tick 491 定位 `hero.cState.attacking=false→true`。
- **确定性边界**：首版移动路线暴露 111/113 fixed tick 调度分叉，未隐藏纠偏；正式 oracle 缩小为 stationary grounded/idle，并以 `v1-float32-decimal-4` 只在比较层消除 sub-ULP 噪声。RNG 为 `not-captured`，跨安装 Campaign C 为 `NOT_RUN`。
- **恢复状态**：Steam Cloud 警告未被绕过，取消后待其自行恢复再断点续跑；最终游戏进程为 0、普通 Mods profile 18 项、无交换目录、验证设置 false。T07 `LOCAL_VERIFIED`，下一任务为 T08。

## 2026-07-28 14:38 CST

- **实现完成**：T08 增加 Core pause/step 状态机、显式 raw-input suspension、Runtime movie gate、只修改 `timeScale` 的 bit-exact lease、异步 StepResult、完整 control ledger 和 9-profile harness。
- **离线证据**：全量 tests 88/88；Runtime build 0 warning/0 error；未声明 raw tick gap/倒退、非法状态转换和错误 step quota 全部 fail closed。
- **实机证据**：静止/左移中暂停 5.010/5.017 秒均保持 movie tick 20、fixed tick 和 verification hash 不变，raw visual/input 分别推进 1178/1196。正常 100 tick 与 100-step endpoint hash 都为 `ddf9fd7b744a70c568ea7d4ccb34ceedf31a2b89e0aa711949cf2f5de5ab1d8a`。
- **命名结论**：100 次 StepResult 中 79 次 visual/fixed/input=`1/0/1`、21 次=`1/1/1`；因此正式名称为 `Controlled Step`，不是 `Tick Step`。
- **恢复与账本**：resume、真实 `GG_Workshop` scene request、focus handler、受控异常、Mod shutdown 均 exact-bit 恢复 time settings 和输入 binding；9/9 完整 ledger 通过 T03 validator。Focus 为共用处理器的受控触发，不外推为 OS 窗口自动化验证。
- **恢复状态**：游戏进程 0、普通 Mods profile 18 项、无交换目录、验证设置 false。T08 G3 prerequisite PASS，下一任务为 T09。

## 2026-07-28 18:32 CST

- **实现完成**：T09 增加 Replay Save v1、始终开启的 journal 固化、content-addressed store、原子 transaction/catalog、manual/interval retention、游戏内目录、专用槽 lease、non-blocking restore coordinator 和可选 accelerator 回退。
- **确定性修正**：动态路线启用 `unity-capture-delta-equals-fixed-v1`，只在 `timeScale=1.0f` 时推进 movie tick；目标 Hero 位置使用 `Rigidbody2D.position`。重复恢复先 `DontSave` 返回 `Menu_Title` 再加载，修复第二次直接 LoadGame 残留旧 Hero lifecycle 的 airborne 基线。
- **离线证据**：Core/CLI 全量 tests 108/108；Runtime build 0 warning/0 error；transaction crash、corruption/gap、retention、slot lease 与 accelerator 决策矩阵通过。
- **实机证据**：`artifacts/replay-save/final-same-slot2-10` 捕获 5 个手动点和 3 个自动点，按非时间顺序各恢复 10 次；80/80 次 canonical semantic SHA-256 原始值完全一致，cursor/bindings/settings/Hero control 全通过。此前 24/24 稳定性矩阵验证同进程重复恢复。
- **槽安全**：正式矩阵以相同 bytes 的 slot 2 零写入恢复；`capture-smoke-10` 的真实不同占用槽拒绝覆盖通过，合成矩阵覆盖 approved backup/rollback/commit。四个用户槽 SHA-256 最终不变。
- **恢复状态**：游戏进程 0、普通 Mods profile 18 项、验证设置 false、隔离 replay store 已清理。T09 G3 PASS，下一任务为 T10。

## 2026-07-28 T10 RNG 诊断与构建白名单

- **实现完成**：新增目标构建 `Random.State` s0..s3 canonical codec、
  Core fingerprint/whitelist/diff、严格 assembly SHA/MVID/signature/token/
  IL hash resolver、两个 method-boundary hook、RNG JSONL ledger、manifest
  schema v2 coverage 字段，以及 T07 `rng-state` 首差异集成。
- **离线证据**：solution build 0 warning/0 error；全量 tests 119/119；
  assembly/MVID/token mismatch 均 fail closed。
- **实机证据**：`artifacts/rng/final-5-match/` 的 5 个 MATCH 受控
  RNG/semantic/call trace 完全一致；NOHOOK 保持同一 RNG/semantic trace
  且零 call；DIVERGE 在 milestone 0 首先出现 RNG 差异，语义差异在
  milestone 4 出现。
- **边界**：60-tick ambient endpoint 为 4 unique / 5 MATCH，
  `unwhitelistedUnityRngObserved=true`；coverage 固定为
  `unity-random-partial-whitelist-v1`，System.Random/其他 Mod RNG 未覆盖，
  未实现 RNG playback/结果强制。
- **恢复状态**：游戏进程 0、普通 Mods profile 18 项、四槽 hash 不变、
  验证设置 false、隔离 replay store 已清理。T10 PASS，下一任务为 T11。

## 2026-07-29 T11 语义 Inspector 与叠层

- **实现完成**：新增 Core typed watch/registry/canonical JSON，以及 Runtime
  tick/scene、Hero、RNG、FSM、enemy、Collider providers；文本 overlay、
  Collider outline 和有界 JSONL exporter 共用同一 immutable WatchFrame。
- **身份与生命周期**：静态对象使用 scene/hierarchy/component ordinal
  verification key；无稳定 adapter 的动态 enemy 只生成 display-only key。
  scene change 清除旧引用并重建，Inspector disable 后停止采样与绘制。
- **离线证据**：solution build 0 warning/0 error；全量 tests 129/129。
- **实机功能证据**：`artifacts/inspector/final-10-load-10min/` 的 10 个
  功能进程全部通过同回调 direct parity、overlay/Collider、同源导出、
  pause/resume、disable、scene clear/rebind 和截图检查；41 个稳定 key
  集合 SHA-256 均为
  `50f86a0f7bfe77e18e944142f60dcd223c693c5ec2604118c6641d9a9d9edc45`。
- **性能证据**：默认采样间隔 120 movie tick 的 600 秒 profile 得到
  sample p95 `0.1874 ms`、归因 managed allocation
  `959.115651432796 B/frame`、provider failure 0、export drop 0。
- **恢复状态**：游戏进程 0、普通 Mods profile 18 项、四槽 hash 不变、
  验证设置 false、隔离 replay store 已清理；持久化默认采样间隔已设为
  120。T11 PASS，下一任务为 T12。
