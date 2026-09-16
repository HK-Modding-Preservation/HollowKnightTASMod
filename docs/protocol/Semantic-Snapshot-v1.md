# Semantic Snapshot v1

Semantic Snapshot v1 是 HollowKnightTAS 的严格、可复现状态视图。它只包含已注册的语义字段，不序列化 Unity 对象、instance ID、对象枚举顺序或本地化文本。

## 1. Schema

Schema version 为 `1`，全部 16 个字段都是必需且非 null：

| Key | Type | Runtime source |
|---|---|---|
| `scene.name` | `Utf8String` | active Unity scene name |
| `game.state` | `Utf8String` | `GameManager.gameState` enum name |
| `hero.position.x/y` | `Float32Bits` | Hero Transform world position |
| `hero.velocity.x/y` | `Float32Bits` | Hero `Rigidbody2D.velocity` |
| `hero.actorState` | `Utf8String` | `HeroController.hero_state` enum name |
| `hero.cState.onGround` | `Boolean` | Hero cState |
| `hero.cState.jumping` | `Boolean` | Hero cState |
| `hero.cState.falling` | `Boolean` | Hero cState |
| `hero.cState.dashing` | `Boolean` | Hero cState |
| `hero.cState.attacking` | `Boolean` | Hero cState |
| `hero.cState.wallSliding` | `Boolean` | Hero cState |
| `player.health` | `Int32` | `PlayerData.health` |
| `player.maxHealth` | `Int32` | `PlayerData.maxHealth` |
| `player.mp` | `Int32` | `PlayerData.MPCharge` |

未知、重复、缺失或类型不符的 key 都必须拒绝。采集失败必须返回 error，不能填 `0`、空串或旧值。

## 2. Value encoding

类型 tag 固定为：

```text
1 Boolean
2 Int32
3 Int64
4 Float32Bits
5 Utf8String
```

- Boolean 只允许单字节 `00` 或 `01`。
- Int32/Int64 使用 two's-complement big-endian。
- Float32Bits 使用 IEEE 754 single 的原始 32 bit big-endian，不规范化 `-0` 或 NaN payload。
- Utf8String 使用严格 UTF-8，不带 BOM。

## 3. Canonical byte stream

所有整数和长度均为 big-endian signed Int32，长度不得为负：

```text
4 bytes  magic "HKSS"
4 bytes  schema version
4 bytes  entry count
repeat entry count times in StringComparer.Ordinal key order:
  4 bytes  UTF-8 key byte length
  N bytes  UTF-8 key
  1 byte   type tag
  4 bytes  value byte length
  M bytes  canonical value
```

v1 canonical stream 最大 1 MiB，必须恰好消费到 EOF。哈希是完整 canonical byte stream 的 lowercase SHA-256。

## 4. Equality and diff

严格相等要求 schema、key、type 和 value bytes 全部相同。`+0/-0`、不同 NaN payload、任意一个浮点 bit 的变化均不相等。诊断 diff 按 key 的 Ordinal 顺序报告，并包含双方 type、canonical hex 和 invariant round-trip display。

## 5. Capture phase

Runtime milestone snapshot 在 T03 的 `LateUpdateEnd` 稳定相位采集。每个采集结果同时记录完整 `TickStamp`；只有 phase 相同的 milestone hash 才能直接比较。scene epoch 是采集元数据，不进入 v1 快照字段。
