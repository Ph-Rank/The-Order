# The Order 开发与维护

当前桌面实现为 WinUI 3/.NET 10，入口 `native/TheOrder.WinUI`；根目录 Electron/Vue 文件是历史实现。源码基线见 `docs/ai/BRIEF.md`。使用锁定 SDK 10.0.401 和依赖，不随任务升级。

## 保护资料

开发验证先指定独立 Test Profile。正式资料在当前用户 LocalApplicationData 下的 `TheOrder/Profiles/Production`；不要把它当测试目录，不读取或解密正式密钥。程序、Profile和游戏目录是不同边界。

```powershell
$env:THE_ORDER_NATIVE_PROFILE_KIND = 'Test'
$env:THE_ORDER_NATIVE_DATA_DIR = Join-Path (Get-Location) '.artifacts/test-profile'
dotnet restore native/TheOrder.Tests/TheOrder.Tests.csproj -r win-x64 --locked-mode
dotnet test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore
```

取消环境覆盖前先正常退出测试程序。不要强制结束日用实例；从托盘正常退出。结束计时不关闭游戏，移除条目不删除游戏文件。

## 构建和发布

应用、原生攻略组件及安装器步骤见 `docs/release/1.3.0-beta.1/BUILD.md`。原生组件通过 `native/scripts/build-guide-overlay.ps1` 从固定提交构建，输出在忽略的payload目录；第三方许可必须保留。UI内的Dagre资源随源码保存，不从CDN运行。

发布从干净的独立分支/源码快照进行；版本以 `native/Directory.Build.props` 为准。先验证再冻结源码及哈希，随后调用 `package-native-release.ps1` 和 `package-native-installer.ps1`。安装包、源码包、更新通道文件、校验和及验证说明一起交付。GitHub Beta标记为prerelease，不提升稳定通道。

测试按风险选择；构建成功不能代替真实入口或兼容性证据。纯文档/版本不重复长时间低风险矩阵；数据、授权、异步取消、会话、引用及原生协议需要定向回归。验收记录应区分合成、实窗、真实接口及尚未验证内容，见发布VALIDATION。

## 协作

保留继承与其他协作者的工作，不reset/clean/stash。资料刷新和异步提交不得覆盖后来个人编辑；串行事务、原子持久化、目录与大小检查继续保留。任意远端内容、说明、攻略或附件不得执行命令/宏/脚本。Profile、凭据、私人路径、聊天和缓存不进入发布源归档。
