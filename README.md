<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="native/TheOrder.WinUI/Assets/Brand.Dark.png">
  <img src="native/TheOrder.WinUI/Assets/Brand.Light.png" alt="The Order" width="96" height="96">
</picture>

<h1>The Order</h1>

<p><strong>整理你的 Galgame 书架，探索作品之外的世界。</strong></p>
<p>Windows 原生游戏管理器 · 本地优先 · ACGN 跨媒体资料</p>

<p>
  <a href="#下载与安装"><img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square" alt="Windows 10 / 11 x64"></a>
  <a href="https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.0"><img src="https://img.shields.io/badge/Stable-1.2.0-2EA44F?style=flat-square" alt="稳定版 1.2.0"></a>
  <a href="https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.0-beta.4"><img src="https://img.shields.io/badge/Beta-1.2.0--beta.4-DB8A26?style=flat-square" alt="公开测试版 1.2.0-beta.4"></a>
  <a href="https://t.me/+ONE-Lif0uZBiNzJh"><img src="https://img.shields.io/badge/Telegram-加入交流群-26A5E4?style=flat-square&amp;logo=telegram&amp;logoColor=white" alt="Telegram 交流群"></a>
</p>

<p>
  <a href="#下载与安装">下载与安装</a> ·
  <a href="#功能">功能介绍</a> ·
  <a href="#界面预览">界面预览</a> ·
  <a href="#交流与反馈">交流与反馈</a>
</p>

</div>

## 下载与安装

| 版本 | 适合谁 | 下载 |
| --- | --- | --- |
| **正式版 · 1.2.0** | 游戏书架、游玩记录与五类 ACGN 专业资料 | **[下载 Windows 安装包](https://github.com/Ph-Rank/The-Order/releases/download/v1.2.0/The-Order-1.2.0-Setup-x64.exe)** · [更新说明](https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.0) |
| **历史 Beta · 1.2.0-beta.4** | 保留旧测试包；现已由 1.2.0 正式版接续 | **[下载 Beta 安装包](https://github.com/Ph-Rank/The-Order/releases/download/v1.2.0-beta.4/The-Order-1.2.0-beta.4-Setup-x64.exe)** · [测试说明](https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.0-beta.4) |

**同一个程序，可在「设置 → 程序更新 → 更新通道」中选择正式版或 Beta。** 切换时沿用当前书架，安装前自动备份。只有主动选择 Beta 才会收到测试版；也可切回兼容的正式版。

旧 `1.2.0-beta.1` 用户需手动安装一次新 Beta；首次打开可选择继续使用旧 Beta 书架，无需重新扫描游戏。已有两份书架不会自动合并。不要直接复制 Profile 文件夹。

面向 **Windows 10 1809 / Windows 11，x64**。运行依赖随安装包提供，无需安装 Node.js 或开发 SDK。Windows 10 尚未完成实机验收，安装包暂未签名；详细限制与 SHA-256 校验文件见各版本发布页。

## 功能

- **把游戏整理成书架**：扫描文件夹、选择导入，支持同一作品的多个安装，以及封面 / 列表视图、搜索、筛选和愿望清单。
- **补齐作品资料**：识别标题与版本，展示封面、简介、标签、人物和制作信息；有歧义的结果留给你核对。
- **记录自己的游玩**：启动游戏、统计时长，保存状态、收藏、评分和笔记。移除书架条目不会删除游戏文件。
- **按自己的习惯使用**：自选封面、保留个人名称，切换浅色 / 深色主题；支持统一资料语言偏好，缺少中文时自然回退。

### ACGN 跨媒体探索

从一部游戏出发，继续了解它的改编动画、原声音乐、漫画和轻小说，在应用内浏览更深入的资料。

| 媒体 | 可以继续探索的内容 | 主要资料来源 |
| --- | --- | --- |
| **Galgame** | 发行资料、角色与声优、制作人员、相关游戏 | NextMoe |
| **动画** | 逐集资料、角色 / 声优、Staff、制作公司、前后作、PV 与正版观看入口 | AniList · Bangumi |
| **音乐** | 专辑、不同发行版本、多碟曲目、录音 / 音乐作品、作曲与演奏等制作资料 | MusicBrainz |
| **漫画** | 作品资料、作者 / 作画、系列与单行本关系、出版信息 | AniList · Bangumi · NDL Search |
| **轻小说** | 作者 / 插画师、系列与单卷、出版社、文库、ISBN 与出版版本 | Bangumi · AniList · NDL Search |

Bangumi 连接跨媒体作品，各专业来源补充深度信息。不同季度、单卷和发行版本分别保留；不确定的对应关系可核对、确认或排除。资料差异可手动确认，不同媒体可添加为独立相关作品；网络失败的明确链接可待核验保存。

## 界面预览

**作品详情** — 从本地游戏进入作品资料与关联内容。

![The Order Galgame 详情：封面、个人记录与发行资料](docs/images/galgame-details.png)

<details>
<summary>展开查看动画、音乐与轻小说页面</summary>

### 动画 · 逐集资料

![动画详情中的分集列表、原文标题与本集说明](docs/images/anime-episodes.png)

### 音乐 · 多碟曲目与制作资料

![音乐详情中的曲目列表、录音、音乐作品和作曲人员](docs/images/music-tracks.png)

### 轻小说 · 单卷与出版记录

![轻小说单卷的插画师、出版社、ISBN 与 NDL 出版记录](docs/images/novel-publication.png)

</details>

<sub>预览来自隔离演示书架；跨媒体页面已进入 1.2.0 正式版。作品图片及资料的权利归各自权利人所有。</sub>

## 开始使用

1. **安装并打开**：下载所选版本的 Setup，安装后从「The Order」入口启动，在设置中选择更新通道。
2. **导入游戏**：选择游戏目录，核对扫描结果；有多个启动程序时，指定希望使用的程序。
3. **完善书架**：补全资料、确认不确定的匹配，再开始游玩。作品目录默认可匿名浏览，部分功能可在设置中自行连接账号。
4. **探索关联作品**：在游戏详情中打开关联动画、OST、漫画或小说，继续查看专业资料。

<details>
<summary>安装、资料与使用边界</summary>

- **更新通道**：在「设置 → 程序更新 → 更新通道」选择正式版或 Beta，再检查更新并确认安装。默认只接收正式版，主动选择 Beta 后才会收到测试版。
- **书架与资料**：切换通道继续使用所选书架，无需重新导入。已有的正式版与旧 Beta 书架不会自动合并，请勿直接复制 Profile 文件夹。
- **备份与回退**：软件内安装更新前自动备份用户资料；在设置中切回「正式版」即可回退到兼容版本，最低为 **1.1.2**，保留 Beta 期间新增的个人记录。自动备份不含游戏本体与存档，请另行备份。详见 [备份与回退说明](https://github.com/Ph-Rank/The-Order/blob/v1.1.2/docs/release/1.1.2/README.md#备份与回退)。
- **旧版过渡**：`1.1.0` 用户先更新至 `1.1.2`；`1.2.0-beta.1` 用户需手动安装一次新 Beta，首次启动时选择原 Beta 书架。旧 Electron 资料不会自动迁移。手动安装前，请从托盘正常退出所有相关实例。
- **账号与同步**：登录与个人同步相互独立，同步默认关闭。稳定版真实云端写入等未验证范围见 [认证与隐私说明](https://github.com/Ph-Rank/The-Order/blob/v1.1.2/docs/release/1.1.2/AUTHENTICATION.md)。
- **联网与播放**：资料覆盖、限流与可用性依各来源；PV 和观看入口受原平台版权、地区、年龄及订阅条件限制，链接存在不代表当前可播放。
- **Beta 当前范围**：尚不包含漫画逐章目录、阅读器、VGMdb 接入或离线机器翻译。干净系统、多 DPI、完整物理输入、长期稳定性及最终 Beta 包的真实 OAuth 到期恢复仍待进一步验证。

</details>

## 交流与反馈

欢迎加入 **[The Order Telegram 交流群](https://t.me/+ONE-Lif0uZBiNzJh)**，分享使用体验、讨论功能与提出建议。

遇到可复现的问题，请通过 **[GitHub Issues](https://github.com/Ph-Rank/The-Order/issues/new/choose)** 反馈，附上软件版本、Windows 版本、操作步骤及预期 / 实际表现。截图和短日志可选，请先脱敏，勿上传 Token、账号凭据、私人路径、完整书架备份或游戏文件。

## 文档与开发

- [稳定版使用指南](https://github.com/Ph-Rank/The-Order/blob/v1.1.2/docs/release/1.1.2/README.md) · [稳定版更新记录](https://github.com/Ph-Rank/The-Order/blob/v1.1.2/docs/release/1.1.2/CHANGELOG.md)
- [Beta 发布说明](https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.0-beta.4) · [Beta 待验证项目](https://github.com/Ph-Rank/The-Order/blob/v1.2.0-beta.4/docs/release/1.2.0-beta.4/HUMAN-CHECKS.md)
- [稳定版构建说明](https://github.com/Ph-Rank/The-Order/blob/v1.1.2/docs/release/1.1.2/BUILD.md) · [Beta 源码与构建说明](https://github.com/Ph-Rank/The-Order/tree/v1.2.0-beta.4/docs/release/1.2.0-beta.4)
- [开发与数据隔离](DEVELOPMENT.md) · [历史 Electron 文档](https://github.com/Ph-Rank/The-Order/blob/85f06e18976d7a97222d25e2a35bf4aa9d31a789/README.md)

当前原生版本使用 **C# / WinUI 3**。稳定版与 Beta 的对应源码随各自 Release 提供；构建或贡献时，请先选择对应分支 / Tag，阅读其开发说明，并使用独立测试资料目录。

## 开源与致谢

The Order 采用 **[AGPL-3.0-or-later](LICENSE)** 开源。第三方依赖、数据、模型和美术保留各自许可，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。

感谢 [NextMoe](https://developer.nextmoe.dev/)、[Bangumi](https://bgm.tv/)、[AniList](https://anilist.co/)、[MusicBrainz](https://musicbrainz.org/)、[NDL Search](https://ndlsearch.ndl.go.jp/) 与 [bangumi-data](https://github.com/bangumi-data/bangumi-data) 提供的资料；感谢 [PotatoVN](https://github.com/GoldenPotato137/PotatoVN) 的功能与架构参考，以及 [PotatoDBMapper](https://github.com/GoldenPotato137/PotatoDBMapper) 的公开标题索引。

本项目为独立应用，不代表上述项目或平台官方。
