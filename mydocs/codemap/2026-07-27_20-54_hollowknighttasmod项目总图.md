# HollowKnightTASMod 项目总图

## 作用域

- **active_project**: `HollowKnightTASMod`
- **active_workdir**: `C:\Users\33361\Desktop\Modding\HollowKnightTASMod`
- **change_scope**: `local`
- **当前类型**: 已进入按 Spec 顺序实施；T01-T13 与 T15 已验证，T14 按 ReplayOnly 降级验证，T16 为当前任务。

## 当前结构与职责

```text
README.md                         GitHub 首页：可行性结论、能力边界、路线图
docs/
  compatibility/rng/             目标游戏构建 RNG codec/call-site 白名单
  protocol/                      Movie、Semantic Snapshot、Replay Save 协议
  来源与证据索引.md               原始 URL、断言追溯与证据等级
  验证实验清单.md                 原型的 Go/No-Go 实验与验收记录模板
src/
  HollowKnightTAS.Core/          稳定协议、hash、ledger、movie、replay save、RNG、typed watch
  HollowKnightTAS.Runtime/       HK Mod 运行时、录制回放、控制、存档、RNG 与 Inspector
  HollowKnightTAS.Cli/           manifest/movie/verification 离线命令
  tests/HollowKnightTAS.Core.Tests/ Core 契约与回归测试
scripts/                         实机隔离验证 harness
mydocs/
  context/                        本次 SDD 输入上下文包
  codemap/                        本文件
  specs/                          总 Spec 与 T01-T16 独立任务 Spec
  evidence/                       T01-T15 验收报告
memory/HISTORY.md                 可恢复执行检查点
```

## 关键链路

```text
文本 movie / Studio
        ↓
输入 DSL 解析 + 静态校验（已实现）
        ↓
Hollow Knight 运行时 Mod 的主线程命令队列
        ├── 动作层输入替代 / 录制 / 回放（T06）
        ├── Tick ledger（visual tick、fixed tick、T-FT，T03）
        ├── Hero/scene 语义 hash（T05/T07）
        ├── 任意/定时持久化重放存档（T09）
        ├── Unity RNG state + partial call-site 诊断（T10）
        ├── FSM/Collider/敌人 typed watch + overlay（T11）
        └── authenticated IPC / Companion launch（T12 已验证）
```

## 依赖与风险索引

| 范畴 | 依赖/事实来源 | 对仓库的影响 |
|---|---|---|
| Mod 加载与版本 | HK Modding API 发布物及其 MonoMod 路线 | 未来代码必须对目标游戏构建建立适配层与指纹。 |
| 时间与物理 | Unity 的可变 `Update`、固定 `FixedUpdate`、`timeScale` | movie 的最小时间单位必须经原型确定，不能先假定等于渲染帧。 |
| 历史 TAS 实践 | HollowKnightTasInfo + libTAS | 可借鉴 T-FT、RNG、碰撞箱与日志；不能照搬其替换 DLL 的加载方式。 |
| 纯 Mod 的边界 | 无进程/内核级控制权 | 不把通用即时 savestate、全局线程/文件/时间虚拟化列为 MVP 承诺。 |

## 当前边界

- 已验证范围限定于锁定本机 manifest；跨安装仍为 `NOT_RUN`。
- T10 只读诊断且 coverage 为 partial，不提供 RNG playback。
- T11 Inspector 只读；默认每 120 movie tick 采样，外部低延迟按需读取属于 T15。
- T12 60 分钟 Companion/IPC、T13 签名只读 NativeHost 与 10+10 逐 run 原生/语义矩阵、T15 非视觉 Automation/AI 接口均已验收；T14 已按 `ReplayOnly` 验收，当前只剩 T16 最终自编 TAS。
