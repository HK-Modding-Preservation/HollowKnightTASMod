# T11 语义 Inspector 与叠层验收报告

## 结论

T11 为 **PASS / VERIFIED**。最终矩阵位于：

```text
artifacts/inspector/final-10-load-10min/
```

本任务已证明同一份 immutable typed `WatchFrame` 可以驱动游戏内文本叠层、
Collider 轮廓与 JSONL 导出；稳定身份、场景生命周期、禁用语义和默认性能
预算均通过。Inspector 保持只读，不提供任意反射或 gameplay 状态修改。

## 交付能力

- Core `WatchKey`、typed `WatchValue`、descriptor、frame、registry 与
  canonical JSON schema。
- Runtime tick/scene、Hero、RNG、显式 FSM、enemy 和 Collider providers。
- 文本 overlay、Collider outline 与有界异步 JSONL exporter。
- `VerificationKey` 与 `DisplayKey` 分离；动态 enemy 无稳定 adapter 时只
  产生 `display-only` key，不进入验证哈希。
- scene change 时清除旧 provider/Unity 引用并重建；禁用 Inspector 后停止
  采样、绘制与导出。
- 功能探针在 registry 完成采样的同一 `LateUpdate` 回调内逐字段核对直接
  组件读数，避免跨 Unity 更新点比较旧帧。

## 离线验证

```text
dotnet build .\HollowKnightTAS.sln -c Debug
0 warning / 0 error

dotnet test .\tests\HollowKnightTAS.Core.Tests\
  HollowKnightTAS.Core.Tests.csproj -c Debug --no-build
129 passed / 0 failed
```

测试覆盖 typed value/canonical JSON、重复注册与失效、采样节流、provider
失败隔离、稳定 key schema 以及 read-only watch 契约。

## 10 次功能矩阵

10 个独立冷加载进程均完成：

```text
runPass                    = true
stopReason                 = Completed
directParity               = true
overlayGroupChanged        = true
overlayRendered            = true
colliderRendered           = true
exportSameSource           = true
exportPauseHeld/Resumed    = true/true
disableStoppedSampling     = true
disableStoppedRendering    = true
sceneClearObserved/Rebound = true/true
screenshotWritten          = true
providerFailureCount       = 0
exporterDroppedCount       = 0
```

每次均得到 41 个 verification-stable key、6 个 display-only key；稳定 key
集合的跨运行 SHA-256 为：

```text
50f86a0f7bfe77e18e944142f60dcd223c693c5ec2604118c6641d9a9d9edc45
```

第 1 次的可视证据位于
`runs/functional-01/probe/screenshots/overlay.png`，其中可见实际 Inspector
面板和青色 Collider 轮廓。截图只用于人工确认渲染；功能判定来自 typed
result/watch 数据，不依赖 OCR 或像素识别。

## 10 分钟性能门禁

正式 profile 使用产品默认 `InspectorSampleEveryMovieTicks=120`：

```text
performanceSeconds                    = 600
sampleCount                           = 1562
sample p50 / p95 / max                = 0.1566 / 0.1874 / 3.2621 ms
providerFailureCount                  = 0
exporterWritten / Dropped             = 781 / 0
requested / effective sample interval = 120 / 120
Inspector attributable average        = 959.115651432796 B/frame
Inspector attributable p95            = 0 B/frame
```

采样 p95 小于 1 ms，归因后的 steady-state managed allocation 小于
1 KB/frame，两个预算均通过。

目标 Unity 2020.2 Release Player 不提供可用的 per-frame `GC.Alloc`
recorder。因此分配门禁按 Spec 使用
`Profiler.GetMonoUsedSizeLong()`：分别在 Inspector-disabled baseline 与
enabled 的 10 秒窗口禁用自动 GC、逐帧记录 mono heap 增量，再计算 enabled
减 baseline；窗口结束和异常路径均恢复原 GC mode。600 秒主体使用正常 GC
模式。

## 修正记录

- 初始 parity 将旧 `WatchFrame` 与后续 Unity 更新点比较；改为同一
  `LateUpdate` 回调中的直接读数后，门禁才具备严格含义。
- 初始场景返回门使用了错误对象；修正为 `door_dreamReturn` 后，scene
  clear/rebind 由真实往返场景验证。
- 初始 5-tick 默认采样的归因分配约为 5440 B/frame，超出门禁；5 tick
  保留为功能压力 profile，产品默认改为 120 tick，正式结果为
  959.12 B/frame。外部低延迟读取由 T15 的按需通道负责。
- Unity 版本缺少目标 allocation recorder 后，没有把缺失指标当作通过；
  改用 Spec 明确记录的 baseline 差分方案重新执行完整 10 分钟门禁。

## 数据与槽安全

最终矩阵前后状态：

```text
gameProcesses = 0
Mods = 18 entries (17 directories + HkVoiceMod.zip)
VerificationModeRequested = false
replay-saves/v1 exists = false

slot1 = 413613FF631E479EAAF7E5E4B2026354478A0E9DC20C1AA7F50AA82055C8B389
slot2 = 1ACD6214B8DACAF112A2FBAE0E5E7AEB3D487C9365F5F955E4AEF8E51459DADE
slot3 = 9ABF27172D1003B607E543B88FD8759DCB0FD9668AD3D57A3A286C9BF4DB4855
slot4 = A6F09F2E7DE8F2B2CD25C8EB923F8F6728120646F97671A1296C00896186B062
```

## 证据完整性

```text
F0CA21551C719DD4DA9E7129C2474DA97DAB883C69478CC960DA64FDF13DC4C5  inspector-matrix.json
D9485206BCB1C9134A28317E0219E58313679C016892434B4BB942F75068E43F  verdict.md
3ABDA6607BEAD3D5E36665D24F366EF6A284ACD3BCE4834741ED87CA755A06BD  functional-01/probe/result.json
174C391D1919D649F730FEA36A3346E50647757A2E89725EA71D6809298A1F6B  functional-01/inspector-watches.jsonl
3298ECC78E77B81AF908404099B1575A6E687C5C20F72B384E8ACBE39D9CF572  functional-01/probe/screenshots/overlay.png
46DE942C460BDF8A4E60DF299FC4E0EB8824226B48DB4A64433CD475202AC1F4  performance-01/probe/result.json
6CFEC253A602B859378C7F3CF7455286446469CE42DB36906DDD5C6B5BB55BC2  performance-01/inspector-watches.jsonl
```

## 支持声明边界

- 仅验证本机锁定 HK `1.5.78.11833` / Modding API v77。
- v1 只观察显式注册对象，不做全场景每 tick 扫描。
- `DisplayKey` 不能提升为跨运行验证身份。
- Inspector 不修改 position、HP、FSM 或 RNG；外部受控写入属于 T15，
  并受独立权限、safe point、审计和最终验收限制。
