# 当前进度：D06 启动首帧暂停已交付

入口：`mydocs/specs/tasks/D06_启动与首帧暂停.md`。D01–D03 的 TAS 基本控制、存档/回档及示范，D04 的 MP4，D05 的 Studio 初版改善均已交付。D06 已安装：直接从 Steam 启动游戏，Mod 自动打开 Studio，在进入存档前把原标题进程移交给受控进程，默认停在原生 `Startup frame: 0`。普通启动的 Studio 按钮单步 0→1 后保持，并可继续初始化；相同门闩路径另实测按钮 0→1→2 后保持。Studio 原先关闭和原先运行两种入口均实测，Runtime `getStartupProfile` 为 `Verified`；加载已批准的第 4 槽后，暂停、保持、单步和正常退出均通过。最后一次实测 tick 21153→21154，游戏进程退出、Studio 按设置保留且注销会话，存档 SHA-256 仍为 `1acd6214b8dacaf112a2fbae0e5e7aeb3d487c9365f5f955e4aef8e51459dade`。目前游戏和 Studio 均已关闭。

定向检查：Core 5/5、Companion 14/14；安装签名验真，正式更新包 `artifacts/releases/HollowKnightTAS-Studio-startup-frame-final.zip`，490 项，166067256 字节，SHA-256 `1fc7677153906523e161833a43b5b41445bcc20dec38f58d8fafb860487a0c95`。安装 Runtime `3aaa230901d2d6fb66de1275a34a42e3fe0e89af7eff91d9017bc07c89883391`、Core `8aa4a71370d74e72cb3ca463e6fdf1eac1a9f1f5fc18e1f3e7a680617b76a5a2`、Companion manifest `eaaafdc7a8f80a09aede40ee71386a923d7094356c3faa47c8ad7261bf11298e`。旧组件备份在 `artifacts/d06-startup/pre-handoff-backup`，旧包与旧示范保留；以下历史哈希不代表当前安装身份。启动帧下的 V 快捷键尚未做实机按键验证，按钮路径和快捷键解析定向测试已通过。下一项由新的用户需求决定，不重跑旧矩阵。

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
