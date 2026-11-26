# The Order 1.3.1 构建

从发布标签 `v1.3.1` 检出源码。使用 .NET SDK 10.0.401 和项目锁定的 NuGet 依赖，以 Release x64 发布 `native/TheOrder.WinUI/TheOrder.WinUI.csproj`。发布属性为 `NativeProductionCandidate=true`、`NativeMultilingualEnabled=false`。本机画面层使用现有的 Windows SDK 和 C++ 工具链。

按 [安装器构建说明](../../../native/installer/BUILD.md) 执行 `package-native-release.ps1` 和 `package-native-installer.ps1`。随版源码和安装器源码附包以各自的文件清单为准。使用 [发布附件](https://github.com/Ph-Rank/The-Order/releases/tag/v1.3.1) 中的 `SHA256SUMS.txt` 核对下载。
