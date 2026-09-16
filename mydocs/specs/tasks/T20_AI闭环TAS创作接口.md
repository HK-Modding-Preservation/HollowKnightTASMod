# Task Spec T20: AI 实时观察与输入接口

- **Status**: READY_FOR_IMPLEMENTATION
- **Gate**: G6 Product Completion
- **Depends On**: T04, T06, T11, T15, T19, T21, T22
- **Produces**: 规范化战斗快照、lease 约束的短输入批次、可回放的 authoring movie

## 1. Goal

外部工具或 AI 必须能在不读取画面、不访问 Runtime 私有对象、不注入物理键盘
事件的情况下，创作 TAS：读取当前场景、Hero 与 Boss 的结构化
状态，决定接下来少量 input ticks 的动作，提交动作，再读取结果并继续决策。

实时创作产生的每一个 input sample 都必须进入与 T04 相同的 canonical movie
语义和 T15 Automation 审计；完成后可以冻结为普通 `.hktas`，并在最终 scored
run 中仅靠静态 movie 回放，不保留 authoring 时的动态写控制。

## 2. Problem Statement

当前已通过共享 CLI 的 `getCombatState` 与 `queueInputBatch` 完成实时观察、短批次
输入及完整击杀 Movie 导出。开发流程证据见
`artifacts/interactive-origin-smoke/false-knight-development-20260914.md`。
完整自动控制器、历史修正后的正式冷重放和原版等价验收仍待完成。

同时，`bossPractice.boss.fsmsJson` 虽然信息完整，却是面向诊断的 JSON 字符串；
决策器不应猜哪个 FSM 是主战斗 FSM，也不应自行从 object path 和 collider 列表
推导相对方向、距离和接触范围。

## 3. Normalized Combat Snapshot

在 T15 canonical state/schema 中新增稳定、带 freshness 的只读字段：

```text
combat.primaryBoss.available
combat.primaryBoss.objectPath
combat.primaryBoss.hp
combat.primaryBoss.dead
combat.primaryBoss.position.x
combat.primaryBoss.position.y
combat.primaryBoss.velocity.x
combat.primaryBoss.velocity.y
combat.primaryBoss.facing
combat.primaryBoss.mainFsm
combat.primaryBoss.mainState
combat.primaryBoss.recentEvent
combat.primaryBoss.collider.center.x
combat.primaryBoss.collider.center.y
combat.primaryBoss.collider.extents.x
combat.primaryBoss.collider.extents.y
combat.relative.delta.x
combat.relative.delta.y
combat.relative.distance
combat.relative.horizontalSide
combat.contact.overlap
```

所有字段与同一 watch frame 的 `movieTick`、`inputTick`、`sceneEpoch`、`phase`、
`fresh` 和 `ageMovieTicks` 一起返回。Boss 不存在时用显式 `available=false` 和协议
规定的空值，不复用上一帧的陈旧对象。

主 FSM 选择规则必须由 Runtime provider 固定并测试；允许同时保留完整
`bossPractice.boss.fsmsJson` 作深入诊断，但规范化字段不得要求客户端解析该字符串。

### 3.1 多部位生命与实际碰撞体

- `combat.primaryBoss.healthManagersJson` 列出主体及子对象的 HealthManager：
  `objectPath/objectName/activeInHierarchy/enabled/hp/dead/invincible/invincibleFromDirection`。
  以对象路径关联头部等部位，主体 HP 不能代替头部 HP。原始无敌字段不等于具体攻击必然命中；
  仍需结合部位激活、实际碰撞体和攻击类型。
- `combat.hero.collidersJson` 使用原生 Collider2D 世界坐标包围盒，包含
  `boundsAvailable` 和 `primaryHeroCollider`；控制器应使用标记的有效主体碰撞体，
  不得以角色脚部坐标加固定偏移宣称准确碰撞范围。
- `combat.hazardsJson` 的摘要选择包含 Trigger，并提供 `colliderAvailable`、
  `colliderIsTrigger`、`boundsKind` 及完整 `colliders` 数组。无可用主体碰撞体时
  `boundsKind=Unavailable`；零尺寸占位不是安全证明。目录中的 `overlapHero`
  仍表示世界轴对齐包围盒相交，不是精确多边形接触或必然扣血。
- 上述新增字段已编译及通过源码约束检查，尚未安装、实机核验；必须覆盖倒地头部、
  Trigger 武器和 Hero 主体，验证同一帧的原始组件与输出一致。

## 4. Frame Input Control

实时控制必须复用 T21 的单帧原子输入与任意长度多帧输入事务，而不是另造只给
AI 使用的受限子集：

- 仅在 `ExternalAutomationMode=ApprovedControl` 时可用；
- 必须持有当前 exclusive lease；
- 请求必须携带 `expectedMode`、`expectedMovieTick`、`expectedSceneEpoch`；
- 单帧可执行 `set-next-input + step-one-input-tick` 原子操作；
- 多帧通过分块事务提交，整体长度受 T04 movie 上限约束，不受单次 IPC payload
  大小限制；
- payload 先经 T04 parser/validator/canonicalizer，未知动作、相反方向、超限和陈旧
  compare-and-set 全部 fail closed；
- Runtime 只在 gameplay gate 与 pause/step gate 允许时消费动作；
- 每次响应返回 batch/transaction hash、接受 tick、消费 tick 数、当前状态 hash和
  剩余队列长度；
- lease 过期、客户端断开、scene epoch 意外变化、stop 或异常时立即清空队列并提交
  neutral release，不能留下 held input；
- 命令不修改 PlayerData、Boss HP、FSM、scene 或内存地址，不标记
  `NonVerifiableDebugMutation`；它属于受审计的输入控制。

## 5. Authoring Session

新增显式 `beginAdaptiveAuthoring` / `endAdaptiveAuthoring` 生命周期，或提供等价的
单一模式机；人类 UI 与 AI API 均通过 T22 的同一控制内核执行：

1. acquire lease；
2. begin authoring，Runtime 建立空的 T04 input recorder；
3. 循环 `getCombatState -> stepWithInput/commitInputBatch -> getCombatState`；
4. 每个 batch 的请求、响应、输入 hash、观察帧与因果 tick 写 Automation audit；
5. end authoring 返回 canonical movie、movie ID、expanded ticks 和 validation；
6. 客户端把路线前缀与战斗录制片段合并为隔离 movie branch，再显式 apply。

同一时刻禁止普通 replay 与 adaptive authoring 并行。authoring 期间可以 pause/step，
但不得使用 typed mutation；一旦发生 mutation，输出 movie 保留，但 session 永久不具备
最终验证资格。

## 6. Reference Controller

项目必须提供一个外部参考控制器，至少实现：

- 基于 `delta.x`、Boss 主状态、Hero/Boss collider 和 Hero 状态选择接近、转身、
  攻击、跳跃、冲刺或等待；
- 决策日志记录输入状态摘要、规则 ID、输出 batch 和结果 tick；
- 不调用截图、OCR、Computer Use 或物理键盘；
- 策略参数可配置，但所有动作只通过 `queueInputBatch`；
- 能把一次成功的观察与输入运行导出为 canonical `.hktas`。
- 实际使用 T21 的 replay save、向后 seek 和历史输入替换修正至少一个失败片段。

参考控制器是验收工具和示例，不承诺成为通用自动打 Boss 的 AI。

## 7. Verification

### 7.1 Observation

- 在假骑士静止、跳跃、冲锋、砸地、硬直/倒地和死亡状态各采样一次；
- 规范化位置、速度、主 FSM/state 与 Runtime 原始组件/FSM 同帧一致；
- Hero/Boss 相对方向、距离和 collider overlap 用固定 fixture 做边界测试；
- Boss 销毁或场景切换后不得返回陈旧状态。

### 7.2 Control

- AI 根据 Boss 位于 Hero 左/右两侧分别生成不同输入，证明确实使用实时状态；
- 连续至少 500 ticks 的 observe/decide/act 不丢 tick、不重复输入；
- stale tick、错误 scene epoch、超出 T04 上限、无 lease、ReadOnly 模式全部拒绝；
- lease 过期、Companion 被杀和场景切换中断均恢复 neutral input。

### 7.3 Recording and Replay

- authoring 输入记录与 audit 中全部 batch 展开后逐 tick 相同；
- 导出的 movie 通过 T04 parse/validate/canonicalize；
- 在独立冷启动中不启用 adaptive authoring，仅回放冻结 movie，输入 ledger 一致；
- T16 的最终候选必须记录至少一次“Boss 左右位置导致不同决策”的实际输入证据。

## 8. PASS

- 外部 AI 能以稳定结构化字段反复执行 observe/decide/act；
- 控制是短批次、lease 约束、CAS 防陈旧、可审计且断线自动释放的；
- 全过程无视觉识别、无任意状态写入；
- 实时创作的成功输入能导出为 canonical movie，并能在干净 session 静态重放。

## 9. FAIL

- 仍然只允许整段写死 movie 后看结果；
- 客户端必须解析任意 `fsmsJson` 才能判断基本 Boss 动作；
- 输入控制绕过 T04、lease、expected tick/epoch 或 Automation audit；
- 断线留下 held input；
- 最终运行依赖截图、实时人工按键或 Boss/Player 状态修改。

## 10. Rollback

`queueInputBatch` 使用独立 authoring 模式和现有 input adapter；移除该模式不会改变
T04 静态 movie 或 T06 replay 语义。任何失败 session 先 neutralize input、释放 lease，
再保留 audit 与未应用 movie branch，不覆盖用户 movie 或存档。
