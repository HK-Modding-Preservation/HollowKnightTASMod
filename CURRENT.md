# 当前任务：序列导出 MP4

实现入口：`mydocs/specs/tasks/D04_MP4导出.md`。格式、时间轴、流式编码器、游戏采集适配层及 CLI/SDK 通用命令已实现；Studio/MCP 专用入口和完整序列协调尚未完成。实机音频探针失败，当前不能可靠导出游戏序列。

下一步：解决安装版 Unity AudioRenderer 返回 0 样本的问题。API Start/Render 返回成功，但按帧直接 Render 的短探针也只有零样本；不能当成有效音频。查明原生实现/初始化要求，必要时实现其他按模拟帧驱动的音频采集。视频管道在实机第 24 帧停滞，已加 zerolatency 并通过 720p 合成测试，实机修复仍待确认。完整证据摘要在 D04，只做定向探针，不重跑旧战斗矩阵。

本次新增 startVideoExport / finishVideoExport / cancelVideoExport（control.playback），状态在 getState 的 videoExport.* 字段。CLI 已在游戏中成功执行；导出开始要求 Paused，结束和取消校验 operationId。当前时钟实测为 50 fps，不能写死 60 fps。最终源码恢复音频样本不匹配即失败的检查；游戏与 Studio 已正常关闭，最终 Runtime 构建安装后未再启动。

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
