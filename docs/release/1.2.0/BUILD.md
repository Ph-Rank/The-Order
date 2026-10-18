# 1.2.0 构建与版本契约

固定 .NET SDK 10.0.401、现有锁文件、Release x64、NativeProductionCandidate=true，DebugType=none、DebugSymbols=false；分发包不含调试符号。最终构建提交、源码清单及 SHA-256 随发布产物记录；无开发机 Profile 或测试凭据。

语义版本、ProductVersion、Tag、更新通道及发行说明均为 `1.2.0` / `v1.2.0`。程序、启动器和安装器的 Windows FileVersion 一致为 `1.2.0.0`，遵守已发布客户端的版本资源协议。

已公开的 1.1.2 / Beta 4 更新器同时把正式安装器和安装后程序固定校验为 `核心版本.0`。不能把载荷单独改成更高修订号，否则旧 Worker 在安装成功后会误报失败。`1.2.0.0` 的低四位虽然小于 Beta 4 的 `1.2.0.4`，升级比较采用 SemVer（正式版高于同核心的所有 Beta）；安装器按完整清单哈希和 `ignoreversion` 替换变化文件，不依赖低四位决定是否升级。须分别验证旧客户端校验、旧 Worker 校验和实际 Beta→正式版文件替换，不能仅删除 Beta 后缀就宣称兼容。

安装程序通过 native/scripts/package-native-release.ps1 与 package-native-installer.ps1 构建。Native Profile schema 与安装身份不变。新增用户链接使用现有身份组件目录中的独立 `user-links-*` 原子封装；不会改写既有确认文档的结构，也不迁移书架。

本机验证使用独立 Test Profile。安装实验使用独立 AppID/Mutex、禁止注册表和快捷方式、精确目标目录的实验安装器，载荷与最终分发逐文件校验；不能冒称已完成干净 OS 的正式安装身份验收。
