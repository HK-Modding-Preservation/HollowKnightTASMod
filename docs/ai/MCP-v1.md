# MCP v1

`HollowKnightTAS.AgentBridge` 是 Automation v1 的 stdio MCP 适配器，不是新的权限源。

## 启动

签名 bundle 中的入口：

```text
Companion/win-x64/Tools/HollowKnightTAS.AgentBridge.exe
```

默认读取当前用户 automation bootstrap；也可由本机 MCP host 显式传入：

```text
--bootstrap=<automation-v1.json>
```

桥接器只从 stdin 读取 UTF-8 JSON-RPC，每条消息一行；stdout 只输出单行合法 JSON-RPC，诊断只写 stderr。它不监听 HTTP/TCP，不发起网络请求。

实现以 MCP `2025-11-25` 为冻结版本，支持：

- `initialize` / `notifications/initialized` / `ping`
- `resources/list` / `resources/read`
- `resources/templates/list`（空）
- `tools/list` / `tools/call`

这遵循 MCP 官方对 stdio 的换行分帧、stdout 隔离、初始化先行、resources 和 tools schema 的要求：

- <https://modelcontextprotocol.io/specification/2025-11-25/basic/transports>
- <https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle>
- <https://modelcontextprotocol.io/specification/2025-11-25/server/resources>
- <https://modelcontextprotocol.io/specification/2025-11-25/server/tools>

## 工具可见性

全流程 v2 的完整对象观察使用 `hktas_get_world_snapshot` 和 `hktas_get_object_details`，支持暂停查询、FSM 和碰撞形状。参数、分页与遗漏说明见 [世界观察接口](WORLD-OBSERVATION.md)。

`hktas_get_state` 默认返回角色和游戏语义快照。主菜单没有角色时，传入
`{"statusOnly":true}` 可读取最新控制模式、帧号和自动存档策略；返回值标记
`availability.semanticSnapshot=not-requested`，不包含 `stateJson`。
SDK 对应 `GetOperationalStatusAsync()`，CLI 对应
`automation call getState observe.state.summary statusOnly=true`。
自动存档策略仍经 `setAutoSavePolicy` 的控制租约和模式/帧号校验修改。

`ReadOnly` 只列只读工具。`ApprovedControl` 才列控制工具；两个 mutation 工具还要求 Runtime 注册时 `DebugMutationEnabled=true`。tool annotation 只用于 MCP UI，实际权限始终由 Companion 重新检查。

首批工具包括：

```text
hktas_get_state
hktas_get_timeline
hktas_get_desync
hktas_get_movie
hktas_propose_movie_patch
hktas_validate_movie_patch
hktas_acquire_control
hktas_release_control
hktas_pause
hktas_resume
hktas_quit_game
hktas_step
hktas_run_until
hktas_start_recording
hktas_stop_recording
hktas_start_replay
hktas_stop_replay
hktas_create_replay_save
hktas_restore_replay_save
hktas_approve_replay_save_overwrite
hktas_cancel_replay_save_restore
hktas_resume_replay_save_restore
hktas_apply_movie_branch
hktas_set_hero_pose
hktas_set_player_resources
```

每个 input schema 都是 closed object（`additionalProperties=false`）。tool 结果同时返回 `structuredContent` 与兼容的 JSON text content；业务拒绝使用 `isError=true`，协议形状错误使用 JSON-RPC error。

桥接器可为一次 MCP 进程保存一个 lease ID，只有成功调用 `hktas_acquire_control` 后才会把它附加到写工具；stdio 关闭会断开 automation client，Companion 随即回收 lease。

Replay Save 恢复由 Companion 监督器执行。`hktas_restore_replay_save` 返回 `ColdRestorePrepared` 和 `operationId` 只表示已启动，不表示目标已恢复。需要覆盖授权时，读取拒绝原因，经 `hktas_approve_replay_save_overwrite` 明确批准后重新提交恢复。

监督器在目标校验后自动完成所有权交接，游戏仍保持暂停；客户端不需要再调用 `hktas_resume_replay_save_restore`。该旧入口不作为当前冷恢复流程的收尾步骤。

取消接口为 `hktas_cancel_replay_save_restore({"operationId":"<恢复返回的 ID>"})`，无需旧进程的模式或帧号，但仍需已批准的控制权限与恢复控制租约。通过 MCP `resources/read` 读取 `hktas://session/current/status`，或通过 CLI 调用 `getStatus observe.status`，可获取 `coldRestore.operationId/state/sequence/detail/targetMovieTick`。客户端必须匹配操作 ID；`Completed` 才是该操作成功结束，`Failed` 或 `Cancelled` 不是成功。运行中观察超时不能当作取消。

恢复会更换游戏进程和会话。收到会话失效时，重新连接当前 automation bootstrap，再查询并匹配原 `operationId`；不要重新提交恢复来代替状态查询。已通过模拟游戏端的真实本地管道接线检查，覆盖源进程退出后的 MCP 取消及重连后的终态读取。

取消不会保证原游戏窗口继续存在：源进程已交给恢复监督器后，取消会结束该源进程；若目标已启动，则结束本次操作拥有的目标并执行槽文件恢复。等待匹配操作的终态，再重新启动或选择已有存档。源端接管与取消并发已通过安装版立即取消检查；无游戏进程时保留只读状态入口的保护通过定向管道检查。

使用包含上述修复的成套交付包。旧 `HollowKnightTAS-ea42-3042.zip` 的 MCP 取消入口缺失操作 ID；其 CLI 可提交 `automation call cancelReplaySaveRestore control.replay-save operationId=<ID>`，但仍存在源端刚完成接管时立即取消的缺陷，应更新配套工具。
