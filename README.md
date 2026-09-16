# HollowKnightTASMod

Hollow Knight TAS 工具：游戏内 Runtime、Studio 编辑器与非视觉 AI 控制接口。

当前版本已交付。假骑士示范在第 3042 帧完成击杀，已通过两次独立完整回放；匹配的 Mod、Studio、存档和操作文档见 [交付包](artifacts/releases/HollowKnightTAS-3042-delivery.zip)。已验证功能及限制见 [交付验收范围](docs/DELIVERY-STATUS.md)。

开发从 [CURRENT.md](CURRENT.md) 开始，它包含架构边界、真实状态和后续任务。

普通操作见 [使用说明](docs/USER-GUIDE.md)，其中列出了当前可用入口和尚未验收的限制。

只编译、不安装：
```powershell
dotnet build HollowKnightTAS.sln -p:SkipHKTASInstall=true
```

只构建并验证 Companion 暂存包：
```powershell
./scripts/Build-CompanionBundle.ps1 -StageOnly
```

本机路径使用 LocalBuildProperties.props；签名密钥保留在本地。
详细打包说明见 [packaging/README.md](packaging/README.md)。
首次配置可参考 LocalBuildProperties.props.example。

[早期研究与旧进展](README.history.md)仅供按需追溯。
