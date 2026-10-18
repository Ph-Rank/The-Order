# 1.2.0 构建与版本契约

固定 .NET SDK 10.0.401、现有锁文件、Release x64、NativeProductionCandidate=true。最终构建提交、源码清单及 SHA-256 随发布产物记录；无开发机 Profile 或测试凭据。

语义版本、ProductVersion、Tag、更新通道及发行说明均为 `1.2.0` / `v1.2.0`。实际程序与启动器的 Windows FileVersion 为 `1.2.0.65535`，高于同核心版本的 Beta 修订号。

已公开的 1.1.2 / Beta 4 更新器把正式安装器引导程序的版本资源固定校验为 `核心版本.0`。因此安装器引导资源保持 `1.2.0.0`，与已发布客户端的校验契约兼容；这不是实际程序载荷的版本号。安装器使用相同的 `1.2.0` ProductVersion、受管安装身份和精确哈希清单，按哈希替换载荷，不依赖低四位决定是否升级。不得将两种资源字段混作同一比较规则。

安装程序通过 native/scripts/package-native-release.ps1 与 package-native-installer.ps1 构建。Native Profile schema 与安装身份不变。新增用户链接使用现有身份组件目录中的独立 `user-links-*` 原子封装；不会改写既有确认文档的结构，也不迁移书架。

本机验证使用独立 Test Profile。安装实验使用独立 AppID/Mutex、禁止注册表和快捷方式、精确目标目录的实验安装器，载荷与最终分发逐文件校验；不能冒称已完成干净 OS 的正式安装身份验收。
