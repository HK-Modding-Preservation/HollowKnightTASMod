# 全流程世界观察

全流程 v2 使用 `getWorldSnapshot` 与 `getObjectDetails`（权限 `observe.state.deep`）。两者只读，不需要控制租约；普通游戏/v1 会话明确返回 Unsupported。SDK、CLI、MCP 走同一个 Runtime 接口。

查询在 Unity 主线程的完整帧边界执行。暂停时通过独立观察事件唤醒，不运行 PlayerLoop、不消耗 Movie 输入、不更改时钟或随机数。运行时返回实际采样帧；需要相同帧的多次读取，应先暂停并传 `expectedNativeFrame`。

## 世界概览

```powershell
HollowKnightTAS.Cli.exe automation call getWorldSnapshot observe.state.deep view=world includeInactive=false offset=0 limit=64
```

MCP：`hktas_get_world_snapshot`。SDK：`GetWorldSnapshotAsync`，或使用 `GetWorldSnapshotJsonAsync` 自动翻页得到完整 JSON。

参数：`view=world|all|colliders`、`includeInactive=false`、`offset=0`、`limit=64`（1–128）、`snapshotId?`。

- `world`：角色、敌人、FSM、碰撞体和带游戏逻辑组件的对象。
- `all`：已加载场景的完整对象目录，包括在已发现持久场景中的对象；`includeInactive=true` 同时返回未激活对象。
- `colliders`：当前游戏相机视野内有效的碰撞轮廓，供显示层使用。

结果字段 `snapshotJson` 是完整 JSON 页面，含 `metadata`、`objects`、`total`、`nextOffset`；`-1` 为末页。后续页携带同一 `snapshotId`，不会混入新帧。保留最多 4 份概览，合计 3200 万字符；过期会明确报错。单个超大对象返回 `detailsRequired` 和对象 ID，用详情接口获取完整内容。

对象以会话、场景和 Unity 实例标识区分，同名克隆不会合并。数据包括变换、父子关系、激活/启用状态、速度、血量/无敌、接触伤害、角色资源/护符/能力/计时、FSM 当前状态和变量、碰撞形状及屏幕投影。场景切换或对象销毁后必须重新取目录，不能把缓存快照视作当前状态。

概览中的对象字段是受预算约束的 core 投影。它保留已知原生核心字段和可安全读取的实例字段，但不会把遗漏字段扩展、猜测或静默填成默认值。字符串、集合、组件字段、未支持类型或预算超限时，投影必须原样保留 `errors`、`omitted`、`omittedCount` 及对应的遗漏原因；需要更多内容时再用对象详情查询。概览缓存最多保留 4 份快照，合计最多 32,000,000 个字符，淘汰或过期都应显式报错。

`view=colliders` 是显示用刷新流。新的碰撞页应用前，显示层先回收旧的 collider display 条目，再安装当前 `snapshotId` 的完整页集合；不能把每轮轮廓追加到世界概览缓存，否则碰撞刷新会挤出仍可用的 world 快照。分页必须固定在同一 `snapshotId`，并在完整替换前拒绝过期页、重复页或不单调的 `nextOffset`。

## 对象详情

```powershell
HollowKnightTAS.Cli.exe automation call getObjectDetails observe.state.deep objectId=<概览返回的ID> expectedNativeFrame=123 cursor=0 maxCharacters=100000
```

MCP：`hktas_get_object_details`。SDK：`GetObjectDetailsAsync`，或 `GetObjectDetailsJsonAsync` 自动拼接并校验 SHA-256。

返回完整概览和组件字段，包括已加载 FSM 的全部状态、转移、动作字段及当前动作计时。未加载或不可用的 FSM 只返回明确状态和遗漏原因，不为了补齐数据触发加载、初始化或任何副作用。查询仅读取实例字段和已知原生状态，不执行任意 Mod 的属性 getter、方法、枚举器或递归对象图。

大型详情以 `detailsId`、`cursor`、`nextCursor` 分段，最后一段 `nextCursor=-1`、`complete=true`。`detailsJson` 单段不一定是完整 JSON；按顺序拼接后用 `totalCharacters` 和完整 UTF-8 `sha256` 校验，再解析。后续段传相同 `objectId/detailsId`，缓存不会跨帧变化。

## 数据边界

“可见全部对象”不等于自动理解任意 Mod 的规则。字段不可用、未初始化的 FSM、未支持的数据类型、集合长度或字段预算限制会返回明确的遗漏原因；请检查 `errors`、`omitted`、`omittedCount` 和元数据中的范围。原生组件不暴露的内部数据不能凭空推断。

碰撞数据同时提供原始形状参数、世界轮廓、范围、触发器和图层。屏幕坐标相对整个游戏渲染视口，左上 `(0,0)`、右下 `(1,1)`，不是桌面坐标。曲线、圆弧和圆角用折线近似，误差和近似来源写在 `geometry`；AABB 只表示范围，不能冒充真实轮廓。附着 Rigidbody 的物理位姿修正已实现，碰撞几何按当前物理位姿输出；静态 Transform 读取不调用 `Physics2D.SyncTransforms`，因此不会为了观察强制同步物理世界。逐对 IgnoreCollision 规则、接触流形和 Mod 自定义碰撞判定不保证由几何数据完整表示。

Studio「设置 → 显示碰撞箱」默认关闭，可持久化。显示层复用此接口，只画轮廓，不修改游戏碰撞器；游戏未连接、最小化或退出时隐藏。

## 受保护 Mod 与来源边界

Runtime 的 `ReviewedProtectedMods` 是精确白名单，不代表“所有 Mod 都已审查”。除本 Mod 自身外，当前允许 shadow-save redirector 的外部 Mod 仅限名称 `EnviousMarmu`，且程序集 SHA-256 必须严格等于：

```text
3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673
```

碰撞箱的形状、分类和场景生命周期参考 [DebugMod HitboxRender](https://github.com/TheMulhima/HollowKnight.DebugMod/blob/ea4c07de0c93ffee3518e07d6b0351188f7f3293/Source/Hitbox/HitboxRender.cs)（固定提交 `ea4c07d`）。本实现通过只读数据在 Companion 绘制，并额外处理 Rigidbody 物理位姿、Capsule 和 Composite；未启用 DebugMod，也未将其加入受保护运行白名单。参考核对记录保存在 `artifacts/observer-review/debugmod-reference.md`。
