# D14 AI 世界观察与碰撞箱

## 用户要求与当前状态

2026-09-26：用户暂停嫉妒马尔穆 TAS，要求先补齐正式 AI 接口：角色位置和状态、Boss 位置和 FSM、障碍物大小与位置等，并在 Studio 设置中增加碰撞箱显示/隐藏开关，参考 Debug Mod；验证后本地提交。

EnviousMarmu 已安装，仅作为动态多敌人验证用例。TAS 尚未制作，游戏尚未启动。原先独立临时观察器已撤下，TAS 全局配置已恢复。未跟踪的 `scripts/ModBossObserver`、`Author-ModBossMovie.py` 是上一任务准备产物，不能混称正式接口。

## 设计

- 新增正式只读 `getWorldSnapshot` 与 `getObjectDetails`，`observe.state.deep` 权限；CLI、SDK、MCP 共用相同协议。
- 世界概览：带唯一实例 ID 的场景对象，角色状态/资源/护符/能力，位置/速度/朝向，敌人血量与伤害属性，FSM 状态和变量，碰撞体类型/位置/范围/真实轮廓、触发器与启用状态。覆盖动态生成、销毁及同名对象。
- 对象详情：组件类型、公开/私有实例字段的纯数据，以及 FSM 状态图、转移、当前动作与计时字段。只读取字段，不调用任意属性 getter、对象方法或修改状态。任意 Mod 的语义不可能自动推断；无法编码、资源限制和遗漏必须明确返回。
- 在 Unity 主线程的原生帧边界按需采集；暂停查询通过独立只读唤醒通道服务，不推进 PlayerLoop/Movie，不修改时钟/RNG，不每帧全场景深度采样。
- 返回原生帧、Movie 帧、scene、采样阶段。概览保留不可变快照并分页，过期 ID 明确拒绝；详情可分段读取，支持预期帧检查。
- 碰撞箱使用相同几何数据，在 Companion 拥有的透明、不抢焦点、鼠标穿透窗口中绘制；暂停也能立即显示/隐藏。跟随游戏客户区与 DPI，游戏退出/关闭设置后清理。设置默认关闭，本地持久化，中英界面一致。
- 对曲线细分与不支持的类型标明近似或原因，不能用 AABB 冒充实际轮廓。以 Debug Mod 的形状/分类绘制思路为参考。

## 协议

`getWorldSnapshot` 参数：`snapshotId?`、`view=world|all|colliders`、`includeInactive=false`、`offset=0`、`limit=64`（1–128）。首请求采集，后续传 snapshotId 读取同一快照。结果含 `snapshotId/nativeFrame/movieFrame/snapshotJson`，页面是完整 JSON。超大对象概览给出明确详情引用。

`getObjectDetails` 参数：`objectId`、`expectedNativeFrame?`、`detailsId?`、`cursor=0`、`maxCharacters=100000`（1024–200000）。结果含 `detailsId/objectId/nativeFrame/movieFrame/detailsJson/cursor/nextCursor/totalCharacters/complete/sha256`；详情片段合并后用完整 UTF-8 SHA-256 校验。详情首采集后缓存，分页不会混入新帧。

## 验收

1. 原生暂停观察通道：重复查询不增加原生帧/Movie/输入更新，不执行游戏循环；运行查询在安全边界处理；退出/故障/无回调安全拒绝。
2. 协议：只读权限、v2 路由、分页边界/过期/超大对象/详情完整性及 invalid args 的定向检查。
3. 数据：角色资源与状态、实际 FSM/变量、各类形状、动态克隆及场景切换；未知/未支持信息明确暴露。
4. UI：开关持久化与中英标签；运行/暂停时显示、隐藏；窗口退出清理；不抢焦点、不改变屏幕分辨率。
5. 安装版实机：标题、神居、Boss 场景；对比观察与绘制开/关的同输入轨迹，检查 fault/mismatch 与原始存档 SHA-256。
6. Release 构建、签名安装、安装验真。各完整阶段本地提交，不推送。

当前为实现中，尚无 PASS 声明。
