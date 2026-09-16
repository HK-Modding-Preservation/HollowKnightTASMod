> 历史归档。仅按需检索，不作为当前进度或执行顺序。当前入口：[CURRENT.md](../../../CURRENT.md).

# HollowKnightTASMod 任务 Spec 索引

当前已获得按顺序完整实现授权。T01-T13 已验证；T07 的等级为
`LOCAL_VERIFIED`，跨安装验证为 `NOT_RUN`。T08 的旧能力名称为
`Controlled Step`，现已因 T24 的逐 tick 反例重新打开；T09 的历史矩阵曾完成
8 个存档点、80/80 次严格恢复，但坐标量化和旧时间写入退役后已随 T24 重新打开，
等待原版执行语义下重新验收；
T10 的 5-run 受控 RNG、hook-off 和 deliberate divergence 矩阵已通过，
coverage 明确为 partial；T11 的 10 次功能加载与 10 分钟性能门禁已通过；
T12 的三种启动路径、Companion off/on 语义对照与 60 分钟 soak 历史范围已正式通过，
但因 T24 冷进程回退新增 restart supervisor 范围而重新打开；T13 的签名 NativeHost、
只读 observe、逐 run native-off/on 各 10 次 T07 语义对照与 1,000 次 attach 已通过，
启动级时钟发布 capability 仍随 T24 保持重新打开。
T14 已按安全降级路径完成为 `ReplayOnly`：协议/store/能力报告已验证，
但零个 `RoomEntry` gate 达到 parity + 600 tick oracle 门禁，因此没有
伪造关键帧加速支持。T15 已验证。实机发现攻击/二段跳的 Hero 动画、物理与
衍生特效不同步，当前先执行 T24 原版执行语义逐帧等价硬门禁；T24 PASS 前
T16 保持阻塞，既有战斗结果不计入最终验收。

## 验证节奏

开发期不把最终验收矩阵当作日常测试循环：

1. 小范围源码或文档改动，只跑直接相关的语法检查、源守卫、单元测试或
   一个精确 test filter；不启动冷进程矩阵。
2. 修改 Runtime 控制边界、跨进程协议或实机 harness 后，在该小批改动内部冻结时
   最多跑一次针对性完整 smoke；失败先修根因，不连续新建 cohort 碰运气。
3. 多个功能、Runtime、验证脚本或基准哈希仍在叠加时，保留已有有效证据，
   延后因哈希变化而必然失效的大矩阵；不为每个中间版本重复跑 10-run。
4. 只有当本批实现、fixture、reference、比较器、包络和验收脚本全部冻结后，
   才一次性执行各控制面的正式多进程矩阵。

该节奏只减少重复测试，不降低各 Spec 的最终 PASS 条件，也不允许拼接中断或
不同哈希版本的样本冒充正式 cohort。

| ID | 状态 | 任务 | 前置 | 独立验收产物 | 门禁 |
|---|---|---|---|---|---|
| T01 | VERIFIED | [基础工程、环境指纹与证据流](T01_基础工程环境指纹与证据流.md) | 无 | build、安装包、session manifest、JSONL | 基础 |
| T02 | VERIFIED | [输入采样与注入相位探针](T02_输入采样与注入相位探针.md) | T01 | phase matrix、边沿日志、绑定恢复报告 | G0 |
| T03 | VERIFIED | [Tick 账本与时间模型探针](T03_Tick账本与时间模型探针.md) | T01 | ledger、20-run 负载矩阵、时间模型判定 | G1 |
| T04 | VERIFIED | [Movie 协议、解析器与静态校验](T04_Movie协议解析器与静态校验.md) | T01/T03 | parser tests、schema fixtures、CLI validate | 离线 |
| T05 | VERIFIED | [语义状态快照与稳定哈希](T05_语义状态快照与稳定哈希.md) | T01 | canonical vectors、hash/diff tests、实机样本 | G2 前置 |
| T06 | VERIFIED | [输入录制与回放引擎](T06_输入录制与回放引擎.md) | T02/T03/T04 | record→edit→replay movie、恢复报告 | MVP |
| T07 | VERIFIED_LOCAL | [冷启动确定性验证与 Desync 报告](T07_冷启动确定性验证与Desync报告.md) | T05/T06 | 10-run report、first-diff bundle | G2 |
| T08 | REOPENED_BY_T24 | [暂停与受控步进](T08_暂停与受控步进.md) | T03 | step ledger、状态恢复报告、无额外 Unity frame 的逐 tick 差分 | G3 前置 |
| T09 | REOPENED_BY_T24 | [持久化重放存档与定时检查点](T09_持久化重放存档与定时检查点.md) | T06/T07/T08 | 任意/定时存档目录、跨启动 10-run 恢复报告 | G3 |
| T10 | VERIFIED | [RNG 诊断与构建白名单](T10_RNG诊断与构建白名单.md) | T03/T07 | RNG ledger、白名单、首差异报告 | 诊断 |
| T11 | VERIFIED | [语义 Inspector 与叠层](T11_语义Inspector与叠层.md) | T03/T05/T07 | overlay、watch export、性能报告 | 工具 |
| T12 | REOPENED_BY_T24 | [Companion/Studio、本地 IPC 与 Mod 自动启动](T12_Studio与本地IPC.md) | T04/T07/T09/T11 | 原 signed bundle/IPC soak 保留；新增 cold-restore supervisor 与跨 session lineage | G4/体验 |
| T13 | REOPENED_BY_T24 | [外部原生能力层与进程级验证](T13_外部原生能力层与进程级验证.md) | T07/T12 | 原 observe 已验证；startup clock 发布能力与双进程对称门禁待完成 | G4/实验 |
| T14 | DOWNGRADED_REPLAY_ONLY_VERIFIED | [语义关键帧与短尾重放加速](T14_语义关键帧与短尾重放加速.md) | T09/T10 | canonical protocol/store、ReplayOnly tier、实机降级矩阵；RoomEntry 未启用 | G3A/可选 |
| T15 | VERIFIED | [外部自动化与 AI 辅助接口](T15_外部自动化与AI辅助接口.md) | T10/T12 | Automation API、SDK/CLI、stdio MCP bridge、typed state mutation、control parity/AI loop/security report | G5/自动化 |
| T16 | BLOCKED_BY_T24 | [自编 TAS 端到端最终验收](T16_自编TAS端到端最终验收.md) | T07/T09/T12/T15/T17-T24 | 非视觉接口审计、自编 movie、诸神堂椅子到调谐假骑士 5-run 证据 | G6/最终验收 |
| T23 | IN_PROGRESS | [协议化确定性 RNG 回放基线](T23_协议化确定性RNG回放基线.md) | T04/T06/T10/T15/T19 | manifest 绑定 seed、top-level replay 重置、冷启动 RNG 证据 | G6/确定性阻塞项 |
| T24 | IN_PROGRESS | [原版执行语义逐帧等价门禁](T24_原版执行语义逐帧等价门禁.md) | T02/T03/T05/T06/T08/T15/T22 | 无 TAS/被动/人工/AI/逐帧/批量逐 tick 差分与 mutation 审计 | G0R/T16 硬门禁 |

## 执行顺序

1. 先实现 T01。
2. T01 通过后，T02/T03/T04/T05 可并行。
3. T02 与 T03 任一失败时，不进入 T06。
4. T07 通过后才允许把功能描述为“已验证重放”。
5. T08-T15 不得反向放宽 T07 的确定性门禁。
6. T14 只能在 T09/T10 通过后作为可选加速实现；缺失、损坏或失步必须自动回退 T09。
7. T12 必须先证明 Companion 的固定路径、签名/哈希、单实例和失败降级；T13 才能启动。
8. T13 按 capability 独立判定；单项失败标记 `unsupported`，不阻塞 Runtime/T09/T14。
9. T15 必须默认只读；控制能力只有在用户批准、单一短期租约和 typed command 下启用，失败时降为 `ReadOnly/Disabled`，不阻塞 Runtime/T12。
10. T15 的 typed state mutation 只能通过显式 writer adapter 在暂停安全点执行，并把运行标记为 `NonVerifiableDebugMutation`；不得进入 T07 oracle 或 T16 计分运行。
11. T16 是项目最终完成门禁：必须由本项目外部接口驱动、自行创作输入 movie，并从诸神堂椅子真实冷启动到击败调谐难度假骑士；不得用截图、OCR、像素、视频或人工中途输入代替结构化状态。
12. T24 是 T16 的先决硬门禁：同一 logical input 在无 TAS、人工 TAS 与 AI TAS
    中必须逐 gameplay tick 保持原版动画、物理、FSM、碰撞与资源语义等价；
    正常 TAS 路径只允许输入注入、完整 tick 边界控制和基于原版 baseline 的回退。
    回退必须走 fresh-process `VanillaEquivalentColdReplay` 并比较完整 trace；同进程
    `FunctionalReplayRestore` 只能作为显式诊断，不能计入 PASS。
    T24 未通过时暂停战斗策略优化，任何 Boss 击杀与重放成功均不计分。

## 通用证据规则

- 所有实机测试必须记录游戏构建、API 版本、关键 DLL hash、Mod manifest、设置 hash、baseline hash、OS 和测试次数。
- 原始证据必须是可复制的 movie、JSONL、manifest、hash diff 或日志；截图只能补充，不能代替。
- 测试 profile 只允许 Modding API + HollowKnightTAS；加入其他 Mod 必须另建兼容性矩阵。
- 每个任务只在自身 Spec 的 PASS 条件全部满足后标记完成。
- 外部进程、NativeHost 和 Bridge 的路径/参数不得来自 movie；所有 release capability 必须绑定签名 bundle 与精确构建指纹。
