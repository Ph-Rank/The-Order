# 跨管理器 ZIP 互通（2026-10-20）

基线 `854ff3f817afde752d1efbe3a496e1c8dfd5f541`，工作分支
`codex/manager-interchange-20261020`。应用版本保持 `1.2.1-beta.1`。
本增量增加四种来源和五种目标的兼容 ZIP，不取代 The Order 原生书架备份。
没有读取 Production 或私人导出，没有发布、推送或替换日用安装。

## 已固定的真实格式

| 程序 | 核实版本与备份容器 | 时间与主要边界 |
| --- | --- | --- |
| PotatoVN | 1.10.2.0 的 `.pvnExport.zip`；公开 1.8.0 JSON 夹具也回归 | 累计和按日为分钟；导出秒数取整、整数评分；不迁移制作人员缓存 |
| Vnite | 4.14.0；关闭后的 PouchDB 9 / LevelDB `game`、`game-local`、`game-collection` | 毫秒转整秒；读取 winning revision，不把被删文档计入；计时器与手工按日累计分别处理 |
| ReinaManager | 0.32.0；官方 `VACUUM INTO` 一致性 `.db` 放入 ZIP | 总量和会话为分钟，时间戳为 Unix 秒；导回时解压并选择 `.db`；部分活动图只显示会话 |
| Playnite | **10.62 稳定版**；`library\\games.db` 等 LiteDB 4，database.json Version=4 | 总量为秒、评分 0–100；基础库没有完整会话，不能从总量伪造；master 新布局不等于稳定版 |
| LunaBox | **1.13.3 稳定版**；DuckDB `EXPORT DATABASE` CSV，`database/` 或旧根目录布局 | 会话秒数与 TIMESTAMPTZ；**无独立无日期总量字段**，不能无损还原 The Order 历史累计 |

固定原始证据：

- [PotatoVN 官方数据交换](https://potatovn.net/usage/advance/data-exchange.html)，源码 `d877bbe166e5b71d6520784f9d268e6df7eb4487`。
- [Vnite 备份实现](https://github.com/ximu3/vnite/blob/36df84c5ded93b24fa86a269fb3f93f473316061/src/main/features/database/services/backup.ts)。
- [ReinaManager 数据库备份](https://github.com/huoshen80/ReinaManager/blob/97de1ac4bd7f6eedb9198560e5df21ea71a4f636/src-tauri/src/backup/database.rs)。
- [Playnite 10.62 原生备份](https://github.com/JosefNemec/Playnite/blob/10.62/source/Playnite/Backup.cs)及 [官方恢复说明](https://api.playnite.link/docs/manual/library/backup.html)。
- [LunaBox 1.13.3 备份服务](https://github.com/Saramanda9988/LunaBox/blob/v1.13.3/internal/service/backup_service.go)，固定提交 `36413a2878111cf0951d69f9c39644b08728b75b`。

未知版本、加密包、插件自定义导出和不完整实时数据库不宣称兼容。
ReinaManager 自身不直接接受 ZIP；本功能的 ZIP 是运输容器，恢复步骤明确要求解压。

## 个人资料与身份

仍复用 `ImportDataset → MigrationResolver → MigrationPlan → TransactionalLibrary`。
源文件及父目录物理身份被固定，ZIP 只读；数据库在 UUID 临时目录的受限副本上打开。
不读取游戏目录，不打开来源内 URL，不复制凭据、游戏、存档、图片或插件，也不执行脚本/启动参数。

VNDB/NextMoe 身份、明确的 Galgame/Visual Novel 类型标签，或现有标题索引中的精确外部 ID
用于确认 Galgame 范围。普通 Action/RPG/Anime 标签及同名没有类型证明作用。
非 Galgame 和缺少可靠类型证据的条目排除，并在预览列出名称与原因；不会为补命中率去联网猜测。
外部 ID 仍是来源声明，不自动升级成 NextMoe 已验证资料。

作品匹配、版本差异、安装冲突继续使用原来的候选与逐条核验；同名绝不自动合并。
笔记、评分、个人名称和原有作品资料不被覆盖；进度取双方较高者。
新来源谱系仍写进原书架文件，与个人字段在同一事务提交，没有平行数据库。

## 三种时长选择

两边累计非零且不同的核验默认选「相加两边时长」，另有「保留本地」「采用来源累计值」。
相加同时保留来源已提供的逐日日期和真实会话起止时间；只有无法归属的余量进入无日期历史。
逐日累计不会被改造成会话，也不推算启动次数、完成日期或连续天数。已有启动次数保持不变。
同一来源记录保存已计入秒数，重导同份包不增加时长；来源总量后来增长只增加未计入差额。
同一来源的每日增长和会话延长只补差额，保留日期来源凭据。完全相同的会话在首次计入时去重；
仅有日期相同不能证明是重复历史，核验框仍提醒用户自行选择是否相加。
有歧义的重叠会话、改写的历史、已被用户修改或撤销的记录不覆盖；预览和 JSON/Markdown 报告说明未分配原因。
真实最近游玩时间取较晚的时刻；只有日历日期时不生成午夜时间。原书架添加日期保持不变。
此前 v6 已相加但没有保存日期的记录，可重新选择**同一份备份**；原核验摘要相符且有可归属余量时，
预览显示「补全游玩日期」，提交后总量不变。来源内容已变化时重新核验，不静默重新对应。
换记录 ID 的来源不能凭同名获得去重证明。手工降低的本地时长不会因重导被自动补回。
进度自动更新不会隐藏尚未解决的时长冲突。

原跨管理器谱系/累计相加使用 NativeExtras **v6**；新的日期来源凭据使用 **v7**。
旧 v1–6 可读；旧客户端遇到不认识的扩展版本需只读。回退程序前应先在新版恢复对应的导入前备份。

## 导出边界

设置页保留「导出书架备份」，另增「导出兼容 ZIP」选择目标，再显示精度/字段损失预览。
目标恢复通常替换目标库，必须先备份目标库，不是自动合并。

保留目标能够表达的名称、来源身份、路径、分类、状态、评分、笔记、累计/会话。
多安装在 PotatoVN 保留；其他目标仅使用第一个安装，提前说明。
Vnite 缺少相同的想玩/放弃字段，以分类保存差异；收藏不能直接表示时作为分类。
Playnite 不伪造插件会话；LunaBox 仅导出真实会话，不能把无日期历史改成一次假游玩。
不能表达的事实放在 `the-order-supplement.json`；**目标管理器不会自动读取该文件**。
`The-Order-导入说明.txt` 与 `the-order-export-report.json` 说明恢复方法和损失。
补充文件也不会在再次导入 The Order 时静默覆盖目标管理器后来修改的原生数据。

## 不可信输入与依赖边界

ZIP 检查路径穿越、规范化重复、链接成员、条目数、声明大小、解压比和实际输出长度。
白名单选择文件，不按 ZIP 路径直接解压；不执行输入 `schema.sql` / `load.sql`。
JSON/BSON 拒绝 `_type`、`$type`、重复属性和超限结构；CSV 有行、列、字段和引号约束。
SQLite 仅查询固定表，禁止扩展和任意 SQL；不接收 WAL/SHM 作为一致性证据。

WinUI 在同一自包含程序的辅助进程解析，不初始化 UI、Profile 或账号。
父进程先固定文件和摘要，再将子进程加入 Windows Job：单进程、768 MiB、最长 90 秒，
管道响应最多 64 MiB；取消/父进程退出会释放自己的子进程。
这限制损坏数据造成的崩溃/资源消耗，**不声称是权限安全沙箱**。

Playnite 10 格式必须使用 LiteDB 4.1.4；[GHSA-3x49-g6rc-c284](https://github.com/advisories/GHSA-3x49-g6rc-c284)
涉及的 CLR/BsonMapper 类型激活路径没有使用。读取只采用原始 `BsonDocument`，递归拒绝类型标记；
写入仅由应用生成字典。NuGet 只对该编号作局部豁免，未关闭审计；不得将这项旧依赖用于其他对象反序列化。
SQLite 采用锁定的更新版本与 SQLitePCLRaw bundle；LevelDB/SQLite 原生文件必须随候选打包。

Vnite 恢复并修改后的真实 PouchDB SST 压缩样本暴露：LevelDB.Standard 2.1.6.1
附带的 Windows 引擎不能解压 Snappy block，会报 corrupted compressed block contents。
因此仅复用它的托管 C API 绑定，排除其 native/build 资产；随程序分发固定的 Google
LevelDB 1.23 + 静态 Snappy 1.2.2 x64 引擎，约 0.5 MiB。上游 ZIP 和实际 DLL 摘要位于
`Migration/Native/win-x64/build.json`，可用 `native/scripts/build-leveldb-snappy.ps1`
在现有 C++ 工具链重建。常规应用构建不编译 C++；不安装新工具、不需要 Node/PouchDB 运行时。
构建禁用 AVX/AVX2/BMI2/SSSE3 等可选指令，静态 CRT，不另加载 snappy.dll。
保留上游 C4722 默认 WindowsEnv 单例析构警告的构建记录，没有把日志样本通过当成完整 SST 兼容。

## 验证口径

`native/fixtures/migration/interchange/` 保存不含私人资料的真实引擎合成样本与摘要。
PouchDB 和 Playnite SDK/Backup 产生的来源独立于 The Order 写入器；另用实际 SQLite/DuckDB
产生一致性快照/CSV。自产 ZIP 回环、外部引擎读取、原生应用恢复和真实用户库不是同一层证据。

独立验证包括：PouchDB 9 读取导出数据库；Playnite 10.62 实际 Backup.RestoreBackup 与 SDK
Game 模型；ReinaManager 0.32.0 隔离便携应用显示两部作品、242 分钟；LunaBox 的实际
DuckDB 导入及官方运行中 CLI 列出两部作品。没有启动游戏、私人登录或导入真实用户资料。
PotatoVN 沿用公开原始 JSON 夹具及当前导出模型检查，没有声称通过 Store 安装实测。

全量测试、最终构建摘要和 WinUI 短交互截图位于 `.artifacts/manager-interchange/`；
实际执行结果以最终回执为准。干净 Windows、真实用户迁移和不同历史发行版不在已验证范围。
本轮不自动发布或替换当前安装。

## 最终验收结果与试用

完整客户端回归 **4693 passed / 0 failed / 0 skipped**，包含本地合成 Worker，不接触
真实云端账户。`final-tests-6/` 保存完整 TRX。前轮一项原有进程交接测试超时，单独复测
及完整重跑通过；原日志保留，没有排除用例、改断言或把重试冒充第一次通过。
Vnite 的 Snappy SST 兼容及导出排除诊断 LOG/LOCK 文件均进入最终回归。

| 验证 | 实际结果与边界 |
| --- | --- |
| 固定真实来源格式 | 五管理器、六份夹具（含两种 Vnite 存储形态）由最终自包含 WinUI 辅助进程只读解析；来源哈希不变 |
| 新增四来源实际落盘 | CLI 对五份来源夹具执行预览、绑定计划提交、重读和重复提交；每份保留一条 VN，重复时库字节不变 |
| Playnite 混合库 | 3 条保留 1 条、排除 2 条，保留项 12,345 秒；排除普通游戏和只有相同标题的条目 |
| Vnite 混合库 | 2 条保留 1 条、排除 1 条；7,200 秒中含真实会话 1,800 秒、按日累计 600 秒，其余无日期 |
| 五目标兼容 ZIP | 冻结合成库两条均能导出；除 LunaBox 的无日期总量限制外，累计 14,520 秒回环一致；补充文件不用于伪造目标原生事实 |
| 独立目标读取 | 最终 Vnite ZIP 由 PouchDB 9 读取；最终 Playnite ZIP 经其真实 Backup.RestoreBackup 和 SDK Game 读取；SQLite/DuckDB 独立读取最终数据库/CSV |
| WinUI 相加 | 7,320 秒 + 7,800 秒 = 15,120 秒；原 3,600 秒按日记录、0 次启动和 0 段会话不变；导入前备份经核对，重读后无可重复提交操作 |
| WinUI 导出 | 原生备份入口保留，五目标选择可用；实际保存 Playnite ZIP，并由其官方恢复代码验证 2 条 / 22,320 秒；最终包实际保存 Vnite ZIP，再读 2 条 / 22,320 秒 |
| 无 SDK 候选 | Release x64 自包含构建及实际启动通过，显式 Test Profile；最终辅助进程逐来源读取约 0.32–0.38 秒，未使用系统 SDK |
| 规模 | 各目标 1,000 条合成记录的导出、解析、转换、Codec 重读及重导去重通过；整项测试 1.1–4.9 秒，是本机功能量测，不是通用性能承诺 |

来源解析、转换、隔离导入及 The Order 实际界面 **PASS**；真实用户迁移、全部目标程序
GUI 恢复和干净 Windows **尚未验证**。结论为可进入隔离人工试用，不是公开发布验收。

最终候选目录 `.artifacts/manager-interchange/candidate-delivery/`；完整 ZIP、SHA-256、
源码文件清单和机器回执位于同级 `The-Order-manager-interchange-x64-Test.zip`、
`SHA256SUMS.txt`、`source-manifest.json`、`delivery.json`。源码留在当前分支未提交工作树，
不能把基线提交 `854ff3f` 单独当作本增量源码；源码清单记录所有有效源码文件摘要。

从候选目录运行 `launch-test.ps1 -DataDirectory <绝对的专用 Test 目录>`，或解压完整 ZIP 后
运行 `Start-Test.cmd`。该入口使用 Test 资料，不接管日用书架。请在此隔离实例按
[操作步骤](migration-trial.md)选择来源 ZIP，核对排除项/冲突后提交；导出在「设置 →
备份与恢复 → 导出兼容 ZIP」。不要单独双击内部程序 EXE 或复制 Profile 文件切换版本。

短交互证据：[默认相加](../.artifacts/manager-interchange/screenshots/sum-default.png)、
[三种选择](../.artifacts/manager-interchange/screenshots/sum-three-choices.png)、
[导入及导出成功](../.artifacts/manager-interchange/screenshots/import-export-completed.png)、
[最终 Vnite 导出](../.artifacts/manager-interchange/screenshots/final-vnite-export-completed.png)、
[ReinaManager 原生恢复](../.artifacts/manager-interchange/screenshots/reina-native-restored.png)。
