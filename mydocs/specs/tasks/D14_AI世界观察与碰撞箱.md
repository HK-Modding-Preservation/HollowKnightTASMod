# D14 AI 世界观察与碰撞箱

## 用户要求与当前状态

2026-09-26：用户暂停嫉妒马尔穆 TAS，要求先补齐正式 AI 接口：角色位置和状态、Boss 位置和 FSM、障碍物大小与位置等，并在 Studio 设置中增加碰撞箱显示/隐藏开关，参考 Debug Mod；验证后本地提交。

已实现、安装并通过下述范围的最终验收。EnviousMarmu 已安装，仅用于自定义组件/运行时生成对象观察验收，速杀 TAS 尚未制作。原先独立临时观察器已撤下并保存在 artifacts/envious-marmu-tas/preparation；生成验收输入的小工具现为 scripts/WorldObservationHarness/Author-Fixture.py，正式采集通过 Runtime 接口完成。

## 设计

- 新增正式只读 `getWorldSnapshot` 与 `getObjectDetails`，`observe.state.deep` 权限；CLI、SDK、MCP 共用相同协议。
- 世界概览：带唯一实例 ID 的场景对象，角色状态/资源/护符/能力，位置/速度/朝向，敌人血量与伤害属性，FSM 状态和变量，碰撞体类型/位置/范围/真实轮廓、触发器与启用状态。覆盖动态生成、销毁及同名对象。
- 对象详情：组件类型、公开/私有实例字段的纯数据，以及 FSM 状态图、转移、所有已加载状态的动作字段、当前动作与计时字段。只读取字段，不调用任意属性 getter、对象方法或修改状态；未加载动作不触发加载。任意 Mod 的语义不可能自动推断；无法编码、资源限制和遗漏必须明确返回。
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

2026-09-26 补充：用户允许碰撞箱显示开关在下一帧生效；AI 暂停查询仍必须即时可用且不推进帧。

## 最终验收（2026-09-26）

- 原生门闩27/27、Core定向19/19、Companion定向17/17、只读字段/形状/IL检查27/27、FSM与真实概览9/9、物理位姿11/11通过。Runtime Release零警告零错误；Core测试构建存在既有Windows平台分析警告。FSM独立脚本需 Windows PowerShell（powershell.exe），已显式声明。
- 最终安装 Runtime `2a8e1935bb7bbc2b1bf6feea03317d73dc59d3697691e4847b2703fb49eef3c9`，Core `2fdb1ed4962670024239d9a7403fb525765d10dbfd00b6836626e1ad9a9ab59e`。下面三次运行均为此二进制组合。
- `baseline-final` 与 `observe-final`：均完成假骑士 Movie 10892，fault/mismatch=0。后者在标题1、神居1500、Boss8500暂停查询；所有查询保持原生帧不变，分页保持同一快照及帧。角色概览保留位置与资源，完整FSM动作通过详情读取。碰撞箱实际显示、暂停隐藏/重开及离屏PNG渲染通过。
- 同输入的2828行战斗轨迹，按Movie帧对齐，scene/heroX/heroY/heroHealth/bossHp/bossX/bossY/bossDead/deltaTime/rngSha256全部一致。nativeFrame/frameCount/time/fixedTime受加载循环影响有偏移，记录为时序差异，不混称全部列相同。早期不同DLL的候选对比不计入最终验收。
- `marmu-final`：Movie8250的 `GG_Ghost_Marmu` 读到 MarmuNode 巨型层级、208 HP、MarmuBattleManager 存活数1/基准HP208/模板引用；`view=all, includeInactive=true` 找到并读取 `Marmu Template`。8335帧结束，fault/mismatch=0。此验收没有击杀Boss，没有验证完整分裂战斗路线。
- 已签名安装并验签。31个原始user*文件的集合、长度与SHA-256差异0；测试游戏/Companion/验收进程全部关闭。未使用桌面自动化，未改分辨率；最终宿主峰值约489MiB、游戏约1.61GiB。

## 实测修正与边界

- 超大角色概览原先退化成ID占位，现有界保留核心字段并明确遗漏，详情仍可分段读取。高频碰撞显示优先回收旧显示快照，避免挤出正在分页的AI world快照。
- 显示Transform与物理Rigidbody插值有差异；轮廓现以物理位置/旋转修正，仅读取，不调用SyncTransforms、不分配Unity原生对象。无Rigidbody的静态对象保留Transform来源标记。圆/胶囊/圆角及特殊缩放有明确近似标记。
- 受保护运行原先拒绝所有外部Mod。对本机EnviousMarmu源码和安装DLL审阅后，仅放行名称和SHA-256精确匹配的这一版（`3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`）；其他Mod仍需单独审阅，未削弱存档保护。
- 未声称任意Mod语义、引擎未公开数据、逐对IgnoreCollision、接触流形或自定义碰撞规则自动完整。检查errors/omitted/元数据；旧缓存不代表当前场景。未进行独占全屏、跨DPI显示或所有Boss回归。
- 详细接口见 docs/ai/WORLD-OBSERVATION.md；最终证据、哈希、查询、截图和轨迹对比见 artifacts/world-observation/REPORT.md。DebugMod固定源码参考见 artifacts/observer-review/debugmod-reference.md。
