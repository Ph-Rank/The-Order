# The Order：当前实现与评审交接

## 1.2.1 Beta 1 公开交付与首页（2026-10-19）

`v1.2.1-beta.1` 已公开为 Pre-release，Tag 与应用源码固定为 `f5b7ca4fc61d0355c9ee27e70b09ae100843148e`；完整客户端回归4443/0/0。原日用目录也已用同一安装包替换，653份载荷及3份入口附件核对一致，空Test启动并正常退出0；未打开或迁移Production。实际发布、说明修订、安装和程序回退凭据以 `.artifacts/release-1.2.1-beta-20261019/completion.json`、`report.md` 为准。

用户要求同步GitHub首页：README改用1.2.1-beta.1徽章、安装包与说明链接，标注Beta影像和音乐/书籍平台入口，并更新对应版本文档。默认main仅同步README，不合入Beta应用实现，不改稳定1.2.0或已发布Tag/附件；首页回执在同目录 `homepage-revision-1/`。此文档后续提交不是安装包源码基线，不能据新HEAD重新标注既有构建。

## 1.2.1 Beta 1 发布候选（2026-10-19）

维护分支 codex/public-beta-1.2.1-20261019。已将接手时未提交、但已在日用版验收的音乐/书籍增量单独提交为ccd15ee；未混入reports或research_notes。版本按现有更新器协议采用1.2.1-beta.1（数字1.2.1.1），GitHub必须Pre-release，不修改稳定latest及旧附件。

修复IGDB有合法YouTube关系但名称缺失时被丢弃：保留原名称null，中性显示“宣传影像”，不声称官方PV；105342真实公网返回68884/lVNrSOc0wG0且originalName为null，修复前同ID空列表。服务继续使用原Worker、原SQLite namespace和single-app-quota-v1；缓存命名升级，不创建第二套配额或修改Secrets。客户端保留旧正缓存离线降级，旧空缓存重新读取；主库、人工确认/排除/默认schema不变。

NextMoe已收录DLsite的RJ/VJ/BJ商品编号转换为普通来源蓝色链接，首页/搜索页不进入商品来源，不后台逐条探测商品。上游旧商品可能404；已以真实RJ297925核对《星空铁道与白的旅行》商品页。宣传影像放到标签上方，高级撤销/重置收进影像选项。播放器显式启用YouTube原生控件与中文界面，分辨率由YouTube自身决定；不使用失效的强制画质API。

本轮隔离证据目录.artifacts/release-1.2.1-beta-20261019/。最终完整客户端4443/0/0（含既有本地合成Worker闭环）、服务Node36/36与workerd/SQLite7/7通过。真实IGDB复核31个既有人工审定编号全部成功，29个有影像、5个原有歧义仍保留。实际WinUI已播放Sabbat影像，并打开原生画质列表及选择720p；不是物理设备验收。补充发现并以先失败/后通过测试修复CSP脚本哈希的HTML换行规范化问题，不放宽CSP。最终安装包、回退、日用切换及GitHub发布须以对应回执确认，不能视作已交付。所有测试仅合成Test Profile，不读取Production或游戏数据。后续以该目录最终report.md和completion.json确认实际完成范围。


## IGDB 公网服务已验收（2026-10-19）

用户批准后，按已冻结29e8ef4执行独立the-order-igdb-game-videos的关闭部署、核对域名、Cloudflare Secrets及启用。实际地址https://the-order-igdb-game-videos.zyx2251721565.workers.dev/，与客户端内置值一致。已登录Workers面板显示每天100000次限额及Upgrade，与官方Free档一致；没有升级、付费资源或Bangumi部署。SQLite与single-app-quota-v1保持唯一，所有本地直接凭据服务已关闭。

公网16项协议/身份/缓存/并发一致性/安全检查通过；停用返回503，重新启用后200，同一协调器命名空间和缓存时间戳保留。WinUI候选先移走远程缓存，使用同一真实HTTPS服务取得CLANNAD与魔法使之夜重制版；用户各点击一次YouTube播放，应用探针均为playing。收起/返回释放，服务停用后保留9段缓存与重试提示，恢复后重试成功。再次清远程缓存及重启，人工确认/默认恢复，4份决定与主库5文件哈希不变，两个Test进程退出0。

冻结代码回归仍为完整客户端4428/0/0、Node35/35、workerd/SQLite7/7及Release x64；本次未改产品代码，不重算36部覆盖。线上缓存/首次读取抽样9个Worker及DO事件均ok，无异常、无日志，CPU1–6ms；不代表压力或长期验收。浏览器账单导航和桌面工具连接发生故障，未完成DO每日行数/存储面板核查，未取得新的有效桌面截图；应用内RenderTargetBitmap不含WebView2/完整文字，已排除。不能把上一阶段回环截图称为公网证据。

服务启用GO；客户端候选已具备零配置接线，但GitHub现有程序及日用安装未更新，公开客户端发布须另行授权。Free账户额度与OAuth服务共享，分布式滥用仍可能耗尽额度；不自动升级。维护步骤见services/igdb-game-videos/README.md；公网报告和最终版本/回退记录见.artifacts/igdb-production-ready-20261018/public-deployment-report.md。全部继承音乐/书籍修改保持未提交，Production、版本和GitHub均未修改。

用户随后明确授权替换本机正式版：2026-10-19已将同一已验收IGDB程序载荷切换至原C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe，版本仍1.2.0。源码检查点382b322、应用代码29e8ef4，保留全部继承音乐/书籍工作；没有重新编译应用。标准Setup隔离安装通过，实际日用654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留。原安装目录WinUI以全新空Test资料启动并退出0，辅助工具默认全关；未打开、迁移或复制Production及凭据，未启动真实游戏、推送或发布。旧日用662份程序及旧Setup已核验备份在C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261019-003803-igdb-public-pv，仅供程序回退。当前客户端可使用内置公网IGDB地址，收尾只读请求返回200；实际播放沿用此前相同程序字节的公网验收证据，不宣称本次私库/实机游戏已验收。切换凭据以.artifacts/local-igdb-daily-20261019/completion.json为准。

## IGDB真实API、PC候选及发行版核对（2026-10-18，当前增量）

当前分支 codex/igdb-live-qualification-20261018，基于6665e5d及全部继承dirty。503已证实来自IGDB字段game_type.name错误，修正为game_type.type；同一Wrangler和凭据恢复200，无代理/DNS/TLS改动。新增默认关闭的阶段诊断，不输出凭据、原始错误或响应正文；保留parent_game与version_parent区别和旧缓存兼容。

搜索不使用个人自定义名称，优先源内非机器英文正式名，再补日文原名/别名，最多三次。默认只优先显示标题对应的PC本篇/重制，Mod、合集、主机移植及未知结果折叠保留。作品级词条和当前安装分两轮核对；默认安装Steam编号与完整、非陈旧的唯一IGDB发行记录一致时直接显示PV，不写成伪造的人工确认。目录Steam链接、同名唯一候选和共用VNDB词条不能代替本地版本证据；非Steam缺少发行版证据仍需复核，尚未证明几乎全自动。

冻结10部API10/10、人工复核9/10、有视频8/10；36部API36/36、人工复核31/36、有视频28/36，5部原版/版本歧义保留。31部包含一次单独标注的Little Busters英文别名补查；不是自动匹配准确率。首修构建实际播放5个目标，最终publish-live-v4另实播Steam版及重制版、检查PC默认候选和释放。合成本地库/安装输入、真实IGDB/YouTube分别记录；不称真实游戏扫描或物理验收。

最终完整客户端4409 passed / 0 failed / 0 skipped；服务17/17；Release x64成功。缓存移走后重启确认和默认恢复，3份决定文件哈希不变。逐项证据及最终载荷在.artifacts/igdb-live-20261018/，详细规则见docs/igdb-game-videos-v1.md。开发GO、公开启用CONDITIONAL GO，仍无公开服务。Production、日用安装、版本、生产Bangumi Worker、GitHub均未修改；继承音乐/书籍dirty完整保留，提交只含本轮增量。


## IGDB 游戏宣传影像 v1（2026-10-18，最新开发增量）

当前 `codex/igdb-game-videos-20261018` 从3ed4463及全部既有音乐/书籍dirty继续，仅增加游戏影像。NextMoe仍为主资料；IGDB提供候选和已关联YouTube记录。游戏详情按需加载，标题或Steam检索不自动确认，明确编号可精确读取。原版/重制/移植/合集需核对，作品级影像不声称对应本地发行版。确认/排除/撤销/默认复用Native Profile独立用户决定，缓存清理和重启不丢失；迟到结果、并发revision、错误缓存和变更后的版本身份均有回归。

新增44项客户端测试，最终完整客户端含既有本地合成Worker契约4371 passed / 0 failed / 0 skipped；独立IGDB服务14项Node测试通过，Release x64 publish-v4通过。实际Test WinUI验证确认、排除、默认、撤销、切换、隐藏/收起释放及560/1180宽浅/深主题。游戏假视频得到真实YouTube150降级；已有CLANNAD动画PV独立进入playing，返回未恢复旧游戏播放器。以上不能当IGDB真实覆盖证据。

先冻结36部真实样本、10部pilot；无明确授权开发凭据，真实IGDB匹配率、视频覆盖/版本准确性和游戏播放率均未验证，不能写作0%或以Mock补齐。独立服务端适配默认禁用、无公开路由、未部署；凭据只在服务端，普通用户不登录Twitch、不申请Key。没有修改现有生产Bangumi OAuth Worker。后续最少配置与门槛见docs/igdb-game-videos-v1.md及services/igdb-game-videos/README.md。

本任务仅提交IGDB增量，接手时音乐/书籍UI及文档增量仍独立保留未提交；实际候选含它们，复现需结合.artifacts/igdb-game-pv-20261018/source-freeze.json。当前是开发接入GO、真实数据/公开分发NO-GO，缺开发凭据及真实样本播放。未改Production、日用安装、版本、Tag或GitHub；不要将此前的替换/发布授权套到本轮。

## 书籍阅读与购买入口 v1（2026-10-18，最新增量）

当前维护 `codex/book-outlets-v1-20261018`，从 9147b06 及接手时全部未提交音乐成果继续。本轮为已确认的漫画/小说/其他书籍增加紧凑“阅读与购买”：保留 AniList externalLinks 与 Bangumi 显式网址，按实际用途和层级分类，首页/搜索/禁用/类型冲突不进主要区域。MangaDex 新增轻量只读补充：一次原题检索最多10条，完整候选、唯一相同 AniList 编号、完整标题及已知 MAL 编号不冲突才采用已有完整网址；不引入阅读器、章节下载、账户、新服务器或全库索引，也不把其上传记录当官方渠道。版本、语言、ISBN 差异和原始来源保留；单卷不借用根系列身份。

新增64项书籍回归，最终完整客户端含本地合成 Worker 契约 **4327 passed / 0 failed / 0 skipped**；165项相关回归是重叠子集。确认/拒绝/撤销与缓存清理、重启仍沿用独立用户身份持久化；新元数据复用现有 Profile 远程缓存，无主库/用户资料迁移。同一 AniList ID 的核对编号发生冲突时，旧补充入口须隐藏，反例在移除保护时实际失败、恢复后通过，原日志保留。

Release x64 `publish-accepted` 在自建 Test Profile 实际运行并正常退出0。最终冷启动验证漫画、小说、单卷、返回、空状态、来源折叠及560/1180宽、浅/深主题；较早同轮同UI代码实际点击VIZ、ebookjapan和台湾角川，核对作品、作者及可选卷，未阅读、登录、购买或下载。Fate游戏935→漫画34549使用真实公开关系；芙莉莲与药屋为显式公开条目验收，合成小说导航不是跨作品关系证据。

28项先冻结样本：25项AniList准确解析，16项有主入口；另1项原作小说未定位、2项NDL分别429和超时。MangaDex对14漫画样本增补3部电子书类别，新增覆盖作品0；当前具体ISBN版本准确商品覆盖仍未建立。MangaDex网页被浏览器安全策略拦截，未绕过，仅以公开API验证。早两次全套分别有既有launcher/chain交接超时，中间及最终全套通过；未修改启动代码、限时或删除用例，时序根因未宣称修复。一次Test控制器25秒截止先到，应用随后正常完成小说资料并验证入口，不能当首次读取已按时通过。

源码只提交本任务书籍增量，继承的音乐代码和文档修改保留未提交；实际验收载荷包含这些音乐成果，须结合 `tested-source-manifest.json`。详见 `docs/book-reading-outlets-v1.md` 与 `.artifacts/book-outlets-v1-20261018/`。开发交付GO，后续公开发布CONDITIONAL GO，需单独发布验收及公开限制说明。本轮不推送、发布、替换日用安装或访问Production。


用户随后明确授权替换日用正式版，已完成原安装目录的本机切换，仍显示1.2.0。复用上述已验收publish字节，标准Setup在独立AppID/Mutex且无注册的目录通过安装；实际日用654份载荷、3份入口附件与更新基包一致，4份未知/路由文件保留。原目录WinUI以全新空Test资料启动并正常退出0，辅助工具默认全关；未打开或迁移Production、读取凭据或改动GitHub。662份旧程序和上一音乐构建Setup回退保存在 C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-172357-book-outlets/。切换最终凭据 .artifacts/local-book-outlets-daily-20261018/completion.json；本次授权不包含发布。

## 书籍入口排版与音乐来源按钮（2026-10-18，最新增量）

用户授权两项视觉调整及日用替换。在现有3ed4463与全部继承音乐dirty上，只改BookReadingView、MusicListeningView：书籍所有有效主入口共用一个按可用宽度换行的布局，用途、语言及卷册版本说明保留；不再按类别强制另起一行。音乐“资料与来源”改成与书籍相同的普通Button，沿用主题文字颜色，展开、收起和链接内容不变。

完整客户端含本地合成Worker回归4327 passed / 0 failed / 0 skipped；Release x64通过。真实隔离WinUI验证1180请求宽四个书籍入口同排、560宽自然换两行、浅/深主题及来源展开；音乐按钮原生角色Button、自动鼠标点击展开和受限peer收起通过。冻结公共元数据与明确合成的音乐版本/导航不作为新覆盖率、实际播放或物理输入证明。早期书籍展开在确认弹窗尚未关闭时被拒绝，确认后通过；无产品修复扩大范围，无新增低价值镜像单元测试。证据 .artifacts/outlet-layout-20261018/。

已用标准Setup替换原日用目录，仍显示1.2.0。654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留；原目录WinUI仅用全新空Test资料启动，辅助工具默认全关，正常退出0。662份旧程序及上一书籍版本Setup备份在 C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-182404-outlet-layout/，仅供程序回退。未访问或迁移Production、读取真实凭据、提交、推送或修改公开附件。当前包含新UI及继承音乐的工作树增量，不能单靠HEAD重建相同载荷；源码及载荷哈希、切换最终凭据见 .artifacts/local-outlet-layout-daily-20261018/completion.json 和 source-freeze.json。


## 音乐数字版自动加载与固定排序（2026-10-18，最新）

当前维护分支`codex/music-auto-digital-20261018`，HEAD仍9147b06，保留继承的全部未提交音乐外链与排版成果。本次展开“正版收听与购买”即依次读取当前入口和同组数字发行版，无需再点查找；两阶段共享请求时效和取消，重复展开不重复查询，失败或取消后可重试，刷新同时更新两阶段。切换作品后迟到响应不能继续查询旧作品。界面移除排序选项，平台统一按显示名称排序，资料站也按Amazon、Discogs、VGMdb等友好名称排列；不删除有效链接、不更换当前发行版、不新增Provider或迁移用户资料。

完整客户端含本地合成Worker契约4263 passed / 0 failed / 0 skipped；85项定向为重叠子集，新增11项自动加载/去重/取消/迟到/失败保留回归。首次定向失败源于Recording合成夹具误带发行版状态，修正夹具后通过，未放松产品校验，原失败日志保留。Release x64及标准安装包通过。实际WinUI中一次展开、另一次定向自动鼠标点击均直接呈现2组数字版，4个平台与3个资料链接保留，没有额外查找或排序控件。冻结公开元数据加明确合成的版本/关系仅证明本次交互，不证明真实覆盖、版权播放或物理输入。

用户正常退出后已替换原`C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，显示版本仍1.2.0。654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留；实际安装路径WinUI仅以全新空Test资料启动并正常退出0，辅助工具默认关闭。未访问或迁移Production、读取真实凭据，未提交、推送或发布。662份旧程序及上一排版构建Setup回退在`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-142540-music-auto/`。开发凭据`.artifacts/music-auto-digital-20261018/`，切换凭据`.artifacts/local-music-auto-daily-20261018/`。下方旧“当前”均需结合本节时间阅读。

## 音乐详情排版与日用切换（2026-10-18，上一节点）

维护分支为`codex/music-layout-20261018`，HEAD仍9147b06，继承此前所有未提交音乐外链成果并继续保留为本地工作树修改。用户明确要求最大程度精简截图中的音乐详情并替换本机正式版：专辑头部合并日期/地区/载体/曲目数；平台改成32像素高的紧凑按钮；宽窗口数字版信息与入口同排，窄窗口换行。普通资料站、排序及完整关系来源默认折叠；不同发行版仍保持独立，未知地区不再逐项占行。恢复自动和取消关联归入资料区，已有个人商店页面及全部有效外链保留。没有新增Provider、身份融合或数据迁移。

完整回归4252 passed / 0 failed / 0 skipped，74项定向为重叠子集；新增5项状态回归确保错误、离线回退、空结果、取消和恢复提示不会因精简被隐藏。Release x64构建、实际WinUI浅/深主题与1180/560宽窗口通过，资料区3个信息站链接保留、两种排序保留4个平台，窄窗口按钮无越界/重叠。两组数字版的展开收听区实测255逻辑像素。截图使用冻结公开音乐资料及明确合成的发行/关系夹具，不是国内真实覆盖、实际播放、物理输入或所有DPI的证明。

用户正常退出日用程序后，已安装到原`C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，显示版本保持1.2.0。654份载荷、3份入口附件及更新基包核对，4份未知/路由文件保留；实际安装路径WinUI以全新空Test资料启动并正常退出0，辅助工具默认全关。未打开、迁移Production或复制真实凭据。662份旧程序文件及之前MB外链体验构建的Setup保存在`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-135533-music-layout/`。开发证据`.artifacts/music-layout-20261018/`，安装证据`.artifacts/local-music-layout-daily-20261018/`。未提交、推送、改Tag或发布；下方“当前”均需结合本节时间阅读。

## MusicBrainz统一音乐外链来源（2026-10-18，当前增量）

当前在update-channels-20261016工作树的`codex/music-mb-links-20261018`，基于9147b06继续。保留MusicBrainz既有网易云、QQ、酷狗及国际平台url-rels，不接入国内平台独立API、搜索、授权或代理；偏好只改变排序。当前界面和生产依赖停用主动iTunes目录查找，但保留MB的Apple外链和用户此前保存的独立Related页面。新增原始关系证据及按实体/URL去重，旧缓存可离线重分类，不迁移书架或人工身份schema。

MB官方对streaming/free streaming明确使用合法收听定义，相关入口保留“正版收听 · MusicBrainz 收录”，普通资料及购买关系分别标注，不宣称即时可播放或应用独立核验版权。详见`docs/music-listening-v1.md`。本轮完整回归4247/0/0，84项定向为重叠子集，Release x64构建和实际隔离WinUI三种排序验证通过；国内链接使用明确标记的合成记录，真实播放未测试。证据根`.artifacts/music-mb-links-20261018/`。

源码仍为本地未提交增量，不推送或发布。用户随后明确授权替换本机正式版：本轮已将上述音乐外链修复安装至原`C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，显示版本仍为1.2.0，与GitHub公开453c671及先前8edaa72载荷不同。654份程序载荷、3份入口附件及更新基包核对通过，4份未知/路由文件保留；实际安装目录WinUI在全新空Test资料中启动、辅助工具默认全关、正常退出0。未打开、复制或迁移Production及真实凭据。662份旧程序文件及其Setup保存在`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-132514-music-mb-links/`；证据根`.artifacts/local-mb-links-daily-20261018/`。先前8edaa72切换记录仍保留在下节，不用旧验收代替本次安装证据。

## 1.2.0 已发布；后续音乐入口独立开发（2026-10-18）

v1.2.0 已在 Ph-Rank/The-Order 正式发布，draft=false、prerelease=false、Latest；Tag 与远端 main 对应 453c671d00e16d78cd665e0ec3610b282c2d7778。公开六份附件均匿名重新下载并核对 SHA-256，旧稳定版和 Beta 4 资产保持。完成凭据：.artifacts/release-1.2.0-20261018/publication-complete.json。此前 NextMoe 提示修复 67175a1 已随此版发布。

当前维护仍在 update-channels-20261016 工作树，音乐分支 codex/music-listening-20261018 从上述发布提交建立，仅本地开发、不推送、不发布第二版、不移动 v1.2.0 Tag 或修改附件。根仓库及其他 dirty 工作树保留，Production、日用安装及真实凭据不作为测试输入。下方历史阶段的旧“当前”路径与授权须结合此首节阅读，不能覆盖本轮边界。

已实施：手动对应将普通日期/标题差异与类型、发行形式、卷册层级及明确编号冲突分开；后者可保存有方向的相关作品，不融合身份。有效目标链接可待核验保存；确认、拒绝、恢复自动选择沿用既有身份组件，新链接使用同目录的独立原子封装，既有确认结构与书架 schema 不变。补充 KunGal 链接解析、游戏候选直接操作及封面重试。Bangumi 403 不再清除有效授权；网络与账号状态分开。

正式版完整客户端回归4176 passed / 0 failed / 0 skipped（含本地合成Worker契约）；人工关联及持久化定向43/0/0，更新器/Worker数字版本协议定向57/0/0。最终载荷完成稳定1.1.2/Beta4升级、程序回退、运行中拒绝和退出后重试：6次成功安装、2次预期拒绝，42份合成用户文件字节不变。最终安装后普通 Test 入口启动和关联导航、异常退出恢复通过。所有实验独立AppID/Mutex/路径且无正式安装注册；不是干净 Windows 或真实 Production 的升级验收。证据根 .artifacts/release-1.2.0-20261018，实际系统为 Windows11 x64，完整物理输入、干净Win10和真实OAuth自然到期未验证。

程序语义版本为1.2.0，程序、启动器和安装器FileVersion统一为既有协议要求的1.2.0.0。旧Worker同时校验安装后的程序；曾准备的65535修订号候选会被误报失败，已排除，不可发布。SemVer认定正式版高于Beta，安装器按清单哈希/ignoreversion替换文件，必须用最终载荷验证Beta升级。详见 docs/release/1.2.0/BUILD.md，不得把数字版本差异漏检为单纯删除Beta后缀。稳定/Beta现有安装与物理书架选择规则不变，不静默复制/合并旧Beta资料。

稳定main与ACGN历史已在942da21保留双亲整合；核对稳定启动/通道修复后正常快进推送。源码归档逐文件对照冻结Git内容通过，15项仅Windows checkout与Git archive的UTF-8行尾差异。原始Windows构建清单不可直接当Git归档字节清单，详见source-archive-proof.json。

音乐开发最终完整回归4224 passed / 0 failed / 0 skipped（含本地合成Worker契约）；251项定向是重叠子集，不相加。实际WinUI发现并修复发行版browse缺media、旧group缓存遮蔽数字版的问题；两项反例修复前失败、修复后通过。既有group缓存保留，新列表使用group-v2键，主库与人工映射无schema迁移。实现与边界见 docs/music-listening-v1.md。

最终音乐应用代码为8edaa7207b1f668410b58d6ee086c3d1e8bd20fb，后续提交仅验收测试与文档；publish-dev-qualified/package-dev-qualified为载荷。实际无缓存重新联网找到魔法使之夜数字发行版；清音乐缓存、重启后CLANNAD人工商店选择仍在，原有5份书架/身份文件哈希不变。普通无诊断入口也完成鼠标导航/展开/滚轮浏览，没有打开平台或播放。

独立试用：.artifacts/music-listening-20261018/Start-Music-Samples.cmd仅打开本轮合成Test书架；The-Order-Music-Dev-8edaa72-x64.zip无需SDK、首次为空Test书架，资料在解压目录相邻profile，不附带测试Profile。旧4b69c9d包保留为失败证据，不作为交付。证据与源码提交索引以该目录completion.json/report.md为准。音乐功能没有第二次公开发布或推送。

用户随后明确授权本机日用替换并回复已从托盘退出：2026-10-18已将8edaa72音乐应用构建、9147b06源码检查点的安装载荷切换至原C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe。版本仍为1.2.0，这是本机音乐体验构建，与GitHub正式1.2.0的453c671载荷有区别。654份程序载荷、3份入口附件和更新基包核对通过，原安装目录4份未知/路由文件按元数据验证保留；没有读取、复制、迁移或启动Production资料。662份受管旧程序文件已核验备份至C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-114014-music-local-8edaa72/Program，同级保留原正式1.2.0回退Setup。安装目录实际WinUI使用全新空Test资料启动、默认工具全关、正常退出0。该启动检查不代表真实私库或物理输入验收。证据根.artifacts/local-music-daily-20261018；本次没有远端推送、Tag或Release变更。

## NextMoe 访问状态提示修复（2026-10-16，待下次发布）

当前本地维护分支 `codex/nextmoe-auth-reminder-20261016`，基于已发布 Beta 4 `7bf0ca2` 增量修复。用户明确要求本次不发布、留待下次发布；版本仍为 `1.2.0-beta.4`，不推送、不创建 Tag、不替换日用程序，不访问真实 Production、账号或凭据。根仓库和其他工作树既有修改不动。

已复现：书架用固定的 API 密钥文案解释所有目录访问不可用状态，后台授权刷新失败后，设置页又保留初始化时的登录快照，导致仍显示用户名而缺少恢复提示。现在从同一已提交的授权状态投影书架和设置页：有效账号和匿名公开目录访问不要求 API Key；账号需要恢复时明确指向重新登录；选择 API Key 且密钥缺失时才提示检查密钥；无法读取登录资料时显示独立的访问状态提示。主窗口通过 UI dispatcher 接收授权变化，登录恢复后同步清除过期提示。状态检查不刷新令牌、不增加网络请求。

此修改不改变 DPAPI 文件、Profile schema、账号身份、密钥存取或授权刷新失败后的安全保护，不把“存在用户名”认定为令牌已验证。修复前的合成反例保留在 `.artifacts/nextmoe-auth-reminder-20261016/tests-before/`；后续定向、完整回归和独立 Test Profile 的 WinUI 验证证据集中于同一目录，真人账号授权仍不作为本次验证范围。


验证结果：新增13项合成回归，修复前保留1项实际失败；当前完整客户端4132 passed / 0 failed / 0 skipped（含本地合成 Worker 契约），Release x64 构建成功。实际 WinUI 在独立 Test Profile 下完成6项状态验证：已保存账号无API Key、刷新中断后的登录恢复提示、合成重新登录后提示消失、进程重启恢复、退出后的匿名访问、只有API Key模式缺失密钥时才提醒。程序内渲染截图与对应可访问性树、逐步控制回执已保存。窗口捕获工具返回的桌面画面与目标状态不一致，该画面被排除；未对日用窗口施加输入，不宣称物理输入、完整视觉或真人 OAuth 验收通过。未执行Git提交、推送、Tag、公开发布或安装切换，修复作为本地工作树修改留待下次发布。

## 启动目录兼容修复（2026-10-16，上一发布节点）

用户明确授权修复并公开发布。当前分支 codex/startup-hotfix-20261016，版本 1.2.0-beta.4。正式版与 Beta 分别从各自已发布源码增量修改，不合并两套 Provider，不访问 Production 或替换日用安装。

已用已发布 Beta3 DLL 在合成目录复现：旧 Beta 根不存在时 NativeChannelProfiles.ResolveAt → WindowsAtomicFile.CheckExisting 抛出 DirectoryNotFoundException。修复只针对可选旧目录读取；目录祖先和文件的链接、非目录、访问异常仍拒绝，已保存选择缺失仍 SelectedProfileMissing。已打开的旧 manifest 句柄保持至用户选择完成，书架 schema 及物理身份不变。新增13项启动边界回归及4项新版本通道判定；本轮完整回归4119/0/0，全部使用合成资料。完整测试、实际分发及发布结果见本工作树 `.artifacts/startup-hotfix-20261016/`，不能把构建或模拟通过当真实用户安装验收。

## Beta 3 资料语言修复（2026-10-16，当前）

维护源码 `C:/Users/MUSK/.codex/worktrees/update-channels-20261016/The-Order`，分支 `codex/beta3-language-20261016`，基于已发布 Beta 2 的 `4918d0c` 增量修复；根仓库与正式版分支的既有工作不改。用户已明确授权完成后上传 GitHub Beta 3；这不包含本机日用替换、真实 Production 访问或云端服务部署。

匿名真实 NextMoe `/v2/catalog/works/86` 复现：CLANNAD 的简体中文简介标为 `is_machine:true`，旧显示选择器把资料源译文与尚未启用的本地翻译包混为一类，一律排除，导致中文首选回退到日文。新增仅显示层的 `ProviderMachineTranslation` 来源类型；资料源已有译文参与首选及备用语言选择，同语言的非机器译文优先。可选本地翻译包仍不自动启用，不推断未知语言、不用译名建立实体身份。原字段、缓存结构、人工对应和 Profile schema 不变，已有缓存直接受益。

按用户后续要求，不再展示额外的全文原文对照；公共组件改为「资料语言与来源」，只说明当前字段。简介里的资料源机器译文有简洁来源标记。源码唯一版本为 `1.2.0-beta.3`，文件版本 `1.2.0.3`，仍共享现有安装身份、当前物理书架及 `profileContract:1`，支持 beta.2 更新与回退到兼容正式版1.1.1。

证据根 `.artifacts/content-language-fix-20261016/`：修复前14项合成反例中5项失败，修复后语言／缓存定向156/0/0，版本调整并增加3项更新反例后完整客户端4102/0/0（含本地合成 Worker 契约，非真人账号）。实际匿名响应及当前缓存适配器往返证明 CLANNAD 直接采用既有中文译文，自建单条 Test 书架不含凭据或真实游戏。最终源码冻结、安装包、隔离窗口／安装验证与发布回执以该目录最终记录为准；不把模拟测试等同真人授权或干净 Windows 验收。

隔离安装另复现 Inno InitializeSetup 过早展开 `{app}`，诊断探针的原始异常为 `An attempt was made to expand the "app" constant before it was initialized`。删除此过早调用，原进程路径校验完整留在 PrepareToInstall（写入前）和卸载检查。旧失败日志、首版包及冻结保留；安装器修复后使用新的 final 冻结及分发目录，不重写前一候选。

## 更新通道统一（2026-10-16，前一节点）

本工作树交付Beta 1.2.0-beta.2，与另一通道共享The Order安装身份、入口及所选物理Profile。用户明确授权本轮更新GitHub程序；不授权代理切换本机日用或访问真实Production。旧Beta独立安装策略是历史版本行为，后续两包改为设置内通道切换；旧beta.1需一次手动安装，首次可明确继续旧Beta书架，不自动合并或复制物理绑定资料。

新增有界SemVer、GitHub固定源/摘要校验的update-channel.json、Profile绑定通道偏好与安装目录active-profile.json身份选择。仅新兼容包允许跨通道；稳定用户默认只接收稳定，手选Beta后才发现预发布。Beta回退下限正式版1.1.1。worker先等待精确owner完成保存并退出，再持有原writer锁，原子提交用户资料快照后安装。快照失败中止；不自动恢复旧数据覆盖新增个人记录。

正式桥接版只回移必要nativeExtras v1/v2数据codec和模型；Beta专属配置保留，不暴露或误合并为旧字段。新版无法安全处理的旧式合并/混合刷新拒绝，未来schema只读。两个通道的Galgame/媒体实现不合并成一套未经验证的新Provider。

证据在本工作树`.artifacts/channels`：Beta完整回归4085/0/0（含本地合成OAuth，非真实账号）；稳定完整回归2690/0/0，后加3项稳定回退反例及worker最终快照定点6/0。22项合成库已跨两版本实际codec读写并回验；保留原始失败日志。Release WinUI两包构建通过；最终分发包、隔离安装/更新/回退及公开发布状态以最终回执为准，打包不等于已验收。未读写Production、未替换本机安装、未部署Worker。


## GitHub 首页整理（2026-10-16）

用户授权调整 Ph-Rank/The-Order 默认分支首页并公开添加 Telegram 交流群 `https://t.me/+ONE-Lif0uZBiNzJh`。本轮仅更新 README、演示图片与此记录，不合并 Beta 应用代码、不改已有 Tag / Release / 安装包或用户资料。首页分别指向稳定版1.1.0与已发布的Public Beta1.2.0-beta.1；跨媒体功能明确标为Beta。演示图片来自既有隔离测试窗口，不是Production截图。旧Electron长文改为不可变历史链接，详细使用、构建、验证限制保留在原版本文档与发行说明中。Beta源码Tag `v1.2.0-beta.1` 对应 `50f2b226290bda03e2811a3499a3b8890e95b8d7`，本次主页文档修改不改变该冻结基线。新工作树检出时已有的脚本行尾差异单独保留，不纳入本次文档提交。

## 1.1.0 GitHub 发布记录（2026-10-12）

用户明确授权将已验收的 1.1.0 上传 GitHub。本次公开入口为 https://github.com/Ph-Rank/The-Order/releases/tag/v1.1.0；安装器、完整对应源码、差量块表、校验与原验证报告一同发布。源码保持当前 native 实现、锁文件和必需数据，公开文档更新下载入口与范围；原开发编号 1.0.2 的真实 OAuth／完整回归按实际版本保留。

真实云端写入／删除、收藏夹额外权限、Win10 实机、真实安装升级和长期稳定性仍未验收。同步默认关闭；双向模式后台本地时长／状态上传仍需手动预览。旧标签／附件与日用程序不变；发布暂存目录不作为维护入口。

## 最新候选 1.1.0（2026-10-12，原开发编号 1.0.2）

用户明确确认将本次新增认证、个人同步、程序更新及 Win10 适配的功能版本命名为 **1.1.0**。维护源码、分支、HEAD 和所有继承成果不变；程序集、Setup／GUI 入口资源、安装器身份、包与差量附件名称和当前说明统一使用 1.1.0。内部应用 ID、安装 AppId、Profile／StoreId、资料格式、公开 1.0.1 与全部旧候选不改。

当前候选和版本调整检查位于 `.artifacts/release-1.1.0-20261012/`，最终入口和条件结论见该目录 `validation-summary.json` 与 `distribution/ACCEPTANCE.md`。原 1.0.2 的完整 2638 项回归、真实匿名和真人 OAuth 只读闭环证据位于 `.artifacts/auth-sync-20261011/`，不改写它们的实际版本。新增检查验证 1.0.1→1.1.0 升级、1.1.0→1.1.1 更新以及拒绝 1.0.3 降级；合成更新预览版本改为 1.1.1。当前说明见 `docs/release/1.1.0/`。

尚未发布、提交／推送或替换正式程序。真实云端写入／删除和收藏夹额外授权未验收；官方替换接口缺少条件更新，后台双向本地上传仍需要手动预览。以下 1.0.2 记录是已保留的实施与验收历史。


## 当前 1.0.2 认证、同步与设置候选（2026-10-11）

本轮在 `D:/Projects/The-Order-worktrees/claude-recognition-rnd/native`、`codex/library-ui-refinement`、HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加继承 dirty 上继续实现，没有重置、整体提交、推送、修改公开 1.0.1 或安装日用更新。保留 Win10 17763 能力守卫、低模糊横幅、差量下载与按文件安装、原识别／会话和全部个人数据格式。下方旧候选、失败记录及证据均保留，不代表当前入口。

新增匿名／账号／API Key 三种明确访问模式：匿名不携带认证头，旧 DPAPI Key 保留；系统浏览器 PKCE 与随机 IPv4 环回端口完成账号授权，刷新串行轮换并原子记录未知状态，失败要求恢复登录。Client ID `2df77ee1e0167493048a94b677c73e10` 已经官方授权端点与无效对照验证为有效应用，不是仅从截图猜测。正常登录 scope 为 openid/profile/catalog:read，收藏夹按用户选择另外授权。

个人同步默认关闭、独立于登录。时长、状态和收藏夹提供按类别的读取、预览、确认导入／上传；评分只读、独立展示，本地会话和小数评分保留。账号／Profile／客户端基线、独立收藏映射、真实游标、完整删除快照、UUID POST 日志与 207 逐项结果保护；未知任务同内容重试或明确核对后结束，不静默新建任务。常规自动化仅使用合成账号与资料；真人授权后仅在专门隔离 Test 中验证获准的只读接口，不使用 Production 或真实游戏。

**官方接口限制：时长／状态替换写没有 compare-and-swap。** 三方比较和提交前重读无法使两侧提交原子化；双向模式的后台本地替换改为提示手动预览，安全云端新增可自动导入，删除不自动传播。严格无覆盖的全自动双向替换尚不能承诺；受控替代方案通过合成验证，不能标为全自动能力通过。

设置按用户最新要求统一左对齐，移除重复书架视图、当前材质生效与系统策略说明和不必要的小字；首页网格／列表保留。同步冲突、额外权限、删除说明在预览或确认时显示。常规读取、错误、进度、更新内容与操作入口保留，窄窗口控件与按钮可收缩／换行。

新输出根 `.artifacts/auth-sync-20261011/`：真实匿名 `anonymous-live.json`（3 GET/200/无 Authorization）；完整回归 `tests/full-release-windows-bound.trx`（2638 通过／0 失败／0 跳过）；最终原生 XAML Test 状态／明暗／小窗口流程 `ui-acceptance-windows-bound/summary.json`（11 项通过）。用户已完成系统浏览器登录并确认连接成功、正常退出；`oauth-real-readonly.json` 记录新进程一次刷新、相同账号、目录／时长／状态／评分只读 200、本机退出及退出后私人访问拒绝，隔离书架字节未变，真实云端写入为 0。没有请求收藏夹额外权限，真人收藏夹读取／写入未验收。当前产物和结论以 `validation-summary.json`、`distribution/ACCEPTANCE.md` 为准。最终同步伴随文件 schema 2 绑定 Windows SID／ProfileId／StoreId；早期本轮未发布的 schema 1 测试文件拒绝直接借用。Root RenderTargetBitmap 不能完整捕获系统透明／组合层，不将它计为像素美观验收。最终候选未公开发布，未签名；Win10 实机、真实安装升级、真实云端写入／删除和长稳未执行。复现与隐私见 DEVELOPMENT、`docs/release/1.0.2/BUILD.md` 和 AUTHENTICATION.md。

## 当前 1.0.2 差量更新候选（2026-10-11，包含程序更新与 Win10 适配）

用户要求从下载完整新包改为比较版本差异并定向修改。本轮在已授权的本地 1.0.2 候选上实现差量下载及按文件安装，仍未发布、提交／推送、替换日用安装、访问 Production／个人密钥或启动真实游戏。权威维护源码仍在 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`；继承 dirty 工作及此前输出保留，产物目录不作为开发入口。

发布工具为每个新 Setup 生成版本化 SHA-256 块表；安装后仅在托管程序目录留一份 `update-base.bin`，由普通卸载记录管理。用户点击更新后比较当前缓存与新版块表，通过 GitHub Range 只下载变化块，再合成并校验完整新安装包。支持跨版本比较，不要求逐版本升级；没有缓存、块表异常、差量收益不足或 Range 不支持时只回退一次全包。取消停止下载与合成，失败不写入运行中的程序。更新来源、完整包哈希／PE 身份、保存／退出／安装交接和下载安全标记保持；后台仍仅检查、不下载。

新安装器根据已验证载荷逐文件编译 SHA-256 条目，相同文件跳过，缺项或不同内容才新增／替换；不删除清单外的额外文件。所有预期目标及父目录拒绝链接，快捷方式、卸载记录和资料兼容行为保留。为改善差量复用改用独立压缩流，完整 Setup 比此前固实压缩包稍大；差量仍需本地重建完整安装包及保存单份基包的磁盘空间。

本轮 **109 项针对性测试通过、0 失败／跳过**，覆盖块表边界、跨版本与内容移位、复用与整包校验、缓存／网络异常回退、取消、更新状态及 Win10 守卫。真实 Inno 合成文本安装探针 **12 项通过**，验证未变文件时间保持、变化替换、缺项新增、重复／修复、缓存复制、额外文件保留及目录链接拒绝；探针不注册系统卸载信息，不安装真实应用。公开 1.0.1 的实际 64 KiB Range 请求通过；最终 GUI `--validate-only` 经系统 PowerShell 5.1 校验 **657 份载荷**，未打开应用或资料。

两份内部 1.0.2 构建实测：完整包 **79,209,628 字节**，差量含块表 **5,439,527 字节**，复用 **73,890,172 字节**、23 个 Range；重建 SHA-256 与新 Setup 完全一致。该对比不是已公开版本的真实升级，后续下载量随改动变化。前轮 worker、命名事件、界面和兼容证据仍分别在下方目录，本轮没有把这些旧检查重复计数。

最新输出 `.artifacts/delta-update-20261011`：`publish-final`、`package-candidate`、`installer-candidate`；交付入口为 `distribution-final/The-Order-1.0.2-Setup-x64.exe`，SHA-256 **AE1791B91917D2EB10D451FB00F70ECD724F9AF7B7EFC0257CAB4617F0AE4C4D**。同目录含 `.exe.blocks.json`（120,071 字节，SHA-256 **EB85D152F570EA6571662E042FF43013E4F5C7A1CEC2C2EE4F3C724056A85B93**）、完整对应源码、校验及验证摘要。发布差量能力需同时上传 Setup 和该块表；没有执行上传。之前 `self-update-20261011` 和 `win10-compat-20261011` 的包属于历史候选。

**真实应用覆盖安装／升级／重启、Win10 实机、长稳和完整历史回归仍未验证。** 当前宿主 Windows 11 10.0.26300.0；仅运行针对性及合成验证。安装包未签名；没有绕过系统保护。公开版本仍为 1.0.1，其首次迁移需完整安装含更新功能的新版本，之后才能差量更新。维护细节见 DEVELOPMENT 与 `docs/release/1.0.2/BUILD.md`。

## 前轮 1.0.2 程序更新候选（2026-10-11，已由差量候选替代）

用户授权加入设置内检查、下载并升级安装，以及后台只检查、轻提示和主要升级说明。本候选仍是未发布的 1.0.2，替代下方仅含 Win10 适配的本地包。维护源码仍在 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`，分支 `codex/library-ui-refinement`、HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加继承 dirty；没有整体提交、推送、替换日用安装、访问 Production／个人密钥或启动真实游戏。

Production 初始化后延迟 15 秒静默检查，再每 12 小时检查一次；发现新版仅在设置图标右上角显示 6px 提示点，不弹窗、不自动下载，后台失败不覆盖已有更新状态。设置直接显示发布者的主要改动（最多六条），可展开完整说明；手动检查取代在途后台检查，迟到结果不能覆盖新结果。更新 feed 固定为 `Ph-Rank/The-Order` 的 GitHub latest 正式发行，不需要 GitHub 登录或 NextMoe 密钥。

“立即更新”下载明确的 Setup 附件，检查名称、来源、大小和 SHA-256，保留下载安全标记；下载支持取消和重试。安装前处理草稿并保存游玩记录，准备独立 PowerShell 5.1 worker，仅当应用发出保存完成的命名事件且准确的父 PID／创建时间退出后才开始静默升级并重新打开。取消、保存失败或未提交的异常退出不安装；Test／Development 禁止安装。使用资料格式与识别规则不变，Win10 1809 x64（17763）的能力回退和 CPU ONNX 适配保留。公开 latest 当前为 1.0.1，候选不会降级。

Release 构建／publish 无警告错误；本轮 **72 项针对性测试通过**，包含版本与来源验证、下载失败／取消、保存交接、后台与手动竞争、更新说明和 Win10 能力守卫。系统 PowerShell 5.1 的实际 worker 校验 18 项、命名事件／父进程退出探针 4 项、隔离 Test 界面状态检查 18 项、最终入口检查 27 项通过；实际下载现有公开 1.0.1 安装包并核对 SHA-256，保留 MOTW，合成书架未变。这些不相加为全量回归，不代表真实安装升级成功。界面 PNG 为 RenderTargetBitmap，透明／原生合成层捕获不完整，只作结构参考，不声称原生像素验收。

最新输出 `.artifacts/self-update-20261011`：`publish-final-2`、`package-final`（657 份清单文件）、`installer-final/distribution/The-Order-1.0.2-Setup-x64.exe`。Setup **72496776 字节**，SHA-256 **CC85F7CAFA35352011C6DD16D28B25ABE92A15DD2C2A1D3706C9967C24E965A9**。交付包、完整对应候选源码和校验见 `distribution-final`；产物与证据绑定在 `validation-summary.json`。此前 Win10 阶段输出、失败记录和早期更新候选均保留，不能作为最新入口。

**未运行真实覆盖安装、Production 升级或 Win10 实机验收，也未重跑全部历史回归和长稳测试。** 本轮不要求用户操作；日用安装保持。安装包未签名，系统安全提示仍可能需要用户确认，不绕过防护。1.0.1 不含程序内更新，需要先安装包含此功能的新版本；未来发布仍需用户授权。构建与边界见 DEVELOPMENT 和 `docs/release/1.0.2/BUILD.md`。

## 当前 1.0.2 Windows 10 兼容候选（2026-10-11）

用户授权在没有 Win10 实机／虚拟机、无人值守的情况下完成兼容开发及联网研究。本轮未发布 GitHub、替换日用程序、安装／卸载用户程序或读取 Production、个人密钥。权威维护源码仍是 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`，分支 `codex/library-ui-refinement`、HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加继承 dirty 工作；不要用发布暂存或本轮输出目录继续开发。

最低运行版本、编译 API 检查、启动提示及安装器门槛统一为 **Windows 10 1809 x64（17763）**；Windows SDK 引用仍为 26100。Mica 只在 Win11 且能力支持时启用；Win10／附加失败使用主题色窗口背景，自定义标题栏不可用时保留系统控制。Win10 等宽字体使用 Consolas，动画变化事件按 19041 保护，旧版本依靠原有系统通知刷新。详情低模糊、无折射／色散玻璃独立于窗口 Mica，标准材质及系统关闭效果的回退保留。

横幅文字检测改用已随包提供的固定 CPU ONNX Runtime 2.1.74，避开旧 Win10 内置 WinML 无法运行原 opset-15 模型的问题。模型、评分与识别规则不变，不下载提供程序。平台消费者按锁文件自包含同版运行时；取消／释放按串行评估收尾。合成图片烟测还发现并修复 `local-banner-<SHA256>` 被缩略图缓存拒绝的问题，只增加该固定命名空间，不放宽路径校验。1.0.1 的识别进度、连续核对、移除、卸载选项、黑白图标及继续游玩小封面卡片保留，资料格式未迁移。

实际宿主是 **Windows 11 10.0.26300.0**。Release 构建、最终 publish、完整载荷与 Inno 编译通过；三份 TRX 共 619 个运行结果，按 TRX testId 去重 **604 项通过、0 失败**，不是历史整版全量回归。另有最终入口检查 27 项通过，以及四种窗口能力场景、明暗主题、标准／Auto 材质的 **74 项检查通过**；累计 1406 个采样帧未出现可见但未就绪的详情首帧。合成横幅实际解码为 800×300，Auto 路径存在 GPU 绘制与 σ1 模糊，不再以缺图回退冒充成功。RenderTargetBitmap 不包含原生合成面板和系统标题栏，截图不能当作这些像素的视觉验收。

输出根目录 `.artifacts/win10-compat-20261011`：最终程序包 `package-final`，Setup `installer-final/distribution/The-Order-1.0.2-Setup-x64.exe`，SHA-256 `12C3AF2BC03418742C38A29806E1765D7ABA7EB64026187F87A00E581B8B163C`（72462328 字节）。交付副本、完整候选源码 ZIP 和校验文件在 `distribution-final`；实际验证及产物绑定见 `validation-summary.json`、`test-union-final.json`、`delivery-validation.json`、`fallback-smoke-final-2/summary.json`。GUI `--validate-only` 已调用系统 Windows PowerShell 5.1 校验最终全部载荷，未打开正式资料。早期失败／缺图记录保留，后续结果仅以 final 路径为准。

**没有真实 Win10 安装、文件选择器、驱动／缩放或长稳验收**；Win11 上关闭能力的模拟不能等同 Win10 通过。本轮也未验证真实 API／游戏，未运行交互安装器或重跑全部历史测试。安装包未签名，公开版本仍为 1.0.1。后续发布、用户安装和实机测试分别需要相应授权；当前用户无需参与操作。构建及官方依据见 `docs/release/1.0.2/BUILD.md`。

## 当前 1.0.1 已公开发布（2026-10-11）

用户回复“可以了，更新 GitHub 平台”，已明确授权本次源码提交、发行及下载说明更新。公开发行 https://github.com/Ph-Rank/The-Order/releases/tag/v1.0.1 已标记 Latest，README 推荐直接下载 Setup.exe。v1.0.1 固定提交 a4c82decf8fc44d106afee86542a13da9919d2cc，tree 2ad653090dd278bf3ac63a64cc9d428cb5d9f071；原 v1.0.0 标签与附件保留。

上传的是用户确认的 home-progress-20261011 安装包，没有重新编译或改动；Setup SHA-256 12092E7B8DB3D077056D6A4C434BC020D9437CFD934712C13079FAA100C2B4BB，72419445 字节。完整源码含安装器，669 份文件和清单，源包 SHA-256 0B216C68EA6B894C0A9520AA565961C5CC1202E2FA3456860E818E41F4808B0E。四份公开附件（Setup、源码 ZIP、SHA256SUMS、ACCEPTANCE）均回下载比对大小与 SHA-256 一致，源码归档清单逐文件一致；223 份可验证编译源码与 PDB 文档哈希一致。打包时 release-identity 的 STAGED_NOT_PUBLISHED/sourceSnapshot:null 是历史构建记录，未重写已确认的安装器，公开身份另由发行页和源码清单说明。

170 个前轮针对性用例和后续 49 项存在重叠，不相加，不声称全量回归通过。用户确认提醒、移除交互和继续游玩布局，并同意发布；真实 API／游戏识别、用户安装卸载、独立干净 Windows 和长期稳定性仍不计入自动验收。未签名和可能的下载信誉提示如实保留。

维护源码与继承 dirty 成果保持，未整体提交开发工作树。发布暂存 .artifacts/github-publication-1.0.1-20261011/repository 仅用于公开快照，不作为开发入口；完整发布凭证 publication.json、public-download-verification.json 和 published-release.png 在同一输出目录。未操作 Production、个人凭据或真实游戏，未自动安装日用版；本次授权不扩大到未来任意发布。下方“尚未发布”反映候选阶段。


## 当前 1.0.1 首页进度与继续游玩修复（2026-10-11，替代上一候选）

用户继续反馈自动识别排队数恒为 0、首页看不到进度，以及继续游玩把竖版封面放大成模糊横幅。本轮仅针对这两项修改并验证，版本保持尚未发布的 1.0.1，继承前轮七项修复。

统计原因是此前只读取 RecognitionQueue，遗漏尚未送入两工作线程的外层 automaticTargets；识别结束后的资料复核、写入和图片准备也被过早算作已处理。现自动批次保留完整总数，排队包括外层等待项，处理中包括复核／应用／图片准备，已处理只在该项流程结束后增加。重复加入不重复计数，追加任务更新总数，取消归入取消项，新批次不继承上轮总数。设置与首页显示同一份统计；首页新增进度条、阶段说明和取消入口。

继续游玩改为 84×112 的书架封面与右侧标题、游玩信息和操作，使用普通主题卡片，不再将封面作为宽横幅放大／模糊，也不建立该区域的 ArtworkStage 材质。详情页横幅和玻璃不变。空图使用既有占位，离开页面释放图片请求。用户已回复“布局合适”；这里只确认独立合成 Test 的布局，不代表实际游戏识别已核验。

AutomaticRecognitionTests 与 RecognitionPresentationTests 49/49 通过；覆盖外层排队、重复和追加、取消后重试、新批次重置，以及图片准备期间仍处于处理中的情况。Release publish、完整候选打包、安装器编译、654 项启动清单校验和独立 Test 启动通过；没有重跑全部历史回归，没有访问正式资料、启动游戏或替换日用程序。实际游戏识别进度仍待用户安装后核验。

新 Setup：.artifacts/home-progress-20261011/installer/distribution/The-Order-1.0.1-Setup-x64.exe，SHA-256 12092E7B8DB3D077056D6A4C434BC020D9437CFD934712C13079FAA100C2B4BB。测试证据 home-progress-20261011/tests/home-progress.trx；日志 .artifacts/home-progress-*.log。Test 入口 home-progress-20261011/Start-The-Order-Test.cmd 使用本轮新建 visual-test（三条合成条目，仅一项包含本轮空白合成安装目录）。此新包替代上一节的本地候选；未上传 GitHub、未提交或推送，旧候选和记录保留。

## 当前 1.0.1 本地修复候选（2026-10-11）

用户授权修复七项实际使用问题，并要求尽快完成针对性修改，省略耗时的历史基线复核。维护源码仍在本工作区 native，保留原继承 dirty 状态。候选版本为 1.0.1，尚未发布、提交、推送或替换日用安装；未打开 Production、读取密钥或启动真实游戏。

- 保存 API 后自动补识别已导入但未关联的本地条目，等待正在收尾的导入批次后重试；使用原安全自动识别规则，歧义／版本冲突仍待核对。补全缺失资料也可重试旧空建议；成功后清除失效旧建议。
- 待核对确认或手动关联成功后进入下一项，队列结束返回书架；失败不跳转。
- 候选列表上下边界继续滚轮时转交外层滚动，并启用原生滚动链。
- 书架右键、详情更多及选择模式提供单项／批量移除。确认后原子删除书架资料和记录，不删除游戏文件；活跃计时及事务保护保留。
- 交互卸载询问保留或删除默认 Production 使用资料，默认保留；静默卸载保留。清理拒绝目录链接，不影响真实游戏、独立 Test／Development 和手动导出备份。
- 桌面／开始菜单快捷方式按系统主题：深色白色，浅色黑色。安装时、启动时及应用运行中的系统主题变化同步；程序完全退出期间不另设常驻进程，下次启动再同步。只改指向本安装程序的已知快捷方式。
- 未配置 API 时书架首页显示提示，设置提供 developer.nextmoe.dev 获取入口，保存后自动继续识别。

170 个不重复的针对性用例通过（usability-2/3/4.trx 去重）；Release 构建、publish、安装器编译、完整包与 GUI 启动入口校验通过。临时 .lnk 黑白切换和属性保留、合成资料清理及目录链接拒绝通过。未重跑完整历史回归、干净 Windows 或长稳测试；真实 API 批量识别、滚轮手感、安装后的桌面图标和交互卸载仍需用户实用核验，不能合并成整版 PASS。

已用三条虚构资料打开独立 Test，用户回复“已检查，这部分合适”，仅确认首页提醒及单项／批量移除。旧 experience-profile-v2 已删除，没有恢复旧私人资料。当前 Test：.artifacts/usability-20261011/visual-test-3；入口 .artifacts/usability-20261011/Start-The-Order-Test.cmd。初次合成种子使用 localCover:null 被校验拒绝，改为省略可选字段并经过 codec 正规化后成功；两个失败种子未作为验收资料。

交付 Setup：.artifacts/usability-20261011/installer-final/distribution/The-Order-1.0.1-Setup-x64.exe，SHA-256 FD2C120A56C20B70AB073C00A8EEB4051DA479229D991C1FA8036495B6B5921E。完整候选 package-final、验证摘要 validation-summary.json 和本轮日志均在 usability-20261011 及 .artifacts/usability-*.log。包仍未签名，历史 SmartScreen 状态没有被宣称已消除。原 1.0.0 公开发行保持不变。

## 当前 1.0.0 安装版与下载信誉检查（2026-10-11）

用户要求提供可直接下载的 Setup.exe，并明确授权将已验证的安装包、安装器源码补充和校验文件上传至现有 1.0.0 发行页，更新发行页及 README 的推荐下载；原 ZIP 和标签保留。当前公开仓库为 https://github.com/Ph-Rank/The-Order ，发行页 https://github.com/Ph-Rank/The-Order/releases/tag/v1.0.0 。新增 The-Order-1.0.0-Setup-x64.exe（72423273 字节，SHA-256 F3F75D8AAE79147022D422CE3580563AB446D5FEF97E9A4DECA9696F5E12E8ED）、The-Order-1.0.0-installer-source.zip 和 INSTALLER-SHA256SUMS.txt；三份公开下载均已核对大小和哈希。原五份发行附件、v1.0.0 标签及其冻结源码身份保持。

安装器采用 Inno Setup 6.7.3，仅为当前用户安装到 %LOCALAPPDATA%/Programs/The Order，创建桌面／开始菜单快捷方式和卸载入口。新增无终端窗口的 TheOrder.exe 调用原 launch.ps1；原 655 份应用文件全部逐字节一致。运行中拒绝安装／卸载，不强制结束用户程序；拒绝资料目录、联接及未受管理目录，修复仅恢复受管理程序，卸载保留资料和未知用户文件。新增维护源码在 native/installer 和 native/scripts/package-native-installer.ps1、test-native-installer.ps1；安装器对应源码补充与原应用完整源码 ZIP 合起来对应安装版，不能用发行包或源码恢复作为开发入口。

.artifacts/installer-20261011/validation-final/summary.json 记录 49 项本机安装、修复、卸载、快捷方式、文件一致性、边界保护及合成 Test 启动检查 PASS。使用本轮新建合成 Test，原 experience-profile-v2 已按清理授权删除，不恢复旧库；没有读取 Production 或执行真实游戏。启动烟测后仅结束自己创建的空白 Test 实例，正常退出未在本轮重测。独立干净 Windows、长期稳定性和本轮用户安装视觉确认不计入该 PASS。

用户反馈 Edge“通常不会下载”提示；公开下载、完整程序文件和新增入口经本机 Microsoft Defender（定义 1.459.669.0）自定义扫描，三个退出码为 0，均未发现威胁。安装包未签名，本机没有可用的有效代码签名证书；该提示属于下载信誉问题，不能直接认定病毒，也不能凭本机扫描声称微软认可或风险消失。未关闭／绕过系统防护、安装自签名信任或向外提交私人信息。微软反馈页的说明由用户如实填写，验证码和最终提交由用户处理；尚无微软审核结果。后续可信签名或 Store 分发需身份、服务和独立发布准备，本轮未冒称完成。证据及未通过／未验证边界见 .artifacts/installer-20261011/report.md、publication.json 和 defender-scan-results.json。

维护源码仍是 claude-recognition-rnd/native 的原分支和 HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加继承 dirty 工作；本次公开文档提交仅涉及 README，未整体提交工作区、移动标签、升级应用或依赖。下方历史阶段的入口和资料路径按相应时间点理解。

## 当前本机清理与资料清空已完成（2026-10-11）

用户在 1.0.0 发布后明确要求清理本机 The Order 历史无用版本及信息、清除全部使用资料，准备从 GitHub 重新下载；并补充“旧源码工作树和本机发行副本也清理”。这次明确授权覆盖旧 Production／Electron 资料、原 experience-profile-v2、缓存、加密凭据及私人备份的删除，优先于下方第 17 条的旧资料保留要求；不授权读取／解密／输出密钥、删除真实游戏文件或影响公开 GitHub 仓库。后续新资料仍适用原保护边界。

历史候选、冻结源码副本、发行 ZIP／暂存 clone、旧构建依赖、旧工作树和清单内临时文件已删除；原 Start-The-Order-Test.cmd 已随其失效的测试资料入口删除，不能再按下方历史路径启动。当前维护源码仍是 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native、codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加继承 dirty 成果；长期仓库和 Git 分支／标签／提交保留，未集成、提交、推送或重建程序。保留 1.0.0 少量验收记录、源码清单及 .artifacts/github-publication-1.0.0-20261011 下的发布凭证；其余历史 artifact 路径仅是历史引用，不能视为本机仍存在的文件。SDK 和生成依赖需按 DEVELOPMENT 的原安装／恢复步骤重新准备。

用户先删除四处受保护目录，再确认永久删除回收站七项。最终复核：267 个原清理目标均不存在，回收站 The Order 残留为 0，应用运行实例为 0；正式／Test 资料、缓存、加密凭据和私人备份已清空。1093 份维护源文件的前后哈希核对仅有两仓库 BRIEF／DEVELOPMENT 清理记录变化及失效 Test 启动脚本删除，程序源码未改变；两个 HEAD、全部用户分支和标签不变。Codex 内部 capture／checkpoint 引用有变化，但对应 tree 对象仍存在，不与用户分支／提交历史混同。清理及源码保护复核为 PASS，详见 .artifacts/cleanup-20261011/final-verification.json；result.json、recycle-cleanup.json 和初步检查保留为历史状态。自动审批拒绝与 SeSecurityPrivilege 失败未被改写成成功。真实游戏文件、公开发行和以后新资料不在本次删除范围。

## 当前 1.0.0 已正式发布（2026-10-11）

用户在完成首版本机验收后明确授权“完成上传任务并更新记忆”，并说明此前没有 GitHub 仓库。本轮已在用户账户创建公开仓库 https://github.com/MuskTesla/The-Order ，正式发行 https://github.com/MuskTesla/The-Order/releases/tag/v1.0.0 ，Release ID 409555394，published_at 2026-10-11T09:03:28Z，draft=false、prerelease=false。该授权仅覆盖本次必要的公开源码提交、发行上传与记录；不改变 Production、日用程序、真实游戏及继承 dirty 工作的保护边界。

v1.0.0 对应源码提交 f8d0115f097894e0b86a1415f6262b13ca6def4d，Git tree 9f963eab975415514e8f4692ae5e5411dddd483c。标签中的 653 份源码、SOURCE-MANIFEST.json 和 PUBLICATION.md 共 655 个 Git 文件逐 blob 核对一致；653 份完整源码清单 SHA-256 为 2A5E9B4678AC5213BD53D13AF5E0D7DD01D9D3A8A8E824FBA540E1DCD3790952。其后 main 的 bd8062aebc07f751b2cca9946caaa88dbd5018da 只更新 README.md 和 PUBLICATION.md 的下载与实际发行记录，发行标签和完整对应源码 ZIP 未改变。

五个发行附件（完整程序 ZIP、完整源码 ZIP、README.md、ACCEPTANCE.md、SHA256SUMS.txt）均已从公开地址回下载，大小与 SHA-256 全部相同。程序 ZIP 保持 A0869195D086A3FE831724308B51CF19E76CA6138744C6BF772AF4BC57900159，源码 ZIP 保持 1484FF4C7446C7EE0888675A05041ACB50E7842A5C1DE1580493DCDF64ADDF18。公开附件不包含个人资料、缓存、私人检查点或真实密钥。实际发行状态为 PUBLISHED；发布证据统一在 .artifacts/github-publication-1.0.0-20261011/publication.json、public-source-verification.json、public-download-verification.json 和 published-release.png。

维护源码仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，codex/library-ui-refinement，HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加原继承 dirty 成果。D:/Projects/The-Order 尚未集成；发行暂存 clone、源码 ZIP、冻结恢复目录和 GitHub 标签都不是本轮新的开发入口。没有 reset/clean/stash，没有在原开发仓库提交或推送，没有替换日用程序、打开 Production、执行真实游戏或操作正在运行的普通 Test 窗口。原 Start-The-Order-Test.cmd 仍固定 1.0.0 和既有 experience-profile-v2。

已通过的 2413 项完整回归、27 项启动入口检查、60 分钟持续操作及用户本轮显示确认保持原范围。用户明确不在发布前重验干净 Windows，并把长期稳定性留待发布后验证；两项没有改写成 PASS，持续高频操作中末段约 +0.315 MiB/min 的缓慢私有内存增长继续保留。源码、程序及附件身份不能只用原 dirty HEAD 代替。下方全部 18 条有效要求继续保留，后续要求优先；下方发布前 STAGED_NOT_PUBLISHED 文本仅是相应时间点的历史状态。

## 历史 1.0.0 首版发布前候选（2026-10-11）

用户已确认最终 v24“已检查，本轮显示合适”，随后明确要求完成正式第一版发布前的其他工作。本轮仍从 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native 开发，codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加全部继承 dirty 成果；长期 D:/Projects/The-Order 尚未集成。不得从源码恢复、发布包或长期仓库旧副本继续开发，也不得整体盲目集成这份 dirty 快照。

版本统一为 1.0.0／文件版本 1.0.0.0，设置页显示版本号；依赖锁文件和用户已确认的 v24 图片／玻璃／首帧行为不变。仅简体中文，其他语言既有实现保留。本轮增加正式／Test 启动入口、完整性校验、发行说明、构建说明、固定依赖许可对应表与完整对应源码供给。图标继续使用：用户确认已有公开分发授权，并要求表述为“借鉴参考《9-nine-》中的 The Order”；应用源码许可不改变原游戏美术的权利。

根 Start-The-Order-Test.cmd 和 multilingual 同名历史入口固定 .artifacts/first-release-20261010/launch-1.0.0.ps1 → candidate-1.0.0。此维护入口仅打开原 experience-profile-v2，ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e；诊断、Advanced 资格与请求控制器关闭。发行包中的 Start-The-Order.cmd 为最终用户的正式入口；代理仅做 ValidateOnly，不打开 Production。发行包 Start-Test.cmd 的默认空白试用资料不属于本轮验证范围。本轮用户再次限定“仍只使用本机既有隔离资料”，未启动 TheOrder-RC-Win11 虚拟机。

完整 Release 回归 2413/2413、0 失败／跳过；Windows PowerShell 5.1 实际启动校验回归 27/27。锁定依赖恢复／漏洞查询未报告已知漏洞，不等于永远没有漏洞。程序使用自包含 x64 .NET／Windows App SDK／VC 运行依赖，依赖版本与许可在包内 dependencies.json、licenses/。已执行 10 个横幅／Opaque 场景的 849 个合成帧状态采样，802 个可见样本中未出现未就绪状态；31 张合格 CG 的常用宽窗布局全部横向铺满、信息仍在图内；极窄窗下移和缺图／挂起／迟到完成等回退检查通过。合成帧状态不是像素录像，高对比度注入不是新包的实际 OS 高对比度验收。

验收、持续运行和重启的完成记录及精确文件身份以 .artifacts/first-release-20261010/report.md 为准，不能仅据构建、局部显示确认或有界图片缓存宣称全部进程／所有环境通过。新包未在独立干净 Windows 上重验；较早 Phase 7 的干净宿主证据仍单独保留，不扩大为本版的新验证。最终普通入口的用户显示回复也记录在该报告，未回复不能当通过。

source-1.0.0/、source-1.0.0.json、source-1.0.0.patch 是完整 653 文件源码，包含继承成果；task-delta-1.0.0.json 相对本轮保留的 647 文件基线标出 15 个任务变化文件，不是把全部快照当单任务补丁。完整快照包含 docs/release/1.0.0 的三份发行文档。delivery/ 提供程序 ZIP、完整源码 ZIP、SHA256SUMS.txt 与验收记录。包内无个人资料、游戏图片缓存、真实密钥、游戏文件或测试工具；闭合私人资料检查点只保存在本机 .artifacts，不随发行。

本轮为 STAGED_NOT_PUBLISHED：未提交、推送、公开发布、替换日用程序、操作 Production 或执行真实游戏。正式发布仍需用户明确授权；程序回退只换完整程序，不以旧资料覆盖新编辑。下方全部 18 条有效要求继续保留，后续要求优先。

## 历史隔离候选：简体中文 Test v24、封面恢复与横幅首帧同步（2026-10-10）

维护源码仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加全部继承 dirty 成果。v23 用户已确认“已检查，本轮显示合适”；全宽原比例、罕见下移、无折射／无色散、低模糊 σ1 及现有选图结果全部保留。本轮修复每次重启先旧封面后新图，以及详情页玻璃先闪白／短暂全透明。

根 Start-The-Order-Test.cmd 和 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd 固定 .artifacts/cover-startup-v24/launch-v24.ps1 → candidate-v24-frame-ready。651 项 payload，manifest SHA256 82612135D4414B3B18239076C89ED7F23475849988297AEEF2C1BC389EFBDEBD，WinUI MVID 7107477d-593c-4549-8033-1a5ca2a661b8。普通入口实际 PID 5252／UTC 2026-10-11T05:59:34.5836220Z；诊断、Advanced 资格、请求控制器关闭。仅原 experience-profile-v2 的 37 项 Test，ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e。

启动封面：新增 ICachedCoverRecommendationSource，在 WorkspaceViewModel 发布查询结果前读取经过身份／签名和 CoverSchema 验证的本地推荐。只读本地，不联网、不解码、不 OCR／评分，不等待后台准备队列。手动／本地选择优先；缺失、损坏、无权读取或旧 schema 保留现有资料，较新 Prepared 事件优先，取消／新查询继续阻止旧结果覆盖。查询临时映射不受 128 项事件缓存上限截断，原缓存预算不变。

首帧：第一个 candidate-v24 仅清掉临时白底，用户反馈变成短暂全透明，判为未解决，旧包和证据保留。最终 BannerPresentation 在图片、首个有效绘制及当前文字布局准备好之前保持整个横幅不可见、不可点击，但保留测量；到下一次合成帧复核当前图片／尺寸后，横幅、玻璃及文字一起显示。详情和目录共用此时序；没有空闲轮询或常驻 Rendering 订阅。资料读取失败、无 CG、图片缺失、Opaque／高对比度及渲染失败可进入有效回退，离页取消待揭示回调，旧图片完成不能揭示新页面。没有修改光学着色器或 σ1 等最终外观参数。

验证：212/212 首轮相关 Release 回归及 143/143 首帧相关回归通过，两组去重 277 项；初轮 78 是子集，不累计。最终构建／打包成功。实际 Test 连续合成帧检查含 4 个正常横幅、2 个 Opaque、1 个无 CG、1 个极窄下移，共 8 场景、621 个采样帧，0 个可见但未就绪的样本，最终全部可见；首个可见采样在导航后 128–401 ms。生命周期注入验证图片缺失、挂起图片、离页后取消无效的迟到完成、系统保护及高对比度；均通过。连续帧状态不是逐帧像素录像，主观过渡仍由用户确认。

实际 Ready、37 项、无待保存编辑／计时；CatalogLoads／Analyses／Prepared 为 0，图片错误／过期封面／待处理均 0，最大解码并发 2，解码缓存 67,019,232 bytes <64 MiB。3 个正常详情复核全宽且全部文本框内；封面／横幅选择 URL 0 改变，library.json 和个人字段 hash 不变。用户已回复“已检查，本轮显示合适”，记录为最终 v24 本轮显示通过；检查提示后没有操作窗口，不扩大为全部历史任务或正式发布验收。 这些结论不代表全部历史任务或正式发布 PASS。

证据见 .artifacts/cover-startup-v24/report.md。source-v24-confirmed/、source-v24-confirmed.json、source-v24-confirmed.patch 保存完整 647 文件当前 dirty 源码（含继承成果）；task-delta-v24-confirmed.json 相对本轮 645 文件基线列出 18 个任务变化文件，不是把完整工作区当单任务补丁。v23 与失败首个 v24 包保留。未提交、推送、发布、操作 Production／日用程序、执行真实游戏或整体集成长期仓库；回退只能换程序，不能用旧库覆盖新编辑。

## 历史隔离候选：简体中文 Test v23、横幅构图与无折射轻玻璃（2026-10-10）

当前维护源码继续位于 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，分支 codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加完整继承 dirty 成果。用户要求横幅采用独立审美权重、保持原比例，并随后明确“横向还是要铺满”和文字移到下方只能是极少数保底；不要恢复首个试验候选的低清图居中缩窄策略。用户又明确采用无折射轻玻璃建议：详情／目录横幅文字框关闭折射和色散，保留轻微模糊、背景明暗自适应和细边缘高光；其他历史 Daily／Gallery 实现保留。

普通入口为根 Start-The-Order-Test.cmd（或 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd），固定 .artifacts/banner-aesthetics-v23/launch-v23.ps1 → candidate-v23-clearer。651 项 payload，manifest SHA256 E6860070EFE37026993EEF6D7F6C37E92E28BBC6B83DF5C95714278253A859B7，WinUI MVID 0a4c718f-95aa-4d01-96ce-96315ec04b85。仍仅使用既有 experience-profile-v2 的 37 项隔离 Test；ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e。实际普通入口 PID 19956／UTC 2026-10-11T05:02:41.4007270Z，诊断、Advanced 资格和请求控制器均关闭。

BannerAesthetics（landscape-composition-v1）在原有同作品／可靠版本关系及 OCR 门槛后，按场景结构 25、叠层留白 25、明暗层次 20、有效分辨率 30 评分；约 1600×500 后分辨率封顶。不同于竖版封面评分，不奖励饱和度／肤色，也不是人脸检测或训练过的审美模型。最多 32 张有界准备，近似同画面优先清晰原图，评分与选择写入 BannerSchema 1；CoverSchema 2／MediaSchema 6／ArtworkSchema 4 保持，个人选择不改。未分类封面图库不再充当自动 CG。37 项准备完成，15 个横幅 URL 改变（含 1 张不适合的封面被移除），7 项选图像素增加，31 项有合格横幅；11 张仍受低清源图限制。封面推荐 URL 0 改变，个人字段及 library.json hash 不变。

横幅始终铺满内容宽度，高度随源图比例变化，无强制 16:9／4:3、无模糊上下填充。短横幅先压缩间距、把事实组并排、合并游玩统计行、加宽文字框，再按实测文本是否放得下决定罕见下移；画面主体位置只选择左右，不触发下移。常用 1150 DIP 窗口的 31 张横幅实测全部留在图内、文字无越界；620 DIP 的四个短／普通样例也保持叠层，480 DIP 极窄窗口的超宽 34 样例才下移。材质使用既有 GPU 渲染器，折射深度／色散为 0；依用户最新“模糊度可以再低一点，保留玻璃澄澈感”将 σ2.5 降至 σ1，亮暗可读界限 .30/.10、细边缘 .04；事件驱动复用，不增加滚动评分或轮询绘制。系统保护、Standard／Opaque／高对比度与失败回退保留。

412 项相关 Release 回归、31 项材质／可读性回归通过（两组有重叠）；最终 σ1 再跑 GPU 像素回读，验证零折射位置、框外原图不模糊、7 种背景×两种极性的可读界限及已有资源生命周期。构建／打包成功。最终包实测 Ready、37 项、无待保存编辑／计时，图片错误／过期图／待处理为 0，解码缓存 66,699,568 bytes <64 MiB、并发最多 2，CatalogLoads／Analyses 均 0，复用已有准备。实际布局、主题与普通入口证据以及仍未验证范围见 .artifacts/banner-aesthetics-v23/report.md。已打开最终候选并异步请用户检查，用户回复“已检查，本轮显示合适”；提示后未操作窗口，仅记录本轮显示通过，不代表整体正式发布验收。随后用户报告重启先显示旧封面、稍后换新封面，下一轮继续修复。旧包及失败试验均保留；回退只换程序，不恢复旧库。

source-v23/、source-v23.json、source-v23.patch 保存完整当前源码（含继承成果，不是单独任务补丁），task-delta-v23.json 相对本轮 641 文件基线列出任务变化。未提交、推送、集成长期仓库、发布、替换日用程序或执行真实游戏。

## 历史隔离候选：简体中文 Test v22、构图优先的自动封面（2026-10-10）

用户在确认 v21 本轮封面合适后，指出《魔法使之夜》更喜欢目录默认的紫色构图，要求研究将美观度纳入封面算法；已明确选择“均衡：构图优先，清晰度达标”。本轮继续在 D:/Projects/The-Order-worktrees/claude-recognition-rnd 的 native/，分支 codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加继承 dirty 源码，未集成长期仓库。

CoverAesthetics（bookshelf-composition-v1）先保留 v21 的同作品／封面用途门槛：只比较目录 primary、已验证 VNDB primary 和 Steam library poster，最多 3 张。评分为竖版构图保留 30%、细节保留 25%、裁切边界 15%、画面重心 10%、有效分辨率 20%；达到约 360 像素卡片宽度后，分辨率不再加分。分数相近（4 分内）优先目录 primary，否则 2 分内保留原推荐。复用书架的等比填满裁剪，既不按作品名称写特例，也不把未分类图库的像素／票数作为用途证明。它是轻量构图启发式，不是经过插画偏好训练的审美模型，不保证识别人脸、标题语义或放大的低清图。个人手动／本地封面仍优先。

每部最多分析 3 张、512 边长、64×80 网格，准备阶段写入各项评分／候选签名／选择结果；重启复用，不在滚动时重新评分。派生缓存 CoverSchema 2、MediaSchema 6，ArtworkSchema 4 不变。选择窗口第一项改为“自动推荐”，预览读取实际书架推荐；目录默认图在图库中单独标注，可手动固定。保存前复核推荐未变；尚未准备好则禁用该项保存并说明，手动选择仍可用。详情来源和菜单同步为“自动推荐封面／恢复自动推荐封面”，不再把自动算法结果误标为资料源原图。

实际 Test 37/37 评分持久化，最多 3 个候选，28 个自动 URL 变化；32 张横幅 URL／来源全保留，未重跑 OCR。魔法使之夜目录 primary 720×1080 得 92.70，VNDB 1238×1500 得 93.27，Steam 300×450 得 89.57；前两者都达分辨率门槛且差距小，按稳定规则选目录 primary，不声称它的数值分更高。全部个人字段和 library.json 哈希与基线一致。

623/623 图片／目录／在线／界面相关 Release 回归通过；随后仅补齐命名并通过 51/51 封面／个人编辑／界面回归，两组有重复，不相加。构建／打包成功；实际最终 Test Ready、37 项、无未保存编辑／计时，图片错误／过期图／排队为 0，解码 66,494,704 bytes <64 MiB，峰值并发 2。重启 CatalogLoads 0、CG Analyses 0；持久复用另有单元正反回归。初版失败（2 个旧 schema 断言）、初版包与日志均保留。

最新入口仍为根 Start-The-Order-Test.cmd 及 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd，固定 .artifacts/cover-aesthetics-v22/launch-v22.ps1 → candidate-v22-final。651 项 payload，manifest SHA256 B32327B0E334BCF97EB1538FABBB26D91F93D1CBECD42E0428F617935FD8C62E，WinUI MVID 5199bfea-3ddc-4a0e-9337-27b50bfaffcf；普通入口已实际打开 PID 12888／UTC 2026-10-11T03:48:00.0544070Z，diagnostics／Advanced／controller 均关闭，始终仅原 experience-profile-v2。只启用简体中文，四语言实现保留。

已异步请用户检查；用户反馈《魔女的夜宴》更喜欢下方群像、《西历2236年》更喜欢下方作品封面，并明确“如果能优化算法更好，比较困难目前水平也完全够用”。核对后前者的同类 VNDB 群像已参选（92.45），默认单人图 94.78；后者 68098 未分类图库图没有进入可信候选池。不能把这两例说成算法已符合审美，也不能仅调权重保证解决。补查 VNDB release API 有图片类型，但可靠扩池与主视觉语义尚未实现；本轮保留 v22，不为两个样本写特例或恢复任意图库晋升。记录为用户认可当前可用、两例偏好未满足，非视觉全 PASS；未擅自替用户固定封面。检查提示后没有操作窗口。没有真实游戏执行、Production／日用替换、提交／推送／发布。整体／正式发布仍不是 PASS，旧长期资源、OS 无障碍和干净机器边界不变。

本轮 .artifacts/cover-aesthetics-v22/report.md 保存研究、权重和证据。baseline-source/、baseline-source.json 是开始时 638 份完整源码；source-v22/、source-v22.json、source-v22.patch 为交付完整源码恢复，包含继承成果，task-delta-v22.json 为相对该基线的本轮范围。不得把完整 dirty 副本作为单独补丁盲目集成。回退用旧程序，不能恢复旧资料覆盖新编辑。

## 历史隔离候选：简体中文 Test v21、封面用途优先（2026-10-10，评分及预览由 v22 接续）

用户否定了 v20 的部分自动封面：出现盘面、封底、视觉粉丝书、原声带、PS 包装。v20 的“16 个自动封面改进”仅是当时像素比较，不能继续当作视觉质量已通过。当前修复在同一 dirty native 工作区完成，未改主仓库／Production／日用程序，也未提交推送。

自动封面先限定用途与来源，再比较裁剪后的有效像素。目录未分类 covers 不再按尺寸／票数自动晋升，仅保留给手动选择；目录明确指定的 primary cover 为回退，缺失／被过滤时不拿任意图库项补位。同作品已验证的 VNDB primary image、Steam library poster 用保留的精确 ID 与规范 URL 参与选择；旧扫描图再大也不能阻止切回作品主图。手动／本地选择优先权保持，不改变版本身份或横幅策略。本方案是来源与用途门槛，不是通用视觉分类器，不宣称未来源站指定主图永远正确。

新派生缓存 CoverSchema 1、MediaSchema 5；ArtworkSchema 4 与 CG 算法不变。旧缓存有可靠补充封面直接重选，否则仅恢复当前目录 primary URL，失败清空未核实的自动图并保持可重试，不覆盖个人选择。书架接受空自动图更新，避免沿用旧图。实际既有 Test 37/37 升级完成，30 个自动 URL 变化（不是声称 30 张旧图全错）；32 张横幅地址和来源不变，未重新 OCR。library.json 与全部个人字段哈希前后一致。

最新入口仍是根 Start-The-Order-Test.cmd 和 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd，固定 .artifacts/cover-selection-v21/launch-v21.ps1 → candidate-v21。651 项 payload，manifest SHA256 957805A1AAE094B8A8E41D1DADE179757CB9D75F169852BF20F6CE2BF1217CBB；WinUI MVID 63ed08b7-3d03-47cf-863c-63edf735f696。仅 experience-profile-v2（原 37 项），简体中文，普通入口 diagnostics／Advanced／controller 关闭。已正常打开 PID 22988，UTC 2026-10-11T03:12:28.7134830Z。

579 项图片／目录／在线相关回归、31 项界面状态回归通过（合计 607 个不同用例），0 失败／跳过；publish／package 成功。首次广泛回归暴露历史 Electron oracle 的任意图库回退差异，已明确投影本版规则并保留原 oracle 与失败证据。实际 Test Ready、37 项、无脏编辑／计时，图片错误／旧图引用／排队均 0；解码缓存 66,605,808 bytes，小于 64 MiB。详情见 .artifacts/cover-selection-v21/report.md。完整 source-v21/、source-v21.json、source-v21.patch 含继承成果；task-delta-v21.json 才是相对本轮 638 文件基线的范围清单，不可整体集成 dirty 快照。

用户已回复“已检查，本轮封面合适”，确认 v21 本轮封面显示；确认后不再操作窗口。历史长期资源、系统／无障碍／干净机器验收边界仍未闭合，不能签署整体或正式发布 PASS。下方 v20/v19 仅为历史。

## 历史隔离候选：简体中文 Test v20、图片质量与发布前修复（2026-10-10，封面策略已被 v21 取代）

用户在 v19 后明确授权 12 项修复、参考 PotatoVN 的图片与流畅性策略，以及无人值守的检查、修复、复验。工作继续在 D:/Projects/The-Order-worktrees/claude-recognition-rnd，codex/library-ui-refinement，HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加继承 dirty 源码；界面是 native/ 的 C#/WinUI。长期主仓库尚未集成，不从包或恢复副本开发，不整体盲目集成。

本版采用精确作品关联的 Steam 官方 library_hero 和 VNDB 原图／精选提示，按清晰度与素材设计比例选图，仍进行本地无对话框筛选；KunGal 资料优先、可靠唯一版本关系和个人选图优先保留。Steam 同时关联正式版／试玩版时，只有官方 appdetails 确认唯一正式版及试玩版的精确父作品编号才取图，不接受标题猜配、两个不同游戏或未知关系。37 项 Test 中横幅从 21 增至 32，16 个自动封面更换为更合适的高像素素材。9-nine 从 700×394 横幅改用 1920×620 官方美术，封面 741×1080 改为 1202×1750；樱之诗封面 256×178 改为 600×842。5 部仍无满足当前纯画面规则的候选，不能说全部作品都有高清 CG；本地横幅选择仍可用。

初次启动可从既有安装目录有界列出 EXE/CMD、唯一完整的正常游戏程序自动选择并持久保存；唯一卸载／安装／配置工具仍列出但须明确选择。游玩统计进入标题框，安装设置和本次安装选择进入“…”；来源在正文之后、相关作品之前，来源网站直接外部打开。删除详情重复的完整资料块，发现页居中空状态并准确标注 KunGal 目录／NextMoe 搜索服务，制作人员改为随宽度分列的职位卡片，角色与作品标签共用样式。标题玻璃沿用 Daily 无色散，CG 与玻璃同源对齐并减少整体提白；宽窗 16:9、紧凑窗 4:3，实测所有标题文字在框内。缓存详情先显示、后台准备图片；缓存、解码并发和非递归预取的边界保留。过时的所选识别候选可点击复核，但只接受同一编号的新有效结果，禁止偷偷换候选。

固定候选 .artifacts/release-readiness-20261010/candidate-v20-audit9，651 项 payload；manifest SHA256 E32364149858BD627D24F3138463A5E11CB94644AEE00D66665279E7AE109375，WinUI MVID cc3d02f9-a918-43c6-8259-470e7699ad09。最新入口仍为根 Start-The-Order-Test.cmd 和 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd，转到本轮固定 launch-v20.ps1。仅现有 experience-profile-v2：ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e，37 项。NativeMultilingualEnabled=false，仅简体中文；四种语言实现／资源／历史包保留。

full-release-final7 完整 Release 2359/2359 PASS，0 FAIL、0 SKIP，TRX 218.43 秒；包括唯一辅助程序须明确选择的补充修复，publish/package 成功。37 项派生图片资料准备完成、ArtworkSchema 4；实际 audit8 Test 诊断关闭，Ready、无未保存编辑／图片错误，9-nine 的 ExecutableMissing 状态下启动按钮 enabled、invoke 和 keyboardFocusable 均为 true，未执行游戏。正常宽窗和 480 DIP 明暗主题的标题文字、图框对齐与 renderer 正常；应用内强制实体回退及恢复通过，不等同真实 OS 高对比度切换。audit7 完成 60 分钟、9199 轮 actual XAML，图片错误／过期封面为 0、缓存预算通过；进程 private bytes 四段中位数 663.05→720.16→756.18→776.16 MiB，长期资源仍未签署。audit8 应用二进制完成 10 分钟、1535 轮复验，图片错误／过期封面为 0，缓存预算通过；audit9 与其应用文件逐字节相同，仅修复 PowerShell 5.1 启动模块加载及 batch 失败退出码。根 Test.cmd 实际普通运行 PID 34328、UTC 2026-10-10T23:50:20.3868470Z，诊断和控制器关闭；缺失文件反例退出码 1、未启动程序／未改库。最终记录见 report.md。个人字段与 library.json 哈希相同，没有真实游戏执行、Production、提交、推送、发布或日用替换。用户尚未检查 v20，不能用 v19 的“已检查”、无人回复或代码通过签署整体／正式发布 PASS。

完整当前源码、基线和本轮范围清单位于 .artifacts/release-readiness-20261010：baseline-source/、baseline-source.json，source-v20/、source-v20.json、source-v20.patch，以及 task-delta-v20.json。完整副本包含继承成果，任务差异以 v19 的 620 文件完整基线比较，不能将 tracked HEAD patch 当作本轮单独补丁。失败日志和中间包保持不可变。源码冻结及最终检查记录见 report.md 和 DEVELOPMENT。视觉检查由用户进行；检查提示发出后不得再操作窗口。

## 历史隔离候选：简体中文 Test v19、暗色 CG 标题玻璃（2026-10-10，已由 v20 取代）

本轮最后修正并继承 v18 的封面填满裁剪、日常玻璃无色散、CG 圆角、本地横幅与高分辨率解码。用户比较暗色 Angel Beats CG 时，v18 的浅色主题会把整个标题区域强制提亮，线性亮度下限令细节接近平灰，视觉上与标准材质近似。v19 按标题下方实际 CG 区域估算两种可读处理所需的 tint，选择较少遮盖的文字／底色极性：暗区可用白字、浅区可用深色字，不按作品名称或编号特殊处理；原 Daily 光学参数、色散 0、亮度对比界限、系统保护与失败回退保留。最多 384 点采样，无新图像缓冲／联网／OCR，不新增轮询，布局改变导致极性变化时才重建场景。

v19 当时源码：D:/Projects/The-Order-worktrees/claude-recognition-rnd，分支 codex/library-ui-refinement，HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加继承的 dirty 源码；长期主仓库 D:/Projects/The-Order 尚未集成。当时两个 Start-The-Order-Test.cmd 转到 launch-language-v19.ps1，固定 candidate-language-v19、647 项 payload；manifest SHA256 83D5B258E9F087EFB53D09BB8341E80DA13DF1846129AFABCD84D812780FEB53，WinUI MVID 144754e9-3678-451f-a1df-118cb2d7de3c。该历史脚本仍使用现有隔离 Test experience-profile-v2，日常玻璃、NativeMultilingualEnabled=false；四种核心语言资源／代码未删除。当前两个普通入口已转到本文最前面的 v20。

48/48 定向 Release 回归，无跳过；final publish、package 成功，日志分别为 tests-v19.log、tests-v19/artwork-v19.trx、build-language-v19.log、package-language-v19.log。v19 PID 34812，启动 UTC 2026-10-10T18:54:52.5598566Z；启动确认 Ready、37 项、无未保存编辑／游戏计时，人工检查实例启用原有请求文件控制器但 diagnostics=false、Advanced Test flag=false；普通入口不启用该控制器。用户在检查提示后回复“已检查”（2026-10-10T19:00:19.681Z），仅记录本轮显示检查，不将全部历史任务或正式发布验收升级为 PASS；助手没有再次进行桌面视觉巡检／全仓测试／资源长测。

v19 当时的入口为工作区根 Start-The-Order-Test.cmd，以及 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd；当前入口已转向前述 v20。v19 完整源码恢复为该目录 source-v19.json、source-v19/、source-v19.patch；618 个继承源码文件加本版 2 个新文件，共 620 个，已与 Git 的全部非忽略源码清单核对，包含继承成果，不是可整体套用主仓库的任务补丁。具体测试、来源低清限制、本地横幅伴随目录备份、版本与用户确认范围见同目录 report.md 与 DEVELOPMENT。v19 结束时没有新的具体用户任务；随后用户已授权本页最前面 v20 的 12 项开发和验收。接手读取下节全部有效要求，不自行恢复多语言、无限缓存或替换 Production。


## 用户有效要求与后续代理身份（截至 2026-10-10，优先于旧设计说明）

接续代理身份：GPT／Codex 开发与维护代理，负责用户明确授权范围内的实现、修复、必要的代码验证与可回退交付，不是默认只读评审者。长期源码仓库仍是 D:/Projects/The-Order；当前这批未集成的 native 工作继续在 D:/Projects/The-Order-worktrees/claude-recognition-rnd，不能把历史包／诊断副本当成开发入口，也不能盲目复制整份 dirty 工作到主仓库。先读本文件当前版本和 DEVELOPMENT、实际 AGENTS/CLAUDE，再检查当前差异。用户后续明确修改的要求优先。

以下是用户历次要求的当前有效版本；已实现与未验证要分开说明，不因上下文更换取消，也不把本清单视为已全部验收：

1. 搜索框仅在书架主页按需显示，默认收起；搜索按钮与排序、筛选、视图、选择、导入等工具栏按钮并排。其他页面不得常驻书架搜索。发现输入支持回车搜索；审查其他提交输入的回车行为。发现按钮只叫“搜索”，不叫“搜索／重试”。发现搜索结果使用封面网格，点击后再替换为详情，不保留旁边的常驻详情栏。
2. KunGal 始终为资料优先来源。用户本轮允许比较后改变图片来源优先级，采用更合适、清晰的同作品官方宽幅美术／原图；这不改变资料与版本身份规则。查明仅 Bangumi 的缺资料／版本情况，不能靠页面号猜身份。重制版／其他版本可能资料稀少：保留准确的新版本身份，仅通过可靠、唯一的版本关系补充原版资料／CG，不能把原版的发行、评分、角色事实冒充新版事实。方案必须普适，不只对纯白交响曲；用户给过 KunGal 页面 https://www.kungal.com/galgame/221453。
3. 详情图层不放竖封面。横幅挑选纯画面 CG／官方宽幅美术，排除带对话框／文字 UI 的实机截图及不明用途封面图库；没有合适 CG 时可从可靠关联原版找。最新要求为横向铺满内容区、高度依原图比例，不硬扩展到 16:9／4:3、不因原图低清缩窄居中；横幅使用独立于封面的构图／留白／明暗／清晰度权重。初次挑选可耗时，但在导入／自动识别期间完成；持久保存选择，重开直接复用，不在每次点封面时重新 OCR／挑选。来源版本放在默认折叠的来源内。
4. 标题、状态、制作公司、发行、引擎、评分等在 CG 图层上方叠放，用户“上方”指图层而非位置。最新要求：优先紧凑排版／加宽文字框，游玩统计及最近游玩可同一行；短图的制作／发行／引擎可按空间并排。只有这些方法仍放不下时才将文字移到下方，必须是极少数保底，不能因画面繁忙就下移。评分站点间距清楚，只显示分数，不显示通常的 /10 或 /100。长标题尽量单行，可延长图框／缩小字体，极端长度提供完整提示。启动、编辑、省略号保持适当间距；编辑与省略号短小，省略号仍只显示省略号（当前 108/80/40 DIP、间距 8 DIP）。所有蓝色链接与相邻文字中轴线对齐。
5. 封面必须铺满卡片，等比且不得拉伸；调整裁剪窗口保护人物和底部标题，不用完整原图留边来回避裁剪问题。全局书架／发现／相关作品封面四角统一圆角，玻璃模式下 CG 外框圆角也不能消失。封面与角色图下姓名居中，包括 CV 与相关作品名称；字符很长时尽量避免换行并保留提示。
6. 横幅优先原素材，避免低解码上限或重复压缩；源素材确实低清时不要谎称已高清。详情“…”支持选择本地横幅、恢复自动横幅；独立保存，不被刷新覆盖。源文件不改，保存／取消／损坏文件失败不能破坏旧选择。
7. 全程序正文支持 Markdown；简介标题只叫“简介”，删除“· KunGal”。来源链接只用网站名（KunGal、VNDB、Bangumi 等），不加“打开／查看／在…”。相同作品的 VNDB 来源只保留最合适的一个作品级链接，移除无用重复／实体链接，但不篡改原始来源身份。
8. 补齐 KunGal 有图角色的图片；角色卡片下方显示声优而非主角／配角。有真实图片地址但请求失败不能假装源站无图。真正没有图片的角色显示两行姓名／声优，不分配空图框／占位图标。CV 链接不要下划线。去重依据独立身份、正式名称／别名与声优事实，保留不同角色及剧透边界，不能因罗马音／中文／日文混合而重复展示。未知姓名不显示 ??????。
9. KunGal 中可点击的角色、CV、公司、制作人员、引擎、系列和标签等都可在程序内打开进一步资料，替换当前主内容并可返回，不使用悬浮窗口或跳到网页替代内部资料页；明确的来源网站链接仍可打开源站。相关作品只展示封面、名字及与本作关系，不嵌套展开另一游戏的全文／角色详情。实体相关作品列表同样显示封面、居中名称。
10. 关联作品规则全局统一：仅展示能核实为 KunGal Galgame 的游戏，排除动画等非 Galgame。不能只针对 Angel Beats 或某个页面做白名单；角色、CV、公司、人员、引擎、系列、标签和发现的作品列表共享媒体／KunGal 页面事实核验。NextMoe 作品号和 KunGal 页面号是不同身份，不自动视为相同。
11. 标签词汇、尺寸、间距、链接与普通项的一致性要统一。点击标签按实际书架作品编号分为“已在书架／未在书架”，不能把原版／重制／同名当成已经入库。设置保留全局 NSFW 开关，只影响在线搜索与标签结果，不删除或隐藏已在书架的作品；按 KunGal 的严格 SFW/NSFW 分类判断。
12. 游玩历史、下载信息、来源信息采用紧凑布局，不为少量内容长期占用整个右栏；本轮游玩次数／时长进入横幅文字框，安装与启动设置进入顶部“…”；来源在本作资料之后、相关作品之前，KunGal 来源链接直接打开网页。删除重复的“完整作品资料”区；制作人员用紧凑职位分组卡片，作品与角色属性标签样式统一。展开项不能被截断、溢出，尽量不换行。侧栏 Discover 等标签不得遮挡，使用清楚的中文界面。
13. 当前只启用简体中文，这是用户后来的明确收缩。已有简体／繁体／日文／英文功能、资源、设置保存与历史候选不能删除，保留日后复用；暂不拓展其他语言。未来再启用时默认系统语言、允许设置调整，优先简体，再繁体／日文／英文。当前远端简介优先中文；缺少正式译名／译文时保留来源原名或明确原文回退，不捏造翻译、不把罗马音机械当正式中文名。之前 tags、职位、关系、CV 的混合语言问题不能被一次语言菜单改动算作整体解决。
14. 本地已识别作品的封面、角色、选定 CG、直接相关卡片图在导入／识别准备阶段持久缓存，尽量避免打开时加载。用户后来明确资源成本优先，不必追求每次绝对零等待；A→B→C 的无限关联预取不要求实现，禁止无界递归。遵守现有原图 2 GiB／20000、解码 64 MiB／有限并发等预算，旧图继续复用，不能为“秒开”无限增加内存或联网。
15. 最新明确选择：详情及目录横幅文字框使用无折射轻玻璃，保留轻微模糊、按实际 CG 明暗自适应和细边缘高光，不是单纯透明色块。用户随后要求降低模糊、保留澄澈感，v23 最终折射／色散 0，圆角 18，轻模糊 σ1（先前 σ2.5 已替换），可读界限 .30/.10，边缘 .04。历史 test2／Daily（边沿 20／厚度 16／光泽 .32）及 Gallery 实现仍保留，其他场景不擅自更换。普通设置不标 Test；旧 Opaque 偏好兼容映射 Standard，保留系统透明效果、高对比度、节能、远程保护与失败回退。暗色 CG 不被浅色主题强制提亮成平灰，不按单一作品写特例。
16. 用户允许充分完成必要工作，反对半成品／反复确认；已授权可自主实现修复，真实库／凭据／正式发布边界不因自主权放宽。本轮另明确授权长时间无人值守的全面检查、发现问题后修复复验；仍不得把未知项改成 PASS。视觉检查由用户进行，避免助手长时间截图巡检或无必要的重复宽泛测试。改好后直接打开最新候选，发异步检查问题，用户点“已检查”或反馈；等待时不操作该窗口，不把等待／未回复当成通过。保留必要的代码检查／构建和匹配风险的测试，不把编译成功等同实际显示通过。
17. 保留旧候选、失败证据、未提交的其他协作者成果和回退点，不擅自 reset／clean／stash／提交／推送／发布／替换日用程序。仅用既有隔离 Test，37 部作品及个人名称、分类、标签、状态、评分、收藏、笔记、安装、参数、游玩记录都要保留。不要读取／解密／输出真实密钥，也不复制 profile／缓存进源码快照。不得启动真实游戏来验证展示。不把用户说的 GPT App 自身故障记为本项目代码错误。
18. 用户希望结束后给出最新版本入口与完整接续信息。本文件及 DEVELOPMENT 为维护入口，版本 manifest、运行记录、测试日志及完整 dirty 源码恢复在明确的 artifact 目录；区分已实现、用户已确认、未验证和已否定行为。下一位接手不能宣称全部历史任务／正式发布已通过；先检查最新未决项，再继续授权范围内工作。


## 历史隔离候选：简体中文 Test v18、封面裁剪与日常玻璃（2026-10-10，已由 v19 取代）

接续下节 v16。v17 提高横幅解码与持久准备上限至 3840 像素，详情菜单支持选择本地横幅原文件／恢复自动横幅；本地文件保存在 Test profile 的伴随目录，不修改来源图片，不写入旧版书架备份。旧库、个人封面与自动资料刷新契约保留。分辨率受来源原文件限制，不宣称低清素材可变成高清。

v17 的封面完整显示造成留边，用户明确要求仍须铺满，故 v18 改为等比填满并自适应选择最小裁剪窗口；有界细节分析提高竖封面底部标题区域的保留优先级，避开细节密集边界，不做识别人脸或文字的保证。共用 CoverPresenter 覆盖书架、发现与相关作品；角色图和个人选择预览继续完整显示。原图、资料身份和图片缓存不改。

材质采用历史 RefractionGallery 的第二候选 Daily（日常玻璃）参数：边沿 20、厚度 16、光泽 .32，色散 0；设置直接命名日常玻璃，移除手动“关闭透明效果”，旧 Opaque 偏好兼容映射 Standard。系统透明、高对比度、节能与远程策略继续优先。玻璃横幅增加四角遮罩以覆盖不支持透明的原生 SwapChainPanel，普通图片的圆角裁切仍保留。

当时两处 Start-The-Order-Test.cmd 与 launch-language-v18.ps1 固定 candidate-language-v18；647 项 payload，manifest SHA256 A8623A3D2A6995B2EE61539B5955B04E7275E3101FE79DCF8ECF0C4F9FCCD09D，WinUI MVID 2afe9732-09e4-4e9c-8ac7-b0ed707587a1。43/43 定向 Release 回归，无跳过；首次编译的圆角 API 错误及修复日志保留，最终 publish／打包成功。v18 当时打开到书架，37 项保留、无未保存编辑、无游戏计时；用户随后指出暗色 CG 玻璃近似标准材质，v19 接续修复，未执行助手桌面巡检、全仓测试或资源长测。

完整当前源码恢复为 .artifacts/multilingual-20261010/source-v18.json、source-v18/、source-v18.patch，报告为同目录 report.md。含继承工作，不是任务专用集成补丁。历史包不覆盖；主开发、Production 与日用程序未替换，无提交、推送、游戏执行或资料迁移。


## 历史隔离候选：简体中文 Test v16、封面四角裁切（2026-10-10，已由上节取代）

接续下节 v13。v14 已去除紧凑角色声优链接下划线；角色、声优、制作人员、公司、引擎、系列的相关作品列表在缺少封面时，使用一次有界批量 covers 请求补全，继续核验 Galgame／KunGal 页面事实与返回编号，保留分页游标及取消／过期保护，不逐项请求详情。相关 Release 回归 126/126，无跳过；真实隔离来源样本中 Marika 的 6 项、角色页的 1 项封面取得，证据位于 `.artifacts/multilingual-20261010/entity-covers-live-v14-credit/` 和 `entity-covers-live-v14-character/`。

v15 将四角裁切加入共用 CoverPresenter，但用户截图证明 UniformToFill 图片溢出导致底部圆角仍被截掉。保留此失败记录。v16 改为在 CoverPresenter 的实际显示边界裁切；完整显示模式按实际绘制区域裁切，生命周期结束释放 composition clip。适用于书架、发现及实体相关作品，个人封面选择及图片缓存契约不变。用户随后回复“解决了”，仅记录为这次圆角问题确认，不推定整个历史任务或发布验收通过。

当前入口 `.artifacts/multilingual-20261010/Start-The-Order-Test.cmd`／`launch-language-v16.ps1` 固定 `candidate-language-v16`，647 项 payload，manifest SHA256 `8A0D53005776A23631F83A5016C4791152E0E3707BBF40466639EECD5EE6AEE7`。Release publish／打包成功；依用户新要求，v16 显示效果由用户核验，未再次运行助手桌面巡检或全仓测试。v15 既有图片／性能定向回归 60/60 属于 v15，不能称为 v16 圆角像素测试。电脑操作已停止。

当前仍固定简体中文；四种语言实现、资源和历史候选保留，未承诺完整翻译所有远端资料。图片预取采用已有有界策略，不无限递归关联图谱。识别仍为 recognition-v2-release-aware-7：研究及有界错字召回已实现，普遍优于 PotatoVN 尚未证明。v16 未生成完整源码恢复清单；当前恢复清单为 v18，下节历史源码清单 v13 仍保留。版本与用户确认范围见 `.artifacts/multilingual-20261010/report.md`。本轮没有主开发集成、提交、推送、日用替换或 Production 写入。

## 历史隔离候选：简体中文 Test v13、无图角色两行布局（2026-10-10，已由上节取代）

接续下节 v11，全局 Galgame／KunGal 关联与缓存规则保留。最新改动仅在 `native/TheOrder.WinUI/Views/CatalogContentView.cs`：没有来源图片地址的角色不创建空图框或占位图标，姓名、声优各占一行，居中、长内容省略并提供完整提示；有图角色仍使用图片卡片，剧透折叠及角色／声优的窗口内导航保留。规则不依赖游戏名称或编号，不将图片加载失败伪装成来源缺图。

炽焰天穹当前 Test 的实际 NextMoe 详情缓存有 74 个角色、0 个图片地址；直接检查 KunGal 页面也显示文字角色列表。不是新捕获的 API 响应，也不表示该游戏在其他网站没有图片。v13 实机检查了紧凑列表、多声优单行、角色姓名与 CV 链接、窗口内返回；Angel Beats! 既有 21 张非剧透角色图与 3 张关联封面仍正常。v12 首次检查发现多余 CV 分隔符，已修复，历史包及记录保留。

最新普通入口 `.artifacts/multilingual-20261010/Start-The-Order-Test.cmd`／`launch-language-v13.ps1` 固定 `candidate-language-v13`，647 项 payload，manifest SHA256 `F21815F9F94E03AE3AE5A80BCA0463D297C0D68421D0FF7B4FCEBFA8511650D2`，WinUI MVID `ff549f1e-2d01-4ba5-bc02-87755be24c4d`。仍为既有 37 项隔离 Test、有效 Standard、诊断关闭、Advanced 资格关闭。Release publish／打包成功；v11 的 230/230 回归对应的 5 个核心 DLL 与 v13 逐字节相同，v13 布局另以实机验证，没有声称重新运行全仓测试。

完整源码恢复见 `.artifacts/multilingual-20261010/source-v13.json`、`source-v13/`、`source-v13.patch`，含继承工作而非任务专用集成补丁。桌面证据、资料 hash、版本范围与回退见同目录 `report.md`。用户已检查 v11 全局关联；v13 已打开供检查，确认范围单独记录。正常退出后可用 `launch-language-v11.ps1` 回退布局并保留全局关联修复。原主开发和 Production 未修改，没有提交、推送、日用替换或游戏执行。

## 历史隔离候选：简体中文 Test v11、图片缓存与全局 Galgame 关联（2026-10-10，已由上节取代）

工作区 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`，分支 `codex/library-ui-refinement`，HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加未提交源码；完整恢复清单见 `.artifacts/multilingual-20261010/source-v11.json`、`source-v11/` 和 `source-v11.patch`。清单含继承成果，不是可直接整体套用主开发的任务差异，不得整体引入 `5b574f0`。主开发实际 HEAD 仍为 `45b77f4`，34 项既有修改，本轮未集成。

按用户最新决定，当前入口固定简体中文，`NativeMultilingualEnabled=false`；保留四种核心语言资源、投影、设置保存及旧多语言候选，`Start-The-Order-Multilingual.cmd` 仍固定历史 v7，不能把它当作本轮修复版。游戏简介优先中文，远端缺少中文时明确提示原文回退，不伪造译文。角色按独立来源身份、名称与声优事实合并重复记录，保留图片与剧透边界；职位归入统一词汇，未知姓名显示“未公开姓名”。关联作品只显示封面、名称与关系，不在当前游戏下展开其他游戏的全文。

全局关联规则：仅接受 `object=work`、`medium=galgame`，并具有有效 `claim.site=kungal`、`site_work_id` 和可展示 `live/draft` 状态的记录。NextMoe 编号与 KunGal 页面编号分开保存，不因编号相同就猜出页面。作品详情、角色出演、制作人员、公司、引擎、系列、标签及发现列表共用验证。旧缓存缺少媒体／页面事实时先隐藏该关联，再由既有有界后台队列刷新；原始书架关联与个人编辑不变。接口已确认的 Galgame 页面仍可能日后删除或暂时不可访问，失败提示不再无依据归咎 NSFW。没有作品名称或固定编号白名单，也不把页面存在当作识别证据。

用户截图中的 Angel Beats! 动画确实由 NextMoe 的 `relations` 返回为 `medium=anime`、`claim=null`；不是已证实的本程序直接请求 Bangumi。该实际响应的 7 项关联最终保留 3 项 Galgame；3 个 KunGal 页面当前 HTTP 200，动画编号页面 HTTP 404。实机显示 21 张非剧透角色图、3 张相关作品图均有像素且使用内存缓存，点击关联游戏替换主内容并可返回。公开响应回放、直接公开网页检查与桌面请求分别记录在本轮报告中。

所有当前作品封面、角色图、选中 CG 和直接相关卡片图片在导入／识别准备阶段写入持久图片缓存，重开继续复用；精确显示尺寸预解码后再放入页面。原图缓存上限 2 GiB／20000 文件，解码缓存 64 MiB，有限并发。按用户成本取舍不递归预取整个关联图谱，下一层首次访问仍可等待。旧缓存升级只重新取得必要展示事实、复用仍有效的 CG，不重复 OCR。详情计时值只测资料读取，不等于整个点击至绘制延迟。

最终相关 Release 回归 **230/230，无跳过**，覆盖全局媒体／页面筛选、6 类实体列表、发现／标签、旧缓存升级与离线保留、媒体缓存、导入、取消及既有自动识别保护。此前 v9 全量 Release 1633/1633 与 v10 定向 73/73 保留各自版本范围；本次没有重跑资源长测。最终 Release publish／打包成功。真实 Test 37 项资料；核验 library、Profile、界面／材质／语言配置及加密凭据 6 个文件哈希未变，没有游戏 Session 或游戏执行。

当前体验入口 `.artifacts/multilingual-20261010/Start-The-Order-Test.cmd`／`launch-language-v11.ps1`，固定 `candidate-language-v11`，647 项 payload；manifest SHA256 `FA2EE271BBFACC0F783ED45E0778A92E028A2B9711953A7A296B5674C72F8171`，WinUI MVID `2ffd262c-0464-478c-9708-ec5e067659fe`。有效 Standard、诊断轮询关闭、Advanced 资格关闭；ProfileId `22c746b9-c20b-50d9-efea-1ce4281261ee`、StoreId `eb86f351-0960-fd91-3469-bb46c947018e`。实际截图、测试失败历史、源码身份和回退边界见 `.artifacts/multilingual-20261010/report.md`。程序留在 Test 供用户检查；用户检查不能预先记为通过。没有提交、日用替换、Production 写入、推送或发布。

## 历史隔离候选：Catalog v9、标签分组与 NSFW 设置（2026-10-10，已由上节取代）

工作区仍为 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`，分支 `codex/library-ui-refinement`，HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加未提交源码；不能仅凭 HEAD 或相同 apphost 哈希恢复当前程序。最终源码清单、修改前副本、完整差异和新文件保存在 `.artifacts/ui-catalog-refinement-20261009/source-catalog-v9.json`、`source-catalog-v9/`、`source-catalog-v9.patch`。这些包含继承的未提交成果，集成时按清单审阅，不整体引入 `5b574f0` 基线复制。

已实现：书架搜索移入工具栏并按需展开；发现页支持回车搜索和封面网格，详情／角色／制作公司／人员／标签导航替换主内容并可返回；按完整已提交书架的作品编号将标签结果分为“已在书架／未在书架”，分页后继续归组，不以同名或原版／重制关系替代入库身份。普通与可点击标签使用一致的尺寸、间距、换行和中轴对齐，封面／角色／CV 名称居中。详情不再重复放竖封面，标题、状态、制作、发行、引擎及分隔评分覆盖在 16:9 CG 图层上；启动／编辑／省略号最终宽度为 108／80／40 DIP，间距 8 DIP。长标题尽量单行，极端长度有省略与完整提示。

KunGal 优先资料补全、角色图片和 CV／内部实体链接、站点名称链接与单一作品级 VNDB 链接、原生 Markdown 已实现。新版缺少资料或 CG 时，只从核实的唯一版本关系获取补充，不更改实际识别的作品身份；补充资料保留来源，CG 来源放于折叠信息中。初次 CG 选择在自动识别／导入流程准备，采用有界尺寸、对白／文字检测与候选质量筛选，持久缓存复用；不存在保证任何 CG 都美观或任何网络状态下详情绝对零延迟的声明。详细条件、缺失与历史尝试见本轮报告。

设置新增“显示 NSFW 内容”，新资料默认关闭，仅限制发现搜索和标签相关作品；书架、识别、刷新和直接本地详情不受开关影响。使用 KunGal／NextMoe `content_limit=sfw/nsfw` 的展示分类，不能把 `content_rating=r18` 当作 NSFW。关闭时服务端筛选加客户端分类核验，未知分类拒绝展示并报错；改变设置会取消／清空旧结果，过期请求不能回填。偏好存入独立 `native-catalog-browse-v1.json`，严格校验版本和 Profile／Store 身份，原子保存；既有 library、native-ui、writer 和 manifest 契约未变，旧程序忽略附加文件。

验证按版本区分：此前定向 native Debug 1012/1012、Release CG／缓存 83/83；标签分组 v8 Release 128/128；最终 NSFW v9 Release 165/165，均无跳过。真实隔离桌面完成标签分页与分组、Light／Dark 和窄窗标签布局、搜索回车、NSFW 开／关对照及重开恢复；另外 4 次有界公开 API 调用核对展示分类。37 部 Test 书架 hash 未变，两次 v9 正常退出均为 0。未重跑 Phase 7 资源长测或完整虚拟机验收，历史证据不冒充 v9 全量重测；识别仍为 `recognition-v2-release-aware-7`，本轮未再次优化排名或声称普遍优于 PotatoVN。

最终测试入口 `.artifacts/ui-catalog-refinement-20261009/Start-The-Order-Test.cmd`／`launch.ps1` 固定 `candidate-catalog-v9`，647 项 payload；`files.json` SHA256 `910F5D93B2165E86E7E41BF2C649C7BBDD3708592CF1C3AACC7AC29B50D9187D`，WinUI MVID `d027a182-85d7-4d89-9599-ede9a7c93b5f`。有效 Standard、诊断关闭、Advanced 资格关闭；测试资料所保存的 Advanced 意图不会绕过资格。完整结果、截图、失败历史、范围与回退入口见同目录 `report.md`。没有提交、集成主工作区、替换日用、写 Production、启动游戏或推送。

## 历史隔离候选：简洁 Library／Details 与识别 v7（2026-10-09，已由上节取代）

本轮在 `codex/library-ui-refinement` 实施用户截图所要求的界面调整。仓库 HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 是文档基线，应用父版本 `03f1f7bb0dfcc35957fe5ba4301cf21551a8dc88`；**当前候选还包含未提交源码，不能仅凭 HEAD 恢复**。完整 52 文件源码身份、tracked diff、新文件副本及结果见 `.artifacts/ui-refinement-20261009/report.md`、`candidate-ui-v5-source.json`、`delivery-source.json`。

实际实现：84 DIP 图标加下方文字的固定导航栏；移除汉堡按钮及独立识别导航；仅可返回时显示图标下方 Back；书架 Grid／List 仅封面与名称；Details 默认只读作品、简介、标签、角色及游玩信息，个人修改进入编辑，安装／来源折叠，识别建议与人工关联嵌入当前作品。Settings 收纳在线／PE 识别开关，材质改下拉，已配置密钥隐藏编辑框。Light／Dark 应用图标和系统主题驱动的窗口／托盘 HICON 均已实现；本轮没有实际切换宿主系统主题或拍摄托盘，Win32 owner／WM_GETICON 回归与应用主题截图分开记录。

Details 背景优先当前作品 Banner，其次有尺寸资料的合适横图，否则完整竖图靠右留白；小封面完整显示，不裁切成局部。个人封面继续遵循“资料源默认＋个人选择”。Library 继续游玩仍使用当前作品封面构成静态艺术背景，C1 完全清透／连续玻璃及提前释放原则保留，未扩展到滚动 Toolbar。新增读取当前详情已有的公开资料和字符图片，无新增 API 请求、schema 迁移或凭据复制。

识别版本 `recognition-v2-release-aware-7`：精确索引独立保留，追加有界错字召回／检索词，不把模糊结果升格为 Exact、IndexProof 或独立来源编号。旧待核对建议需重查，严格 >0.9、版本／独立编号冲突与唯一领先保护保留。新 32 项公开标题合成错字的候选召回 0→32/32；**不是 32 项自动识别成功**。同一真实 NextMoe 响应回放：公共开发集 54/60 正确自动、6 人工、20 负例拒绝、0 错误，24 项独立在线样本的最终回放 24/24；离线模拟 181 正例为 168 正确自动、13 人工、0 错误，64 负例拒绝。既有自动覆盖未提升，不能声称所有条件优于 PotatoVN；数据来源与历史错误保留。

最终 Debug UI 构建／Release publish 成功；本轮全量 native Debug **1994/1994**、Release 相关 **576/576**，无跳过，native optical 回归通过。v5 的七个核心组件与受测候选逐字节一致；后续 UI 修复由真实桌面补验。实际点击／键盘完成选择与文本 Ctrl+A 区分、编辑保存／未保存提示、返回、菜单 Esc、配置密钥显隐，以及 480 DIP 请求窄窗的 Light／Dark、Grid／List。Library／Details 明确稳定后各 10 秒零绘制；最终 owner 4/4、2/2，正常 exit 0。这是定向生命周期证据，**不是新的 Private 资源全量验收**。

冻结程序 `.artifacts/ui-refinement-20261009/candidate-ui-v5`：641 项 payload，`files.json` SHA256 `EBE6912CFD8FC9A367884B4A8DA393D50B055F8BE2E20B46217C49442A312222`。普通 `Start-The-Order-Test.cmd`／`launch.ps1`：隔离 Test、Standard、诊断 0、Advanced 资格 0；另有受控玻璃和诊断入口。实际截图见 `review.html`，没有新录像或正常入口截图的虚假声明。普通入口启动后桌面工具发生前台进程识别错误，保留日志，不作为应用缺陷。

两个原工作区 958 个文件、HEAD、分支和未提交状态核验未变；没有提交、集成主工作区、替换日用、写 Production、启动游戏或推送。`5b574f0` 基线复制不得整体集成。体验和完整代码／资料回退边界见 DEVELOPMENT 当前节；后面的 v6／旧版本记录保留为历史范围。

## 历史识别候选：v6 公共泛化复核（2026-10-09，已由上节取代）

应用源码 **03f1f7bb0dfcc35957fe5ba4301cf21551a8dc88**，算法 `recognition-v2-release-aware-6`，分支 `codex/recognition-v2-generalization`。**RECOGNITION CANDIDATE VERIFIED — READY FOR USER-APPROVED INTEGRATION / DAILY-USE UPDATE**，限以下证据范围。用户要求的“几乎任何条件下优于 PotatoVN”**尚未证明**；用户报告34/36不是可核验的同输入／版本真值配对。未集成主工作区、替换日用或写 Production。

v4新增有界公司／作品站点身份、版本与等价记录复核、不完整索引卡片详情调度；v5新增繁简检索、官方非机器中文等价、仅用于检索的副标题前缀、CD/DVD/BD-GAME噪声清洗及长原始查询调度。v6修复同名精确候选因“索引＋发行”证据等级不同被排除的通用漏洞。独立编号、可区分的程序发行公司、更长明确标题及已核实等价记录仍可区分。无游戏名／ID特判，不扩大6查询／2详情／2发行限额，不把索引推导当独立事实，保留章节／FD／重制／编号冲突和唯一性保护。

数据角色分开：既有公共60项是**开发集**，真实NextMoe同缓存v4→v6正确自动 **51→54/60**，错误0，人工9→6，20负例拒绝。授权36项真实目录回归作品级正确36/36，35项确认版本，WA2完整作品正确但安装年份未知；31项真正隐藏负例拒绝，另5项有效其他身份仍存在（4自动／1人工）。

新225项公开目录生成合成输入（181正例／44隐藏负例）运行前冻结，与既有1510作品系列不重叠；20重复通用负例单列。**首次离线VNDB模拟盲测v4和v5均168正确自动／181、错误1，没有覆盖增益。** The Question同名误应用驱动v6修复，同集修复后回归168正确、错误0、13人工，44新负例及20重复负例拒绝；不得改成首次盲测PASS。新24项真实NextMoe盲测v4/v5均24/24，v6同响应回归24/24，错误0；不能外推为任意安装正确。

最终全量native Debug **1969/1969，无跳过**，Release受影响 **533/533**，定向88/88，WinUI Debug／Release publish成功。v3真实桌面导入、编辑、歧义、取消及过期结果证据按版本复用；本次无原生桌面工具，未声称v6重新完成鼠标键盘录像。v4–v6未改UI，当前核心提交／异步回归通过。所有历史错误、缓存缺项和版本断言失败保留。

候选 `.artifacts/rnd/gpt-generalization-20261009/candidate-03f1f7b`，640项payload，files.json SHA256 `500201A24746398FF6B9C41E998D91DDDFA2EB9CAE081EA048F931D522CB4823`。普通 `launch.ps1`：隔离Test、Standard、诊断0、Advanced资格0；独立诊断入口保留。19运行时＋9测试文件的集成／完整回退补丁同目录，主工作区只读apply-check通过且34项修改逐字节未变，尚未应用。禁止整体引入5b574f0基线复制。评测角色、人工原因、成本、身份及比较缺口见 `report.md`／`final-metrics.json`／`integration-manifest.json`；私人输入、profile和缓存不进Git或程序包。

## 历史识别候选：Recognition v3 验证记录（已由上节取代，2026-10-09）

**RECOGNITION CANDIDATE VERIFIED — READY FOR USER-APPROVED INTEGRATION / DAILY-USE UPDATE。** Claude 原分支保留在 `27b7166`；接续分支 `codex/recognition-v2-validation`，最终应用源码 `78427b406c6a58ba2a17be5d51a2e5865fc1e8e9`，算法 `recognition-v2-release-aware-3`。本节及 DEVELOPMENT 的当前节取代以下历史候选的当前状态声明；历史结果与失败不改写。

接续修复：版本官方标题优先规则被父作品精确别名提前返回绕过；机器翻译标题误作独立身份；索引推导编号误作更强事实；自动提交未携带竞争候选发行上下文；短拉丁名称与目录缺失同名作品的误应用；歧义候选界面错误声称“唯一对应”。提交 `38ace0f`／`78427b4`。自动应用仍严格 >0.9，同时必须身份／版本一致且唯一领先，不按分数或 ID 处理歧义。提交时保留本轮竞争候选的有界发行上下文，重取选中详情并复核；上下文不持久化，重开后的历史建议需重新识别。

最终同条件 **真实 NextMoe JSON 响应回放**，使用运行前冻结的版本级真值和修订隐藏集合：36项授权真实目录正确自动识别 **30→31/36**，候选错误自动应用0，5项人工；公共60项 **47→51/60**，基线错误1→候选0，20项负例均拒绝自动应用，真实响应召回57/60。真实隐藏组有31项目标确实不存在、5项独立版本／其他有效记录仍存在，不能称36项全是负例。历史29→33、离线35/36不是最终结果。本轮有限在线补采24次请求及真实GUI网络访问与最终零新请求回放分别记录。

一次新独立 **离线 VNDB 转储模拟**，排除既有1144个作品系列、运行前冻结390项（295正例／95负例）：候选v2正确自动282、错误1；该错误驱动短标题保护后，最终v3在同一集合正确281、错误0、95/95负例拒绝。最后一项是修复后回归，**不是又一次未调参的独立验证**。有限目录仍可能缺失同名／版本资料，零观测错误不能外推为所有游戏正确。

全量Debug **1927/1927，无跳过**（含现有Electron依赖测试；最终算法v3、界面说明修订前）；最终78427b4的Debug识别477/477、Release识别及FilesPresentation492/492，WinUI Debug／Release构建成功。真实桌面在隔离Test完成自动导入、版本匹配、歧义详情与人工应用、个人编辑保存重开、识别取消及旧异步结果不覆盖当前选择；最终实例正常exit0，图像及导航／详情在途0。没有启动游戏、改动Production或切换日用。

Release候选 `.artifacts/rnd/gpt-verification-20261009/candidate-78427b4`，559项hash一致，manifest SHA256 `7FBFD5210E8BC169C4C4A1EEAF0DD6F43E03FC5E63E883A5DAD31D69E94982DB`；普通隔离体验 `launch.ps1`，Standard、诊断0、Advanced资格0。完整结果、实际截图、失败尝试和23文件运行时／测试集成补丁见同目录 `report.md`／`integration-manifest.json`。原主工作区34项修改及HEAD未变；32源码逐字节、2文档仅换行差异匹配5b574f0基线。**不得整体引入5b574f0**；实际应用补丁已在主工作区只读 `git apply --check` 通过，尚未应用。

## 历史 Claude v1 识别研发候选（2026-10-09，归档结果，已由上节取代）

隔离分支 `claude/recognition-rnd`（仅本地）。`5b574f0` 是 `codex-phase7-finalization`（HEAD `45b77f4`）34 个未提交文件的逐字节副本，已按 `final-checkpoint.json` 核对 34/34；**不要单独集成该提交**。Claude 的改动是其后的提交。算法版本 `electron-recognition-v1-native-4` → `recognition-v2-release-aware-1`。

实测的旧系统损失与改动：
- 版本否决过宽。任何"相近但版本未指明"的本地字符串（含 EXE 名、补丁版本号）都会否决正确作品，且从不查发行资料；被否决候选还排在无关候选之后。现由 `ReleaseAwareRanking` 用作品标题及**发行标题**确认版本标识，由更具体的候选承接，构建版本／分辨率／R18 视为包装。仅资料侧有的重制／FD 仍需本地证据；章节／编号／独立编号冲突仍为硬否决。
- PE 版本资源以字节计长时解析抛错，导致整个识别失败（PE 默认开启后的回归）。现仅接受恰好落在该叶块内的字节计长；其他越界仍拒绝，且单个 EXE 失败只丢弃其 PE 证据。
- 自动策略在多个合格候选间按分数和 ID 取最高，同名作品会被误应用。现仅应用排序器判定唯一的首位候选；同名索引项（`Homonyms`）只有在本地 Steam 编号命中时才可解除。提交时在原候选集合中复核唯一性。
- 发行格式噪声（下载版／通常版／限定版／含全CG存档／汉化组／v1.02 等）使完全一致的标题仅得近似分。现由 `LocalTitleCleaner` 清洗，来源不变。
- 索引的同一作品多个 Steam 编号曾被视为本地编号冲突。现每来源至多取一个。

离线回放评估：VNDB 数据转储构建的目录替身（非实时 NextMoe），人工真值。
- 公开合成 held-out：自动正确 197/238 → 225/238，误应用 2/331 → 1/331。
- dev：322/374 → 356/374，误应用 8 → 1。
- 用户授权的私有真实库（36 个导入候选，数据不入 Git）：26/36 → 35/36，误应用 0。
- 隐藏真实作品后，36/36 均正确留待核对。

剩余误应用均为基线已有的错误：一例目录缺失的同名作品，一例被截断成本篇标题的 FD 样本。请求数与基线相当。识别回归 427 → 467（Debug／Release 全过）；全量失败仅为本工作区环境项。

**未验证**：实时 NextMoe（需用户自建 API key，未读取已存凭据）、GUI 实机输入、日用切换。此前"多个合格候选取最高"是上一轮明确决定；本轮按数据改为唯一性优先，需评审确认。详见 `DEVELOPMENT.md` 同名节。

## 当前 Phase 8.2：Electron 识别默认与个人封面选择补齐并切换日用（2026-10-09）

已核对真实 Electron 源码、识别差分 oracle 与历史 `9c27af3`／`16ccc28` 封面记录。原生 TitleMatching、EvidenceRanking、ReviewSafeRanking 已包含标题清洗、别名、独立编号、同索引行、PE 标题及章节／FD／重制冲突等规则，本轮没有重写算法或把排序分当成概率。实际缺口是原生自动导入默认 IncludePe=false；已恢复 Electron 的默认只读 EXE VERSIONINFO 线索，沿用数量／大小／路径边界及用户关闭选项，不执行 EXE、不扩大目录扫描。严格 >0.9 的最新自动选择规则继续保留；既有待核对条目不自动重跑。

用户确认沿用最终“资料源默认封面＋个人选择”，不恢复被取代的高票竖图自动优先。原生默认选图和本地／远程个人封面字段已兼容；新增 Details ⋯ 中“选择作品封面…”及“恢复资料源默认封面”，完整预览横竖图，同一作品刷新保留个人选择，取消不写入。保存再次核验作品、封面 ID／URL／分级、凭据代次与原 cover token，经既有串行事务提交；不改变个人字段、Session／计数／时长或 schema。既有本地封面优先级保留，显式远程选择可替换本地封面，界面提前说明。没有借此补做全局 blur/plain/crop 显示偏好 UI。

最终相关 Debug／Release 回归各1283/1283、Debug WinUI 构建及独立 Release publish通过。实际鼠标／键盘在新原生 Test identity 加入虚构作品、选择蓝色横图、取消（library 字节不变）、恢复资料源默认图；正常exit0，image work／waiters／导航／详情在途及 stale covers=0。开发验证没有使用真实库、凭据、真实 API 或游戏。完整历史来源、源码差异、候选／UI／测试证据见 `.artifacts/phase8-2-electron-algorithm-parity-20261009/report.md`。

用户批准“替换日用版本”后，普通宿主入口实际切换成功：独立候选 `D:/Projects/The-Order-candidates/phase8-2-electron-algorithm-parity-20261009/candidate-01/win-x64`，558项文件冻结核验，WinUI DLL SHA256 `A6302FF41FB7777001EB9995702733063441E9B30F3109DF5556EBA292A40C8F`。新入口 `start-daily-electron-parity.ps1 -UserApprovedSwitch`；Standard、诊断关闭、Advanced资格关闭。真实物理Production／原ProfileId／StoreId／writer owner及实际进程代次一致，切换时36部作品、36安装、30关联、4条Session／4次／466秒不变，另建15文件一致性备份并验证独立恢复；旧版和旧备份保留，没有恢复旧快照。源码HEAD仍为 `45b77f4` 加已记录的继承与本轮未提交差异，不以HEAD或相同apphost哈希代替完整程序身份。未暂存、提交、推送，不代表Phase8完成或正式发布。

## 历史 Phase 8.2：自动识别与候选跳转链接已切换日用（2026-10-09）

用户要求导入后不再手动开始识别；排序分严格大于0.9时应用最高候选，其他结果人工选择。当前分支在`45b77f4`与上一轮未提交的Session书架同步修复上实现：导入事务回执只提供实际新增ID，自动队列最多2项在途、2000项待处理；已有安装目录被跳过时不重复识别。启动时补处理没有关联、安装存在且没有既有核对记录的条目，不替换已有作品关联或既有核对决定。

自动选择不要求旧的唯一候选分差标志；同分按作品ID稳定选择。仍保留版本／编号冲突否决、最新详情与发行资料复核、本地证据／凭据代次及原事务保护；分数为排序指标而非准确概率。失败或不满足条件的结果留待核对。后台应用等待未保存个人草稿处理完毕，保留当前查询、详情页、选择和后提交的个人字段。Session、历史、启动授权与持久化schema未改变。候选详情新增可点击作品／来源链接，限定当前来源HTTPS白名单；选择或凭据变化立即清除旧链接，不合成未提供的URL。

Debug与Release相关回归各548/548通过，最终Debug构建及独立Release publish成功。实际鼠标键盘在新的原生Test identity中选择、扫描并导入3个合成目录，无“开始识别”点击，2项自动关联、1项章节冲突保留核对；实际详情显示Steam链接控件。Test正常exit0，图片任务及导航／详情在途均为0，退出不改变library hash。真实API、私人库和凭据没有用于开发测试，浏览器外部站点导航未执行。新候选与合成体验入口见`.artifacts/phase8-2-auto-recognition-20261009/report.md`及`D:/Projects/The-Order-candidates/phase8-2-auto-recognition-20261009/`；该合成fixture需要Diagnostics=1，不能冒充普通日用入口。用户批准“本轮完成替换”后，普通宿主入口`start-daily-auto-recognition.ps1 -UserApprovedSwitch`已实际启动候选，Production根／ProfileId／StoreId／writer owner与进程身份一致，Diagnostics=0、Advanced资格关闭；启动核验时36个安装及4条Session／4次／466秒均未变，预先新增15文件备份并验证独立字节恢复。应用正常使用已保存凭据进行获授权的自动识别，代理／备份助手不解密或输出凭据。用户已在操作新窗口，代理停止进一步输入并保留运行；不把此时点核验冒充全部真实库已识别。旧程序和入口保留，不恢复旧快照。不得把源码HEAD或相同apphost EXE hash当作同一完整程序。未暂存、提交或推送。

## 历史 Phase 8.2 日用修复切换：启动／退出自动同步书架（2026-10-09）

用户报告已批准的 `2b5e67a` 在游戏结束后，书架最近游玩仍需手动刷新。实际源码确认 Session 事务已更新内存 writer 和持久化记录，Shell 原有刷新只更新计时状态，没有重新生成书架摘要。当前独立分支 `codex/phase8-native-profile-protection` 在 `45b77f4` 上保留未提交的窄范围修复：复用既有 Shell tick，针对改变的已提交 LibrarySnapshot 单飞查询当前筛选，保留 Details 草稿与作品选择，不重开 writer、不增加新计时或写盘。Session／进程／资料版本契约均未改动。

相关 Debug／Release 回归各230项通过；最终6项新增回归及 Debug构建、Release publish通过。真实鼠标输入在原生 Test identity的6项合成库启动项目自有游戏，自动显示最近游玩与继续游玩；正常结束后列表12→13分钟、选择保持1项，存储次数3→4、历史0→1、720→798秒，Completed／ExitConfirmed；Test应用正常exit0。修复验证限合成Test；未启动真实游戏、重跑资源长测或移动Phase7标签。

用户明确批准“切换到修复版”并在普通宿主PowerShell执行后，**DAILY-USE REPAIR SWITCH COMPLETE — OBSERVATION IN PROGRESS**。独立候选 `D:/Projects/The-Order-candidates/phase8-2-live-refresh-20261009/candidate-01/win-x64`，新日用入口 `start-daily-repair.ps1 -UserApprovedSwitch`，普通合成Test入口 `try-repair.ps1`；诊断与Advanced资格均关闭。实际PID3404、创建时间／EXEhash／原Production identity／writer owner一致；启动前后2部作品、2个安装、3条Session、3次／426秒及library hash不变，真实窗口核对Standard后返回书架留给用户。资料自Phase8.1发生变化，切换前已在资料锁下另建15文件备份并验证独立恢复副本，旧备份保留。完整差异、候选manifest、回归、合成实机与宿主切换回执见 `.artifacts/phase8-2-live-session-refresh-20261009/report.md`；真实游戏自动更新仍待用户确认。旧 `2b5e67a` 程序／入口不覆盖，源代码修复未暂存或提交。不代表Phase8全部完成或正式发布。

## 历史：Phase 8.2 首次切换2b5e67a，随后日用发现书架刷新问题（2026-10-09）

**DAILY-USE SWITCH COMPLETE — OBSERVATION IN PROGRESS。** 用户明确批准后，普通宿主 PowerShell 从既有 Production 入口启动未改动的 `2b5e67a55d8eda905b8200652d189d7754dff112`。635项候选文件、553项旧程序回退文件核验一致；真实 Production 与 Phase 8.1 备份哈希未变。实际进程 EXE、原 Profile/Store identity、真实根及 writer lock owner 经宿主物理读取／Windows Restart Manager核验，启动前后库字节与数量一致，实际窗口显示真实书架。设置页确认 Standard；诊断轮询关闭，Advanced资格关闭。未改原程序、快捷方式、历史候选或证据。

独立分支仍为 `codex/phase8-native-profile-protection`；Phase 8.1 原待提交的 `DEVELOPMENT.md`／本文件已单独文档提交 `4bf727e`，不含私库、备份或凭据。报告及实际启动回执：`.artifacts/phase8-2-20261009/report.md`。由于 Codex 宿主仍有 LocalAppData 文件虚拟化，本次真实启动由用户在普通外部 PowerShell 执行冻结 Production 入口，代理核验其回执并以 computer use 只读核对实际窗口，未把虚拟空库作为真实资料。

程序保持运行留给用户；真实游戏启动与计时、结束记录、编辑保存、托盘及退出重开仍待日用证据，异常恢复仅按实际发生记录。本轮不再跑资源长测，不自动恢复旧资料、不公开发布、不推送。普通启动和程序回退命令见 `DEVELOPMENT.md`；程序回退不恢复资料。**不代表 Phase 8 全部完成或正式发布获准。**

## 历史：Phase 8.1 原生资料保护与日用候选准备（2026-10-09）

**PHASE 8.1 PASS — READY FOR USER-APPROVED DAILY-USE SWITCH。** 仅完成准备，等待用户批准实际切换；没有新程序打开／写入原始 Production，没有替换正式程序或快捷方式。工作区沿用 `codex-phase7-finalization`，本轮分支 `codex/phase8-native-profile-protection`，起点文档 `1381806`；应用仍冻结 `2b5e67a55d8eda905b8200652d189d7754dff112`，没有应用源码修改或重建。

普通宿主 PowerShell 在真实物理源、独占既有 writer lease 下完成12文件一致性备份及最终哈希不变核验；备份只读并限制写入，私人 payload 不进入 Git／候选包。以受检 NativeProfileStore API建立新的 Test身份副本，保留完整库／UI／recognition，测试凭据和启动授权保持空值。实际新GUI完成搜索、筛选、Grid/List、详情中日文标记保存和重开；旧 Phase6程序 `7a3a739` 实际读取新保存副本并正常退出。另一 Test恢复目录完整库语义及设置与原快照一致，新旧reader均通过；没有 schema迁移需求，次数、时长及Session不变。

报告与失败尝试保留在 `.artifacts/phase8-1-20261009/report.md`。独立候选 `D:/Projects/The-Order-candidates/phase8-1-20261009/2b5e67a/win-x64` 核验635/635；同级 `try-private-copy.ps1` 是普通 Test体验，Standard、无250ms诊断、无Advanced资格。`start-daily-candidate.ps1` 是待批准的真实切换／程序回退入口，不得在本轮执行 Production分支。回退旧程序与恢复旧资料快照分开，后者必须保留较新快照并另获明确丢弃授权。真实游戏启动、计时、Production编辑、托盘和异常恢复的日用观察尚未执行；不代表Phase8全部完成或正式发布。

## 历史：Phase 7 完成，Release Candidate 可进入 Phase 8（2026-10-09）

**PHASE 7 COMPLETE — RELEASE CANDIDATE READY FOR PHASE 8。** 这是 RC 阶段完成／进入下一阶段的结论，**不是正式发布、Production 使用或替换正式程序的授权**。本轮只做已有证据复核、离线交付核验和文档归档，应用修改／构建／负载／ETW／宿主设置写入均为 0。

工作区 `D:/Projects/The-Order-worktrees/codex-phase7-finalization`，分支 `codex/phase7-finalization`，接管文档 HEAD `b689152ba5ef08290199519ff44dbf4880281992`。最终应用仍 **2b5e67a55d8eda905b8200652d189d7754dff112**；冻结 tag `codex/phase7-app-desktop-final-20261008` 未移动，应用源码无后续差异。新旧程序目录和 ZIP 的 manifest 项分别 635/635 一致（另有 manifest 自身，ZIP 共636文件）；程序 ZIP SHA256仍 `1FD295C9B96CB8A319648481C634937ABB174490ECCA15822B80FF0B63C5D501`。收尾文档提交独立绑定在 `.artifacts/phase7-final-rc-confirmation-20261009/checkpoint.json`。

用户明确接受宿主原 API 默认高对比度方案名 `""` → `高对比黑色`，以及将来启用时默认方案是否完全等价未验证。此项归为**用户接受的宿主测试环境残留**，仅解除其 RC 阻塞；不是精确恢复、应用修复或测试 PASS。本轮不再修改宿主。取消注销此前按用户范围排除为问题／阻塞，实际“应用已结束后重开恢复”的记录保留，不补成同一实例恢复 PASS。

桌面／键盘／人工 Narrator／策略及相关回归、原64MiB资源验收、独立 Windows 标准账户无SDK部署／单实例／合成升级重装回退／实际注销后重开，均按各报告的真实候选和范围继承；未再执行。没有其他尚未处理的 RC 阻断。历史 FAIL、未知归因、有限开发例外及冻结包内旧 Gate 文案均保留；当前结论由外部最终验收回执绑定，不把开发例外扩展为发布豁免。

普通入口仍 Standard、250ms JSON 诊断关闭，Advanced C1 仅显式受控 Test。VM 本轮实际为 Off，原 `Clean-Windows-Before-TheOrder` 检查点存在，未改变状态。最终矩阵、范围、体验与程序／资料回退分离规则见 [PHASE7_FINALIZATION](PHASE7_FINALIZATION.md)、[DEVELOPMENT](../../DEVELOPMENT.md) 和 `.artifacts/phase7-final-rc-confirmation-20261009/report.md`。Phase 8 待办为**现有原生资料快照、版本升级兼容、受控真实日用、回退与正式发布验收**，不包含 Electron→WinUI 数据迁移；本轮未执行 Phase 8。

## 历史：独立 Windows 验收完成，RC 当时仅余宿主方案名恢复（2026-10-09）

工作区仍为 `D:/Projects/The-Order-worktrees/codex-phase7-finalization` / `codex/phase7-finalization`；接管文档HEAD `c3dc7dcb64f1fa9d249b371645e3cb1d22233f43`，应用仍是 **2b5e67a55d8eda905b8200652d189d7754dff112**，未修改应用／构建／冻结包。最终新旧包635/635核验一致，程序ZIP仍 `1FD295C9B96CB8A319648481C634937ABB174490ECCA15822B80FF0B63C5D501`。

实际 Hyper-V `TheOrder-RC-Win11` / `e3282eb2-ab6c-4461-a72f-4f77effda189`、E盘路径及原干净检查点已核验；来宾 `DESKTOP-7GKL4M7`，CIM Windows11企业评估版26300。独立 `TheOrderTest` 不在Administrators，实际Session1/2运行，SDK0、dotnet/MSBuild不在PATH、未安装开发工具；.NET／WinUI／WindowsAppRuntime实际从包加载，Standard无Optical DLL、无250ms诊断。空库首次启动、实际保存／重开List、隐藏后的第二实例转发、真实托盘退出、旧91 UI→新2b保留合成编辑、只移除程序后重装、同根14文件快照回退均通过。快照是闭合合成Test操作演练，不是Phase8产品恢复功能。

真实来宾注销至锁屏→用户重新登录标准账户→新Session2重开，ProfileId/StoreId和库hash不变，最后托盘正常exit0。Windows取消注销时app已结束，只验证重开恢复，没有伪称同一实例恢复；**用户明确认为该非常规操作不作为问题或RC阻塞**，不再追加验证。没有活跃游戏／Session、真实关机／重启或实体GPU性能的新证据。

**ISOLATED WINDOWS VALIDATION: PASS（规定VM／账户／合成资料／负载范围）。RC GATE: BLOCKED，仅余宿主默认HC方案名精确恢复。** 本轮只读重验flags126/OFF、31色、动画均匹配原快照，但scheme原空字符串仍为“高对比黑色”；未盲改宿主。无新应用缺陷、资源负载或ETW，复用既有1813/1813、2b构建＋28/28及原资源通过证据。本任务app／任务／记事本已清理，VM和检查点保留。新报告与索引 `.artifacts/phase7-isolated-windows-20261009/run-20261009-070202/`；矩阵见 [PHASE7_FINALIZATION](PHASE7_FINALIZATION.md)，准确来宾入口见 [DEVELOPMENT](../../DEVELOPMENT.md)。不进入Phase8、Production或发布。

## 历史：App 桌面验收、对比度修复与新工程候选（2026-10-08）

工作区 `D:/Projects/The-Order-worktrees/codex-phase7-finalization`，分支 `codex/phase7-finalization`；接管HEAD5d89dfc，新应用 **2b5e67a55d8eda905b8200652d189d7754dff112**，tag `codex/phase7-app-desktop-final-20261008`。635文件候选 `.artifacts/phase7-app-acceptance-20261008/frozen-rc/2b5e67a/win-x64`，ZIP/源码/manifest独立绑定；435、f628、91及历史FAIL保留。文档HEAD和应用源码分开。

App真实桌面工具可用；已修复HC OFF后Dark caption变黑、放大文字卡片截断、Details搜索丢query/旧返回焦点抢输入；取消未保存提示保留Back意图。最后补齐继续游戏/识别所选/批量编辑复合按钮名称，2b实机及live provider核验。C1、cache/display surface/ownership和业务/schema未变，光学DLL与f628相同。435 Debug/Release与完整native1813/1813通过，2b仅三项Name属性，Debug/Release和28/28相关回归另验，完整核心回归按风险复用，不冒称重新执行。

真实Windows accent、Light/Dark、HC、透明/动画、100/150/200%DPI、有效1.44文字与窄窗、搜索/选择/菜单/未保存对话框/返回键盘均有对应源码证据。435 Narrator指定朗读/焦点/选中状态获用户“均成功”，真实托盘打开/退出获确认；exit0/hash不变。2b名称新路径实机补验，正常Test清理exit0/hash不变；第一次工具“physical Escape”中止原因未知，用户否认，reset后重试通过，原INTERRUPTED保留。

最终435 C1完成边界后13.949秒零绘制、隐藏/离开/Standard后owner/pending归零；2b只改名称，复用不变路径。继承f628两轮四组原64MiB资源PASS，不重开内存调查。宿主accent/history、text100、透明1、Dark、200%DPI、HC OFF/31色/动画与Narrator OFF恢复；**最初CLI默认HC方案名空字符串仍为高对比黑色，未精确恢复**。原registry存在性未采，不猜测删除；不能报完整宿主恢复或NON-ENVIRONMENT全PASS。干净无SDK Windows、独立普通账户、真实结束会话仍需独立环境。

**RC GATE BLOCKED**，不是Phase7完成或发布批准。最新矩阵 [Phase7 Finalization](PHASE7_FINALIZATION.md)，本机证据 `.artifacts/phase7-app-acceptance-20261008/report.md`、completion/checkpoint；体验命令见 [DEVELOPMENT](../../DEVELOPMENT.md)。未访问Production/真实游戏、替换正式程序、push或Phase8。

## 历史：Phase 7 Full Closure，资源与工程通过，RC Gate 仍阻断（2026-10-08）

独立工作区 `D:/Projects/The-Order-worktrees/codex-phase7-finalization`，分支 `codex/phase7-finalization`。接管HEAD `a6ee5635e9fe5ed33d249b8568fdc9a4688a75d9`、tracked clean；新应用源码 **f62860cd7b42dd92ca55887655e1e7a605127374**。新635文件self-contained x64包 `.artifacts/phase7-closure-20261008/frozen-rc/f62860c/win-x64`，程序ZIP／源码archive／manifest逐hash绑定；旧91b98d6包635/635复核未变。**RC GATE BLOCKED**，不是Phase7完成或发布批准。

固定decoded buffers在cache命中后仍重复新建显示WriteableBitmap并复制像素，因果对照3273→80次创建、1.882GB→49.5MB复制；已用按pipeline隔离的弱decoded引用身份复用display surface，未改64MiB decoded预算、清晰度、虚拟化、过期保护或C1。没有查明全部历史native retaining owners，不声称永久泄漏归因。真实HC试验发现不可用Fluent颜色的红色sentinel，改用系统contrast palette；WinRT 0x80070490警告仍保留，已用实际Win32 settings/color/theme事件补通知路径，无新timer，真实OS变化／C1退休通过。光学DLL与91b98d6逐字节相同。

最终clean f628 Debug／Release publish、90/90受影响回归与 **完整native Release1813/1813，0fail／0skip**通过，包含三项真实Electron合成密钥／profile envelope对照；Electron只是本地测试工具，不进入发行包。材质optional v1 companion、原native-ui schema、原子备份及串行保存不变；普通入口即使保存Advanced意图仍生效Standard、无250ms诊断。本机medium token初始化／保存／单实例、换路径重装、程序移除不删资料、快照新增修改默认拒绝及显式合成同根15文件精确恢复通过；实际14d8ede旧程序集能读新f628编辑的384合成资料。

两轮正常最终候选、共四组冷Standard／Advanced→Standard，按原384条／固定24ID／8warmup＋2×24同节奏往返／30秒静置与未改64MiB协议、原分析器全部 **PASS**：静置2−1为4.00／2.98／−4.72／−0.50MiB；相对预热38.71／47.72／41.96／54.14，重复段峰差≤11.71。每静置窗口15个1Hz样本、RAM／磁盘连续保护，owner／texture／pending0、无OFF绘制、hide/show、exit0、库hash不变。历史92.79／106.67FAIL、重叠无效实验、旧32MiB代理FAIL与有限开发例外完整保留；不扩大为历史全协议或全部硬件PASS。

实际HC／动画／透明关闭与恢复、Root.ActualTheme Light/Dark、文字1→1.44→1、长CJK窄窗及live XAML peers子集通过。host的文字／透明／appTheme值和类型精确恢复，HC flags126／31色／动画恢复；SPI默认HC方案名从空被Windows规范化为高对比黑色，精确名称恢复尚未完成。Computer Use node_repl导入前kernel-assets os error3阻断真实HWND、accent、100/150%DPI、完整物理键盘／Narrator；这属于**平台工具阻断，不是干净环境依赖或PASS**。真正无SDK Windows／独立非管理员账户／真实系统结束会话仍待隔离验证。详见 [Phase7 Finalization](PHASE7_FINALIZATION.md) 与本机 `.artifacts/phase7-closure-20261008/report.md`／checkpoint.md。下面Phase7.2 PASS仅为历史开发集成范围。

Phase6原生Production bootstrap及6.2日用Gate已通过。Phase8 **不执行Electron→WinUI迁移**，只准备现有原生快照、版本兼容、受控日用、回退和正式发布验收；本轮未进入Phase8、未访问Production、未替换或推送。

## 当前：Phase 7.2 最终集成验收与候选冻结（2026-10-08）

**PASS — DEVELOPMENT INTEGRATION ACCEPTANCE / TEST CANDIDATE FROZEN。** 应用源码 `14d8ede1b2240f30b153aee9f919bbd123385f48`，工作区和分支沿用下面本地集成记录。完整 native Release 回归 **1793/1793，0失败/0跳过**；修复后 Debug/Release publish 通过。首轮1792测试的两项失败保留：识别队列 idle 在最终结果发布前完成的实际竞态已用确定性红/绿测试修复；220项 Electron oracle 重新生成，仅更新旧源码哈希，全部用例与期望不变。

最终557文件候选为 `.artifacts/local-development-integration/frozen-phase7-2-final/win-x64`；不要把下节 `aa7dd7f` 原候选当作这次冻结版本。普通隔离入口 `.artifacts/phase7-2-final-acceptance/launch-frozen.ps1` 启动前核验完整 SHA256 清单，默认 Standard、250 ms JSON 诊断关闭；显式 `-Diagnostics` 为独立诊断入口。命令见 DEVELOPMENT。

新普通入口实际补验搜索/筛选、焦点上下文、多选、Details 返回、草稿在隐藏/恢复和取消退出后保留、保存读回及正常退出0；深浅主题和实际620DIP请求窄窗截图已保存。新诊断短验18.698秒静置材质绘制/图像任务零增量，开关/隐藏/离开后材质纹理为0、8创建/8销毁，安全降级/过期背景/异步离开 audit 通过。诊断库hash未变，普通库仅合成条目名称与custom标记发生预期改变。四个原工作区及历史候选hash重验一致。

C1和Claude视觉方向未变；旧native optical 132 readbacks/160动态回归按相同光学DLL字节身份复用。历史Private代理FAIL、未知归因与用户批准的有限例外完整保留：**已批准有限资源例外，完成开发集成；不代表资源协议全部通过或正式发布获准。** 高级材质默认OFF、Test-only；未验证的真实系统accent/HC、其他DPI/文字缩放、关机注销、真实游戏及设置持久化仍列入后续边界。没有新资源长测、Production访问、正式替换、push或Phase8实施。

最终证据：`.artifacts/phase7-2-final-acceptance/report.md`、协议、修复diff、完整TRX、557文件manifest、实际输入与释放记录；维护结果见[独立审查第12节](../design/claude-phase7/07_INDEPENDENT_REVIEW.md)。下面集成/视觉记录保留为历史。

## 当前：Claude UI 本地开发集成（2026-10-08）

用户已批准有限资源例外，完成开发集成；不代表资源协议全部通过或正式发布获准。当前工作区 `D:/Projects/The-Order-worktrees/codex-claude-ui-integration`、分支 `codex/claude-ui-integration`；可运行源码 checkpoint `aa7dd7f`。没有移动主开发、GPT 视觉、Claude 或独立审查工作区的分支，也没有处理其未提交修改。

从主开发提交 `14a96a1` 继承已提交视觉依赖至 `ad59c6e`，逐文件提取必要的同设备背景源、提前释放与光学回归依赖（`3da3997`），再按依赖顺序集成 Claude 的真实改动及 `95b0ad7` 修复（本地 `1acae7f`）。**未整体引入 `7d74e24`，该复制提交不是当前分支祖先。** 旧 Toolbar 捕获实现和历史调查驱动不属于本次集成；原工作区和历史证据全部保留。

已认可的 Fluent 方向、Windows Accent、普通点击 Details、多选/Shift 范围、关闭隐藏到托盘及正确退出流程保留。两处作品玻璃为 Library「继续游玩」Continuous 和 Details 标题 Clear；C1、同设备 Gallery 背景源、提前释放、过期背景/安全降级/异步离开/退出封锁/失败 owner 保留均未撤回。默认 **Standard**，高级效果手动 opt-in；普通隔离入口默认关闭诊断 timer/JSON 文字。Auto / Advanced / Standard / Transparency Off 保留，设置仍仅本次会话有效。

集成候选 Debug/Release 构建、294/294 相关回归、native optical 回归通过；普通入口实机设置、两处材质、Details 返回、托盘正常退出及独立诊断入口短验通过，两个 Test 库前后校验值不变。不是新资源协议 PASS，没有再次执行长测。历史热端点与 RC1 三项 Private FAIL、low Advanced PASS、GA1 旧候选范围和未知归因完整保留；只豁免 Private 代理 FAIL 对本次隔离开发集成的硬性前置，其余释放/正确性/降级/数据要求不豁免。

体验命令见 [DEVELOPMENT](../../DEVELOPMENT.md)；集成依赖、回退位置和限制见 [独立审查第11节](../design/claude-phase7/07_INDEPENDENT_REVIEW.md)。新候选、哈希、截图和日志在本工作区 `.artifacts/local-development-integration/`。下面各阶段段落保留为历史，不表示当前实现仍停在旧 Gallery-only 或固定灰绿方向。没有 Production 访问、正式替换、默认高级材质启用或远程推送授权。

## Phase 7.1 closure / Phase 7.2A Round 3（2026-10-07）

Phase 7.1 已正式 **PASS — PHASE 7.1 MEMORY MAJOR CLOSED / B0/M0/m0 / PASS FOR PHASE 7.2**。主工作区 closure 见 `D:/Projects/The-Order/docs/ai/PHASE7_PERFORMANCE_AND_RELIABILITY.md`；原 memory 诊断源码不复制进视觉分支。anonymous owner、global bitmap/GPU/COM、约3MiB residual、storage scaling、Level3独立验证均为 OBS，不再阻塞视觉工作。

Round 3 仅修复原六项：移除 Shell enclosing glass；窄窗零选择隐藏 bulk、范围/排序进入筛选、低频命令 overflow；Details 首屏去重标题并上移状态，无安装显示 compact next-action；Filter body 滚动而 footer 固定（条件仍即时应用，完成只关闭）；Dark brand semantic control resources 保持灰绿，系统 focus/high-contrast 不改；共享 owned Acrylic fallback 覆盖 Navigation/Toolbar/Details 与公开 FlyoutPresenter/MenuFlyoutPresenter，ContentDialog 用实色。OS caption/system menu 不在自有材质承诺内。

计时使用固定 240DIP 等宽数字区域；Test-only visual fixture 增加 bounded screenshot/layout/keyboard evidence 入口。Library 默认虚拟化与 inactive ItemsSource=null 保留；未修改 CoverPresenter/cache/业务服务/Session accounting。Level3 Gallery 保留 Test-only，未接入 Shell/Toolbar/Details。

本源码 checkpoint 后重新 build/publish，再补齐实际 DPI、键盘、text scale、motion/transparency 与六项 targeted evidence；完成后更新此处结果。新候选绑定与原始证据保存在 `.artifacts/phase7-2a-round3/`。旧轮 checkpoint/MVID 的证据保留为历史，不冒称本轮 final。视觉 Gate 只重审六项，仍须用户实际体验确认，不自动 merge/push/Productionize。


## Phase 7.2A：独立视觉候选，未合并

- 视觉源码仅在 managed worktree `C:/Users/MUSK/.codex/worktrees/winui-phase7-visual/The-Order`、分支 `codex/winui-phase7-visual`。基线为已提交 `14a96a1fc793b7fba231ab3b8fd1da607c3e86c5`；没有复制主工作区尚未提交的 memory diagnostics、fixed replay、plateau 或 checker 修改。
- 主工作区 Phase 7.1 **memory MAJOR 1 保持开放**；384-item 最终候选和 workload 已冻结，正式 2h soak 尚未在本轮执行。下面 Phase 7.1 段落是早期自检记录，不代表当前 memory Gate 已通过。
- 视觉候选采用「轻盈悬浮收藏库」方向：共同 token、收敛工具栏、较大封面、对齐列表、作品优先详情、底部事实驱动的会话区域。业务、持久化、Session accounting、匹配与 image/cache/CoverPresenter ownership 合同不变。
- 实时折射仅在显式 **Test + Diagnostics + VISUAL_TEST=1** 材质实验室；应用内部背景、公开 Composition API、18 个有界采样带。正常导航无实验室。不是桌面采样或 production-ready optical shader。
- 构建、profile、cache、screenshots 和测试输出均属于视觉 worktree。视觉审阅与 memory Gate 必须分别通过后才考虑合并；不自动提交原工作区、推送或替换日用程序。
- 实际证据、A/B 选择及尚未覆盖边界见 [Phase 7.2A 视觉审阅](PHASE7_2A_VISUAL_REVIEW.md)。启动方式见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 7.1：自检完成，等待 independent Gate

- 当前 Phase 6 独立 Gate 为 **PASS FOR DAILY NATIVE USE / PHASE 7**，受测代码 `7a3a739a3a758216419e8d5c0273a74e8e15561c`；证据 `.artifacts/native-phase6-2-independent-gate/gate-audit.json`。下方 Phase 6.2 paused 段落是 host handoff 前的历史记录，不能当成当前结论。
- Phase 7.1 从干净 `4292bf1de29d8e2d2d6509c554b26cb1e8679f77` 开始，确认相对受测代码只有文档差异。真实 Production 是当前 authoritative user store，本轮完全不访问 native Production / Electron profile 或真实 credential；只用新建 Test profiles、原创 synthetic covers 和合成 history。
- Phase 7.1 生产修复包括线性完整JSON数组校验、64MiB/512 decoded cache、256 work/waiter、两路decode、共享取消与新消费者续接、零布局recycled cover立即作废、不完整PNG拒绝。最后行为修复605addd；最终Test诊断candidate d3ed15c；1126b1f只修loopback测试shutdown。schema、Session accounting、launch/matching不变；五个共享程序集5763方法与tracked source通过二进制核对。
- **READY FOR PHASE 7.1 INDEPENDENT GATE**。最终Debug/Release各1763/0/0、原1734项全部保留，新增29项；locked restore、两配置build/publish成功。最终headless和actualWinUI各连续7200秒以上、正常exit0，硬资源/accounting/错图/排空检查PASS。自检B0/M0/m0/OBS9；首次recognition/handoff/host-child/COM失败保留且未宣称根因解决。详见 [Phase 7.1质量记录](PHASE7_PERFORMANCE_AND_RELIABILITY.md)，命令见 [DEVELOPMENT](../../DEVELOPMENT.md)。
- 新 published candidate 为独立 artifact，不自动替换日用 binary；本阶段结束等待 independent Gate，不开始 Phase 7.2 / Liquid Glass、SQLite、public release 或远端推送。
- 收尾补测 `9ff6040` 的同一安装原生Session追加（真实SessionEngine+native store，纯合成process adapters）：1k/10k/50k median约405/1453/4468ms，三组四次追加均count+4、time+0、资源排空，94项targeted通过。与d3ed候选5763个共享方法一致，生产代码未改、两小时证据不作废；含补测的检查器28697项PASS，108项本机latency参考见`acceptance-append/thresholds.json`。

## WinUI Phase 6.2：历史宿主阻断记录（后续独立 Gate 已通过）

- 基线 `ba37df62dbda23020dd1b85684b42953f999b430`、`codex/winui-phase5` 和干净工作区已核对。Phase 6.1 independent Gate 的 PASS 与六项 observations 保留，不把历史 handoff 波动称为已解决。
- 本轮明确授权最小真实 Production 验收，接入显式 `--allow-production-profile` / runner 开关，代码 checkpoint `7a3a739a3a758216419e8d5c0273a74e8e15561c`。默认拒绝正式根、fixture/其他 kind 不接受授权；guard、schema、Session/accounting 均未修改。新增 9 项授权回归，Debug/Release 全量各 1734/0/0；candidate 和程序集哈希/MVID 见 `.artifacts/native-phase6-2/candidate-identity.json`。
- 隔离目录中的同一 published candidate 可正常进入空书架并退出。首次名义 Production 启动产生空 bootstrap 文件后，被 `IsolatedLibrarySource.OpenChecked` 的精确物理路径检查拒绝，界面显示 `DataIsolationException`。只读实测所有 13 个文件的 handle final path 位于 `C:\Users\MUSK\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\TheOrder\Profiles\Production`，而请求路径是 `C:\Users\MUSK\AppData\Local\TheOrder\Profiles\Production`，links=1。当前 packaged Codex 宿主发生文件虚拟化，不能放宽 guard 或将此结果算作真实 Production PASS。
- 初始 absent 结论仅属于当前虚拟化视图，**未证明未虚拟化的实际 Production root 不存在**。失败进程已正常关闭；所有文件及 `.bak` 原样保留，并有外部 `safety/first-bootstrap-paused` 快照和哈希回执，后续只读检查确认未变化。没有添加真实数据、启动游戏、输入凭据、恢复演练、迁移、重建或 reset；Electron root 全程未访问。
- 暂存 manifest：ProfileId `72bc5132-4033-033b-3abe-c528c67e3d7a`，StoreId `ed629dd4-3207-8e47-3c0e-d71d2f212efd`，Production/format 1；library/install/session/count/total 均 0，credential unconfigured。它是保留的失败现场，尚不能指定为未来 authoritative profile。
- 自检：**BLOCKER 1（验收环境）/ 新确认产品 MAJOR 0 / MINOR 0 / 历史 OBSERVATION 6**。尚未 READY FOR PHASE 6.2 INDEPENDENT GATE。其余真实验收未做，停止 Production 写入；下一轮需先解决未虚拟化的启动/只读 preflight 边界，再决定是否继续，不能移动此目录冒充恢复。详细边界见 [DEVELOPMENT](../../DEVELOPMENT.md) 和 `.artifacts/native-phase6-2/production-readiness-summary.json`。

## WinUI Phase 6.1：Read Provenance / Stale-Write Remediation（等待 targeted re-Gate）

- 基线 `codex/winui-phase5` / `a0befa5895dd05181047be609d4545041f054a64`，最新 targeted re-Gate 为 **MAJOR 1 / OBSERVATION 5**；已通过的 ProfilePathGuard 和原 ownership exception safety 保留，scope reset 不变。
- 唯一剩余缺陷是新 main baseline 与旧 backup editable state 混用。现在 native writer 的加载 payload、recovery 来源和 stale token 绑定同一份已验证 authoritative main envelope；再次访问遇到 sharing/AccessDenied 明确失败，不 fallback。main generation 变化拒绝 Open/Save，解锁后重试读取当前 main。
- 确认 malformed/truncated/structural/missing main 才允许在 native borrow 前恢复兼容 backup → main，readback 后新建 baseline。已借出 writer 不能另选 backup；超限 main 继续 `LibraryRecoveryRequired → 显式 Restore → borrow`。Phase 2 通用 Open 本身不写盘，事务和业务 mutation 未改；Session accounting/schema/guard/Electron 未改。
- 新增 25 项回归和实际 child-process 原 Gate probe 复验，最终测试/构建/资源数值见 [DEVELOPMENT](../../DEVELOPMENT.md)。证据位于 ignored `.artifacts/native-phase6-1-provenance/validation-audit.json` 与提交后 `checkpoint-receipt.json`。旧测试和阈值未改，五项 observations 保留；不把本轮自检当作独立 Gate 通过。
- 最终 provenance targeted **25/0/0**、相关 **708/0/0**，完整 Debug/Release 各 **1725/0/0**，原 1700 项逐名称保留；recognition **44/0/0**、Windows Session **9/0/0**。两配置构建 0 warnings/errors、locked restore 通过；三轮独立 child-race 均无回退，新增资源循环 Debug **671→671** / Release **676→676**。自检 **BLOCKER 0 / MAJOR 0 / MINOR 0 / OBSERVATION 5**，**READY FOR PHASE 6.1 TARGETED RE-GATE**。
- 正式 native/Electron profile、凭据未访问；无真实游戏、重新发布、正式替换、推送或 6.2 工作。完成本次独立整改 checkpoint 后暂停，等待 targeted re-Gate。

## WinUI Phase 6.1：Guard + Writer Exception-Safety Remediation（等待 targeted re-Gate）

- 本轮从 `codex/winui-phase5` / `f797809983111e3f0f6becfc484dc13a803b203f` 的干净基线开始，核对 independent Gate audit/probes。该 Gate **REMEDIATION REQUIRED / MAJOR 2 / OBSERVATION 5** 优先于下方旧自检结论。仅整改 M1/M2，未改变 scope reset `62da4b46829602e3f4852d094489bbf15d10113b` 或进入 6.2。
- M1：新增共享纯 `ProfilePathGuard`，正常 profile 与 raw/fixture 均先规范 ordinary/extended DOS alias 并判断正式根 overlap/授权，再进入既有 filesystem identity/reparse/pin 检查。不支持的 UNC/device/NT namespace 在 I/O 前拒绝；raw 不能碰默认 Production/Development/Electron roots。Program 两条入口、library/credential/preferences/Session/persistence 与 backup/restore/open-existing 均经共享边界；ProfileProbe 也先 guard 再读 marker。
- M2：原先先置 ownership、constructor 再读 main，异常形成 phantom borrower。现在在同一 owner gate 内先验证 main/允许的 backup 和有界 baseline，无 I/O constructor 成功后才交付 ownership；异常不改变 flag，取消/后续 open 失败沿既有 dispose 清理。锁冲突可重试；有界 malformed/truncated main 保留 Phase 2 fallback；外层超 48 MiB 明确 `LibraryRecoveryRequired`，允许显式 Restore 后 borrow。没有自动覆盖、放宽 stale-write 或 backup identity/version/compatibility 门禁。
- 新增 `ProfilePathGuardTests` **321** 项与 `ProfileWriterRecoveryTests` **16** 项；相关 targeted **753/0/0**，含旧 profile 85 与 Session/persistence/transaction 331。547 次 alias/namespace/capability 拒绝断言均为零文件访问边界；synthetic-equivalent known roots 与正向 I/O 对照，不访问真实 Production 验证。跨进程 main 锁、第二 owner、十轮双 borrower、取消、三类损坏、容量边界、invalid/future/foreign backup、恢复后 stale-write 与 cwd/accounting 组合回归通过。
- Debug/Release 完整各 **1700/0/0**，原有 1363 项逐名称保留，最终全量均一次通过；locked restore、两配置 solution build（0 warnings/errors）通过。完整验证见 [DEVELOPMENT](../../DEVELOPMENT.md) 与 ignored `.artifacts/native-phase6-1-remediation/validation-audit.json` / 提交后 `checkpoint-receipt.json`。失败路径短轮次资源 targeted 472→472、Debug 全量中 672→672；Release 650→653 后稳定 21 个采样，原阈值通过；temp 0，owner/borrower 均可释放。旧测试和阈值未改；初期新测试的编译/有效输入/既有脱敏异常契约错误保留日志。
- 未修改 Electron、Phase 2 transaction/atomic primitive、Phase 5A/5B identity/accounting 或 durable schema；未访问正式 native/Electron profiles、凭据，未启动真实游戏、重新 publish、替换程序或推送。原五项 observation 保留（跨组件非原子备份、性能、短轮次资源、历史 recognition/Windows Session 波动）；不把回归绿灯当作历史根因解决或长期保证。独立 targeted re-Gate 尚待执行。
- **READY FOR PHASE 6.1 TARGETED RE-GATE**。整改自检 BLOCKER 0 / MAJOR 0 / MINOR 0 / OBSERVATION 5；不代替独立复审。独立整改 checkpoint 后暂停，不进入 6.2。

## WinUI Phase 6.1：Production Store Bootstrap（自检完成，等待独立 Gate）

- 基线 `codex/winui-phase5` / `b2a360a005aecd8bea1b18571eb78b75259c115b`，Phase 5B 已通过最终独立 Gate（`3d9a71ad920b5c20fe38772ef3e5c1698dd5010d`）。
- 产品事实更新：没有要求保留的 Electron 正式资料。旧 [PHASE6_MIGRATION_AND_STORE.md](PHASE6_MIGRATION_AND_STORE.md) 已标记 **SUPERSEDED**，仅保留历史，不再是实施计划。
- 当前规范为 [PHASE6_PRODUCTION_PROFILE_BOOTSTRAP.md](PHASE6_PRODUCTION_PROFILE_BOOTSTRAP.md)。6.1 只在隔离 synthetic production-like profile 实现新库初始化、物理隔离、身份与版本门禁、单 writer、备份恢复和原生凭据/偏好；不建立正式 profile，不自动导入 legacy。
- 6.2 为 **Production Readiness / First Real Profile**，以后另获明确授权才创建第一份真实 native production profile。旧 Electron 归档可另行授权；无无损迁移、回滚到 Electron、双 writer 协调或 SQLite 要求。
- scope-reset 文档 checkpoint：`62da4b46829602e3f4852d094489bbf15d10113b`。已完成 native profile owner、稳定身份、严格版本/能力门禁、崩溃续作、跨进程单 writer、Phase 2 共用原子写与实际 restore。新增 manifest/envelope v1，库内部、Session accounting、cwd、cover 和 DPAPI model 保留；备份来源传回原 SessionEngine，不能恢复活动进程的接管资格。
- 最终 Debug/Release 全量各 **1363/0/0**，原有 1278 项逐名称保留，新增 85 项；locked restore、两配置 build（0 warnings/errors）与隔离 publish 通过。最终 Windows Session 分组 **9/0/0**，recognition **44/0/0**，独立性能/资源 **4/0/0**。实际 WinUI 已验证空库、偏好重开、发布默认 Production 与正常退出（exit code 0）；全部指向隔离 production-like 根。
- **READY FOR PHASE 6.1 INDEPENDENT GATE**。自检未发现新的 BLOCKER / MAJOR / MINOR；保留两项既有测试间歇失败观察，分组与最终完整复验均通过但根因尚未确定，未改原测试期望。完整数字、限制及源码/二进制/checkpoint 绑定见 [DEVELOPMENT](../../DEVELOPMENT.md) 与 ignored `.artifacts/native-phase6-1/validation-audit.json`、`checkpoint-receipt.json`。正式 Electron/native profile 和凭据未访问，无真实游戏、Electron 改动或推送；暂停等待独立 Gate，不进入 6.2。下方旧阶段状态为历史记录。

## WinUI Phase 5B.2：Persistence Retry Remediation（等待 targeted re-Gate）

- 本轮从 `codex/winui-phase5` / `9f1aef2e944f8ad07c31a5b43f6f7c84b97d678d` 干净基线开始，独立 Final Gate 的 **REMEDIATION REQUIRED / MAJOR 2** 取代下方历史自检结论。只修两项 persistence retry，不进入 Phase 6。
- `AppSessionHost` 分离一次 engine/writer 创建与可重试 recovery attempt；失败/取消后复用同一 engine，Initialize/Explicit Exit 并发共享 attempt，成功幂等。清理先封闭 retry、等待在途工作，不让旧完成回调复活 host。
- `SessionEngine` Exit 不再跳过未 durable 的 terminal；复用现有 Record/Durable 与 transaction receipt，固定终态候选的 cutoff/EndedAt/final delta，补写成功后重复 Exit/checkpoint no-op。没有重新打开 terminal、补离线时间、增加 count 或新增 durable schema。
- 新增 22 项，新增测试 **22/0/0**、相关 targeted **192/0/0**；Debug/Release 最终完整 suite 各 **1278/0/0**，原 1256 项逐名称保留；两配置 build 0 warnings/errors、locked restore 通过。首次 Release 有 1 项既有 handoff 资源阈值失败，保留日志，不改阈值，单项及随后完整复验通过，纳入资源观察项。未修改原 Gate probe 源码，引用本次 DLL 复验后原三条失败路径均已修复。证据与源码/二进制身份详见 [DEVELOPMENT](../../DEVELOPMENT.md) 和 `.artifacts/native-phase5b2-remediation/`。
- 未改 Phase 5A/Phase 2 contract、共享 codec/Electron、recovery identity、occupancy、Tray/Single Instance/Presentation；没有重新启动真实游戏、访问正式资料、重新发布或推送。旧 publish-final 仍对应 a11704b，不能混作本次修复候选。原 3 项 OBSERVATION 保留，独立 targeted re-Gate 尚未通过，不能宣称 Phase 5B 完成。
- **READY FOR PHASE 5B.2 TARGETED RE-GATE**。整改自检 BLOCKER 0 / MAJOR 0 / MINOR 0；提交后暂停，等待独立复审。

## WinUI Phase 5B.2：App Lifecycle / Tray / Single Instance（自检完成，等待独立 Gate）

- 开始于 `codex/winui-phase5` / `5f57ad8e0a071c2f3c4aed673f546fcf9c2ab792`，核对干净工作区。该基线已通过 5B.1 targeted re-Gate；本轮授权正式接入 5B.2，尚不代表 5B.2 独立 Gate 通过。
- 已实现 AppInstance 在 writer/engine 前分流、application-owned `AppSessionHost`、恢复完成后开放操作、同 writer 刷新、关闭到托盘、显示/暂停/恢复/明确退出、保存失败取消退出及 bounded shutdown/logoff。窗口隐藏不 dispose engine；Explicit Exit 使用 RecoveryPending，不使用 Stop Observing；重启沿用既有身份恢复并诚实显示 CurrentProcessOnly。
- Session UI 直接读取已确认 accounting，不增加全局 stopwatch。Paused、Limited、Unknown、Incomplete 和真实 Completed 分开；未改核心计次、awake 时间、5A facts、Phase 2 transaction 或 recovered terminal occupancy 的当前进程边界。新增正常 UI 不暴露普通“停止观察”。
- 新增 `AppLifecycleTests` / `AppLifecycleWindowsTests` 共 28 项，完整 Debug/Release 各 **1256/0/0**、原 1228 项保留，两配置 build 0 warnings/errors、locked restore、Electron 665/0/0、artifact 32/0/0 通过。真实 WinUI 已验证 simultaneous startup、暂停关窗/唤回、Explicit Exit/no-kill、并发重启只恢复一次、Limited 继续、约 55 秒离线不补时及 Tray failure 可访问；真实托盘 Resume 菜单由用户操作并确认，随后运行时/JSON 复核。43 次 secondary 激活全部退出，预热后 20 次 handles 1077→1077；Windows shell icon/subclass 30 轮、Close/Show 策略 50 轮通过。
- 发布源码 checkpoint **a11704b902d0c643385efa2209713f3f7fff8c01**；唯一候选 `.artifacts/native-phase5b2/publish-final/win-x64`，Release WinUI MVID **058e7e46-b091-40ab-ad95-3d2682ff50f7**，使用 `acceptance-profile`。`published-identity.json` 与最终 `checkpoint-receipt.json` 绑定源码/二进制/验证，Published 入口要求显式绝对 `-PublishedPath`。8 组 Debug Windows UI 与 9 组最终包 UI manifest 分开，最终文档提交不冒称二进制内嵌 SHA。
- 本轮真实 Game-A **2 次**：第一次暂停 55.656 秒累计 ticks 不变，关窗同 Session 继续，正常退出 Completed 164 秒；第二次 Explicit Exit 游戏存活，约 138 秒离线后同 Session/install/count 恢复，ticks 精确不变且 CurrentProcessOnly，随后正常退出为 Incomplete 312 秒，A 总计 476 秒/2 次/2 条记录。Game-B **1 次**：44140→45024 handoff，正常退出 Completed 45 秒/1 次/1 条。仅主菜单与退出；全部验收进程正常退出，A/B 目录元数据及 EXE hash 不变。一次缺少 native profile 参数的启动被隔离保护拒绝，成功恢复证据另列。Game-C 未重跑，旧 cwd 证据明确标为历史。
- **READY FOR PHASE 5B.2 INDEPENDENT GATE**。自检 BLOCKER 0 / MAJOR 0 / MINOR 0，OBSERVATION 3：真实 host 生命周期待人工验收；终态 occupancy 当前 engine 生命周期边界不变；短轮次本机资源与用户托盘操作证据不代表长期/跨机器保证。未改 5B.0/5B.1 核心规范，未访问正式资料、未推送；完成最终 checkpoint 后暂停，不进入 Phase 6。完整分层证据、发布 hash 和后续人工步骤见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 5B.1：Recovery Occupancy Remediation（2026-10-05）

- 本轮从 `codex/winui-phase5` / `3cb1984d2bf2e4186b6fc95aca36f436d72f2b80` 干净工作区开始，只修 independent Gate 的唯一 MAJOR：恢复 A 后停止观察，A 尚存活时 B 被错误放行。该 Gate 结论为 **REMEDIATION REQUIRED**，优先于下方历史实现自检声明。
- `SessionEngine` 在成功恢复时保留独立内存 occupancy，复用完整 `SessionRecoveryIdentity`（PID/creation/image/file identity、game/install 与原 folder/exe/cwd binding）。停止观察仍终结为 Incomplete、停止 timing、释放 observer、清除 durable recovery token，但不释放此占用。普通 Session/personal transaction 不改占用；没有新增 durable schema。
- 后续启动在原 `launching` gate 内有限复核：完整身份与安装仍有效为 Active；查询失败或不能确认属 Unknown；二者均 AlreadyRunning，不创建进程/Session、不计次/计时。只有明确退出或 PID creation generation 不同为 Inactive，才释放后调用既有 5A admission；并发请求不能穿过复核与释放之间。
- engine 拥有该无句柄记录；每次复核临时查询 handle 由 `using` 释放，最终 Inactive 或 engine dispose 清除记录。未修改 5A Process Fact / identity、Phase 2 transaction、Session accounting、共享 codec 或 Electron compatibility 层。当前进程外的 terminal reservation 持久化、正式 cutover、UI/Tray/Single Instance 均不在本轮。
- **READY FOR PHASE 5B.1 TARGETED RE-GATE**（整改自检，不代替独立复审）。新增 19 项；Session/recovery 定向 **128/0/0**，Debug/Release 全量各 **1228/0/0**，原有 1209 项逐名称全部保留；两配置最终 solution build 各 0 warnings/errors。真实 Windows 23 次拒绝后的 handles Debug **727→727**、Release **677→677**，A 正常退出后 B 各只创建一次；原 Gate probe 复验 A PID20360 仍活、B AlreadyRunning/无进程/无 Session/count 不变。证据与 hash audit 存于 `.artifacts/native-phase5b1-remediation/`，详见 [DEVELOPMENT](../../DEVELOPMENT.md)。未发现新的 BLOCKER/MAJOR/MINOR；完成 checkpoint 后暂停，不进入 5B.2，不推送。

## WinUI Phase 5B.1：Session / Playtime Core & Lifecycle Engine（2026-10-05）

- **READY FOR PHASE 5B.1 INDEPENDENT GATE**（实现自检，不代替独立 Gate）。Debug/Release 全量各 **1209/0/0**，原有 1108 项逐名称全部保留，新增 101 项；两配置 build 均 0 warnings/errors、locked restore 通过。Electron **665/0/0**、build/import、artifact **32/0/0** 通过。checkpoint 后暂停，不进入 5B.2、不推送。
- 从纯设计 checkpoint `17b91810fa0db9d967a56fc426892608bb6294c1`、分支 `codex/winui-phase5` 开始。用户本轮授权将原计划的 Domain、accounting、Process Fact integration、Windows awake clock、suspend 与 recovery core 合并为 5B.1；原 5B.0 的分阶段编号为历史计划。尚未接入 WinUI、Tray、Single Instance 或正式 profile，不代表桌面产品已经启用计时。
- 已实现 `SessionEngine` 单消费者有界事件队列、独立只读进程采样、v1 native history 扩展、串行原子差额记账与恢复资格。实际 launch operation 是 SessionId；创建成功但身份未知也形成零秒 invocation；首次 durable 保存计次一次，handoff/pause/recovery/replay 不再计次。真实退出事实与保存 cutoff 分开；未知、睡眠、离线、写盘等待不补时。
- Windows 时钟使用 precise unbiased interrupt API，双时钟断点与 power callback 冻结区间；DPAPI token 绑定物理 profile、BootId、PID/creation/image identity、原安装路径身份。重开仅恢复同代进程为 CurrentProcessOnly；backup/import/多候选/身份失效全部拒绝自动接管。未改 5A Process Fact / identity contract 或 Phase 2 原子替换保证。
- legacy 四字段、ID、count、aggregate surplus 保留；未知新 schema 只读，不退回备份覆盖。可移植导入/导出清除恢复资格。旧 Electron 不能与 native 共用可写 profile，也不保证它保留 native 扩展。
- 本轮验证清单与准确结果见 [DEVELOPMENT](../../DEVELOPMENT.md) 的 Phase 5B.1 节；证据位于 ignored `.artifacts/native-phase5b1/`。Windows 合成 EXE 证明直接/多级接续、真实退出时间与管理器释放后进程仍活着；休眠/重启采用语义及身份故障注入，没有实际让宿主休眠/重启，没有本轮真实游戏验证。下方各阶段为其当时的历史记录。

## WinUI Phase 5B.0：Session / Playtime 语义设计 Gate（2026-10-05）

- **READY FOR PHASE 5B IMPLEMENTATION**。规范见 [PHASE5B_SESSION_SEMANTICS.md](PHASE5B_SESSION_SEMANTICS.md)，包含状态机、真实 Process Fact 映射、计次/时长/恢复/兼容契约及分层测试矩阵。这里只完成设计，Phase 5B production implementation 尚未开始；纯文档 checkpoint 后暂停。
- 开始时核对 `codex/winui-phase5` / `041ddca804a134aa146ee7b884d6eff596f451d0`，工作区干净。此前独立 targeted Gate 已通过 Phase 5A.1 且没有修改代码；该 SHA 仍是实现基线，下方“整改自检完成”是历史证据，不能把本次文档提交当作新的实现或二进制 checkpoint。
- 本设计规定：新 Session 首次 durable 创建计次，awake 时长排除睡眠；同 Session handoff、legacy total 保底、原子差额记账；重启同身份恢复不补离线时间且降为 CurrentProcessOnly / Limited；保留全局单 owned launch。关窗默认托盘和零秒成功启动计次是相对旧 Electron 的明确行为变化。均待后续实施验证，未操作正式数据、凭据、真实游戏或发布包。

## WinUI Phase 5A.1：Process Fact Remediation（整改自检完成）

- 可信起点 `codex/winui-phase5` / `9b9b15983124aaf3622056475037b665dd68a2e4`。独立5A Gate为NO-GO；本轮仅修M1–M4与external AlreadyRunning占用，保留分层，不进入5B。
- M1：Windows创建时原子关联非kill Job，补充连续的后代存活事实；可见且被证明是唯一存活者的当前进程形成authoritative checkpoint。最终退出要求保留句柄的真实exit、scope为空、checkpoint后无新出生及无未解决歧义。漏掉helper不能把空快照当作结束。Limited之后有界复核scope，空才释放admission，不补造exit。
- M2：snapshot capture开始时间、同一候选句柄的真实parent PID、父代identity/已确认退出区间共同约束；后续Attach仍校验完整identity。M3：cwd普通路径重新观察并比较目录file ID/final path，非等价明确拒绝。M4：Started/Exited发生时间与ObservedAtUtc分开，最终ExitOccurredAtUtc不包含6秒接续等待。
- external AlreadyRunning只保存外部身份用于后续复核，不产生owned session；同代存活/Unknown拒绝重复启动，旧代退出或PID换代后可重新走正常启动链。不写playtime、次数、history，不访问正式资料/credential，不改真实游戏文件。
- Job accounting→membership→accounting前后必须一致，6项确定性回归防跨查询混合状态；整个整改新增31项、Launch及整改96项。最终Debug/Release各**1108/0/0**，build各0warnings/errors，locked restore、Electron665/0/0、build/import、artifact32/0/0、Release publish均通过。最终10轮handle Debug653→640、Release675→654稳定；一次早期654→661阈值失败保留，未改门槛，单独及最终完整复测通过。
- 本轮A直启/退出、B39256→14568接续/主菜单/退出、C显式cwd/主菜单/正常退出通过；A/B真实OS退出时间与Application精确一致。C独立watcher的0FILETIME作废，不当作精确时间交叉验证。最后跨查询加固后，最终包额外复验B35468→31244、正常退出时间08:45:42.0073080Z一致，manager46636 exit0。A/C证据属于加固前候选，最终fixture完整覆盖，不冒充重跑。
- 隔离library逐字节不变，不写时长/次数/history；A/B游戏元数据不变，C仅既有启动日志，无新增/删除。8组UI manifest/12截图按实际两批build绑定并核查。最终包 `.artifacts/native-phase5a1/publish-final/win-x64`，MVID f5bffdd1-7798-4d10-a408-35b583972075；内嵌父SHA9b9，source/bundle hashes+receipt绑定本轮checkpoint。具体契约/入口与broker、breakaway、UNC未验证边界见DEVELOPMENT。
- **PASS FOR PHASE 5B（本轮整改自检）**；当前阶段提交即Phase5A.1 checkpoint，精确SHA见Git及本机receipt。完成后暂停，未进入5B，不替代独立Gate；下方旧5A本机通过不代表其独立Gate通过。

## WinUI Phase 5A：Game Launch & Process Continuity（本机 Gate 通过）

- 从可信Phase4 checkpoint `7965bee95c49750e26b85302da037582b96a54fc` 创建 `codex/winui-phase5`；Phase5A本机Gate通过，当前阶段提交即Phase5A checkpoint，精确SHA见Git及本机checkpoint receipt。**提交后暂停，未进入5B**。
- LaunchViewModel→GameLaunchService/serialized library admission→Windows process平台。不可变snapshot、独立launch lifecycle、最终folder/exe/cwd物理身份复核、structured ArgumentList、明确cwd、首选/本次显式安装；位置首次核对后保存在隔离机器DPAPI，非legacy/session数据。保留single-active，分开Started与TrackingLimited，PID+creation+image+retained handle防复用，有限parent/child唯一窗口接续，不选同目录最新进程。
- cancel/shutdown不kill，不写时长、次数、历史、session persistence或tray session。native候选窗口关闭真正退出，旧Electron活动窗口隐藏托盘语义留待5B；正式资料及正式程序没有接管。
- 自有fixture新增65项production-path测试通过。真实验收暴露并修复两处通用问题：关闭后迟到的Details.Unloaded访问已释放CTS（2项回归先失败）；扩展EXE路径影响旧引擎/按路径兼容行为（2项读取实际Windows命令行的回归先失败）。前者增加disposed保护；后者仅对再次验证为同一物理文件的短路径使用普通Win32写法，身份、固定句柄、structured args及安全边界不变。未修改GPU设置，无游戏名特例。
- Game-A x86直接启动、实际cwd、AlreadyRunning、正常退出检测通过。最终候选PID7904 exit0，Game-A PID44384/creation134356586222243292保持运行且窗口可用，随后游戏正常退出；此前用户同时关闭所有进程不算证据，早期manager崩溃的no-kill也不算clean shutdown PASS。
- Game-B按用户纠正使用真正游戏入口，原扩展路径只有无窗口子进程；普通路径对照成功，最终WinUI也成功：入口31608→同目录子进程28316，父子/创建时间/唯一窗口接续、主菜单、cwd、正常退出检测全部通过。GPU偏好注册项只读确认仍存在，没有直接测量最终GPU选择；不能把路径对照结果夸大为驱动根因证明。旧错误launcher与无窗口证据已supersede。
- Game-C x64资源在已有`files`子目录。仅修改隔离样本的explicit workingDirectory并重新核对位置，最终PID2572正常主菜单/实际cwd/退出检测通过，未移动资源或改游戏配置。备用Game-D只作有界选择，因B已提供真实handoff，没有执行。
- 证据`.artifacts/native-phase5a/`：26组manifest/27截图，requested/runtime、长度及SHA256已核验，无重复截图hash；`win32-b-real-menu`像素被管理器遮挡，明确INVALID，由`win32-b-real-menu-foreground`替代。最终包`win32-fixed/win-x64`，WinUI MVID db300925-99ba-455e-bc36-93e2ee5d55b3；全层bundle hashes与最终commit receipt绑定，不把内嵌父SHA7965当成本阶段commit。
- A/B目录元数据无变化，C仅既有启动日志变化；无新增/删除文件，无The Order/代理写入游戏目录的操作，不冒称内核级IO审计。隔离4条library SHA256逐字节不变，时长/次数/history未写入。未访问正式profile、API凭据、修改图形设置或替换正式Electron。
- 最终Debug/Release完整C#各1077/0/0；65启动专项、Electron665/0/0、build/import、artifact32/0/0、locked restore、两配置0warnings/errors、Release publish通过。未进入5B。UAC、DRM、broker、未观察到的快速中间进程、UNC、需要扩展语法的长路径及跨重启恢复没有扩大承诺。完整旧Electron行为表、证据版本与baseline见[DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 4R：Real API / CDN Compatibility Validation（2026-10-05 完成）

- 从已通过 targeted re-Gate 的 `codex/winui-phase4` / `c077fd257c5195e790a540f77c571269375a7f0d` 继续。**REAL API SMOKE PASS；PHASE 4 COMPLETE — READY FOR PHASE 5**。只完成本阶段并暂停，没有进入 Phase5。以下旧节中的“未验证真实 API/CDN”仍描述其历史 checkpoint，本节补充当前真实服务证据，不代表正式部署或全部真实游戏兼容。
- 只使用用户提供的专用 test credential、`.artifacts/native-phase4r/profile` 的 DPAPI 和3条合成库。未访问正式 profile/凭据/真实游戏，未替换 Electron、迁移 SQLite 或推送远程。测试凭据按授权保留于隔离 DPAPI，明文全仓审计0匹配。
- 用户修正测试 DNS 后，`image.kungal.iloveren.link` 返回真实公网 IPv4/IPv6，原生产 IsPublic 检查通过；Fake-IP、私网、DNS/IP校验、TLS、无任意跳转规则均不变。API默认代理策略和图片无代理路径在当前网络均成功；本轮没有更改系统网络设置。
- 真实 CDN 暴露静态 **WebP** 兼容缺口。获授权进入局部4R.1：仅 RemoteCoverSource 增加 `image/webp`，WindowsRasterCodec 增加有界 RIFF/WebP 静态容器校验并使用原 Windows WIC 解码。动画/坏长度/超大画布拒绝，20MiB/4000万像素/目标边长/并发边界保留。新增10项生产路径回归，先3失败7通过，再全部通过；无依赖升级、识别规则或事务修改。
- 真实鉴权、日文/中文/零结果搜索、分页20→40、详情133/15/919的请求=响应=canonical ID、3张真实WebP、缩略图与A→B→A缓存通过。无效ID为HTTP400/INVALID_PARAMETER→InvalidRequest。实际可选null/缺失cursor等被原DTO正确处理，无schema修改；canonical merge、429、其他CDN格式未自然遇到，不主动枚举或制造限流。
- 最终发布包真实WinUI：连接/搜索/分页/详情/封面/识别/Review/Apply/Manual/重启通过。千恋万花15明确候选0.985、3独立来源一致；Summer Pockets4候选，版本不确定/编号冲突正确阻止Apply。隔离Apply15、明确Manual4成功，错误Manual零库写入；重启个人名称、笔记、评分、分类/标签、安装和游玩数据保留，UTC时间戳仅格式规范化；原合成库没有个人封面，不冒称非空个人封面实测。
- 真实响应受控延迟的生产VM harness验证快速换查询、取消、旧分页、移除/替换同一凭据；旧上下文不接纳，UI也验证远程候选撤销而本地证据保留。最终包活动关闭另使用仅候选进程的透明TLS延迟通道，未解密/更改响应或TLS校验，真实连接处于Loading时关闭、exit0，通道已停止；普通综合smoke仍用正常网络。早期两个关窗观察有采样间隔，不能单独证明活动关闭。
- locked restore、Debug/Release build各0 warnings/errors，完整C#各 **1012/0/0**；102 original、13 filesystem/cover、32 online、254 recognition差分及14fault保留。Electron完整 **665/0/0**、build/import回归、Release publish通过。
- `.artifacts/native-phase4r/` 保存 **13组最终截图/tree/manifest**，requested==runtime、SHA256及构建MVID核对；`real-service-harness.json`只有脱敏schema/状态摘要，不保存原始API响应。旧Fake-IP blocker及封面失败证据由本节/最终证据supersede，保留为历史。最终包嵌入父SHA c077，使用build-identity/提交后receipt绑定本次checkpoint，不能声称嵌入最终commit。
- 剩余边界：自然merge/429未遇到、离线以确定性回归为准；WIC/WebP在当前Windows验证，干净机codec/发布签名升级仍需后续验证。此前UNC/reparse、10k unique images及正式库写入前workingDirectory门禁继续保留。详细证据见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 4B.1.1：Final Recognition Gate Remediation（整改自检完成）

- 基线 `codex/winui-phase4` / `41fa1e0a573d83f232e3d5e8d2d2011bf884acc6`。Phase4B.1 targeted re-Gate实际判定 **NO-GO**，本轮只修剩余M1/M5；本节 supersede 下方旧M1/M5完成及放行声明，其余已通过整改保持不变。没有改评分/veto或Review UI，没有进入Phase5。
- **M1** native PE资源选择、标题去重/排序均在有界截断之前；每程序16标题/8公司、总40标题和IO上限不扩张。`pe-evidence-v2`使用实际返回的有效有序输入，`native-local-evidence-v3`还绑定实际总40条输入；algorithm身份`electron-recognition-v1-native-4`。旧Electron raw PE fingerprint及`electron-review-identity-v1`不变。39目录+chapter1/2原反例现在重排后digest/input/ranking/veto一致；真正有效标题变化拒绝旧Apply。
- **M5** 明确Uninitialized/Initializing/Ready/Failed。初始化前live结果保留在原有有界queue中，不发布到Review字典；完成读取后同一gate取最新generation状态，与历史合并并统一retirement/capacity/结构检查，最后一次性发布。容量失败保留合法基线并显式ReviewCapacity；取消可重试，读取损坏降级内存且原文件保留。决定入口等待初始化，Flush不会写入初始化期间的空快照。原active保护及256历史退役策略不变。
- 新增**14**项production-path/integration回归，覆盖PE排列/资源上限/实际40条截断、旧Gate反例与Apply，及暂停near-16MiB读取、live接纳、10000历史、多generation/双初始化、容量拒绝、初始化失败/取消、随后Ignore/Apply。完整Debug/Release C#各 **1002/0/0**，保留既有988及102/13/32/254差分组与14fault；两配置build各0 warnings/errors，locked restore、Electron **665/0/0**、build/import regression、Release publish全部通过。
- 证据 `.artifacts/native-phase4b11/`：完整TRX/日志、`validation-audit.json`、`build-identity.json`及提交后的receipt；发布程序仍嵌入父SHA41fa1e0a，需hash+receipt绑定最终checkpoint。没有新增桌面smoke；真实Windows有界reader及production集成测试证明本轮反例，旧UI artifacts只证明旧场景。详细契约与边界见 [DEVELOPMENT](../../DEVELOPMENT.md)。
- **PASS FOR REAL API SMOKE（仅本轮self-check）**。没有开始真实API smoke、使用真实credential/正式profile/真实游戏；不代替独立re-Gate，也不代表真实catalog/CDN或干净机发布验证。建立checkpoint后暂停，不进入Phase5。

## WinUI Phase 4B.1：Recognition / Review Boundary Remediation（整改自检完成）

- 从 `codex/winui-phase4` / `7da6eebab3c79b66af65e628daad8ae1788644d4` 整改独立Gate的M1–M5及两个MINOR。保持分支，未进入Phase5。本节 supersede 下方旧Phase4B关于证据完整性、候选详情绑定、provider自动名称、历史校验与预算已经充分通过的判断；下方同名“4B.1内部checkpoint”是旧开发子步骤，不是本轮整改。
- **M1** 保留Electron兼容PE fingerprint和`electron-review-identity-v1`；新增`pe-evidence-v1`（消费的四字段、presence/effective output、Ordinal排序、SHA256、原资源边界），纳入`native-local-evidence-v2`。Apply在原事务beforeCommit重新读取证据，PE原地变更即Stale/zero writes。LegalCopyright等未消费metadata不参与新摘要；资源顺序/culture不影响相同有效证据。algorithm为`electron-recognition-v1-native-3`。
- **M2/M3** 候选选择独立generation，切换即取消并清空详情；接纳绑定review/candidate/selection/request/auth。Reidentify只把NameIsCustom=true的个人名作为名称证据；有旧metadata的false/null自动名不进入scoring，原authoritative Input.Entry仍保留。reader继续提供目录、程序、配置/说明和索引来源。
- **M4/M5** historicalCandidates即使Result=null也验证非null/编号/有界文本/有限0–1分数/数量，live result校验证据和veto状态。坏Review保留原文件且持续显示明确告警，不覆盖、不冒充空队列，Library可用。接纳前对拟定状态精确有界序列化：10000总条目/16MiB/5候选；保护NeedsReview，Stale/NeedsRecheck按CreatedAt再Guid最旧先退役，历史最多256条且必要时再缩减。空间仍不足则显式ReviewCapacity且旧状态不变；启动、失效说明及Manual清理同样先预算，已提交后清理错误仍是CommittedWithWarning。
- **两个MINOR** VersionNeedsConfirmation与confirmed conflicts分开，但均阻止不安全直接Apply；凭据epoch变更只撤销remote enrichment，保留current local identity/candidates/evidence，recognition generation/lifecycle变更仍使全结果过期。
- 新增**44**项production-path整改回归；locked restore成功，最终Debug/Release build各0 warnings/errors，C#各**988/0/0**。保留102 original、13 filesystem/cover组、32 online、254 recognition差分及14 fault；artifact tests **32/0/0**；Electron完整**665/0/0**、build/import回归通过，Release publish成功。`test-audit.json`核对两份TRX；未升级依赖或修改Electron。
- 真WinUI隔离验收：200已加载切201、延迟200切201后迟到丢弃、Apply精确保存201、FD显示“版本信息不足，需要确认”、重新识别旧provider标题排除、模拟凭据变更保留Summer Pockets本地索引、Ignore和Manual均完成。损坏历史原SHA256不变且新识别5项完成；容量10000历史→256→接纳5项261→Apply260→Ignore259→Manual258（256历史+2待核对），个人名称/笔记保留。最终两个实例exit0。PE zero-writes/ABA/最终publish前auth时点等由确定性集成测试证明，不冒称真实游戏/API操作。
- `.artifacts/native-phase4b1/`保存**11组manifest/UI tree/JPEG，11个不同截图hash**，requested==runtime、树状态、长度/SHA256已独立核对，并目视复核关键文件。8组早期publish证据覆盖候选/版本/名称/auth/普通决定，3组publish-final证据覆盖损坏提示及容量/决定最终修复；范围和全层DLL/EXE hash见`build-smoke1.json`、`build-final.json`及DEVELOPMENT。最终WinUI MVID `7d69294c-61ee-411f-baad-1df14773ac6e`；二进制内checkpoint仍是父SHA，不能当作最终commit身份。旧Phase4B证据不证明本轮整改，旧Phase3A作废证据继续作废。
- **PASS FOR PHASE 5（仅Phase4B.1整改自检）**。独立targeted re-Gate和其后的真实API smoke尚未执行，需按既定顺序继续；本轮未访问正式profile、真实credential或游戏目录/执行。合成HTTP不代表真实catalog/CDN、长期压力、全部真实游戏或干净机发布；旧UNC/图片/workingDirectory门禁仍保留。完成checkpoint后暂停，**不进入Phase5**。设计、production位置、回归及精确artifact范围见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 4B：Metadata Recognition & Review Queue（历史本机验收；独立 Gate NO-GO，整改见上）

- 用户已授权 Phase4B，保持 `codex/winui-phase4`，起点 `0de0b342623c0e6a047fd354843903779e564e2e`；不得进入 Phase5。下方 Phase4A.1 的“无 Phase4B”仅描述该历史 checkpoint。
- 已重新审计实际 Electron recognition/local evidence/review/manual/merge/lifecycle 源码，行为地图留在 DEVELOPMENT.md。先完成 4B.1 内部 checkpoint `7f858ad54b6d90e2668ffd24dfdf2bcffc3fd0a2`，再实现 4B.2；本节 supersede 其“仅核心、未接入 Apply/UI”的中间状态。
- 分层为 Windows 有界证据读取 → Infrastructure 纯识别 → Application 只读双 worker 队列 → 持久化 Review → 显式决定服务 → 原事务内核。RecognitionEngine 不持 writable repository，Presentation 不直接读文件/JSON/HTTP。WinUI 提供单条、所选、只补缺失、重新识别、取消、核对、Apply、Ignore、Manual Link，所有高分仍需确认。
- 最终算法 `electron-recognition-v1-native-2`。原标题/证据 helper 保持 Electron oracle；独立 ReviewSafeRanking 对 FD/remake 等显式版本与无版本目标的歧义增加 veto，不改变原分数。索引沿用 56,064 records / 97,038 titles 及许可，惰性有界加载；128候选饱和拒绝唯一身份推断，结果最多5项。源指纹 `electron-review-identity-v1` 保持 JS JSON UTF-16 契约；证据指纹 `native-local-evidence-v1` 覆盖所读字节和物理身份，两者均不是提交权限。
- `native-recognition.json` 保存有版本的建议，旧 `metadata-matching.json` 仅作为历史输入；重启后 NeedsRecheck，坏文件保留并禁止覆盖。Apply 重验 generation/lifecycle/auth、canonical detail、发行/证据与最终本地 fingerprint，再进入原串行原子事务；个人字段保留，已持久提交后的附加错误不会伪装未保存。Ignore 只删除该 review ID；Manual 仍走 canonical+token；重新识别保留旧关联，旧建议失效。
- 最终 locked restore、Debug/Release solution build 各0 warnings/errors、C#各 **944/0/0**（原609+新增335）。新增包含254实际Electron差分、六指纹、三例完整 golden 流程、状态/持久化/Apply/UI回归；保留原102 transaction differential、13 filesystem/cover、32 online、14 fault。Artifact tests **32/0/0**；Electron完整 **665/0/0**、build/import regression通过；Release publish成功。性能工具默认不跑，另以显式开关实际完成1/100/1000本地与合成HTTP基线，详见 DEVELOPMENT，不是SLA。
- 真实 WinUI 隔离合成 smoke 完成：单条/批量、offline/local index、多候选、veto、Apply/Ignore/Manual错误ID与成功、重新识别、stale、restart、活动取消/迟到响应、活动关闭。实测修正卡片反复重建导致选择高亮不同步、详情残留、快照误标“当前关联”和 stale进度计数；最终包重新核验含键盘选择。活动关闭PID42136 exit0，约0.52秒；再次正常关闭PID31524 exit0。
- `.artifacts/native-phase4b/` 为 ignored 本机证据：**23组manifest/UI tree/JPEG，23个不同截图hash**；9组早期publish、14组最终publish-final。requested/runtime、tree、字节数/SHA256逐项校验。早期`multiple-candidates`的高亮问题由`final-multiple-candidates` supersede，早期Apply/Manual截图不能证明最终清理/计数修复；最终场景另有明确证据。最终MVID与各层DLL hash见build-bundles及manifest，程序集checkpoint仍指向构建前4B.1，不冒称最终提交已嵌入二进制。
- 正式资料、真实 credential、真实游戏目录/执行均未访问。真实 API **SKIPPED — no safe development credential**。合成HTTP不证明真实catalog/CDN、全部真实游戏识别或长时间稳定；干净机器发布/签名/升级及既有UNC/filter环境缺口仍在。**可进入独立 Phase 4B Gate Review；完成本阶段checkpoint后暂停，不进入 Phase5。**

## WinUI Phase 4A.1：Online Result Acceptance Remediation（整改自检完成）

- 起点 `f737bf7c4174cdabe66d25ffa60629d939ca69dd`，保持 `codex/winui-phase4`。Phase4A Gate 为 NO-GO；本节 supersede 下方历史阶段关于 credential lifecycle、detail identity 和结果接纳完整性的判断。仅修 M1–M3、corrupt-vs-missing 和 DNS callback 测试覆盖，无 Phase4B、正式数据/凭据操作或识别功能。
- M1：`AuthenticatedCredentials` 按调用次序执行 save/clear，每次用户意图立即推进共享进程内 auth epoch 并取消旧上下文；请求等待已排队 credential mutation 完成。失败的旧保存不丢弃新保存。Presentation 分离连接测试和凭据操作；旧测试即使忽略取消后完成也不能覆盖新状态。PasswordBox 仅在当前保存成功且输入未再变化时清空，失败保留输入，删除失败不宣称已移除。
- auth generation 由 composition root 的共享 adapter 提供；connection/search/details/cache 均通过 transport 捕获、取消和接纳检查，不要求每个 caller 手动 ClearCache。详情缓存带 epoch；匿名图片不无意义清空，秘密或其 hash 不作为认证缓存身份。Phase4B 新调用者必须复用该 adapter/transport，并保留自身用户意图和事务 token 检查。
- M2：最终 `work.Id == current`，`current` 仅由合法 ENTITY_MERGED 修改。直接86→work90及多跳终点不符拒绝为 InvalidResponse，无错误缓存、alias 或库写入；这是对 Electron 盲点的明确加强，不退化为旧行为。合法3跳/防环保留。
- M3：EOF、parse、map、cache admission、final return 检查取消/45秒预算；统一最终接纳在 auth 锁中完成，失败回滚本次 provisional cache entry；缓存读取也使用该锁，不暴露尚未最终接纳的条目。保留 DTO/identity、cache generation、auth generation 检查、8MiB/严格UTF-8/最多2次重试。
- credential 只有文件不存在表示缺失；空、超大、坏DPAPI或坏格式为 CredentialStorageCorrupt，检查不自动删除/覆盖。当前仍为 **raw DPAPI blob，无应用层版本 envelope**；Phase6/migration 前评估 versioned envelope + legacy raw-DPAPI compatibility。图片生产 DNS/IP/无代理/无自动跳转策略不变，仅增加 resolver/connector seam，测试真正穿过 HttpClient 的生产 ConnectCallback，不连接内网。
- 最终 locked restore 成功；Debug/Release build 各 **0 warnings / 0 errors**；完整 C# 各 **609/0/0**，新增52项 OnlineAcceptance 回归。原102 transaction differential、13 filesystem/cover differential、32 online differential、14 fault injection 全通过。Artifact **24/0/0**；Electron 完整 **665/0/0**（含 relevant），build/import 通过：37新增、重复0新增/37跳过，个人字段/重启/游戏文件保留，renderer errors0。Release publish 成功。
- 真WinUI使用合成 HTTP + **实际隔离 DPAPI store**，验证损坏提示、保存失败保留输入、慢连接时删除/替换、超过旧响应窗口仍不回退、错误ID、取消后重新发HTTP、正常搜索2→分页3/详情/合成远程封面、愿望单3→4且重复仍4、活动请求关闭exit0。EOF精确取消、deadline、失败clear及不合作迟到回调由生产路径/Presentation确定性回归证明，不冒充真实服务桌面测试。
- `.artifacts/native-phase4a1/` 保存 ignored 证据：**19组 JPEG/UI tree/manifest**，requested/runtime、tree、文件长度及SHA-256一致，19个不同截图hash。初批11场景在 `publish`，最终8场景在 `publish-final/win-x64`；最终复核clear、正常流程、错误ID、取消/重新请求与关闭。最终 WinUI MVID `4f81d976-2eb5-43be-8ec1-67ed3d5c6d87`；两批各层hash及范围在 `build-bundles.json`，不能声称全部截图来自最终包。旧4A的26组为历史功能证据，不证明本轮整改；旧3A作废证据仍作废。
- **PASS FOR PHASE 4B（Phase4A.1整改自检）**，不是Phase4B实施授权。真实API：**SKIPPED — no safe development credential**；真实API/CDN/代理/WebP兼容、长时间故障压力、干净机安装仍属后续Gate。Phase3的UNC/generic reparse/10k unique images和正式库写入前workingDirectory降级门禁仍保留。完成checkpoint后暂停。详细位置、回归与证据见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 4A：Online Data / API / Remote Assets（历史本机验收；后续 Gate 与整改见上）

- 从可信 Phase 3B.1 `9ae2199aba6aedbb8e8012ab38e4a3ec8eb1a1a2` 新建 `codex/winui-phase4`。本节为当前实现；下方历史阶段的“无 Phase 4 / 远程图片尚不下载”不再描述当前候选。没有进入 Phase 4B、读取正式资料或真实 credential、扫描或执行真实游戏。
- 已实际审计 `electron/nextmoe.ts`、settings、library 与 Vue 在线入口，并以当前 Electron 生成32个在线 oracle。Application 定义在线/credential/cover 接口；Infrastructure 分离 transport、DTO mapping、catalog/cache、remote source；Platform.Windows 提供隔离 DPAPI；Presentation 管理请求 generation/取消/显式状态；WinUI 只负责输入和展示。保留事务、filesystem、图片管线和七项目依赖方向，无依赖升级。
- 固定正常 API endpoint，长寿命连接、45秒共享 operation deadline（搜索两步、重试和合并均消耗同一预算）、最多2次重试，仅502/503/504与429 `RATE_LIMITED`。8MiB流式上限，严格UTF-8/JSON；状态分类、响应/stream释放、有限 Retry-After。HTTP跳转不跟随；作品 `ENTITY_MERGED` 最多3跳且防环，返回canonical string ID。
- 原生发现页提供搜索/取消/重试/分页/详情；去重、查询与详情generation防旧结果覆盖新意图。愿望条目显式加入，经旧事务内核保存；重复ID不增条目，无安装；资料刷新保留个人字段并检查请求token；删除只移除无安装的书架关系。移除/刷新/并发个人编辑的完整语义由生产服务集成测试验证，不冒充全部桌面点击覆盖。
- credential仅隔离目录 `native-online-key.dpapi`、当前Windows用户DPAPI、原子写入、受限读取；不读取旧safeStorage或真实密钥，不写明文，UI只暴露保存状态。连接测试必须同时通过鉴权和目录响应校验。日志结构只允许method/host/status/elapsed/retry/requestId；错误不带响应体、完整URL或密钥。
- 远程图只接受已注册API/库来源的HTTPS公开DNS目标；无IP/本地或内部地址、userinfo、非标准端口、cookie/auth或自动跳转。DNS解析检查后直接连接已检查IP，绕过系统代理以免绕开目的地校验。20秒/20MiB，PNG/JPEG复用现有WIC像素上限与stale-image保护；WebP等仍占位，未声称全格式支持。个人封面优先；URL+revision哈希与decode bucket区分缓存，不把URL写文件名。详情缓存32项/8MiB/5分钟，远程原图64项/32MiB/10分钟，派生缓存沿用64MiB内存/128MiB磁盘，缓存丢弃不删除用户资料。
- 最终 Debug/Release build各0 warnings/errors，C#各 **557/0/0**（原472+85）；保留102 differential、14 fault injection、13 filesystem/cover differential；新增在线 differential **32**。Artifact **24/0/0**，Electron relevant扩展到 **252/0/0**，Electron build/import通过：37新增、重复0新增/37跳过、个人字段/重启/文件保留、renderer errors0。Release publish成功。
- 真WinUI在隔离合成库验证：启动、搜索2项、分页去重到3项、详情和远程封面、坏图占位、空/401/429/离线、加入愿望单及重复、重启保留4项、取消、A→AB及超过旧响应窗口后不回退、活动请求关闭exit0。关闭动作至退出约0.36秒，非性能SLA。正常装配（ONLINE_FIXTURE关闭）无凭据重启，缓存封面和书架可用；连接测试明确提示缺少密钥。真实API smoke因没有安全可用开发凭据明确跳过。
- `.artifacts/native-phase4a/` 为ignored证据：**26组 JPEG/UI tree/manifest**逐项requested/runtime/hash核对，26个不同截图SHA-256。`search-active-before-cancel`仅早期诊断，不证明取消通过；最终 `checkpoint-*` 六场景复核启动、加载、改词Idle、取消与活动关闭exit0。最终包 `publish-checkpoint/win-x64`；WinUI MVID `f0c3cc82-958e-4e2f-9bcb-66f778c83ee0`，Presentation SHA-256 `1884A3CC532FB41D5D15F25E3F7F0E62BEAD87DE089CB7C6D0539538B1C6ED6A`。最终两处状态重置修复有新增回归，三批构建差异/各层hash见DEVELOPMENT及 `build-bundles.json`，不能仅用WinUI MVID判断整个Solution相同。此前Phase3A错误artifacts仍作废。
- **Phase 4A 本机验收自检通过，可进入独立 Gate Review；不是正式接管或 Phase 4B 实施授权。** 剩余边界：真实API/CDN/代理环境与WebP兼容、长时间网络故障压力、干净机安装未验证；既有UNC/generic reparse/10k unique images边界保留。`workingDirectory`降级写入门禁最迟Phase6且必须先于正式库写入，不能用备份提醒代替。完成checkpoint后暂停。

## WinUI Phase 3B.1：Filesystem Boundary Remediation（整改自检完成）

- 基线 `e75663c42fff9b0eea194cbca7d2706cbae45412`，保持 `codex/winui-phase3`。Phase3B独立Gate曾判NO-GO；本节supersede下方历史阶段关于扫描边界/未知identity/候选连续性的完成判断。只修复M1–M3、Depth、Navigation，无Phase4或正式数据操作。
- M1：root observation固定，queue携带子目录observation；dequeue/枚举边界检查根当前identity、卷、组件范围和reparse链。Windows枚举在lexical/final祖先链短期句柄保护后重验；根变化整次失败，子级替换/额外reparse跳过提示，root alias仍支持。不是绝对文件系统sandbox。
- M2：ConfirmedUnique / ConfirmedDuplicate / Indeterminate / NotChecked明确分开。不同字符串的不可比较历史安装阻止自动导入，保留记录并展示路径/状态；恢复后“重新检查当前目录”。无人工强行放行，比Electron更保守；同路径仍保守判重复。
- M3：PreparedInstallation保留原ReviewedCandidate，预处理与final precommit确认它仍有效，不能把A的核对依据替换成B。变化返回CandidateStale；99 stable + 1 stale整批零写入。原路径拼写、一次原子事务与窄TOCTOU边界保留。
- 深度6且仍有未入队目录时ScanResult明确IsPartial / DepthLimit，保留有效候选，零候选不假装完整扫描。InstallationNavigation generation防止晚加载夺回Library/Settings，连续请求最新者生效。
- 新增32项：FilesystemBoundary27 + Presentation5；真实临时目录/junction/symlink/rename/删除/断链测试，生产OfflineFilesClient/事务＋计数persistence验证零写入。Navigation为生产Presentation集成验证，没有新桌面截图。Debug/Release各472/0/0、build各0 warnings/errors；保留102 differential、14 fault、13 filesystem/cover differential；Artifact18、Electron relevant183、Electron build/import（37新增、重复0/37）通过；Release publish成功。
- `.artifacts/native-phase3b1/`保存本轮ignored证据。旧38组Phase3B UI evidence不证明本轮竞态；未重采图片baseline或修改图片架构/decoder/cache预算。UNC/generic reparse真实环境仍未验证；10k unique images留到Phase7。
- **workingDirectory降级保护最迟Phase6，且先于首次写正式库**：必须有独立迁移库/版本或写入门禁，阻止旧Electron无提示回写丢字段；备份提醒不能替代机制。本轮不做migration。
- 五项整改自检通过：**PASS FOR PHASE 4（整改自检）**。建立Phase3B.1 checkpoint后暂停；不开始Phase4，不访问正式资料/真实凭据，不执行或修改真实游戏。用例及验证限制见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 3B：Filesystem / Installations / Image Pipeline（历史本机验收；后续 Gate 与整改见上）

- 用户在 Phase 3A.1 Gate 后正式授权本阶段；沿用 `codex/winui-phase3`，起点 `55069ac4fddd1491e3122fd4c69a24487a3b2d86`。本阶段 checkpoint 后暂停，不进入 Phase 4。下面旧阶段的“尚未进入 Phase 3B”是历史停点；当前实现和限制以本节及 DEVELOPMENT 的 Phase 3B 节为准。
- 七项目分层未推翻，无 NuGet/npm 升级。Application 增加 filesystem/preflight、bounded scanner、安装和个人封面事务、image/source/codec/cache 接口；Platform.Windows 实现 Win32 handle identity、顶层枚举、WIC decode/normalize、隔离派生缓存；Presentation 管理扫描/草稿/受跟踪 mutation 和请求 generation；WinUI 使用标准 picker 与复用安全的 CoverPresenter。IO/扫描/identity/解码均在后台，最终像素上传在 UI。
- stored user path、display path、final path、卷号+128-bit file ID 分开；保留原始路径拼写，基于真实身份判断相同目标/alias 重复，保守拒绝安装范围重叠。Preflight 有十类可解释结果；写盘前重验 snapshot。未知 reparse 或可靠 identity 不可取得时拒绝新关系，不把字符串当替代身份。目录离线只显示状态，保留历史安装。最后重验到写盘仍有窄 TOCTOU 窗口；未来启动必须再验证。
- scan BFS 上限：深度6、目录5000、目录项50000、候选2000，队列有界，子 reparse 不跟随、identity 防重复、取消和节流进度；本阶段不读取 metadata/text、不执行 exe。用户先核对候选再确认一次批量事务；100项测试只保存一次。支持多安装、结构化 args、exe、范围内 working directory、首选切换、只移除关系。安装草稿有 stale token，已保存+刷新失败不变成未保存，不重复提交。
- personal cover 继续在 v1 JSON 保存 Base64 JPEG，最多128KiB；不放入可丢缓存。PNG/JPEG输入≤20MiB、单帧/≤4000万像素、decode edge≤1600，按控件尺寸和DPI请求缩略图；有效IHDR的2.25亿像素测试明确在pixel decode前拒绝。默认并发2、64MiB LRU、128MiB派生磁盘缓存；坏图占位、旧结果不能覆盖复用后的条目，退出取消/drain。pending tasks没有另设全局硬上限，后续可调优；真实远程图片尚不下载。纯cover priority已差分，不能说在线封面系统完成。
- NativeLibraryCodec 保留 legacy 校验与原始安装路径；workingDirectory 为 optional 扩展。旧 Electron 会忽略且在重写时丢掉 workingDirectory，回退前须保存候选 profile 副本，两端不能共用同一可写 profile。更严格的 identity/范围/队列/候选规则是记录在案的 intentional differences，未迁移正式资料。
- 实际锁定 restore、Debug/Release build各0 warnings/errors、完整 C#各 **440/0/0**（保留102 differential/14 fault）；新增61项覆盖filesystem11、offline-files17、filesystem differential13、images10、presentation10。Artifact tests **18/0/0**；Electron relevant **183/0/0**，build/import regression通过：37新增、重复0/37、个人字段和重启保留、文件未删、renderer errors0。Release `publish-final/win-x64` 已实际启动/退出0。
- identity 实际验证 lexical别名、长路径、junction/断链、symlink、8.3、subst、hardlink、rename/替换、ACL拒绝。`filesystem-capabilities.json` 将 UNC（没有获授权synthetic SMB share）、generic filter reparse（无可创建fixture）明确列为 **skipped**，不能用全套xUnit的0 skipped冒充这些环境通过。scan loop/unreadable/disappeared另有可控适配器注入，不等于所有OS环境验证。
- 真 WinUI smoke覆盖目录picker→扫描→取消→再次扫描→核对→导入2条→重复0/1，多安装/参数/工作目录/首选/失效路径、本地封面/坏图保留、坏封面占位、Grid/List、重启持久化、scan/image活动关闭。用户额外指定 `D:\entertainment\PC` 后仅只读扫描36候选，未导入；隔离库仍2条synthetic。正式 `%APPDATA%\KunGalManager`、真实凭据、NextMoe、game launch/playtime、Liquid Glass、SQLite均未访问或实现。
- `.artifacts/native-phase3b/` 是本机生成证据，不提交。38组截图/manifest逐项hash/runtime核验，38个不同截图hash；`debug-multi-install` 专门保留为草稿区残留bug诊断，由 `fixed-edit-draft-closed` supersede。其余场景用途、早期/最终build范围见DEVELOPMENT。最终WinUI MVID `c923ac56-5f7e-47f4-8970-c27611d5d95d`；最终与大库采集版只有Settings阶段标签的产品源码差异，五个核心层DLL字节相同，源文件/DLL/XBF/PRI清单保存以便复验。原Phase3A无效截图不恢复可信；Phase3A.1有效证据保留。
- **Phase 3B baseline，非SLA**：1k/5k/10k带四张重复40×60真实JPEG，viewport1042×455.5；Grid首容器848.4/2359.1/3090.0ms，Grid容器45→51/45→46/45→46，List28→30（全部规模），Grid/List首屏working set295.4/342.7、373.9/427.1、451.7/485.1MiB。10k首次窗口被用户关闭，后续滚动/List来自磁盘缓存复测，Grid3014.2ms/462.8MiB。图片最高并发2、稳定后outstanding0/errors0，10k往返滚动与Light/Dark均实际检查。容器不随库规模线性增长；5k/10k读取时间和内存高于Phase3A.1无图片基线，不宣称正式性能SLA或万张大原图压力通过。
- **本机验收完成，可进入独立 Phase 3B Gate Review；暂停等待下一阶段范围。** 仍缺UNC/未知filter真实环境、真实大库/长时间运行、干净机安装签名升级、断电证据；本阶段不是正式资料接管/联网/启动授权。完整架构、语义差异、baseline、复验命令与artifact列表见 [DEVELOPMENT](../../DEVELOPMENT.md)。

## WinUI Phase 3A.1：Presentation State Remediation（验收完成）

- 基线 `codex/winui-phase3` / `29e5577c0e82384b66ce9d12fd6b06b996dc8b9c`；仅整改 M1–M5、Search lifecycle 和 Selection contract。没有 Phase 3B 授权，不访问正式资料、真实凭据或游戏目录。
- Presentation 保留原分层和事务内核：详情响应检查请求 generation、草稿修订号和同条目 lifecycle；偏好有 Uninitialized/Loading/Ready/LoadFailed，启动操作按字段合并，读取失败后本次运行只作临时修改；完整批量操作从解析起受关闭等待；PersistenceResult 与 LastRefresh 分开，提交后的刷新故障明确提示已保存。
- 选择契约为明确清空：Grid/List、查询/排序/筛选、Reload、projection 替换和重新进入 Library 都清空选择；隐藏后重现、删除/同 ID 恢复均不自动恢复选择，也不继承旧草稿 token。
- **旧 Phase 3A UI 证据作废**：`.artifacts/native-phase3a` 的 31 PNG / 31 UI txt 都是初始 1k Dark Grid 快照。已查到 `saveState` 闭包绑定旧 REPL cell 的 `state`，并以两次 cell 调用复现。旧首屏/容器表不能作为验收依据；build/TRX/Electron 记录不因此作废。
- 新采集入口 `native/scripts/capture-ui-evidence.mjs` 每次自行读取当前窗口；先比较期望场景与应用运行时 metadata，再保存 screenshot、UI tree、含时间/进程/MVID/checkpoint/状态/容器/内存/文件 hash 的 manifest。场景不符、快照过期、无可视容器均失败；不再闭包引用外部快照。
- 重启 Codex 后桌面工具恢复，已实际完成 M1 新草稿抗延迟覆盖、M2 启动偏好合并并保留已有筛选、M3 真实事务提交后的刷新故障、M4 后台预处理期间关闭等待/放行提交及主动取消、Search active → Reload failure，以及 Grid/List/Search/Filter/Reload 选择清空。批量取消实测发现提示误称保留草稿，已改为明确未写入，不改变取消或事务行为。CommittedWithWarning + RefreshFailed 有确定性回归，未冒称真实桌面故障注入。
- 真实 UI 新证据位于 `.artifacts/native-phase3a1/ui`，逐场景 manifest 与 UI metadata、文件 SHA-256 已交叉核验；旧 `.artifacts/native-phase3a` 截图继续作废。1k/5k/10k 均为 Grid **45→51**、List **29→31**（首屏→快速滚动后）；Light/Dark、Normal/Read-only/Corrupt/Empty/Zero-results 独立场景齐全。原生发布版本实际启动、滚动、持久化、重启及正常退出。baseline 与构建身份分层见 DEVELOPMENT，不是 FPS 或正式性能 SLA。
- Debug/Release build（各 0 warnings/errors）、C# full（各 **379/0/0**；新增 30，原 102 differential / 14 fault injection 保留）、artifact tests **10/0/0**、Electron relevant **130/0/0**、build/import 与 Release publish 通过。验收只使用隔离合成库，无正式资料、真实凭据、API 或游戏启动。**PASS FOR PHASE 3B** 表示本阶段 Gate 就绪，不是执行授权；建立 Phase 3A.1 checkpoint 后暂停，不进入 Phase 3B。

## WinUI Phase 3A：Library & Personal Data UI（2026-10-04，候选待 Gate）

- 从可信 `codex/winui-phase2` / `d0702cafe1b6762069d63fe08bcba07b199ca713` 建立 **`codex/winui-phase3`**。本节为当前状态，下面 Phase 1/2 的“尚未接入 UI writer”是历史状态。本阶段只开放带既有合成标记的隔离目录；没有访问正式 `%APPDATA%\KunGalManager`、凭据、真实游戏或联网目录 API。完成 Phase 3A checkpoint 后暂停，**没有 Phase 3B 授权**。
- Solution 新增纯 .NET **TheOrder.Presentation**，现为七项目。调用路径为 `WinUI View → WorkspaceViewModel / PersonalDraft → IOfflineLibraryClient → TransactionalLibrary → LegacyLibraryCodec / IsolatedLibraryPersistence`。View 不调用 repository；Presentation 无文件 IO、JSON serializer、HttpClient、Win32 或具体持久化实现。composition root 负责装配，Theme Resources 负责基础外观，业务层不依赖 XAML。
- Library 使用默认虚拟化 GridView/ListView、受限高度 viewport、轻量 GameSummary、按需 GameDetails 和封面占位。详情只开放个人名称、评分、状态、笔记、分类、个人标签和收藏。数据不从 binding 直接写入 authoritative snapshot；Save 经事务后刷新，Cancel 丢弃草稿。导航、关闭、重新读取保护 dirty draft；旧 epoch、删除/恢复 generation 和个人字段冲突均拒绝过期提交。批量状态/收藏/分类/标签最多 1000 个 ID，一次 candidate、一次 durable transaction。
- 搜索索引和查询在后台工作，120 ms 输入防抖与 cancellation/generation 防止旧结果覆盖新查询。迁移旧五种排序、五种库视图、状态/收藏/分类/个人和目录标签/最低评分筛选。实际 Electron oracle 提供 **38 查询 + 5 标签拆分 + 8 批量差分**；NFKC、Unicode lower、AND 搜索项、标签大小写/次序/追加/删除保持参考行为。排序使用 zh-CN 名称比较，ID 使用 en-US 比较；未声称任意 Windows/NLS/ICU 版本的所有 Unicode 排序等价。
- 原 legacy v1 和提交内核不变；Application 新增个人草稿 token 与有限批量用例。`native-ui.json` 单独保存主题、Grid/List、排序和显式命名筛选；临时查询不自动保存，损坏偏好不覆盖旧文件。正式 Electron localStorage 迁移后置。只读能力来自统一 LibraryCapability；所有个人编辑入口禁用。CommittedWithWarning 显示“修改已经保存，但存在附加警告”，不自动重试；失败保留草稿；损坏库显示“尚未加载书架”，不显示为真实空库。
- **实际验证**：locked restore、Debug/Release 全 solution build 均 0 warnings/errors；C# 两配置各 **349 passed / 0 failed / 0 skipped**，比 Phase 2.1 新增 79，原 102 differential / 14 fault injection 保留，新增 UI differential 51。Electron 7 个相关单元文件 **130/0/0**；build + desktop import 37 新增/重复 0 新增、37 跳过/个人字段与重启保留/renderer errors 0。独立 Release publish 成功，发布 EXE 实际启动、滚动、批量写入并正常关闭。
- **UI 验收记录已被 Gate 否定为有效证据**：原截图/UI tree 被错误重复保存，无法支持各场景声明；原首屏/虚拟化表撤回。详见上方 Phase 3A.1 根因与替代采集流程。旧进程内存 JSON 不能单独证明库规模和视图身份。
- 仍保留的阶段边界：图片只是占位和数据说明；没有图片下载/解码/缓存、安装管理/扫描/导入 UI、启动/计时、API、识别或 Liquid Glass。长时间运行、全 Unicode 排序、干净机器/安装包签名/升级和无障碍完整验收尚未完成。当前可交给 **Phase 3A Gate Review**；通过后才能规划 Phase 3B filesystem identity/preflight，不能直接接入真实目录。

## WinUI Phase 2.1：Transaction Kernel Gate Remediation（2026-10-04）

- 当前工作基于 `codex/winui-phase2` / `f495d06c6b3b6d8dc2b6125405490876b5d7fab1`，只整改 B1/B2/M1/M2 和异步 token 入口。六项目结构、legacy v1、32 MiB / 每条目 100k sessions 上限、事务提交顺序与正式资料隔离保持不变。WinUI 仍未注册 writer，未进入 Phase 3，未访问正式资料或真实凭据；完成 checkpoint 后暂停。
- **B1**：`LegacyLibraryCodec.Inspect` 明确返回 InvalidInput / ValidReadOnly / ValidWritable。原始输入仍经 32 MiB 限制和完整验证；规范化后内部验证不把缩进扩张当作输入损坏，输出能力另行检查。有效但不可写的主库保持 Main，snapshot 提供 Access/ReadOnlyReason，mutation 在 candidate 与 IO 之前拒绝。构造实例不再重新 Encode，因此也不会从构造路径误回退备份。没有提高限制、压缩内容或删 session；既有 unknown-field 白名单规则未作任何“腾空间”变更。
- **B2**：默认时间通过 TimeProvider 获取 UTC instant，再用明确 invariant Gregorian 格式生成 persisted timestamp。测试注入 instant 而非字符串，覆盖 en-US / zh-CN / ja-JP / th-TH / ar-SA 的 addedAt、exportedAt、metadata 时间及主/备份；输入日期规范化和校验原本即显式 invariant。UI 本地化显示不受此约束。
- **M1**：在轻量 admission lock 内登记原 semaphore 的等待顺序，然后通过 Task.Run 在后台执行 candidate、导入解析、规范化、验证、序列化及持久化；持久化内部 await 不捕获 UI context。ShutdownAsync 停止接单，异步排空已经接收的操作；排队/候选阶段仍可取消，已开始 durable 阶段完成最小提交并发布状态。DisposeAsync 用此协议，旧同步 Dispose 仅允许空闲调用，忙时明确拒绝而不阻塞。WinUI Closing 先取消关闭、await 容器异步释放，再投递真正关闭；当前窗口只读，不能把内核测试称为完整 UI 写入集成验证。
- **M2**：session ID → index 字典替代循环扫描/IndexOf；source 顺序、同 ID 冲突、等长 source 优先、较长整条替换和历史累计算法不变。80k+80k 不相交时仍因 160k 超过单条 100k 上限而整笔拒绝，不调整契约。
- 即时用户意图继续用 LinkAsync；所有延迟 API/识别结果必须用 token 必填的 CommitLinkAsync。提交结束（含失败/取消）清理本次 Pending；BeginRequestAsync 的取消也安排无取消的清理，清理不影响较新 generation/request。退休请求注销取消回调，关闭排空后释放剩余登记。ReadOnlyMemory 导入遵循异步缓冲区所有权约定：调用方在任务结束前不修改输入；后台复制/验证后才构造候选。
- 专项回归位于 `TransactionRemediationTests.cs`，真实 Electron oracle 增加 4 个小型 session 差分和大输入边界观察。最新实际验证、性能与证据路径见 DEVELOPMENT 的 Phase 2.1 节；下面 Phase 2 数值是整改前历史证据。
- **实际结果 / Gate 自检**：locked restore、Debug/Release 全 solution build（0 warnings/errors）；两配置 C# 各 **270/0/0**，新增 **36**（32 专项 + 4 差分），包含 **102 differential / 14 fault injection**。Electron oracle 可重复生成，相关单元 **120/0/0**，desktop import 37 新增/重复 0/37/个人字段和重启保留/renderer errors 0。Debug/Release 原生窗口实际正常关闭 exit 0、合成资料哈希未变。Release 完整事务中位数：10k 普通条目 **182.1 ms**，20k/40k/80k 每侧重叠合并 **160.1/294.3/604.7 ms**（各预热 1、测量 3；范围见 DEVELOPMENT）。B1/B2/M1/M2 均有实测回归，**PASS FOR PHASE 3**；此结论不是下一阶段执行授权，仍须暂停。真实资料、完整 UI 写入、断电/网络盘和干净机器发布未在本轮验收。

## WinUI Phase 2：Legacy Compatibility & Transaction Kernel（2026-10-04，历史 checkpoint）

- 从已通过 Gate 的 `d988d2bf75d6885061e4a860337c95ada1371b7b` 新建 `codex/winui-phase2`。当前新增内容是 **仅用于显式合成目录的事务内核**；WinUI 仍只读，没有注册 writer，也没有迁移正式资料、网络、扫描、启动、计时或视觉系统。Electron 产品源码、依赖、正式发布目录不变。完成本 checkpoint 后暂停，不能把本文当作 Phase 3 授权。
- 先运行真实 Electron 44.5.1 `LibraryStore` 建立 oracle，再实现 C#。`native/scripts/generate-transaction-oracle.mjs` 使用锁定 Electron 的 Node 模式、固定时钟/UUID 提供器、临时虚构目录，调用原类而非抄写一份参考算法。`transaction-oracle.json` 包含 50 个 mutation 场景、5 个异步生命周期序列、5 个旧恢复观察和 43 个 Phase 1.1 字符串/路径/数字向量；记录 library/title-matching/local-cover 原文件 SHA-256，改动参考必须重新审查和生成。
- Application `TransactionalLibrary` 持有 immutable authoritative snapshot、候选值、串行队列、生命周期 token；Infrastructure `LegacyLibraryCodec` 负责旧格式规范化、全库验证/序列化、目录身份及名称保护；Platform.Windows `IsolatedLibraryPersistence` 负责隔离、目录/独占 writer lease、备份和原子替换。六项目依赖方向未改变，无新增 NuGet 包。代表性操作为 add、个人字段、安装记录增删、delete、旧备份 append/restore、明确作品 ID 的 link/merge。
- 必须保持的顺序：**取队列中的最新状态 → immutable candidate → 全库验证/规范化 → 大小校验 → 最后取消检查 → 保存 lastGood 备份 → 保存主文件 → 更新 generation/revision → 发布 authoritative snapshot**。IO 前失败不得更改内存；主文件替换成功后出现检查异常，必须按已提交返回 warning，并同步内存，不能谎报回滚。备份可能先于失败的主保存更新为 lastGood，这与旧版一致。
- 取消在队列前/等待时可中止；进入持久化后不向文件 API 传递取消令牌，完成最小安全提交，结果 `CancellationAfterCommit` 表示取消发生于提交阶段。请求 token 独立于 cancellation，包含仓库 epoch、条目 generation、request ID 和 revision。后发请求使先发结果失效；名称/关联/安装身份变化、每次 link、删除再同 ID 恢复均使旧 token 失效；普通个人字段编辑可与资料返回共存。目标条目有待完成请求或较新意图时拒绝合并。
- unknown fields **按旧版主动丢弃**：`parseBackup` 是白名单重建对象，不保留扩展。本阶段保持这一行为并用 oracle 验证；没有承诺任意未来 schema 的无损转存。显式 null / 0 / false / 缺省 nameIsCustom / 缺省或 null 的封面模式分别保留。旧格式 version/format 不变；ID、数组顺序、字段存在性、数值和 UTF-16 内容逐项比较，空白与 JSON escape 拼写不要求字节相同。旧版 trim 后 ID 相等的首选安装检查现已补齐，Phase 1.1 原值读取保持不变。
- **有意差异和边界**：旧 Electron initialize 会创建空主文件或从备份修复并隔离损坏文件；C# 打开只加载 Main/Backup/New 状态，不改主/备份。首次显式 mutation 才保存；若从备份恢复，此次保存以恢复后的有效库作为 lastGood，替换损坏主文件，不另生成 corrupt 副本。打开会留下无内容 `.the-order-writer.lock`，不应描述为整个目录绝对零写入。安装移除是新增的纯记录操作，首选项被移除时设 null，永不删游戏文件。安装添加接受上游已授权的值，不扫描/打开路径；import 使用 lexical Windows identity，没有移植旧 realpath 对真实目录别名的解析，因而不是可用于正式资料的完整导入/迁移器。当前 token 是单一显式请求通道，未实现旧 automatic batch / association / cover 的完整多通道调度。
- 实际验证：locked restore、Debug/Release solution build（均 0 warnings/errors）；Debug/Release C# 各 **234 passed / 0 failed / 0 skipped**，比可信基线新增 **150**。其中 **98 个差分测试**（50 mutation + 5 lifecycle + 43 codec），**14 个标记 fault-injection 测试**（13 个备份/主文件阶段组合 + 1 个包含权限/目录消失/ENOSPC 三种异常的测试），此外有真实只读文件、主/备份锁定、硬链接、目录固定和互斥验证。Electron 6 个相关单元文件 **120/0/0**；构建和 desktop-import 全通过：37 条导入、重复 0/37、个人字段/重启一致、游戏文件保留、renderer errors 0。
- 整库成本如实保留：candidate 初始共享 immutable 记录，验证会重建整库；一次事务约三次整库序列化、两次整库 parse，加 identity 比较。10k 个最简条目约 5.02 MB，本机完整套件中 Debug/Release 单次事务约 577/703 ms，进程累计分配观测约 198/185 MB（并行测试会干扰，不是独立微基准）；单条 10k sessions 约 1.76 MB，约 299/334 ms。不得在 UI 线程执行这种路径；真实大元数据/图片/session 库和 UI 流畅度未验收。32 MiB 上限可能早于 10k 条达到；此证据供后续数据库阶段使用，本阶段未引入 SQLite。
- `.artifacts/native-phase2/` 保存本机日志/TRX，不提交。合成 fault injection 不等同于断电实验；网络盘/非 NTFS、恶意外部进程、突然断电、任意手工日期/WHATWG URL 文法、完整 JPEG 解码/EXIF、真实资料与凭据仍未验证。目录 lease 和 writer lock 只协调 native writers，Electron 不识别此锁；禁止两端共用同一 profile。详细恢复矩阵、合并表和复验步骤见 DEVELOPMENT 的 Phase 2 节。

## WinUI Phase 1.1 Gate Remediation（2026-10-04）

- 在 `codex/winui-phase1` / `775450e` 上完成限定整改；Gate 为 **PASS FOR PHASE 2**，但尚未开始 Phase 2。无可写 repository、事务、数据库、联网或正式资料迁移；Electron 产品源码与 npm 依赖未改动。
- `LegacyJson` 在真实 reader → validation → DTO → mapping 边界保留 UTF-16 code units，包括孤立 high/low surrogate；不替换 U+FFFD、不丢字段/条目。整数 DTO 也按旧 JavaScript 的数字解析语义读取 `1` / `1.0` / `1e0`，仍拒绝字符串、非整数和越界值。Domain/WinUI 字符串处理未改。
- `LegacyPathIdentity` 单独构造目录身份，原始保存路径不改写；参考 `electron/library.ts` 的 trim/isAbsolute/normalize/resolve/lowercase，覆盖尾斜杠、`.` / `..`、大小写、重复/混合分隔符、根、UNC、drive root。JavaScript 全量小写与 .NET invariant 不等价，因此 Legacy 内嵌从 Electron 44.5.1 内置 Node 24.21.0 / Unicode 17.0 生成的 27 KiB 大小写快照，并处理词尾 sigma；运行时不调用 JavaScript，不依赖本机 NLS。此身份函数不是文件访问授权；正式资料隔离适配器未修改。
- 新增 **45** 项 C# 用例：7 个生产链字符串、29 个路径正反差分、7 个数字词法、1 个参考完整性/新鲜度、1 个指纹陈旧源码拒绝。现有指纹实验也增加源码 SHA-256、元数据和六向量完整性校验。差分参考由真实 Electron `validateLibraryBackup` / `pathKey` 生成；测试使用固定向量，正常测试不执行 JavaScript 自证。所有生产链用例通过临时合成 `library.json` 加载并核对字节未变。
- 锁定 restore、Debug/Release solution build 通过（0 warnings/errors）；Debug、Release 完整 C# 测试各 **84 passed / 0 failed / 0 skipped**。Electron build 与修复后的 `tests/desktop-import.mjs` 完整通过：37 条导入，重复 0 新增/37 跳过，个人字段与重启一致、游戏文件保留、renderer errors 0。旧失败点是原第76行等待 compact grid 不存在的启动按钮；现在从作品详情验证启动可用及收藏，不实际运行合成 EXE。
- 发布目录正常运行与 SDK 查找禁止探针均通过真实 WinUI 窗口核对（3 条合成资料）并正常退出。探针收窄当前进程 PATH，并令 SDK 查找立即失败；没有移动或卸载 SDK，也不等同于干净机器验证。证据在 `.artifacts/native-phase1.1/`。VC++ 系统前置、干净 Windows 11、完整 JPEG/EXIF、真实资料/凭据、大库性能与全功能迁移仍是后续 gate。
- 完成独立 checkpoint 后暂停。以下 Phase 1 记录是整改前历史证据。

## WinUI 并行候选：Phase 1（2026-10-04）

- 用户在审计通过后授权 Phase 1；新源码位于 `native/`，从 Electron 基线 `387336801981ae30e9de317916024b016dee3605` 建立 `codex/winui-phase1`。这不是正式程序替换，也不是 Phase 2 授权。下文既有 Electron 基线、功能和发布入口仍有效。
- 六项目 Solution：Domain、Application、Infrastructure、Platform.Windows、WinUI、Tests。Presentation 置于 WinUI 内部，没有独立 Materials 项目。实际锁定 .NET SDK **10.0.401**、.NET runtime **10.0.12**、Windows App SDK **2.5.1**（WinUI 3 的 NuGet 子包 `Microsoft.WindowsAppSDK.WinUI` **2.3.9**），目标 `win-x64` / Windows 11。SDK 通过官方 ZIP 与 SHA-512 验证后安装在忽略目录；未修改系统 PATH 或 VS 工作负载。
- 已实现原生窗口、系统标题栏、书架/设置导航、System/Light/Dark 会话主题、全局读取错误、轻量摘要 ListView、旧版 v1 JSON DTO/校验/映射。只读，无自动修复写回；本地 Base64 图片只保留数据和结构校验，页面使用占位图，不宣称完成原生解码与 EXIF 校验。
- 资料仅来自显式 `THE_ORDER_NATIVE_DATA_DIR`，还必须带 `.the-order-synthetic` 标记。拒绝正式目录及其父子目录、重解析目录/文件和硬链接；缺目录/损坏/真空库分别反馈。未指定目录时可开壳，但拒绝数据操作。没有读取正式 settings、真实密钥、用户库或 Chromium 存储。
- Debug/Release 构建、锁文件 restore、39 项测试已实际通过；Debug/Release 原生窗口、发布目录运行、导航、主题、正常/损坏/空库和退出有真实验证。证据在 `.artifacts/native-phase1/`，操作见 DEVELOPMENT。发布过程中发现应用 XBF/PRI 未被默认 pipeline 纳入，已在项目声明发布资源并复验。
- 两项合成实验：① Electron 44.5.1 密文不能直接 DPAPI 解密；同一合成 profile 的 `Local State` 中 DPAPI 加密主密钥 + v10 AES-GCM 可在 C# 验证，三个独立 profile 均通过解密/重新保护/篡改拒绝。仅测试代码，无生产迁移器；遗失 profile 密钥或账户不兼容时仍应重新输入凭据。② 从当前 TypeScript 提取 `reviewIdentity` / `decisionFingerprint` 生成 6 组向量，Unicode、缺项、null、属性顺序、孤立代理项的序列化及 SHA-256 对照通过；新进程不能继承旧决定授权，后续仍需重建 token/重新确认。
- 发布为未签名 unpackaged、自包含目录，.NET 与 Windows App Runtime / WinUI 从发布目录加载；VC++ 运行库实际从本机 System32 加载。未验证无 VC++ 运行库的干净机器、ARM64、完整图像解码、大库性能、真实 API/游戏/私库或 MSIX 安装。
- Electron 源码、npm 锁文件及发布脚本未改动；`npm run build` 和 `tests/packaged-b-smoke.mjs` 通过。额外运行 `tests/desktop-import.mjs` 在导入 37 条后旧网格启动按钮定位处超时（第76行；当前 compact grid 无该按钮），未修改范围外页面或旧测试，不宣称整套 Electron 回归通过。
- Phase 1 完成后停止。Phase 2 建议只做旧格式兼容/事务与失败恢复的差分验证；任何正式资料接管、SQLite、联网识别、游戏启动或高级材质均需后续明确范围。

## 基线与用途

- 正式项目：`D:\Projects\The-Order`，内部版本 **0.7.1**；用户于2026-10-04在三风格候选交付后明确要求“替换正版”，已部署第六轮候选至原`release\win-unpacked`，完整备份旧夜间程序。正式资料未读取、迁移或修改；准确入口、备份与替换验证见[第六轮替换](ITERATION.md#用户授权替换第六轮正式程序2026-10-04)。
- 当前正式程序对应候选交付`707aaeb79075ef7a7ca12c79928d07d450594b1e`，业务`dd89ea2326d3651955cfb2e86c91a1bd47286a9b`，分支`codex/appearance-styles`；后续替换说明提交只改文档。旧正式夜间业务`583e46d`及更早`16ccc28`程序、分支和候选仍保留，准确历史见[ITERATION](ITERATION.md)与[OVERNIGHT](OVERNIGHT.md)。
- 第五轮保留候选分支`codex/windows-experience`，代码`57bce0f0b452051ab0056694410e535925adef5d`从第四轮交付`8adc1a0276d55240859bb6eecbe1d6737814d06d`接续。中性明暗、有效系统字体、原生标题栏/主题同步与窄窗口适配已实现，69项相关单元、最终生产9组/39图与隔离启动通过。宿主Windows11/200%，高对比为媒体模拟，Win10与Snap鼠标操作未验证；准确范围与证据见[第五轮记录](ITERATION.md#第五轮windows体验与审美适配2026-10-04)。用`& 'D:\Projects\The-Order\scripts\start-iteration-candidate.ps1' -Iteration 05`隔离试用，资料`.artifacts/iteration-05/trial-profile`，ASAR`e916f089…`。第四轮代码`3b26dfb25b7fcc1089aa632d0ae42b9c264aabc6`、程序和trial-profile保留：84/72px导航、仅封面与两行名字、长竖图铺满；评分/收藏/启动在列表及详情保留。这两轮候选均未替换正式程序。
- 当前开发分支`codex/appearance-styles`，第六轮候选代码`dd89ea2326d3651955cfb2e86c91a1bd47286a9b`从第五轮交付`3ef02f8b0cdcaa3553b298eed5f03b5b18772c75`接续。设置中可保存经典书架、Windows、Liquid Glass三种风格，明暗独立；玻璃使用应用内边缘折射/轻微色散，保留原生窗口及清晰封面，不依赖桌面捕获或额外原生面板。236项相关单元、15组生产UI检查/53图、真实像素对照和隔离启动通过，准确范围及上游参考取舍见[第六轮记录](ITERATION.md#第六轮三套可切换界面2026-10-04)。启动`& 'D:\Projects\The-Order\scripts\start-iteration-candidate.ps1' -Iteration 06`，数据`.artifacts/iteration-06/trial-profile`；ASAR`8fac672c…`，已按后续明确授权部署为正式程序；试用入口继续保持独立数据。
- 本文用于帮助下一位维护者或评审者理解当前程序。用户操作见 [README](../../README.md)，构建、数据隔离和 Git 规则见 [DEVELOPMENT](../../DEVELOPMENT.md)。
- 上一次夜间替换时，夜间候选与当时正式程序75个文件逐一核对一致，ASAR SHA-256为`652c3c2beba98c7b363f90dfc5deeb3f68b5510a1618f9877640eaf9f111176f`。用`& 'D:\Projects\The-Order\scripts\start-overnight-candidate.ps1'`仍可隔离试用，资料固定为`.artifacts/overnight-2026-10-04/trial-profile`；六条虚构书架/模拟游玩记录与原创封面可编辑重开保留，没有预置密钥。九次已完成，任务PAUSED；夜间交付及未验证范围见[晨间交付](OVERNIGHT.md#晨间交付)，此次部署没有带入试用数据。
- 第一轮独立评审、A-1修复和历次正式替换记录仍保留在 [迭代记录](ITERATION.md)。下面以当前源码为准；第四至六轮的简洁网格、窄导航、所有比例铺满、Windows适配与三风格选择现已随第六轮进入正式程序。各轮隔离入口仍可使用，不会把试用书架或测试密钥带入正式资料。
- 以下第0–7次描述保留各轮当时的交付状态，“未替换”指该轮结束时；这些成果现已随用户授权部署。夜间开发接续同一仓库的 `codex/overnight-2026-10-04`，睡前分支和程序备份保留。第0次已实现可检索的分类收藏页：分类/所含作品/别名搜索、名称/数量/近期排序、封面与在玩/完成进度、全部标签搜索，以及小窗口图标导航的中文名称。准确提交、原生窗口前后图和检查范围见 [OVERNIGHT](OVERNIGHT.md)。
- 第1次夜间源码增加命名“常用筛选”：完整筛选/排序保存、实时匹配数量、书架/分类页直接打开，以及独立改名/更新条件/删除；过时分类/标签保持精确条件。记录位于当前profile的本机UI存储，不随书架备份导出；保存失败与损坏记录有明确反馈，应用时清除批量选择，批量写入中禁用操作。同profile重启与跨profile隔离已通过真实桌面合成回归，17项定向单元、构建、原有书架相关回归和10张修改后原生图通过；未验证真实大库性能。准确代码提交与比较图见 [第1次记录](OVERNIGHT.md#第1次自己的动态书架已完成)，仍未替换正式程序。
- 第2次夜间源码让状态、个人评分和笔记可在作品资料页明确保存，与管理共享草稿并仅提交三个字段；小窗口保留发行日期/公司，封面选择器支持焦点往返。未完成评分输入保留并校验，失败/晚输入/切详情保护通过；77项相关并发与分页单测、26项封面/桥单测、构建、新详情和既有资料保留桌面回归通过。前后20张原生图、准确提交与尚未验证范围见 [第2次记录](OVERNIGHT.md#第2次详情里的个人记录已完成)。仍是夜间源码，未替换睡前程序。

- 第3次夜间源码统一设置页明确保存，保留保存中的后续编辑；移除已保存密钥不提交草稿，连接测试对应已保存配置，失败持续提示并可重试。设置开关具备中文控件名称与可见键盘焦点，侧栏在高度不足时可滚动。82项相关单测、构建、设置/资料保留/书架桌面回归通过；24张原生图及准确提交见 [第3次记录](OVERNIGHT.md#第3次明确的设置保存流程已完成)。本轮仅使用模拟目录接口，未实连NextMoe；当时任何设置保存均停止识别，此行为已在第4次单独核实修正。仍未替换睡前程序。
- 第4次夜间源码 `4af0469b350eaebf626893095fb3cd3e03006cae` 让外观/封面保存、无效设置与设置写盘失败保留识别任务；资料源、密钥、分级或程序名称读取变化成功保存后停止旧任务。启动、设置及资料提交采用同一顺序边界；手动取消也撤回此前排队的手动/导入自动启动，一次识别固定读取偏好。设置保存与识别状态保存分别反馈，不把后者失败误报为设置未保存。188项相关单测、构建、新交错桌面回归及原导入/匹配/设置/取消回归通过；模拟接口、惰性文件，未实连NextMoe。原前后6张和设置回归18张原生图、准确版本见 [第4次记录](OVERNIGHT.md#第4次设置变更与识别连续性已完成)；第5次误写导致其中3张after原图缺失，原回执保留、索引已明确标记，不能把后来文件冒充原图。仍未替换睡前程序，未迁移正式数据。
- 第5次夜间源码最终`cbfa8c293c397d34d32995c898692f1b73b34cd5`改善扫描导入：状态分组、当前结果选择与隐藏已选提示、完整路径展开、窄窗固定确认区；取消与真空结果区别，写入失败保留选择，已经导入但读取失败仅重读。导入已提交而自动补全启动失败时明确返回warning，并恢复识别旧状态与待核对时效保护。229项相关单测、构建、真实合成扫描/失败重试/37目录重复与重启、17组设置识别时序回归通过；11张修改后导入状态图及4张基线图，版本/证据缺口见 [第5次记录](OVERNIGHT.md#第5次可控的导入清单已完成)。无真实API/游戏验证，尚未打包或替换正式程序。

- 第6次夜间源码`1908c3123192ebb07fdf28125e1db650ab8cf474`将核对改为可搜索队列与独立对比/决策区，支持窄窗150%、筛选外当前项、完整来源展开及持续错误恢复；提交期间锁定交互，部分成功重新读取实际关联，已确认后的读失败仅重读，关闭重开也不重复提交。最终167项定向单元、新6组原生核对与9组既有核对语义回归通过；2张基线/11张最终状态图和5张来源回归图准确版本见 [第6次记录](OVERNIGHT.md#第6次可定位可比较可恢复的核对工作区已完成)。均为合成资料/模拟API，停滞运行与未验证范围如实保留；尚未打包或替换正式程序。

- 第7次夜间源码`583e46dd8df921fff25b4dbacc76ae6dbc656e9e`明确详情四组草稿与分组保存，正常关页/标签导航先确认放弃，封面切页保留，保存中阻止离开；原安装参数不会因默认切换静默丢失或投到新安装。已保存而读取失败仅重读，恢复后才离开，重开不会从旧快照覆盖已提交值；Escape与嵌套确认已实际检查。219项相关单元、8组新原生及5/9组既有详情/核对回归通过，16张图与版本、停滞/未验证范围见 [第7次记录](OVERNIGHT.md#第7次详情草稿与可信保存已完成)。合成profile/模拟接口，不保证应用退出或强杀后的未保存草稿持久化；尚未打包、替换正式程序。

## 已实现的核心流程

| 流程 | 当前行为 |
| --- | --- |
| 书架与整理 | 窄导航保留中文短名，收藏列表包含全部分类及特别喜欢。顶栏搜索、排序、封面/列表切换；进度、本地/愿望单、收藏、分类、标签和评分筛选仍可组合。短继续条仅显示有效候选。本地封面网格只显示3:4封面及两行名字，列表/详情保留单评分（个人 0 分优先）、状态、收藏、时长、未安装/待设程序与启动动作。显式铺满对长竖图及所有比例生效；柔化/纯色保留特殊比例全图，普通竖图仍按20%裁损边界自适应。单游戏模式优先于已保存全局默认，小缩略图保持原策略。整理勾选独立，批量修改只处理当前可见勾选项；偏好保存在本机。主栏目保留醒目的中文标题及较小Galgame装饰名。 |
| 本地导入与附加 | 普通扫描用户勾选确认，精确重复跳过、父子范围重叠明确拒绝。未安装条目的「添加本地游戏」由明确根目录和程序选择把安装附加到现有条目，不自动猜测跨条目合并。已有有效默认不会因给另一个安装设置程序而切换，显式设置默认仍可用。 |
| 补全资料 | 默认导入后自动补全；未配置密钥仍可本地导入/明确附加。常驻「资料整理」的「只补缺失」处理没有资料或缺少封面的本地游戏；已有绑定但缺封面时刷新原作品。 |
| 重新识别 | 支持整库、所选、单个游戏和扫描范围内已入库游戏，包含已有完整资料的本地条目。重新读取独立本地线索，旧的自动生成标题不能证明旧关联正确。同 ID 且证据可靠时刷新，不同 ID 保留原关联并进入待核对，确认后才改绑。 |
| 核对与取消 | 「资料整理」显示当前/最近任务与稳定核对入口。夜间源码支持队列搜索而保留当前决策，实时关联/候选比较、完整安装来源展开与独立详情，持续失败及只读恢复；稍后保留队列，保留当前只结束该建议。旧队列降级可读，过时建议须手动搜索/重新识别，提交复核用户看到的核对 ID 和身份指纹。取消阻止尚未开始保存的结果，已开始的原子保存会完成。 |
| 发现与详情 | NextMoe 搜索、分页、去重和原页重试；详情展示简介、角色、开发商、来源评分与链接。使用资料源默认封面，票数不影响自动选图。详情「封面与展示」可反复选资料图、恢复默认、选择本地PNG/JPG及指定展示模式或跟随全局。目录手选图同作品刷新保留、换关联清除；本地图片作为个人字段在刷新/换绑后保留，明确选默认或另一资料图才清除。无资料关联也可选本地图片。默认随明确刷新更新，新远程图须联网核验；恢复已存默认、仅改展示及本地图片可离线。支持愿望单、手动关联和刷新；未安装条目先附加明确的本地游戏。 |
| 启动与记录 | 通过选定 EXE 和参数数组启动，记录被跟踪进程的运行时长；尝试接续同目录启动器产生的游戏进程。关闭窗口可留在托盘继续计时，「结束计时」不结束游戏进程。 |
| 个人数据与统计 | 名称、状态、0–10 分个人评分、分类、标签、笔记、收藏、会话与总时长；继续游玩、近 14 天、最近会话及常玩榜。当前会话单独显示，不重复计入已保存检查点。 |
| 配置与备份 | 夜间源码统一明确保存全部偏好、保存中后续输入保护、独立移除已保存密钥、保存后测试与配置时效反馈；API 地址、加密密钥、成人分级、明暗主题、界面风格、全局封面展示、程序内名称读取；书架JSON导入导出、原子保存及上一有效版本恢复。第四轮展示文案包含长竖图，内部旧字段及备份格式兼容。导出含本地封面压缩副本与每游戏展示，不含API密钥、全局设置或游戏文件；远程图片仍只保留网址。 |

### 必须保持的行为

- 个人名称、评分、笔记和时长不能因自动识别或迟到响应而丢失；编辑页的未保存草稿也有请求代次保护。
- 资料提交在保存队列内重新检查身份。名称、关联、首选安装、安装 ID、目录或启动程序变化会使旧识别失效；启动参数和个人字段编辑不会无故取消有效结果。
- 同批自动识别可以合并同一作品的多个安装；后来的明确人工操作会终止旧批次的合并权限。
- 实际联网请求由 Electron 主进程发出。前端只获得是否保存密钥的状态；正式包忽略开发页面环境变量。

## 模块入口

| 文件 | 维护时先看什么 |
| --- | --- |
| [src/App.vue](../../src/App.vue) | 页面、搜索分页、导入、识别、核对和详情编辑的异步状态；多数流程仍集中在此文件。 |
| [src/components/GameCard.vue](../../src/components/GameCard.vue)、[LibraryOverview.vue](../../src/components/LibraryOverview.vue)、[PlayStatistics.vue](../../src/components/PlayStatistics.vue) | 封面卡片、继续游玩和统计；共享计算在 `src/library-workspace.ts`、`src/library-insights.ts`。 |
| [LibrarySavedViews.vue](../../src/components/LibrarySavedViews.vue)、[saved-library-views.ts](../../src/saved-library-views.ts)、[LibraryCollections.vue](../../src/components/LibraryCollections.vue) | 夜间源码的命名动态筛选与分类浏览；UI存储的有界校验/显式失败、改名与更新区别、过时标签保留、实时匹配数量与键盘交互。 |
| [PersonalRecord.vue](../../src/components/PersonalRecord.vue)、[App.vue](../../src/App.vue)、[detail-drafts.css](../../src/detail-drafts.css) | 夜间源码的详情分组草稿、正常离开确认、持续失败和只读恢复、原安装参数归属；共享个人记录/管理输入与明确保存；小窗口基础资料和封面焦点往返。 |
| [CoverPicker.vue](../../src/components/CoverPicker.vue)、[DetailCover.vue](../../src/components/DetailCover.vue)、[cover-display.ts](../../src/cover-display.ts) | 详情封面草稿、原子保存、比例展示与共享个人选图/分级规则；默认预览须与恢复后实际图一致。 |
| [local-cover.ts](../../electron/local-cover.ts)、[SectionSignature.vue](../../src/components/SectionSignature.vue) | 有界PNG/JPG读取、方向/透明处理与JPEG副本校验；主栏目小副标题及视觉主次。 |
| [LibraryMaintenance.vue](../../src/components/LibraryMaintenance.vue)、[installation-attachment.ts](../../electron/installation-attachment.ts) | 常驻整理面板；明确安装附加的真实目录边界与有界独立编号冲突检查。 |
| [MatchReviewWorkspace.vue](../../src/components/MatchReviewWorkspace.vue)、[App.vue](../../src/App.vue) | 夜间源码的核对队列检索、响应式对比/决策区、准确建议身份、提交锁定与持续错误/只读恢复；既有来源与过时保护继续生效。 |
| [src/shared/types.ts](../../src/shared/types.ts)、[src/desktop-bridge.ts](../../src/desktop-bridge.ts)、[electron/preload.ts](../../electron/preload.ts) | 数据类型、Vue 响应式对象转普通数据快照、受限桌面 API。修改接口时三处需一致。 |
| [electron/main.ts](../../electron/main.ts) | IPC 参数及来源校验、原生对话框、身份操作队列、窗口与托盘生命周期。 |
| [electron/library.ts](../../electron/library.ts) | 本地扫描、安装、备份、原子事务、资料代次及合并保护；数据可靠性的关键入口。 |
| [electron/metadata-matching.ts](../../electron/metadata-matching.ts) | `missing` / `reidentify` 模式、任务取消、来源时效、候选详情复核与待核对队列。 |
| [electron/game-identification.ts](../../electron/game-identification.ts)、[evidence-matching.ts](../../electron/evidence-matching.ts)、[title-matching.ts](../../electron/title-matching.ts) | 本地线索、公开索引、候选排序、独立证据以及章节和版本冲突规则。 |
| [electron/local-game-evidence.ts](../../electron/local-game-evidence.ts)、[local-text-evidence.ts](../../electron/local-text-evidence.ts) | 有界 PE 版本资源读取、Steam 配置、已知说明书或配置标题；不执行游戏或 HTML。 |
| [electron/nextmoe.ts](../../electron/nextmoe.ts) | NextMoe v2 请求、鉴权、超时、错误脱敏、重试、游标、作品合并、作品和发行映射。 |
| [PreferencesSettings.vue](../../src/components/PreferencesSettings.vue)、[App.vue](../../src/App.vue) | 夜间源码的统一设置工作台与草稿/连接反馈；字段编辑代次、成功回复确认、清除旧密钥独立于草稿、明确失败与键盘焦点保护。 |
| [electron/playtime.ts](../../electron/playtime.ts)、[settings.ts](../../electron/settings.ts) | 进程跟踪、累计检查点，以及设置队列和 Windows 密钥加密。 |
| [src/bridge.ts](../../src/bridge.ts) | 无桌面桥时启用的浏览器演示；不能用此模式证明真实导入、识别或启动有效。 |
| [tests](../../tests) | 单元测试与隔离 Electron 回归。重点先看 `metadata-concurrency`、`metadata-matching`、`desktop-reidentification`、`desktop-cancel-queue` 及各页面并发测试。 |

样式主要分布在 `src/style.css`、`src/order-theme.css`、`src/library-layout.css` 和组件局部样式；卡片/继续条与整理/核对样式集中在 `src/library-refresh.css`、`src/workspace-refresh.css`。第六轮依次加载 `src/windows-theme.css`（保留全局系统字体/无障碍，Windows覆盖避开经典）、`src/glass-theme.css`（玻璃材料）与 `src/desktop-caption.css`（原生按钮安全区域）；`src/components/LiquidGlassMaterial.vue`仅在已保存玻璃风格时挂载，`src/liquid-glass.ts`负责有界、事件驱动的SVG背景折射及卸载；准确顺序见 `src/main.ts`。保持 KunUI 组件、明暗主题和较小窗口下的可用性。

## 真功能、演示数据与未实现内容

### 真实桌面能力

桌面应用通过 preload / IPC 操作本地 JSON 书架和安装，NextMoe 是作品目录 API。搜索先读取 `catalog/search`，再批量取作品卡片；详情、来源编号查询及有界发行列表也已接入。它没有 LLM 模型参数或 SSE 文本生成。

识别使用文件名、已知小配置、标题索引和可选程序版本资源。正式标题、罗马字、机器翻译、缩写等来源有区别；旧 PotatoVN 编号不能单独证明身份，相关线索不会重复计作独立证据。

### 演示与测试数据

- `public/preview-data.json` 是 12 部公开作品的缓存资料；浏览器演示在内存中生成无本地安装的书架。它不是个人库，也不是实时搜索结果。
- `tests/ui-fixtures/` 的封面为原创合成测试素材。桌面回归中的游戏、安装和会话为测试记录，联网结果由测试替换。
- `data/title-index.json.gz` 是 PotatoDBMapper 公开数据的派生索引：56,064 条记录、97,038 个标题，只含标题与来源编号。来源和许可见 [THIRD_PARTY_NOTICES](../../THIRD_PARTY_NOTICES.md)。
- 已有截图使用独立合成 profile。截图展示真实程序界面；其中封面、评分、路径、时长和匹配结果不能解释为用户真实书架或线上识别率。

### 尚未实现

云同步、Steam 库自动扫描、自动补丁安装、压缩包解包、`.bat` / `.lnk` 启动、仅统计前台时间、PotatoVN 全量迁移、远程封面离线缓存，以及自动更新公开标题索引。本地手选图片副本已保存，不能与远程缓存混称。不要把未实现内容写为可用功能。

## 已知边界与待办

1. **识别仍需要核对。** 目录缩写、合集、版本差异和上游缺失可能没有唯一答案；来源编号及程序元数据都可能过时或被复制。排序系数不是正确概率，资料关联也不验证游戏本体或文件安全。
2. **PE 读取有实际系统影响。** 新设置默认开启程序内名称读取，可在偏好设置关闭。即使不执行 EXE，读取也可能触发 Windows 扫描和隔离。隔离演示只使用合成程序夹具；已有采集保留默认开启状态，只扫描合成惰性 EXE。不能据“仅被读取”认定检测为误报。
3. **统计有明确范围。** 包括后台运行时间；一次跟踪一个游戏。跨日分布按会话区间分摊，未保存暂停发生位置，因此不是逐分钟精确活动日志。复杂外部启动器或目录外子进程仍需验证。
4. **上游接口与数据会变化。** 请求已有错误、限流和超时处理；第二轮已使用用户新提供的测试密钥实际验证作品84的详情、票数和图片，不代表全部作品、权限或配额已验证。尺寸缺失不猜竖图，图片失效仍显示失败占位。不得从正式个人设置提取密钥用于测试。
5. **开发依赖公告尚未处理。** 本机迁移审计记录 9 个受影响依赖条目（8 high、1 low；不等于 9 个独立漏洞），涉及 `electron-builder` 及下载/缓存依赖链和 `esbuild`。生产依赖审计当时为 0。这是迁移阶段的报告，本轮没有重新审计或升级；后续应单独评估升级并重跑打包回归。依据：[完整审计](../../.artifacts/migration/dependency-audit.json)、[生产依赖审计](../../.artifacts/migration/runtime-dependency-audit.json)。这些本机报告不进 Git，其他环境需重新生成。
6. **资产许可分别保留。** 应用源代码、KunUI、公开索引和游戏资料各有原有许可或权利归属。当前纹章重建自用户提供的游戏截图，源码许可不包含底层游戏图案的权利；详见第三方说明。
7. **本地照片是压缩副本。** 只接受静态PNG/JPG，源图不超过20MiB/4000万像素，保存最长边1600及128KiB以内JPEG，透明铺白底；原有书架32MiB总容量不变。新程序读取旧备份兼容，不保证旧候选无损读取新字段。真实照片的全部编码变体、大库照片性能尚未验证。
8. **缩放布局的证据范围。** 睡前正式版本分类长名称在1000×700、150%下横溢；夜间第0次用更长的合成分类复现1503px文档/667px视口并修为657px文档/667px视口，另三种窗口与缩放也通过，此修复现已随正式替换部署。第0次同时发现第三轮部分“1000×700”详情/设置图实际仍为大窗口，撤回这部分尺寸证据；六栏目独立布局采集仍有效。详见迭代记录勘误与夜间记录，不能据局部结果声称所有页面布局通过。

## 评审顺序

| 优先页面 | 建议检查 |
| --- | --- |
| 1. 我的书架 | 1280×720 下首屏信息与可点击区域；长名称、完整封面、缺封面、无默认程序；搜索、筛选、列表切换及批量操作范围。 |
| 2. 详情 → 管理 | 个人名称、笔记和评分的可读性；多安装和默认程序是否易懂；刷新、切换游戏或切换安装时未保存草稿是否稳定。 |
| 3. 添加游戏 → 重新识别 → 待核对 | 区分新导入与已入库；区分补全、刷新和重新识别；进度、取消、不同候选的确认、稍后与保持当前是否清楚。 |
| 4. 发现、统计与偏好设置 | 分页重试和空状态、统计解释、密钥保存与连接测试反馈、程序内名称读取说明，以及明暗主题。 |

先评审实际页面，再提出改版；保留上述数据与并发约束。截图无法替代键盘、滚动、窗口缩放和交互检查。

## 验证记录与截图

### 第四轮简洁书架候选（2026-10-04）

定向单元50/50、类型/构建/隔离打包、正常生产EXE五组回归通过；同夹具前13/后17张原生截图覆盖明暗、长竖铺满/柔化/纯色、列表/详情/预览、小窗150%、导航收藏/选择/重启。补充当前构建的模拟IPC回归26项测量/15图通过，覆盖125–200%键盘与加载/失败恢复。截图索引`.artifacts/iteration-04/compact-library/README.md`，包/提交/图哈希回执`.artifacts/iteration-04/committed-evidence.json`；准确结果、诊断修正与未验证范围见[第四轮](ITERATION.md#第四轮简洁书架与封面铺满2026-10-04)。未重跑整套测试，没有真实API/游戏验证。

隔离试用：`& 'D:\Projects\The-Order\scripts\start-iteration-candidate.ps1' -Iteration 04`，数据固定为`D:\Projects\The-Order\.artifacts\iteration-04\trial-profile`，六条虚构书架及原创图片、无预置密钥，重开保留试用修改。该脚本实际可见窗口/退出0已验证。候选ASAR为`c95327d82981bd69a9d28cff8e642d1f08a96c70d9daaf017d3d54f1e06fc0bd`，原正式仍为下方夜间版本，正式数据未访问。

### 夜间最终候选（2026-10-04）

完整产品单元639项、辅助测试9项，以及正常生产EXE的6组烟测和原资料保留回归通过；实际隔离试用入口、六条数据与本地图解码、宽100%/小窗口150%已检查。本夜真实API/游戏未验证，不把早前作品84的线上检查算作本夜结果。最终截图索引与准确提交为[第8次记录](OVERNIGHT.md#第8次最终候选与晨间交付已完成)，本机总回执`.artifacts/overnight-2026-10-04/round-8/committed-evidence.json`；后续用户授权正式替换及正式入口隔离检查见 [替换记录](OVERNIGHT.md#用户授权替换正式程序2026-10-04)。本次没有重跑上述整套测试。

### 第六轮三风格证据

当前图像清单：`.artifacts/iteration-06/appearance/after/2026-10-04T04-58-16-064Z/manifest.json`（53图），同条件Windows基线：`.artifacts/iteration-06/appearance/baseline/2026-10-04T04-19-01-922Z/manifest.json`（11图）。代表图为`glass-grid-dark-1440x940-zoom1.png`、`glass-detail-light-1440x940-zoom1.png`、`glass-active-filters-dark-500x600-zoom1.png`和`glass-reduced-transparency-detail-simulated-500x600-zoom1.png`，均位于最终目录；同目录`classic-*`、`windows-*`供比较。原创合成数据，未调用真实API。光学棋盘对照另在`.artifacts/iteration-06/optics/2026-10-04T04-52-35-076Z/`，是实际滤镜像素验证，不是正常产品界面。版本绑定与未验证范围见[第六轮记录](ITERATION.md#第六轮三套可切换界面2026-10-04)。

### 睡前候选（第三轮）

第三轮证据、启动方式及准确候选提交见 [第三轮记录](ITERATION.md#第三轮全局封面偏好本地图片与栏目装饰2026-10-03)，截图索引为 `.artifacts/iteration-03/cover-personalization/README.md`。部分详情/设置小窗口采集的实际尺寸与文件名不符，不再作为1000×700验证，详见该轮后补勘误；其他栏目独立布局测量仍保留。图片真实codec/备份、源码定向回归及正常EXE试用哈希不受采图缺口影响。夜间源码的分类页同条件原生窗口对照另列于 [OVERNIGHT](OVERNIGHT.md)，不冒充睡前正式程序。

### 第二轮调整（历史候选）

候选 `9c27af3` 的入口、223项定向回归、真实桌面、最终包与线上默认图结果集中在 [调整记录](ITERATION.md#第二轮调整默认封面与个人选择2026-10-03)。该轮截图索引为 `.artifacts/iteration-02/cover-preferences/README.md`：相同4部合成作品、CC0封面、两种窗口与缩放的修改前8图、修改后24图及1张小窗口键盘保存图。先于提交采集的源码/构建哈希已核对与候选一致，详见该目录 manifest 和 package-check。线上作品84图另作真实接口证据，不能与合成图当作同条件视觉对照。

初次票选候选 `c58ec38` 与200条夹具、14张界面图保留在 [原第二轮记录](ITERATION.md#第二轮封面改进2026-10-03) 和 `.artifacts/iteration-02/cover-final/README.md`，属于被默认封面调整取代的历史证据；该历史候选交付时正式程序仍是第一轮。

### 第一轮迭代（历史已发布程序）

第一轮的提交、隔离启动方式和截图索引集中在 [ITERATION](ITERATION.md#提交程序与试用数据)。当时执行 447 项单元、必要桌面链路和专项、实际明暗/缩放/键盘检查及独立打包入口验证；具体结果与限制以该记录为准。当时的书架对照、整理/核对和附加安装材料均使用合成资料，无真实 API 或游戏执行。下面的迁移与 14:06 截图仍作为历史基线，不能说明第二轮改动后的当前界面。

### 已有迁移记录，非本轮重跑

已阅读本机 `.artifacts/migration/` 的报告和日志：395 项单元测试通过；迁移报告记录 13 个打包桌面测试套件及 26 项界面检查通过，新目录的安装、类型检查、构建、打包和隔离启动完成。测试使用临时 profile、合成游戏及模拟 API，没有据此证明真实游戏库识别率。

依据：[迁移报告](../../.artifacts/migration/migration-report.md)、[单元日志](../../.artifacts/migration/unit-tests.log)、[打包日志](../../.artifacts/migration/packaged-tests.log)。这些结果属于迁移阶段；历史文档交接没有重新运行整套测试，当前迭代的验证另列。

### 已有页面证据（2026-10-03 14:06 采集）

证据目录：`D:\Projects\The-Order-Artifacts\review-current`。已有采集记录显示，于 **2026-10-03 14:06（北京时间）** 实际运行 `release/win-unpacked/The Order.exe`，应用版本 0.7.1、`packaged=true`；源码对应旧交接基线 `9c456115b0420513d25388463b8bb14a0f17d956`。manifest 记录的内容区域为 1440×960，6 张 PNG 实际像素均为 2882×1922。文件清单、SHA-256 和结果见 [manifest.json](../../../The-Order-Artifacts/review-current/manifest.json)，场景说明见 [截图 README](../../../The-Order-Artifacts/review-current/README.md)。

| 已有截图 | 场景及数据性质 |
| --- | --- |
| [01-library.png](../../../The-Order-Artifacts/review-current/01-library.png) | 6 部虚构作品的明亮书架：状态、0 分、收藏、时长及未安装的想玩条目。 |
| [02-detail-overview.png](../../../The-Order-Artifacts/review-current/02-detail-overview.png) | 作品详情：合成简介、开发商、来源评分和标签。 |
| [03-detail-management.png](../../../The-Order-Artifacts/review-current/03-detail-management.png) | 管理表单：状态、个人评分、分类、标签、笔记和合成安装目录。 |
| [04-import-selection.png](../../../The-Order-Artifacts/review-current/04-import-selection.png) | 真实扫描 4 个合成目录；1 个已有条目禁选，3 个新条目可导入，默认勾选自动补全。 |
| [05-recognition-running.png](../../../The-Order-Artifacts/review-current/05-recognition-running.png) | 真实识别任务的进度横幅；测试暂挂模拟 API 响应以保留运行中状态。 |
| [06-recognition-review.png](../../../The-Order-Artifacts/review-current/06-recognition-review.png) | 同名冲突产生的 2 个模拟作品候选，以及真实待核对界面和未匹配队列。 |

管理与导入截图记录的是弹窗当时的滚动位置，下部操作需要继续滚动，不表示所有控件同时可见；弹窗可滚动本身不是已确认缺陷。

**该次采集记录的覆盖：** 桌面程序、IPC、扫描、导入、持久化、识别算法、NextMoe 响应解析和候选手动确认。原生文件夹选择器和网络响应由测试驱动替换，8 次 API 请求均使用模拟响应；封面来自仓库 CC0 矢量夹具，标题、角色、评分、笔记和安装全部为合成数据，假 EXE 未执行。

运行记录显示：初始 6 条，加上导入的 3 条后为 9 条；自动匹配 1 条，同名冲突有 2 个候选，手动关联已保存，另有 1 条未匹配记录保留待核对。`rendererErrors=[]`，真实 API 调用为 0，实际游戏运行次数为 0；合成密钥已清除。截图目录仅含 6 张图片、说明和 manifest，不含 profile、密钥、源码或安装包。

**该次采集没有覆盖：** 全量单元测试、重新构建、完整桌面回归、实时 NextMoe 服务、真实账号和游戏库、真实游戏启动与计时。该次采集也没有修改正式版本。6 张截图不包含窄窗口、深色主题、完整键盘遍历或取消竞争验证；这些需在后续相应评审中单独检查。

### 第一轮协作的评审准备核对（历史）

- 准备起点为 `feab2ec91692132507cb83306c3b2e9af4e8c08b`（`main`）。与截图 manifest 的源码基线相比，仅新增 `AGENTS.md`、`CLAUDE.md` 和本文，业务源码、资源、配置与锁文件没有变化；本轮也只修改这三份文档。
- 六张截图均已实际打开检查，SHA-256 全部与 manifest 一致；现有材料覆盖书架、详情与管理、导入选择、缺资料与识别运行、完成和待核对状态，足以开始 Claude 的源码与静态界面只读评审，无需重新采集。
- 本轮只读核对证据，没有重跑测试、构建、打包或启动应用。上文采集运行结果与迁移测试结果均为已有记录；截图不能证明交互、取消竞争、真实联网或游戏启动行为。发现、统计、偏好设置、窄窗口和深色主题缺少本组截图，评审这些界面时须标明证据范围，不把缺图视为已验证。

## 接手时的工作方式

- 在正式仓库工作，不把历史输出、旧候选源码或发布目录作为开发入口。
- 普通开发、`npm start` 和正式 EXE 默认共用 `%APPDATA%\KunGalManager`。调试先设置 `KGM_DATA_DIR` 为独立目录；具体命令见 DEVELOPMENT。
- 不提交 profile、密钥、个人备份、构建、报告或截图；不运行真实游戏来验证纯界面改动。个人库兼容工具要求显式启用且不属于默认测试。
- 改动匹配、保存或身份逻辑时，先补有行为意义的合成回归；修复后运行对应测试，再由维护者统一进行构建和必要的桌面验证。
