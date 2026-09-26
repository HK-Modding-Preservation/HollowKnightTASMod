# 嫉妒的马尔穆：辐辉无伤 TAS

`envious-marmu-radiant-v2.hktas` 是 3800 帧、固定 50 fps 的完整输入，从启动、选择第 4 槽、神居换装开始，选择辐辉难度，击杀所有分裂体并返回神居。保留首次成功击杀的完整输入，未再压缩等待或改动 RNG 路线。

## 播放条件

使用本机已安装的 HollowKnightTAS 与 EnviousMarmu，以及本次第 4 槽神居存档。打开 Studio，使受保护游戏停在 Native frame 0；在 Input Editor 打开此 `.hktas`，点击 Play。换装、赶路、难度选择和战斗均由序列完成。

基准 `user4.dat` SHA-256：`1acd6214b8dacaf112a2fbae0e5e7aeb3d487c9365f5f955e4aef8e51459dade`；`user4.modded.json`：`3cfac58f5a84d98369e05af7d050286ef48b3b4a4dab7edcd0a8c851c8f9f203`。第 4 槽已解锁马尔穆辐辉；其他槽位在选档菜单中的排列也应保持本机基准。存档不随交付包分发。

游戏版本 1.5.78.11833、Modding API 1.5.78.11833-77，800×450。已验证环境的玩法 Mod 为 EnviousMarmu；TAS 允许用户安装其他 Mod，但其他存档或 Mod 组合不属于此序列的验证范围。运行使用影子存档。

## 护符和打法

- 虚空之心、快速劈砍、力量护符、法术扭曲者、萨满之石、荣耀印记，ID `[36,32,25,33,19,13]`。通过正常护符菜单装备，实测 13/11 槽过载；辐辉本身受击即死，选择过载换取骨钉范围。
- Movie 1682 前完成换装，随后跳跃、冲刺到马尔穆雕像并选择辐辉；2253 已进入 `GG_Ghost_Marmu_V`。控制器 `bossLevel=2`，巨型 300 HP；大型、中型、小型分别为 150、75、37 HP。
- 用横劈、上劈和下劈积攒灵魂，黑冲穿过追击者，黑波处理大型，上吼集中清除分裂体。新生分裂体有短暂无敌期，等待其可受伤后攻击。全程只用正常输入，没有改写血量、资源、伤害或 Boss 状态。
- 2641 已击杀巨型；2945 已消灭两只大型；3252 只剩最后两只小型；3336 剩一只 5 HP 小型；3354 已全部消灭，角色仍为 9 血。2253 到 3354 检查点间约 22.02 秒，此值不是逐帧定位最后一击的计时。
- 3800 返回 `GG_Workshop`，角色 9 血。全序列逻辑时长 76 秒，加载循环另计。

## 环境

- Movie SHA-256：`8b9db132c369967775d119e260d3f0138eada4b7d5bf80e3ada6687d141b83fc`
- 输入正文 SHA-256：`463a9d8d61b84f706ee773d0d59242ccecc35cbf8d0daa34af8fa761819031e2`
- Runtime：`4de4428f99a6666af6d62049def10e01213e3bcffb88d08af2827d922604e2e9`
- Core：`7454afb9863c8224437e922d2dee9541b81e0a401131d2bf82b7d4e12855febb`
- EnviousMarmu：`3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`
- 环境 manifest：`210d41d1d93adafa9daa7e5bc5b2c8f9b436b2b8cdc823aa685dbf5c7a137eb9`

## 验证和视频

两次独立冷启动（普通回放、导出回放）均执行完整 3800 帧，`Completed`、fault/mismatch=0，均确认 `bossLevel=2`、全灭时 9 血并返回神居。22 个相同暂停检查点的角色、敌人、FSM、自定义分裂组件及碰撞几何语义一致；沿用既有的已过期施法计时器和特定音频对象引用归一化，保留原始差异，不声称逐帧 RNG 或全部未选字段一致。

MP4 从 Movie 1400 后开始，包含进入存档、换装、赶路、战斗和返回；2781 帧、55.62 秒、800×450/50 fps H.264 与 48 kHz 双声道 AAC。暂停观察和编写输入的现实等待不进入视频。画面抽查、完整音视频解码及非静音检查通过，31 个原始 `user*` 文件保持不变。

交付包：`artifacts/envious-marmu-radiant/envious-marmu-radiant-delivery.zip`；视频、序列和复现说明位于其 `delivery/` 目录，详细证据见 `REPORT.md`、`replay-comparison.json`、`audit.json`。
