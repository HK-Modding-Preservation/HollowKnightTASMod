# TAS 辅助 Mod 兼容清单

适用于当前 HollowKnightTAS；版本以 2026-09-28 的 ModLinks 为准。修改设置后重启游戏。

## 不能装（已安装的禁用）

| Mod | 版本 |
| --- | --- |
| QoL | 4.9.0.0 |

## 需要关闭部分功能

### Custom Knight 3.5.0.0
- 关闭 `GenerateDefaultSkin`、`SwapperEnabled`、`EnableParticleSwap`、`EnablePauseMenu`、`EnableSaveHuds`。
- 开启 `DisableDirectorySwaps`。
- 录制前选好固定皮肤；TAS 会自动屏蔽换皮和重载快捷键，无需清空绑定。

### DebugMod 1.4.10.2

- 关闭变速、暂停、逐帧、传送、读状态、无敌、穿墙、无限资源和伤害修改。
- 清空热键：`binds={}`，并设置 `FirstRun=false`。
- 关闭 `NumPadForSaveStates`、`TopMenuVisible`、`SaveStatePanelVisible`、`EnemiesPanelVisible`、`ConsoleVisible`。
- 信息面板、碰撞箱可以保留。

### GodSeekerPlus 0.25.0.0

- 关闭 `ColosseumOfFools`、`CreateLag`、`AggressiveGC`。
- 关闭 `ActivateFury`、`AddLifeblood`、`AddSoul`、`CarefreeMelodyReset`、`P5Health`、所有 `HalveDamage*`。
- 关闭 `InfiniteChallenge`、`InfiniteGrimmPufferfish`、`InfiniteRadianceClimbing`、`restartFightOnSuccess`、`restartFightAndMusic`。
- 关闭 `NoDiveInvincibility`、`NoNailAttack`、`NoNailDamage`、`NoSpellDamage`。

### Benchwarp 3.2.6.1
- 关闭 `ShowMenu`、`EnableDeploy`、`DoorWarp`、`EnableHotkeys`、`LegacyHotkeys`、`UnlockAllBenches`、`ModifyVanillaBenchStyles`。
- 录制起点不能保留部署椅子重生状态；先回真实椅子存档，无法确认时禁用整个 Mod。

### SpeedRunQoL 0.6.1.0

- 按上面的 DebugMod 配置关闭功能并清空全部热键。
- 不启用房间/位置/复制状态加载、斗兽场波次跳转、碰撞修改、梦门无敌、遮幕隐藏和视觉状态切换。

以上为已测片段使用配置，不代表全部场景均已验证。