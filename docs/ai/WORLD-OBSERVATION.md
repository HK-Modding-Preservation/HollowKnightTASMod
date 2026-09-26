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

## 对象详情

```powershell
HollowKnightTAS.Cli.exe automation call getObjectDetails observe.state.deep objectId=<概览返回的ID> expectedNativeFrame=123 cursor=0 maxCharacters=100000
```

MCP：`hktas_get_object_details`。SDK：`GetObjectDetailsAsync`，或 `GetObjectDetailsJsonAsync` 自动拼接并校验 SHA-256。

返回完整概览和组件字段，包括 FSM 状态图、转移、当前动作及计时字段。仅读取实例字段和已知原生状态，不执行任意 Mod 的属性 getter、方法、枚举器或递归对象图。

大型详情以 `detailsId`、`cursor`、`nextCursor` 分段，最后一段 `nextCursor=-1`、`complete=true`。`detailsJson` 单段不一定是完整 JSON；按顺序拼接后用 `totalCharacters` 和完整 UTF-8 `sha256` 校验，再解析。后续段传相同 `objectId/detailsId`，缓存不会跨帧变化。

## 数据边界

“可见全部对象”不等于自动理解任意 Mod 的规则。字段不可用、未初始化的 FSM、未支持的数据类型、集合长度或字段预算限制会返回明确的遗漏原因；请检查 `errors`、`omitted`、`omittedCount` 和元数据中的范围。原生组件不暴露的内部数据不能凭空推断。

碰撞数据同时提供原始形状参数、世界轮廓、范围、触发器和图层。曲线用折线逼近，特殊缩放/圆角的误差在 `geometry` 中标记；AABB 不冒充真实轮廓。屏幕坐标相对整个游戏渲染视口，左上 `(0,0)`、右下 `(1,1)`，不是桌面坐标。逐对忽略碰撞和 Mod 自定义碰撞判定不保证由几何数据完整表示。

Studio「设置 → 显示碰撞箱」默认关闭，可持久化。显示层复用此接口，只画轮廓，不修改游戏碰撞器；游戏未连接、最小化或退出时隐藏。
