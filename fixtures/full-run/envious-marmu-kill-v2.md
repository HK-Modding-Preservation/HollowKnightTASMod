# 嫉妒的马尔穆：击杀 TAS

`envious-marmu-kill-v2.hktas` 是 3700 帧、固定 50 fps 的完整输入，覆盖启动、选择第 4 槽、神居换装、马尔穆战斗和胜利返回。按照用户要求保留首次成功版本；输入正文与 `artifacts/envious-marmu-kill/kill-candidate.hktas` 完全相同，仅文件头更新为实际安装的导出版本环境。

## 播放

使用本机已安装的 HollowKnightTAS 和 EnviousMarmu，以及本次第 4 槽神居基准。打开 Studio，使受保护游戏停在 Native frame 0；在 Input Editor 打开此 `.hktas`，点击 Play。无需手动换装或进场。序列结束时应回到神居马尔穆雕像旁。

基准 `user4.dat` SHA-256：`1acd6214b8dacaf112a2fbae0e5e7aeb3d487c9365f5f955e4aef8e51459dade`；`user4.modded.json`：`3cfac58f5a84d98369e05af7d050286ef48b3b4a4dab7edcd0a8c851c8f9f203`。其他槽位在选档菜单中的排列也应保持本机基准。不要把此输入当作兼容任意存档的通用脚本。

游戏版本 1.5.78.11833、Modding API 1.5.78.11833-77、800×450。实际加载的玩法 Mod 为 EnviousMarmu；TAS 仍允许安装其他 Mod，但不同环境不属于此序列的验证范围。回放使用影子存档，原始存档保持不变。

## 路线和战斗

- Movie 1480 打开护符界面，经正常菜单操作换装；1611 前完成。装备 ID `[36,32,25,33,19]`：虚空之心、快速劈砍、力量护符、法术扭曲者、萨满之石。实测使用 10/11 槽，未过载。
- 起身、跳跃、冲刺赶往雕像；2180 已进入 `GG_Ghost_Marmu`，巨型 208 HP，角色 9 血。
- 结合横劈、上劈、下劈、跳跃、冲刺和法术，依次击杀巨型、大型、中型与小型分裂体。没有改写血量、伤害、资源或 Boss 状态。
- 3215 还剩 1 只小型，3229 已全部死亡，角色剩 4 血；从 2180 入场观察点到全灭约 21 秒。此时间是检查点区间，非精确最后一击计时。
- 保留 Boss 原有胜利等待和返回流程，3700 在 `GG_Workshop` 完成。

全序列逻辑时长 74 秒，加载循环另计。MP4 从 Movie 1400 后开始，包含进存档、换装、赶路、战斗和返回；开头标题菜单仍保留在输入文件中。暂停读取状态和制作输入时的思考时间不会进入成片。

## 环境标识

- Movie SHA-256：`d6bc4774c7240ace4e90f58f0cc4a192f5111e6ef654f79e5103bee151178445`
- 不含文件头的输入正文 SHA-256：`c5ed529af867ef73ae227de7a337588aa1736a3e1264798af3d38ef87892cb32`
- Runtime：`4de4428f99a6666af6d62049def10e01213e3bcffb88d08af2827d922604e2e9`
- Core：`7454afb9863c8224437e922d2dee9541b81e0a401131d2bf82b7d4e12855febb`
- EnviousMarmu：`3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`
- 环境 manifest：`210d41d1d93adafa9daa7e5bc5b2c8f9b436b2b8cdc823aa685dbf5c7a137eb9`

两次独立冷启动（普通回放、导出回放）均执行 3700 帧并 `Completed`、fault/mismatch=0，退出前后原存档检查通过。21 个相同暂停检查点的角色、分裂体、FSM、自定义字段和碰撞几何语义一致。原始字段仍有已过期施法计时器及特定 `Voice Player` 音频引用解析差异，沿用此前已核对的精确语义规则，原值保留在报告中；不据此声称逐帧或 RNG 完全相同。

MP4 为 800×450、50 fps H.264 / 48 kHz 双声道 AAC，2661 帧、53.22 秒；画面抽查、完整解码和非静音检查通过。实机验证和媒体检查见 `artifacts/envious-marmu-kill/REPORT.md`。本文件和 Git 不包含用户存档。
