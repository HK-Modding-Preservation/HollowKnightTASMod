2026-09-26 D10 候选测试发生系统内存耗尽，已停止实机测试。Windows 事件 2004 确认 codex-computer-use.exe 使用约 29.7 GiB，是最大占用者；Studio 1.66 GiB、游戏 1.17 GiB。新增 InputFrameGrid 辅助功能可见行边界与主动进度合并，离线 25/25 通过、31 原始 user* 文件不变。工具内部泄漏点及分辨率变化原因尚未确认；未改显示设置、未再启动游戏。D10 实机验收未完成，不继承历史 PASS。详见 artifacts/studio-simplification/REPORT.md 和 mydocs/specs/tasks/D10_Studio操作简化.md。

2026-09-26 D09 回档显示冻结已实现并安装：退出原进程前捕获游戏表面，以独立静止画面覆盖重启和重放；回档启动专用窗口钩子阻止 Unity 再次显示启动白屏，结束后解除。Studio 表格、选区、当前标记、列头与滚动位置冻结，自动跟随设置不变，完成后才更新目标。定向 Companion 6/6、窗口 14/14、原生帧门闩 19/19；窗口模式实测 27025→27008 精确暂停，后台 6754 帧时表格仍为原位置，错误/偏差 0；关闭跟随的候选实测 500→310 后首行保持 300。WGC 被遮挡窗口捕获、失败解除冻结已检查；31 原始 user* 文件集合及哈希不变，测试进程关闭。未验独占全屏及跨 DPI，未重跑战斗矩阵。Spec：mydocs/specs/tasks/D09_回档显示冻结.md；证据和各候选边界：artifacts/restore-presentation/REPORT.md。仅本地提交，不推送。

2026-09-26 本地提交收尾：本次归档暂停/跟随、末尾接续、Timeline 快捷槽、游戏键位列头与列宽、退出联动、未来播放/过去恢复及输入边沿衔接修复。下列对应记录的“源码未提交”为当时状态，现随本次提交归档；定向测试、构建验签及实机验证范围见各条记录，未重跑战斗矩阵。仅本地提交，未推送。

2026-09-26 回档列宽与帧导航语义已修复并安装：键名测量像素列宽保持紧凑；右键未来帧原进程播放到目标，过去帧冷重启恢复，当前帧禁用两项。6/6定向检查；实机1→10不重启、10→5重启精确暂停，恢复和滚动后列宽稳定。组合测试发现并修复编辑帧接回录制帧的多次输入更新边沿误报，最终PID4900恢复5→改未来8→播放10精确暂停，故障/偏差0。31原始存档文件不变，测试进程关闭。证据 artifacts/frame-navigation-width/REPORT.md；源码未提交。

2026-09-26 Studio 关闭联动退出游戏、未来帧编辑末尾不再重启，已修复并安装。退出结束确切受控进程；实时编辑回放末尾改普通暂停，Play/Step 补帧并同进程接续。定向12/12；安装版 PID8440 编辑未来帧→500末尾→Step501→延长→Play/Pause12926，故障/偏差0，关闭Studio后游戏同时退出，31个原始文件哈希不变。用户残留PID5720已关闭。证据 artifacts/studio-exit-live-edit/REPORT.md；未重跑战斗矩阵，源码未提交。

2026-09-26 序列动作列改为游戏绑定键名，已构建、签名安装：Runtime 从 HeroActions 读取原始绑定（播放租约期间读取备份），经状态同步到 Studio；键盘优先、方向键箭头、Enter ↵、动作名保留 tooltip，列宽自适应且最小32 DIP。未就绪 ?、未绑定 —、切换会话清空。定向3/3与150%渲染布局通过；本轮未进游戏实测改键同步，证据 artifacts/input-key-headers/REPORT.md。源码未提交。

2026-09-26 快捷槽统一至 Timeline，已实现并安装：移除 Savestates 标签及菜单入口；选择世界线和节点后可绑定 F1–F10，引用同时保存世界线以避免共同祖先恢复到其他分支。Fn 恢复、Shift+Fn 保存当前帧保留，旧双字段引用兼容。12/12 定向测试通过，安装版界面检查通过，已有时间线及快捷槽文件哈希不变；本轮未新增游戏内恢复实测。证据 artifacts/timeline-shortcut-binding/REPORT.md；源码未提交。

2026-09-26 Completed 末尾无法接续已修复并安装：固定 Replay 到末尾后允许 Play／Step／应用重放；Play/Step 校验已执行前缀并经现有受保护冷启动重放恢复末尾后接续，无后续帧自动补 500。Replay 追加空帧会标记待同步。定向 7 项及补充断言 2/2 通过；实机 Completed 500→补至1000→Step 501、Completed 1000→Play→Completed 1500 成功，故障与输入偏差为0，原始 user* 31 文件未变。恢复需要重放等待，非瞬时续播。证据 artifacts/completed-replay-resume/REPORT.md；源码未提交。

2026-09-26 暂停／跟随故障已修复并安装：输入保护误将加载及 RandomSeedHandshake 当成帧外，阻断种子同步导致 native 160 / fault 41 / Movie 0。现分离原生帧活跃与 Movie 采样就绪；失败按钮明确需重启并禁用推进；滚轮不再隐式关闭跟随。定向 8/8，安装版 Play→Pause 20216→继续→Pause 35192→Step 35193 实测通过，表格跟随正常，故障及输入偏差为 0，原始 user* 31 文件无变化。详见 artifacts/pause-follow-fix/REPORT.md。未重跑战斗矩阵，源码未提交。

2026-09-26 D08 空白会话展示修复：旧帧存档和旧 F1 快捷槽本来保存在本地，上版默认选中历史树并混显旧槽。现在空 Movie 打开时间线只显示未存档 Frame 0；历史树可主动切换，Savestates 显示空时间线槽。定向 18/18、安装版 UI 验证通过，历史时间线与快捷槽文件哈希未变。详见 artifacts/studio-worldlines/blank-start/REPORT.md。

2026-09-25 D08 时间线世界线已实现并安装：新增可滚动树图，初始 Frame 0、每次存档新增节点；按已执行输入前缀自动分叉，叶子选择世界线、祖先选择恢复位置；支持删除子树及本地原子持久化。右键存档／页面存档／Shift+Fn 共用节点路径，旧索引存档一次性导入。定向测试 19/19；实机验证 0→1→2 与 0→1→3、两条世界线恢复共同祖先后完整 Movie 分别一致、重开持久化和 F1 快捷恢复。恢复仍为冷启动重放。Spec：mydocs/specs/tasks/D08_时间线世界线.md；证据：artifacts/studio-worldlines/REPORT.md。

2026-09-25 右键播放到帧后控件禁用已修复并安装：定位完成时解除 gridApplying 未触发 CanExecuteChanged，现统一刷新门闩、文字和按钮可用性，含异常收尾及应用重放。修复前实机复现第 10 帧已暂停却按钮禁用；修复后同路径第 10 帧 Play/Step 可用，逐帧到 11，再 Play 完成 500。定向 10/10 通过，签名安装验证通过。详见 artifacts/studio-seek-controls/REPORT.md。

2026-09-25 Studio 三项反馈已修复：未来帧修改在播放／逐帧前校验已执行前缀并同步当前 Runtime；暂停确认即时刷新按钮；拖动实时预览并在释放时一次提交。22/22 定向测试通过，安装版同进程未来第 3 帧执行、两轮 Play/Pause/继续、过去帧拒绝直接推进实测通过。详见 `artifacts/studio-input-fixes/REPORT.md`；中途拖动反馈由模型测试与事件路径验证，非中途截图证据。原始 user* 31 文件差异 0，测试进程关闭，源码未提交。

# 当前进度：Studio 七项交互优化已实现并安装

2026-09-25 回放速度初测：10892 帧假骑士序列，安装版约 20.4–21.7 秒回放（约 10–10.7 倍速）；解除限速并将相机 cullingMask 设 0，约 17.1–17.4 秒（12.5–12.7 倍速），含启动约 24 秒。有效试验战斗至终点 2828 行位置/血量/Boss/RNG/deltaTime 对比差异 0；全局或战斗场景 OnDemandRendering 间隔 1000 会改变结果，淘汰。使用外部计时器及无 UI 的现有控制路径，尚非正式快进功能、非绝对性能上限。实验源码还原、安装版未替换且验签通过，原始 user* 31 文件差异 0，测试进程关闭。详见 `artifacts/replay-speed/REPORT.md`。先前仅凭 targetFrameRate 推断实际限速的判断已被实测修正。

2026-09-25 D07：无 Movie 播放／逐帧自动新建；整段虚拟序列表格（最多 1000 万帧、有界 2048 行缓存）、末尾补空帧及录制增长；右键存读档／精确定位；锁定起始列的拖动输入；19 像素行高；默认及选区 FPS；深色弹出菜单均已实现。详见 `mydocs/specs/tasks/D07_Studio交互体验.md` 与操作手册。定向 Companion 22/22、Core 18/18、原生 19/19 通过，Release 构建和安装验签通过。

实机覆盖无 Movie 启动、跨 500 行、拖动 Left 3–13、区间 100 FPS、默认 120 FPS、存档及重开 Studio 后恢复 13 帧（输入偏差 0）、跨帧率区间逐帧至 15。长录制 628 帧暂停后正常同步，实际 120 FPS 时钟步长 0.0083333 秒；恢复 13 时 100 FPS 为 0.01 秒。此次未重跑完整战斗矩阵，不继承历史构建的战斗 PASS。定位和帧读档采用冷启动重放，非瞬时内存快照。证据见 `artifacts/studio-ux/`。原始 user* 31 文件集合及哈希差异 0；默认 FPS 恢复 50，测试游戏和 Studio 已关闭。当前源码未提交。

---
# 当前进度：全流程 Movie、加载省帧与新版假骑士示范已通过冷启动回放

2026-09-25 原生第 0 帧窗口卡死修复：已清理用户残留 PID 11896。匹配 Unity PDB 将卡死栈定位到标题栏定时器重入 PerformMainLoop 后等待 SwapChain 提交；旧 PlayerLoop 内层 guard 不足。现在同时保护 TitleBarTimerUpdateCallback→PerformMainLoop 的尾跳转（PDB 身份与原五字节指令校验），暂停继续处理窗口消息而不嵌套执行主循环。原生消息泵回归 19/19；安装版经 Steam→Modding 手动启动，第 0 帧停留数分钟、拖动/系统移动菜单后仍响应，再打开旧 Movie 完整播放 10892/10892 Completed（原生11422）。原始 user* 31 文件集合/哈希无变化；测试进程均关闭。当前 ClockBridge `c4fb720637ad3ab5c0ab80d48751317f97a89a8b256cc70e7234cc6ac9896767`，manifest `92641a99e3047aee3e708c17ae36974f95b3f3bc81152ca658bb4a6b48fe2fd1`；Runtime/Core/ClockPayload 未变化。证据、旧组件备份及签名更新包见 `artifacts/startup-window-reentry/`。前一轮只修复 Studio 命令确认的验证不再被视为原生卡死已解决的证据。

2026-09-25 第 0 帧 Replay 播放边界错误修复：Play 改为等待原生 ack，未确认期间不接受残留 Ready/Paused 作为可操作边界；即时刷新 UI、禁用重复播放/逐帧，故障与超时显示具体原因。人工和自动化共用异步确认路径。启动定向测试 8/8；安装版实机标题手动启动→打开旧假骑士 Movie→快速双击播放/暂停→逐帧 Movie 1→继续至 10892/10892 Completed（原生12768）通过。原始 user* 31 文件哈希与集合不变，测试游戏/Studio 已关闭。原截图未稳定复现，已确认并修复命令确认时序漏洞，不能从截图反推唯一根因；范围及证据见 `artifacts/startup-replay-boundary/verification.md`。仅替换 Companion，Runtime/Core 仍为下文手动启动最终哈希；当前 manifest `e027f9ab2b992cbc14f62439b3a376e6f4c8fa6188f905122cebe06ab27c28c4`。

2026-09-25 启动入口改为手动（覆盖下文自动打开 Studio 的历史说明）：普通启动不创建 TAS Runtime 或 IPC，只保留 Modding 设置菜单；标题 `选项 → Modding → HollowKnightTAS → 打开 Studio（重启游戏）` 才建立最小认证移交通道、打开／复用 Studio、重启至原生第 0 帧。局内点击只提示先保存退出到标题；每次点击单独创建请求，先前正常游玩不使后续标题请求失效。旧 AutoStartCompanion 设置不会激活普通进程，Studio 已开也不自动接管普通游戏。重复点击不重入；离开标题取消；组件错误／超时保留原进程并允许重试。实现与范围见 `mydocs/specs/tasks/D06_启动与首帧暂停.md` 文首修订；手册已更新。

本轮 Core 7/7、Companion 启动门闩 6/6 定向检查通过，Release Runtime 构建 0 警告，Companion 签名安装验真。已实机覆盖普通启动不弹 Studio、已有 Studio 不接管普通游戏、两种 Studio 状态下点击重启至第 0 帧、组件缺失保留原游戏及恢复后重试；短录制单步 Movie 0→1（原生1437）→2（原生1438）并保持、Play 与退出通过。最终 Runtime 增补启动器终止错误立即回报，SHA-256 `1653f8d511b24b7fd76f4f648302731a508afc798515d77e1b91762074f753d2`；Core `deaa7379f57a97532769fee70663041fe64aa62b943335e69da1ed26e77775e0`；Companion manifest `667c56ce2f978015b7f6f336eb3ad7217a92f050139a5912d70915dfe806fd4b`。证据与旧包备份在 `artifacts/manual-studio-startup/`。本轮未进入用户原始存档执行局内／保存退出测试，未重跑假骑士 Movie；旧冻结构建 VERIFIED 不直接继承为此 Runtime 的回放 PASS。

手动启动收尾：最终 Runtime 的组件缺失即时失败、原游戏保留、恢复组件后重试至第 0 帧均已实测。最后仅同步 Studio 内置帮助文字并重签安装，未再次运行游戏；Runtime/Core 哈希未变。原始顶层 `user*` 31 个文件测前测后文件集与 SHA-256 差异为 0；游戏与 Studio 均已关闭。最终安装 zip SHA-256 `5daaf81c741eb9ab3b62451f98f9b912372df7da094725c47d8e7c9bc5dd369f`。

2026-09-25 用户撤回 Studio 游戏窗口／全屏与分辨率设置需求：本轮相关源码、测试和手册改动已撤回，新增的本地显示偏好已删除；从提交 `115d6d3` 重新构建并签名安装，游戏和 Studio 均已关闭。当时 Companion manifest SHA-256 `63f03d076fc723bf2750434ad928b91c7097d05899243e12e78408acf32bbf2d`，安装 Companion DLL `087f41016263ce53056f4b4646d4946b2ce4deb027b85182f1c59c3fa5c7cd70`、Runtime `a4d163d67bf7e182b9a0602eea0ad92b0d622d075ce6a80605a1b27d8a13794d`、Core `ac217a36b8ca7bbb20115c636eb064283e9710929cc1623d2922e35097c59463`。构建 0 警告、安装包验签通过；原始四槽 20 个文件的文件集和 SHA-256 与本轮测试前一致。本次撤回后未重新启动游戏实测。

2026-09-25 Studio 退出入口补齐：主界面新增 `Quit Game 退出游戏`，全流程运行中先暂停到原生帧边界再退出；第 0 帧 Runtime 尚未连接或原生门闩报错时，只关闭 Studio 所持有的受保护游戏进程；Movie 已完成后按钮仍可用。`Stop` 仍只停止序列，`File → Exit` 只关闭 Studio。定向后台测试 4/4 通过（含第 0 帧回调、Completed 命令可用性、WPF 布局与命令绑定），Release 构建 0 警告，签名安装验真。当时 Companion manifest SHA-256 `04872a9b5c419d3080681bf9da60dd8836dcfb4ffac6d367bf003756fb69a647`，安装 Companion DLL `a0c404a6e37effbaa1d15368c26f9f1bd33712b219aae493097e2d8d09c6b6ab`、Runtime `0e564961634e9b5656abc6e5d8cb46e0f28c4bd37630947bbf63383603a88b25`、Core `55668815cc8ed5c28ec9affb23b3e15af4ada85f37b0ddaf36793dbb56b6f946`。用户授权实机测试后，从 Steam 三次普通启动：第 0 帧、全流程播放中（已推进至数千 Movie 帧）、完整播放完成后三种状态下，Studio 的 `Quit Game` 均成功关闭受控游戏；随后显示 `DISCONNECTED` 且按钮禁用。完整回放使用 `fixtures/full-run/false-knight-startup-v2.hktas`，从原生第 0 帧运行至 Movie 10892/10892、原生帧 12232，Studio 显示 `Completed`。本次未额外读取 Boss 事件，战斗语义仍以此前相应构建的正式验收为准。测试前后原始四槽 20 个文件的文件集与 SHA-256 一致；游戏和 Studio 均已关闭。

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
