# 构建 1.3.0-beta.2

以发布标签和完整 source.zip 为准。Windows x64，固定 .NET SDK 10.0.401、Windows SDK 10.0.26100.0、Visual C++ 工具链及锁定 NuGet 依赖；安装器使用经脚本 SHA-256 校验的 Inno Setup 6.7.3。不升级依赖。

```powershell
dotnet restore native/TheOrder.WinUI/TheOrder.WinUI.csproj -r win-x64 --locked-mode
pwsh -File native/scripts/build-guide-overlay.ps1 -Architecture all
dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -r win-x64 -p:NativeProductionCandidate=true -p:DebugType=None -p:DebugSymbols=false --no-restore --self-contained true -o .artifacts/publish
```

攻略原生组件的脚本拉取固定 ReShade/Detours 提交并保留修改步骤；许可证见 THIRD_PARTY_NOTICES.md。Dagre 浏览器资源、Jint/Acornima、OpenMcdf/ExcelDataReader 均有固定版本和许可。本次发布复用与完整原生输入哈希相符的已验证组件，不宣称本轮重新编译了所有 C++ 第三方库。

随后使用 `native/scripts/package-native-release.ps1` 和 `native/scripts/package-native-installer.ps1`，文档目录指定 `docs/release/1.3.0-beta.2`。打包拒绝版本不一致、文件哈希变化或缺失依赖/许可。源码归档不包含生成的 payload、SDK、NuGet 缓存、个人 Profile 或密钥。

开发和启动验证必须使用独立 Test Profile，见 DEVELOPMENT.md。构建与校验成功不代表所有真实游戏、Windows 版本和长期运行均已验收。
