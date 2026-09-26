# 全流程 v2 MP4 导出

全流程导出复用既有 Unity 离线音频和 FFmpeg H.264 / AAC 管线，不修改游戏时钟、输入或 RNG。当前支持每条输入都为 50 fps 的 v2 Movie；混合帧率会明确拒绝。Studio 的旧版 MP4 按钮没有改为 v2 流程；使用正式 SDK / MCP / CLI 接口调用。

1. 通过受保护启动器加载完整 v2 Movie，并停在输入系统已经就绪的暂停边界（例如 Movie 第 1 帧）。此时 Runtime 已连接。
2. 持有 `control.playback` 租约，调用 `startVideoExport`：`ffmpegPath` 为现有 FFmpeg 的绝对路径，`outputPath` 为不存在的 `.mp4` 绝对路径，`maximumFrames` 大于剩余输入帧数并留出加载余量，`replayLoadedMovie=true`，期望模式为 `Paused`。
3. 接口自动开始播放当前暂停点之后的剩余 Movie，到 Movie 末尾自动结束采集和封装。它不会回档到 Movie 起点；开头已经执行过的帧不进入视频。
4. 通过 `getStatus` / `fullRunStatus` 查询 `videoExport.state`，等到 `Completed` 再使用文件；开始回执的 `data.detail` 是操作 ID。`videoExport.frames`、`fps`、`outputPath`、`startNativeFrame`、`startMovieFrame` 标识输出范围。仅回放完成并不代表后台编码已经完成。

采集发生在原始 Unity PlayerLoop 返回后的原生帧边界，包括切场景实际执行的加载帧；暂停期间没有 PlayerLoop，因此等待不增加音视频时长。`fullRunPause` / `fullRunPlay` 可暂停、继续同一导出，`cancelVideoExport(operationId)` 取消输出并请求暂停。v2 序列导出自动结束，不提供手动 `finishVideoExport`。

导出期间拒绝编辑 Movie 或终止 Movie；请先取消导出。不要改变分辨率或音频格式。扬声器在 Unity 离线音频采集期间静音，结束后恢复。沿用有界音画传输、已有文件保护和临时输出发布规则；编码失败会暂停，失败、取消均不发布成片。

固定 50 fps 的编码器及租约权限有离线定向测试。2026-09-26 安装版已用嫉妒马尔穆 3700 帧击杀序列实测：一次普通冷回放与一次导出冷回放均正常击杀并返回神居，21 个暂停检查点的角色、敌人、FSM、分裂组件和碰撞几何语义一致，fault/mismatch=0。成片从 Movie 1400 后开始，包含加载帧共 2661 帧，800×450 / 50 fps H.264、48 kHz 双声道 AAC，音视频各 53.22 秒；画面抽查正常，解码音频非静音。证据位于 `artifacts/envious-marmu-kill/`。这是该序列和环境的检查点验证，不代表其他场景或逐帧 RNG 一致性已验证。
