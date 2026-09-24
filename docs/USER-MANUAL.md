# HollowKnightTAS Studio 操作手册

Studio 用来在《空洞骑士》中逐帧播放、编辑和回退 TAS 输入。直接启动游戏时，Mod 会自动打开 Studio，并把受控游戏停在 `Startup frame: 0`；如果先打开 Studio，也可以点击 `Start 启动` 拉起游戏。

## 开始前

`Native frame: 0` 表示游戏的第一个原生 PlayerLoop 尚未执行。全流程 v2 Movie 在此时通过 `Movie → New full-run Movie 从第 0 帧新建` 或 `Movie → Open Movie 打开…` 预置，之后才能逐帧或播放。游戏加载期间的原生循环不消耗 Movie 帧；Movie 第 0 帧是第一个可接受真实游戏输入的循环。`Frame Advance` 会穿过加载，直到完成一个 Movie 帧才停下。

新建前可在 `Settings → Enable game mouse for new v2 Movie 启用游戏鼠标` 设置游戏鼠标；默认关闭，标题菜单仍可用键盘操作。打开已有 v2 Movie 时使用文件记录的鼠标模式。开始后模式固定，Studio 自身的鼠标不受影响。

直接启动时，最初的标题进程会在进入存档前退出，Studio 随后启动受控游戏。这个短暂的重新启动是首帧暂停流程的一部分。

v2 会话中的标题菜单、选槽、局内操作以及 Save&Load 都走真实游戏输入。游戏对原始存档只读；保存、删档和建档作用于受保护影子副本，不自动写回原件。打开 Movie 前请确保所用槽位存档由你自己对齐。原生门闩和 Runtime 会在启动过程中接管，无须手动预选存档。

顶部四个控件是最常用的运行控制：

- `Play / Pause`：继续或暂停，默认快捷键为 `Pause`。
- `Frame Advance`：暂停时前进一帧，默认快捷键为 `V`。
- `Stop`：停止正在播放的序列。
- `Quit Game 退出游戏`：关闭受控游戏。全流程播放中会先停在原生帧边界，再请求退出；回放完成后也可直接点击。启动第 0 帧还未加载 Movie 时同样可用。需要保留录制内容时，先停止并保存 Movie。关闭 Studio 请另用 `File → Exit 关闭 Studio`。
- `Frame`：当前 TAS 输入帧。

快捷键只在 Studio 窗口获得焦点时生效。可在 `Settings → Hotkeys` 修改播放和逐帧按键。

## 从启动第 0 帧播放或录制 v2 Movie

1. 从 Steam 启动游戏，等待 Studio 自动打开并显示 `Native frame: 0`。
2. 回放时用 `Movie → Open Movie 打开…` 选择 v2 `.hktas`；录制时先选游戏鼠标模式，再用 `Movie → New full-run Movie 从第 0 帧新建`。
3. 点击 `Frame Advance` 或 `Play`。Movie 的真实输入会操作标题菜单并选择存档；场景加载以及确认保存退出后的保存、淡出过程只影响等待时间，不移动 Movie 输入位置。暂停时可查看当前 Movie 帧和原生帧。
4. 录制完成后停止并通过 `Movie → Save Movie 保存…` 保存。回放结束后核对 `Completed`、输入偏差数和游戏终态。

已验证的假骑士示范在 `fixtures/full-run/false-knight-startup-v2.hktas`。按上述步骤打开即可由序列在标题菜单选择第 4 槽，进入神居假骑士战斗并返回神居；不需要先手动进入存档。该文件有 10892 个 Movie 帧，需使用含 `load-elision-scene-rng-2026-v3` profile 的当前 Mod。游戏加载变慢时，Studio 的原生帧数会增加，Movie 帧位置保持不变。

v2 Movie 不绑定某一个槽位或存档哈希；若存档内容不同，输入仍会执行，但结果可能不同。v2 的协议、动作表及原生加载省帧／随机同步 profile 必须匹配。旧 v2 试验文件若使用旧 profile，需重新录制或明确转换并验证，不能当作已兼容的新 Movie。

## 播放旧版局内 TAS（v1）

1. 从 Steam 启动游戏，等待 Studio 自动打开并显示启动暂停点。此时可在 `Movie → Open Movie` 预览 v1 `.hktas` 文件。
2. 点击顶部 `Play` 继续初始化。在游戏标题画面选择序列所需的存档槽；进入起点场景后，等待 Studio 显示 `READY` 和 `CONNECTED · IPC v1`。
3. 如果序列配有需要恢复的状态，打开 `Savestates`，选择匹配且显示 `Compatible` 或 `Ready` 的项，点击 `Restore Selected` 并等待 `Completed`；如提示覆盖，先核对再决定是否批准。已经从匹配存档槽进入正确起点时，可直接继续。
4. 在 Studio 点击 `Pause`，打开 `Movie Text`，依次点击 `Validate`、`Upload`、`Start Replay`。开始回放后仍处于暂停状态；点击 `Frame Advance` 可执行 Movie 第 0 帧，再次点击逐帧推进，点击 `Play` 则连续播放。
5. 回放结束后查看 Runtime 状态中的 `reason=Completed` 与 `mismatchCount`。回放完成后游戏可能继续运行，需要查看终点时及时点击 `Pause`。

存档显示 `Incompatible` 或 `Upload` 报 `HKTAS220` 时不要强行使用。通常是游戏版本、Mod manifest、画面分辨率/窗口模式、基准存档或序列身份与当前环境不一致，应换用该序列配套的环境和存档。

## 编辑输入

`Input Editor` 每行代表一帧，每列代表一个游戏输入。单击格子可切换输入；按住 `Shift` 选择连续多帧后，可以一次修改整个选区。修改按键或撤销后，选区会保留，便于继续编辑其他列。

播放和逐帧时，默认勾选的 `播放时跟随` 会把当前 Movie 帧滚动到可见区域；跨过表格的 500 行分页时会自动换页。取消勾选后可以停在其他帧查看，`当前帧` 按钮随时可手动定位。全流程 v2 的表格行号按 Movie 帧计算，加载时跳过的原生循环不会推进表格行号；顶部同时显示 Movie 帧和原生帧。

- `Frame` 和 `Count`：选区起始帧和帧数。
- `Undo / Redo`：撤销或重做草稿修改。
- `Copy / Paste`：复制输入帧，或用剪贴板内容替换选区。
- `Insert / Delete`：插入空帧或删除选区。
- `Axes`：设置模拟轴，范围为 `-10000` 到 `10000`。

表格编辑先改变 Movie 草稿，不会直接移动或修改游戏角色。点击 `应用分支` 后，Runtime 才会接收草稿；修改过去输入后，使用 `应用并重放到 Frame`，让游戏从兼容存档重新播放到目标帧。

表格快捷键：`Ctrl+C/V` 复制和粘贴，`Ctrl+Z/Y` 撤销和重做，`Insert/Delete` 插入和删除。

## 人工录制输入

1. 先暂停游戏。
2. 打开 `Tools → Advanced Authoring`。
3. 点击 `Start Recording`，回到游戏进行操作。
4. 完成后点击 `Stop Recording`。
5. 打开 `Movie Text` 查看、校验并保存生成的序列。

`Advanced Authoring` 也支持单帧指定输入、批量输入、编辑完整局内记录、分支切换和按帧定位。普通编辑优先使用 `Input Editor`。

## 存档和回档

- `Shift+F1` 到 `Shift+F10`：保存到对应快捷槽。
- `F1` 到 `F10`：从对应快捷槽恢复。
- `Create Manual Save`：创建带名称的手动存档。
- `Restore Selected`：恢复列表中选中的存档。

快捷槽保存的是存档引用和 TAS 帧位置，不是完整进程内存快照。恢复过程会执行兼容性检查，并通过输入重放回到目标帧。恢复期间等待 `Completed`，不要同时发送其他运行命令。

自动存档位于 `Savestates` 页面。勾选 `自动存档`，填写有效帧间隔和保留数量，再点击 `应用自动存档策略`。系统只清理超出保留数量的自动存档，不删除手动存档。

## 导出 MP4

1. 把游戏恢复到序列要求的起点并暂停。
2. 在 `Movie Text` 打开并校验序列。
3. 点击 `导出 MP4`。
4. 首次使用时选择 `ffmpeg.exe`，然后选择一个尚不存在的 `.mp4` 输出文件。
5. 等界面显示导出完成。导出期间不要手动操作游戏。

导出会先上传当前 Movie，再从起点重放并录制。已有输出文件不会被覆盖。

## 常见问题

### 已连接但按钮不可用

等待右上角同时显示 `READY` 和 `CONNECTED`，并确认游戏已经进入可控制小骑士的场景。标题画面和加载过程没有可用的 `HeroController`。

### 在文本框里按 V 没有逐帧

文本输入焦点会优先保留字母输入。点击输入表格或窗口空白处，再按逐帧快捷键。

### 修改了表格但游戏没有变化

表格修改默认是草稿。点击 `应用分支` 提交；如果修改的是过去输入，再点击 `应用并重放到 Frame`。

### F1 没有恢复

先用 `Shift+F1` 保存槽位，并等待存档达到 `Ready`。恢复时 Studio 必须获得焦点。

### Tab 为什么没有快进

当前没有独立的加速播放命令，`Tab` 用于界面焦点导航。
