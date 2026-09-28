# ModBoss 与 TAS 兼容情况

2026-09-28，已加入预加载和无参 `System.Random()` 兼容。公开包版本及 SHA256 记录在 `artifacts/boss-mod-audit/` 和 `artifacts/mod-startup-compat/`。

| Mod | 当前结论 | 需要关闭什么 |
| --- | --- | --- |
| 嫉妒马尔穆（本机版） | 原辐辉序列通过 | 无新增关闭项 |
| HuKing 2.1 | 预加载启动通过；加载它的新环境下，3800 帧严格冷重放两轮通过 | 不必关闭预加载；尚未验证 HuKing 整场战斗 |
| WeaverCore 2.2.0.7 | 预加载启动及新环境严格回放通过；实际招式洗牌函数两次冷启动结果一致 | 无新增关闭项 |
| Inferno King Grimm 4.2.3、Corrupted Kin 1.4 | 已处理共用的 WeaverCore 预加载和洗牌问题；各自完整战斗待验证 | 无针对这两类问题的关闭项 |
| DoodleBosses 0.9.6、RadianceSkin 0.1.1、TribeOfBattle 1.2 | 已加入通用预加载兼容；尚未逐个实测 | 不再仅因强制预加载判定不能装 |

加入或移除 Mod 后，旧序列的输入调用顺序可能变化。本轮旧序列加 HuKing 在第 400 帧失配；按原输入意图执行并重新采集实际输入后，两次严格重放通过。原序列文件未修改；撤回测试 Mod 后另做原序列回归。不能承诺跨 Mod 环境直接使用旧序列。

独立随机数兼容范围：TAS 启动保护安装后，由 Mod 在游戏主线程构造的无参 `System.Random()`，按调用位置和构造次数给出稳定种子。保留显式种子；普通启动不启用。已创建的旧实例、显式时间种子、自定义 PRNG 和多线程调度不在此范围。Movie 的 `rngSeed` 字段仍只重设 Unity RNG。

来源：[ModLinks](https://github.com/hk-modding/modlinks/blob/main/ModLinks.xml)、[WeaverCore 2.2.0.7](https://github.com/nickc01/WeaverCore/tree/v2.2.0.7)。详细验证记录：`artifacts/mod-startup-compat/REPORT.md`。
