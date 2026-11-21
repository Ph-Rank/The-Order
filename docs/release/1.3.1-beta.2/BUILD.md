# 构建 The Order 1.3.1-beta.2

使用发布标签 `v1.3.1-beta.2` 或对应的源码附件。构建需要 Windows x64、.NET SDK 10.0.401、Windows SDK 10.0.26100.0、Visual C++ 工具链、PowerShell 7 和锁定的 NuGet 依赖。安装包使用脚本校验的 Inno Setup 6.7.3。

```powershell
dotnet restore native/TheOrder.WinUI/TheOrder.WinUI.csproj -p:Platform=x64 --locked-mode
pwsh -File native/scripts/build-guide-overlay.ps1 -Architecture all
dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false -p:DebugType=None -p:DebugSymbols=false --no-restore -o .artifacts/publish
```

使用 `native/scripts/package-native-release.ps1` 与 `native/scripts/package-native-installer.ps1` 打包。各版本的 .NET 程序与 XAML 均重新构建，攻略组件复用输入和输出均经校验的原生载荷。依赖版本与许可证可在源码和包内清单查看。

用独立 Test Profile 验证发行包。入口和构建说明见 [DEVELOPMENT.md](../../../DEVELOPMENT.md)。
