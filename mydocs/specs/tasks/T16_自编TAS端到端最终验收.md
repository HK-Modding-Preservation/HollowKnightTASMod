# Task Spec T16: 非视觉外部接口驱动的自编 TAS 端到端最终验收

> 当前验收以 D03_假骑士稳定回放.md 为准。本文保留接口与场景细节；下文 T24 硬前置、逐 gameplay tick 原版等价及固定 5-run hash 要求不再执行。正常操作响应、自编 movie、非视觉控制与稳定击败目标仍需证明。

- **Status**: READY_FOR_IMPLEMENTATION
- **Gate**: G6 Product Completion
- **Depends On**: T07, T09, T12, T15, T17, T18, T19, T20, T21, T22, T23, T24
- **Produces**: 自编输入 movie、结构化 authoring transcript、Automation 审计、诸神堂椅子到调谐假骑士的 5-run 冷启动报告

## 0. Open Questions

- None。目标固定为“从诸神堂专用测试槽的椅子坐姿冷启动，到击败调谐
  （Attuned/Tier 1）难度假骑士”。实际 scene、FSM、BossStatue completion
  字段和稳定 watch key 必须在 T15 实机发现后冻结进 fixture，不凭名称猜测。

## 1. Goal

用最终产品本身证明两件事：

1. HollowKnightTAS 的完整 TAS 流程能从编辑、校验、控制、观察、迭代、
   冷启动重放一直走到可重复的游戏内成功结果。
2. 外部工具和 AI 获得的是友好、版本化、非视觉的结构化读写渠道，而不是
   依赖截图/OCR/像素识别、人工代打或任意内存修改。

最终 movie 必须由本项目实施者在本任务中自行创作。允许阅读游戏程序集、
FSM、稳定 watch、ledger 和 Automation 状态；不得下载、导入或改写现成
Hollow Knight TAS movie 作为答案。

T24 是先决硬门禁。无 TAS、人工 TAS 与 AI TAS 对同一输入的 Hero 动画、物理、
FSM、碰撞、资源和世界状态未逐 gameplay tick 等价前，T16 保持 BLOCKED；任何
Boss 击杀、速度或 5-run 重放结果都不计分，也不得通过调整战斗策略规避。

## 2. Fixed Scenario

### Start

- 游戏进程关闭后开始。
- 使用专用 TAS 槽的已冻结 fixture；fixture 的 hash、游戏构建、Modding API、
  Mod manifest 和设置 hash 进入证据。
- 载入后 Hero 必须真实处于诸神堂 `GG_Workshop` 椅子/长椅状态；不得先用
  teleport、scene load、PlayerData 修改或 T09 restore 跳过起点。
- `AutoStartCompanion=true`；Companion 由 Mod 的 T12 固定签名 bundle 自动启动。

### Target

- 从椅子起身并在诸神堂内移动到假骑士雕像。
- 离椅必须走原版椅子 FSM；移动期间控制状态与正常 Hero 动画 clip 一致，
  并由 T17 的非视觉 watch 证明，不得只凭画面或强制播放动画。
- 通过真实输入进入假骑士战斗并选择/确认 `Attuned`（Tier 1）难度。
- 通过真实输入击败假骑士。
- 成功必须由至少两个独立结构化事实源确认：
  - encounter 的目标与难度 watch 明确为 False Knight + Attuned；
  - Boss `HealthManager.isDead`/结束 FSM/milestone 中至少一项确认击杀；
  - 若游戏在该时点更新 Tier 1 completion，则同时记录其稳定字段。
- 视频或截图只可作为补充，不可作为成功判定。

## 3. Authoring Contract

当前导出须直接保留 stopRecording 返回的完整 Movie。起点前后的空输入都是实际模拟时间；禁止沿用旧 RNG-reset 方案裁剪开头、拼接旧路线前缀或补造终点 checkpoint。导出后的完整脚本仍须通过正式 Movie 校验器。`scripts/Test-T16RecordingExport.ps1` 检查原字节保留和非法输入拒绝。

观察值必须来自同一有效 watch frame，按其实际 movieTick/sceneEpoch 标记。任何 provider failure、非 fresh 字段、采样帧不一致、重复键或关键定位字段缺失均中止本次决策，不能用默认坐标继续操作。`scripts/Test-T16CombatObservation.ps1` 覆盖这些拒绝条件；它不证明 Boss 状态或碰撞字段的完整性。

### Allowed

- T15 SDK、CLI 或 stdio MCP resources/tools。
- 结构化 state/watch/FSM/collider/timeline/RNG/ledger/desync。
- pause、step、run-until、record/replay、movie branch/patch/validate。
- authoring 阶段在独立非验证 session 使用 T09 存档和 T15 typed mutation
  做调试；所有此类使用必须出现在审计中。
- 本地反编译/源码检查用于理解确定性规则和 FSM，但运行时决策必须来自
  结构化接口。

### Forbidden

- 截图、录屏帧、OCR、像素采样、模板匹配、目标检测、摄像头或音频识别。
- Computer Use、远程桌面或人工观察画面后给出中途按键。
- 游戏启动后的人工键盘/鼠标输入；测试 harness 只可启动进程和读取产物。
- 任意内存地址、通用反射写入、Boss HP/完成标志修改、FSM 强跳或进程快照。
- 在最终 scored run 使用 typed mutation、teleport、forced scene transition、
  T09 restore、调试控制台或额外 Mod。
- 从网上或本地其他项目导入现成 TAS 输入序列。

### Clean Candidate Rule

调试 session 中得到的策略必须固化为 T04 canonical movie。每个 scored run
都从进程关闭和原始专用槽重新开始，仅注入 movie 声明的输入。若 manifest
显示 `NonVerifiableDebugMutation`、forced transition 或 restore lineage，
该 run 自动失败，不能通过重新命名证据规避。

## 4. Required External Experience

外部客户端必须能在不访问 Runtime 私有对象的情况下完成：

1. discovery：列出 schema、capability、状态 freshness、可用命令和前置条件；
2. observe：读取起点、Hero、scene、雕像、菜单、Boss、tick 与 movie cursor；
3. control：取得/续租/释放 lease，pause/step/run-until/replay；
4. edit：提出 movie patch、离线校验、创建分支、应用分支；
5. debug mutation：在隔离 session 读写允许的 typed 状态并取得 before/after
   hash、rollback 和非验证标记；
6. verify：启动干净 candidate、读取 milestone/desync、比较 run；
7. recover：断线/过期 lease 后无 held input、异常 timeScale 或孤儿进程。

SDK、CLI 和 MCP 使用同一 canonical schema；本验收至少实际使用其中一种
完成整条 authoring loop，并用另外两种各做一次等价状态读取/控制 smoke。

## 5. Independent Verification

### 5.1 Interface Proof

- 保存完整 capability catalog 和 JSON Schema。
- 对关键起点、雕像交互、难度选择、战斗开始、Boss 受击与 Boss 死亡逐点
  对照 Runtime 原始 provider，state 值、tick、phase 和 hash 一致。
- 对两个 typed mutation adapters 各执行一次成功和一次故意失败；成功有
  before/after diff 与非验证标记，失败逐位回滚。随后关闭进程，证明原始
  专用槽 hash 未变化。
- 全程自动记录 client request/response hash、lease、command、movie branch
  和 Runtime audit correlation；credential 做脱敏。

### 5.2 Self-Authored TAS

- authoring transcript 必须展示至少一次基于结构化失败证据的 movie patch
  迭代，而不是只提交最终文件。
- 最终 movie 通过 T04 parse/validate/canonicalize，包含起点 manifest 约束、
  milestones 与预期终点。
- 用外部接口发起最终运行；开始后客户端只能观察和执行回放生命周期命令，
  不能写 gameplay state。

### 5.3 Five Cold Runs

在隔离 profile（Modding API + HollowKnightTAS）连续执行 5 个独立进程：

- 每次恢复同一个原始专用槽 hash；
- 每次由 Mod 自动启动/复用正确 Companion bundle；
- 每次从椅子状态开始并到达调谐假骑士击杀；
- 关键 milestone 的 scene、difficulty、Boss 结束语义 hash 5/5 一致；
- input ledger、movie cursor、RNG coverage、provider failure、desync、cleanup
  全部留证；
- 任一 run 不得包含 mutation、restore、forced transition 或人工输入 lineage。

### 5.4 Negative Controls

- 把一个战斗输入 tick 改错，必须产生结构化失败或 desync，而非仍被判通过。
- 将目标难度改为 Ascended/Radiant 或未能确认 Attuned，必须失败。
- 关闭 T15 write 权限时可继续读取，但无法启动/修改 candidate。
- 杀死 Companion、AgentBridge 或让 lease 过期，Runtime 必须释放输入并恢复
  时间；重新连接后从新冷启动继续，不接管半个未知 run。
- 禁止屏幕采集后，完整 harness 仍可独立给出 PASS/FAIL。

## 6. PASS

- T24 原版执行语义逐帧等价门禁已独立 PASS，且本次 scored bundle 的构建与
  T24 候选 DLL/hash 完全一致。
- 非视觉结构化接口覆盖发现、读取、控制、movie 编辑、typed mutation、
  审计与失败恢复，且关键状态与 Runtime 事实源一致。
- typed mutation 只能在隔离调试 session 生效，失败可回滚，成功会永久标记
  非验证；原始槽和 scored run 不受污染。
- 自编 canonical movie 从诸神堂椅子真实起步，以真实输入进入 Attuned
  假骑士并完成击杀。
- 5/5 冷启动均通过，关键 milestone hash 一致，负对照能被检出。
- authoring 和运行不使用视觉识别、人工中途输入、任意内存写入、Boss/完成
  标志修改、savestate 跳点或额外 Mod。
- 游戏退出后 4 个用户槽、Mods 目录、设置、GC/time/input 和进程状态精确恢复。

只有本节全部满足，项目才可宣称“Mod 完成”。

## 7. FAIL

- T24 未通过，或攻击、受击、跳跃/二段跳、动画、物理、FSM 中任一项与无 TAS
  基准不等价。
- 只能通过 overlay/截图判断位置、菜单或 Boss 是否死亡。
- 外部接口缺少 schema/freshness/前置条件，必须猜字段或解析日志文本。
- 最终 run 使用 mutation、teleport、restore、forced scene transition 或人工输入。
- 用下载/现成 movie 冒充自行创作，或缺少可审计 patch 迭代。
- 只成功一次、难度无法结构化证明、负对照不失败或 5-run hash 不一致。
- Companion/AgentBridge 故障留下 held input、异常时间、孤儿进程或污染用户存档。

## 8. Evidence Layout

```text
artifacts/final-tas/<campaignId>/
  environment/
    manifest.json
    slot-hashes.json
    capability-catalog.json
  authoring/
    requests.jsonl
    responses.jsonl
    audit.jsonl
    branches/
    patch-iterations.json
  movie/
    godhome-bench-to-attuned-false-knight.hktas
    canonical.sha256
    validation.json
  mutation-isolation/
    success.json
    rollback.json
    cold-start-clean.json
  runs/
    run-01/
    run-02/
    run-03/
    run-04/
    run-05/
  negative-controls/
  final-verdict.json
  verdict.md
```

## 9. Implementation Checklist

- [ ] 冻结诸神堂椅子起点 fixture 和精确 slot hash。
- [ ] 通过 T17 证明原版离椅、控制与动画状态一致。
- [ ] 通过 T18 交付并核验普通用户使用文档。
- [ ] 通过 T19 证明场景加载耗时不改变后续 movie cursor。
- [ ] 通过 T20 根据 Boss 实时结构化状态调整每次战斗输入，并保留状态与决策的对应记录。
- [ ] 通过 T21 实际使用单帧输入、步进、存档/回退、多帧输入与历史输入修改。
- [ ] 通过 T22 证明普通用户 UI 与 AI 的 SDK/CLI/MCP 操作能力同权。
- [ ] 通过 T24 证明无 TAS、人工 TAS、AI TAS 对同一输入逐 gameplay tick 等价，
  且正常 TAS/scored run 无 gameplay mutation。
- [ ] 通过 T15 发现并冻结雕像、难度、Boss 与击杀稳定语义 keys。
- [ ] 完成非视觉 state/control/edit/mutation 接口 proof。
- [ ] 由实施者使用外部接口自行迭代并产出 canonical movie。
- [ ] 完成 5 次独立冷启动 scored runs。
- [ ] 完成错误输入、错误难度、只读权限、断线/lease 负对照。
- [ ] 验证用户槽、Mods、设置、输入、时间、GC 和进程清理。
- [ ] 生成机器可读 `final-verdict.json` 与中文验收报告。

## 10. Rollback

- T16 不向用户普通槽写入；专用槽测试前后按 SHA-256 校验。
- authoring 分支与证据按 content hash 隔离，失败 candidate 不覆盖基线。
- 任一 scored run 故障立即停止回放、释放输入/时间/lease 并关闭测试进程。
- 自动化失效时可回退游戏内 T06/T09 功能，但 T16 仍判失败，不能降级为
  “人工看画面跑通”。
