# 当前任务：序列导出 MP4

实现入口：`mydocs/specs/tasks/D04_MP4导出.md`。新版已安装，180 帧序列自动结束和暂停续录已通过实机 CLI 检查。Studio/SDK/MCP 入口已实现，Studio 按钮尚未人工路径实测；完整假骑士导出仍待完成。

下一步：安装仅修改 Companion 的汇总状态修复，核对 Runtime/Core 哈希不变，完成 Studio 操作与交付包/文档收尾。完整假骑士视频已完成，不重跑战斗矩阵。还需补充分段音画同步检查，不以总时长相等替代动作同步。首次短导出曾出现开始回执超时，但查询确认 Completed；第二次和完整导出正常返回。超时只查询同一操作，不重复提交。

完整视频：`artifacts/d04-live/false-knight-ca23.mp4`，43285613 字节，SHA-256 `cd00fb873222eab989b7f461adf0bad7163b900c63e868e9d10323853f8b695c`；H.264 800×450 / 50 fps / 3282 帧，AAC 48 kHz 双声道，音画各 65.64 秒。3043 个输入帧外含场景切换画面。开头、中段、结尾抽帧已检查；两个原生 Boss 死亡回调标志均 true，调谐 / GG_False_Knight / 剩 2 血。primaryBoss 的存活投影仍为 false-dead/260，不用这个投影否定原生终态事件。

当前进程 PID 21892，session `20260916T052243.3334584Z-b782192a769e4255b696fa3df32cf3e1`，tick 3043 / Paused / Idle。最终存档 `save-20260916T0526048550472Z-00000001` Ready；电影 `artifacts/d04-live/false-knight-ca23-3043.hktas`，SHA-256 `5de22816d2d96f70af9de46a7a98c5003fb10243df06e67cd6e6074ef891c080`。导出操作 `video-fe66b646c44f4fb08432729420602f8a` Completed。安装 Runtime SHA-256 `ec1ca3a3f95fc00ec7fc6c393911e086a5a5eeefb6e1ce8684954e595499c32a`，Core `8d9ebdc958971aa16f3b5b9da769c3963d752b9e968f2eb94f727dca86b4f276`。

`getStatus` 汇总 Runtime 诊断与冷恢复字段会超过 128 字段上限；修复已通过定向管道测试，尚未安装。新结构保留 `runtime.controlMode/movieTick/playbackMode/...` 及视频字段，完整诊断放在 `runtimeStatusJson`。实际恢复已通过持久事件确认 Completed，没有重复启动。

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
