# SDD Spec: HollowKnightTASMod 研究文档与私有仓库

## 0. Open Questions

- [x] 初始文档可以使用“Windows 常见 1.5.x + HK Modding API”作为研究范围；精确游戏构建留给 Phase 0 探针。
- [x] GitHub CLI 认证可用：`windplusflower`，具备 `repo` scope；同名仓库在该账号下不存在，已安全创建 private 远端。

## 1. Requirements (Context)

- **Goal**: 整理上一轮“以 Hollow Knight Mod 实现 TAS、尽量覆盖并在体验上超过 libTAS”的调研为有来源、可长期维护的中文研究仓库，并将其作为私有 GitHub 仓库推送。
- **In-Scope**: `README.md`、必要的 `docs/` 补充文档、SDD 过程记录、Git 初始化/提交、private GitHub 仓库创建或安全复用、远端验证。
- **Out-of-Scope**: C# Mod 实现、修改 Hollow Knight 安装、替换游戏 DLL、原生注入、部署、公开仓库、覆盖既有远端内容。

## 1.1 Context Sources

- Requirement Source: 当前用户明确转述及执行授权。
- Research Source: `/home/windflower/.nanobot/codex-async-use/logs/a120931f4a/20260727T123725Z-new/final.txt`
- Process Source: `/home/windflower/.nanobot/codex-async-use/logs/a120931f4a/20260727T123725Z-new/events.jsonl`
- Transcript Source: `/home/windflower/.nanobot/workspace/transcript-viewer/group-details/0001-2026-07-27homewindflowernanobotworkspacecodexhk-tas-mod-feasibility1.json`
- Fresh verification: HK Modding API、libTAS、Unity 官方文档页面，访问日期 2026-07-27。

## 1.5 Codemap Used (Feature/Project Index)

- Codemap Mode: `project`
- Codemap File: `mydocs/codemap/2026-07-27_20-54_hollowknighttasmod项目总图.md`
- Key Index: 当前仓库为空白研究仓库；目标结构、输入来源与未来运行时链路已索引。

## 1.6 Context Bundle Snapshot (Standard)

- Bundle Level: `Standard`
- Bundle File: `mydocs/context/2026-07-27_20-54_hollowknighttasmod_context_bundle.md`
- Key Facts: 上一轮完整结论及 14 个原始 URL 均已读取；v77 版本信息已于 2026-07-27 重新访问核验。
- Open Questions: 目标游戏构建和 GitHub CLI 认证状态。

## 2. Research Findings

- 纯 Mod 的正确目标是“语义级、可验证的输入重放 TAS”，而非 libTAS 的进程级等价替代。
- 最高价值差异化是 HK 专用检查器（Hero/FSM/hitbox/enemy HP/scene）、受限 DSL、时序账本与可复验 movie；全局即时 savestate 和环境虚拟化不能作为纯 Mod 承诺。
- `Update`、`FixedUpdate`、T-FT、RNG、协程、场景加载、音频和其他 Mod 是确定性风险；应通过 Phase 0 原型而不是文档断言消除。

## 2.1 Next Actions

- 初始化独立 Git 仓库，提交，随后只读检查 GitHub CLI 认证并按安全规则创建/复用 private 远端。
- 对 Git 本地与远端执行端到端验证。

## 3. Innovate (Optional: Options & Decision)

### Option A

- Pros: 只保留一个 README，浏览快捷。
- Cons: 引用、方法与实验细节会使首页过长、难以审计。

### Option B

- Pros: README 作为完整可读摘要，`docs/` 放来源索引和实验清单，兼顾首页与长期维护。
- Cons: 读者需在需要证据细节时打开补充文档。

### Decision

- Selected: Option B。
- Why: 满足“README 至少包含全部主题”与“逐条有来源”两个要求，同时避免首页被机械证据表淹没。

## 4. Plan (Contract)

### 4.1 File Changes

- `README.md`: 中文研究主文档；包含结论、能力矩阵、架构、路线、风险、近旁引用及来源入口。
- `docs/来源与证据索引.md`: 14 个原始 URL 的保留、访问日期、用途、事实/推断边界。
- `docs/验证实验清单.md`: Phase 0/1 原型实验、指标、Go/No-Go、记录模板。
- `memory/HISTORY.md`: 可恢复执行检查点。
- `mydocs/**`: SDD 上下文、项目图、规格记录；属于本次决策追溯资产。

### 4.2 Signatures

- 无代码 API、函数或类签名。本任务仅生成 Markdown 研究文档和 Git 元数据。

### 4.3 Implementation Checklist

- [x] 1. 写入研究首页与补充文档，所有关键事实就近标记来源。
- [x] 2. 本地核查 Markdown、URL、来源映射和文档边界：README 包含全部指定主题；14/14 原始 URL 已保留；占位符扫描通过；14 个 URL 的 HTTP 连通性均为 200。
- [ ] 3. 在当前独立目录初始化 Git，设定 `main`，提交全部文档。
- [ ] 4. 仅读取 `gh auth status`；认证可用时，安全创建或复用精确名为 `HollowKnightTASMod` 的 private 仓库并推送。
- [ ] 5. 输出 `git status/log/remote`、`gh repo view` 与远端提交验证，完成三轴 Review。

### 4.4 Spec Review Notes

| Check | Verdict | Evidence |
|---|---|---|
| Requirement clarity & acceptance | PASS | 用户指定了输入、文档内容、远端名称/私有性和验证命令。 |
| Plan executability | PASS | 文件路径、无代码签名说明和原子 checklist 已明确。 |
| Risk / rollback readiness | PASS | 本地 Git 可回退；同名远端先验属主/私有性检查；不覆盖不相关仓库。 |

- Readiness Verdict: GO（建议性）。
- User Decision: 用户在任务描述中已明确批准直接执行，无需再次要求 Plan Approved。

## 5. Execute Log

- [x] Step 0: 读取 SDD-RIPER 与 Hollow Knight Modding 规范，读取三项原始调研产物，访问核验关键一手来源。
- [x] Step 1: 生成 README、来源索引和验证实验清单；完成 README 章节、14 URL 保留、占位符与 URL 连通性核查。
- [x] Step 2: 初始化当前目录自己的 Git 仓库，主分支为 `main`；提交 `8221727 docs: add Hollow Knight TAS feasibility research`。
- [x] Step 3: 仅读取 `gh auth status`，确认 `windplusflower`；读取同名仓库视图确认不存在后，以 `--private` 创建 `windplusflower/HollowKnightTASMod`，设定 `origin` 并推送 `main`。
- [x] Step 4: 端到端验证初始提交：本地 `HEAD` 与 `refs/heads/main` 均为 `8221727e3b52ce44a9dba11647d186fc8add50b9`；仓库视图显示 `visibility=PRIVATE`、默认分支 `main`。

## 6. Review Verdict

| Axis | Key Checks | Verdict | Evidence |
|---|---|---|---|
| Spec Quality & Requirement Completion | README 覆盖结论、边界、能力矩阵、运行时约束、架构、核心子题、Celeste 对照、MVP、风险实验与来源；14 个原始 URL 被完整保留。 | PASS | README 211 行；URL 保留检查 `14/14`；来源 HTTP 连通性 `14 × 200`。 |
| Spec-Code Fidelity | 实际文件与 Plan 的 `README.md`、两个 `docs/` 文档、`memory/HISTORY.md`、`mydocs/**` 相符；无 Mod 代码或越界游戏/系统变更。 | PASS | 初始 commit `8221727` 含 7 个预期 Markdown 文件；独立 Git 工作树与远端 `main` 一致。 |
| Code Intrinsic Quality | 无代码；文档链接、占位符与 Git/远端均已核验。研究结论的剩余风险已被显式降级为 Phase 0 实验，而非伪装为事实。 | PASS | `git diff --cached --check`、占位符扫描、来源 URL HTTP 检查、`gh repo view`、`git ls-remote` 通过。 |

- Overall Verdict: PASS
- Blocking Issues: None。
- Regression risk: Low（仅新增研究文档和 Git 元数据）；未来 Mod 实现的技术风险为 High，但已在 Phase 0/1 实验计划中隔离。
- Follow-ups: 在任何 Mod 代码立项前先执行 `docs/验证实验清单.md` 的 P0-1 至 P0-5。

## 7. Plan-Execution Diff

- 无偏差。用户授权跳过再次索要 Plan Approved，已在 §4.4 记录；此举符合当前任务的明确执行授权。

## 8. Archive Record

- Skipped: 本次 Git 仓库本身即为面向人和后续实现的知识沉淀；不额外复制 archive，避免冗余文档。
