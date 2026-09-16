# HK-TAS Movie v1

HK-TAS Movie v1 是 HollowKnightTAS 的可审阅、可 diff、无代码执行能力的文本输入协议。它只描述动作样本和已注册的语义事件，不允许任意 C#、反射、文件、路径、进程或网络操作。

## 1. 编码与词法

- 文件必须是严格 UTF-8，不带 BOM；canonical newline 为 LF，文件以 LF 结束。
- 空行允许；非 quoted 区域的 `#` 开始行注释。
- token 由空白分隔。标识符只允许 ASCII `[A-Za-z0-9._-]+`。
- quoted string 使用双引号，允许 `\"`、`\\`、`\n`、`\r`、`\t`、`\uXXXX`；canonical writer 对其他控制字符使用小写四位 `\uXXXX`。
- 数字只使用 invariant decimal，不接受 `+`、指数、NaN 或 Infinity。
- 单行最大 65,536 个 UTF-16 code unit；整个 parser 输入最大 16 Mi code unit。

## 2. Header

六个 header 必须各出现一次，顺序在输入中可变，但 canonical 输出固定如下：

```text
hktas 1
game 1.5.78.11833
api 1.5.78.11833-77
manifest-sha256 <64-lowercase-hex>
baseline <baseline-id> <64-lowercase-hex|none>
tick-unit input
---
```

`baseline none none` 表示协议文件尚未绑定 baseline；否则 ID 与 SHA-256 必须同时存在。未知、重复、缺失 header 或缺少 `---` 都是错误。

`tick-unit input` 已由 T03 锁定为 `InControlCommittedTick`：每个展开后的 movie sample 消耗一次已提交的 InControl update tick。raw InControl tick 的绝对起点不属于 movie cursor；Unity visual/fixed tick 只作为 runtime ledger 坐标。

## 3. Commands

### 3.1 frames

```text
frames <positive-int64> hold=<comma-actions|->
frames <positive-int64> hold=<comma-nondirectional-actions|-> x=<-10000..10000> y=<-10000..10000>
```

动作及 canonical 顺序：

```text
left,right,up,down,jump,attack,dash,cast,quickcast,superdash,dreamnail
```

规则：

- `hold=` 必须存在一次；动作不能重复。
- `x=` 与 `y=` 必须同时出现。
- `left/right/up/down` 不能与 `x/y` 字段混用。
- 同一 run 不能同时包含 `left+right` 或 `up+down`。
- 默认最大展开长度为 10,000,000 input ticks；CLI 可显式降低或提高门限。

### 3.2 marker 与 checkpoint

```text
marker "<UTF-8 text>"
checkpoint <identifier>
```

marker 最大 1,024 UTF-8 bytes。checkpoint 使用 ASCII identifier。

### 3.3 assert

```text
assert <semantic-path> <operator> <canonical-value>
```

operator 为 `== != < <= > >=`。value 只能是：

- `true`、`false`、`null`；
- 无指数 canonical decimal：不允许前导零、`-0` 或无意义尾随零；
- quoted string。

semantic path 必须存在于调用方提供的版本化注册表。Core/CLI 的 v1 默认表当前包含：

```text
tick.input tick.visual tick.fixed
scene.name scene.epoch
hero.position.x hero.position.y
hero.velocity.x hero.velocity.y
hero.health hero.soul hero.accepting-input hero.dead hero.respawning
game.state hero.actorState
hero.cState.onGround hero.cState.jumping hero.cState.falling
hero.cState.dashing hero.cState.attacking hero.cState.wallSliding
player.health player.maxHealth player.mp
```

其中 Semantic Snapshot v1 的全部字段由同一份 `SemanticSnapshotSchemaV1`
注册表注入；`tick.*`、`scene.epoch` 与旧版诊断 alias 是 movie runtime 元数据。
未知 path 在 Runtime 启动前 fail closed。

## 4. Canonical form 与 Movie ID

Canonical writer：

1. 按固定顺序输出 header；
2. 按协议顺序输出 action；
3. 合并相邻且输入完全相同的 `frames`；
4. neutral `x=0 y=0` 归一化为无 axis 字段；
5. 使用固定 quoted escape、invariant decimal、LF 和最终 LF。

Movie ID 是 canonical UTF-8 bytes 的 lowercase SHA-256。同内容的注释、空白、header 顺序和可合并 RLE 差异不会改变 ID。

## 5. CLI

```powershell
dotnet run --project .\src\HollowKnightTAS.Cli -- movie validate <movie-path>
dotnet run --project .\src\HollowKnightTAS.Cli -- movie validate <movie-path> `
  --manifest-sha256 <hash> --baseline-sha256 <hash|none> `
  --max-expanded-ticks <positive-int>
dotnet run --project .\src\HollowKnightTAS.Cli -- movie format <movie-path> --check
dotnet run --project .\src\HollowKnightTAS.Cli -- movie format <movie-path>
dotnet run --project .\src\HollowKnightTAS.Cli -- movie inspect <movie-path>
```

- `validate`：解析、静态校验并输出 movie ID、展开 tick 和命令数。
- `format --check`：字节级检查 canonical UTF-8；非 canonical 返回 3。
- `format`：只把 canonical 文本写到 stdout，不原地覆盖源文件。
- `inspect`：输出稳定的 header、计数和 movie ID。

退出码：0 成功，2 CLI 用法错误，3 内容、编码、校验或 I/O 错误。

## 6. 诊断契约

每个内容诊断都携带：

```text
code, source, 1-based line, 1-based column, length, message, action
```

CLI 每条错误以 `INVALID` 开头并保持单行。v1 unknown header、command、field、action、semantic path 均拒绝，不忽略或降级。
