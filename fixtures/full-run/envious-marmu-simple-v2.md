# 嫉妒马尔穆：简单攻击 TAS

文件：`envious-marmu-simple-v2.hktas`。以击杀为目标的一次简单尝试，未回档微调战斗输入；最终角色阵亡，Boss 未完全击败。

## 播放

从 Studio 的 Native frame 0 打开此文件并播放。使用本机第4槽的神居存档及当前护符配置，经过标题菜单、神居椅子和马尔穆雕像进入战斗。需安装 EnviousMarmu；其他环境和起始存档不在本次回放验证范围内。

总长10150个Movie帧，50 FPS，逻辑时长203秒。前8250帧为启动、等待和进入战斗；8250–10110连续攻击，最后40帧释放按键。游戏加载循环另计，不消耗Movie帧。

攻击采用15轮固定循环：每轮8次上劈（1帧 up+attack、12帧 up），然后1帧 up+quickCast、19帧 up，尝试有灵魂时使用上吼。保持原有护符与状态，没有改写血量、伤害、Boss FSM 或其他游戏状态。

## 实际结果

- 巨型马尔穆从208 HP降至死亡，产生大型分裂体，之后打出中型和小型分裂体。
- 最后检查点Movie10149：角色0 HP；仍有2只中型（20、52 HP）和1只小型（26 HP），Manager存活数3、未结束。
- 第一遍实跑后只更新文件头为实测环境信息，所有输入保持不变。冻结文件后进行两次独立冷启动回放，其中一次开启碰撞箱。
- 两次均完整执行10150帧，fault/mismatch=0；暂停查询和碰撞箱显示/隐藏不推进原生帧。20个暂停检查点的角色/敌人状态、FSM变量、动态分裂组件和碰撞几何语义比较全部通过，不是逐帧RNG证明。
- 原始字段并非完全一致：已过期的 `preventCastByDialogueEndTimer` 负值，以及指定 `Control → Voice Player` 音频池引用的场景解析状态有差异。比较器仅对这两条经源码核对的语义作精确归一化，原值和差异全部保留在 `replay-comparison-semantic.json`；没有将其他差异忽略。比较器17/17自测通过。

旧假骑士专用 `bossDeathObserved` / `boss-trace.csv` 不用于判断马尔穆胜负；本次结果来自正式世界/对象详情接口。

## 文件与环境

- Movie SHA-256：`89488a97cbb68f0d72b72ff4a9e09202215de2f629f8aa380586a4d1b8900d15`
- Runtime：`7613b7690be7cd80a44dc246f25081529ade51dc0aed3d52092e62ef98b9b5b5`
- Core：`644d153189f2d9b15be76660403f65f58762ffde867e46f33be331750baff551`
- EnviousMarmu：`3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673`
- 环境 manifest：`e5dcf841f3bf7d0887e90727b9cbcf7c6d863703c790368f69252e1765b437b7`

详细运行与比较证据位于 `artifacts/envious-marmu-simple/REPORT.md`。不包含或分发用户存档。
