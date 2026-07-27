# HollowKnightTASMod 上下文包

## Source Index

| 输入 | 角色 | 处理结果 |
|---|---|---|
| `/home/windflower/.nanobot/codex-async-use/logs/a120931f4a/20260727T123725Z-new/final.txt` | 上一轮完整结论 | 已逐段读取；为主要研究内容来源。 |
| `/home/windflower/.nanobot/codex-async-use/logs/a120931f4a/20260727T123725Z-new/events.jsonl` | 上一轮过程与检索轨迹 | 已读取；核对检索主题与原始 URL。 |
| `/home/windflower/.nanobot/workspace/transcript-viewer/group-details/0001-2026-07-27homewindflowernanobotworkspacecodexhk-tas-mod-feasibility1.json` | 原始会话转录 | 已读取；核对用户问题、只读研究范围和最终答复。 |

## Requirement Snapshot

- 产出一个以中文撰写、可长期参考的 Hollow Knight TAS Mod 可行性文档，作为 GitHub 仓库首页。
- 必须覆盖 libTAS 的能力拆分、Unity/Hollow Knight 运行时限制、能力矩阵、推荐架构、确定性、输入、逐帧、存档/状态、RNG、渲染/音频、调试、CelesteTAS 对照、MVP、风险实验和引用边界。
- 每个重要断言附近应可追溯到来源；保留上一轮 14 个原始 URL；把事实、推断、建议明确分开。
- 初始化独立 Git 仓库，主分支 `main`，提交文档；认证可用时创建精确名为 `HollowKnightTASMod` 的 GitHub 私有仓库并推送。
- 仓库只放研究文档，不实现 Mod 代码。

## 研究事实（已由资料或本轮访问核对）

1. Modding API v77 的发布页标明对应 Hollow Knight `1.5.78.11833`，并给出平台校验和；本轮访问日期为 2026-07-27。
2. HollowKnightTasInfo 是围绕 Linux/libTAS 的历史工具链；其 README 明确写明覆盖 `Assembly-CSharp.dll`，且侵入式功能不能与未修改游戏同步。
3. Unity 的 visual frame 与固定物理步不是一对一关系；`timeScale = 0` 不会调用 `FixedUpdate`，但不等于停止所有可运行逻辑。
4. libTAS 的能力边界在进程级拦截，而纯托管游戏内 Mod 的优势应定位在 Hollow Knight 的语义数据与工作流。

## 约束与验收

- 不把工程推断包装成官方事实；无法从来源直接得到的可行性判断必须标为“推断”或“建议”。
- 在创建远端前仅读取 `gh auth status`；只创建 private 仓库；同名仓库只在当前账号且 private 时复用，绝不覆盖不相关仓库。
- 最终验证要保留本地 Git、remote 与 GitHub 仓库视图的关键原始输出。

## Open Questions

- 目标初始游戏构建、运行平台、是否允许未来原生辅助进程，尚未由用户指定；文档将其作为未来 Phase 0 决策，而不阻塞研究仓库创建。
- 当前 GitHub CLI 的认证和用户名尚未检查；其结果决定是否能创建/推送远端。
