# 当前任务：libTAS 风格 Studio

入口：`mydocs/specs/tasks/D05_libTAS界面.md`。菜单、输入表格、快捷槽/改键、模拟轴编辑和常用/高级界面分层已实现。保留游戏语义和现有分支服务，只读取本 Spec 与相关 Companion 代码。

最新 Companion 已签名安装，manifest 为 `63eda8467bc01aab0eec617f9eb972efdfe10cdd5ceb2f5dc8cd9002c3cb989b`，Runtime/Core 保持下方冻结哈希。恢复进度、IME 映射和表格点击焦点修正已安装。F1 操作 `cold-restore-47949c6f9e18483aa09d7b268b42fd66` 在 UI 显示 Launching → Completed，恢复到 326。文本框中 V 保留给中文输入法；点击表格后 V 实际推进到 327 并暂停。最新进程 PID 2912，操作前仍需实时查询。旧草稿备份 `artifacts/d04-live/studio-before-d05.hktas`。

表格第 0 帧 Attack 切换已提交，Runtime getMovie 返回分支 `0a2fb0318b663aa5ee636c704e478459577f1107fb46e6633aaafda449699018`，首行为 `frames 1 hold=attack`，总长 327。随后 UI“应用并重放到 Frame”从 327 回到 10，操作 `cold-restore-64643a5c055740c7b4801322be75b67d` Completed，游戏 Paused。逐帧键临时改为 N 并保存，实测 10 → 11 后恢复默认 V，配置为 `[7,65]`。最新游戏 PID 12380 / tick 11 / Paused，操作前查询实时状态。此为临时编辑测试，不是正式战斗示范；F1 的 326 帧存档未替换。

下一步解决轴弹窗非零输入的可验证路径、真实缩放检查和最终交付检查。中文说明见 `docs/LIBTAS-MIGRATION.md`。轴弹窗坐标点击有效，type_text 会重新激活主窗口；set_value 报 CacheRequest 错误，文本框右键粘贴尝试也未生效，勿反复走相同工具路径或据此宣布 Mod 有输入 bug。本轮无生产代码改动。

Studio 更新包：`artifacts/releases/HollowKnightTAS-Studio-d04f271.zip`，489 项，165861321 字节，SHA-256 `8d7ae9068d5a3274bb1e8e0c3271734e8bfcff1cf171dd0e97eed896e39b7231`。包含当前签名 Companion、冻结 Runtime/Core 和迁移说明，不包含旧视频的重复副本；源安装验签及包内逐项哈希检查通过。用 `scripts/Package-StudioUpdate.ps1` 打包或 `-VerifyOnly` 复查，禁止默认 Runtime 构建。主目录旧 `HollowKnightTAS.zip` 尚未替换，最终验收仍未完成。

本轮 5 项快捷键及 1 项 WPF 布局测试通过；880×560 内容区预留了最小窗口边框空间，三项常用页与工具栏可用。实际拖拽未改变尺寸，不称为真实缩放通过。computer-use 可能返回旧 UIA 树，应重新 get_window + activate + 截图确认；set_value 报 CacheRequest 错误时不要反复调用。向模态框发送文字的工具焦点问题仍待解决，不冒充轴输入已验证。

安装版 Runtime/Core 继续使用下方 MP4 冻结身份；不要默认重建/覆盖游戏 DLL。文档整理等小任务使用 Luna。下面是已完成 MP4 的交付快照，不是新待办。

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
