# 构建与源码

源码对应 Git Tag v1.2.1-beta.1。使用仓库锁定 .NET 10 SDK 与依赖，Release x64、自包含 WinUI 3；普通用户无需 SDK。程序版本 1.2.1-beta.1，Windows 数字版本 1.2.1.1，与更新器协议一致。

分发包由 native/scripts/package-native-release.ps1 与 package-native-installer.ps1 生成。服务端开发凭据、.dev.vars、测试 Profile、内部诊断证据与调试符号不进入安装包。

安装使用现有统一 AppID；Pre-release 不设为稳定 latest，不改写已发布附件。回归与实际包验证证据另存维护工作区，不能以 Mock 代表真实 API/播放验收。
