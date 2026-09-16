# T15 外部自动化与 AI 辅助接口验收报告

## 结论

**T15：PASS / VERIFIED。**

Runtime 仍只接受 Companion 的受信 IPC；外部 SDK、CLI 与 stdio MCP
AgentBridge 均通过同一个 Companion broker、认证、capability、控制租约、
前置条件、幂等和审计边界工作。状态读取、movie 分支和控制流程全部基于结构化
语义数据，不依赖截图、OCR、像素或其他视觉识别。

各 `matrix.json` 中的 `*_PARTIAL_PASS` 表示该文件只执行了 T15 的一个互斥
子矩阵；下列所有子矩阵共同通过后，本报告给出 T15 聚合 PASS。

## 正式证据

| 子矩阵 | 证据目录 | 结果 |
|---|---|---|
| 权限发现 | `artifacts/automation/t15-20260729T072755575Z-806411ae/` | ReadOnly 下 SDK/CLI/MCP 状态读取通过，控制明确拒绝 |
| 完整控制 parity | `artifacts/automation/t15-full-control-parity-20260729T093154858Z/` | Disabled、ReadOnly、ApprovedControl 三模式通过；SDK/CLI/MCP 的暂停、步进、run-until、录制/回放、Replay Save 均 10/10 |
| typed mutation | `artifacts/automation/t15-mutation-formal-20260729T095512074Z/` | pose/resources 的 SDK/CLI/MCP 各 10/10；9 个拒绝场景、回滚、断线释放和干净重启隔离通过 |
| scripted AI/MCP | `artifacts/automation/t15-ai-formal-20260729T102003036Z/` | 10/10 非视觉闭环；非法候选先拒绝、隔离 proposal、显式 apply、replay milestone、0 desync、lease 释放 |
| 安全矩阵 | `artifacts/automation/t15-security-formal-20260729T102147505Z/` | 8 URI、8 tool、4 schema 攻击拒绝；超 1 MiB 行拒绝后恢复；状态不变；异常断线释放 lease |
| 状态耐久 | `artifacts/automation/t15-state-soak-formal-20260731T063713629Z/` | 10,000 reads、100 reconnects、3,602.028 秒 watch、13,556 页、10,103 事件、最大状态年龄 5ms |

正式状态耐久矩阵的 Companion private bytes：

- warm-up：212,365,312 bytes；
- 峰值：238,350,336 bytes，增长 25,985,024 bytes；
- 结束：229,335,040 bytes，增长 16,969,728 bytes；
- 门禁：峰值增长不超过 256 MiB，结束增长不超过 64 MiB；
- 结果：两项均通过，最终 `verificationEligibility=Eligible`。

同次正式运行完成 Core 196 passed / 1 skipped、Companion 13 passed、
AgentBridge 7 passed；9 个 audit 文件共 36,015 条，credential/个人信息泄漏
匹配为 0。

## 长时内存故障与修正

第一次完整 60 分钟运行证明 CLI duration 已正确，但 Companion 结束增长
162,762,752 bytes，未通过 64 MiB 门禁。根因是 Broker 的 5,000 条 timeline
和 2,048 条完整幂等响应只有条目数上限，没有 payload 字节上限。

修正后：

- timeline 保存 canonical payload bytes，不再保存每条事件的完整字典对象；
- timeline 同时受 5,000 条与 8 MiB payload 上限约束；
- 淘汰游标只保留 sequence/movie tick；
- 只读 observation/validation 不保留完整幂等响应；
- 有副作用命令的结果受 2,048 条/8 MiB 双上限约束，淘汰 key 进入有界
  session tombstone，重复使用 fail closed；
- `memory-samples.json` 在阈值断言前写入，失败也保留完整轨迹。

300 秒真实诊断先确认结束增长仅 8,200,192 bytes，随后正式 60 分钟矩阵从零
重跑并通过；未复用失败运行的时长、页面或内存样本。

## 安装包与恢复

正式运行前的 Release bundle 构建为 0 warning / 0 error，477 个文件签名验证
通过：

- bundle manifest SHA-256：
  `1cb9bab2f9c418113b2b757179ef8569134608a246ce9ed553bb719cb9a59fa1`
- package SHA-256：
  `21bc544a0082ad9b27da7edb56f90048b1ba49e458d66a525456d4d527302e6c`

所有正式矩阵结束后均确认：

- 普通槽 1-4 未改变；
- Mod 列表、设置、Replay Store 与 automation workspace 已恢复；
- 无 `hollow_knight`、Companion、AgentBridge 或 NativeHost 孤儿进程；
- 无 `Mods.HKTAS-T15-*` 恢复目录残留；
- audit 泄漏扫描为 0。

用户数据最终 SHA-256：

- settings：
  `AB075AA739DD34B44FB11D918ADE28DA4CA8C888F76B0BF4616358528B4E80E5`
- slot 1：
  `413613FF631E479EAAF7E5E4B2026354478A0E9DC20C1AA7F50AA82055C8B389`
- slot 2：
  `1ACD6214B8DACAF112A2FBAE0E5E7AEB3D487C9365F5F955E4AEF8E51459DADE`
- slot 3：
  `9ABF27172D1003B607E543B88FD8759DCB0FD9668AD3D57A3A286C9BF4DB4855`
- slot 4：
  `A6F09F2E7DE8F2B2CD25C8EB923F8F6728120646F97671A1296C00896186B062`

## 边界

- typed debug mutation 会把运行永久标为
  `NonVerifiableDebugMutation`，不得用于 T07/T16 计分结论。
- AI/MCP 没有隐藏权限；它只能使用 capability catalog 中普通客户端同样可见
  的资源和 typed tools。
- T15 证明外部非视觉观察、调试和受控 AI workflow 可用；从诸神堂椅子到击败
  调谐假骑士的最终自编 TAS 与五次冷启动复放仍由 T16 独立验收。
