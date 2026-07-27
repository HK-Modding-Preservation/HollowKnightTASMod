# HollowKnightTASMod 项目总图

## 作用域

- **active_project**: `HollowKnightTASMod`
- **active_workdir**: `/home/windflower/.nanobot/workspace/codex/HollowKnightTASMod`
- **change_scope**: `local`
- **当前类型**: 文档优先的空白研究仓库；本轮不实现 Hollow Knight Mod 代码。

## 当前结构与职责

```text
README.md                         GitHub 首页：可行性结论、能力边界、路线图
docs/
  来源与证据索引.md               原始 URL、断言追溯与证据等级
  验证实验清单.md                 原型的 Go/No-Go 实验与验收记录模板
mydocs/
  context/                        本次 SDD 输入上下文包
  codemap/                        本文件
  specs/                          本次 SDD 规格与执行/评审记录
memory/HISTORY.md                 本次可恢复执行检查点
```

## 关键链路（目标架构，非已实现代码）

```text
文本 movie / Studio
        ↓
输入 DSL 解析 + 静态校验
        ↓
Hollow Knight 运行时 Mod 的主线程命令队列
        ├── 动作层输入替代 / 录制 / 回放
        ├── Tick ledger（visual tick、fixed tick、T-FT）
        ├── 状态检查器（Hero、FSM、Collider、RNG）
        └── 哈希、检查点与重放验证
```

## 依赖与风险索引

| 范畴 | 依赖/事实来源 | 对仓库的影响 |
|---|---|---|
| Mod 加载与版本 | HK Modding API 发布物及其 MonoMod 路线 | 未来代码必须对目标游戏构建建立适配层与指纹。 |
| 时间与物理 | Unity 的可变 `Update`、固定 `FixedUpdate`、`timeScale` | movie 的最小时间单位必须经原型确定，不能先假定等于渲染帧。 |
| 历史 TAS 实践 | HollowKnightTasInfo + libTAS | 可借鉴 T-FT、RNG、碰撞箱与日志；不能照搬其替换 DLL 的加载方式。 |
| 纯 Mod 的边界 | 无进程/内核级控制权 | 不把通用即时 savestate、全局线程/文件/时间虚拟化列为 MVP 承诺。 |

## 本轮变更边界

仅创建研究文档和 Git 元数据；没有 C#、DLL、Unity 资产、游戏文件、配置或服务变更。
