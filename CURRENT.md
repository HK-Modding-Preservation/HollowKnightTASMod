# 当前进度：全流程 Movie、加载省帧与新版假骑士示范已通过冷启动回放

2026-09-25 Studio 退出入口补齐：主界面新增 `Quit Game 退出游戏`，全流程运行中先暂停到原生帧边界再退出；第 0 帧 Runtime 尚未连接或原生门闩报错时，只关闭 Studio 所持有的受保护游戏进程；Movie 已完成后按钮仍可用。`Stop` 仍只停止序列，`File → Exit` 只关闭 Studio。定向后台测试 4/4 通过（含第 0 帧回调、Completed 命令可用性、WPF 布局与命令绑定），Release 构建 0 警告，签名安装验真。当前 Companion manifest SHA-256 `04872a9b5c419d3080681bf9da60dd8836dcfb4ffac6d367bf003756fb69a647`，安装 Companion DLL `a0c404a6e37effbaa1d15368c26f9f1bd33712b219aae493097e2d8d09c6b6ab`、Runtime `0e564961634e9b5656abc6e5d8cb46e0f28c4bd37630947bbf63383603a88b25`、Core `55668815cc8ed5c28ec9affb23b3e15af4ada85f37b0ddaf36793dbb56b6f946`。用户正在使用电脑，本轮未启动游戏或抢占前台；该新安装版的界面点击和假骑士回放尚未实机复验，下文历史实机结论限于各自当时的安装身份。

2026-09-24 Studio 输入表格跟随已修复并安装：播放／逐帧时默认按逻辑 Movie 帧滚动当前行，跨 500 行自动换页；可关闭“播放时跟随”停在别处查看，并用“当前帧”手动定位。全流程顶部同时显示 Movie 帧和原生帧。Companion 定向测试 2/2 通过，安装包验签通过，当前 Companion manifest SHA-256 `de563cda3a37dc64c4b8bcdb31e5f498a2cdb06c089c62d20da5379a0c9364fb`。从 Steam 第 0 帧打开正式示范 `fixtures/full-run/false-knight-startup-v2.hktas`，两次完整回放都由表格自动跟到末帧；最终一次 Movie 10892/10892、原生 12024、跳过 1089 个不可输入循环、输入偏差 0，Boss 死亡／战场完成事件成立，终点 `GG_Workshop`。本次受保护会话 `full-run-2af3e448cecd4c378adba6db2e1bd567` 的原始四槽 20 个文件与起始描述符文件集及 SHA-256 差异为 0；游戏和 Studio 已正常退出。

2026-09-24 生命周期补充验证：`RuntimeFullRunSession` 在原生 `GameManager.ReturnToMainMenu` 开始后跳过保存、淡出及 `Quit_To_Menu` 的不可输入循环，直到标题菜单重新可输入。`artifacts/full-run-false-knight/lifecycle-smoke-v4-partial.hktas` 从 Steam 原生第 0 帧录制，经第 4 槽加载、保存退出、重载、再次保存退出；59715 Movie 帧冷启动回放 `Completed`，跳过 2364 原生循环，输入偏差 0。此前只凭 `Quit_To_Menu` 场景名跳帧的 v3 在 Movie 31454 帧失败，证据不再视为通过。

新增 `artifacts/full-run-false-knight/lifecycle-smoke-v5.hktas`（Movie ID `aedacfc4179fce1dde05f23510544cf6ab63376bf1a34a33d05f27de454a4ae0`，197959 帧），真实键盘路线为 Steam 第 0 帧→第 2 槽加载并保存退出→第 4 槽加载并保存退出→在受保护的影子第三槽清档、创建经典模式新档→进入 `Tutorial_01` 并保存退出。首次冷启动在新档开场第 168633 帧遇到同一原生循环中多一次未变化 Hero 输入更新；`FullRunActionSetAdapter` 现允许复用末尾无按键边缘样本，并继续核验值与边缘。修复后原 Movie 冷启动回放 `Completed`，197959/197959 帧、原生帧 204603、跳过不可输入循环 6604、输入偏差 0，终点 `Menu_Title`。回放会话原始四槽 20 个文件的文件集与 SHA-256 均未改变；影子第三槽 `user3.dat`、`user3.modded.json` 已变化。当前 Release Runtime 安装哈希为 `bbb6da318b5c5322de88f3860b347b3f304b2d5aadeea38a3e4130e58c9d88b9`。Studio 新 Movie 游戏鼠标设置已恢复启用。

正式假骑士示范在此 Runtime 再从 Steam 原生第 0 帧由 Studio 打开冷回放：`Completed`、10892 帧、原生帧 12120、跳过 1186 个不可输入循环、输入偏差 0，原生 Boss 死亡和战场完成事件成立，终点 `GG_Workshop`。战场入口仍为 Movie 帧 8063；2828 行战斗轨迹的 10 个语义字段与此前 VERIFIED 冷启动逐行比较差异 0。原始四槽 20 个文件的文件集与哈希未改变。收尾证据见 `artifacts/full-run-false-knight/lifecycle-verification-v5.json`。测试后游戏和 Studio 已退出，Steam 恢复“开始游戏”。其他配置下未激活的 PreMenu／Binder 分支及可点击 UI 的鼠标响应还没有相同等级的实机证据。

2026-09-24：旧实现基线为 d31bf86，用户已批准 Selected B 并要求继续全部执行。v2 协议、Runtime、Studio/CLI 控制、原生门闩、影子存档及原路径写入守卫已接通。正式示范为 `fixtures/full-run/false-knight-startup-v2.hktas`，10892 个逻辑 Movie 帧，SHA-256 `20c3182ad01e2929799c207b821fbae6fb8599498e9da37e353bee9b35bc70b1`。它从 Steam 启动原生第 0 帧开始，经真实标题菜单输入选择第 4 槽诸神堂存档，进入 `GG_False_Knight`，击败 Boss 后返回 `GG_Workshop`；战斗输入由 AI 生成并走与人工相同的真实输入路径。旧试验 Movie 和旧 v1 归档仍保留，不能与正式示范混称。

2026-09-24 载入时长修订已验收：只给可接受游戏输入的 PlayerLoop 计 Movie 帧，加载循环仍执行但不消耗输入序列；Step 持续至一个 Movie 帧完成。每个新场景首个输入边界由外部 ClockPayload 同步 Unity 随机种子，渲染 FastNoise 使用独立随机流；v2 profile 为 `hktas-unity-input-playerloop-load-elision-scene-rng-2026-v3`。同一正式示范两次普通冷启动与一次单核、BelowNormal 压力启动均 `Completed`，样本偏差 0、原生 Boss 死亡事件成立、返回 `GG_Workshop`；跳过的加载／不可输入循环分别为 1060、1077、24457，战场入口同为 Movie 帧 8063。三次的 2828 个战斗 Movie 帧上，场景、角色坐标／血量、Boss 坐标／血量及 Unity RNG 状态逐帧相同。Studio 可见流程另实测在原生第 0 帧 Open Movie，单步后原生帧 46／Movie 帧 1，再点击 Play 至完成。原始四槽受保护文件 20 项与会话起始描述符的文件集、SHA-256 均一致；第 4 槽三个主文件哈希也与历史基线相同。证据入口为 `artifacts/full-run-false-knight/verification-v3.json`。该证明覆盖本机游戏版本、此存档和此序列；其他场景／Mod 组合尚无同等级压力证据。下文历史 VERIFIED 仅指各自冻结构建与旧协议。

2026-09-24 Studio 新建录制补充实测：Steam 从原生 0 帧启动，Studio 设置关闭游戏鼠标后 New full-run Movie，Step 跨启动加载到首个标题输入帧；游戏窗口左键点击未推进菜单，Return 键进入真实选档。Stop → Save 的 `artifacts/full-run-false-knight/manual-record-disabled-mouse.hktas` 静态校验通过（22875 Movie 帧），头部 `mouseEnabled=false`，没有鼠标样本。另一次冷启动开启游戏鼠标，新建录制的 `artifacts/full-run-false-knight/manual-record-enabled-mouse.hktas` 静态校验通过（18609 Movie 帧），含鼠标位置和左键按下样本；标题点击本身未触发游戏菜单跳转，不据此宣称游戏 UI 接受点击。该鼠标 Movie 从 Steam 第 0 帧在 Studio Open → Play 冷启动回放到 `Completed`，18609/18609 Movie 帧、输入偏差 0，证据 `artifacts/full-run-false-knight/manual-record-enabled-mouse-replay.json`。三次正常退出后各自会话描述符所跟踪的原始四槽 20 个文件均无文件集／SHA-256 差异。

探针两轮各从原生第 0 帧逐帧放行，Runtime 首次启动在第 2／24 帧，Hero 更新同帧，标题 UI Process 在第 3／25 帧；第二轮主菜单第 50 帧可交互。另两个预菜单动作集和 InControlInputModule 在现有配置的 200 帧中未激活，其他配置仍须定向核验。两轮原始顶层 userN 文件集、长度与 SHA-256 不变，临时 Runtime 安装文件已恢复；原始日志、注入回执与源码保留在 `artifacts/full-run-boundary-20260924/`。

2026-09-24 新需求：Movie 应从受控游戏启动第 0 帧开始，覆盖标题菜单选槽／新建档、局内操作、Save&Load 和多槽切换，并按此规则重录一份假骑士示范。用户明确要求菜单与局内共用真实游戏输入录制逻辑、Movie 不绑定存档／槽位、Mod 不修改用户原始存档；示范从标题菜单加载现有诸神堂存档。新增鼠标要求：Studio 设置可启用／禁用游戏鼠标输入；禁用时游戏不响应鼠标，键盘选槽照常可用，Studio 自身鼠标不受影响。研究、代码地图及已批准方案持久化到 `mydocs/specs/2026-09-23_21-25_全流程Movie与假骑士重录.md`。下文 D06 与旧假骑士记录是已交付／历史基线，不代表新需求已实现。

# D06 启动首帧暂停已交付

入口：`mydocs/specs/tasks/D06_启动与首帧暂停.md`。D01–D03 的 TAS 基本控制、存档/回档及示范，D04 的 MP4，D05 的 Studio 初版改善均已交付。D06 已安装：直接从 Steam 启动游戏，Mod 自动打开 Studio，在进入存档前把原标题进程移交给受控进程，默认停在原生 `Startup frame: 0`。普通启动的 Studio 按钮单步 0→1 后保持，并可继续初始化；相同门闩路径另实测按钮 0→1→2 后保持。Studio 原先关闭和原先运行两种入口均实测，Runtime `getStartupProfile` 为 `Verified`；加载已批准的第 4 槽后，暂停、保持、单步和正常退出均通过。最终安装包的再次实测使用受控进程 17788，tick 9434→9435，游戏进程退出、Studio 按设置保留且注销会话，存档 SHA-256 仍为 `1acd6214b8dacaf112a2fbae0e5e7aeb3d487c9365f5f955e4aef8e51459dade`。目前游戏和 Studio 均已关闭。

定向检查：Core 5/5、Companion 14/14；安装签名验真，正式更新包 `artifacts/releases/HollowKnightTAS-Studio-startup-frame-final.zip`，490 项，166067256 字节，SHA-256 `1fc7677153906523e161833a43b5b41445bcc20dec38f58d8fafb860487a0c95`。安装 Runtime `3aaa230901d2d6fb66de1275a34a42e3fe0e89af7eff91d9017bc07c89883391`、Core `8aa4a71370d74e72cb3ca463e6fdf1eac1a9f1f5fc18e1f3e7a680617b76a5a2`、Companion manifest `eaaafdc7a8f80a09aede40ee71386a923d7094356c3faa47c8ad7261bf11298e`。旧组件备份在 `artifacts/d06-startup/pre-handoff-backup`，旧包与旧示范保留；以下历史哈希不代表当前安装身份。启动帧下的 V 快捷键尚未做实机按键验证，按钮路径和快捷键解析定向测试已通过。下一项由新的用户需求决定，不重跑旧矩阵。

2026-09-23 假骑士旧序列复查：3042 帧 `fixtures/t16/false-knight-ea42-3042.hktas` 与 3043 帧 `artifacts/d04-live/false-knight-ca23-3043.hktas` 的归档均用当前 Core 验证通过；但当前运行环境 manifest 为 `f2c508c4eb98ac77cf2641a7ee383204bde1696a3cb5b5f8f13d561555c9496f`，两份 movie 的 manifest 分别为 `391ad9b41f15cae7c58166ba1a871a83de6bb71142783a76f08c062c33aaf30d` 和 `ca2351361eca60de168e5380e2ca934c89113352172ba163cc0258c48823a6e5`。`movie validate` 均报 HKTAS220；Studio 的两个对应 Replay Save 均为 `Incompatible`，详情 `manifest-mismatch`。因此当前安装版在战斗回放前即拒绝旧示范，本轮未做战斗通过声明，也未改写标识或恢复旧存档。测试游戏正常退出，第 4 槽哈希仍为 `1acd6214b8dacaf112a2fbae0e5e7aeb3d487c9365f5f955e4aef8e51459dade`。进一步拆包确认：两份归档内的原版及 Mod 存档字节均与当前第 4 槽完全相同；旧起点语义哈希 `2fd2dab925889414df31d39eae39dedbeb568bf37f2a1fdebfb50ccc7a2c4ab4`，当前启动得到 `e486b28f5831be87cb32e4c5cfa2b04c07cbc70b6adf8835292e70323c8e0c62`，而语义快照哈希实现自初版未改。旧归档只加载 TAS 和 ReferenceObserver，当前游戏加载 DebugMod、GodSeekerPlus、Osmi、QoL、Rogue、Satchel、SFCore 等。manifest 是保守的构建/环境门禁，不证明输入在新版必然失败；若要跨版复用，需先在隔离环境对齐实际起点与 Mod 集合、试跑并验证整段结果，再建立明确的兼容迁移，不能只重写哈希。

上述语义哈希差异已定位：直接比较归档 `BaselineBundle.SemanticSnapshotBytes` 和 2026-09-23 会话 `baseline.snapshot`，16 个字段中仅 `scene.name`（`GG_Workshop`→`Town`）、`hero.position.x`（`11.18`→`132.38`）、`hero.position.y`（`37.103325`→`11.603325`）不同。旧会话读档后的首个锚点就是 `GG_Workshop`，当前为 `Town`；旧第 4 槽 `.dat` 的 `playerData.respawnScene=GG_Workshop`，但同目录的 `user4.json` 为 `Town`。当前 `QoL.GlobalSettings.json` 中 `UnencryptedSaves=true`，已安装 QoL 的该模块在 `SavegameLoadHook` 从 `user4.json` 重载并覆盖 `PlayerData.instance`，而旧会话未加载 QoL。因此相同的归档 `.dat`/`.modded.json` 字节仍会在当前环境产生不同起点；QoL 的额外 `user4.json` 不在 Replay Save baseline bundle 中。此结论来自存档内容、QoL 模块反编译和实际快照，无需修改用户存档。要继续实测旧序列，需在隔离且等价的 Mod/存档环境中先对齐起点，再处理当前构建与旧 movie 的 manifest 兼容门禁；目前不能声称旧序列在当前安装版通关。

2026-09-23 最小 Mod 实机复查：临时禁用 TAS 以外的全部本地 Mod 后，仅 `HollowKnightTAS` 被加载，第 4 槽起点回到 `GG_Workshop`，语义哈希精确等于旧示范的 `2fd2dab925889414df31d39eae39dedbeb568bf37f2a1fdebfb50ccc7a2c4ab4`。当前最小环境 manifest 为 `2571c1fe5356bcfef97a8b73462d9dbff473ed7aa644e5fd5661f8f0edd738dc`；旧 movie 原件仍因 manifest 不同而不被当前 Runtime 原样接受。为实测相同输入，保留原件不动，仅在 `artifacts/false-knight-compat/false-knight-ea42-3042-current-manifest-trial.hktas` 副本替换该一行 manifest；其余文本逐字相同，当前 CLI 校验 3043 个输入样本通过。PID 26728、session `20260923T122648.3661578Z-d76c4b635a4c4b3eabf8a55163c47d29` 从诸神堂起点运行测试副本至末尾：`lastReplayMovieTick=3042`、`lastPlaybackStopReason=Completed`、`replayMismatchCount=0`，终点为 `GG_False_Knight`、调谐难度、剩 2 血，原生 `bossDeathObserved` 与 `bossesDeadObserved` 均为 true。证据见 `artifacts/false-knight-compat/trial-result.json`；这是当前 Runtime 的一次输入兼容试跑，不是旧归档原样冷恢复或跨版本严格终态哈希等价证明。测试游戏和自动打开的 Studio 均正常关闭；全部 Mod 目录已恢复到测试前的启用状态，`user4.dat`、`user4.modded.json`、`user4.json` 的 SHA-256 测前测后均相同。

2026-09-23 Steam→Studio 人工界面复查：Computer Use 在 Steam 库点击《空洞骑士》“开始游戏”，自动打开 Studio 且停在 `Startup frame: 0 / Startup gate paused`；在此帧用 `Movie → Open Movie` 成功查看 3043 帧假骑士序列，按钮单步 0→1 后仍暂停。此时 Runtime 为 `NOT READY / DISCONNECTED`，Movie 的 `Upload / Start Replay` 不可用；`Play` 只释放启动门闩，之后仍须在标题画面选择第 4 槽并等待 `READY / CONNECTED`。Studio 当前没有独立的 New Movie 命令，也不能从启动第 0 帧直接录制/回放含标题菜单的 Movie；若用户目标包含这种一气呵成的流程，D06 的现有交付范围尚未覆盖。此次 Steam 全屏会话 manifest `495444c9377138f666d6f91cf289df201707cec9204f4614c93b714d98cc2364` 与此前 800×450 窗口会话仅分辨率和窗口模式不同，原测试副本在 Upload 报 HKTAS220。另保留只改 manifest 头的全屏试跑副本 `artifacts/false-knight-compat/false-knight-ea42-3042-steam-fullscreen-trial.hktas`；在第 4 槽暂停后，Studio 的 Validate→Upload→Start Replay→逐帧第 0 帧→Play 全部由界面完成，`Completed`、3044 次观察、`mismatchCount=0`，Boss 死亡事件两项均为 true。该结果为界面路径下测试副本通过，不代表旧 movie 原件可原样导入。证据见 `artifacts/false-knight-compat/manual-ui-result.json`。测试后游戏与 Studio 已关闭，三个第 4 槽存档文件的 SHA-256 均与测试前相同；为供用户继续手动试跑，当前再次把 DebugMod、GodSeekerPlus、Osmi、QoL、Rogue、Satchel、SFCore、Vasi、WavLib 放在 `Mods/disabled`，仅 `HollowKnightTAS` 启用。恢复原 Mod 列表时参照 `artifacts/false-knight-compat/manual-ui-mod-state.json`。

## 已交付：libTAS 风格 Studio

入口：`mydocs/specs/tasks/D05_libTAS界面.md`；普通用户手册：`docs/USER-MANUAL.md`；迁移说明：`docs/LIBTAS-MIGRATION.md`。菜单、常驻播放/单步控制、输入表格、快捷槽/改键、模拟轴编辑和常用/高级界面分层已实现、安装并打包。后续处理用户新需求或具体缺陷，不重启旧测试矩阵。

安装 Companion manifest：`6b3011e8994ad44ad283decc144ffefdac06fb7e78205d76149cf4063e77eeac`。Runtime：`ec1ca3a3f95fc00ec7fc6c393911e086a5a5eeefb6e1ce8684954e595499c32a`；Core：`8d9ebdc958971aa16f3b5b9da769c3963d752b9e968f2eb94f727dca86b4f276`，均未因 UI 改动替换。不要默认构建/安装 Runtime。

现场证据：Shift+F1 保存 326；F1 恢复操作 `cold-restore-47949c6f9e18483aa09d7b268b42fd66` 显示 Launching → Completed。文本区 V 不推进、表格 V 326 → 327。表格第 0 帧 Attack 修改经 getMovie 确认，分支 `0a2fb0318b663aa5ee636c704e478459577f1107fb46e6633aaafda449699018` 共 327 帧；UI 分支回退操作 `cold-restore-64643a5c055740c7b4801322be75b67d` Completed 到 10。改键 N 10 → 11 后恢复默认 V，配置 `[7,65]`。

23 项定向检查通过（11 表格、5 快捷键、6 快捷槽、1 扩展 WPF 集成）。最后补上刷新后保持多帧选区的修复，单独重跑 WPF 集成通过；安装版使用十帧离线草稿，连续点击 Attack、Jump 并用 Ctrl+Z 撤销，确认两帧一起修改且选区保留。未保存该测试草稿。

WPF 检查覆盖全部菜单导航/收起、控制命令绑定、三个常用页和操作手册的最小内容区、100%/125%/150% 离屏渲染，以及轴弹窗的非零提交、越界拒绝、固定选区和撤销。窗口标题栏、菜单、按钮、下拉框、表头、选中行、复选框和滚动条已统一为深色主题；安装版 Help → Quick Start 打开手册实测通过。未测试跨显示器 DPI 切换；桌面工具对模态框输入的聚焦限制不是通过证据，也不是已确认的产品缺陷。

最终更新包：`artifacts/releases/HollowKnightTAS-Studio-libTAS.zip`，490 项，165870447 字节，SHA-256 `0fd1e92fa308d9fc44f8191c77c3349e4cab80d1087035d547290b62eb8901bc`。包含签名 Companion、冻结 Runtime/Core、操作手册和迁移说明；未重复打包视频。源安装验签、包内逐项哈希检查通过，安装目录 `HollowKnightTAS.zip` 和 `SHA256.txt` 已同步。新包替换本轮旧候选包，旧 MP4 最终包保留。

游戏已正常退出；最后运行位置是编辑测试分支第 11 帧，不是正式战斗示范。F1 的 326 帧存档未替换，旧草稿备份 `artifacts/d04-live/studio-before-d05.hktas` 保留。打包复查用 `scripts/Package-StudioUpdate.ps1 -VerifyOnly`，不重建游戏程序集。

文档整理等小任务使用 Luna。下面是已完成 MP4 的交付快照，不是新待办。

## 已交付：序列导出 MP4

实现入口：`mydocs/specs/tasks/D04_MP4导出.md`。新版已安装，完整假骑士视频、180 帧暂停续录和 Studio 按钮 300 帧自动导出均已实测。使用说明见 `docs/MP4-DELIVERY.md`，验收范围见 `docs/MP4-VERIFICATION.md`。

本轮功能与范围复核完成，无继续跑矩阵或重新构建的待办。完整 MP4 解码无错误；开头、中段、末段的动作画面与音频瞬态已对照，未见明显累计错位。检查基于逐帧画面和音频 RMS，不宣称逐音效精确延迟或主观听感通过。编码器异常退出新增定向测试 1/1 通过，未改生产代码。首次短导出曾出现开始回执超时，但查询确认 Completed；后续正常返回。超时只查询同一操作，不重复提交。

最终配套包：`artifacts/releases/HollowKnightTAS-MP4-final.zip`，209317461 字节，SHA-256 `ab8e036c5afbdbfffb574b3bbc5e26d5bb3a3939be2c52590238aa30e47914c7`。包内 9 个文件及哈希已流式校验，含最新使用说明和验收范围，不含 FFmpeg。上一阶段 ca23 包的二进制、视频不变，说明已由 final 包更新；旧 3042 包保留，不与新版混用。

Studio 实测：`artifacts/d04-live/studio-300.mp4`，操作 `video-02b4378cd65641a8821e43ec84042517`，Completed，300 帧 / 50 fps，H.264 800×450 + AAC 48 kHz 双声道，音画各 6 秒。SHA-256 `c0c9e87a7524f181e3fbd47d81905637fd75479c12ca4c500b7a142cab9b7002`。界面与外部状态均确认完成。

完整视频：`artifacts/d04-live/false-knight-ca23.mp4`，43285613 字节，SHA-256 `cd00fb873222eab989b7f461adf0bad7163b900c63e868e9d10323853f8b695c`；H.264 800×450 / 50 fps / 3282 帧，AAC 48 kHz 双声道，音画各 65.64 秒。3043 个输入帧外含场景切换画面。开头、中段、结尾抽帧已检查；两个原生 Boss 死亡回调标志均 true，调谐 / GG_False_Knight / 剩 2 血。primaryBoss 的存活投影仍为 false-dead/260，不用这个投影否定原生终态事件。

完整战斗所在进程 PID 21892 已正常退出。最终存档 `save-20260916T0526048550472Z-00000001` Ready；电影 `artifacts/d04-live/false-knight-ca23-3043.hktas`，SHA-256 `5de22816d2d96f70af9de46a7a98c5003fb10243df06e67cd6e6074ef891c080`。导出操作 `video-fe66b646c44f4fb08432729420602f8a` Completed。安装 Runtime SHA-256 `ec1ca3a3f95fc00ec7fc6c393911e086a5a5eeefb6e1ce8684954e595499c32a`，Core `8d9ebdc958971aa16f3b5b9da769c3963d752b9e968f2eb94f727dca86b4f276`。

`getStatus` 字段数量修复已安装并实机查询成功，完整诊断放在 `runtimeStatusJson`。最近游戏 PID 30444、session `20260916T053436.9231640Z-2368bcb4f9324d8eb084c78a6c2b5169`，Studio 短导出结束时 tick 326 / Paused / Idle；再次操作前查询实时状态。

当前已安装包保持上述 Runtime/Core 身份。普通构建会把 Git HEAD 写入程序集和 SourceLink，可能改变指纹；不要仅为文档或打包再次默认构建。准确重建需同时固定 `SourceRevisionId=5e61ae587bf6da3c6c46e0152fa5fdd6aa5beac9`、`EnableSourceLink=false` 与 `SourceLink=artifacts/d04-live/frozen-sourcelink.json` 的绝对路径，再核对二进制哈希。该 JSON 无末尾换行。不要改 movie 哈希冒充兼容。

以下短探针和 D01–D03 段落是历史快照，不代表当前进程或安装版本：

当前实测：PID 2064，runId `interactive-1a84db9cb67147e2b0bb593affb8b7f0`，tick 448 / Paused / Idle，场景 GG_Workshop；需实时复查。执行 manifest `ca2351361eca60de168e5380e2ca934c89113352172ba163cc0258c48823a6e5`。`artifacts/d04-live/sequence-pause-180.mp4`：180 帧 / 50 fps / 音视频各 3.6 秒，暂停时两次查询均为 6 帧；空中攻击抽帧已检查，无 TAS 叠加层。旧段落的 PID 17000 已退出。

startVideoExport / finishVideoExport / cancelVideoExport（control.playback）已可经通用接口调用，状态在 getState 的 videoExport.* 字段。开始要求 Paused，结束和取消校验 operationId。实际时钟为 50 fps。`artifacts/d04-live/probe-dsp-60.mp4` 为 800×450、H.264 + AAC 双声道，音画各 1.2 秒、189630 字节；暂停未增加帧数，取消未留下文件。当前游戏 PID 17000、tick 259 暂停；进程是否仍在需读实时状态。构建后 Core/Runtime 身份已改变，旧假骑士示范不得改哈希冒充兼容。

以下为此前 TAS 交付快照；其中进程状态和安装身份不是当前实时检测结果。编译 Core 后的新二进制不可冒充旧身份，旧包和示范保持原样。

## 此前 TAS 交付

本次按 D01–D03 的产品要求交付。使用说明和逐项范围见 `docs/USER-GUIDE.md`、`docs/DELIVERY-STATUS.md`。历史实现记录不作为新待办；无需恢复旧矩阵或再次回放战斗。

## 交付入口

- 最终包：`artifacts/releases/HollowKnightTAS-3042-delivery.zip`，166018474 字节。
- SHA-256：`6bf8667474844c40b0dc57edeb468fcc37f07ba052d1e6adbcba8c7b06e0b01a`。
- 12 个内容文件及 SHA256SUMS 已逐项验证；包内相对 Markdown 文件链接无缺失。
- 内容包含签名 Mod、Studio、CLI/MCP/SDK、3042 帧示范、portable save、人工与 AI 文档、独立输入脚本及验收范围。
- 旧 `HollowKnightTAS-ea42-3042.zip`、2982 包和旧分支保留，不作为推荐安装包。

## 完成结果

- D01：原生输入路径的暂停、单帧、多帧输入和人工/AI 共用控制；动作、手动停止、断连及跨场景边界已有范围实测。
- D02：完整帧边界手动存档、定时存档、跨启动重建、继续输入、过去输入编辑及分支撤销/重做已有证据。恢复采用起点加输入重放，需要等待。
- D03：诸神堂椅子出发，第 3042 帧确认调谐假骑士死亡、剩余 2 血。冻结后两次独立完整回放通过，后续未修改游戏执行程序集或示范身份。
- MCP 取消携带 operationId，状态接口公开对应操作及阶段。立即取消实机经过 Prepared → SourceQuiesced → SourceExited → Cancelled，未启动目标。
- 取消后无游戏进程时，保留只读状态入口；新连接读取 Cancelled、写命令返回 RuntimeDisconnected 的定向管道检查通过。此最后的小改没有再做实机全流程。
- Mod 会自动启动配套工具。正常 TAS 不使用调试状态写入；调试 mutation 会使该进程失去正式验证资格。

## 当前安装身份

- Runtime：`ea42a9c4caf5ddb3861697a30a71a29e8e90b48adf303d03cbae603a55708541`。
- Core：`3efdfdb19358db2d45af9d1873768d1b1fddd78eb4c89fea2772642ea139466f`。
- ClockPayload：`5fba1091b9b8b52397c9fa9522659a87bb0062d63425987a5450ba24dd6e9a13`，Clock v40 / RNG policy v19。
- Companion manifest：`bb2856fe83da5f51c8767262785d9d556e2853a2603e12ab0b5e7f9fa2f3d001`。
- 安装 zip：`6b0c8d4cb14242bdd088fce906b3e2ef0778cfd88e7a437c8cc74a97cf767ebd`。
- 最近实测执行环境：`391ad9b41f15cae7c58166ba1a871a83de6bb71142783a76f08c062c33aaf30d`。
- 当前 movie：`fixtures/t16/false-knight-ea42-3042.hktas`；存档：`save-20260916T0057427801258Z-00000001`。
- 游戏与 Studio 现均关闭。最终包已安装；无需再次构建。

## 证据入口

`artifacts/d03-live-current/`：

- `victory-ea42-3042-20260916.json`：当前示范、两次完整回放及归档导入。
- `mcp-cold-control-20260916.json`：MCP 取消、646 帧短恢复、首错、修复与最终安装身份。
- `transition-no-warmup-20260916.json`、`manual-stop-20260916.json`、`active-batch-disconnect-20260916.json`：边界行为。
- `standalone-authoring-20260916.json`：独立非视觉输入脚本。
- `human-edit-undo-installed-20260915.json`、`continuous-edit-installed-20260915.json`、`lifecycle-branch-installed-20260915.json`：人工编辑、持续编辑与读槽记录。
- `root-plan-installed-20260915.json`、`branch-library-installed-20260915.json`：根重建与跨会话分支。
- `durable-slot-recovery-20260915.json`：隔离槽文件保护。

`artifacts/interactive-origin-smoke/`：

- `d01-product-step-20260914.md`、`d01-window-and-air-restore-20260914.md`：原生动作、人工单步、暂停窗口与空中恢复。
- `autosave-current-20260914.md`、`d02-first-edit-and-auto-fixed-20260914.md`：定时存档与编辑后恢复。

## 维护边界

早期构建证据仅证明各自范围，不冒充最终包全量重测。目标指纹未穷尽所有投射物、未激活对象及 action 私有计时器；其他机器、所有场景和真实多槽操作未逐一验证。旧 ReferenceInputHandshake 的 timeScale 断言失败保留，不宣称全测试通过。

第 4 槽已授权；原备份入口为 `artifacts/interactive-origin-smoke/approved-slot-cold-restore-20260914.md`。保护用户存档、旧分支、仍引用证据和 pending。不得扫描整盘或未经授权清理产物。仅编译传 `-p:SkipHKTASInstall=true`；默认 Build 会安装。

后续仅处理新需求或具体问题；小改定向检查，不自动增加 10-run。日志每文件 64 MiB、sessions 共 1 GiB，输入 journal 不按诊断预算截断。
