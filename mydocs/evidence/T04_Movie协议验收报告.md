# T04 Movie 协议、解析器与静态校验验收报告

- **Task**: T04
- **Gate**: Offline Protocol
- **Verdict**: PASS
- **Date**: 2026-07-28 CST

## 1. 实现范围

- `HK-TAS Movie v1` UTF-8/LF 文本协议与安全边界文档。
- Runtime-independent AST、1-based source span、结构化 diagnostic。
- 有大小/行长上限的 lexer/parser；unknown header/command/field/action fail closed。
- Header、hash、baseline、tick unit、方向、axis、marker、semantic path、assert 与展开长度 validator。
- 固定 header/action/escape/number/LF 的 canonical writer，相邻相同 input RLE 合并。
- canonical UTF-8 SHA-256 Movie ID。
- CLI：
  - `movie validate`
  - `movie format [--check]`
  - `movie inspect`
- valid/invalid/golden fixtures 与 parser/validator/CLI/随机畸形输入测试。

## 2. 构建与测试

| 检查 | 结果 |
|---|---|
| `dotnet build .\HollowKnightTAS.sln -c Debug` | PASS，4 个项目，0 warning，0 error |
| `dotnet test ... --filter "FullyQualifiedName~Movie"` | PASS，24/24 |
| 全量 Core/CLI/Input/Ledger/Movie tests | PASS，49/49 |
| `git diff --check` | PASS，无 whitespace error |

Movie 测试覆盖：

- valid minimal/actions fixtures；
- 11 类 invalid fixture 的首错 code/line/column/action；
- `parse -> canonical write -> parse` 连续 100 次字节与 ID 稳定；
- 注释、空白、header 顺序、可合并 RLE 不改变 Movie ID；
- locale 与平台 newline 不影响 canonical bytes；
- manifest、baseline 与 expanded tick limit 在 Runtime 前拒绝；
- UTF-8 byte marker 上限；
- 2,000 个固定种子的随机畸形输入无未处理异常或 hang；
- UTF-8 BOM、unknown command 和 noncanonical `--check` 的 CLI 失败路径。

## 3. CLI 验收

```text
movie validate fixtures/movie/valid/minimal-v1.hktas
VALID c793c2ec62cdb93a738da9d535f825a128c798b5164979375a401297b47e8dea ticks=1 commands=1
```

```text
movie format fixtures/movie/valid/actions-v1.hktas --check
FORMATTED 453889fc8b9ee0b53d9b9a849cf9dd03ae2571e17a8afcebd1652b353aa45114
```

`movie inspect` 对 actions fixture 稳定输出：

```text
input-ticks=25
commands=7
markers=1
checkpoints=1
asserts=2
```

拒绝样例：

| 场景 | 结果 |
|---|---|
| unknown `script` command | exit 3，`HKTAS120`，line 8 column 1 |
| expected manifest 不匹配 | exit 3，`HKTAS220`，line 4 column 17 |
| UTF-8 BOM | exit 3，单行 `INVALID` |
| noncanonical CRLF/comment source 的 `format --check` | exit 3，`NOT_FORMATTED` |

## 4. Invalid fixture 首错矩阵

| Fixture | Code | Line:Column |
|---|---|---|
| `duplicate-header.hktas` | `HKTAS111` | `3:1` |
| `unknown-command.hktas` | `HKTAS120` | `8:1` |
| `direction-conflict.hktas` | `HKTAS211` | `8:1` |
| `axis-direction-mix.hktas` | `HKTAS213` | `8:1` |
| `unknown-semantic-path.hktas` | `HKTAS215` | `8:1` |
| `invalid-hash.hktas` | `HKTAS202` | `4:17` |
| `unterminated-marker.hktas` | `HKTAS104` | `8:8` |
| `unknown-header.hktas` | `HKTAS110` | `2:1` |
| `missing-header.hktas` | `HKTAS112` | `1:1` |
| `unknown-action.hktas` | `HKTAS123` | `8:15` |
| `duplicate-field.hktas` | `HKTAS122` | `8:17` |

## 5. Canonical 与安全结论

- Canonical writer 只输出定义过的四种 command，不执行输入内容。
- v1 没有 C#、反射、任意文件/路径、进程或网络语法。
- parser 上限为 16 Mi UTF-16 code unit、单行 65,536；CLI 在分配前检查最多 64 MiB 文件并在 decode 前检查字符数。
- marker 最大 1,024 UTF-8 bytes。
- assert 只接受注册 semantic path、固定 operator 与 canonical scalar/string。
- `tick-unit input` 固定表示 T03 的 `InControlCommittedTick`；visual/fixed 不消费 movie sample。

## 6. 产物哈希

| 产物 | SHA-256 |
|---|---|
| `docs/protocol/HK-TAS-Movie-v1.md` | `05162937d3f39dd49fb9822f7f02db00352abe1aab8a9faa0e1f0ffa6f1f3292` |
| `fixtures/movie/valid/minimal-v1.hktas` / Movie ID | `c793c2ec62cdb93a738da9d535f825a128c798b5164979375a401297b47e8dea` |
| `fixtures/movie/valid/actions-v1.hktas` / Movie ID | `453889fc8b9ee0b53d9b9a849cf9dd03ae2571e17a8afcebd1652b353aa45114` |
| `fixtures/movie/golden/actions-v1.canonical.hktas` | `453889fc8b9ee0b53d9b9a849cf9dd03ae2571e17a8afcebd1652b353aa45114` |
| 当前安装包 | `97af87eb0236a21cb1234c72bc127eacd9b84ab50c1efde77b898107b1efa194` |

安装包 hash 与 `SHA256.txt` 一致。

## 7. 门禁结论

**T04 Offline Protocol PASS。** v1 movie 可以在不启动 Hollow Knight 的情况下完成严格解析、静态拒绝、canonicalize、Movie ID、inspect 与 round-trip。

本任务不包含 Runtime 回放；下一步 T05 建立语义状态 snapshot/hash，之后 T06 才消费本协议执行 record/replay。
