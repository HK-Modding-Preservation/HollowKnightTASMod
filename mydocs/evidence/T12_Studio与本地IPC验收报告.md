# T12 Companion/Studio、本地 IPC 与 Mod 自动启动验收报告

## 结论

**T12：PASS / VERIFIED。**

Runtime 能从固定随包路径验证并自动启动 Companion，手动启动、签名自动启动和
已有实例复用均通过；本地 IPC、主线程队列、单实例、生命周期恢复、
Companion off/on 确定性对照和连续 60 分钟订阅耐久全部通过。

## 正式证据

正式矩阵：

`artifacts/companion/t12-final-formal-retry2-20260731T083500000Z/`

根 `matrix.json` 结果：

- `pass=true`；
- 3 个 launch case 全部通过，均完成认证、命令消费和安全停止；
- Companion off/on 的 5 个
  `milestoneId/movieTick/semanticSha256` 投影逐项一致；
- 60 分钟 subscription soak 未跳过，嵌套 Inspector full gate 通过；
- 4 个普通槽未改变，原 Replay Store 已恢复。

三种启动路径分别验证：

1. `AutoStartCompanion=false` 时游戏启动后外部进程数为 0，再由受信入口手动启动；
2. `AutoStartCompanion=true` 时从签名固定路径自动启动并完成握手；
3. 当前用户已有 Companion 实例时只 attach/reuse，不重复创建实例。

三种 case 均观察到结构化 launcher 状态、8 条受信命令和 3 次 snapshot
outcome；existing-instance case 不经过 `Launching`，符合复用状态机。

## T07 语义对照

T12 使用显式 `DEPENDENCY_ORACLE` 模式，只比较正常冷启动，不重复冒充 T07
自身的 deliberate-divergence 验收。Companion off/on 的 manifest 因设置不同而
不同，但下列 5 个语义 milestone 完全一致：

- baseline，tick 0；
- `checkpoint:pre-divergence`，tick 490；
- `checkpoint:divergence-probe`，tick 491；
- `assert:scene.name==GG_Vengefly`，tick 611；
- settled endpoint，tick 611。

五项 semantic SHA-256 均为
`ddf9fd7b744a70c568ea7d4ccb34ceedf31a2b89e0aa711949cf2f5de5ab1d8a`。

## 60 分钟耐久与 Inspector

T12 订阅 soak：

- 请求时长：3,600 秒；
- 实际采样时长：3,635.037 秒；
- 采样数：364；
- Companion 前 10% private bytes 均值：204,185,031；
- 后 10% private bytes 均值：230,884,580；
- 稳态增长：26,699,548 bytes；
- 峰值 private bytes：239,337,472；
- 门限：增长不超过 384 MiB，峰值不超过 2 GiB；
- 结果：两项均通过。

嵌套 T11 matrix：

- 2 次 FUNCTIONAL 均通过；
- stable identity SHA-256：
  `50f86a0f7bfe77e18e944142f60dcd223c693c5ec2604118c6641d9a9d9edc45`；
- 每次 41 个 stable key、6 个 display-only key；
- PERFORMANCE 实际运行 3,600 秒并通过；
- watch 记录 5,046 行；
- sample p95 为 0.1719 ms；
- Inspector attributable average 为 0 bytes/frame；
- provider failure 与 exporter drop 均为 0。

同次正式矩阵完成：

- Core：196 passed / 1 skipped；
- Companion：17 passed / 0 skipped；
- Release bundle 验证：PASS。

## 正式运行前修正

最终从零重跑前修正了三个真实集成问题：

1. T12 on/off 对照改用 T07 的显式 dependency-oracle 模式，避免把 T07 独立
   deliberate-divergence 自检错误纳入 T12；
2. Runtime Inspector probe 的 performance duration 接受范围由 1200 秒统一为
   驱动已发布的 `[60,7200]`；
3. Godhome 槽位进入 `GG_Workshop` 时，T11 probe 不再要求先出现
   `LastCommittedMovieTick` 才跳转固定测试场景，消除了起始场景尚无中性
   journal 基线造成的循环等待；进入测试场景后仍保留真实 journal、
   Inspector provider 和全部功能/性能门禁。

`artifacts/inspector/t11-duration-boundary-smoke-retry2-20260731T083100000Z/`
先以 `performanceSeconds=1201` 连续完成 2 次 FUNCTIONAL，证明参数和 Godhome
启动路由均已修复；该 smoke 不替代正式 3,600 秒证据。

## 安装包与恢复

正式运行使用的 Release bundle 共 477 个签名文件：

- manifest SHA-256：
  `1cb9bab2f9c418113b2b757179ef8569134608a246ce9ed553bb719cb9a59fa1`
- package SHA-256：
  `ff0d39084a6b76177394a5a305dae291b797fe3d37e914df337dcef04702328e`

矩阵结束后确认：

- settings SHA-256：
  `AB075AA739DD34B44FB11D918ADE28DA4CA8C888F76B0BF4616358528B4E80E5`
- slot 1：
  `413613FF631E479EAAF7E5E4B2026354478A0E9DC20C1AA7F50AA82055C8B389`
- slot 2：
  `1ACD6214B8DACAF112A2FBAE0E5E7AEB3D487C9365F5F955E4AEF8E51459DADE`
- slot 3：
  `9ABF27172D1003B607E543B88FD8759DCB0FD9668AD3D57A3A286C9BF4DB4855`
- slot 4：
  `A6F09F2E7DE8F2B2CD25C8EB923F8F6728120646F97671A1296C00896186B062`
- 4 个 `user*.modded.json` 也与矩阵初始 hash 完全一致；
- `hollow_knight`、Companion、NativeHost 产品进程为 0；
- `Mods.HKTAS-*` 与项目恢复目录均为 0；
- 原 Replay Store 存在性与内容已恢复。

## 边界

T12 证明的是外部进程安全、本地 IPC、自动启动、Studio/Companion 生命周期和
长时订阅可靠性。NativeHost 双模式确定性由 T13 独立验收；从诸神堂椅子到击败
调谐假骑士的非视觉自编 TAS 与五次冷启动复放仍由 T16 独立验收。
