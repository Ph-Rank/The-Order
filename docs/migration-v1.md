# 跨管理器迁移内核 v1

## 六种进度与优先级增量（2026-10-19）

用户明确选定普通导入默认「未设置」、迁移保留来源进度。进度为玩过 > 在玩 > 想玩 > 搁置 > 放弃 > 未设置。既有持久值 planned 保持原样、展示改为未设置；新增 wish 表示想玩，PotatoVN PlayType 0/5 分别对应 planned/wish。领域规则统一编辑、筛选、批量操作、列表及统计；显式个人编辑仍可降级，自动优先级用于迁移、导入及明确合并。

已确认身份和准确安装对应后，仅进度取较高值；笔记、评分、时长冲突不再阻止独立的进度升级，也不因升级而覆盖这些字段。重放已导入记录支持相同规范化事实、仅进度改变及旧适配器把想玩合入 planned 的情况。来源个人字段也发生变化时，需再次通过明确外部身份、安装及版本检查才可只更新进度；其他字段不重放。不重复追加来源、标签、日累计或总时长，身份冲突仍阻断。原始导入记录和字段来源保留，新的 ProgressUpdate 单独记录进度、来源文件摘要、来源记录摘要及时间。预览显示进度前后值，计划 policy 升为 2。

NativeCodec 对 wish 使用扩展 v4 的 progressStatus，旧 JSON 主状态回退为 planned；有进度更新来源的记录同样使用 v4 保护新来源字段。既有其他记录不强制升级。旧 NativeCodec 会对 v4 只读降级，不把全库当损坏而回退到较旧备份。已用前一日用构建的实际 Codec 程序集在无 Profile 的合成数据上验证此行为；新 Codec 读旧 planned 仍可写、且不在打开时改盘。普通兼容 Reader 没有修改。

NextMoe 同步明确区分 unset 与 wish：未设置没有上传状态，不会被自动上传为想玩或清空云端；读取云端进度同样取较高值，独立云端通关细节保留。旧同步检查点值仍能读取。构建与替换仅程序文件，仍不使用 Computer Use、桌面、真实库或真实游戏；最终证据见 `.artifacts/progress-status-20261019/`。

## 设置页入口增量（2026-10-19）

用户后续已明确授权设置页选择 ZIP、导入当前书架及替换本机程序。维护分支为 `codex/migration-settings-20261019`，从保留统计快照及 CLI 的 `c8d6ffd` 继续。下方 Test-only 描述是内核第一阶段历史，不再代表应用内入口；CLI 本身仍保留 Test-only 网关，没有 Production 参数。

设置页仅接收用户选择的 ZIP，复用同一个 PotatoVN Adapter/Resolver/投影与现有打开的事务 writer，不另开目标 Profile。预览对象只在进程内生成，绑定 writer epoch 和精确 snapshot；导入时变更了书架、换了 writer 或存在活动会话会拒绝旧预览。原子事务内先将当前书架存入既有备份目录，以「导入前」标识、回读并逐字节校验，再提交整批新增/缺项补全。失败、取消、迟到读取和提交后取消分别处理；CLI 不能绕过其 Test 门禁。来源始终只读，不解压，不探测游戏目录。

预览显示名称、操作和跳过原因；候选或冲突保持跳过，暂不新增逐条手动合并向导。导入后刷新当前书架，保留资料来源与无日期历史；云端自动同步先暂停。详细用户步骤与回退语义见 `migration-trial.md`。新备份原因白名单仅增加「导入前」，不改变主库/凭据模型。验收仅无界面、公开合成格式与隔离 Test，真实用户导出及 WinUI 点击尚待用户试用；实际构建/替换结果见 `.artifacts/migration-settings-20261019/`。

本轮只实现命令行与独立内核，不提供 WinUI 导入向导，不开放 Production 写入。开发从最新已提交基线 `0227ea3` 建立独立 `codex/library-migration-20261019` 分支；原 `codex/statistics-dashboard-20261019` 的未提交统计功能和日用程序保持原状。验证仍使用同一时长台账与原生库事务，不能把本分支当作已经合并统计界面的发行候选。

## 公开格式调查与选择

调查日期为 2026-10-19。以下均为官方文档、公开仓库中固定提交的事实，未安装或运行其他管理器。`main` 快照不等于其全部已发行版本；没有拿自己的 JSON 声称兼容未知数据库。

| 管理器 / 固定源码 | 实际结构与导出 | 关键个人字段及身份 | 本轮决定 |
| --- | --- | --- | --- |
| PotatoVN `d877bbe166e5b71d6520784f9d268e6df7eb4487`，发行标签 `v1.10.2.0` | 官方 `.pvnExport.zip`；`data.galgames.json`、`data.galgameSources.json`、`data.categoryGroups.json`、`data.dataStatus.json`。导出明确仍使用 JSON，当前运行时已转 LiteDB。 | `Name/OriginalName/ChineseName` 的 `Value`、`Ids`、`Uuid`、`MyRate`、`Comment`、`PlayType`、`TotalPlayTime`、`PlayedTime`、`LastPlayTime/AddTime`；现代安装在库的 `Galgames` 条目及 `LocalConfig` 中。 | 第一阶段实现；官方导出稳定、公开测试资料可验证，无新数据库依赖。 |
| Vnite `36df84c5ded93b24fa86a269fb3f93f473316061`，该源码 package 声明 `4.14.0` | `backupDatabase` 先关闭各数据库，再压缩数据目录；PouchDB 的 `game`、`game-local`、`game-collection` 分库。不能把备份当普通 JSON。 | `metadata.name/originalName/tags/*Id`，`record.score/playStatus/playTime/timers/dailyPlayTimes`；本地路径、文件/URL/脚本启动配置另存于 game-local。 | 格式调查完成，适配器未实现。需要固定 PouchDB/LevelDB 备份版本、读取工具及单位核验，不引入整套 Electron/PouchDB 运行环境。 |
| ReinaManager `97de1ac4bd7f6eedb9198560e5df21ea71a4f636`，main 快照 | SeaORM / SQLite；官方数据库备份使用 `VACUUM INTO`，不是运行中复制单个主文件。已有版本化迁移，最近拆分安装路径及来源等表。 | `games` 的路径、executable、launch_type、user_rating/custom_data；`game_sources`、`game_statistics`、`game_sessions`、集合关联表；统计表有 total_time/session_count/daily_stats，会话有 start_time/end_time/duration。 | 推荐下一候选；本轮不读取 SQLite，尚未核验各迁移版本、时间单位与备份实样，不猜表结构兼容。 |
| Playnite `d1dc225369491671fdb9c5815767dd4d2226ca34`，master 快照 | 官方备份按 library/libraryfiles/extension 等内容分类；库使用 LiteDB/BsonMapper，不能当 SQLite。仓库中的历史 LibraryExporter 测试插件只是有限列 CSV。 | SDK 的 Favorite、CompletionStatusId、UserScore、Notes、InstallDirectory/GameActions、Added/LastActivity、Playtime；Playtime 明确为秒。详细会话不是仅凭总时长可重建的字段。 | 适配器未实现。优先稳定、明确版本的只读导出契约，不能把某个插件任意 CSV 当完整格式。 |
| LunaBox `99c2d21d7ca77462868616d75211998c47400e2d`，main 快照 | 当前为 monorepo，后端依赖同时包含 DuckDB 和 SQLite 相关能力，备份有专门服务与迁移；不能沿用旧版描述断言只有一种数据库。 | 游戏模型有状态、评分、路径、来源等；`PlaySession` 明确 duration 为秒、起止为 time.Time。 | 后续候选，未实现；须进一步固定实际备份容器及数据库版本。 |

主要原始证据：

- [PotatoVN 官方数据交换说明](https://potatovn.net/usage/advance/data-exchange.html)。
- [固定导出任务](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager/Models/BgTasks/ExportTask.cs)、[游戏导出实现](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager/Services/GalgameCollectionService/GalgameCollectionService.cs)、[版本状态和导出仍为 JSON 的说明](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager/Models/LocalSettingStatus.cs)。
- [PotatoVN 模型](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager.WinApp.Base/Models/Galgame.cs)、[安装及启动配置](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager.WinApp.Base/Models/Sources/GalgameAndPath.cs)、[UUID / 旧 UID 关联转换器](https://github.com/GoldenPotato137/PotatoVN/blob/d877bbe166e5b71d6520784f9d268e6df7eb4487/GalgameManager/Helpers/JsonConverters/GalgameAndUidConverter.cs)。
- [Vnite 官方备份实现](https://github.com/ximu3/vnite/blob/36df84c5ded93b24fa86a269fb3f93f473316061/src/main/features/database/services/backup.ts)、[PouchDB 分库](https://github.com/ximu3/vnite/blob/36df84c5ded93b24fa86a269fb3f93f473316061/src/main/core/database/BaseDBManager.ts)、[模型](https://github.com/ximu3/vnite/blob/36df84c5ded93b24fa86a269fb3f93f473316061/src/types/models/game.ts)。
- [Reina 官方一致性备份](https://github.com/huoshen80/ReinaManager/blob/97de1ac4bd7f6eedb9198560e5df21ea71a4f636/src-tauri/src/backup/database.rs)、[游戏实体](https://github.com/huoshen80/ReinaManager/blob/97de1ac4bd7f6eedb9198560e5df21ea71a4f636/src-tauri/src/entity/games.rs)、[会话实体](https://github.com/huoshen80/ReinaManager/blob/97de1ac4bd7f6eedb9198560e5df21ea71a4f636/src-tauri/src/entity/game_sessions.rs)。
- [Playnite 官方备份文档](https://api.playnite.link/docs/manual/library/backup.html)、[数据库实现](https://github.com/JosefNemec/Playnite/blob/d1dc225369491671fdb9c5815767dd4d2226ca34/source/Playnite/Database/GameDatabase.cs)、[SDK Game 模型](https://github.com/JosefNemec/Playnite/blob/d1dc225369491671fdb9c5815767dd4d2226ca34/source/PlayniteSDK/Models/Game.cs)。
- [LunaBox 当前依赖](https://github.com/Saramanda9988/LunaBox/blob/99c2d21d7ca77462868616d75211998c47400e2d/packages/backend/go.mod)、[会话模型](https://github.com/Saramanda9988/LunaBox/blob/99c2d21d7ca77462868616d75211998c47400e2d/packages/backend/models/play_session.go)。

本机原始证据下载、哈希及首次错误路径的 404 保留在 `.artifacts/migration-v1/research/evidence.json`；没有执行下载的代码。该临时调查目录不提交，也不作为应用运行依赖。

## 处理链路

`PotatoVnImportAdapter` → `ImportDataset / ImportGame` → `MigrationResolver.Preview` → `MigrationPlan` → `MigrationTestTarget.ApplyAsync` → 现有 `TransactionalLibrary` → 现有 Native Profile 原子持久化。

适配器只读流，核心不依赖 WinUI。CLI 的 Windows 文件网关拒绝链接、硬链接、路径替换和仍有写入者的输入文件。只接受显式 `.pvnExport.zip` 或名为 `data.galgames.json` 的已导出 JSON；不发现、打开或拷贝其他管理器的实时数据库、WAL、缓存或认证文件。解析过程不提取 ZIP 到磁盘，也不枚举或探测其中的游戏路径。

ZIP 中已知 JSON 按白名单读取。其他条目，包括封面和脚本，均不读取内容。路径穿越、绝对成员名、重复成员、Unix 符号链接属性、分卷/ZIP64、异常压缩目录和超预算内容被拒绝。上限为压缩输入 512 MiB、目录 30,000 项、声明解压总量 2 GiB、单个元数据 JSON 32 MiB、10,000 游戏、JSON 深度 32；不以放宽原生库 32 MiB 写入契约换取命中率。超大封面导出包可能超过本版上限，需要下一阶段专门评估流式大包方案。

JSON 拒绝重复属性、类型激活标记、过深或过大结构；只解释已知字段，不反射激活类型。未知显式格式版本及 Live LiteDB 升级标志不被当作导出格式。该 JSON 没有统一发行版本头，因此报告采用“已验证的能力布局”，不是宣称所有 1.8～1.10 及未来发行版均已验收。

## 字段与身份规则

| 来源 | 目标与处理 |
| --- | --- |
| `Name.Value` | 书架名称，保护为个人名称；不伪造 NextMoe 元数据。 |
| `OriginalName/ChineseName/CnName` | 原名及别名保存在 `MigrationOrigins`；不覆盖现有专业资料。未来 UI 应读取该来源事实，不把迁移占位当目录确认。 |
| `Ids[0]/[1]/[7]` | 严格校验的 VNDB / Bangumi / Steam 来源声明。PVN ID 不是 NextMoe ID；其他槽位不猜造。格式错误的历史槽位明确忽略并报告。 |
| `Uuid`，旧版无 UUID | 优先用 UUID；旧格式以名称、明确 ID、路径的稳定摘要标识来源记录，不用数组下标或标题单独作为跨库身份。 |
| `Galgames` / `LocalConfig` | UUID 或旧 UID 可靠关联到逻辑游戏，再保留多安装。现代库条目存在时，不把已废弃的游戏级 Path 追加成虚假安装。相同目录的多套配置需复核。 |
| 本地路径 / exe | 只接受规范的盘符绝对目录及目录内直接 `.exe`；路径存在性未验证。拒绝设备、UNC、环境变量、穿越、ADS、脚本及命令解释器；不访问真实游戏目录。 |
| `ExeArguments`、LE/Magpie、管理员等 | 不自动导入；字符串命令行未经兼容性确认不能转成可执行命令。目标不复制原管理器的启动工具或服务。 |
| 自定义分类组 `Type=2` | 保留“组 / 分类”；自动开发商、引擎、状态分类不冒充个人分类。分类名称不用于猜测“收藏”布尔值。 |
| `Tags.Value` | 保留为带来源的导入标签；不声称它们都属于用户手工创建。 |
| `MyRate` / `Comment` | 个人评分和笔记。`Rating.Value` 是目录评分，不拿来替代个人评分；0 按来源默认未评分处理。 |
| `PlayType` | 明确字典：None→planned（未设置），WantToPlay→wish（想玩），Playing→playing，Played→completed，Shelved→paused，Abandoned→dropped；未知值待核验。 |
| 收藏 | 本次固定模型没有可靠独立布尔字段，保留未知；不从任意本地化标签猜测。 |
| 本地时间 | 明确 offset 直接转换；无 offset 时仅在显式 `--source-time-zone` 下转换。夏令时空档或歧义保留来源本地文字，不猜 UTC。 |

来源 IDs 仍是原管理器的声明，不代表本轮已向 VNDB/Bangumi/NextMoe 联网核验。仅共享作品 ID、却缺少同一安装/版本证据时为 Candidate；同标题不自动合并。明确外部 ID 与完整安装路径同时一致、且没有互相矛盾的 ID 或版本时，才可安全补全。已有名称和目录确认始终保留，进度取双方较高值；笔记、评分、时长不一致时保留这些字段，仍可独立提升已确认条目的进度，其余冲突不自动接受。一般补全的标签与分类采用去重并集，仅进度更新不重放标签。目标有时长时不叠加来源时长；目标空历史才允许填充。

来源内部重复 UUID、重复作品 ID、重复目录、多目标或父子安装目录冲突均先阻断对应记录，不凭排序选赢家。来源无目标且无矛盾时创建独立条目；不会为了整洁把同名作合并。

计划摘要绑定完整来源文件、规范化记录、解析选项、目标 Profile/Store 身份、目标库内容和操作列表。实际提交重新解析和重新预览；换 Profile、改库或改时区都必须重新取得计划。CLI 不接受可注入操作的外部计划 JSON，只接收期望摘要。

## 时长与持久化

PotatoVN 的 `TotalPlayTime` 和 `PlayedTime` 均为分钟，精确乘 60。`PlayedTime` 是按日累计，不是会话。严格接受 ISO 或年在前的日期；歧义短日期不猜文化，保持其总量为无日期历史并报告。日期合计大于总量、重复归一化日期、未知状态等留作 Conflict。

- 只有总时长：使用已有 `TotalPlaySeconds`，没有虚构 Sessions、启动次数或日期。
- 来源按日累计 D、来源总量 T：`TotalPlaySeconds=T-D`，已知日期 D 写入现有时长台账并明确记为“导入按日累计、非真实会话”，有效总量仍是 T。来源精度单独持久化。
- 内核也支持已验证起止时间的真实会话；同一条目不接受会话与全日累计混用、重叠会话或超出总量的会话。此路径有合成单元验证，**不宣称 PotatoVN 导出包含真实会话**。
- 缺失的来源启动次数保持 0，不根据天数或总时长推算。明确提供的来源计数只按原值保留。
- 跨日真实会话沿用既有统计分摊与“估算”标识；没有改写统计算法。

`MigrationOrigin` 与字段来源、记录 ID、原名/别名、外部 ID、时间精度和摘要存于现有库的 `nativeExtras v3`，与条目一起原子提交；没有新增平行数据库或独立幂等账本。普通未迁移条目继续写原 v1/v2，字节契约不强制升级。旧版 NativeLibraryCodec 对未知 v3 的既有策略为只读，不能静默回写丢来源；v4 继续作为未知未来版本测试。此格式尚仅用于本轮 Test Profile，未迁移真实用户库。

原生 codec 的既有文本/路径规范化、32 MiB 限制仍适用；计划在预览阶段就经过真实 codec 的完整投影、编码和校验。字段摘要记录导入投影与来源事实；原导出文件是完整原始证据，不复制进目标。后续显式合并库条目也保留两侧迁移来源；同一来源记录摘要矛盾时拒绝合并。

相同来源记录且没有更高进度时重复导入为 AlreadyImported，不写盘、不重复增加时长。同一来源的可靠较高进度可独立更新；其他来源事实变化需重新核对身份和安装版本，仅接纳安全的进度提升，不重放笔记、评分、标签或历史。原始字段来源摘要保留，新的进度证据单独保存。取消在耐久写入前生效；原子替换已完成则发布已提交状态，即使此时取消也不谎称回退。

## CLI

需要项目固定 .NET 10.0.401 SDK 和现有锁文件；没有安装其他管理器、调用游戏或启动 WinUI。下面从本分支根目录执行。`dotnet` 也可替换为已经配置的该版本 SDK 的绝对路径。

```powershell
dotnet restore native/TheOrder.MigrationTool/TheOrder.MigrationTool.csproj --locked-mode
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- help

# 无参数命令前缀默认 preview；仅生成脱敏报告，不创建目标 Profile。
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- preview --source C:\Exports\library.pvnExport.zip --out C:\MigrationLab\preview-only

# 显式创建全新专用 Test Profile；非空原目录会被拒绝。
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- init-test --target C:\MigrationLab\test-profile

# 对明确目标再预览。来源本地时间已知时可指定时区。
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- preview --source C:\Exports\library.pvnExport.zip --target C:\MigrationLab\test-profile --source-time-zone Asia/Shanghai --out C:\MigrationLab\preview-target

# 只有明确 apply、目标和上次 JSON 中的 planId 才写入；解析选项须一致。
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- apply --source C:\Exports\library.pvnExport.zip --target C:\MigrationLab\test-profile --source-time-zone Asia/Shanghai --commit <planId> --out C:\MigrationLab\committed

# 完全独立的合成演示：生成已知格式、预览、原生 Test 写入、重启、重复导入。
dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- demo --out C:\MigrationLab\new-demo
```

输出目录必须全新；`preview.json` 和 `preview.md` 不输出标题、笔记、私密字段值或绝对路径，只输出记录摘要、计数、字段、问题原因及精度。若导入已提交而报告写盘失败，明确返回 `IMPORT_COMMITTED_REPORT_WRITE_FAILED`，不把已提交谎称为迁移失败。Ctrl+C 支持取消；错误输出脱敏，不回显异常堆栈或传入路径。

专用目标必须由 `init-test` 创建，包含绑定同一 Native Profile/Store 的标记。无 Production 开关、环境变量默认目标或“强制导入”参数。普通 Test Profile、Production/Beta 命名空间、被替换的标记和硬链接输入均不被当作合法迁移目标。

## 验证证据与范围

原始 1.8.0 公开合成 JSON 不改内容保留于 `native/fixtures/migration/potatovn/public-1.8.0/`，附上游 Apache-2.0 许可和 NOTICE。它不是用户真实导出。现代 1.10 风格夹具按固定 writer/UUID/安装及分类契约构造；故障、版本、Unicode、安全、真实会话内核和大型库都是合成验证。

公开夹具为 5 条，来源总量 143,580 秒，按日累计 39,300 秒。首轮 CLI 演示为 2 条、2 个安装，总量 14,520 秒、已知日期 3,600 秒、无日期 10,920 秒，0 个伪造会话和启动次数；原导出哈希不变、重复导入库字节不变。最终演示、原生故障注入、完整测试、性能和提交以 `.artifacts/migration-v1/completion.json`、`report.md` 和 TRX 为准；本文件不把未运行结果写成通过。

最终完整客户端 **4513 passed / 0 failed / 0 skipped**，其中迁移 70 项；相关定向子集 453 项与全套重叠，不相加。独立多进程 CLI 检查 28 项通过：默认只预览、目标绑定、错误摘要、换 Test 目标、旧计划拒绝、报告预检在提交前失败、5 条未改公开 JSON 的实际落盘、重启读取和重复提交均有独立回执。真实起止会话仅为内核合成测试，不能当 PotatoVN 导出能力。

一万条合成来源实际经 CLI 预览、原生提交、重启和重复导入：总量 72,000,000 秒，已知日期 36,000,000 秒，无日期 36,000,000 秒；源字节和重复提交后的库字节不变。初次预览 4.36 秒、提交并重读 13.705 秒，重复预览 10.379 秒、重复提交并重读 20.340 秒；包含进程启动、序列化及报告，且与完整回归并发，不代表所有硬件表现。原生有效载荷 31,673,454 字节，低于既有 32 MiB 上限；带 Base64 身份信封的磁盘文件为 42,231,444 字节，未放宽有效载荷限制。较大笔记、标签等仍可能使预览触及该上限并安全拒绝，不能保证任何一万条库都可写。

首次完整执行 4489 通过、23 失败、1 跳过：20 项无法启动既有固定路径 SDK 的 ProfileProbe，3 项缺少 Electron 合成加密测试依赖；跳过为未启用的本地 Worker 闭环。只在新工作树按锁文件安装依赖、建立 ignored SDK 路径别名并使用既有合成 Worker 入口后，全套 4513/0/0。另保留最初新增测试的数组比较、文件共享夹具错误及 schema 3 已定义后旧未来版本用例的修订记录，未隐藏失败、删除用例或把 Mock 称为真实用户迁移。

Gate 分开判断：已知来源格式解析、转换逻辑、隔离原生 Profile 导入具备独立证据；真实用户跨管理器迁移未验证，WinUI 向导未实现。本轮不推送、发布、覆盖日用，不自动继续 UI 开发。

## 后续最小工作

1. 在现有导入页面接入此预览与显式提交，不重新建立身份或事务系统。
2. 展示 Conflict 的具体字段和 Candidate 的目标选择；人工操作需重新生成受约束计划，不能直接篡改 JSON 操作。
3. 在原名、别名、外部 ID 展示及检索中读取迁移来源事实；只在已确认的目录关联下使用专业元数据。
4. 统计及时长编辑 UI 区分导入按日累计和用户人工修正，保留无日期历史说明。
5. 经明确授权，用脱敏真实导出验证不同版本及规模；明确备份、数据格式回退和真实 Production 的准入，不能直接移除本轮 Test 网关。
6. 优先评估 Reina 官方一致性 SQLite 备份，其后再评估 Vnite 的固定备份导出和 Playnite 的完整只读导出。新增适配器实现同一 `ILibraryMigrationAdapter`，扩展已审查来源白名单及时间单位测试，不内嵌管理器、脚本或另建用户数据库。
