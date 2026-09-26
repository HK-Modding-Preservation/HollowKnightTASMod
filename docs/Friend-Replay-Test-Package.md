# 嫉妒马尔穆跨机器回放测试包（2026-09-27）

本轮只整理交付和做本机验证，没有更改生产代码或重建安装程序集。

桌面交付：`TAS回放测试包_嫉妒马尔穆_2026-09-27.zip`，168674334 字节，SHA256 `599b99fc216de7b74e89c43722598779cc95a5545417824de48dd1d235eb43c2`。

包含两个可直接解压到 Mods 的安装包（各自已带 HollowKnightTAS / EnviousMarmu 顶层目录）、一个绑定存档的辐辉无伤序列包、中文安装说明、环境参考和校验清单。没有附带游戏或 Modding API，没有附带本机日志、令牌或描述符。

## 验证结果

- TAS 安装包 492 个文件、EnviousMarmu 安装包 2 个文件，逐项与安装版来源 ZIP 内容 SHA256 一致；外层 6 项 CRC 和内容 SHA256 通过。
- Runtime `31c8dcdd3d4ab0a3205325e555d5fb0ba732d629ab82a4a4ddededae0af09d5b`；Core `a4e5e1edc6429cc22f08c91bb99378b0a7954e66cdf2aa3426a1696c16a0f294`；EnviousMarmu `3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`。
- 序列共 3800 帧、50 fps。原辐辉序列所有规范输入不变，仅候选副本的环境头更新为本次实际录制得到的 `edd23cadd64549635b2f5ece64cec1dfd72655ebfba53a5b1b7a6dcf45c63b3c`，并完成本机全程回放后才交付。原 `.hktas` 和原 `.hktaspack` 未改。
- 包含原有四槽初始快照 20 文件，InitialSavesId `e67b5e8abe64112c26e777c49ec341cc2c18b69d73c4b7e2243910964b313848`。包内存档与输入校验、读写 round trip 通过。
- 使用安装版 Companion DLL 的实际 App/VM/签名启动器/Runtime 路径，默认 TAS 配置：3354 帧到 `GG_Ghost_Marmu_V`，角色 9 血，分页只读世界查询确认没有存活的激活 EnviousMarmu 敌人；3800 帧 Completed、`GG_Workshop`、9 血、fault/mismatch=0。
- 31 个原始 user* 文件前后文件集/哈希/长度一致。测试前本机 TAS 设置精确恢复，鼠标与帧率设置恢复。游戏和测试程序已退出；测试时间线/快照放在隔离证据目录。

## 兼容性边界

这是朋友机器的待测包，不是跨机器通过声明。当前环境哈希包含 Windows 版本、系统区域、游戏语言及画面设置。本次为 Windows NT 10.0.26200.0 x64、zh-CN、游戏语言 EN、800×450 Windowed、默认 TAS 设置，仅启用两个包内 Mod。其他系统或设置可能被环境校验拒绝；未放宽校验或把失败改写为兼容。游戏/API 要求 1.5.78.11833 / 1.5.78.11833-77，ClockStartup 仍验证游戏二进制身份。

## 证据

`artifacts/friend-replay/live-03/scenario.log`、`migration.json`、`replay-result.json`、`battle-world-*.json`；外层 `saves-before.json` / `saves-after.json`、`delivery-verification.json`、`final-package.json`。临时 harness 源码保留在同目录 `harness-src`，直接引用安装 DLL，没有重建生产程序集。

首次 harness 为 framework-dependent 且误混入本地 runtime，未启动游戏；修为自包含。第二次因 2 GiB 空闲物理内存阈值在战斗前终止；本轮将本地测试阈值设为 1 GiB，保留 2 GiB commit 余量、游戏 3 GiB/宿主 768 MiB 上限与 6 分钟超时，第三次通过。失败尝试日志保留，不计作回放通过。stderr 的旧会话 `Runtime session changed` 提示保留，最终当前会话无 fault/mismatch。
