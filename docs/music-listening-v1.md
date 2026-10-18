# 音乐正版入口 v1（未发布开发成果）

本功能仅在 `codex/music-listening-20261018`，从已发布 v1.2.0 冻结提交453c671延续。没有进入1.2.0公开附件，不发布第二版，不更换日用安装或读取Production。

## 已实现

- 专辑的具体Release、Recording详情中按需展开“正版收听与商店”。Work、ReleaseGroup、Artist不冒充录音或具体发行版。
- MusicBrainz的正向、未结束url关系同时检查关系类型、域名与路径粒度，分为收听、购买、资料。网易云与QQ已有关系不需开发者Key；不拼造搜索地址。
- 入口排序复用观看链接的通用平台顺序、来源与安全URL能力。国内优先不删除国际平台，同平台不同发行物保留各自来源。
- 同组数字版本最多读取25条发行候选、2个具体版本；重验专辑组、Official状态、Digital Media介质。明确显示为其他发行物，不更换当前选择，不以部分结果唯一性证明身份。
- Apple官方匿名Search/Lookup：明确已有Apple专辑ID优先，再UPC，再标题与音乐人；JP/CN/US商店。曲目与专辑、国家及返回ID校验，不相关的条码结果不进入候选。
- Apple返回的专辑页面标注iTunes商店目录，核对标题、音乐人、日期及曲目数量后才可手动保存；不声称Apple Music订阅可听，不改域名或地区路径。
- 人工商店选择使用既有Native Profile人工链接组件、原子写入和修订号比较，记录为有方向的独立Related链接，永远不能提升为相同Release身份。可移除，清远程缓存/重启后仍可恢复。
- 新远程载荷复用MusicBrainz缓存的独立键，保留取消、过期与失败时旧值。普通专辑缓存、主书架和身份确认旧schema不变。无新后台服务或音频播放器。

## 请求与平台条件

MusicBrainz所有操作沿用同一个单调时钟调度器，间隔至少1.1秒。Apple按同进程串行3.2秒间隔（约18次/分钟），30秒总期限、429 Retry-After，最多8个候选和1MiB目录响应。仅固定官方API目的地，不携带账号凭据或Cookie；不请求、存储或使用preview音频/Apple封面。

依据：[MusicBrainz API](https://musicbrainz.org/doc/MusicBrainz_API)、[发行版外链关系](https://musicbrainz.org/relationships/release-url)、[录音外链关系](https://musicbrainz.org/relationships/recording-url)、[Apple Search API](https://performance-partners.apple.com/search-api)。平台页面、地区和订阅的即时播放资格未核验，地区未知不表示全球可用。

[网易云官方skills](https://github.com/NetEase/skills)要求开放平台应用配置及登录，不能据此认定零配置开源分发资格；本轮没有申请、安装CLI、扫码或使用私钥。QQ官方入口https://developer.y.qq.com/docs/openapi未提供足以确认零配置匿名查询、免费额度及分发许可的可读条款。本轮两家主动查询均暂缓；MusicBrainz现有国内链接照常支持。不使用第三方反向API、共享Cookie、代理或云端组件。

## 验证范围与证据

`.artifacts/music-listening-20261018/live`：6个Release样本，跨CLANNAD、樱之诗、魔法使之夜、Cowboy Bebop、Celeste及原神搜索对照。原神泛检索的首项不是已确认HOYO-MiX OST，不能当真实跨站匹配。另以精确标题与HOYO-MiX验证City of Winds and Idylls，独立记录于live-domestic。所有请求为匿名只读，有限页数，不拉全库。

`live-csharp-transports.json`实际C#传输取得CLANNAD的Spotify来源关系与Apple JP目录候选。`coverage-replay.json`是冻结真实响应经当前解析器重放，不能称每项均重新联网通过。Apple标题与音乐人搜索对樱之诗返回无关的Songs of the Seafarer，解析器排除；该样本没有UPC，不能称其为真实UPC错配。UPC错误返回及回退另由合成协议测试覆盖。原始返回条数不是有效覆盖率，合成网易云/QQ路径测试与真实国内命中分开统计。

人工选择、错误版本、限流缓存、取消、迟到响应和持久化通过测试验证；首次缓存Key自序列化导致栈溢出已修复并保留失败日志。升级User-Agent为当前软件版本后，旧测试夹具仍写死1.1.0，改为验证真实构建版本与项目联系地址；未降低限流和安全断言。

清空缓存后的真实WinUI联调还发现：原有发行版browse请求未包含media，真实响应缺少介质，而预填完整数据的夹具掩盖了数字版本被过滤的问题。现已补齐官方inc字段，以group-v2独立缓存键避免旧缺介质页遮蔽新查询；旧group键仍可校验、保留，不删除用户确认。两项请求形状/旧缓存反例均先实际失败，再修复通过。251项定向回归通过，早先4b69c9d开发包不可作为最终交付，最终包另行构建复验。

实际UI与最终开发包状态以本轮completion和截图回执为准。界面用独立合成游戏入口及冻结的真实音乐数据；入口映射是明确测试夹具，不是跨站自动匹配证据。所有平台页面访问、播放、真实会员/地区、干净OS及长期稳定性均不在已通过范围。
