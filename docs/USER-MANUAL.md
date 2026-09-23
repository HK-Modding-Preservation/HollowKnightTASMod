# HollowKnightTAS Studio 操作手册

Studio 用来在《空洞骑士》中逐帧播放、编辑和回退 TAS 输入。直接启动游戏时，Mod 会自动打开 Studio，并把受控游戏停在 `Startup frame: 0`；如果先打开 Studio，也可以点击 `Start 启动` 拉起游戏。

## 开始前

`Startup frame: 0` 表示游戏的第一个原生 PlayerLoop 尚未执行，此时可以点击 `Frame Advance` 逐帧推进，或点击 `Play` 继续启动。启动帧与下面的 TAS 输入帧分开计数；启动阶段尚未连接 Runtime，其他局内命令需等待初始化完成。

直接启动时，最初的标题进程会在进入存档前退出，Studio 随后启动受控游戏。这个短暂的重新启动是首帧暂停流程的一部分。

在标题画面正常选择存档，进入可控制小骑士的场景后，等右上角显示 `READY` 和 `CONNECTED · IPC v1`。如果底部出现 `An active HeroController is unavailable`，说明游戏仍在标题画面、加载过程或没有可控制的小骑士。进入存档并等待场景加载完成即可。

顶部四个控件是最常用的运行控制：

- `Play / Pause`：继续或暂停，默认快捷键为 `Pause`。
- `Frame Advance`：暂停时前进一帧，默认快捷键为 `V`。
- `Stop`：停止正在播放的序列。
- `Frame`：当前 TAS 输入帧。

快捷键只在 Studio 窗口获得焦点时生效。可在 `Settings → Hotkeys` 修改播放和逐帧按键。

## 播放已有 TAS

1. 在 `Movie → Open Movie` 选择 `.hktas` 文件。
2. 打开 `Savestates`，选择与该序列起点匹配、状态为 `Compatible` 或 `Ready` 的存档，点击 `Restore Selected`。
3. 等恢复状态显示 `Completed`。如果要求覆盖原版存档，核对提示后点击 `Approve Overwrite`；不想覆盖则点击 `Deny Overwrite`。
4. 打开 `Movie Text`，依次点击 `Validate`、`Upload`、`Start Replay`。
5. 使用 `Pause` 暂停，使用 `V` 逐帧查看。

存档显示 `Incompatible` 时不要强行使用。通常是游戏版本、Mod manifest、基准存档或序列身份与当前环境不一致，应换用该序列配套的存档。

## 编辑输入

`Input Editor` 每行代表一帧，每列代表一个游戏输入。单击格子可切换输入；按住 `Shift` 选择连续多帧后，可以一次修改整个选区。修改按键或撤销后，选区会保留，便于继续编辑其他列。

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
