# Task Spec T04: Movie 协议、解析器与静态校验

- **Status**: VERIFIED
- **Gate**: Offline Protocol
- **Depends On**: T01, T03
- **Produces**: `HK-TAS Movie v1` 规范、无 Unity 依赖的 parser/validator/CLI

## 0. Open Questions

- None。v1 只接受白名单命令，不支持任意 C#、反射、文件、进程或网络指令。

## 1. Requirements

### Goal

定义一个可 diff、可审阅、可静态拒绝错误输入的文本 movie 格式，并在不启动 Hollow Knight 的情况下完成解析、canonicalize、验证和 round-trip。

### In-Scope

- Header、RLE input、marker、checkpoint、assert。
- UTF-8 文本、稳定 canonical 输出、行列错误定位。
- 版本、manifest、baseline、action、数值范围和命令白名单校验。
- CLI：`movie validate`、`movie format`、`movie inspect`。

### Out-of-Scope

- runtime 回放。
- camera/capture/脚本命令；这些在后续协议版本另立 Spec。
- 隐式读取当前游戏环境来修补缺失 header。

## 1.5 Code Map

```text
docs/protocol/HK-TAS-Movie-v1.md
src/HollowKnightTAS.Core/Movie/
  MovieDocument.cs
  MovieHeader.cs
  MovieSourceSpan.cs
  MovieDiagnostic.cs
  MovieCommand.cs
  FrameRunCommand.cs
  MarkerCommand.cs
  CheckpointCommand.cs
  AssertCommand.cs
  MovieProtocolV1.cs
  MovieParser.cs
  MovieValidator.cs
  MovieCanonicalWriter.cs
src/HollowKnightTAS.Cli/Commands/
  MovieValidateCommand.cs
  MovieFormatCommand.cs
  MovieInspectCommand.cs
  MovieCommandUtilities.cs
tests/HollowKnightTAS.Core.Tests/Movie/
fixtures/movie/
  valid/minimal-v1.hktas
  valid/actions-v1.hktas
  invalid/
  golden/
```

## 2. Protocol

### 2.1 Encoding

- UTF-8 without BOM，canonical newline 为 LF。
- 空行允许；`#` 从非 quoted 区域开始到行尾为注释。
- 标识符使用 ASCII `[A-Za-z0-9._-]+`。
- 数字使用 invariant culture，不允许指数、NaN、Infinity。

### 2.2 Header

```text
hktas 1
game 1.5.78.11833
api 1.5.78.11833-77
manifest-sha256 <64 lowercase hex>
baseline <baseline-id> <64 lowercase hex|none>
tick-unit input
---
```

所有 header key 恰好出现一次；未知 key、重复 key、缺失 key均为错误。

`tick-unit input` 的 v1 语义已由 T03 锁定：一个展开后的 `frames` sample 消耗一个已提交的 InControl update tick（`InControlCommittedTick`）。它不表示 Unity visual tick 或 fixed tick；visual/fixed 只作为运行时账本坐标，不改变 movie 的展开长度。

### 2.3 Commands

```text
frames <positive-int> hold=<comma-actions|->
frames <positive-int> hold=<comma-nondirectional-actions|-> x=<-10000..10000> y=<-10000..10000>
marker "<utf8 text>"
checkpoint <identifier>
assert <semantic-path> <operator> <canonical-value>
```

Actions：

`left,right,up,down,jump,attack,dash,cast,quickcast,superdash,dreamnail`

Operators：

`== != < <= > >=`

Rules：

- `left/right/up/down` 与 `x/y` 不能混用。
- 同一行不能同时包含 `left+right` 或 `up+down`。
- `frames` 总展开长度必须在配置上限内，默认 `10,000,000` 个 committed InControl input ticks。
- marker 文本最大 1,024 UTF-8 bytes。
- semantic path 必须在注册表中；unknown path 在加载前拒绝。
- v1 unknown command 必须报错，不能忽略。

### 2.4 Canonical Form

- Header 固定顺序。
- Action 按协议枚举顺序输出。
- 连续完全相同的 `frames` 合并。
- quoted text 使用固定 escape。
- canonical document 的 SHA-256 作为 movie ID。

## 3. Detailed Design

```csharp
public sealed class MovieParser
{
    public MovieParseResult Parse(TextReader reader, string sourceName);
}

public sealed class MovieValidator
{
    public MovieValidationReport Validate(
        MovieDocument movie,
        MovieValidationContext context);
}

public sealed class MovieCanonicalWriter
{
    public void Write(MovieDocument movie, TextWriter writer);
    public string ComputeMovieId(MovieDocument movie);
}

public sealed class MovieValidationContext
{
    public long MaxExpandedTicks { get; }
    public ISet<string> AllowedSemanticPaths { get; }
    public string ExpectedManifestSha256 { get; }
    public string ExpectedBaselineSha256 { get; }
}
```

错误对象必须包含：code、source、1-based line、1-based column、长度、可操作说明。Parser 不抛出普通格式异常到 CLI 顶层。

## 4. Independent Verification

### Commands

```powershell
dotnet test .\tests\HollowKnightTAS.Core.Tests -c Debug --filter Movie
dotnet run --project .\src\HollowKnightTAS.Cli -- movie validate .\fixtures\movie\valid\minimal-v1.hktas
dotnet run --project .\src\HollowKnightTAS.Cli -- movie format .\fixtures\movie\valid\actions-v1.hktas --check
```

### PASS

- valid fixtures 全通过，invalid fixtures 各自命中预期错误 code/line/column。
- `parse -> canonical write -> parse` 结构等价。
- canonical 输出重复 100 次字节完全一致。
- 同内容不同注释/空白得到相同 movie ID。
- manifest/baseline 不匹配在 runtime 启动前即可被 CLI 拒绝。
- fuzz/property test 不导致 hang、OOM 或未处理异常。

### FAIL

- parser 自动容忍 unknown command 或不匹配 manifest。
- canonical 输出依赖 locale、字典迭代顺序或平台 newline。
- 格式允许任意代码/路径/网络副作用。

## 5. Implementation Checklist

- [x] 写 `docs/protocol/HK-TAS-Movie-v1.md`。
- [x] 实现 AST、parser、validator、canonical writer。
- [x] 实现 CLI 三个子命令。
- [x] 添加 valid/invalid/golden fixtures。
- [x] 添加 round-trip、canonical、limit、fuzz/property tests。
- [x] 在 T03 通过后锁定 `tick-unit input = InControlCommittedTick`；raw InControl tick 的绝对起点不写入或改变 movie cursor。

## 5.5 Verification Record

- 验收报告：[`mydocs/evidence/T04_Movie协议验收报告.md`](../../evidence/T04_Movie协议验收报告.md)。
- `dotnet build .\HollowKnightTAS.sln -c Debug`：0 warning / 0 error。
- Movie tests：24/24；全量 tests：49/49。
- Minimal Movie ID：`c793c2ec62cdb93a738da9d535f825a128c798b5164979375a401297b47e8dea`。
- Actions/Golden Movie ID：`453889fc8b9ee0b53d9b9a849cf9dd03ae2571e17a8afcebd1652b353aa45114`。
- Invalid 首错矩阵：11/11 命中预期 code/line/column。

## 6. Compatibility

- v1 一经用于验证 movie，不原地改变语义。
- 新 action 或命令需要提升协议版本或能力 flag。
- Runtime 对更高版本必须 fail closed。
