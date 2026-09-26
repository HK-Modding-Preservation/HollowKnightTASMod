# EnviousMarmu 兼容性与速杀 TAS

## 用户目标

安装同级目录 `../EnviousMarmu` 的用户自制 Mod，使用正常 TAS 输入，在神居椅子上调整护符并击败嫉妒的马尔穆，尽可能速杀。

## 执行边界

- 当前正式 v2 全流程路径，从受保护 Native frame 0 开始，包含标题选第 4 槽、椅子配置、进入雕像挑战与战斗。
- 默认选择调谐难度；速度以实际战斗输入帧计算，另报告全流程长度。
- 使用原生输入、正常护符和法术；禁止改写角色、敌人、随机数、场景或资源状态。
- 原始 `user*` 存档文件保护并在结束后核对集合和 SHA-256。
- 独立只读观察器仅用于编写及诊断；最终须说明验证时的实际 Mod 集合，不能把死亡根 Boss 当作全部分裂体已击败。
- 保留现有 TAS 安装二进制、历史示范、时间线与证据；不重跑无关矩阵。

## 安装已完成（2026-09-26）

- 来源：`C:/Users/33361/Desktop/Modding/EnviousMarmu`，提交 `0821a1df3c1732645ff38b633bcf623e8a9fba4e`。
- `dotnet build -c Release --nologo`：0 警告、0 错误，项目自身安装目标已把 Mod 安装到游戏 `Managed/Mods/EnviousMarmu`。
- 已安装 DLL SHA-256：`3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`。
- 原始存档审计与安装身份：`artifacts/envious-marmu-tas/saves-before.json`、`installed-identity.json`。

## 机制与验收

EnviousMarmu 在 `GG_Ghost_Marmu` / `GG_Ghost_Marmu_V` 复用原版 Control FSM；1 巨分裂成 2 大、4 中、8 小，共 15 个。子代出生无敌 1 秒，最后一个小死亡才结束战斗。优先实测群体法术与骨钉回魂组合。

待完成：

1. 实机确认 Mod 加载和正确挑战入口。
2. 记录椅子护符配置，编写并优化正常输入 Movie。
3. 记录全部分裂体死亡、原生战斗收尾和返回神居；保存可重放 `.hktas`。
4. 冷启动复现相同结果，核对逐帧轨迹及原始存档不变。

当前只完成安装；尚无击杀或兼容性通过声明。
