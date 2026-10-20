# 1.2.2-beta.2 构建与验证

源码以本次 Release Tag 和随附 source.zip 为准。构建固定使用 .NET SDK 10.0.401、已锁定 NuGet 依赖、Windows SDK 10.0.26100 及现有 Visual C++ 工具。运行用户不需要安装开发工具。

依照 DEVELOPMENT.md 恢复各项目锁文件，不能重新求解或升级依赖；随后构建和发布：

```powershell
dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:DebugType=None -p:DebugSymbols=false --no-restore -o .artifacts/publish
```

安装包通过 native/scripts/package-native-release.ps1 和 package-native-installer.ps1 生成，使用校验过的 Inno Setup 6.7.3。构建、隔离安装及通道检查的最终证据另见发布附件 VALIDATION.md；不存在的验证不能由打包成功替代。
