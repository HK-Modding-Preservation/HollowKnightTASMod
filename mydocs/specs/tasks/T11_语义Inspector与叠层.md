# Task Spec T11: 语义 Inspector 与叠层

- **Status**: 原功能保留历史 VERIFIED；导出容量与完整性改动已通过定向检查，尚未安装验证。
- **Gate**: Productivity / Diagnostics
- **Depends On**: T03, T05, T07
- **Produces**: 只读 watch registry、游戏内 overlay、JSONL export、性能报告

## 0. Open Questions

- None。v1 观察器只读；任何状态修改器必须另立任务。

## 1. Requirements

### Goal

提供 Hollow Knight 专用的 Hero、scene、FSM、敌人 HP、Collider、tick/T-FT/RNG 状态观察器，并能把同一份 typed watch 数据用于 overlay、导出和 desync 报告。

### In-Scope

- Watch registry、typed values、采样频率、分组与 capability。
- Hero/scene/tick 默认面板。
- 显式注册的 PlayMaker FSM、HealthManager、Rigidbody2D、Collider2D。
- Collider outline 与文本 overlay。
- JSONL export、性能/分配预算。

### Out-of-Scope

- 任意字段反射浏览器。
- 修改 FSM/HP/position。
- 将临时 display ID 自动提升为 verification key。
- 全场景每 tick 扫描所有 GameObject。

## 1.5 Code Map

```text
src/HollowKnightTAS.Core/Inspector/
  WatchKey.cs
  WatchValue.cs
  WatchDescriptor.cs
  WatchFrame.cs
  WatchRegistry.cs
src/HollowKnightTAS.Runtime/Inspector/
  RuntimeInspector.cs
  HeroWatchProvider.cs
  TickWatchProvider.cs
  FsmWatchProvider.cs
  EnemyWatchProvider.cs
  ColliderWatchProvider.cs
  OverlayRenderer.cs
  ColliderOverlayRenderer.cs
  WatchJsonLinesExporter.cs
tests/HollowKnightTAS.Core.Tests/Inspector/
artifacts/inspector/<sessionId>/
  watches.jsonl
  performance.json
  screenshots/
```

## 2. Architecture

- 所有 watch 通过 registry 显式声明，不允许 movie 提供任意反射路径。
- `VerificationKey` 必须具备跨 run 稳定身份；`DisplayKey` 只用于当前 session。
- 静态对象优先使用 `scene + hierarchy path + component type + component ordinal`。
- 动态对象只有在明确注册的 spawner/adapter 能给出稳定语义 ID 时才能用于验证；否则标记 `display-only`。
- 默认不做 `FindObjectsOfType` 每 tick；scene change 时发现，运行中增量注册/注销。
- Overlay 与 exporter 消费同一个 immutable `WatchFrame`。
- Watch JSONL 单文件最多 64 MiB，并与 Runtime 事件日志共享 sessions 根的 1 GiB 预算。已有日志计入预算但不自动删除；容量耗尽停止导出，不影响按需观察。
- 导出写入期间保留 `.incomplete`；仅无丢失且正常关闭后移除。丢失、失败或未完成的文件不能用于完整性验收；既有同名文件不覆盖。
- 默认完整面板每 `120` movie tick 采样一次并每两帧导出一次；功能压力
  profile 使用 `5` tick。T15 的外部按需 snapshot/step 是独立低延迟通道，
  不以提高默认全量 Inspector 频率换取响应性。

## 3. Detailed Design

```csharp
public readonly struct WatchKey : IEquatable<WatchKey>
{
    public string Value { get; }
    public bool IsVerificationStable { get; }
}

public sealed class WatchDescriptor
{
    public WatchKey Key { get; }
    public SemanticValueKind ValueKind { get; }
    public string Group { get; }
    public int SampleEveryMovieTicks { get; }
}

public interface IWatchProvider
{
    string ProviderId { get; }
    IEnumerable<WatchDescriptor> Describe();
    void Sample(WatchFrameBuilder builder, TickStamp stamp);
}

public sealed class WatchRegistry
{
    public void Register(IWatchProvider provider);
    public bool Unregister(string providerId);
    public WatchFrame Sample(TickStamp stamp);
}
```

### Default Watches

- `tick.input/visual/fixed/movie/tft`。
- `scene.name/epoch`。
- `hero.position/velocity/actorState/cState/health/mp`。
- 显式 FSM：stable key、`ActiveStateName`、最近 event。
- 显式 enemy：HP、position、velocity、dead。
- 显式 collider：enabled、trigger、bounds/shape 摘要。
- T10 enabled 时的 RNG state/call count。

## 4. Independent Verification

### Functional

- 一个固定 gameplay scene 注册 Hero、一个 FSM、一个 enemy、一个 collider。
- 切换 overlay group、暂停 export、scene change 后注销/重建。
- 在 registry 完成采样的同一 LateUpdate 回调内对照直接组件读数逐字段核对，
  不拿旧 WatchFrame 与后续 Unity 更新点做伪精确比较。

### Stability

- 同一静态对象跨 10 次 load 的 VerificationKey 相同。
- 动态对象若无稳定 adapter，必须显示 `display-only` 且不进入 T05 hash。
- scene unload 后无 stale Unity object access。

### Performance

- 默认面板在目标机器运行 10 分钟。
- warm-up 后默认 watch 采样 p95 小于 1 ms/frame。
- steady-state managed allocation 默认小于 1 KB/frame。
- Unity 2020.2 Release Player 不提供 per-frame GC allocation recorder；验收在
  短窗口禁用自动 GC，以 `Profiler.GetMonoUsedSizeLong()` 逐帧增量对照
  Inspector-disabled baseline，窗口结束或异常时恢复原 GC mode。10 分钟主体
  使用正常 GC mode。
- 队列压力导致降采样时生成事件，不静默丢失。

### PASS

- typed overlay 与 JSONL 同源且值一致。
- 身份稳定性规则被执行。
- 无全场景每 tick 扫描。
- 性能预算通过；关闭 Inspector 后不再采样/绘制。
- 只读保证通过代码审查与测试。

### FAIL

- 使用 Unity instance ID 作为跨 run 主键。
- 读取失败填默认值。
- Inspector 修改 gameplay 状态。
- 性能超限但仍默认每 tick全量采样。

## 5. Implementation Checklist

- [x] 实现 Core watch types/registry。
- [x] 实现 Hero/Tick/FSM/Enemy/Collider providers。
- [x] 实现 overlay、collider drawing 和 JSONL exporter。
- [x] 实现 scene lifecycle 注册/清理。
- [x] 添加 identity、type、sampling tests。
- [x] 完成功能、稳定性和 10 分钟性能测试。
- [x] 将稳定 watch keys 按版本显式注册给 T04/T05。

## 6. Rollback

- Inspector disable 必须取消 provider 与 scene subscriptions。
- Overlay GameObject/材质在 scene change 和 shutdown 销毁，不进入 baseline。
