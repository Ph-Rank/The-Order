# 开发与维护

## 1.2.2 beta 2 候选（2026-10-20）

最新 Native 维护源为 `D:/Projects/The-Order-worktrees/beta-1.2.2-20261020`，分支 `codex/settings-beta2-20261020`。在上一轮完整 dirty 基线上继续，已保存继承文件快照；长期根及其他工作树未重置。本轮获授权制作并发布 beta 2，同时替换本机日用程序；旧的“未安装/未发布”说明按对应历史阶段理解。

设置页采用居中限宽、统一卡片留白与图标底板；版本号并入页标题，小窗口移至下一行。保留六组折叠、全部现有控件、来源入口、备份迁移及状态持久化。包含上一轮详情图片降级、失败重试与文字排版修复，不改资料格式与身份规则。

候选 Release 构建 0 错误/警告，真实隔离 WinUI 的浅/深、1160/560、六组和凭据编辑检查通过；50 份实际设置布局回执无标题越界。图片模块重验 232 组字段布局和 24 个实际详情状态。Computer Use 已核对自有 Test 窗口；默认并行稳定性限制仍按上一轮记录，不以串行通过冒充已修复。

当前此源码提交时完整回归/最终安装与公开发布验收尚在进行，不能仅凭本段视为已发布。最终包、源码清单、测试及安装/发布回执使用本工作树 `.artifacts/release-1.2.2-beta.2/`；发布结果完成后更新本文件。全程不以真实 Production、凭据或游戏目录作测试输入，不启动真实游戏。

## 详情图片兼容与排版修复（2026-10-20，本地开发）

最新 Native 源仍为 `D:/Projects/The-Order-worktrees/beta-1.2.2-20261020`，分支 `codex/settings-groups-20261020`，基线 `a0d537c` 加继承设置页修改和本轮未提交修复。版本未升级，**本次未替换日用程序，未提交、推送或发布**；下节“设置页已替换”是上一轮安装状态，不能代表此修复已安装。

修复了关闭透明效果/材质降级时静态图片被隐藏、图片失败仍保留巨大横幅空白，以及相同地址不能直接重试的问题；缺少横幅时复用作品封面，失败显示轻量重试。发行标签和日期合为单段文字，并统一流式布局取整容差。保留人工关联、个人资料与高对比度策略；未改资料格式。

Release 构建 0 错误/警告；真实 WinUI 合成资料完成 232 组字段布局与浅/深、1160/560 窗口共 24 个详情状态检查，另验迟到结果与高分辨率失败保留。完整串行回归 4766/0/0。两次默认并行各有一项既有集成用例失败，单独复测均通过；原断言和超时未改，不能将串行通过解释为并行问题已修复。未取得反馈者机器的实际目录、图片响应或系统设置，自动识别未命中原因与全部 DPI/Win10 实机仍未验证。

具体证据、失败日志、修复说明和完整脏源码清单在最新源 `.artifacts/details-artwork-fix-20261020/`；详情见该目录 `report.md` 和 `completion.json`。未读取真实 Production、凭据或真实游戏目录，未启动真实游戏。后续开发从该最新源继续，勿丢失尚未提交的设置页成果。



## 设置分类折叠与操作文字精简（2026-10-20，已本机替换）

最新开发入口为 `D:/Projects/The-Order-worktrees/beta-1.2.2-20261020`，分支 `codex/settings-groups-20261020`，基线 `a0d537c` 加本轮未提交界面修改；此前统计、迁移、时长日期和媒体入口均保留。长期根与原 manager-interchange 工作树的继承修改未重置。版本沿用 `1.2.2-beta.1`，本轮只替换本机日用程序，没有 Git 提交、推送、Tag 或 GitHub 发布。

设置页默认收起为外观与语言、游戏启动、资料与账号、书架与识别、备份与迁移、程序更新六组；个人同步另作内层折叠，书架的资料设置入口会展开对应组。保留原控件、绑定、处理函数与资料格式。全 Native 界面 29 类操作按钮/菜单去掉尾部省略号，保留状态信息和更多操作图标。

完整回归 **4766/0/0**，入口脚本 **29/0**；最终 Setup 独立身份安装、实际 WinUI 的 1160/560 宽度、浅/深主题、六组展开/收起、跨页保持、已有凭据编辑、Bangumi 编辑及合成账号同步界面检查通过。Computer Use 只读核对了自有隔离窗口的真实深色排版；RenderTargetBitmap 未正确捕获部分系统控件，其图片不作为完整视觉截图。物理键鼠、全部 DPI 与 Windows 10 实机未重新验收。

已安装的 965 份载荷、3 份入口附件、更新基包校验通过；6 份非受管/书架路由文件只比较元数据并保持不变。未打开普通 Production 窗口、真实用户资料、私人 ZIP 或凭据，未启动真实游戏。Setup SHA-256 `c47b972c2ef20011d49249bfbe8e1fc4441fa6cb8b29fea2a55b242021e35094`，源码清单 SHA-256 `c7f28347e8b6c0b8ec1db79e0f00642845f16150ee44b43fd1b64d1b7ec88425`；源码 dirty 状态已纳入清单，不能只用 HEAD 代表安装包。

所有验证、最终 Setup、对应源码 ZIP 与回执保留在本工作树 `.artifacts/settings-groups-20261020/`。成功后移除 D: 本轮临时程序回退、重复载荷和 bin/obj 共 4,258,140,839 字节；SDK、锁文件、共享依赖和维护源码保留，后续按既有锁定 restore 步骤重建。没有新增 C: 历史回退。

初始 UI 尝试修正了夹具的诊断开关、已保存凭据的父级可见性判断及展开后重新枚举；完整回归初次启动修正了移动夹具后的 Node 依赖解析；入口测试初次误用 Windows PowerShell 5.1 作为外层驱动，改由 PowerShell 7 驱动原有 5.1 子进程。原始失败日志保留，未降低产品断言或跳过测试；最终以 ui-installed.json、full-tests 与 launcher-validation-final 为准。下文按历史阶段理解。



## 1.2.1 正式版与 1.2.2 beta 1 已发布（2026-10-20）

- 正式版 [1.2.1](https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.1) 已设为 GitHub Latest；Tag `v1.2.1`，构建源码 `b9cf6cc097bbf81c5a639dfd2a1d29c61f0b7f23`。范围为此前公开 Beta 的媒体功能及 FAT/exFAT、显式数据盘根目录兼容修复，未包含统计和管理器迁移。
- 测试版 [1.2.2 beta 1](https://github.com/Ph-Rank/The-Order/releases/tag/v1.2.2-beta.1) 为 Pre-release；Tag `v1.2.2-beta.1`，构建源码 `a66b74421cff9fa9735cebf4473720d3893186bc`。保留最新日用源码的统计、六种进度、五管理器 ZIP 互通、时长相加及可靠日期合并。GitHub 显示名称使用 beta 1，程序与更新协议仍使用标准预发布版本号。

完整客户端回归分别为 4480/0/0 与 4766/0/0，入口检查分别为 27/0 与 29/0。两份最终分发包均完成独立身份安装、实际空 Test WinUI 启动/退出、真实 exFAT 合成目录扫描/事务和版本检查；Beta 另通过已安装工具五目标 ZIP 原生 reader 回读及日期幂等验证。15 个公开附件均匿名流式回下载并核对 SHA-256。两版实际编译的更新器均读取到 stable=1.2.1、beta=1.2.2-beta.1，正式通道排除 Beta。服务 Node 36/0、SQLite 合约 7/0；本轮未部署或修改生产服务。

新 Beta 的 NativeExtras v7 不可由 1.2.1 改写，旧版读取为只读保护。回退须先在 Beta 恢复与旧版兼容的早期备份，先另存当前资料；没有兼容备份时保留 Beta，不能强制转换。新旧来源的真实用户迁移、Windows 10 干净实机、全部 DPI、长期稳定性和真实 OAuth 到期恢复仍未全面验收，见各 Release 的 VALIDATION.md。

本机日用安装保持此前 1.2.1-beta.1，未替换、启动普通实例或访问 Production、私人 ZIP、凭据及真实游戏。最终包、源码清单、测试、匿名下载和实际更新通道证据在两个发布工作树的 `.artifacts/release-1.2.1/` 与 `.artifacts/release-1.2.2-beta.1/`；统筹回执在原开发工作树 `.artifacts/dual-release-20261020/`。下方旧“未发布”等文字属于对应历史阶段。


## 迁移日期合并（2026-10-20，已本机替换）

已按用户后续授权完成本机程序替换，沿用版本 `1.2.1-beta.1`；未改真实书架、凭据或 Profile 路由。最终证据在 `.artifacts/migration-dated-daily-20261020/`：965 份受管载荷、3 份入口附件、更新基包及 5 份未知/路由文件元数据核对通过；29 项入口检查、标准 Setup 隔离安装、实际安装工具五管理器互通与日期合并合成冒烟通过。新 Codec 对 v6/v7 均可写，已安装核心五程序集与开发验收字节一致。没有普通 WinUI、Computer Use、真实游戏或真实用户迁移，未提交、推送或发布。

本轮安装包 SHA-256 `44bcc14b732944e37091d16d2e0f4a52bcc84438b44e9ea7c72e917d2635a92b`，源码清单 SHA-256 `acfc723fe701979ada68bb0cc9c71f852aa773501c47425693c618e042a7df82`。D: 临时程序回退、重复载荷及独立测试安装在成功后清理，释放 3,318,998,561 字节；最终 Setup 与所有回执保留。以下为开发阶段说明，真实旧相加记录须由用户重选原 ZIP 并确认日期补全，不由代理自动处理。

当前工作区仍为 `D:\Projects\The-Order-worktrees\manager-interchange-20261020`，基线 `854ff3f` 加既有未提交内容。本增量只修改迁移时长/日期、预览提示与对应验证；后续获授权的日用替换结果见上方。未授权真实资料读取、Git 提交、推送或发布；沿用隔离合成 Test Profile。

`MigrationHistoryDates` 仅处理已验证的统一模型，复用原事务与统计聚合。相加保留来源日期/会话，未知余量不猜日期；源每日增长及可证实的会话延长只补差额。来源高水位和日期收据使用 NativeExtras v7，旧 v1–6 兼容，原 v6 客户端应只读 v7。ImportPlan 策略版本增至 5，使旧预览不能冒充新语义的同意。旧相加记录只有在原核验摘要相符时，才在重新导入预览中提供不增总量的日期补全。

测试覆盖真实公开格式夹具与合成历史，证据 `.artifacts/migration-dated-history-20261020/`。使用既有 .NET SDK 和本地合成 Worker 运行完整客户端回归；不以 Mock 或单纯构建代替真实用户库验证，不为此次纯数据/提示变更启动 WinUI 或真实游戏。旧程序只读兼容探针只加载程序程序集和合成 JSON，不打开 Profile。

完整回归 **4764/0/0**，日志/TRX 为 `full-tests-final/` 与同名日志。随后仅补旧 en-US 日历编号兼容和两条语言测试；最新源码定向 **287/0/0**（36 项新增）、WinUI x64 构建 **0 警告/0 错误**，分别见 `focused-final/`、`winui-build-latest.log`。`compatibility-latest/` 保存独立旧 Codec 只读检查和合成预览：总量 8,100 秒，有日期 6,000、未知 2,100、次数保持 2，重导幂等。真实 Native Test 导入前备份/重启、旧 v6 日期补全、撤销保护、真实格式及 10,000 天/10,000 会话规模测试均通过。

早期完整回归及进程定向复核各有一项旧时序超时，原日志保留；另有一次复核提前启动与运行中的测试争用构建输出，未运行成有效测试。后续均串行运行；没有跳过用例、放宽时限或修改进程代码。最终完整重跑通过后，仅用相关回归覆盖追加语言补丁。源码未提交，不访问真实资料；开发阶段 WinUI 仅构建，后续隔离安装与程序替换不等于鼠标/键盘实际验收。

## exFAT / 数据盘根目录（2026-10-20）

本轮已按后续授权完成本机替换：版本 `1.2.1-beta.1`，965 份载荷、3 份附件、更新基包与入口校验通过，5 份未知/路由文件元数据未变。完整 **4730/0/0**、启动器 29/0、真实 exFAT 的隔离扫描/事务落盘/回读/重导、标准 Setup 隔离安装通过。已安装工具的合成迁移闭环与五目标兼容 ZIP 导出/实际原生 reader 回读通过；未运行普通 WinUI、真实游戏或打开 Production。源码仍未提交，冻结清单 `source-freeze-final.json` 与 Setup 哈希、安装和清理结果都在 `.artifacts/filesystem-compatibility-20261020/`；保留最终 Setup/源码证据，成功后移除本轮冗余载荷及 D: 临时程序回退，不在 C: 留新回退目录。

最新实现仍在 `D:\Projects\The-Order-worktrees\manager-interchange-20261020`，继承本分支所有管理器互通变更。用户追加授权将这两轮更新一起替换日用程序；不授权访问真实资料或公开发布。版本保持 `1.2.1-beta.1`，以完整未提交源码清单绑定构建，不能只用 HEAD 代表候选。

`WindowsFilesystemIdentity` 优先保留原 128 位文件身份；FAT 家族仅对信息类不支持错误启用句柄证明的兼容路径，检查卷能力、原生属性及 GUID，使用与 NTFS/ReFS 分隔的卷/索引/创建时间标识。禁止用路径哈希代替身份，也不把权限或重解析失败当作普通文件。改名、卷替换、缓存的候选过期仍须重新检查。Profile 持久化安全规则不改变。

`ScanDirectoryPolicy` 统一盘根名称、排除目录及显式手动检查边界。数据盘根可选，系统盘根和网络共享根仍禁；既有扫描预算、取消、原子导入、目录租约和重复/父子目录检测不变。根目录有启动文件时视作一个安装，不同时列出其中子安装；普通游戏库文件夹仍支持自动发现。导入提示及三套界面翻译同步。

测试只允许本次新建的合成目录；真实 E: exFAT 的卷根只读元数据，不可为了验证盘根而枚举已有整盘内容。测试文件系统模拟盘根枚举；实际 exFAT 合成子目录验证扫描、文件选择、重复与过期快照。证据根 `.artifacts/filesystem-compatibility-20261020/`，安装前完成全回归、标准 Setup 隔离验证及受管程序清单核对；日用进程未退出时不得覆盖或强杀。不得读取/复制 Profile 路由、真实书架或凭据；本次临时程序回退放在 D: 验证目录，成功核验后按用户清理偏好移除，不在 C: 累积历史备份。

已修复 Beta 打包对稳定版块映射工具执行无关 restore 的问题。固定锁文件在各引用项目的 RID 目标不完全相同；如需重建资产，使用各项目对应 RID 的 `restore --locked-mode --no-dependencies`，不要 force-evaluate 或重写其他任务的锁文件。首轮辅助验证误把提交结果期望为 Success（实际 Committed）、PowerShell 将 null 字符串转换为空字符串、日期 JSON 去掉末尾小数零以及隔离安装核验引用了另一轮 launcher 字节的问题均为夹具问题；原日志保留，产品断言未放宽。最终回归与已安装字节绑定通过。挂载点标题仅取文件夹名，不将其本地父路径传作作品名称。

## ZIP 互通增量（2026-10-20）

最新开发在 `D:\Projects\The-Order-worktrees\manager-interchange-20261020` / `codex/manager-interchange-20261020`，基线 `854ff3f`，不覆盖其他工作区。支持固定版本五管理器 ZIP 的读取/预览及兼容导出；原生备份和事务复用。设置页先核验，再自动备份并提交；默认相加只是核验框中的首选，不会跳过用户确认。

格式、字段损失和原生引擎验证见 `docs/manager-interchange.md`，用户步骤见 `docs/migration-trial.md`。NativeExtras v6 保护新谱系/时长语义；旧库兼容，回退程序前用新版恢复导入前备份。WinUI 的 `--manager-backup-reader` 是仅用于本机管道的短进程：在任何 Profile/WinUI 初始化前分流，90 秒/768 MiB 限制，不作为普通用户入口。

测试运行需要既有固定 .NET 10 SDK。此工作区 `.artifacts/native-phase1/tools/dotnet` 仅为指向已有 D 盘 SDK 的 junction，不复制 SDK，不删除其目标。完整客户端测试通过 `services/bangumi-auth-worker/test/run-client-bridge.mjs --all-client-tests` 启动本地合成 Worker；需要本仓库锁定的 Electron 运行时供旧安全存储回归，不能把缺少依赖记作 PASS。无真实账号/Cloudflare 部署。迁移夹具均为公开结构和合成记录，CLI 目标仍限 Test。

正式候选 publish 仍要求 `-p:NativeProductionCandidate=true`；这不授权代理打开 Production。测试明确设置 `THE_ORDER_NATIVE_PROFILE_KIND=Test` 与专用 D 盘目录，使用隔离启动器。此次不发布、不推送、不替换日用安装。

最终完整客户端 **4693/0/0**，结果 `.artifacts/manager-interchange/final-tests-6/`。前轮一次进程跟踪用例超时，保留原日志，单独复测及完整重跑通过；未放宽断言。Vnite 引擎使用固定 LevelDB 1.23 / Snappy 1.2.2，普通构建只复制其锁定 DLL，重建方法及来源摘要见 `native/scripts/build-leveldb-snappy.ps1`。候选 `candidate-delivery/`，显式启动 `launch-test.ps1 -DataDirectory <专用空目录>`；本次源码未提交，确切源码及载荷摘要以同目录最终回执为准。

## PotatoVN 逐条核验（2026-10-19，当前开发）

后续授权的本机替换已完成，应用源码 `5543bbc` / 版本 `1.2.1-beta.1`，不是一次公开发行。沿用已完成的 4651/0/0 源码回归；本次另验证标准载荷和独立安装，18 项实际候选程序集核验/备份/重启检查、29 项启动器检查、8 步无 SDK 工具及 7 步进度闭环。最终以 `.artifacts/migration-review-daily-20261019/installation-receipt.json`、本机工具检查和 `completion.json` 为准；929 份载荷、3 份附件与更新基包核对，937 份旧程序备份保留，5 份未知文件不改动。程序回退与书架恢复分开：核验导入写 v5 后，如需退旧版，先在新版恢复导入前备份。没有运行正常 WinUI、访问真实 Profile、推送或发布；下方开发阶段状态为历史。

从 `9506108` 建立 `codex/migration-review-20261019`，不覆盖原统计工作树。设置页预览可逐条核验身份/版本和选择累计时长；Steam 已有完全相同 App ID 时，额外引用不单独触发冲突，真正版本/编号差异仍需确认。不改目标作品资料、安装或非空笔记/评分；时长不相加，已有会话/人工修正/次数保留。复用原备份与串行事务，新预览替代旧同意，来源损坏、重复及活动会话不能用核验绕过。

`MigrationReviewTests` 及原迁移、进度、备份、统计回归覆盖此增量。使用固定 SDK、全新 `.artifacts/migration-review-20261019/` 和已有本地合成 Worker 完整测试入口；不得将无界面测试称为 WinUI 实际点击通过。人工确认仅存入已有 MigrationOrigin，并按需使用 NativeExtras v5；须用本机旧 v4 Codec 对合成 v5 做独立只读兼容检查。新旧程序回退与书架恢复分开，用户步骤在 `docs/migration-trial.md`。

开发诊断阶段用户允许只读核对指定 ZIP 与当前真实书架，禁止代理写入真实资料或读取凭据；诊断不打开 NativeProfileStore，不获取 writer，不探测游戏路径，只用只读文件句柄和纯 Codec/Resolver。已验证自动匹配两条、其余五条都有核验候选，源/目标哈希不变。开发阶段没有替换日用安装，后续授权替换见上段；不推送或发布。下方原日用进度版本信息保留为历史基线。

最终完整 4651/0/0（含新增 39 项），WinUI Release x64 发布目录构建和隔离 Native Test 的真实落盘/备份/重启通过。候选 v5 → 日用旧 v4 Codec 的只读保护独立验证通过。首轮完整 4650/1/0 的遗留未来版本哨兵已改为 v6，原断言不变，失败 TRX 保留。最终无界面验证不代表新弹窗已接受实际鼠标或键盘验收；没有制作或安装新公开版本。

## 六种进度与本机替换（2026-10-19）

`codex/progress-status-20261019` 基于 `99d1a34`，继承设置页 ZIP、统计和媒体成果。应用源码冻结为 `c8c9537`，沿用版本 `1.2.1-beta.1`；最终文档提交不重新标注载荷源码。最终完整本地合成 Worker 客户端回归 4612/0/0，修正后定向 197 项、启动器 29/0、Release x64 及标准 Setup 的隔离安装通过。日用目录已替换并核验 929 份载荷、3 份附件、更新基包和入口 `--validate-only`；已安装工具的 8 步迁移与 7 步 wish→completed→较低 playing 合成闭环通过，重复导入总量不增加。旧程序 937 份已备份，5 份未知文件未动。

集中定义 GameProgress，保留既有 planned 的磁盘值用于未设置；wish 和新进度更新来源由 NativeExtras v4 保护，兼容主字段仍使用旧枚举，旧 NativeCodec 进入只读。不能直接往旧 JSON status 写未知 wish 或批量改真实资料。测试包括六乘六进度组合、旧迁移重放、其他字段冲突、真实 Native Test 持久化重启及云端 unset/wish 区分；旧日用与最终载荷程序集的无 Profile 兼容探针已通过。首轮完整 4610/2/0 的边界修复与冻结 oracle 单字段新契约投影、测试分析器及辅助脚本错误日志均保留，没有跳过用例或放宽时限。仍没有桌面交互、真实用户导出或 Production 验收。

本轮原始失败、夹具修订、源码冻结、候选与程序回退凭据保留在 `.artifacts/progress-status-20261019/`。用户只授权替换程序；不读取真实书架、凭据或其他管理器私人数据。日用实例未退出时不可覆盖或强杀，不能把上一轮备份当当前程序备份，须核对当前已安装载荷并保存新回退点。步骤及数据格式回退说明继续维护 `docs/migration-trial.md`。

## 设置页 ZIP 迁移入口（2026-10-19）

实际已用源码 `3461478` 构建的标准 Setup 完成本机替换：完整回归 4564/0/0、117 项定向、29 项启动检查、隔离 Setup 和安装目录 8 步 CLI 闭环通过。安装的 929 份载荷、3 份入口附件及更新基包均核对，回退程序 662 份已备份，5 份未知/路由文件未改动。没有启动正常 WinUI 或访问真实资料。`.artifacts/migration-settings-20261019/installation-receipt.json`、`completion.json` 为本轮事实；下方准备及 CLI-only 描述按历史节点理解。最终文档提交不是另一份程序构建。

当前 `codex/migration-settings-20261019` 基于保留统计及 CLI 的 `c8d6ffd`。用户已授权在设置页选择 PotatoVN 导出 ZIP、预览后导入当前打开的书架，并替换本机程序；不是授权代理读取或迁移真实用户库。本轮仍不使用 Computer Use、不操作桌面、不启动真实游戏、不推送或发布。测试仅隔离 Test Profile，应用内 service 复用既有 writer/备份/事务；没有提供 CLI Production 开关或新用户数据库。

相关回归为 `LibraryMigrationSettingsTests`、原 `MigrationTests`、备份及统计测试，完整测试继续用现有本地合成 Worker 入口。构建 Release x64，安装前冻结源码与载荷并使用全新 artifact；包先做隔离安装与 headless `--validate-only`，不能裸启动默认 Profile。实际日用替换仅在确切进程退出、旧程序哈希备份及新包校验均通过后执行；不读 Profile 或路由文件内容。全部新回执在 `.artifacts/migration-settings-20261019/`，用户步骤在 `docs/migration-trial.md`。旧版本回退前需在新版本恢复「导入前」备份，避免旧 codec 只读含迁移来源的书架。

## 本机迁移工具随附候选（2026-10-19）

`codex/local-migration-daily-20261019` 保留已验收统计快照并继承 `7821269` 迁移内核，不修改原统计工作树、Production 或公开附件。沿用固定 SDK/锁文件，分别自包含 publish WinUI 和 MigrationTool；不要用迁移分支的旧 UI 覆盖现有统计版，也不要把测试启动或 GUI 自动化附加到本轮替换。

`native/scripts/package-library-migration-tool.ps1 -PublishedTool <独立工具publish> -PublishedApplication <应用publish>` 在冻结文件清单前附加工具和 `docs/migration-trial.md`，检查版本、自包含运行时、源/目标边界及开发/敏感文件。再用原 release/installer 脚本打包，工具载荷进入同一清单与标准安装/卸载范围。安装器同时拒绝选定目录内活动的 GUI 或迁移工具，不结束进程。`TheOrder.exe --validate-only` 仅校验入口、不会启动应用；本轮仅运行该校验、命令行合成导入及无界面测试，真实界面未重验。

替换前应核验旧载荷与 `update-base.bin`、只备份受管理程序文件，未知文件仅记录大小/时间并保持；不得读取 Profile、路由文件内容或凭据。若确切日用进程仍在运行，先完成可独立的候选验证与备份，再等待用户正常退出，不能强杀或绕过安装器保护。实际完成状态见 `.artifacts/local-migration-daily-20261019/`，手动试导入步骤见 `docs/migration-trial.md`；试导入不等于正式书架入库。

## 跨管理器迁移内核 v1：仅无界面 Test（2026-10-19）

独立分支 `codex/library-migration-20261019` 基于 `0227ea3`，代码检查点 `5c13f59`；不要在原统计工作区重放、覆盖未提交文件或据此替换日用程序。没有版本升级或 Production 迁移。本分支新增 `native/TheOrder.MigrationTool`，复用现有 Native Profile、事务和时长台账，不启动 WinUI 或其他管理器。

固定 .NET 10.0.401 SDK，从分支根目录执行 `dotnet restore native/TheOrder.MigrationTool/TheOrder.MigrationTool.csproj --locked-mode`、`dotnet run --project native/TheOrder.MigrationTool -c Release --no-restore -- help`。`preview --source <明确导出文件>` 为默认只读流程；`init-test --target <全新目录>` 创建专用合成 Test Profile；带目标重新预览后，只有 `apply --source ... --target ... --commit <planId>` 才提交。来源时区明确时可加 `--source-time-zone Asia/Shanghai`，预览/提交参数必须一致；报告目录必须全新。`demo --out <全新目录>` 自行生成已知格式合成资料，完成预览、提交、重启读取和重复导入。完整命令见 `docs/migration-v1.md`，没有 Production 或强制绕过入口。

完整无界面客户端回归沿用 `services/bangumi-auth-worker/test/run-client-bridge.mjs --all-client-tests`，仅本地合成上游；设置 `THE_ORDER_TEST_DOTNET` 为固定 SDK exe、`THE_ORDER_TEST_RESULTS_DIR` 为本轮新目录。既有 ProfileProbe 回归还要求根目录 `.artifacts/native-phase1/tools/dotnet/dotnet.exe` 可用，本轮只在新工作树的 ignored 目录建立指向已有 SDK 的路径别名；加密合成测试所需 Electron 用根锁文件 `npm ci`，Worker 用其目录锁文件 `npm ci`，没有升级依赖或运行旧 Electron 主程序。不要清除跳过条件来声称服务闭环通过。

本轮完整客户端 4513/0/0，其中迁移 70 项；独立 CLI 检查 28 项。公开合成夹具 5 条和一万条合成库均实际写入原生 Test，重复导入没有时长增量或库字节变化；完整证据位于 `.artifacts/migration-v1/`。首次缺依赖的失败和跳过日志完整保留。新谱系为 `nativeExtras v3`，只在已提交迁移条目上出现；旧格式条目不升级。旧客户端对未知扩展的既有保护是只读，本轮未执行旧程序读写测试，不得宣称真实用户升级/回退已验收。下一阶段需真实脱敏导出、统计 UI 对导入按日累计的说明及明确的用户资料准入；本轮结束后不自动开发向导。

## 1.2.1 Beta 1 发布候选（2026-10-19）

维护分支 codex/public-beta-1.2.1-20261019。已将接手时未提交、但已在日用版验收的音乐/书籍增量单独提交为ccd15ee；未混入reports或research_notes。版本按现有更新器协议采用1.2.1-beta.1（数字1.2.1.1），GitHub必须Pre-release，不修改稳定latest及旧附件。

修复IGDB有合法YouTube关系但名称缺失时被丢弃：保留原名称null，中性显示“宣传影像”，不声称官方PV；105342真实公网返回68884/lVNrSOc0wG0且originalName为null，修复前同ID空列表。服务继续使用原Worker、原SQLite namespace和single-app-quota-v1；缓存命名升级，不创建第二套配额或修改Secrets。客户端保留旧正缓存离线降级，旧空缓存重新读取；主库、人工确认/排除/默认schema不变。

NextMoe已收录DLsite的RJ/VJ/BJ商品编号转换为普通来源蓝色链接，首页/搜索页不进入商品来源，不后台逐条探测商品。上游旧商品可能404；已以真实RJ297925核对《星空铁道与白的旅行》商品页。宣传影像放到标签上方，高级撤销/重置收进影像选项。播放器显式启用YouTube原生控件与中文界面，分辨率由YouTube自身决定；不使用失效的强制画质API。

本轮隔离证据目录.artifacts/release-1.2.1-beta-20261019/。最终完整客户端4443/0/0（含既有本地合成Worker闭环）、服务Node36/36与workerd/SQLite7/7通过。真实IGDB复核31个既有人工审定编号全部成功，29个有影像、5个原有歧义仍保留。实际WinUI已播放Sabbat影像，并打开原生画质列表及选择720p；不是物理设备验收。补充发现并以先失败/后通过测试修复CSP脚本哈希的HTML换行规范化问题，不放宽CSP。最终安装包、回退、日用切换及GitHub发布须以对应回执确认，不能视作已交付。所有测试仅合成Test Profile，不读取Production或游戏数据。后续以该目录最终report.md和completion.json确认实际完成范围。


## IGDB 公网服务维护（2026-10-19）

独立the-order-igdb-game-videos已获用户批准并部署在现有Workers Free账户；实际根地址https://the-order-igdb-game-videos.zyx2251721565.workers.dev/已核对，Secrets仅在服务端。部署载荷冻结29e8ef4，SQLite迁移v1、固定single-app-quota-v1及限流/持久预算保持。wrangler.production.jsonc仍默认IGDB_ENABLED=false；正常重部署会关闭服务，启用必须显式覆盖。不要删除命名空间或同时用同一Twitch凭据另起本地/staging协调器，不操作Bangumi Worker或升级套餐。

使用已审查.artifacts/igdb-production-ready-20261018/predeployment-review.json及worker-qualified-v3：operate-approved.mjs disable --user-approved --review=<该文件>可关闭；重新启用加enable --free-plan-confirmed --local-use-stopped。命令标记不是授权，须核对具体操作已获授权。首次bootstrap已完成，不应重复建立命名空间；Secrets不进入配置普通变量、客户端、日志或仓库。详细安全部署与回退见services/igdb-game-videos/README.md。

Production/Beta默认内置HTTPS；Test/Development无默认公网。实际隔离验收使用本轮Start-Test.ps1 -Run ui-<全新名> -Payload <portable-rc绝对路径> -ExpectedVersion 1.2.0 -HttpsService https://the-order-igdb-game-videos.zyx2251721565.workers.dev/；不能裸启动EXE。不要重启共享开发凭据的18787/8787服务。两部真实公网视频由用户点击开始、应用探针确认playing；其余原生peer流程、缓存故障、恢复及重启均有独立回执，不作为完整物理输入验证。新桌面截图受工具故障阻塞，应用内渲染截图已排除。

完整冻结回归4428/0/0、Node35/35、workerd SQLite7/7和Release x64不变；新增公网16项、停用恢复、确认/默认和5文件哈希检查通过。线上CPU样本1–6ms无异常；Free共享账户100000调用/日，DO每日行数/存储用量及长期/不同地区网络仍待运营观察。本轮无库schema迁移、Production访问、日用替换、版本提升、推送或Release；本机候选不等于已向公开用户分发。报告、最终线上版本及冻结哈希见.artifacts/igdb-production-ready-20261018/public-deployment-report.md。

用户随后明确授权替换本机正式版：2026-10-19已将同一已验收IGDB程序载荷切换至原C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe，版本仍1.2.0。源码检查点382b322、应用代码29e8ef4，保留全部继承音乐/书籍工作；没有重新编译应用。标准Setup隔离安装通过，实际日用654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留。原安装目录WinUI以全新空Test资料启动并退出0，辅助工具默认全关；未打开、迁移或复制Production及凭据，未启动真实游戏、推送或发布。旧日用662份程序及旧Setup已核验备份在C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261019-003803-igdb-public-pv，仅供程序回退。当前客户端可使用内置公网IGDB地址，收尾只读请求返回200；实际播放沿用此前相同程序字节的公网验收证据，不宣称本次私库/实机游戏已验收。切换凭据以.artifacts/local-igdb-daily-20261019/completion.json为准。

## IGDB真实API验收与版本核对（2026-10-18，当前增量）

codex/igdb-live-qualification-20261018从6665e5d和原dirty继续。真实凭据仅在services/igdb-game-videos/.dev.vars（Git ignored），不复制/回显/打包；客户端只有固定服务地址。Wrangler本地仅127.0.0.1:8787，默认公开配置仍禁用、无路由，无部署。game_type.type是当前实际字段；IGDB_DIAGNOSTICS默认false，开发诊断仅返回白名单阶段/状态，不输出原异常或认证内容。

最终Release x64载荷为.artifacts/igdb-live-20261018/publish-live-v4。仅经该目录Start-Test-V2.ps1 -Run ui-<全新名> -Payload <publish-live-v4绝对路径> -ExpectedVersion 1.2.0启动，固定自己的test-profile-ui-v2和Test标记，显式注入本地服务；不能裸启动默认Production。首修Start-Test.ps1使用另一个test-profile，旧证据单独保留。退出用Control.ps1 -Control <本轮ui目录> -Action exit并检查exitCode=0。不要使用先前build的截图替代最终构建验收。

固定SDK10.0.401及锁依赖。最终完整含既有Worker合成契约4409/0/0，服务Node17/17；定向与各中间全套均为重叠阶段。全套入口沿用既有run-client-bridge.mjs --all-client-tests，仅把启动器输出改为本轮新目录；未改Bangumi Worker。真实覆盖36部、人工复核31、视频28不等于自动匹配或播放率；首修实际播放5部，最终构建复验本地Steam版自动对应/播放与重制版。测试目录安装和NextMoe输入是合成的，不读取Production和真实游戏。

PC候选分组不写身份事实，用户可展开其他结果；英文/日文检索不生成译名。只把默认安装的Steam编号作为自动发行版证据，目录链接不等于当前安装；拒绝、撤销和并发revision保护保持。旧主库及人工确认无schema迁移，新增ParentGame可选；服务旧缓存缺字段重新获取。缓存移走/重启的用户决定哈希、失败与负对照、截图、源码及载荷清单均在.artifacts/igdb-live-20261018/。未推送、发布、提升版本或替换日用。


## IGDB 游戏宣传影像 v1（2026-10-18，最新增量）

当前codex/igdb-game-videos-20261018基于3ed4463及全部继承dirty，只做IGDB游戏对应/影像、Native Profile决定、共享动画播放器复用。无新版本、真实资料迁移、日用替换、推送或发布。完整设计、凭据及样本口径见docs/igdb-game-videos-v1.md；独立Worker未部署，默认IGDB_ENABLED=false、无公开路由，不改生产Bangumi OAuth Worker。

固定SDK10.0.401、锁依赖。Release x64/NativeProductionCandidate=true/UseSharedCompilation=true/PathMap/CopyOutputSymbolsToPublishDirectory=false的publish-v4为最终应用载荷。只用.artifacts/igdb-game-pv-20261018/Start-Test.ps1 -Run ui-<全新名> -Payload <本轮publish-v4绝对路径> -ExpectedVersion 1.2.0启动，固定自己的Test Profile，不能裸启动默认Production。-WithoutService验证开发配置缺失；默认读取本轮合成Miniflare地址。退出用该目录Control.ps1 -Action exit并检查exitCode=0。测试创建的cache可删除，不能删除用户身份目录来模拟撤销。

最终完整客户端4371/0/0含既有Worker合成契约；启动器输出重定向本轮新目录，未改既有Worker源代码。独立服务14项Node测试使用node --test services/igdb-game-videos/test/worker.test.mjs。真实IGDB尚无开发凭据，36样本及前10pilot保持未验证；coverage.mjs --all --output=<全新文件>无配置时零网络并输出BLOCKED报告，有地址也不自动证明真实上游或身份准确。测试脚本、日志、真实WinUI截图与源码/载荷哈希保留于.artifacts/igdb-game-pv-20261018；Mock、真实原站嵌入和物理输入不得混称。

## 书籍阅读与购买入口 v1（2026-10-18，最新增量）

维护 `codex/book-outlets-v1-20261018`，当前工作树 `update-channels-20261016/The-Order`；保留9147b06上全部未提交音乐增量。本轮仅书籍入口。`BookOutlets`做纯分类、保参去重和层级/来源保留；`BookReadingViewModel`只在展开时刷新缺字段和补充元数据，带取消、世代和旧资料保留。MangaDex仅已确认AniList漫画，原题单页10条，完整/唯一跨站编号、标题和已知MAL编号均核对；只使用实际完整网址，不扩展bw等标识短键，不写人工身份。

复用固定SDK10.0.401、locked依赖与现有客户端回归入口。本轮最终 `.artifacts/book-outlets-v1-20261018/tests-full-accepted/` 为 **4327/0/0**，包含本地合成Worker；`tests-books-qualified/`165/0/0是重叠子集，新书籍64项。`tests-before-mal/`保留同ID核对编号变化的实际失败反例；恢复保护后的源文件逐字节核验。两次早期全套既有launcher/chain超时、一次中间全套4325/0/0及全部原日志也保留，不放宽启动标准、不伪称环境根因已修复。

最终 `publish-accepted` 是Release x64/NativeProductionCandidate=true载荷；只经本轮 `Start-Test.ps1 -Run ui-<全新名> -Payload <本轮publish-accepted绝对路径> -ExpectedVersion 1.2.0` 启动，固定自己的 `test-profile`，不可裸启动EXE默认Production。`Control.ps1`/`Native-Step.ps1`仅已有Test受限peer，平台按钮只inspect，实际外部点击由定向桌面工具完成。final为`ui-accepted-books`，其前`ui-qualified-books`含实际平台点击；每阶段源码/载荷、控制回执与截图均独立保留。已退出自己的全部Test进程，未操作日用程序。

新缓存字段为可选附加，MangaDex补充使用现有AniList远程缓存的 `booklinks:md-v1:<format>:<id>`，24小时新鲜/14天最大离线及原子封装不变；旧版无法识别时忽略该可丢弃行，无主库或人工确认schema迁移。不能将Provider编号声明视为人工确认；同源编号相同但MAL发生冲突时，每次重建展示仍重新验证。实际断网、其他Windows/DPI及物理输入不属本轮已验收范围。

28项冻结样本与平台证据见 `coverage-final.json`、`md-coverage.json`、`*-platform-evidence.json` 和 `ui-and-test-gate.json`；16/25主入口、MangaDex新增3部电子书类别，不代表具体版本准确商品覆盖。NDL429/超时和MangaDex网页安全拦截另列，未绕过。新功能详见 `docs/book-reading-outlets-v1.md`。只提交书籍增量；音乐原dirty继续保留，实际构建须同时保留其工作树内容，不可仅凭新HEAD声称已包含它们。本轮无发布、安装切换或Production操作。



## 书籍入口排版与音乐来源按钮（2026-10-18，最新增量）

用户授权两项视觉调整及日用替换。在现有3ed4463与全部继承音乐dirty上，只改BookReadingView、MusicListeningView：书籍所有有效主入口共用一个按可用宽度换行的布局，用途、语言及卷册版本说明保留；不再按类别强制另起一行。音乐“资料与来源”改成与书籍相同的普通Button，沿用主题文字颜色，展开、收起和链接内容不变。

完整客户端含本地合成Worker回归4327 passed / 0 failed / 0 skipped；Release x64通过。真实隔离WinUI验证1180请求宽四个书籍入口同排、560宽自然换两行、浅/深主题及来源展开；音乐按钮原生角色Button、自动鼠标点击展开和受限peer收起通过。冻结公共元数据与明确合成的音乐版本/导航不作为新覆盖率、实际播放或物理输入证明。早期书籍展开在确认弹窗尚未关闭时被拒绝，确认后通过；无产品修复扩大范围，无新增低价值镜像单元测试。证据 .artifacts/outlet-layout-20261018/。

已用标准Setup替换原日用目录，仍显示1.2.0。654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留；原目录WinUI仅用全新空Test资料启动，辅助工具默认全关，正常退出0。662份旧程序及上一书籍版本Setup备份在 C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-182404-outlet-layout/，仅供程序回退。未访问或迁移Production、读取真实凭据、提交、推送或修改公开附件。当前包含新UI及继承音乐的工作树增量，不能单靠HEAD重建相同载荷；源码及载荷哈希、切换最终凭据见 .artifacts/local-outlet-layout-daily-20261018/completion.json 和 source-freeze.json。

随后用户授权本机日用替换：标准包及安装、备份、替换后空Test启动凭据统一在 .artifacts/local-book-outlets-daily-20261018/，completion.json 为最终依据。654份载荷、3份入口附件及更新基包核对，4份未知文件保留；Test正常退出0、默认辅助工具全关。旧音乐版662份程序和Setup已核对备份到 C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-172357-book-outlets/。只替换程序，不访问Production、不迁移用户资料、不推送或改动公开附件，版本仍1.2.0。

## 音乐数字版自动加载与固定排序（2026-10-18，最新）

当前`codex/music-auto-digital-20261018`基于9147b06及全部继承dirty成果。`MusicListeningViewModel.ExpandAsync`将当前入口和数字发行版纳入同一次请求/取消/时效管理：先展示当前资料，再自动加载有明确专辑组的Release数字版；Recording或缺少组编号不推测查询。成功展开去重，失败/取消保留当前结果并允许再展开重试，刷新更新两阶段。视图移除查找数字版按钮和排序控件，平台及资料站按显示名称排序；原始MusicBrainz身份、关系和当前发行版保持。

固定SDK10.0.401、锁定依赖，`.artifacts/music-auto-digital-20261018/`保存冻结源码清单、publish、package和真实窗口截图。完整4263/0/0含本地合成Worker契约，85项定向为重叠子集；新增11项请求次序、去重、取消、迟到、失败保留测试。首次定向Recording夹具误带Official发行版状态被既有校验拒绝，只修正夹具、不降标准，失败记录保留。实际WinUI一次展开/定向自动鼠标点击即显示2组数字版；Apple Music、Deezer、Spotify及Amazon、Discogs、VGMdb按显示名称排列，7个来源入口保留，无额外搜索或排序控件。明确合成的发行/关系不作真实覆盖或播放证据，不宣称物理输入通过。

`.artifacts/local-music-auto-daily-20261018/`标准Setup、候选启动和独立AppID/Mutex隔离安装已通过。用户正常退出后完成原日用目录替换，654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留；实际安装目录WinUI使用全新空Test资料启动、默认辅助工具全关、正常退出0。662份旧程序及上一排版构建Setup备份至`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-142540-music-auto/`，仅回退程序；没有打开或迁移Production、复制凭据、Git提交、推送或发布。版本仍1.2.0，最终安装状态以本轮completion.json为准。

## 音乐详情排版与本机替换（2026-10-18，上一节点）

当前`codex/music-layout-20261018`基于9147b06及继承的dirty音乐外链成果，不清理、提交或覆盖无关工作。仅调整音乐详情布局、来源折叠及提示投影；不更换Provider，不迁移书架或人工身份schema。新`NeedsAttention`区分普通成功/有界查询说明与错误、离线回退、取消和空结果；完整消息及来源保留，不以简洁为由删除真实错误。

固定SDK10.0.401和锁定依赖；最终publish-v2→package-final位于`.artifacts/music-layout-20261018/`。4252/0/0完整客户端含本地合成Worker，74项定向为重叠子集。WinUI真实窗口的浅/深主题、1180/560宽度、资料展开及排序通过；原生peer证据与窗口截图分开保存。P1枚举会包含已实现但祖先折叠的XAML子控件，按钮边界断言须限定主区收听/购买控件，不能把折叠资料区当作主区可见；首次误断言保留在verification-attempt-1.json。合成夹具项目有既有NU1603依赖下界提示，未改变产品锁文件。没有真实播放、物理设备、干净OS或全部DPI验收。

用户授权替换并回复托盘已退出后，在`.artifacts/local-music-layout-daily-20261018/`完成标准包、独立AppID/Mutex且无注册的隔离安装、备份和真实目录替换。654份程序载荷、3份入口附件及更新基包一致，4份未知/路由文件保留。原安装路径WinUI仅用新建Test资料启动、工具默认关闭、退出0；不打开真实Production或读取凭据。回退为`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-135533-music-layout/`中的662份旧程序及上次MB外链体验构建Setup，只回退程序。版本仍1.2.0，公开GitHub附件和Tag未改，最终状态看本轮completion.json。此前历史阶段的旧“当前”不能覆盖本节。

## MusicBrainz统一音乐外链来源（2026-10-18，当前增量）

在update-channels-20261016工作树的`codex/music-mb-links-20261018`基于9147b06继续开发。MusicBrainz为远程音乐外链的唯一来源；国内平台只保留已有url-rels，不进行独立API/搜索/授权/代理。生产界面及DI停用主动iTunes查询，历史个人Related页面保留。原始关系证据和URL去重不改变书架、人工映射或Native Profile schema；旧远程缓存缺少原始URL时不补造证据。

验证继续使用固定SDK10.0.401及锁定依赖。新证据根`.artifacts/music-mb-links-20261018/`，当前publish/package仅在其下新建的合成Test Profile启动；完整客户端经本地合成Worker闭环4247 passed / 0 failed / 0 skipped，84项定向为重叠子集。真实隔离窗口验证三种排序保留全部四个平台及历史个人页面，国内记录为合成，未打开平台或播放。MB streaming/free streaming标签采用“正版收听 · MusicBrainz 收录”，不扩大成独立版权认证。具体边界见`docs/music-listening-v1.md`。

源码未执行Git提交、推送或发布；保留继承的两份文档修改及reports/research_notes，不清理其他工作树。用户随后明确授权本机替换，已在新证据根`.artifacts/local-mb-links-daily-20261018/`冻结当前dirty源码并复用已验收publish字节，生成标准安装包，完成独立AppID/Mutex/路径的隔离安装和程序校验。旧日用662份受管程序文件备份至`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261018-132514-music-mb-links/Program`，同级Setup可回退至之前的8edaa72体验构建。实际日用654份载荷、3份入口附件及更新基包一致，4份未知/路由文件保留；原安装目录的WinUI以全新空Test资料启动和正常退出0，默认辅助工具全关。版本保持1.2.0，不启动Production或复制、迁移用户资料。新的`completion.json`和安装回执为本次切换依据，先前切换记录仅属历史。

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

## 2026-10-16 Beta 3 资料语言修复

当前维护 `codex/beta3-language-20261016`，基于 Beta 2 的 `4918d0c`，版本1.2.0-beta.3。资料源既有译文与可选本地翻译包分开处理；只变更显示投影和来源说明，不迁移Profile、不清空远程缓存。真实NextMoe响应、修复前反例、4102项完整回归、缓存往返、自建Test书架与最终包证据位于 `.artifacts/content-language-fix-20261016/`，最终安装／发布状态看该目录回执。

继续使用本工作区 `.artifacts/native-phase1/tools/dotnet/dotnet.exe`（10.0.401），锁定依赖。完整回归入口仍为 `services/bangumi-auth-worker/test/run-client-bridge.mjs --all-client-tests`，显式绑定SDK与新结果目录。Release x64 / NativeProductionCandidate=true 构建至全新候选目录；严禁裸启动默认Profile。当前匿名公开资料只用于自建Test记录，不能复制Production或真实凭据。

## 2026-10-16 更新通道开发边界

新正式版1.1.1/Beta1.2.0-beta.2共享安装入口。详见对应docs/release目录，不照搬历史Beta独立入口或禁止自动更新说明。
默认构建仍不访问Profile；实际启动必须显式Test目录。NativeChannelProfiles对Test在任何正式目录探测前返回；正式与Beta仅由正常用户启动及固定命名空间身份选择打开，不能通过任意路径迁移。
稳定桥接工作树codex/stable-channel-bridge-20261016保持原稳定功能，Beta工作树codex/update-channels-20261016保持ACGN成果。共享更新协议及codec测试分别通过；不要粗暴合并无共同祖先的两套历史。
固定SDK/锁依赖；source checkpoint后创建全新publish/package/installer目录。发布附件含通道契约、校验和及完整对应源码；历史Release/资产不改写。实际更新执行权仍仅限已安装正常Profile，Test不得放宽为可正式安装。
`.artifacts/channels/` 保留测试、双版本读写、原生UI与独立安装实验。原工作区及继承的行尾修改不纳入任务提交；禁止清理或覆盖。

## 1.2.0-beta.1 分发边界（2026-10-16）

当前Beta源码树`D:/Projects/The-Order-worktrees/public-beta-1-20261016`，从32942db延续；`.artifacts/beta1`保存本轮证据。用户已单独授权通过Beta门槛后推送Tag与Pre-release，不等于授权修改Production、云服务或日用安装。

`native/Directory.Build.props`控制SemVer/Windows文件版本。Release publish仍传`-p:NativeProductionCandidate=true`，但预发布应用默认NativeProfileKind.Beta并拒绝稳定Profile；受控Test入口仍要求显式合成根。公开默认Profile是`%LOCALAPPDATA%/TheOrder-Beta/Profiles/Beta`，禁止直接复制稳定Profile。测试必须使用合成目录，不能把Beta标记当Production授权。

复用`package-native-release.ps1`与`package-native-installer.ps1`；它们从源属性和实际二进制校验版本，Beta安装ID/目标/任务栏独立，卸载保留Beta资料。稳定版更新feed和版本规则继续拒绝预发布；Beta仅手动下载Pre-release，无自动安装。本轮不生成Beta差分自动更新文件，完整Setup附SHA-256和对应源码。


## 详情排版与本机日用切换验证（2026-10-16，当前）

当前入口 `D:/Projects/The-Order-worktrees/detail-typography-daily-gate-20261016`，基线2fac66d，代码检查点3d171dc、f149f6b；原dirty根工作区及前轮RC目录均保留。仅音乐展开区/动画分集排版与Gate发现的Win32原子临时路径问题，未增加Provider、迁移schema或更改辅助工具默认关闭策略。

固定SDK10.0.401，locked restore，x64 Release publish仍用 `-p:NativeProductionCandidate=true -p:UseSharedCompilation=true -p:PathMap=<当前repo>=/_/TheOrder`。本轮最终 `publish-v3`，完整客户端通过 `services/bangumi-auth-worker/test/run-client-bridge.mjs --all-client-tests` 启动，4020/0/0及本地workerd合成授权契约均通过；不是新的真实账号授权验证。生成产物与日志在 `.artifacts/detail-typography-daily-gate-20261016/`。

真实窗口由该目录 `Start-Test.ps1 -Run <全新ui-名称> -Payload release-final` 启动，固定自己的合成 `test-profile`；`Typography-QA.ps1` 使用既有Test受限原生peer，最终online16项、offline8项，`Session.ps1`/`capture-v3` 只对精确Test PID/EXE/Profile截图。验证结束所有Test实例与子进程网络注入器均停止；不能盲跑历史 `-Offline`，它需要重新创建并验证自己的注入器。无系统网络/代理/DPI变更，没有桌面输入或真实游戏执行。

长路径宿主回归不能仅用测试宿主PASS代替应用事实：相同WinUI manifest的 `runner/Proof.exe cache-probe` 在修复前target229/temp266失败，修复后成功；3项普通测试覆盖229/244/300字符目标及重启/原文/主库不变，但普通测试宿主本身启用了长路径，修复前也会通过，不能称作红灯。`long-path-actual-host-before.json` / `after.json`、首次失败的原生离线控制记录均保留。最终WinUI在相同资料根重新在线写缓存、退出、阻断Test进程全部API连接后重开，动画与音乐恢复成功；分集目录仍是会话缓存，离线重启明确降级，无新持久化承诺。

`release-final` 是完整待审安装载荷，`installer-final/distribution/The-Order-1.1.0-Setup-x64.exe` 仅构建未在日用安装运行。`install-lab-final` 采用独立AppID/Mutex/路径，无正式注册/快捷方式/自动启动；37项覆盖当前已安装载荷→最终候选、强制中断/修复、旧新实际WinUI、程序单独回退后原数据重开、再次升级、卸载保留合成资料与未知文件。真实Production从未参与；不能将此升级成标准安装器/干净Windows/所有DPI通过。

日用切换CONDITIONAL GO，尚未执行：用户批准且托盘正常退出后，先备份并核对受管程序文件，保留未知文件，只切换程序；回退不自动恢复旧Profile快照。真实安装基线清单SHA256 `69F22D89C2A4CF810F08AF2BFA56B9747DABC65BCC62E7BAEBBB3DE88995604C`，可回退来源仍为 `launch-global-rc-20261016/installer-rc2/payload` 及对应addon/Setup。最新候选和证据哈希以本轮completion/gate-ledger为准，不依赖相同1.1.0显示版本判断身份，不动公开自动更新版本比较。

## ACGN RC qualification（2026-10-16，当前）

使用 `D:/Projects/The-Order-worktrees/rc-qualification-20261016` / `codex/rc-qualification-20261016`；基线8e57662，运行修复5876b66。最终HEAD在本轮 `.artifacts/rc-qualification-20261016/completion.json`，只接受本轮自己的证据。本轮禁止真实Production、日用安装替换、推送和公开发布；下方历史安装授权已经完成，不可复用。

固定SDK10.0.401，`dotnet restore native/TheOrder.slnx --locked-mode`；x64 Release publish加 `-p:NativeProductionCandidate=true -p:UseSharedCompilation=true -p:PathMap=<当前repo>=/_/TheOrder`。本轮 `publish-final` → `payload-final`，实际窗口只用 `Start-Test.ps1 -Run <全新ui-名称> -Payload payload-final`，严格绑定新建 `test-profile`。它使用仅子进程生效的127.0.0.1 CONNECT故障注入，不拦截TLS，不修改系统代理/防火墙/DNS。`Control.ps1 -Control <该run> -Action exit` 正常结束；不得裸启动WinUI默认Production入口。

新Bangumi入口缓存是可选、可清空的Profile旁目录，不改主库、人工映射或NativeProfile schema。服务读取和写入都验证媒体ID、授权epoch及世代；平台写入在BeforeReplace再次检查，已开始的原子替换完成后保持完整文件。未来/外来/损坏文件不收养不删除，权限/容量失败只使缓存不可用。最终完整套件中相关服务81/0，含36项新增，总计4017/0/0；Worker24/0/0是另一个测试套件，子集不可相加。

完整客户端仍从 `services/bangumi-auth-worker` 运行 `node test/run-client-bridge.mjs --all-client-tests`，显式传本工作区固定SDK及新结果目录；本轮最终TRX为 `tests-full/client-full-with-worker-1792107143633.trx`。首次4项新增断言错误使用record内数组引用比较，修正为完整序列化结构比较；失败TRX保留，未删用例。Music重试不在原有受限controller白名单，使用仅识别准确Test PID/窗口/按钮的Windows UIA辅助调用；不是产品按钮失效或物理输入。

`install-lab/` 仅是复制真实Inno源后加严格实验根限制的测试副本：独立AppID/Mutex，无卸载注册/快捷方式/自动启动，删除路径只能是本轮合成Profile。旧公开payload和新payload逐文件验证；中断只强杀写入标记并验证祖先链的这个副本子进程。34项通过不等于原版Setup/干净OS通过。合成旧Profile普通字段在新旧code中均可读；其他含新nativeExtras的旧版本安全拒绝行为仍由完整回归覆盖，不能推广成所有降级均可读。回退快照恢复到同一物理Root，不能把拷到新位置的Profile当作可用克隆。

当前资格Gate为NO-GO。可发给用户的仅为明确标注未获RC资格的隔离试用ZIP；不能分发 `install-lab` 测试安装器冒充正式安装包。实际200%DPI/浅深/窄宽/公开五媒体已验证；100/150、物理输入、Windows10、干净Windows11、宿主断网、真实OAuth到期恢复和正式更新交接仍须补验。版本仍1.1.0，自动更新必须拒绝同版本；不得通过放宽Test安装或版本检查凑通过。

## 全局启动与本机体验候选（2026-10-16，当前）

当前维护 worktree `D:/Projects/The-Order-worktrees/launch-global-rc-20261016` / `codex/launch-global-rc-20261016`，基线240e3a5；不要到根仓库的既有 dirty 性能工作或历史发布目录继续开发。应用行为 f82ad94，构建符号处理6338e60，RC2分发源码冻结5b23883；后续文档提交见本轮 `.artifacts/launch-global-rc-20261016/completion.json`。用户最终仅另行授权本机体验替换，不等于远端发布或真实资料操作授权。

固定 SDK10.0.401，锁定 restore，Release x64、`NativeProductionCandidate=true`、`UseSharedCompilation=true`，publish 加 `-p:PathMap=<当前源码绝对路径>=/_/TheOrder -p:CopyOutputSymbolsToPublishDirectory=false`。不要并行写同一 Native 构建输出。`publish-v3` 为最终应用代码；打包 `package-native-release.ps1 -PublishedDirectory <publish-v3> -OutputDirectory <全新package> -SourceManifest <source-freeze-rc2.json> -DocumentationDirectory <仓库/docs/release/candidate-20261016>`，再以已验证 Inno6.7.3 调用 `package-native-installer.ps1`。默认不包含 Test 启动器；不能把 Test-only RC wrapper 当可安装 release 包。

全局偏好 `native-global-launch-v1.json` 是可选用户资料，不是缓存，首次三个 Enabled 均 false。旧主库无写入/迁移需求；用户明确保存新的游戏三态才会产生 nativeExtras v2。备份/降级必须保留全部用户伴随组件，不能只导出主库。旧版会安全拒绝新配置；同物理Root的合成全量快照恢复验证通过，不能直接把复制到新目录的 Profile 当可用克隆。

完整回归入口仍在 `services/bangumi-auth-worker` 执行 `node test/run-client-bridge.mjs --all-client-tests`，显式指定 `THE_ORDER_TEST_DOTNET=<本工作区固定SDK>` 和 `THE_ORDER_TEST_RESULTS_DIR=<全新结果目录>`。最终 `tests-full/client-full-with-worker-1792088849561.trx` **3981/0/0**，包含本地 workerd/SQLite 合成契约，不是真实账号授权。集成232、音频36为重叠子集；不得相加。包复制10/0、Inno文本文件选择安装夹具12/0不是完整应用安装测试。准确旧版DLL兼容16/0+14/0及真实Setup离线字节重建有独立回执，不冒充VM升级或更新执行。

实际UI入口为证据根 `Start-FinalCandidate.ps1 -RunName <未使用的ui-rc数字>`，严格绑定 `installer-rc2/payload` 和自建 `test-profile-final`；禁止裸启动默认Production。该测试库是公开元数据+22项合成记录，无真实游戏或凭据。`Control.ps1 -Control <新run> -Action exit` 正常退出。原生 peer 负责有限按钮/展开/焦点；三态选择使用有界原生 ComboBox 设置，不能写成物理键盘。当前 `ui-rc4` 已完成设置和三类人工选择重启恢复并退出；`test-library-final-proof.json` 比较全部字段，只允许已测试的那项启动覆盖修改。

UI实验中的旧夹具目录和存档路径曾过期，保存被正确拒绝；在程序退出后用真实 Profile writer 仅修正合成路径，失败回执保留。受限控制器重复打开安装草稿曾等待正常保存提示；一次性 UIA 仅对准确Test PID的该提示调用原生“放弃草稿”，无桌面输入、任意目标或游戏启动。最终重新验证全部相关步骤，未修改产品来绕过校验。

用户授权的本机替换已完成，详见 BRIEF 当前节与 `local-promotion/installation-receipt.json`；安装后的空Test smoke也通过，未启动Production。常规重复安装不能照抄该已完成的不可重试脚本；先确认新的明确授权、准确包和全新回退。原安装程序与新发行包的载荷、GUI、Setup及旧程序回退均已校验。未准备隔离VM、其他实际DPI、物理输入与真实工具联调仍待人工，不开启宿主网络/代理/安全/DPI设置来伪造隔离验证。发布材料与网络/许可边界见 `docs/release/candidate-20261016/`，最终Gate为CONDITIONAL GO。

## ACGN 真实体验 Gate（2026-10-15，上一节点）

维护入口 `D:/Projects/The-Order-worktrees/acgn-experience-gate-20261015`，分支 `codex/acgn-experience-gate-20261015`，基线 e62bc12；前一语言 worktree 与根仓库的既有未提交性能工作均保留。本轮没有安装、Production、密钥、推送、合并或发布操作。现有用户身份持久化、语言偏好和缓存格式不变，无 schema migration。

固定 SDK10.0.401、锁定还原与既有依赖；最终构建 `dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -o .artifacts/acgn-experience-gate-20261015/publish-v9`。原工具/依赖 junction 只复用已锁定运行时，不升级包。候选必须通过本证据根 `Start-IsolatedTest.ps1 -RunName <全新名字> -PublishName publish-v9` 启动，固定为自建标记 Test Profile；不能裸启动 EXE。Test 种子是21项合成库，没有复制真实库、缓存或凭据；元数据首次经匿名公开API读取，后续缓存单独标注。用 Control exit 正常关闭后，以本证据根 ui-runner verify 核对 library payload 字节。

`Accept-V9.ps1` 在实际窗口执行7组连续场景，原生控件回执及截图分别保存。驱动 `native/scripts/invoke-acgn-control.ps1 -ControlDirectory <本证据根/新run> -ProfileDirectory <本证据根/test-profile> -Mode invoke -Id GameRelatedJump` 验证运行 PID/路径、Test marker、有限控件及详情主体互斥。Expand/Collapse 使用原生对应 pattern，Invoke 必须命中实际可用按钮，不重试已发送的点击；最多等2秒让滚动布局稳定。观看平台只能 inspect/focus，不能通过这个驱动打开外站。该机制不提供任意代码、桌面输入、授权、启动或真实数据操作。

完整客户端仍从 `services/bangumi-auth-worker` 运行 `node test/run-client-bridge.mjs --all-client-tests`，设置 `THE_ORDER_TEST_DOTNET=<固定SDK绝对路径>`、`THE_ORDER_TEST_RESULTS_DIR=<本轮证据根/新目录>`。本轮最终 tests-accepted/client-full-with-worker-1792078380573.trx **3821/0/0**，含本地 workerd/SQLite 合成交换1、刷新1、直连资料2，不是 Cloudflare 部署或真实授权。新增9项用例；聚焦141/0/0、后续38/0/0与早期全套3815/0/0有重叠，不能相加。全套无失败或跳过；基线阶段的历史进程超时不能算成本轮失败，也不能藏掉历史证据。

早期v2由本轮测试助手缺少using/错误peer构造器导致编译失败，后续构建已修复并保留日志；初版100ms布局等待出现未发送点击的命中失败，现为有界2秒等待，未绕过命中检查；错误的控制器动作/Expander模式也保留在早期回执。真实404反例与修复后的原生重试、返回恢复在截图索引中。`report.md` 明确分开真实公开API、缓存、原生UI、控制入口及模拟故障；不把它们混作全场景人工验收。

仅允许在当前支持边界内进入RC验证准备。漫画逐章目录仍缺失；各媒体完整主题/DPI、物理鼠标/滚轮、真实断网与OAuth/Production验收尚缺。最终候选及核心模块哈希、场景结果、提交与限制见本证据根 completion.json / report.md / screenshots-review.html。维护时继续保护原始语言、独立Release/Volume/Publication身份及用户确认目录，不把远程缓存当用户决定备份。

## ACGN 多语言资料 v1（2026-10-15，上一节点）

维护入口 `D:/Projects/The-Order-worktrees/acgn-language-v1-20261015`，分支 `codex/acgn-language-v1-20261015`；基线 cffb0bb 加原工作区两份文档的完整继承检查点 bd576da。原质量工作区不变。本轮不访问 Production、不迁移真实数据、不替换日用、不推送或发布。运行 Provider 仍为 NextMoe、Bangumi、AniList、MusicBrainz、NDL；MangaDex/MangaUpdates 不在运行链路。

固定 SDK10.0.401 和既有锁依赖；构建 `dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -o .artifacts/acgn-language-v1-20261015/publish-v8`。不要裸启动或把候选放进日用目录。受限 `Start-IsolatedTest.ps1 -RunName <全新名字> -PublishName publish-v8` 设置本轮标记 Test Profile、现有 Test 控制器与匿名公开 API，清除继承的资料目录环境变量，不复制真实库或凭据。`Accept-Ui-V8.ps1` 保存五类详情、窄窗口与已证实的跨容器重叠回归；其 run 目录要求全新，重复执行须使用新的目录名。截图助手只接受匹配 publish-v8、Test PID/Profile 和新 PNG；不控制桌面。退出使用 Control exit，再由 ui-runner verify 检查合成 library payload 字节。

资料偏好是独立可选组件 `native-content-language-v1.json`（含 ProfileId、StoreId、Version=1），和旧 `native-language-v1.json` 区分。复用 Native Profile 的路径验证、lease 与 WindowsAtomicFile，最多4096字节，旧有效文件有 .bak；不改主库 schema。保存失败不改变当前选项，未知版本/外来身份只读保留。没有缓存清理或主库迁移需求；旧程序忽略新组件，回退时保留它即可。跨站人工身份决定仍由既有持久化组件管理。

完整客户端入口仍在 `services/bangumi-auth-worker` 执行 `node test/run-client-bridge.mjs --all-client-tests`，显式设置 `THE_ORDER_TEST_DOTNET=<本工作区固定SDK绝对路径>` 和 `THE_ORDER_TEST_RESULTS_DIR=<本轮证据根/全新测试目录>`。本地 Worker 上游是合成契约，不是真实 OAuth 联调。公开 Provider 验证原始响应在 live-data；final-source-replay.json 使用当前解析器/语言选择器重放这些哈希固定响应，不能与新实时请求混称。过程中的 XAML 样式异常、QA 允许域名遗漏、旧进程测试超时及修复前 UI 重叠证据均保留。

最终实现 fd89363；WinUI publish-v8 构建退出0，最终全套 tests-accepted/client-full-with-worker-1792072699239.trx 为3812/0/0，包含57项新增语言/原始字段/安全保存用例。tests-final 首次3810/1/0因既有ProcessFactRemediationTests进程交接等待超时；该项tests-recheck单独通过，完整重跑通过，不把分次相加当全套结果。当前源码公开响应回放12条×5种偏好、13份原始HTTP响应SHA256保持；真实UI测试仅用Test Profile及公开匿名数据。ui-verify-v8.log证明合成library payload字节相同。完整限制、字段级矩阵、失败与整改证据见report.md。

修改资料语言后重新进入详情生效；界面语言构建开关不变。显示投影不能送进身份匹配、缓存持久化或库事务。增加本地翻译包应通过 ILocalContentTranslationPack 返回独立、有来源的 synopsis 变体，必须显式启用、可取消、限制大小；本轮未安装翻译包，不允许在线翻译服务成为依赖。

## ACGN 全媒体质量候选（2026-10-15）

当前隔离维护入口 `D:/Projects/The-Order-worktrees/acgn-quality-20261015`，分支 `codex/acgn-quality-20261015`，继承检查点 `5bb654d`。保护原音乐工作区全部 10 项 dirty；开发阶段没有本机替换、Production 操作、真实资料迁移或发布授权；用户随后明确授权替换，已完成本机安装，未授权公开发布。证据 `.artifacts/acgn-quality-20261015`。不要裸启动候选；`Start-IsolatedTest.ps1 -RunName <新名字> -PublishName publish-v3` 只使用本证据根的标记 Test profile，种子来自已校验 SHA256 的既有合成 library payload，没有复制凭据或真实库。

固定 SDK 10.0.401，锁定 restore；WinUI 使用 `dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -p:UseSharedCompilation=true -o .artifacts/acgn-quality-20261015/publish-v3`。全套通过 `services/bangumi-auth-worker/test/run-client-bridge.mjs --all-client-tests`，显式设置 `THE_ORDER_TEST_DOTNET` 为固定 SDK 和 `THE_ORDER_TEST_RESULTS_DIR` 为本证据根 `tests-full`。该入口启用本地合成上游，3755/0/0 并不代表真实 OAuth 授权或正式私库验收。

当前控制入口保留 Test profile 双重限制；新增 `catalogEntityOpen` 仅接受准确 `Character:<id>`、`CreditName:<id>`、`Person:<id>`、`Company:<id>`，用于验证真实公开实体来源，不能用于 Production。截图助手只允许准确 Test PID、EXE、Profile 与新 PNG 输出路径，通过 WGC 捕获窗口，不控制桌面。退出通过 Control `exit`，等待进程正常结束，再运行隔离 `ui-runner` 的 verify 核对合成 library payload 字节；不强杀或恢复真实资料。

最终 Native 源码提交 `998465c`（实现 `fc74f10`）；测试全套后仅增加 Test 嵌套书籍控制转发，已用 v3 实际窗口验证。完整字段与分母见 `field-coverage.md` / `corpus-summary.json`，UI链路、重启恢复、截图和限制见 `report.md` / `ui-acceptance.json`。PV probe 为就绪未播放，不能称真实视频播放通过。原始工作区10项哈希须继续保持；用户后续授权的本机安装已完成，未进行公开发布。

本机替换已完成（用户在质量验收后明确授权）：`publish-v3` 安装到原 `C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，版本仍1.1.0。Setup/GUI完整性校验退出0；659份payload、3份入口、7份候选模块及update-base重复核验一致，桌面/开始菜单快捷方式保留。2821份正式/旧资料文件仅比对脱敏元数据，前后未变，不读内容、令牌，不复制或启动Production。667份旧程序核验备份至 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261015-195908-acgn-quality/Program`。包装后另用新空Test Profile实际启动并正常退出0；不替代此前完整详情UI证据或真实私库验收。安装源码HEAD为`cffb0bb`，实现`fc74f10`；回执 `local-promotion/installation-receipt.json`、`independent-installed-verification.json`。没有公开发布、推送或合并主分支。

## MusicBrainz 检索修复（2026-10-15，当前）

当前工作区 `D:/Projects/The-Order-worktrees/music-retrieval-20261015`，分支codex/music-retrieval-20261015，基于874ed514并保留继承dirty。本轮产品变更未提交；源码冻结、真实公开API和修复前反例保留在 `.artifacts/music-retrieval-20261015`。仅改变来源音乐别名/查询计划、候选顺序、检索缓存代际及提示，不迁移用户决定或改变游戏库schema。新增TitleAliases兼容旧JSON，原指纹不加入查询别名；Search key另含retrieval-4与别名。

相关回归264/0/0；按上一节既有本地Worker入口运行全套，`tests-full/client-full-with-worker-1792054990288.trx`为3697/0/0。真实LiveProbe用本轮编译Provider和匿名官方API验证Blue sky两种输入、CLANNAD及准确发行曲目，所有原始JSON/503和初轮探针断言失败保留，不冒充真实账号或私库验收。WinUI Release x64 publish-v1退出0。用户授权本机替换，包装/安装依旧脚本与边界，回执在local-promotion；没有公开发布/推送授权。

本机安装已完成：659payload+3入口逐份及独立核验通过，4核心托管DLL与publish-v1相同；Setup/GUI验证退出0，原快捷方式可用。2821份正式资料文件元数据未变，未读取内容或凭据、未运行Production迁移；667旧程序仓库外完整回退路径见BRIEF。仅额外运行空Test启动/正常退出检查，不作为真实详情交互验收。代码仍为未提交冻结变更，安装回执用sourceHead/sourceDirty/sourceManifestSha256标记，不能误称874ed514本身包含本轮修复。

## ACGN 身份持久化集成（2026-10-15，当前）

维护入口 `D:/Projects/The-Order-worktrees/acgn-identity-20261015` / `codex/acgn-identity-20261015`；红队整合提交9becb20与原8feede0 tree相同，基线491/0/0。新用户组件 `native-media-identities-v1` 复用NativeProfile身份、路径pin、进程写入所有权和WindowsAtomicFile；按来源Revision比较提交，不与远程cache淘汰共用容量。书籍继续使用既有独立publication-bindings；其读取发现旧层级并显示待核对，不改主Profile或库schema。用户随后明确授权本机替换，已将874ed514/publish-v2安装至原正式入口，未授权真实资料试迁移或公开发布。

本机替换证据 `local-promotion/`：现成 `package-native-release.ps1`、`package-native-installer.ps1` 包装，659份payload与3份入口逐份哈希一致，Setup及GUI校验退出0；包装候选在新空Test Profile启动/正常退出0。受限 `Install-Local.ps1` 只更新原程序，拒绝运行中或链接目标，667份旧程序已核验备份到仓库外回退目录；2641份正式资料仅比较脱敏文件元数据且完全未变，不读内容、不启动Production。安装及独立复核回执位于该证据目录；版本仍1.1.0，无GitHub上传或主分支合并。真实UI验收仍未完成，不能用启动检查替代。

固定SDK路径同下文；已按锁文件restore测试与WinUI。最终全套含Worker合成联调：在 `services/bangumi-auth-worker` 设置 `THE_ORDER_TEST_DOTNET=<固定SDK绝对路径>`、`THE_ORDER_TEST_RESULTS_DIR=<本轮证据根/tests-final>`，执行 `node test/run-client-bridge.mjs --all-client-tests`。该可选参数只扩展本地测试范围，不部署Worker。最终 `client-full-with-worker-1792051622242.trx` 为3689/0/0；合成上游拒绝其他地址，交换1、刷新1、直连资料2。依赖沿用锁文件一致的既有node_modules junction，没有npm安装/升级。先前无Worker的全套3684/0/1和自动cache新反例2项失败保留，不用它们替代最终结果。

最终WinUI构建 `dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -p:UseSharedCompilation=true -o .artifacts/acgn-identity-20261015/publish-v2`，日志 `build-winui-v2.log`。不要并行写同一Release输出。新入口 `.artifacts/acgn-identity-20261015/Start-IsolatedTest.ps1 -RunName <新的短名>`，必要时显式 `-ReuseTestProfile`；`-ValidateOnly`只验证候选及Test路径，不启动应用。默认全新空Test资料，不读取Production、不复制凭据；预设原生Test控制目录供下一阶段受限夹具使用。候选构建/入口校验不等于实际视觉验收。

迁移先备份准确旧Envelope及规范化记录，成功归档后才允许远程缓存淘汰旧绑定；源ID缺失时只凭旧完整指纹归属，不按标题猜。归属回执在主提交后失败会保留已提交选择，并在后续读取修复。恢复旧格式的合成测试校验了逐字节Envelope及v1 reader；未运行旧EXE或真实降级。新用户组件是必须单独保留的用户资料，不能被缓存清理命令删除，主库旧导出/lastGood不包含它。最终源文件哈希、兼容证明及原工作区不变证据都在本轮证据根。

## ACGN 架构红队验证入口（2026-10-15，当前）

源码 `D:/Projects/The-Order-worktrees/acgn-redteam-20261015`，分支 `codex/acgn-redteam-20261015`；继承快照 `50efc66` 完整保留前轮 12 项 dirty。仅本轮改动独立提交，证据 `.artifacts/acgn-redteam-20261015`。禁止 Production、日用安装、推送及发布；下面历史安装命令不构成本轮授权。

固定 SDK10.0.401 从 `D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/native-phase1/tools/dotnet/dotnet.exe` 运行，锁定 restore；构建命令为 `dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -p:UseSharedCompilation=true -o .artifacts/acgn-redteam-20261015/publish-v1`，日志 `build-v1.log`。没有启动候选或生成安装包。不要裸运行默认 Production 入口。

最终定点 `dotnet test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -p:UseSharedCompilation=true --filter "FullyQualifiedName~AcgnRedTeam|FullyQualifiedName~AniList|FullyQualifiedName~MusicBrainz|FullyQualifiedName~MusicAlbum|FullyQualifiedName~Book|FullyQualifiedName~CatalogRefinement|FullyQualifiedName~OnlineTransport"` 为 491/0/0，含 71 项独立红队用例。`tests-baseline/` 的失败不得删除或替代；`tests-final/focused-frozen.trx` 是最终源码结果。全套 `--no-build` 首轮 3624/24/1，23 项依赖缺失随后按 `dependency-recheck-filter.txt` 重跑 23/0/0；分次结果不等于一次全套通过。SDK 与 Electron44.5.1 仅通过本工作区忽略目录 junction 复用既有工具，见 `test-dependencies.json`，没有 npm 安装或依赖变化。剩余图片夹具失败及 Worker 联调跳过如实保留。

原子取消/乱序用例使用临时合成 Test Profile 与真实原子 writer；不会访问用户正式资料。公开 API 探针与模拟测试分开：`public-api-c/manifest-index.json` 列出请求及哈希，NDL v3 超时文件已在 manifest 标为无效，不能用作成功证据。当前增量字段兼容读取旧资料，旧自动关联必须重验；没有实际旧 EXE 降级运行验证，也没有进行用户映射迁移。

## 详情可读性优化（2026-10-15，当前开发；隔离验收完成）

维护入口为 `D:/Projects/The-Order-worktrees/detail-readability-20261015` / `codex/detail-readability-20261015`，干净基线 `531cf8c`，版本保持 1.1.0。旧工作树和本机正式安装保留；本轮未提交、未安装、未推送或合并。下方同日期旧入口和旧安装状态保留作历史证据，不能据此切回旧候选继续开发或自动替换程序。

主要修改在 `native/TheOrder.WinUI/Views/`：`BookPublicationView`、`AniListDetailView`、`MusicBrainzAlbumView`、`MusicBrainzEntityDetailView`、`CatalogContentView`、`DetailsPage.Catalog`，以及新增的 `DetailSynopsis`／`MusicDetailFormatting`。额外修复 `MusicBrainzProvider.Entities` 人物生卒日期未知组件，保留原串；通用发行日期和关系日期校验不变。共享身份模型、网络请求、匹配确认、缓存和映射持久格式均未改变。书籍表单与字段、音乐曲目和专业关系保持单个页面滚动器；默认简介七行、860 阅读宽度，展开记忆只在当前视图内按身份保存，最多 32 项，不写用户资料。

构建沿用锁文件、固定 SDK 10.0.401、x64、`NativeProductionCandidate=true` 和 `UseSharedCompilation=true`，不要并行写同一个 Native 构建输出。证据根 `.artifacts/detail-readability-20261015`，最终候选 `publish-v6`，日志 `build-v6.log`。`tests/detail-regression-final.trx` **229／0／0**：过滤 AniList、BookDetailsViewModelTests、BookBibliographyServiceTests、MusicAlbumViewModelTests、MusicBrainzDeepeningTests、MusicBrainzEntityTests、MusicBrainzArtistPartialDateTests；最后一类新增 27 项。此前 202 项结果保留，不能与最终 229 相加。没有全仓或长时测试。

隔离窗口使用该证据根已有 `Start-IsolatedTest.ps1 -RunName <新的短名称> -PublishName <已验证候选目录>` 与 `Control.ps1`。禁止裸启动候选以免默认进入 Production；`fixture-ready` 仅表示控制入口存在，仍须等 `probe` 中 `runtime.lifecycle.ready=true` 再导航。书籍实际样本为 Bangumi 43194、AniList 45821 和 NDL `000010877344`；音乐为 Bangumi 8201 的限定发行 `a9f46365-0bdd-4015-9253-3d271a5ee8ee`。只读公开资料与 Test 内确认映射不等于真实用户资料操作。

实际窗口截图在本证据根 `*.png`，状态证据在各 `ui*/bangumi-evidence.json`；截图限制准确 Test PID／EXE／HWND，不使用桌面输入。深浅色、640 窄窗、真实出版结果、人物职责、冲突预览已观察。v6 的 Artist／Work／Recording 真实请求成功；`music-artist-ready-final.json`、`music-recording-final.json`、`music-return-settled-before.json`／`after.json` 留证。MusicBrainz 人物原始响应与官方 PartialDate 格式依据在 `music-artist-diagnosis/`；未知日期不得用于放宽发行版匹配。

Test 已通过 Control 的 `exit` 正常退出；等待确切 PID 结束后执行 `dotnet run --project <证据根>/runner/BooksProof.csproj -c Release -p:Platform=x64 --no-build -- verify`，`test-library-proof.json` 确认合成库 payload 未变。首次在退出尚未完成时验证被 writer lock 正确拒绝，等待退出后才重试，未绕过锁。既有 NDL 搜索子状态在 AniList 跨作品返回时不恢复，见 BRIEF 当前节；主简介展开和音乐稳定返回通过。布局可进入用户评审，不代表全面发布 Gate；未验证物理鼠标和长时稳定性。本轮不提交或更换日用安装。

## AniList 书籍确认修复（2026-10-15，当前）

入口 `D:/Projects/The-Order-worktrees/anilist-book-confirm-20261015` / `codex/anilist-book-confirm-20261015`，基线16607fe；版本1.1.0、原锁文件与SDK10.0.401保持。候选 `.artifacts/anilist-book-confirm-20261015/publish-v1`，生产资料未作为测试输入。

实现提交2396ead已完成本机原目录安装；Install-Local.ps1拒绝运行中程序、保留旧程序回退、逐文件核验，安装及GUI校验均退出0，2521份正式资料文件元数据未变。完整安装与回退回执在本证据根prepared-install，BRIEF顶部记录实际备份路径。没有启动Production或读取凭据，不能将入口校验当私库全功能验收。

主要修改AniListMatching.Assess：Anime年份仍是硬冲突；Book日期作为原始证据和口径提示，不单独阻止手动确认。StrongEvidence对Book继续返回false；明确ID、Format、Kind以及Volume/Publication→多卷防护保留。不改变Fingerprint、用户映射格式或缓存版本。

固定dotnet执行 `test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -c Release -p:UseSharedCompilation=true --filter "FullyQualifiedName~AniList|FullyQualifiedName~BookDetailsViewModelTests|FullyQualifiedName~BookBibliographyServiceTests"`，最终146/0/0、约1秒；TRX在本证据根tests/confirmation-regression-final.trx。没有全仓或长时测试。

实际UI使用本证据根Start-IsolatedTest.ps1和Control.ps1；fixture-ready只表示控制入口就绪，导航前还须等待probe的runtime.lifecycle.ready=true。首次过早导航失败保留，随后及重启均已等待正常恢复。Test专用aniOpen可传Kind Book与准确SubjectId43194，不构造真实跨媒体边；aniPreview45821、aniConfirm走原产品操作。证据增加confirmButtonEnabled以验证原生按钮状态。截图工具限准确Test PID/EXE/profile，正常exit后runner verify核对合成库未变。

## NDL 可靠性修复（2026-10-15，当前）

维护入口 `D:/Projects/The-Order-worktrees/ndl-reliability-20261015` / `codex/ndl-reliability-20261015`，干净基线690d9c9。产物 `.artifacts/ndl-reliability-20261015/publish-v1`，版本1.1.0；锁定还原、固定SDK10.0.401、x64和NativeProductionCandidate=true。该工作区保留前阶段源码，未升级依赖。

实现提交26571dc，源码冻结清单在证据根source-manifest.json。用户正常退出后已执行Install-Local.ps1，原目录安装及GUI入口校验退出0；659份payload和3份入口哈希一致，2629份正式资料文件元数据未变。旧程序667份文件已核验回退备份，路径在BRIEF顶部和prepared-install/installation-receipt.json。没有打开Production或读取资料内容、凭据；不把安装入口校验当正式私库功能测试。先前等待退出的状态仅为历史记录，当前状态为INSTALLED_AND_VERIFIED。

短回归使用固定dotnet执行 `test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -c Release -p:UseSharedCompilation=true --filter "FullyQualifiedName~Book|FullyQualifiedName~Ndl"`；163项通过。新增独立确认保护后只重复BookBibliographyServiceTests，20项通过，3项新增、17项重叠。证据为同根 `tests/ndl-focused.trx` 和 `tests/confirmation-safety.trx`。无全仓或长资源测试。

`NdlBibliographyProvider`共用25秒总预算，一次尝试12秒、至多两次；HTTP 429/502/503/504的原有限重试保留。连接失败或尝试超时可重试，用户取消和总截止不重试。请求串行、至少1秒间隔；长限流冷却及入口占用明确快速降级。每页10项、最后起始91保留100候选浏览；解析旧20项缓存不变。

真实API探针 `probes/` 与产品链路 `live-provider-proof.json` 分别记录，不能把模拟通过当真实恢复。后者实际观察ISBN首次卡住被取消、重试200，标题及独立OAI成功，缓存复用零请求。`runner/BooksProof.csproj`只允许本证据根Test profile，seed仅复制既有合成library组件，不复制设置、凭据或Production。临时helper的NU1603仅属于验证工程，产品锁定构建无依赖变更。

UI入口 `Start-IsolatedTest.ps1 -RunName <新的短名称> -PublishName publish-v1`，控制和截图都限准确Test PID/EXE/profile。不要裸运行候选默认Production。候选预览用SRU已得资料；`ConfirmAsync`仍独立读取OAI（允许有效的已核实记录缓存），失败不保存。测试完通过Control.ps1的exit正常退出，并用runner verify核对合成library payload。新FromSearch仅为展示标记，不进入持久格式。

## ACGN 深层导航与本机更新（2026-10-15，当前）

维护入口 `D:/Projects/The-Order-worktrees/acgn-depth-navigation-20261015` / `codex/acgn-depth-navigation-20261015`，基线a1d360a。最终候选 `.artifacts/acgn-depth-navigation-20261015/publish-v4`。锁文件和固定SDK10.0.401构建，使用既有x64、NativeProductionCandidate=true、UseSharedCompilation=true；不并行写Native构建输出。上轮worktree保留不变。未更改版本或依赖。

本轮相关回归 `tests/regression-final.trx` 1094通过，最后节点 `tests/nodes-final.trx` 146通过，均0失败/跳过，数量有重叠。新节点覆盖MusicBrainzEntity、BangumiEpisodeDisplay、CatalogReleaseExploration；旧Book/NDL/AniList/Bangumi/MusicBrainz/Related/NextMoe等相关回归保留。ui-review.md和core-review.md记录3项已关闭P2，不把旧失败删掉或重标通过。

真实窗口仅通过本证据根 `Start-IsolatedTest.ps1 -RunName <新名称> -PublishName publish-v4` 使用标记Test profile；Control.ps1为既有受限fixture，新增musicEntity/Back、aniEpisodeFilter/Expand、releaseFilter/Row/Rebind。Rebind只在Test调用真实SetWork两次，检查卸载代际；只读元数据，不启动游戏。最终截图accepted-v4-前缀；截图工具限制准确PID、EXE和Test根，不是Production控制接口。

MusicEntity reader复用原Provider实例、匿名HTTP、全局限流与35秒操作预算，只有精确Work/Artist/Recording查询，不递归抓关系。缓存32项/4MiB，单项512KiB，24h fresh、14d瞬态回退；永久拒绝设有界拒绝标记，所有公开返回深快照。UI历史16层且无第二层滚动器，返回专辑/实体保留位置和折叠，延迟恢复遇用户输入或新导航取消。Episode与发行筛选只作用已载入行，不把未加载说成没有资料；NextMoe发行engine原样验证，不改变识别Reader或安装。

用户已授权完成后替换本机正式程序。只能复用package-native-release.ps1、package-native-installer.ps1生成新包；安装前确保正式/测试进程均正常退出，备份原Program到仓库外本机回退目录，仅比较Production文件元数据、不读取内容。安装回执必须显示payload与GUI入口逐份哈希一致、Profile元数据未变；不推送、主分支合并或公开发布。原始正式资料和凭据不可随Test复制。安装结果应更新BRIEF并指向实际回执，准备完成不等于已安装。

本轮结果：feature提交f37997e、publish-v4已安装到原 `C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`。Install-Local.ps1仅为忽略证据目录内的一次受限本机更新助手，固定目标和安装包SHA，拒绝运行中程序/链接目标。先备份并逐哈希核验667旧程序文件，执行现成Setup静默升级；659payload+files.json+3入口验证一致，TheOrder.exe --validate-only退出0且不打开Profile；2464正式资料文件元数据前后相同。证据在prepared-install/installation-receipt.json、profile-metadata-before/after.json，回退根见BRIEF。Setup SHA256 AFD16EE8044F26453767D94787E41DE9D45A7FE26D7161F17CA3F9401A058A5B。没有强杀、卸载、Profile恢复/迁移、GitHub推送或公开发布。

## PV 原生滚轮修复候选（2026-10-14）

最新候选 `.artifacts/pv-native-scroll-20261014/publish-v3/`，同一 `codex/anilist-20261014`、HEAD `cddd6d5`加保留的dirty。短相关回归 `tests/related-final.trx` 583／0／0、9秒；原生OS输入比较记录在该证据根的 `ui-acceptance.json`。PV初始化前先拦截垂直滚轮，父host再恢复同一事件，原ScrollViewer处理原生惯性；不再以固定像素ChangeView模拟滚动。不能把初始化后直接重置Handled的早期候选当成最终修复，它已在实际输入对比中被否决。

验证必须显式Test启动：该证据根的 `Start-IsolatedTest.ps1 -RunName <新的名字> -PublishName publish-v3`。原生输入验证辅助器只在忽略的产物根内，限制准确Test PID／EXE／manifest／前台／命中，保护其他窗口并保留早期失败；它不是正式程序接口，也不能用于Production。已完成播放、折叠、导航释放、返回及Test书架不变验证，尚未人工实体鼠标验收。沿用用户本机替换授权，通过已验证旧程序的原托盘退出项正常退出后，本轮已安装到 `C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，1.1.0不升版。659份payload与3份入口独立重复核验，安装及GUI入口校验退出0，安装期间正式资料元数据未变，未读取其内容。旧程序备份 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261014-234341-watch-links/Program`；安装回执在本证据根 `prepared-install/installation-receipt.json`、`independent-installed-verification.json`。正式Profile未作为测试输入，也未在更新后重新启动。

## AniList 交互修复候选（2026-10-14）

本轮仍在 `D:/Projects/The-Order-worktrees/argon-details-20261014`，HEAD `cddd6d5`加原有dirty。当前新增六项修复的候选 `.artifacts/anilist-ux-fixes-20261014/publish-v7/`，最终短回归 `tests/related-final.trx` 552／0／0。明确Test启动：该证据根 `Start-IsolatedTest.ps1 -RunName <新名字> -PublishName publish-v7`；fixture-ready仅表示入口就绪，须等会话恢复完成后导航。用户明确回复“已退出，完成替换”后，本轮已安装至正式入口，未访问Production内容；前一次watch-links安装属于已完成历史。

Application的AniListMatching／Service负责强证据自动关联及“只读取候选”；Presentation的ChooseAgainAsync保留旧绑定直到确认。UI卡片内容 `IsHitTestVisible=false`，让外层按钮接收点击；PV `Expanding`自动加载、失败重试、原平台圆角入口、居中宽度、透明背景＋iframe CSS圆角，并以WebView2已处理的PointerWheelChanged驱动原外层滚动器。延续异步代际和注销／释放，不更改存储格式、OAuth、游戏启动或正式资料。

真实API、实际WinUI和模拟分别记载；实体鼠标未测，测试回调与原生坐标命中探针不等于人工点击验收。证据 `live-experience-proof.json`、`ui-confirm-reselect.json`、`ui-steins-reselect.json`、`watch-button-hit-proof.json`、`pv-wheel-proof.json`、`pv-final-evidence.json`与窗口PNG；全部Test实例正常关闭，库字节保持。维护规则和具体边界以原BRIEF本轮段落为准。

当前安装是该六项修复候选：`C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，版本1.1.0，安装与GUI入口校验均退出0；659份payload和3份入口独立重复核验通过，Production文件元数据未变，未正常启动正式Profile。证据在 `.artifacts/anilist-ux-fixes-20261014/prepared-install/installation-receipt.json` 与 `independent-installed-verification.json`；旧程序回退副本 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261014-222357-watch-links/Program`。未提交、推送或公开发布。

## 当前 Watch Links／PV 候选（2026-10-14）

维护入口 `D:/Projects/The-Order-worktrees/argon-details-20261014`、`codex/anilist-20261014`、HEAD `cddd6d5`加保留的dirty。最新验收候选 `.artifacts/anilist-watch-pv-20261014/publish-watch-ready/`；短相关回归534／0／0、8秒，TRX `.artifacts/bangumi-watch-data-20261014/tests/related-ready.trx`。真实API、30部固定样本、更新、UI／PV与安装证据在该证据根；不要默认以Production测试，复用受限Test启动和控制入口。用户最后允许本机替换，结果须以实际安装回执确认，不授权GitHub推送／发布或真实游戏。

`native/TheOrder.Infrastructure/WatchData/`保留完整主库和补充Git JSON的无损gzip、SHA-256及修订manifest，NOTICE随publish复制。主库npm0.3.231与Git哈希一致；补充Git与npm0.3.23100不同，按Git标注。不要修改测试样本数据、按标题自动绑定、将md转ss或复制采集脚本。`BangumiWatchDataIndex`按身份／类型／季度查询，`BangumiWatchDataService`每七天按需更新，固定公共GitHub版本与不可变修订JSON，复用无凭据HTTP池；失败／取消保留旧快照。`native-bangumi-watch-data-v1.json`是Profile绑定独立公开缓存，不能覆盖未知版本或改书架／授权格式。

整个观看区默认折叠，恢复其他放送平台卡片，固定Bilibili优先；旧排序偏好文件保留，按用户要求不再读写／显示控件。没有明确season_id时不制造入口、不加搜索；作品页不伪装为播放器，保留原始来源证据并在提示中说明。PV独立WebView2按需创建，InPrivate／固定origin与referrer／无自动播放，导航和折叠释放，不下载完整视频。无需新账号、服务器、平台密钥或Node安装。

最终窗口验证保留9个平台卡片、下标只有地区；宽浅色／窄深色均实际截图。`WatchLinksView`组合标准Expander，不继承Expander：早期collapsed候选触发WinUI样式TargetType异常，异常和未安装的失败包保留。新动画／重开条目递增滚动恢复代际并置零，返回仍恢复来源页位置；加强现有导航回归，最终实际CLANNAD→After Story从页首打开→返回355.5→原游戏通过。完整UI门禁见 `ui-acceptance.json`，安装前须通过该门禁。

本机已按明确授权替换：`C:/Users/MUSK/AppData/Local/Programs/The Order/TheOrder.exe`，1.1.0不升版。证据 `.artifacts/bangumi-watch-data-20261014/local-install-ready/`：659份payload加3份入口哈希一致，安装／GUI校验退出0，Production仅核对文件元数据且完全未变，未读取内容或启动Production。旧程序回退副本 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261014-202603-watch-links/Program`。维护源码与实际安装候选保持对应，未经新授权不得再次替换或公开发布。下文早期候选“正式程序不变／未安装”是本次安装之前的历史边界。

## AniList 当前隔离开发入口（2026-10-14）

唯一当前基线仍是 `D:/Projects/The-Order-worktrees/argon-details-20261014` 的完整未发布工作树，本轮分支 `codex/anilist-20261014`，保留原未提交 OAuth／Worker 工作。正式程序不变。最终候选 `.artifacts/anilist-20261014/publish-anilist-v4`；使用该根 `Start-IsolatedTest.ps1 -PublishName publish-anilist-v4 -RunName <新的名字>` 启动独立 Test，`Control.ps1 -Control <本次控制目录> -Action exit` 正常退出。该辅助入口只在忽略的产物目录中，固定 Test 及新控制输出，不是产品第二套导航或授权系统。

实现位置：Application 的 `AniList.cs`／`AniListService.cs`；Infrastructure 的官方 `AniListProvider`、全局限流协调和明确外链提取；Windows 的 `IsolatedAniListCache` 复用 Profile pins 与原子持久化；Presentation 的 `AniListDetailsViewModel` 保护取消／迟到响应和内部历史；WinUI 的 `AniListDetailView` 接入原书架／目录媒体主机。媒体和确认数据隔离于游戏书架；图片仅规范的 `s4.anilist.co/file/anilistcdn/` 使用原公共池，没有放宽任意图片地址的 DNS 检查。不要把新 Provider 当作追番列表、云端书架或全站镜像入口。

相关短回归用固定 SDK10.0.401运行 `test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore -p:Platform=x64 -p:UseSharedCompilation=true --filter "FullyQualifiedName~AniList|FullyQualifiedName~Bangumi|FullyQualifiedName~MusicBrainz|FullyQualifiedName~RelatedWorks|FullyQualifiedName~RelatedGameIdentity|FullyQualifiedName~KunGalRelated|FullyQualifiedName~ShellNavigation|FullyQualifiedName~InstallationNavigation|FullyQualifiedName~NextMoeOAuth"`。最终468／0／0、WinUI publish通过；真实API、漫画／小说格式探针和窗口截图独立于模拟测试记录，不以测试模拟代替实际连接。证据根 `.artifacts/anilist-20261014/`，功能、授权边界、条款判断、缓存时效及未覆盖项见原BRIEF顶部。

## Bangumi Workers 当前开发与部署门禁（2026-10-14）

用户明确批准本机替换后，最终 `publish-recovery-message-fixed` 已安装到原正式目录，版本1.1.0保持。复用 `package-native-release.ps1`／`package-native-installer.ps1`，不重复产品构建或长测试；658份payload核验、GUI入口校验及普通正式窗口启动通过。证据 `.artifacts/bangumi-worker-live-20261014/local-install/`，回退副本在仓库外 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261014-135146`。安装期间1957份正式资料及数量不变；普通启动后书架／备份／manifest不变，正常运行会自行更新运行资料。没有Computer Use、启动游戏、密钥解密或Test凭据迁移；如原正式Profile未连接OAuth，需要在设置首次连接。下面未安装及禁止正式资料的描述保留为安装授权之前的开发验证边界；此次授权不允许后续自动发布或跨Profile复制令牌。

后续“进行下一阶段”及官方授权已完成：现有Worker当前enabled/ready=true，版本 `04018f23-595e-4a49-8754-88a896f117e5`，运行时变量通过 `wrangler deploy --keep-vars --var BANGUMI_AUTH_ENABLED:true` 开启，源配置仍保留首次安全默认false。后续部署如需继续开启，应在已授权范围内显式带true，不能误把源默认false当成当前远程状态。Worker密钥不读取／修改，未切换计费方案。当前真人授权只在 `.artifacts/bangumi-worker-live-20261014/test-profile`，不是正式Profile；该目录不得打包、提交或复制到Production。

真人联调用 `.artifacts/bangumi-worker-live-20261014/runner-source*` 的小型本地诊断入口，直接引用已构建产品程序集，要求 `THE_ORDER_BANGUMI_LIVE_ALLOWED=1`、固定Test路径和限定操作；请求只允许原Worker及api.bgm.tv，不打印令牌／请求体／授权URL。不作为第二套登录系统或产品入口。初次login使用原Windows浏览器并由用户同意；后续verify、subject、refresh、cancel、disconnect-restore、failure-recovery不需要额外浏览器操作。恢复断开测试仅作用于同一个锁定Test Profile。v4引用最终 `publish-recovery-message-fixed`；前版及失败／整改前提示记录保持。离线与503通过本地Handler模拟，恢复阶段使用真实上游刷新，不关闭生产Worker。

最终344项相关客户端测试通过；新增过期OAuth遇到临时失败仍自动续期、手动无效令牌仍要求授权等文案回归。真人刷新通过，没有修改期限或等待七天自然到期。实际WinUI重启／深浅主题／窄窗检查及Test库哈希在本轮回执中，最终候选 `publish-recovery-message-fixed` 未安装。不要为复验直接双击裸EXE；需显式Test参数并等待正常启动恢复后导航。数据保护和未验范围见原BRIEF顶部；下面部署关闭及真人待确认描述为早期阶段记录。

继续Argon当前未发布源码，保留既有dirty；本轮源码未提交、未推送或安装。TypeScript服务位于 `services/bangumi-auth-worker/`，旧ASP.NET Broker仅保留为参考／旧测试。ClientSecret始终由既有Cloudflare Secret提供，客户端只有固定第一方HTTPS根地址。执行本地验证前不需要Cloudflare登录，不读取Secret或Production Profile；首次生产代码部署和真人授权分别以用户本轮确认作为门禁。用户已经确认旧Secret撤销并更新，值没有被代理读取。

从服务目录按锁安装 `npm ci --ignore-scripts`，运行 `npm run typecheck`、`npm run check:deployment`、`npm test`。`npm run build` 是Wrangler **dry-run**，输出到本轮证据根，不上传或创建远程资源。独立Worker锁住Wrangler4.150.0／Miniflare4.20260730.0，兼容日期固定2026-08-01，避免本机workerd不支持当前日日期；不升级根项目依赖。本地测试通过异步outboundService拦截每一个出站请求，没有真实网络回退；mock与fixture入口不在生产bundle中。

C#相关短回归：固定.NET10.0.401 `test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore -p:Platform=x64 -p:UseSharedCompilation=true --filter "FullyQualifiedName~Bangumi|FullyQualifiedName~MusicBrainz|FullyQualifiedName~NextMoeOAuth"`，输出TRX到 `.artifacts/bangumi-worker-20261014/tests`。独立端到端在Worker目录设置 `THE_ORDER_TEST_DOTNET` 为现有SDKexe，运行 `node test/run-client-bridge.mjs`；它只监听随机127.0.0.1端口，临时设置 `THE_ORDER_WORKER_FIXTURE_URL` 给C# test进程，运行 `WorkerBrokerIntegrationTests` 后关闭。该Fact在未启动显式本地fixture的普通测试中标记跳过，不能把默认跳过算作闭环通过；本轮专门执行结果1／0／0。测试创建并正常关闭Test Profile，DPAPI只接触合成令牌，不改变真实资料。

唯一生产目标名称 `the-order-bangumi-auth`，无 `--env production` 后缀、无第二Worker。Wrangler `keep_vars=true`保留控制台Client ID，Secret不在vars、源码或包装中，也不执行secret写入／删除。新增绑定为 `AUTH`／`BangumiAuthCoordinator`，migration `bangumi-auth-sqlite-v1` 使用SQLite，免费计划支持、无需购买或创建D1/KV。正式首次部署前还需确认该Worker没有同名冲突绑定、账号为Workers Free，及Wrangler身份可访问原Worker。

用户已明确批准首次部署、保持授权关闭。当前已部署版本 `026ea642-433e-4474-bdbe-d029ea8be5c4`，远程原Worker和AUTH绑定成功；公网health为200、configuration_ready=true、enabled=false、ready=false，关闭门禁返回503。没有更改计费方案、读取Secret或触碰正式Profile。部署CLI使用用户完成的Cloudflare官方授权，仅申请account/user读取及Worker脚本写入权限；Windows保险库辅助包 `@napi-rs/keyring@1.3.0` 是CLI环境组件，不属于产品依赖。部署记录在 `.artifacts/bangumi-worker-20261014/cloudflare-deployment-receipt.json`。后续开启授权或真人联调需要另行许可；本机正式版没有替换。

部署前可运行 `scripts/deploy-approved.ps1 -ValidateOnly` 核对已验收源码哈希与配置，不连接Cloudflare或执行部署。首次授权的代码部署可使用 `scripts/deploy-approved.ps1 -UserApprovedProductionDeployment -UserConfirmedSecretRotation`；两个参数只记录已从用户取得的许可，不能代替许可。Wrangler若提示未登录，由用户执行 `npx wrangler login` 并在浏览器同意。部署固定保持 `BANGUMI_AUTH_ENABLED=false`，`/health`应返回 `enabled=false`及 `configuration_ready=true`，这样可检查控制台配置是否被保留而不读取值。不允许为排错导出Secret。

真人联调获许可后，才将控制台普通变量 `BANGUMI_AUTH_ENABLED`设为字符串 `true`；该步骤不是更改ClientID/Secret。`/health.ready`应为true，再启动独立Test候选，点击连接并由用户完成Bangumi官方浏览器授权；检查重启、续期与确切受限subject ID，不默认打开正式资料。以后再次执行当前Wrangler配置会把此开关恢复false，需要重新按部署门禁启用；暂停时设false，匿名客户端元数据不受影响。不要直接把本轮候选安装到正式版或公开发布。

最终证据及边界见原 `docs/ai/BRIEF.md`顶部。一次性消费与密钥轮换不能消除所有崩溃窗口：已发送但丢失的领取结果、已轮换但未落盘的刷新结果需要重新授权。保留旧本地文件并明示错误，不重试授权码或从云端恢复私人书架。本轮实拍只有未连接设置状态，真人登录状态尚未通过。

## Argon 候选本机替换完成（2026-10-14）

用户“可以替换”授权后，原已验收 `gpt-publish-accepted` 经既有 `package-native-release.ps1`／`package-native-installer.ps1` 包装，安装到原正式目录，版本和安装身份不变。证据根 `.artifacts/argon-pages-20261014/local-install/`；Setup退出0、658份payload匹配、GUI校验通过，安装前后1969份Production文件及数量一致。仓库外回退副本：`C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261014-102127`，包括旧程序与完整正式资料，私有清单不进入源码。

原快捷方式启动普通窗口成功，指定窗口实拍显示36项书架；启动后的核心资料哈希检查见 `post-start-data-summary.json`。没有Computer Use、真实游戏启动或长测试。包装遗漏的中文说明从原权威源码补回，首个失败输出保留，安装器使用新 `installer-v2/`。没有业务代码重建／更改、版本提升、分支合并、GitHub推送或公开发布；下文未部署表述是接受前实验记录，本节为后续部署状态。完整证据及原Gate限制见 `docs/ai/BRIEF.md` 顶部。

## Argon 主页面扩展候选（2026-10-14）

继续使用 `D:/Projects/The-Order-worktrees/argon-details-20261014`／`codex/argon-details-20261014`，扩展基线 `113bfbf`，产品提交 `52f5f39f267239515799e992567f84b708507d85`。最终 UI 产物为 **`.artifacts/argon-pages-20261014/gpt-publish-accepted/`**；它是独立候选，没有部署到正式目录。原当前开发工作区、继承修改及上一轮详情页均已核验保留，综合 Gate 为 CONDITIONAL PASS，完整判断、模型评价与未验范围在原 `docs/ai/BRIEF.md` 顶部。

`.artifacts/argon-pages-20261014/Start-IsolatedTest.ps1` 支持 `-PublishName gpt-publish-accepted -RunName <新的短名字>`。它检查 `test-profile-v2` 的 Test manifest、拒绝复用控制输出目录，设置独立数据根、`PROFILE_KIND=Test`、`FIXTURE_PROFILE=0`、诊断关闭、受限 visual fixture、公开目录与合成更新源，然后启动候选。`Control.ps1 -Control <本轮目录> -Action exit` 通过既有退出流程释放 profile，不强杀。该入口供后续可选评审，不能把未设置隔离环境的 EXE 当作新的正式入口。

GPT 夹具新增动作仅限原 Test／visual 机制：固定合成文件夹扫描但不导入、公开 CLANNAD 搜索、首项选择、设置固定分区滚动与空授权编辑器展开／收起、历史核对及手动编号定位。不可保存凭据、应用建议、启动游戏或接受任意路径。`accepted-source-manifest.json` 保存实际最终源码哈希；`tests/gpt-final.trx` 为415／0／0（17秒）。最终占位图标纠错后独立重建并复拍缺图／选择／搜索，未重复无相关风险的长回归。实际 UI 记录 `after-final/`、`after-accepted/`，9组图片对照 `screenshots-review-final.html`。

Gemini CLI 使用精确模型/HIGH出站校验、精确读写路径与固定 Build／Test／Inspect 命令，不是操作系统沙箱。终端后台；本轮第二整改的第一次 CLI 启动无响应，日志保留，显式空标准输入与 `GEMINI_CLI_NO_RELAUNCH=1` 后重试成功，没有改用其他模型或放宽权限。工具未读写正式私有 profile；授权的正式窗口截图在 `user-input/`，正常程序自身运行写入不作字节未变声明。

## Argon 详情页隔离视觉候选（2026-10-14）

本次候选在 `D:/Projects/The-Order-worktrees/argon-details-20261014`／`codex/argon-details-20261014`，沿用当前未发布原 worktree 的完整源码快照，不是从历史 Electron 主目录重新开发。视觉提交 `642d086e8bd16cf218fed45be57d7e4c6c4aa0c8` 只含 7 个展示文件；继承快照 `22806dcf4c6cde1a236e9223cc5e696d80fdb999` 不应整包集成。最终构建位于 `.artifacts/argon-details-20261014/gpt-publish-final/`，工程及真实 UI 验收范围见原 `docs/ai/BRIEF.md` 顶部。Gate 为 CONDITIONAL PASS，未获合并／安装／发布授权。

复验只使用该证据根内的 `test-profile-v2`，manifest 明确为 Test。`Start-IsolatedTest.ps1` 要求固定生成目录名称、新的 control 目录，设置 `THE_ORDER_NATIVE_DATA_DIR`、`PROFILE_KIND=Test`、`VISUAL_TEST=1`、`DIAGNOSTICS=0`、`FIXTURE_PROFILE=0`、`ONLINE_FIXTURE=0` 与受控更新 fixture 后才启动候选；不能双击原始 EXE，避免默认打开 Production。`Control.ps1` 等待 fixture-ready 和对应 nonce 回执；已有 Test 命令处理导航、主题、尺寸和正常退出。新增 `p1DetailsInspect` 严格要求 Test／visual fixture、清洁详情草稿，允许固定的编辑／结束、顶部／标签／角色滚动、关闭测试提示与固定合成长提示，仅用于检查实际控件；不能保存、执行游戏或读取任意目录。长提示测试修改的只是当前控件显示，随后重启隔离实例获取干净截图。

完整窗口截图使用证据根内 `capture-window.exe`，检查指定 PID 的准确 EXE 路径及 Test manifest，输出也限制在该实验根内；捕获指定 HWND 的实际 Composition，不读取全桌面，不发鼠标／键盘输入。早期 Root RenderTargetBitmap 对 Composition 截图不完整，不作为视觉通过依据。`.artifacts/` 内源码复制清单、模型日志、公开 Test 缓存／图片、截图与构建均不提交。

本机 CLI 实际 PowerShell 位于 Codex bundled runtime，普通 `C:/Program Files/PowerShell/7/pwsh.exe` 不存在；WT 的带空格标题和完整命令需正确引用。Gemini 使用 direct Node CLI、UTF-8 提示词、仅隔离目录信任与工具路径白名单，禁用任意 shell、外部读写和模型回退；用户环境 API Key 只注入进程，不输出／存入文件。alias 在后续工具回合可能解析为 actual-model 名称，需同时配置 `gemini-4-argon` 的 HIGH override。稳定 BeforeModel hook 会省略 thinking 字段，因此另在实际 API 出站请求上核验模型／HIGH；异常请求发送前拒绝。这是工具级隔离，未宣称操作系统沙箱。主要实现与整改日志具有出站 HIGH 证据，Stage B 的部分早期回合不能追溯确认。

构建仍用固定 .NET 10.0.401 SDK，`publish --no-restore -c Release -p:Platform=x64 -p:UseSharedCompilation=true -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false` 输出到新目录。GPT 最终定点测试覆盖目录准备／浏览、关联语义／身份、MusicBrainz、导航及图片可读性／横幅审美／材质策略，TRX `gpt-final.trx` 为 320/0/0；没有进行无关全仓审计或长时间测试。原开发树 827 个文件及 HEAD／分支／index、Test 库／库备份／manifest 均有未变回执；Production 没有被读写或用作默认测试输入。

## 番组计划快捷登录与配套授权服务（2026-10-13）

本轮按用户明确授权安装正式版且禁止 Computer Use。当前没有注册的 Bangumi 应用及授权服务，`BangumiOAuthDefaults.ServiceUrl` 保持空值；主入口正确禁用并显示“尚未开通”，手动授权可用。仅有客户端／服务代码和合成闭环，不是已完成的真人 OAuth 功能或公网部署；配置完成前不得宣称“快捷登录可以使用”。

维护入口及继承分支／HEAD 不变。新增应用层 `BangumiOAuth`／`BangumiOAuthBrowserPolicy`、基础设施 `BangumiOAuthProvider`、Windows 浏览器实现及 `TheOrder.BangumiAuthService`；设置入口首选 OAuth。客户端只向编译指定的 HTTPS 服务发送领取证明／刷新请求，只打开固定 `https://bgm.tv/oauth/authorize`，核对授权 URL 的全部字段及第一方 callback；原 NextMoe 浏览器和凭据不复用。取消／三分钟超时清理会话，失败不替换旧授权；服务器返回的身份还需客户端 `/v0/me` 核验。

`WindowsBangumiCredentialStore` 兼容 v1 手动文件；手动保存／清除仍写 v1，OAuth 才写 v2，并将 access token、refresh token、期限、服务来源与账号作为同一个原子 DPAPI 记录。匿名／授权缓存边界沿用。每分钟在空闲时续期，实际 snapshot 同时检查过期，服务来源变更不发送旧刷新令牌。退出取消并等待正在验证／原子提交的个人请求，再释放资料。

服务启动前必须完成以下外部配置：

1. 在自己的 Bangumi 开发者应用中取得 App ID／App Secret，将回调登记为 `https://<自己的授权域名>/v1/bangumi/callback`。不借用其他项目应用身份。
2. 将服务部署到具有 HTTPS 的自有主机。运行环境配置 `Bangumi__AppId`、`Bangumi__ClientSecret`、`Bangumi__ServiceUrl`；ServiceUrl 必须为域名根路径，如 `https://auth.example.com/`。App Secret 放入主机秘密管理／私有环境，不写源码、前端或普通配置样例。
3. 用 HTTPS 反向代理承接公开入口，服务进程只监听受控的内网／回环地址。只有 HTTP 的公网部署不满足客户端验证规则。还需完成代理日志脱敏（回调含短期授权码／state）、TLS、公网限流、进程守护及真人验收。
4. 服务的 `/health` 可检查启动状态；实际成功／失败／拒绝及刷新须另验。设置 `BangumiOAuthDefaults.ServiceUrl` 为上述第一方地址后重新编译／包装客户端，主按钮才启用；这些公开参数不需要用户填写个人密钥。

配套服务采用 .NET 10 的 ASP.NET Core，无新第三方包；服务本体不进入桌面安装包。可使用仓库固定 SDK `dotnet publish native/TheOrder.BangumiAuthService/TheOrder.BangumiAuthService.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=true --no-restore -o <新的输出目录>` 生成框架依赖产物，部署主机需对应 ASP.NET Core Runtime。客户端沿用原 WinUI publish 参数。不要使用测试中的 HTTP 转发／合成凭据来配置正式服务。

后台接口为 POST `/v1/bangumi/login/start`、`poll`、`cancel`、`refresh` 与 GET `/v1/bangumi/callback`。短会话有独立随机 ID／state，native 私有 verifier 的 S256 摘要绑定领取；state 防止错回调，令牌只领取一次，三分钟超时且最多 128 项。默认出站间隔一秒；请求／响应限制、IP 固定窗口、禁重定向／cookie、不记录响应或秘密、no-store 与固定上游共同约束网络。该领取证明是中继协议，并非 Bangumi API 已支持 PKCE。服务不建立用户数据库，也不预建其他 Provider 框架。

产物 `.artifacts/bangumi-quick-login-20261013/`：`before-manifest.json` 仅保存 11 份相关原文件；`publish-final` 为桌面构建、`service-publish` 为未部署服务；`tests/quick-login-related-final.trx` 为 389/0/0，执行四秒。真实本地 Kestrel Host 和合成账号覆盖授权／callback／领取／续期，全程无 Computer Use、真实账号或 UI 自动化；原音乐、关联、拒绝缓存、持久化及导航测试保留。初次编译出现服务全局 Program 与既有性能 Program 名称冲突，已改为服务命名空间内入口，原失败日志保留。依赖锁文件仅增加新项目关系，既有外部包版本及哈希不变。

用户已授权本机安装：检测原实例退出后，CLI 校验复制旧程序及 1916 份 Production 资料到外部 `TheOrder-Local-Rollback/20261013-235525`，不解密秘密，不导出私人清单到仓库。新 Setup 退出 0，658 份 payload、GUI 入口与原快捷方式校验通过，安装期间资料全部未变；回执在 `local-install/`。保持原分支／HEAD／index、版本 1.1.0 与继承修改，没有 GitHub／发布操作。源码与模拟验证已完成，实际快捷登录仍等待注册和部署。

## 番组计划授权本机应用（2026-10-13）

用户后续明确授权安装新候选到正式版。复用 `.artifacts/bangumi-authorization-20261013/publish-v1`，核对 23 份任务文件与已测试程序集后，使用既有 release／installer 包装流程生成 `local-install/release-package` 和 `local-install/installer`。本轮不重新构建应用或重跑无关测试；已有 `authorization-related-v3.trx` 为 360/0/0。生成摘要的测试计数已修正为显式数字，原 TRX 与早期失败日志保留。

桌面组件可控制 The Order 窗口但不能定位系统托盘，故用户正常退出旧实例后继续。先校验复制 666 份旧程序及 1838 份 Production 文件到仓库外 `TheOrder-Local-Rollback/20261013-225114`；私人清单不写入仓库。新 Setup 使用原安装身份、目录与静默参数安装，退出 0，658 份 payload、额外入口／身份、原快捷方式及安装入口校验通过。安装期间全部 1838 份资料哈希未变。

原快捷方式启动普通 Production／诊断关闭窗口，实际查看 36 部书架作品和新番组计划授权区；未点击授权、填写凭据或改变设置。启动后的 17 份非锁顶层文件中 15 份未变，仅 `native-nextmoe-auth.dpapi` 与运行日志发生正常启动更新；没有读取其内容，运行中的 writer lock 不做哈希。截图保存在外部回退目录 `ui/installed-bangumi-settings.jpg`，部署回执／简要 UI 证据在 `local-install/`。HEAD、分支与 index 保留；源码仅补写这两份原交接文档，没有提交、推送、合并、发布或版本升级。

## 番组计划个人访问令牌（2026-10-13）

新增 `BangumiAuthorization`／`BangumiAccessContext`、`BangumiAccountProvider`、`WindowsBangumiCredentialStore`、`BangumiSettingsViewModel` 和原设置页 partial。固定官方 PAT 页面 `https://next.bgm.tv/demo/access-token`；用户粘贴后先通过 `/v0/me` 验证，成功才原子保存到 `native-bangumi-key.dpapi`，读写绑定当前资料身份并复用路径固定／原子文件机制。取消发生在替换前保留旧值；替换完成后仍发布对应内存状态。明文仅在应用进程内用于受限请求，不进入设置 JSON、库、错误、快照 ToString 或日志。

现有 Bangumi Providers 接收同一可选 access context，匿名构造与旧测试接口兼容；请求本身由快照检查固定 HTTPS origin／GET／许可 subject 路径，再附加 Bearer。共享 PublicArtworkNetwork 仍禁用 cookies／重定向，NextMoe、VNDB、MusicBrainz 和图片不接触该令牌。CacheKey 的 anonymous 值保持；新增可选 AccessScope 将授权缓存隔离到令牌 SHA256 指纹。Root、目录与发现页的关联身份同步带入该 scope，切换授权取消旧响应和视图内容，保留游戏／个人库格式与原导航。明确拒绝不返回旧缓存作为成功读取；七天以内的临时离线、超时、限流或服务错误可降级。

输出 `.artifacts/bangumi-authorization-20261013/`。最终 `tests/authorization-related-v3.trx` 360/0/0，测试执行两秒；无全仓审计或长任务。最早 User-Agent 非标准 product 写法导致请求前失败并取消该次回归；随后修正合法格式，旧品牌断言和拒绝缓存降级的旧测试按实际行为调整，保留早期日志。WinUI x64／NativeProductionCandidate／禁用多语言 publish 和原 RC 包装成功。实际隔离设置页布局及正常退出已核验；测试不访问 Production 或真实令牌。`Start-Bangumi-Test.cmd` 默认诊断关闭；真实授权、受限条目完整响应及 Win10 实机尚未验收。不要自动安装新授权候选、推送或发布。

## MusicBrainz 本机应用（2026-10-13）

用户已授权将最新候选应用到原正式安装。复用 `musicbrainz-20261013/publish-v5`，先核对 26 份任务源码检查点和 19 份 native 产物，再由原 `package-native-release.ps1`／`package-native-installer.ps1` 生成 `.artifacts/musicbrainz-promotion-20261013/release-package` 和 `installer`，未重新编译或重跑无关审计。安装前确认没有 The Order 进程；回退程序及资料只复制到仓库外 `TheOrder-Local-Rollback/20261013-212031`，不解密凭据、不把私人清单写入仓库。

新 Setup 以 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` 升级已核验原安装目录，保留桌面入口，安装退出 0。安装前后 1790 份资料哈希一致，658 份 payload、额外 GUI 入口／身份和 `TheOrder.exe --validate-only` 通过。原快捷方式启动 Production 普通窗口，实际渲染和响应确认；启动后的运行 writer lock 正常占用，不在运行时读取它做哈希。17 份其他顶层文件中 15 份未变，仅登录伴随文件和运行日志哈希变化，未读取秘密内容；部署回执保存在本轮输出目录。原分支、HEAD 和 index 保留，只补写原交接文档，无 GitHub／公开发布动作。

## MusicBrainz 与音乐页（2026-10-13）

当前未发布源码是唯一基线，原分支与版本 1.1.0 保持不变。应用／基础设施／展示层分别由 `MusicBrainz.cs`、`MusicBrainzService`、独立 `MusicBrainzProvider`（含 Mapping／Covers／SearchQueries）、`MusicAlbumViewModel` 和 `MusicBrainzAlbumView` 组成。既有 `RelatedMediaDetailView` 只在 Music 类型下委派新视图；书架、嵌套 KunGal 与发现页通过原内部媒体宿主配置同一单例 reader。初始化或进入游戏详情不触发音乐请求。

Bangumi Subject 的可选 `MusicFacts` 仅用于公开发行事实，读取有界 infobox 中的商品编号／品番、艺术家、厂牌、碟数、条码与明确的类型化 MusicBrainz URL。不会把正文 UUID、标题或单曲 Recording 编号当专辑发行版。匹配检索与自动选择独立；原始标题和关系不因检索变体而改写。最终发行版 lookup 再核验编号、碟数与曲目结构。手动查询不能绕过预览与确认，确认指纹包含原 Bangumi 编号及发行事实。

`PublicArtworkNetwork` 继续与带 NextMoe 鉴权的 transport 分开。单例／静态 MusicBrainz 协调器串行等待单调时钟 1.1 秒间隔，含重试；429／503／5xx 采用有限重试并遵守 Retry-After，35 秒总预算。JSON 8 MiB、25 候选、最多 50 碟／5000 曲目。Cover Art Archive 的 JSON 和图片仅接受相同发行版路径的 HTTPS archive.org 或明确 ia/dn 数字 CDN，已核验实际 ca.archive.org 路由；其他原图片源仍禁止重定向。

`IsolatedMusicBrainzCache` 位于当前 profile 的 `native-musicbrainz-v1`，不属于 `library.json` 或备份迁移。128 文件／64 MiB／单条 4 MiB，profile/store 身份、payload 哈希、目录钉住、原子写入；未知／损坏／未来文件保留。检索一小时、专辑一天新鲜，临时故障最多回退七天旧资料；拒绝／移除结果保存失效标记，防止重启后复活。检索策略版本独立于确认指纹；确认可跨进程保留。生成旧请求不能覆盖新缓存，UI 代际及取消拒绝迟到详情／预览／确认。

音乐页全局 ScrollViewer＋各碟 ItemsRepeater，没有碟内滚动框；Title／Artist／Credits 用原 TagFlowPanel 自然换行。普通行实测约 33 DIP，窄窗口长曲目自动两行。页面打开时等待测量再恢复顶端，用户滚动／键盘／点击中止该恢复；返回游戏继续由原宿主恢复原状态。

短回归过滤 `MusicBrainz|MusicAlbum|Bangumi|RelatedWorksProjection|RelatedGameIdentity|StartupNavigation|KunGalRelatedWorks|CatalogPresentation|RemoteCover|ArtworkRouting`，每项使用 `FullyQualifiedName~`；`tests/music-related-delivery.trx` 为 367/0/0。原 x64 publish 参数及原包装脚本生成 `.artifacts/musicbrainz-20261013/package-v5`，无依赖升级。普通入口 `Start-Music-Test.cmd` 默认关闭诊断、两条虚构书架。真实 API／UI 与合成故障用例分别记录于 `live/`、`ui-evidence/`、TRX；已核验实际三碟 56 曲、标题有前缀／错字的四曲单曲发行、发行版选择、缓存重启、明暗窄窗与整页滚轮。最终清单／本地增量提交／原工作区保护记录在 `checkpoint/`。

没有访问 Production、启动真实游戏、安装、合并、推送或发布；Win10 实机、长稳和不同 MusicBrainz 出站客户端的合计 IP 请求速率未实测。现有来源的缺字段与曲目译名版需要保守确认；未来 VGMdb 可另建 Provider，当前不引入通用音乐管理数据库。

## 多层关联与来源呈现（2026-10-13）

当前维护源码和继承边界见 [BRIEF](docs/ai/BRIEF.md) 顶部。本轮仅保存 10 份相关文件的开始状态；新增 `CatalogBrowserView.Related.cs`，没有全仓审计或源码提交。产物 `.artifacts/related-depth-20261013/`，最终 `package-v2`／`Start-Related-Test.cmd` 指向本轮新建的两条合成 Test 资料。早期候选和证据保留。

`BangumiRelationIdentityResolver.TryCreateCatalog` 复用原有 refs 校验，使用 `catalog:作品编号` 缓存命名和浏览实例／请求代际生命周期；不需要伪造书架条目，不接受个人混合来源替代 NextMoe 基础层。`CatalogBrowserView.Related` 在每个 Work 页复用统一关联组件及媒体详情，显式注入原 reader、语义 reader 和编号 reader；身份谓词绑定当前作品、代际、鉴权和媒体可见性。旧 `CatalogContentView` 的大关联卡在这些 Work 页关闭，其他实体页的相关游戏浏览保留。

导航历史只保存轻量展示偏好及滚动值；重建视图时复用现有缓存，待测量／关系读取完成后恢复位置，用户输入取消恢复。媒体覆盖层保留原目录视图；返回恢复筛选、来源状态和焦点。视图销毁取消请求并释放回调；壳层退出同时释放书架和发现页的组件。原来源证据不丢弃；第二层展开才构建详细文本控件。

263 项相关短测试通过，过滤 `Bangumi|RelatedWorksProjection|RelatedGameIdentity|StartupNavigation|KunGalRelatedWorks|CatalogPresentation`（每项前缀 `FullyQualifiedName~`），TRX 为 `tests/related-depth.trx`。最终 publish 使用原 x64、`NativeProductionCandidate=true`、`NativeMultilingualEnabled=false`、`UseSharedCompilation=true` 参数，无依赖升级。实际桌面和受限 Test 控制检查多层游戏、内部音乐、逐层返回、缺编号降级、来源分层及小窗口明暗布局；诊断窗口经正常退出，书架文件哈希不变。最终普通入口不启用诊断。没有 Production、安装、发布、长稳或无关回归操作。

## KunGal 详情优先与返回修复（2026-10-13）

当前增量入口不变。本轮 `.artifacts/related-preference-20261013/baseline/` 仅保存改动范围内的开始状态；新增 `NextMoeCatalog.RelatedIdentities.cs` 和 `RelatedGameIdentityTests.cs`。`RelatedGameIdentity` 为展示层编号证据，Reader 使用原 CatalogTransport 的鉴权代际、限速、大小、超时、取消与来源边界；一次最多 100 条、UI 最多 64 个当前已有的 KunGal 关系身份。API 只读 refs，不按名称搜索，也不逐项加载完整详情。Reader 显式由实际 catalog 注入，目录浏览装饰器不负责提供身份映射。

`RelatedWorksProjection` 验证 KunGal claim、Bangumi 游戏类型和一一对应编号后，保留 KunGal 卡片并附上 Bangumi 原始证据；原始两组数据不删除。相同名称、冲突编号、跨媒体或多个 KunGal 身份指向同一 Bangumi 身份均不合并。View 以作品快照和代际拒绝迟到结果，离开／取消停止请求；手动刷新可重新核验，失败在折叠来源区提示并保留原卡片。五个筛选按钮显式使用 Default/Accent 样式及相同圆角。

文件页返回通过 `ShellNavigationPolicy.ParentGameId` 选择 `FilesViewModel.GameId`，不依赖已被清空的 `WorkspaceViewModel.SelectedId`。相关真实导航回归覆盖详情→文件页→原详情；未保存安装草稿仍走原确认流程。最终短回归 `tests/targeted-final.trx` 为 287 通过／0 失败／0 跳过。WinUI 采用原发布参数包装为 `package-v3`，普通入口 `Start-Related-Test.cmd` 指向本轮独立 `test-profile`。最终实际结果及截图见 `validation-summary.json`。未进行 Production 操作、版本升级或公开发布。

## 统一关联与内部媒体浏览（2026-10-13）

当前源码及继承状态见 [BRIEF](docs/ai/BRIEF.md) 顶部。此轮仅维护源码与独立 Test，不安装或访问 Production。产物为 `.artifacts/related-experience-20261013/`；`baseline-manifest.json` 核验开始时 784 份文件，`checkpoint/manifest.json` 记录本轮差异及新增文件，HEAD 不变。不可把整个继承快照当作本轮原创提交。

`RelatedWorksProjection` 统一展示模型，保留 `nextmoe:ID` / `bangumi:ID`、媒体类别、原始关系、来源和证据；仅精确身份去重。`CatalogEntityRef.RelationEvidence` 为展示缓存的可选字段，`CatalogRelatedWorks.Schema=2` 触发旧展示缓存重新读取，不是个人书架迁移。`BangumiRelationsProvider.Semantics` 复用原 HTTP 请求通道和限速，对最多八个粗粒度动画请求反向关系，只有来源与目标编号、游戏媒体类型一致才接受；一次共享 25 秒预算、有限内存缓存，失败保守展示，不递归关系图。

`BangumiSubjectDetailsService` / `BangumiSubjectDetailsProvider` / `BangumiSubjectDetailsViewModel` 分离匿名读取、缓存、取消与界面时效。固定 subject GET，检查返回编号、媒体类型及匿名可访问性；25 秒预算、2 MiB 响应上限。内存缓存最多 64 项，一小时新鲜、最多七天旧值仅用于临时网络故障；拒绝访问、私密内容或身份错误不回退旧内容。新增事实不写入个人库。

`BangumiRelatedView` 保留旧类名但承载统一关联入口；`RelatedMediaDetailView` 复用当前 DetailsPage 宿主。隐藏原详情保留滚动和草稿，壳层返回先退出媒体详情，再处理原导航；恢复未完成的关系细分。`CatalogBrowserView` 的 UI 层 `CatalogIncomingRelation` 和历史保存入站关系、原始证据及滚动位置，不构造新的全局导航或数据库。来源链接继续受固定来源与现有外链校验约束。

使用固定 `.artifacts/native-phase1/tools/dotnet/dotnet.exe`，顺序运行 Release test 与 WinUI publish；本机 `UseSharedCompilation=true`。测试过滤 `Bangumi|RelatedWorksProjection|KunGalRelatedWorks|ShellNavigation|CatalogPresentation`（每项完整前缀 `FullyQualifiedName~`），最终 `tests/related-delivery.trx` 为 236/0/0。发布构建使用 x64、`NativeProductionCandidate=true`、`NativeMultilingualEnabled=false`，然后原 `package-native-rc.ps1` 包装到 `package-v5`。`Start-Related-Test.cmd` 固定该包与 `related-test-profile`，三条合成书架条目、不含凭据，默认关闭诊断。通过包内 launcher 校验后启动，不使用裸 EXE。

实际网络证据 `relation-semantics-live.json`；受限 Test 控制与桌面截图在 `ui-evidence/`，最终验证摘要 `validation-summary.json`。诊断入口只接受既有页面、当前关系项和有限操作，不接受任意执行代码／路径。测试使用正常匿名 API；离线、超时、取消和过期缓存由确定性回归覆盖，不冒充真实断网或长期压力验收。详情缓存不跨重启持久化。没有重跑无关全仓审计、长稳、真实游戏或正式数据操作。

## 本机导入体验更新与 Bangumi 公开关联（2026-10-13）

本轮输出根为 `.artifacts/ux-promotion-acgn-20261013/`，权威源码仍在 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`。保持分支 `codex/library-ui-refinement`、HEAD `7facc502fde52532518171384cd77b45c9a60c9c`、1.1.0 版本与继承 dirty。`baseline/`／`baseline-manifest.json` 保存开始时 776 份文件及哈希；`checkpoint-data-ui/` 保存本轮数据层与界面层的 17 份代码及 manifest。它们是恢复检查点，不是新的开发入口，也不应把继承快照作为本轮原创修改提交。

用户先授权将上一轮导入体验 `import-file-ux-20261013/package-v3`（含复选框修正）安装到本机。该步骤已完成：Setup 退出 0，核验 658 份 payload，安装前后 1790 份 Production 文件哈希相同，原桌面快捷方式启动普通窗口成功。准确路径及回执见 `installation-receipt.json`／`installed-launch.json`；回退副本固定在仓库外 `C:/Users/MUSK/AppData/Local/TheOrder-Local-Rollback/20261013-111856`。后续 ACGN 开发仅生成独立 Test 候选，不能把这次已用完的安装授权延伸为安装 ACGN 或公开发布的授权。

应用层入口 `native/TheOrder.Application/BangumiRelations.cs` 定义来源身份、四种关联、校验与读取服务。身份来自 `MetadataMix.Base` 或未混合的 NextMoe 基础资料，要求全部 Bangumi 引用一致；人工补充层不能替代基础来源。持久键含 schema、本地作品、NextMoe 编号、Bangumi 编号与来源指纹；进程生命周期只用于拦截旧请求，不进入磁盘键，因此同一明确关联可以跨重启命中。缓存新鲜期一小时，强制刷新及错误保留旧值的状态分别呈现。

基础设施入口 `native/TheOrder.Infrastructure/Online/BangumiRelationsProvider.cs` 先读取原作并核验其为游戏，再读取直接关联。固定 HTTPS Bangumi 端点、匿名访问、禁用 cookie／重定向，不访问认证服务；排队、间隔、两次请求、读取及最多两次重试共用 25 秒预算。单响应上限 2 MiB／1000 行，映射最多 256 项，仅保留动画、音乐、书籍、游戏。401／403／404、429、服务错误、超时、取消、畸形响应与真实空列表有不同结果，不通过放宽来源或匿名权限补救。封面通过现有图片管线的 Bangumi 白名单与专用 key 注册；网络接线在 `NetworkOwnership.cs`／`RemoteCoverSource.cs`／`Startup/Composition.cs`。

`native/TheOrder.Platform.Windows/IsolatedBangumiRelationsCache.cs` 使用当前 `NativeProfileStore` 的 ProfileId／StoreId、完整持久身份及 SHA-256 封套；没有 native 资料租约时不创建无身份缓存。伴随目录 `native-bangumi-relations-v1` 有 128 项、32 MiB 总量和 1 MiB 内容上限，仅回收已验证的本资料记录，保留损坏、外来及未来格式。目录链固定后使用既有原子替换，替换前取消保留旧值，替换后完成新值；不迁移 schema，不写主库，不保存账号信息。回退程序只忽略该派生目录，无需用旧资料覆盖新编辑。

界面入口为 `native/TheOrder.Presentation/BangumiRelatedViewModel.cs`、`native/TheOrder.WinUI/Views/BangumiRelatedView.cs` 及现有详情页。展开触发读取，默认动画分类，按需创建当前分类前 12 项图片控件，再由“更多”增加；切分类、折叠、卸载释放当前卡片。请求代次与作品生命周期共同阻止 A→B→A、关联变更、取消后的迟到结果。展示公开关系不改变识别、混合资料或书架模型，也未引入递归关系图、动漫独立管理、播放或收藏同步。

最新定向测试过滤 `FullyQualifiedName~Bangumi`，`tests/bangumi-final.trx` 为 122/0/0；早期 `tests/focused.trx` 的 210 项中包含 91 项既有短回归，其余与 Bangumi 重叠，不能直接相加。既有过滤为 `FilesPresentationTests|P1SourcesTests|P1SourcesUsabilityTests|LaunchSetupTests|P1LaunchTests|OfflineFilesystemTests|LibraryBackupPresentationTests`。固定 SDK 的 Release／x64 构建已生成 `acgn-publish-final`，再经现有 `native/scripts/package-native-rc.ps1` 生成 `acgn-package-final`；日志分别为 `acgn-publish-final.log`、`acgn-package-final.log`。不重跑长稳、Win10 实机或全量历史基线。

`live-nextmoe-mapping.json` 记录匿名公开 NextMoe 查得 CLANNAD 的作品编号 86、明确 Bangumi 引用 13；`live-api.json` 记录本轮实际 provider 取得 30 项关系（动画 6、音乐 14、书籍 4、游戏 6）。早期 seed 把 NextMoe 写成 13，不是可靠映射样例；初始 `acgn-test-profile` 的占位封面使用合成传输，也不是网络封面失败证据。最终验证固定到三条虚构条目的 `acgn-canonical-test-profile`，其中 CLANNAD 使用已验证的 86→13，采用正常匿名公开传输，不使用真实用户资料或账号。桌面已观察到六张真实动画封面，并点击 CLANNAD 卡片打开 canonical Bangumi 51 页面；浅色 707×773 DIP 窗口书籍卡片三列换行，实际截图为 `ui-evidence/related-books-light-narrow.png`。音乐首次 12 项、更多后 14 项，`canonical-music-ready.json` 记录 12 张封面加载完成。缺 ID／冲突分别拒绝读取。`canonical-exit.json` 记录正常退出与书架哈希不变，`ui-evidence/restart-cache.json` 记录重启 Ready／30 项／FromCache=true／Stale=false。更多操作键盘焦点移交已实现，但未由实际焦点观察确认，不作为全键盘验收。

交付入口为本轮根目录的 `Start-ACGN-Test.cmd`，绑定 `acgn-package-final` 与 `acgn-canonical-test-profile`；`ordinary-test-launch.json` 记录包内正式 Test 校验入口成功启动、diagnostics=false。所有本轮诊断实例已正常退出，普通隔离 Test 保留可用。`checkpoint-final/manifest.json` 保存最终 17 份功能代码与两份文档，`source-preservation.json` 核验未触及本任务以外的基线文件。候选不安装正式版、不改公开发行、不提交／推送；真人 NSFW 权限、真实书架、游戏启动、动漫独立管理和长期运行不在本轮验收内。

## 导入／文件操作语义与选择状态（2026-10-13）

本轮源代码仍在 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`；输出 `.artifacts/import-file-ux-20261013/`。继承修改保留，修改前相关文件分存 `before-root/`、`before-import/`、`before-installation/`、`before-sources/`。本机日用安装仍为上一轮 `local-promotion-20261013`，新候选 `package-v3` 仅通过本轮 `Start-The-Order-Test.cmd` 打开合成 Test，不更新正式资料或版本号。

`FilesViewModel` 保存当前可导入选择、按钮适用性及结果状态，新的扫描／发现清选择，提交失败保留选择，成功才清除。`ImportPage` 初始化及状态变化同步按钮；`ListViewItem` 自定义样式必须继承 `DefaultListViewItemStyle`，否则会丢失 WinUI 多选框选中底色。未另写固定颜色模板，明暗／悬停／按下／禁用及高对比度继续用框架资源。

安装页复用既有草稿与导航协调器：当前首选或首行默认选中；存档、辅助工具和高级参数采用折叠区；目录查找与文件选择返回时检查原草稿身份，查找还检查原文件夹，避免旧结果回填到新编辑。自动发现手动扫描使用 `LibrarySourcesService.ScanAsync(waitForTurn: true)`，以取消／生命周期链接等待互斥；后台扫描保持忙时跳过。来源对话框重新生成后更新按钮可用性；恢复忽略后同时恢复导入和忽略操作。

短测试过滤 `FilesPresentationTests|P1SourcesTests|P1SourcesUsabilityTests|LaunchSetupTests|P1LaunchTests|OfflineFilesystemTests|LibraryBackupPresentationTests`，`tests/focused-final.trx` 为 91/0/0，运行 21 秒。新增五项覆盖选择／失败／重扫及手动扫描等待／取消，不重复跑全量基线。WinUI 最终输出 `publish-v3`，之后调用 `package-native-rc.ps1` 到新 `package-v3`。本轮使用固定 SDK 的 `-p:UseSharedCompilation=true --no-restore`：第一次独立 csc 编译进程处于 Suspended，终止已核验的本次编译进程后改用共享编译成功；没有恢复被挂起程序或修改系统防护，原因未确认，原日志保留。

受限 Test 的新选择／按钮操作同时登记于 `MainWindow.Visual.cs` 分派及 `MainWindow.P1Fixture.cs`；仅允许既有合成 Test 的可导入项索引与白名单按钮。`validate-ui.ps1` 验证实际控件回执：v2 的完整交互检查与 v3 唯一后续产品修改（首帧按钮初始化）的复验分开记录。实际图像可确认复选框选中色与局部布局，但系统材质层不在 RenderTargetBitmap 内。没有以这些证据宣称通用鼠标／原生文件选择器、真实游戏生态或长期运行通过。

## 短审后本机部署（2026-10-13）

用户已授权将 P1 测试成果替换本机正式程序，且要求避免长时间测试。本轮输出 `.artifacts/local-promotion-20261013/`：修改前小范围文件在 `before/`、`before-data/`，继承状态保留；`source-build-manifest.json` 绑定 776 份构建时源码，后续仅补写本文及 BRIEF 的部署结果。仍维护原 worktree／分支／HEAD 和 1.1.0 版本，不推送／发布。

修复点：`LibrarySourcesService.IgnoreAsync` 先验证可见选择，再按目录物理身份跨全部重叠来源保存各自键，原子失败不更新内存；`OpenSourcesAsync.SourceToggle` 捕获请求值、禁用控件，在 finally 从提交态同步并抑制递归事件；`MetadataMixing.Merge` 在组合双方来源层前验证目标作品身份，不确定或矛盾的跨作品层阻止整次事务；`package-native-installer.ps1` 在创建输出／查询编译器前校验正式入口、release identity 及 launcher 哈希，复制后再校验。

相关验证为 91 项导入／识别／来源／启动／备份退出测试和 32 项资料合并／扩展／同步测试，0 失败／跳过。没有重跑全量、长稳或历史资源基线。实际 Test 用已知合成资料，未启用 Production 诊断控制；配置文件 Read sharing 阻止替换的负向检查证明保存失败后开关回退，随后成功切换并恢复文件哈希。测试实例正常退出，未强杀；菜单非空和闲置识别收起有 XAML 回执。

构建顺序：固定 SDK Release test → `publish`（x64、`NativeProductionCandidate=true`、`NativeMultilingualEnabled=false`、`UseSharedCompilation=false`）→ `package-native-release.ps1` → `package-native-installer.ps1`。RC 缺少 `launch.ps1` 不可直接作为 Setup 输入；旧 `test-native-installer.ps1` 的临时注册／卸载路径不用于本机现有安装。本次只用新的 Setup `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` 对已核验的原安装目录升级，保留桌面快捷方式。

`installation-receipt.json` 核验安装退出 0、658 份正式 payload、额外 GUI 入口／安装身份及 `TheOrder.exe --validate-only`，1772 份正式资料安装前后逐文件不变。仓库外完整回退副本见 `rollback-receipt.json`；私人文件清单只保存在该回退目录内，未解密凭据。`production-startup.json` 记录通过原桌面快捷方式启动安装版、Production 普通入口、诊断关闭和窗口响应；`post-start-data-check.json` 核对启动后书架／身份／界面／材质／识别配置。此为本机短时验证，不是 Win10、长期运行或真实外部工具验收。当前已打开的正式程序可直接使用，历史 Test 包仅保留作证据。

## P1 体验修正与相关验证（2026-10-13）

本轮维护源码仍为 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`。`.artifacts/p1-usability-20261013/before.json` 和 `before/` 保存开始时 773 份源码哈希与文件，继承 dirty 保留。功能及入口见 [BRIEF](docs/ai/BRIEF.md) 顶部；不升级版本、不发布、不操作正式资料或真实游戏。

`ITitleIndex.ContainsReferenceAsync` 复用有界、可取消的索引加载，按完整 Steam 引用精确查询；`WindowsSteamLibraryScanner` 标记候选，`LibrarySourcesService.Candidates` 默认筛选，并允许每个 Steam 库显式显示未确认项目。旧 `native-library-sources-v1.json` 缺少的新可选字段取默认值；`ShowUnconfirmedSteam` 和 `IgnoredCandidates` 与原根目录清单一同绑定当前 ProfileId／StoreId、校验并原子保存。忽略键包含来源、目录物理身份及 Steam ID，不保存游戏内容。忽略提交重新核对扫描快照和路径身份，只有保存成功后更新内存；坏配置、写入失败、取消和目录替换的正反场景均覆盖。最多管理 16 个根目录和 2000 条忽略记录，不放宽原扫描边界。

`FilesViewModel.ReviewDiscovered` 清除旧手动扫描根目录，并仅在来源候选审阅阶段显示忽略操作。书架提示关闭经过 `IgnoreDiscoveredAsync` 持久化，错误保留重试入口；识别栏复用 `HasRecognitionWork`。菜单从卡片／列表根元素的绑定 Id 取得当前条目，生成后再 `ShowAt`，不在 Opening 中依赖尚未设置的 Target。详情和右键启动设置均复用 `InstallationNavigation.OpenAsync`、未保存检查和意图时效保护，创建当前首选安装草稿后定位启动辅助区。

使用固定 SDK，顺序执行针对性 Release 测试和 WinUI publish／包装。过滤 `P1Sources|TitleIndex|FilesPresentation|RecognitionPresentation|P1Launch|P1DirectMetadata` 的最终 `tests/targeted-final.trx` 为 79/0/0。本轮没有重跑全量基线，旧 P1 全量结果只作为历史证据保留。`publish-v2`／`package-v2` 成功，交付入口通过原包内校验器核验 653 份文件；不从裸 EXE 启动。

`fixture-request.ps1` 仅对本轮合成 Test 发送有界、带 nonce 的现有控制请求；新 `LibraryPage.ContextFixture.cs` 复用生产菜单构造，菜单调用仅允许详情／选择／启动设置。发现／忽略／恢复等入口不接受任意路径、代码、认证或 Production 操作。`ui-evidence/` 保存实际 XAML 状态、操作回执及窄窗口图片；根窗口截图缺少系统合成层，不能当作桌面材质像素验收。`exit-owned-test.ps1` 校验完整程序路径、PID、启动时间和合成 Test 身份，等待与托盘相同的正常退出，不强杀；退出前后书架哈希一致。所有诊断实例已正常关闭，交付 `Start-The-Order-Test.cmd` 默认关闭诊断控制。

## 全部 P1 候选与复验（2026-10-13）

维护入口仍为 `D:/Projects/The-Order-worktrees/claude-recognition-rnd/native`，本轮 `.artifacts/p1-completion-20261013/` 保存修改前全源码哈希／继承状态、新候选、失败与最终证据；没有清理／重置继承 dirty、升级版本、发布或访问正式资料。用户授权全部报告 P1，范围及普通入口见 [BRIEF](docs/ai/BRIEF.md)。

主要新增服务：`LibrarySourcesService` 管理最多 16 个显式根目录，有界扫描和选择导入；Windows watcher 以约三秒防抖、四秒检查及十五分钟低频重核，不扫描整盘。Steam 只读取选定库的已安装 appmanifest，不取账号资料。`LibraryExtrasTransactions` 和 `MetadataMixing` 保持原子事务／时效校验，人工账本不改原会话；`PlaytimeStatistics` 保留未知日期。`WindowsLocalResources` 只检查有限深度的常见存档目录和用户选定文档。直接资料 API 固定官方 Bangumi／VNDB、无重定向／Cookie、有界响应／超时／限速、正式作品类型与内容策略核验；不携带 NextMoe 凭据。LE／Steam 使用结构化参数和物理身份核验；DPI 只影响子进程，管理员 EXE 由系统授权，Magpie 打开选定工具。`WindowsGameAudioPlatform` 通过 CoreAudio 仅取得强身份对应的单进程会话，带事件上下文的静音租约不改变系统或其他程序音量。经典 COM Jump List 使用 profile 绑定激活，不创建第二个 writer。

`nativeExtras` 可选扩展版号为 1；原无扩展库继续读取，未知扩展拒绝写入并保存原文。根目录伴随文件为 `native-library-sources-v1.json`，绑定当前资料身份并原子保存。备份仍是书架数据，包含人工时长、存档／工具配置、攻略路径、混合资料层；不复制存档或攻略内容，也不复制登录／根目录配置。不要用旧 writer 重写新增扩展后宣称无损回退，先保留新程序导出的完整书架。

复验使用现有固定 `.artifacts/native-phase1/tools/dotnet/dotnet.exe` 和锁文件，先执行 `test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -c Release -p:Platform=x64 -p:UseSharedCompilation=false`，再顺序 `publish native/TheOrder.WinUI/TheOrder.WinUI.csproj --no-restore -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false -p:UseSharedCompilation=false -o <本轮新目录>`；测试／publish 不并行以免共享 obj。之后调用原 `native/scripts/package-native-rc.ps1` 包装到新目录，使用自带 `launch-test.ps1 -DataDirectory <本轮合成 Test>` 验证全部文件哈希。不能将文件夹中的裸 EXE 作为日用入口。

最终全量 `tests/full-handshake-final.trx` 2753/0/0，专项 `tests/p1-audio.trx` 103/0/0 为其子集。旧 fixture 的报告文件写入竞争和固定睡眠交接时序失败原日志保留，最终夹具等待真实报告／进程观察，不放宽应用规则。新增 `MainWindow.P1Fixture.cs` 扩展既有受限 Test 操作，只允许当前合成书架条目和实际控件，不接受 arbitrary 文件路径／执行代码或认证操作；安装入口复用 `InstallationNavigation.OpenAsync`，不能直接切页面绕过安装加载。退出测试与托盘共用 `App.RequestExitAsync`，不以强杀替代排空。最终包／重启回执及界面边界见本轮 `validation-summary.json`；新对话框 XAML 捕获可验证布局，根窗口捕获缺少系统合成层。通用桌面控制、真实文件选择器、Win10 实机、真实管理员／外部工具／Steam 游戏及长期音频兼容未验收。

最后的窄窗口整理只修改 WinUI XAML 与 Test 证据入口，不涉及上述已全量测试的领域／应用／平台逻辑。交付包 `package-final-5` 重新 publish、包装和哈希核验；存档操作使用既有 `TagFlowPanel` 自然换行，选项保留可访问名称。`p1ScrollInstallation` 只允许合成 Test 当前安装的存档或保存控件，不接受任意选择器；布局回执含相对坐标。遇到同名旧证据时拒绝覆盖，使用新证据名重试。`exit-owned-test.ps1` 同时核验 PID、启动时间、完整 EXE 路径及 Test 身份，等待正常退出并核对书架哈希，不强杀。`Start-The-Order-Test.cmd` 通过 package 自带校验入口运行，默认关闭全部诊断／视觉控制。

## 当前在线目录与托盘退出修正（2026-10-12）

最新候选根 `.artifacts/settings-exit-20261012/`。维护源码中 `LibraryBackupViewModel.DisposeAsync` 同时由 MainWindow.StopAsync 和 NativeHost 的容器清理调用；应共享同一个释放任务，不重复 Cancel 已 Dispose 的 CancellationTokenSource。新请求在停止后不执行，已接受的写入必须排空；不可用强制退出替代必要的会话保存。修复前的三个失败用例与日志保留在 exit-reproduction.log／test-results/exit-reproduction.trx，最终退出相关 35 项通过，在线相关 41 项通过。涉及原会话保存失败、暂缓写入、取消、权限与账号时效保护，全部为合成无窗口回归。

设置在线目录采用 12 DIP 节内距，空状态文本、闲置取消／进度及默认未测试说明不占空间；绑定复用 SettingsPage 的可见性函数，实际连接结果与错误继续显示。编译配置仍为 x64、NativeProductionCandidate=true、NativeMultilingualEnabled=false、UseSharedCompilation=false；固定 SDK 执行 Release test 后 publish 到本轮 fresh publish 目录，再调用原 package-native-rc.ps1。包与生成绑定核对见 verify-delivery.ps1／validation-summary.json，不代表像素或托盘菜单实际验收。

新 Start-The-Order-Test.cmd 绑定全新 12 条虚构 Native Test，代理未执行；只有 seed 工具源码复用上一轮，旧 profile 不复制或覆盖。没有真实游戏、Production、凭据、GUI 自动化、日用安装替换、版本升级或发布。用户正在使用电脑，继续避免打开窗口或控制输入；修复不能改变仍在内存中运行的旧程序，需用户结束旧进程后从新入口核验。

## 当前设置留白修正（2026-10-12）

本轮仅修改 `Views/SettingsPage.xaml`、其 code-behind 的可见性转换及 `TheOrder.Presentation/RecognitionViewModel.cs` 的 `HasRecognitionWork` 属性通知。识别容器和取消按钮同用现有活动状态；空错误／备份提示 Collapsed。资料存储移入原备份组，采用 16 DIP 节内间距。格式、识别／取消算法及现有导航、启动、备份行为不变。

输出 `.artifacts/settings-spacing-20261012/`，保留修改前五份原文件和继承状态。固定 SDK 的 Release 无窗口检查过滤 `FullyQualifiedName~AutomaticRecognitionTests|FullyQualifiedName~LibraryBackupPresentationTests`，41 通过／0 失败／0 跳过，含等待导入、取消、图片准备和备份提示相关流程。随后按既有 x64／NativeProductionCandidate／禁用多语言配置 publish，并用原 `package-native-rc.ps1` 打包。`verify-delivery.ps1` 核对包哈希、生成的实时绑定接线、状态通知及空控件收起结构；不作为实际像素证据。

新入口 `.artifacts/settings-spacing-20261012/Start-The-Order-Test.cmd` 绑定全新合成 Native Test，由用户自行打开。代理不启动窗口、不用 computer use、不触及真实库／凭据／游戏，不替换安装或发布。合成资料工具首次直接 apphost 因未注册全局 .NET 失败，原日志保留；使用原固定 SDK 执行 DLL 后成功，没有安装系统运行时。实际设置视觉和动态显隐仍待核验。

## 当前导航与多引擎排版修正（2026-10-12）

在同一维护工作区上窄修复 `MainWindow.xaml`、新增 `Views/RailNavigationItem.cs`、`Views/TagFlowPanel.cs` 和 `Views/CatalogFactsView.cs`，不升级版本或改动资料。返回键当前位于主内容的固定 44 DIP 工具栏，不再使用标题栏的中间 Content 槽。侧栏继承原生 NavigationViewItem 模板，依据锁定 `microsoft.windowsappsdk.winui/2.3.9/.../generic.xaml` 的 ContentGrid／IconColumn／ContentPresenter 精确修正留白；复用默认原生样式和现有导航事件。未来升级 WinUI 依赖时需重新核验这些模板部件及中心位置，不能直接沿用阴性检查。

`TagFlowPanel.CenterInlineItems` 默认 true，原标签和链接保持中心线；只有横幅事实外层设为 false，并开放到实际信息框宽度，使多个长引擎组换行而非挤成窄列。没有修改元数据、识别、玻璃效果、横幅原图比例或下方回退条件。

本轮输出 `.artifacts/layout-corrections-20261012/`；固定 SDK 用 `dotnet test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~StartupNavigationTests|FullyQualifiedName~BannerAestheticsTests"` 得到 22 通过／0 失败／0 跳过。最后 publish 使用同一 x64／NativeProductionCandidate／无 Multilingual 的既有配置，输出 publish-final，再由原 package-native-rc.ps1 打包。候选仅供用户自行从 `Start-The-Order-Test.cmd` 打开；新建合成 Native Test，不复制任何旧 profile、缓存、设置或密钥。没有 computer use 或真实应用窗口验证，实际渲染仍待手动验收；下方第一批返回位置为历史方案。

## 第一批备份／导航／冷启动候选（2026-10-12）

维护入口为 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`。本轮候选不升级公开版本，不提交／推送、不安装日用更新；用户要求不使用 computer use，所有验证为隔离、无窗口检查。功能、格式兼容性和未验收范围见 BRIEF 顶部。不要复用旧私库、Production 或已删除资料进行这轮验证。

实现入口为 `native/TheOrder.Application/LibraryBackups.cs`、`LibraryBackupService.cs`、`native/TheOrder.Platform.Windows/WindowsLibraryBackupStore.cs`、`native/TheOrder.Presentation/LibraryBackupViewModel.cs`，设置及恢复流程位于 `SettingsPage.Backups.cs`／`MainWindow.Backups.cs`。可移植导出仍为原版书架 JSON；自动快照另加绑定当前 ProfileId／StoreId 的版本 1 封套、SHA-256 与内容摘要，保留最近十份。内部伴随目录 `native-library-backups-v1` 不替换主库或既有 last-good 恢复机制；旧版本忽略该目录。回退程序与主动恢复书架是不同操作。重置／删除资料时只在获准范围处理该目录。

恢复默认添加缺失作品，整份恢复需预览确认且无活跃／待恢复会话；保存并读回验证恢复前的便携快照后才进入主库原子写盘。预览绑定当前 writer 及完整快照，预览后发生个人编辑／会话／其他写入会拒绝旧确认，需重做预览。原子主库提交一旦开始仍不在半途取消；提交成功后界面／旧队列失败必须按已提交处理。恢复暂停自动个人同步，用户可在重新预览云端差异后恢复同步。备份不包含登录凭据、设置、独立自选横幅或游戏内容；原路径不存在的安装仍需用户自行定位。

本轮输出 `.artifacts/first-batch-backup-shell-20261012/`，保存修改前文件与哈希、继承状态、测试日志和独立 publish／package。针对性检查：使用固定 SDK 执行 `dotnet test native/TheOrder.Tests/TheOrder.Tests.csproj --no-restore -c Release`，过滤 `LibraryBackup|StartupNavigation|SessionPersistence|OfflineClient|NativeTransaction|RecognitionLifecycle` 的 FullyQualifiedName。首批相关 141 项与最终新增 34 项有重叠，按 TRX 测试 ID 去重共 144 项，不能相加为 175 项。启动 gate 的异步顺序、失败与并发启动由 `StartupNavigationTests` 验证；不包含真实 XAML 帧或视觉证据。

WinUI 编译／publish 使用 `NativeProductionCandidate=true`、`NativeMultilingualEnabled=false`、Platform=x64，输出到本轮新的 publish 目录，再调用已有 `native/scripts/package-native-rc.ps1` 生成独立自包含 package。最终 `Start-The-Order-Test.cmd` 固定到本轮新的合成 Native Test，清除继承测试环境并验证包哈希；只提供给用户自行打开，代理未运行该入口。侧栏宽度／返回固定槽与首屏加载切换仍须手工核验，不将历史桌面证据套用于本轮。

## 1.1.0 公开发布完成（2026-10-12）

The Order **1.1.0 已公开发布并标记为 Latest**：https://github.com/Ph-Rank/The-Order/releases/tag/v1.1.0 。Release ID `410369204`；标签源码提交 `751da886a1a4e313b465df6f9497e612c46b19d8`，全部 734 份 Git blob、冻结源码 ZIP 与六个公开附件已核对；实际 1.1.0 更新读取探针通过，包括新版本、安装包、块表和主要升级说明。发布未重新编译，未安装、替换日用程序或访问 Production。

本机回执位于 `.artifacts/github-publication-1.1.0-20261012/`：`publication.json`、`public-source-verification.json`、`public-download-verification.json`、`actual-update-feed.json` 与 `closure.json`。维护入口、原 HEAD 和继承未提交成果保留；发布暂存仓库不是开发入口。下方候选的“尚未发布”等表述为各阶段当时的历史状态。1.0.1 首次需手动安装 1.1.0，之后可从软件内更新。个人同步默认关闭；真实云端写入／删除、收藏夹额外权限、Win10 实机、真实安装升级与长稳范围仍未验收，本次发布不把这些未验证项改为通过。

## 1.1.0 GitHub 发布记录（2026-10-12）

用户明确授权将已验收的 1.1.0 上传 GitHub。本次公开入口为 https://github.com/Ph-Rank/The-Order/releases/tag/v1.1.0；安装器、完整对应源码、差量块表、校验与原验证报告一同发布。源码保持当前 native 实现、锁文件和必需数据，公开文档更新下载入口与范围；原开发编号 1.0.2 的真实 OAuth／完整回归按实际版本保留。

真实云端写入／删除、收藏夹额外权限、Win10 实机、真实安装升级和长期稳定性仍未验收。同步默认关闭；双向模式后台本地时长／状态上传仍需手动预览。旧标签／附件与日用程序不变；发布暂存目录不作为维护入口。

## 当前功能版本 1.1.0（原开发编号 1.0.2）

用户已确认改用 1.1.0。维护入口仍为 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`；当前构建、完整对应源码、安装包和验收绑定使用新的 `.artifacts/release-1.1.0-20261012/`，原认证与真人只读证据保留在 `auth-sync-20261011`。复现命令和功能边界见 [1.1.0 BUILD](docs/release/1.1.0/BUILD.md) 及 [认证与隐私](docs/release/1.1.0/AUTHENTICATION.md)。没有公开发布或日用安装授权；旧 AppId、资料身份与格式保持，安装器可从 1.0.1 直接升级，更新比较拒绝降级。下方 1.0.2 记录保留为原开发阶段历史，不使用其中的旧版本打包命令生成当前候选。


## 当前 1.0.2 NextMoe 认证、个人同步与设置候选

维护入口仍为 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`，保留此前 Win10／更新／差量功能和全部 dirty。新证据和候选根 `.artifacts/auth-sync-20261011`，没有公开发布、提交／推送、操作日用安装或真实 Production。最终入口、源码身份、回归结果和未完成 gate 见该目录 `validation-summary.json`；上一轮 `delta-update-20261011/distribution-final` 原样保留为程序回退点，不用旧资料覆盖新编辑。

`NextMoeAuthentication` 复用 OnlineAuthContext 代次，默认匿名或保留旧 Key；`WindowsOAuthLogin` 使用系统浏览器、127.0.0.1:0 与 PKCE；OAuth/个人 API 请求固定官方端点并禁止重定向。当前 Windows 用户 DPAPI 刷新伴随文件、ProfileId/StoreId 绑定、串行轮换标记和原子替换保护未知结果；退出清除本机登录，不假称远程撤销。

`PersonalSyncCoordinator` 与 `PersonalSyncPlanning` 分离读取、三方比较、确认、远端写入和本地事务。同步全部默认关闭，检查点／UUID 待恢复任务／收藏映射／导入汇总按账号和 Client ID 存在独立伴随文件，旧库与 Key 不迁移。评分结构只读；未知 POST 保留同键／同内容，逐项响应检查后才标成功。秒／分钟、不可信增量、completion、删除、作品合并、取消与迟到账号数据有正反测试。暂停自动同步持久保存，无法恢复的旧任务可明确读取现状后结束，再重新预览。

官方替换接口未提供条件更新。受控双向比较、手动确认与单向能力可用，但严格并发无覆盖的全自动双向替换不能承诺；后台双向本地上传改为提示预览，其他设备新数据可安全导入。真人 OAuth 只读闭环已由用户授权后验证（`oauth-real-readonly.json`）；这不等同于真实云端写入或严格全自动双向验收，不直接提出无条件 Release Candidate GO。详细见 [认证与隐私](docs/release/1.0.2/AUTHENTICATION.md)。

构建／测试统一 `-p:Platform=x64`，测试夹具沿实际输出路径寻找同配置子进程。完整历史安全存储测试按现有 npm 锁文件恢复 Electron 44.5.1，只使用测试自己生成的合成秘密和临时目录。常规自动化、Test 原生 UI、真实匿名和真人授权分别记录；禁止在同一 Test 资料上并行启动应用／探针。

真人 OAuth 验收使用新建隔离 Test 和 `THE_ORDER_NATIVE_READ_ONLY_OAUTH_ACCEPTANCE=1`，该入口从程序层阻止所有个人写入，标题标记“只读登录验收”。先由用户授权并正常退出 Test，再用 RecognitionLab `oauth-readonly-smoke` 验证新进程刷新、目录／获准个人只读接口及本机退出；记录仅含状态／数量。合成 `PERSONAL_FIXTURE` 还要求 Test、VISUAL_TEST、DIAGNOSTICS、ONLINE_FIXTURE 同时启用，不能用于 Production 或真人判定。不要读取／导出旧真实 Key、把测试凭据放进源码或尝试真实账号写入。源码许可与第三方许可保持。

## 当前差量更新候选 1.0.2（2026-10-11，包含程序更新与 Win10 适配）

权威源码仍为 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`，本轮输出 `.artifacts/delta-update-20261011`。公开版本仍为 1.0.1；没有发布、提交／推送、安装日用更新或接触正式资料。最新交付包 `distribution-final/The-Order-1.0.2-Setup-x64.exe`（79,209,628 字节），SHA-256 `AE1791B91917D2EB10D451FB00F70ECD724F9AF7B7EFC0257CAB4617F0AE4C4D`；同目录有对应源码、块表和校验摘要。旧候选及失败记录保持。

`AppUpdateBlocks` 定义内容边界、逐块 SHA-256、比较及重建；`TheOrder.UpdateTool` 与客户端共用该格式，不增加 NuGet 依赖。`GitHubUpdateFeed.Differential` 验证同一正式发行的可选块表 digest，再发准确 Range 请求。`WindowsUpdatePackages` 仅读取托管安装内同应用／版本的单份 `update-base.bin`，重新算块表，按预计收益选择差量或全包；合成后按整包 SHA-256、大小、MZ 和既有 PE 身份验证。失败只截断当前临时文件并回退一次，取消不回退。主进程保存与 worker 交接不变。

`package-native-installer.ps1` 以固定 SDK、锁文件构建发布工具，生成 `.exe.blocks.json`，校验清单收录两个附件；发布时需将其同时上传。Inno 独立文件压缩方便复用，安装时由已验证载荷生成 662 个 SHA-256 条件条目，使用共享 `installer/PayloadFiles.iss`，相同程序文件不写入、缺项或变化才替换。预安装检查所有目标与父目录，拒绝链接；额外文件和使用资料不清理。当前安装包自行复制为一份 `.bin` 基包，普通卸载管理它。

Release 构建／publish、109 项相关测试、12 项真实 Inno 合成文本探针及 GUI 最终 657 份载荷校验通过。公开 GitHub 的实际 Range 请求读取 65,536 字节通过。两份内部 1.0.2 包比较：全包 79,209,628 字节，差量含块表 5,439,527 字节，复用 73,890,172 字节、23 个 Range；重建字节哈希相同。这不是公开版本的真实升级。前轮 worker／界面／Win10 测试不重复计入本轮结果。

本轮最终证据：`tests/delta-tests.trx`、`selective-probe-2/summary.json`、`real-range-validation.json`、`delivery-validation.json`、`benchmark-final.json` 和 `validation-summary.json`。最新完整载荷为 `package-candidate`，安装器为 `installer-candidate`；第一次编译失败和早期中间包保留，不作为交付。复现命令与有界回退条件见 [1.0.2 BUILD](docs/release/1.0.2/BUILD.md)。

真实应用安装／升级／重启、Production、Win10 实机、完整历史回归和长稳未运行。`test-native-selective-install.ps1` 只安装几份虚构文本到新建 `.artifacts` 目录，不创建系统卸载注册或快捷方式，不安装日用程序；其成功不能等同真实应用升级验收。1.0.1 尚无内置更新和基包缓存，首次迁移需全包。后续输出使用新目录，不覆盖已完成证据。

## 前轮程序更新候选 1.0.2（2026-10-11，已由差量候选替代）

最新源码仍在 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`，新增更新的开发验证位于 `.artifacts/self-update-20261011`。公开版本仍为 1.0.1；本轮没有发布、安装日用更新或接触用户资料。最新候选安装包为 `distribution-final/The-Order-1.0.2-Setup-x64.exe`，SHA-256 `CC85F7CAFA35352011C6DD16D28B25ABE92A15DD2C2A1D3706C9967C24E965A9`；对应源码及验证摘要与其同目录。旧 `win10-compat-20261011` 安装包已由本候选替代，保留历史证据。

`AppUpdateViewModel` 处理静默／手动检查、版本、主要说明、下载状态与取消，`GitHubUpdateFeed` 使用独立无鉴权 transport；`WindowsUpdatePackages` 在当前 profile 的固定 updates 子目录存储校验过的下载。`WindowsUpdateInstaller` 只允许 Production，核对托管安装目录与真实 Setup 的 PE 身份，启动随包 worker。`AppUpdateShutdown` 复用普通退出的草稿、会话及事务收尾，再通过命名事件提交安装；worker 必须等待准确的原进程退出。安装失败写入固定短结果，恢复原入口；不终止游戏、不清理资料。

Production 启动后 15 秒和每 12 小时静默检查，Test／Development 不启动定时器。后台不下载或弹窗；设置导航的提示点及辅助功能名称同步 `HasUpdate`。手动检查可抢占后台请求；说明在本地从发布者的更新段落提取，不使用 LLM 或个人密钥。完整说明保留为可展开的纯文本。

本轮 build／publish、72 项相关测试、worker 18 项、实际命名事件交接 4 项、隔离界面状态 18 项、最终入口 27 项及 657 份载荷校验通过。现有公开安装包的真实下载与 SHA-256 核对通过，未执行它。测试使用新建合成 Test；`-ValidateOnly -ProbeHandoff` 永远在安装／重启前退出。没有 Production、实际安装升级、Win10 实机或历史全量／长稳验证；截图透明合成层不完整，不能把它们算作像素验收。

沿用下方固定 SDK、CPU ONNX 和 Win10 能力守卫。构建命令见 [1.0.2 BUILD](docs/release/1.0.2/BUILD.md)；更新测试可用 `AppUpdateTests|WindowsAppUpdateTests|WindowsCompatibilityTests` 过滤器。本轮最终 TRX 是 `tests/update-background.trx`，最终界面状态 `ui-smoke-final/summary.json`，最终 worker／交接 `worker-tests-final-2/summary.json` 和 `handoff-tests-final/summary.json`，最终包 `package-final`。所有完成的输出必须保持，后续使用新目录。

## 当前 Windows 10 兼容候选 1.0.2（2026-10-11）

维护入口仍为 `D:\Projects\The-Order-worktrees\claude-recognition-rnd/native`；本轮输出 `.artifacts/win10-compat-20261011` 仅是候选与证据。公开发行仍为 1.0.1，本轮没有推送、安装升级或操作用户资料。版本与验证范围见 BRIEF 顶部；完整构建说明见 [1.0.2 BUILD](docs/release/1.0.2/BUILD.md)。

`native/Directory.Build.targets` 将 Windows 程序集的最低运行系统与 CA1416 检查下限统一为 17763，保留 26100 的参考 SDK。Windows 平台层引用既有同版 Windows ML 包以使用自包含 CPU ONNX Runtime；消费它的 x64 测试／诊断工具显式 RID 并保持原输出路径。不要因兼容任务升级依赖或模型。`restore --locked-mode`、Release build 与 publish 使用固定 .NET SDK 10.0.401；安装器使用已有 Inno Setup 6.7.3、VS C++ x64 与 Windows SDK 26100。

最终输出依次为 `publish-final`、`package-final`、`installer-final`。安装器接收 `-CandidatePayload <完整候选绝对路径>` 并核对 1.0.2.0／文件哈希，不接受旧发行 ZIP。交付 Setup、完整源码 ZIP 与校验文件位于本轮 `distribution-final`。Setup 未签名；在用户已有安装上运行旧 `test-native-installer.ps1` 会违反本轮无人值守边界，不作为兼容验收步骤。

本轮三个针对性 TRX 在 `tests/`，去重 604 项通过，覆盖版本能力、实际 CPU 模型、图片选择／缓存、合成资料持久化／恢复、启动计时、移除与识别状态。最终 27 项入口测试在 `launcher-tests-final`。`fallback-smoke-final-2` 使用四份新建三条虚构资料、生成横幅、不含密钥或游戏 EXE；74 项窗口与帧检查通过，实例均经内部退出正常结束。其早期失败结果保留，不能用缺图或标准 Acrylic 的 rendererActive=false 判定低模糊玻璃成功／失败。

兼容模拟开关 `THE_ORDER_NATIVE_COMPATIBILITY_TEST=win10|native-caption|mica-fault` 只在 Test 且 `THE_ORDER_NATIVE_VISUAL_TEST=1` 时有效，实际宿主版本始终报告真实值。Production 不接受该开关。真实 Win10 不可用，所以独立安装、文件选择器、Win10 显卡／缩放、真实 API、真实游戏和长稳仍未验收；当前宿主检查不能替代它们。没有迁移数据格式、改默认正式资料路径或恢复已删除的私人 Test。

## 当前 1.0.1 已公开发布（2026-10-11）

用户回复“可以了，更新 GitHub 平台”，已明确授权本次源码提交、发行及下载说明更新。公开发行 https://github.com/Ph-Rank/The-Order/releases/tag/v1.0.1 已标记 Latest，README 推荐直接下载 Setup.exe。v1.0.1 固定提交 a4c82decf8fc44d106afee86542a13da9919d2cc，tree 2ad653090dd278bf3ac63a64cc9d428cb5d9f071；原 v1.0.0 标签与附件保留。

上传的是用户确认的 home-progress-20261011 安装包，没有重新编译或改动；Setup SHA-256 12092E7B8DB3D077056D6A4C434BC020D9437CFD934712C13079FAA100C2B4BB，72419445 字节。完整源码含安装器，669 份文件和清单，源包 SHA-256 0B216C68EA6B894C0A9520AA565961C5CC1202E2FA3456860E818E41F4808B0E。四份公开附件（Setup、源码 ZIP、SHA256SUMS、ACCEPTANCE）均回下载比对大小与 SHA-256 一致，源码归档清单逐文件一致；223 份可验证编译源码与 PDB 文档哈希一致。打包时 release-identity 的 STAGED_NOT_PUBLISHED/sourceSnapshot:null 是历史构建记录，未重写已确认的安装器，公开身份另由发行页和源码清单说明。

170 个前轮针对性用例和后续 49 项存在重叠，不相加，不声称全量回归通过。用户确认提醒、移除交互和继续游玩布局，并同意发布；真实 API／游戏识别、用户安装卸载、独立干净 Windows 和长期稳定性仍不计入自动验收。未签名和可能的下载信誉提示如实保留。

维护源码与继承 dirty 成果保持，未整体提交开发工作树。发布暂存 .artifacts/github-publication-1.0.1-20261011/repository 仅用于公开快照，不作为开发入口；完整发布凭证 publication.json、public-download-verification.json 和 published-release.png 在同一输出目录。未操作 Production、个人凭据或真实游戏，未自动安装日用版；本次授权不扩大到未来任意发布。下方“尚未发布”反映候选阶段。


## 当前首页进度修复候选（2026-10-11）

1.0.1 最新本地候选位于 .artifacts/home-progress-20261011，替代下方 usability-20261011 候选。维护源码与数据边界不变。publish → package-native-release.ps1 → package-native-installer.ps1（CandidatePayload）链路已通过；安装器 SHA-256 及用户确认范围见 BRIEF 顶部。

本轮只运行 AutomaticRecognitionTests 和 RecognitionPresentationTests，共 49 项通过，记录在 tests/home-progress.trx；没有扩展到完整基线或真实私库测试。新的自动批次计数覆盖识别器外的等待项及图片准备，首页和设置共用。继续游玩为普通封面卡片，Library 的 StageEvidence/AuditStageAsync 现在报告 cover-and-text/material=none，不能再把它当作液态材质受测区域；详情材质不受影响。

Start-The-Order-Test.cmd 只打开本轮 visual-test。用户已确认新继续游玩布局，实际游戏识别进度待其安装后验证。自动代理不得在运行中的个人安装上做升级／卸载，不能自行复用已删除的旧私人 Test 资料。

## 当前 1.0.1 修复候选（2026-10-11）

权威源码仍为 claude-recognition-rnd/native。用户要求本轮集中修复，不做重型历史基线审计。已恢复固定 SDK 10.0.401 和锁定依赖；未升级依赖或操作正式资料。七项行为、证据范围和用户局部确认见 BRIEF 最新节。

构建／发布仍使用 native/scripts/native.ps1；候选发布目录为 .artifacts/usability-20261011/publish-final，package-native-release.ps1 输出 package-final。安装器脚本现改用 `-CandidatePayload <完整候选包绝对路径>`，要求 1.0.1.0，不再接受旧 `-ReleasedArchive`；其余 InnoCompiler／OutputDirectory 参数不变。详见 native/installer/BUILD.md。下方 1.0.0 构建命令是历史记录。

本轮针对性测试位于 .artifacts/usability-tests/usability-2.trx、usability-3.trx、usability-4.trx，共 170 个不重复用例通过。覆盖自动重试／队列、确认跳转、原子批量移除、活跃计时保护、快捷方式变更等。卸载清理函数验证脚本为 native/scripts/test-native-uninstall-data.ps1，使用全新合成目录和受限目录链接。旧 test-native-installer.ps1 固定 1.0.0，未在本轮用户安装上运行。

Test 入口 .artifacts/usability-20261011/Start-The-Order-Test.cmd 固定新建的 visual-test-3（三条虚构条目，无真实目录或凭据）。用户已确认首页提示和移除交互；其余实用行为等待后续核验。安装候选在 installer-final/distribution，未自动安装到日用程序或公开上传。后续安装须先正常退出所有 The Order 实例，升级保留现有数据；卸载默认保留，只有用户明确选择删除才清理默认 Production。

## 当前 1.0.0 安装器维护与验证（2026-10-11）

已按用户授权补充 Setup.exe 分发，当前发行页为 https://github.com/Ph-Rank/The-Order/releases/tag/v1.0.0 。安装器维护源码位于当前权威工作区的 native/installer，完整操作说明见 [安装器构建与检查](native/installer/BUILD.md)。继续保护继承 dirty 状态；原 v1.0.0 标签、应用源码 ZIP 和程序 ZIP 不改变。

在维护工作区根目录使用 PowerShell 7 运行 native/scripts/package-native-installer.ps1，参数为 -ReleasedArchive <已发布原 ZIP 的绝对路径>、-InnoCompiler <隔离 Inno Setup 6.7.3 的 ISCC.exe>、-OutputDirectory <新的 .artifacts 输出目录>。脚本固定验证原 ZIP 和全部应用文件哈希、编译器身份；655 份原程序只作为安装器载荷，不作为开发源码。需要现有 Visual Studio C++ x64 Build Tools 和 Windows SDK 10.0.26100.0；本次 MSVC 为 14.50.35717。不因安装器任务升级应用依赖。

验证入口 native/scripts/test-native-installer.ps1 接收 -Setup <Setup.exe>、-BuildDirectory <构建目录>、-OutputDirectory <新的 .artifacts 验证目录>。发现已安装本安装器、既有同名快捷方式或运行实例时拒绝，避免覆盖用户环境。验证只创建本轮合成 Test，不使用已删除的 experience-profile-v2 或 Production；会临时真实安装、修复、卸载并检查保留 Test 资料，退出时移除自己创建的注册和快捷方式。49 项结果见 .artifacts/installer-20261011/validation-final/summary.json。启动烟测不代表正常退出、干净 Windows 或长期稳定性通过。

已公开下载核对新增三个附件，Setup SHA-256 为 F3F75D8AAE79147022D422CE3580563AB446D5FEF97E9A4DECA9696F5E12E8ED。安装包、全部应用文件和新增入口的 Defender 自定义扫描均未发现威胁；使用 -DisableRemediation 只避免本次扫描自动修改证据，不关闭实时防护。日志在 .artifacts/installer-20261011/defender-*-scan.log。包当前未签名，下载信誉提示仍可能出现；不得把本机扫描当作微软认证，不用关闭防护、自签名信任或重新打包规避提示。公共说明和完整发布复核见该目录 report.md、publication.json。

## 当前本机清理后的开发状态（2026-10-11）

用户已明确授权删除全部 The Order 使用资料、历史程序／发行副本及旧源码工作树，准备从 GitHub 重新下载从头使用。原 experience-profile-v2、候选包、源码恢复副本及根 Start-The-Order-Test.cmd 已删除；下方旧启动路径和“保留 37 项”仅反映历史阶段，不能继续使用或自行恢复旧库。现有维护源码及原 dirty 成果保留在 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，长期仓库 D:/Projects/The-Order 未集成，两个原 HEAD 和 Git 分支保持。后续新测试资料需在新的明确任务范围内准备；不要默认打开 Production。

生成的 bin／obj、node_modules、旧 Release 及本地 SDK 副本已清理，未来构建需重新按锁文件恢复依赖，必要时使用 native/scripts/native.ps1 -InstallSdk 准备固定 SDK。本轮没有功能、版本或依赖升级，没有重新构建／启动应用。1.0.0 公开发行和少量原验收／发布凭证保留，原 artifact 文件大部分已删除，历史链接不保证仍有效。

用户已删除受保护目录并永久删除回收站七项；最终复核 267 个目标原路径和回收站 The Order 残留均为 0，应用实例为 0，维护源码及分支／标签保护检查通过。资料清空与清理状态为 PASS，清单和状态以 BRIEF 最新节及 .artifacts/cleanup-20261011/final-verification.json 为准，result.json、recycle-cleanup.json 保留首次删除和等待用户操作的历史结果。此次明确清空授权不扩大到真实游戏文件、密钥读取、公开仓库改动或以后任意 Production 操作。

## 当前 1.0.0 公开发行与维护入口（2026-10-11）

正式发行已完成：https://github.com/MuskTesla/The-Order/releases/tag/v1.0.0 ，公开源码仓库 https://github.com/MuskTesla/The-Order 。用户已明确授权本次上传、必要的公开提交和记忆更新；没有扩大到日用替换、Production 或未来任务的任意推送。

v1.0.0 固定源码提交 f8d0115f097894e0b86a1415f6262b13ca6def4d，tree 9f963eab975415514e8f4692ae5e5411dddd483c。完整冻结 653 文件、manifest 和公开身份说明共 655 个 Git blob 全部一致；两个 ZIP 和另外三份附件经公开下载复核大小与 SHA-256。main 的 bd8062aebc07f751b2cca9946caaa88dbd5018da 只更改两份发行说明，未移动标签或改变源包／程序包。源码供给完整性与发布附件下载检查为 PASS，详见 .artifacts/github-publication-1.0.0-20261011/publication.json。

维护工作继续使用 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native 和 codex/library-ui-refinement 的原 dirty 状态，HEAD 7facc502fde52532518171384cd77b45c9a60c9c。本轮 .artifacts/github-publication-1.0.0-20261011/repository 仅供发行暂存，不作为开发入口；长期 D:/Projects/The-Order 没有整体集成。不要从发布包或冻结恢复副本继续开发。

根 Start-The-Order-Test.cmd 仍只使用既有 experience-profile-v2；未启动新的 Test 资料、Production 或真实游戏，未操作普通 Test 窗口或替换日用程序。程序回退只更换完整包，不以旧库覆盖新编辑。原 18 条、仅简体中文、个人记录与最终 v24 外观继续有效。

本轮仅更新发行和交接信息，没有改运行代码、版本或依赖，不重复构建和整套回归。此前 2413/2413、27/27 以及本机持续／显示检查的证据与范围保留；独立干净 Windows 未重验、整进程长期稳定性延后验证，缓慢内存增长未标为长期稳定 PASS。下方发布准备阶段的授权与状态文本按历史时间点理解，以本节的最新公开发行状态为准。

## 历史 1.0.0 发布前准备（2026-10-11）

维护入口仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native；分支、HEAD、有效要求见 BRIEF 顶部和原 18 条。用户授权首版发布前准备，未授权公开发布／日用替换／Production。根 Start-The-Order-Test.cmd 固定 .artifacts/first-release-20261010/launch-1.0.0.ps1，仅使用原 experience-profile-v2，不能改成发行包默认的空白 Test 或正式入口。本轮用户明确不验证虚拟机，继续只用本机既有隔离资料。

版本为 1.0.0，文件版本 1.0.0.0；UI 设置可查看版本。保留仅简体中文、全宽原比例横幅、罕见极窄下移、无折射／无色散 σ1 玻璃和 v24 的封面恢复／横幅完整首帧。原资料和手动图片选择必须保留，回退程序不能恢复旧库覆盖后续编辑。闭合资料检查点保存在本轮 .artifacts 的 private-profile-checkpoint，含私人资料，只供本机恢复保障，不能复制到源码或公开包。

本轮执行记录（重跑须使用新输出目录，不覆盖本轮证据）：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' restore native/TheOrder.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' build native/TheOrder.slnx -c Release --no-restore -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=full-release.trx' --results-directory .artifacts/first-release-20261010/tests-release
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o .artifacts/first-release-20261010/publish-a1
& './native/scripts/package-native-release.ps1' -PublishedDirectory '.artifacts/first-release-20261010/publish-a1' -OutputDirectory '.artifacts/first-release-20261010/candidate-1.0.0' -SourceManifest '.artifacts/first-release-20261010/source-1.0.0.json'
```

完整回归 2413/2413，无跳过；新增启动入口回归 27/27，真实 Windows PowerShell 5.1 执行。异常包、大小／哈希错误、目录逃逸、重复路径、错误 Test 类型与正式目录及父子目录均拒绝；ValidateOnly 只验证文件，不打开任何资料。正式入口仅由最终用户启动时传入 Production 授权参数。代理验证必须使用固定的隔离入口。发布脚本只写新的 .artifacts 输出，绑定完整当前源码 manifest，程序和源码 ZIP 的所有条目另逐字节验证；不能仅靠 HEAD／MVID 绑定 dirty 源码。

发行包使用自包含运行时，并提供 files.json、release-identity.json、dependencies.json、完整许可、README.md 和 CHANGELOG.md。对应源包包含 653 份源码和三份 docs/release/1.0.0 文档，依赖锁文件不变；source-1.0.0.patch 包含相对 HEAD 的继承变更，不是 15 文件的单任务补丁。task-delta-1.0.0.json 才是相对本轮 647 文件基线的任务差异。构建环境与普通使用说明见 docs/release/1.0.0/BUILD.md／README.md。

本轮最终持续运行、退出／重启、资料差异、普通入口以及用户显示核验的结果统一保存在 .artifacts/first-release-20261010/report.md。高对比度及缺图等注入是明确限定的代码检查；新包没有在独立干净 Windows 上重验，没有访问 Production 或真实游戏，也不把较早宿主证据、单位回归或未回复当作本版全部通过。所有历史候选／失败记录保留，不提交、不推送、不整体集成长期仓库。

## 历史简体中文 Test v24：首次封面与横幅完整首帧（2026-10-10）

源码仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，分支、HEAD、18 条有效要求见 BRIEF。普通入口为根 Start-The-Order-Test.cmd（multilingual 同名历史入口同步），固定 .artifacts/cover-startup-v24/launch-v24.ps1 → candidate-v24-frame-ready。651 项 payload，manifest SHA256 82612135D4414B3B18239076C89ED7F23475849988297AEEF2C1BC389EFBDEBD，MVID 7107477d-593c-4549-8033-1a5ca2a661b8。普通启动 PID 5252／UTC 2026-10-11T05:59:34.5836220Z，诊断／Advanced／控制器关闭；只用 experience-profile-v2 的原 37 项 Test。

ICachedCoverRecommendationSource.ReadCoverAsync 只读本地经过身份／签名和 CoverSchema 验证的推荐，查询结果首次发布前恢复，保留个人选图与较新事件优先。首个 candidate-v24 的“只清临时底色”修复已被用户否定（变成全透明闪现）；最终新增 BannerPresentation，把横幅、文字、玻璃的首帧作为整体，在绘制及当前几何均就绪后的合成帧才揭示。保留布局测量、失败回退、取消和生命周期释放；无新增空闲绘制／轮询。σ1 无折射外观及全宽原比例保持。

本轮最终执行记录：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~CatalogPreparationTests|FullyQualifiedName~PresentationRemediationTests|FullyQualifiedName~UiRefinementTests|FullyQualifiedName~ArtworkReadabilityTests|FullyQualifiedName~MaterialPreferencesTests|FullyQualifiedName~BannerSelectionTests|FullyQualifiedName~ImagePipelineTests|FullyQualifiedName~DecodedSurfaceCacheTests' --logger 'trx;LogFileName=frame-ready.trx' --results-directory .artifacts/cover-startup-v24/tests-frame-ready
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o .artifacts/cover-startup-v24/publish-frame-audit
& './native/scripts/package-native-rc.ps1' -PublishedDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/cover-startup-v24/publish-frame-audit' -OutputDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/cover-startup-v24/candidate-v24-frame-ready'
```

再次执行必须使用新产物目录，不能覆盖历史候选。143/143 上述回归通过，与首轮 212/212 去重共 277 项，初轮 78 不重复累计。最终 Test 内部 heroFrames 只由既有隔离控制器触发，每轮最多记录 120 个实际合成帧快照并及时解除订阅；8 场景共 621 样本，未出现可见但未准备好的横幅，最终均可见。另有图片缺失／挂起／迟到完成、系统保护和高对比度注入检查。它们不是逐帧像素录像或完整发布验收。3 个宽窗布局全部叠放、无越界；解码缓存仍小于 64 MiB、最多 2 并发；无网络资料加载或重新分析。37 项的个人字段／library.json hash、选图 URL 均不变。

用户正常关闭失败首个 v24 后，PID 21472 受控实例完成检查并通过应用正常退出，再从根 CMD 打开最终普通实例。用户已回复“已检查，本轮显示合适”，记录为最终 v24 本轮显示通过；检查提示后没有操作窗口，不扩大为全部历史任务或正式发布验收。 不再操作该窗口。

report.md、TRX、frames-*/summary.json、hero-stage-audit.json、普通入口记录及源码恢复位于 .artifacts/cover-startup-v24。source-v24-confirmed/（647 文件）和 source-v24-confirmed.json/patch 含全部继承源码；task-delta-v24-confirmed.json 是相对 645 文件基线的 18 个任务变化文件。旧 candidate-v24、candidate-first-attempt-summary.json、launch-v24-first-attempt.ps1 仅供失败追溯／程序回退，不能恢复旧库覆盖新编辑。未提交、推送、正式发布、操作 Production 或执行真实游戏。

## 历史简体中文 Test v23：横幅评分、全宽原比例与澄澈玻璃（2026-10-10）

维护源码仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，分支 codex/library-ui-refinement、HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加完整继承 dirty 成果。读取 BRIEF 最前面的 v23 及全部 18 条有效要求。最新用户决定是横幅必须横向铺满，按源图比例确定高度，信息框仅在极窄窗口实在放不下时下移；文字框采用无折射、无色散、低模糊 σ1，保留明暗适配与细高光。旧居中缩窄方案已被用户否定，不可恢复；σ2.5 历史候选也不是当前入口。

根 Start-The-Order-Test.cmd 与 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd 固定 .artifacts/banner-aesthetics-v23/launch-v23.ps1 → candidate-v23-clearer。651 项 payload，manifest SHA256 E6860070EFE37026993EEF6D7F6C37E92E28BBC6B83DF5C95714278253A859B7，WinUI MVID 0a4c718f-95aa-4d01-96ce-96315ec04b85。实际普通入口 PID 19956／UTC 2026-10-11T05:02:41.4007270Z，诊断、Advanced 资格与控制器关闭。只使用既有 experience-profile-v2：ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e，37 项个人资料与 library.json 哈希不变。

横幅评分 landscape-composition-v1 在同作品／可靠版本及 OCR 门槛后，按场景结构 25、叠层留白 25、明暗层次 20、有效分辨率 30 评分，约 1600×500 后像素加分封顶；不是人脸或训练审美模型。最多 32 张有界准备并写入 BannerSchema 1，CoverSchema 2／MediaSchema 6／ArtworkSchema 4 不变；同画面近似图优先更清晰原图，未分类封面不充当 CG。布局先并排事实和游玩统计、压缩间距、加宽文字框，最后才按实测高度保底下移。评分不在滚动或逐帧执行，玻璃复用已有 GPU 渲染器；系统保护、标准材质／高对比度及失败回退保留。

37 项准备完成，31 项有合格 CG，15 个横幅 URL 变化（含移除 1 张误用封面），7 项像素增加；11 张仍受低清源图限制。封面推荐 0 改变，手动／本地素材优先。最终包运行 Ready、无待保存编辑／计时，图片错误／过期图／排队均 0，解码 66,699,568 bytes <64 MiB，最大并发 2；重启 CatalogLoads／Analyses 均 0。完整缓存与用户图片留在原 Test 目录，不复制入源码或程序包。

412 项相关 Release 回归、31 项材质／可读性回归通过（有重叠，不相加）；最终 σ1 GPU 回读验证零几何偏移、框外保持原图、7 背景×两极性可读边界及资源释放。37 部实测：常用 1150 DIP 窗口中全部 31 张 CG 全宽且信息在图内；620 DIP 的 4 个短／常规样例仍在图内，480 DIP 的超宽 34 样例才下移，常规 5980 仍叠层。仅调低模糊后复跑 GPU 和最终包两例实际布局；完整布局矩阵来自相同布局代码的 σ2.5 包，保留明确证据来源。受控暗色及强制 Solid 回退也已检查；强制策略不等于实际 Windows 高对比度／远程环境验收。

```powershell
& './native/scripts/test-optical-source.ps1' -OutputDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/banner-aesthetics-v23/optical-low-blur' -Packages 'C:/Users/MUSK/.nuget/packages'
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/banner-aesthetics-v23/publish-clearer'
& './native/scripts/package-native-rc.ps1' -PublishedDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/banner-aesthetics-v23/publish-clearer' -OutputDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/banner-aesthetics-v23/candidate-v23-clearer'
```

以上是本轮命令记录，再次执行必须使用新输出目录，不覆盖历史包。正常退出受控验证实例后，已通过根 CMD 打开最终普通候选；用户回复“已检查，本轮显示合适”，仅记录本轮显示通过，不能据此宣称全部历史任务或正式发布 PASS。检查提示之后未操作窗口。回退只更换程序，不以旧资料覆盖新编辑。

详细证据在 .artifacts/banner-aesthetics-v23/report.md。baseline-source/ 与 baseline-source.json 是本轮 641 文件继承基线；source-v23/、source-v23.json、source-v23.patch 保存完整当前源码，task-delta-v23.json 标明相对本轮基线的变化。完整源码恢复含继承成果，不是单任务集成补丁。未提交、推送、集成长期仓库、发布、操作 Production／日用程序或执行真实游戏。

## 历史简体中文 Test v22：封面构图评分（2026-10-10）

当前维护源码仍为 D:/Projects/The-Order-worktrees/claude-recognition-rnd/native，保留 codex/library-ui-refinement／HEAD 7facc502fde52532518171384cd77b45c9a60c9c 的全部继承 dirty 成果。用户明确要求美观度权重，并选择“均衡：构图优先，清晰度达标”；读取 BRIEF 最前面的 v22 及全部 18 条有效要求。

根 Start-The-Order-Test.cmd 和 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd 固定 .artifacts/cover-aesthetics-v22/launch-v22.ps1 → candidate-v22-final。651 项 payload，manifest B32327B0E334BCF97EB1538FABBB26D91F93D1CBECD42E0428F617935FD8C62E，WinUI MVID 5199bfea-3ddc-4a0e-9337-27b50bfaffcf；实际普通入口 PID 12888／UTC 2026-10-11T03:48:00.0544070Z，diagnostics／Advanced／controller 关闭。仅 experience-profile-v2 原 Test：ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee、StoreId eb86f351-0960-fd91-3469-bb46c947018e。

CoverAesthetics 在应用层实现 bookshelf-composition-v1：3:4 书架构图、细节、裁切边界、重心共 80%，有效分辨率 20%，约 360 像素卡片宽度后不再加分。候选最多 3 个，沿用 v21 的 primary／精确来源门槛；未分类图库只允许手动选。4 分内保留目录 primary、否则 2 分内保留原推荐。不是人脸／标题检测或已训练的个人审美模型。像素分析只在准备阶段执行，CoverDecision 持久保存；CoverSchema 2／MediaSchema 6，ArtworkSchema 4 与横幅不变。自动推荐预览通过 ICoverRecommendationSource 读取同一结果，保存前复核签名与 URL；手动固定／本地封面优先且不迁移。

实际 37 项准备完成，28 个自动 URL 改变，32 张横幅不变，个人字段与 library.json hash 全同。重启资料请求 0，原有 CG 不重做 OCR；最终 Ready、37 项、图片错误／过期图／待处理均 0，解码缓存 66,494,704 bytes <64 MiB。完整照片原文件仍在原 Test 派生缓存，不复制到程序包／源码恢复。623 项相关 Release 回归、后续命名补齐的 51 项回归全通过，两组含重复；初始 2 个旧 schema 断言失败已修复并保留证据。

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/cover-aesthetics-v22/publish-v22-final'
& './native/scripts/package-native-rc.ps1' -PublishedDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/cover-aesthetics-v22/publish-v22-final' -OutputDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/cover-aesthetics-v22/candidate-v22-final'
```

以上是已执行记录；再次构建／打包必须用新目录，现有候选不可覆盖。普通入口直接双击根 Start-The-Order-Test.cmd，先正常退出旧 Test。回退可用 .artifacts/cover-selection-v21/launch-v21.ps1，只换程序，不能恢复旧库。用户已关闭 v21；v22 视觉反馈偏好《魔女的夜宴》下方群像和《西历2236年》下方作品封面，同时明确“目前水平也完全够用”。本轮保留 v22，两例偏好仍未满足，不签署视觉全 PASS、不按作品写特例。提示后没有操作窗口，也未替用户保存封面。桌面控制工具 kernel assets 初始化故障仍未恢复，内部运行检查用既有应用 Test 接口，不自制 UIA 绕过。

详细研究、各作品评分、日志和运行记录在 .artifacts/cover-aesthetics-v22；维护报告为 report.md。baseline-source/ 为开始时 638 文件基线；source-v22/、source-v22.json、source-v22.patch 是包含继承成果的完整恢复，task-delta-v22.json 才是本轮范围。没有提交／推送／发布、Production／日用替换或真实游戏执行。视觉与整体发布门槛分开记录；不能沿用 v21 确认宣称 v22 或正式发布 PASS。

## 历史简体中文 Test v21：封面用途优先（2026-10-10，评分及预览由 v22 接续）

继续从 D:/Projects/The-Order-worktrees/claude-recognition-rnd 的 native/ 开发；分支、HEAD 与继承 dirty 成果不变。用户指出 v20 自动封面包含盘面／封底／粉丝书／原声带／PS 包装，明确授权修正。详情见 BRIEF 最前面及全部 18 条有效要求。

普通入口是根 Start-The-Order-Test.cmd（或 .artifacts/multilingual-20261010/Start-The-Order-Test.cmd），两者固定到 .artifacts/cover-selection-v21/launch-v21.ps1。candidate-v21 为 651 个 payload，manifest 957805A1AAE094B8A8E41D1DADE179757CB9D75F169852BF20F6CE2BF1217CBB，WinUI MVID 63ed08b7-3d03-47cf-863c-63edf735f696。已实际普通启动 PID 22988／UTC 2026-10-11T03:12:28.7134830Z。始终仅用既有 experience-profile-v2；不恢复旧资料、不复制缓存进源码、不执行真实游戏。

CoverQuality 不再对任意 catalog covers 按像素排序：仅规范 URL 与保留 provider ID 匹配的同作品 VNDB 主图／Steam 书库海报可自动替换；目录明确 primary 为回退。图库候选仍可手动选择。CoverSchema 1／MediaSchema 5 升级派生缓存，ArtworkSchema 4 不变；无可信补充图的旧缓存重新查询 primary，离线失败保持可重试。用户库 JSON、手动选择与本地封面不迁移。实际 37 项升级、30 个自动 URL 更新，横幅 32 张与来源全保留，个人字段及 library.json 哈希相同。

本轮 Release 验证：579 个图片／目录／在线用例、31 个界面状态用例，去重 607 个，全部通过无跳过。构建命令保持 NativeMultilingualEnabled=false、NativeProductionCandidate=true，仅表示候选编译；publish 到 .artifacts/cover-selection-v21/publish-v21，package-native-rc.ps1 输出 candidate-v21。不覆盖历史产物；日志、TRX、包身份、普通入口记录与完整源码恢复见该目录 report.md、source-v21.json/source-v21/、source-v21.patch、task-delta-v21.json。

原生控制工具本轮初始化报 kernel assets 路径不存在，用户已正常关闭 v20。候选运行验证仅使用既有 Test 请求接口，完成后正常退出验证实例，再通过真正普通入口打开 v21，诊断与控制器关闭。用户已回复“已检查，本轮封面合适”，仅记录本轮封面确认；没有继续操作窗口。代码／缓存检查通过不等于视觉或发布验收通过。

## 历史简体中文 Test v20：图片质量与发布前修复（2026-10-10，封面选择已由 v21 修正）

维护源码仍在 D:/Projects/The-Order-worktrees/claude-recognition-rnd，codex/library-ui-refinement，HEAD 7facc502fde52532518171384cd77b45c9a60c9c 加完整继承工作；native/ 是 C#/WinUI 实现。用户本轮明确授权 12 项开发修复和无人值守检查复验，原正式数据／密钥／日用替换／提交推送边界保持。先读 BRIEF 当前状态和全部 18 条有效要求；以下 v19 及更早节为历史记录。

最新普通入口（仅既有 37 项隔离 Test，先正常退出旧候选）：

```powershell
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/release-readiness-20261010/launch-v20.ps1'
# 或工作区根 Start-The-Order-Test.cmd／.artifacts/multilingual-20261010/Start-The-Order-Test.cmd
```

candidate-v20-audit9：651 项 payload，manifest SHA256 E32364149858BD627D24F3138463A5E11CB94644AEE00D66665279E7AE109375，WinUI MVID cc3d02f9-a918-43c6-8259-470e7699ad09。入口固定核对包和 experience-profile-v2 的 Test 身份：ProfileId 22c746b9-c20b-50d9-efea-1ce4281261ee，StoreId eb86f351-0960-fd91-3469-bb46c947018e。普通入口关闭 diagnostics、Advanced Test flag 与请求控制器；内部测量实例启用既有 Test-only 控制器或性能 harness，必须区分。NativeMultilingualEnabled=false；四种语言资源／代码保留。

本版图片准备使用 MediaSchema 4、ArtworkSchema 4，旧公共展示缓存有界升级并保留仍有效的已筛选图，库和个人编辑不迁移。Steam 用同作品唯一独立 app ID，或官方 API 确认的唯一正式版／试玩版父子关系（最多两个既有编号，128 KiB 响应和 20 秒上限）；VNDB 用唯一精确作品 ID 和当前 API 确认的安全图片，均无 NextMoe 鉴权、cookie 或重定向。不同游戏／不明关系不选图，失败保留旧结果并可重试。固定原图通道允许系统 VPN／代理路由，一般远端图片仍做公网 DNS 核验。官方宽幅按设计比例评估质量，OCR 仍排除对话框。原图缓存 2 GiB／20000、解码 64 MiB／最多 2 并发等预算保留；不递归预取 A→B→C，不预加载所有未选封面。来源低清／无合格纯 CG 仍如实报告；本地横幅选择及恢复自动图保留。

EXE/CMD 初次选择仅在安装目录内有界枚举，不执行来识别；唯一完整的正常游戏候选自动选择，唯一卸载／安装／配置工具须明确选择，其他完整或部分列表由用户选择。保存保留参数、工作目录、其他安装和记录。CMD 通过固定 System32 cmd.exe、私有 Unicode 子进程环境和既有非杀死型 Job 范围启动；GUI 接续只接受唯一窗口／完整范围／精确进程身份，遗漏出生或竞争窗口不猜测结束。日常玻璃无色散，标题框宽窗 16:9／紧凑窗 4:3，按实际内容高度扩展。系统保护、失败回退、NSFW、版本事实与内部导航规则保留。

已执行的最终构建命令（打包总使用新目录，不能覆盖历史候选）：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore -p:NativeMultilingualEnabled=false --logger 'trx;LogFileName=full-release-final7.trx' --results-directory '.artifacts/release-readiness-20261010/tests'
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/release-readiness-20261010/publish-v20-audit8'
& './native/scripts/package-native-rc.ps1' -PublishedDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/release-readiness-20261010/publish-v20-audit8' -OutputDirectory 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/release-readiness-20261010/candidate-v20-audit9'
```

audit9 打包复用已通过完整回归和 10 分钟 actual XAML 的 publish-v20-audit8；package-audit9-delta.json 核对全部应用文件相同，仅 launcher／CMD／包身份和许可证清单字段顺序变化，许可证事实相同。普通入口显式从 PSHOME 加载系统 Utility，不依赖宿主的模块自动发现。NativeProductionCandidate 是可移植自包含构建开关，不是 Production 访问授权。2359/2359 完整 Release PASS，0 FAIL、0 SKIP，TRX 218.43 秒；构建／包成功、651 项 payload 逐文件 SHA256 校验通过。37 项缓存准备完成，32 部有合格横幅，16 个自动封面改进。audit8 实际 Ready、诊断关闭；未设程序的 9-nine 启动按钮可用且可访问，未调用真实游戏。宽窄窗口、标题文字边界、明暗主题和应用内强制回退／恢复已测。audit7 完成一小时、9199 轮 actual XAML 操作，图片错误／过期封面为 0，缓存预算通过；进程内存中位数仍上升，长期稳定未签署。audit8 应用二进制完成 10 分钟、1535 轮复验，图片错误／过期封面为 0，缓存预算通过。audit9 保持全部应用字节，只修复 Windows PowerShell 5.1 显式加载系统 Utility 和 CMD 保留失败退出码；根 Test.cmd 已实际打开固定 Test（PID 34328、UTC 2026-10-10T23:50:20.3868470Z），诊断／控制器关闭。缺失 payload 反例正确退出码 1、未启动／未改库，见 launcher-negative-audit9.json。最终记录见 report.md。不要把应用内策略模拟说成真实系统高对比度切换，也不以有限时段签署所有机器通过。

`.artifacts/release-readiness-20261010/report.md` 保存 12 项映射、失败历史、候选运行、数据哈希、未验证范围；source-v20/、source-v20.json、source-v20.patch 是完整 dirty 源码恢复（含继承成果），task-delta-v20.json 比较已核对的 620 文件 v19 基线。不得整体盲目套用主仓库。测试资料和图片缓存不进入源码／包；回退只换程序，不用旧 profile 覆盖新编辑。v20 用户视觉检查和正式发布验收尚未完成，旧 v19 的“已检查”只对应旧轮次。

## 历史简体中文 Test v19：CG 局部明暗自适应标题玻璃（2026-10-10，已由 v20 取代）

v19 当时入口（历史程序保留；当前普通入口见前述 v20）：

```powershell
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/multilingual-20261010/launch-language-v19.ps1'
# 或工作区根 Start-The-Order-Test.cmd／同 artifact 目录 Start-The-Order-Test.cmd
```

固定包 candidate-language-v19，647 项 payload；manifest SHA256 83D5B258E9F087EFB53D09BB8341E80DA13DF1846129AFABCD84D812780FEB53；WinUI MVID 144754e9-3678-451f-a1df-118cb2d7de3c。启动入口逐文件检查包身份与 Test profile，普通启动不需要 SDK／个人密钥／诊断入口。当前人工检查实例 PID 34812，诊断关闭，额外只启用旧请求文件控制器；不能将它混称无控制器的普通入口。

新增 ArtworkReadability 在标题区域有界采样，并按原 shader 的 scrim／线性亮度／tint 规则估算深／浅文字的遮盖成本；选择较清透的可读极性，不降低原对比界限，不改 shader 或原 Daily 参数，不增加图像副本或定时绘制。ArtworkStage 在 CG／布局改变时更新极性，字体／链接通过已有 HeroPlate 主题与前景联动。v18 的铺满裁剪／日常玻璃无色散／角遮罩和 v17 的原文件本地横幅／3840 上限继承。

验证命令：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore -p:NativeMultilingualEnabled=false --filter 'FullyQualifiedName~ArtworkReadabilityTests|FullyQualifiedName~CoverCroppingTests|FullyQualifiedName~PersonalBannerTests|FullyQualifiedName~MaterialPreferencesTests|FullyQualifiedName~ImagePipelineTests' --logger 'trx;LogFileName=artwork-v19.trx' --results-directory '.artifacts/multilingual-20261010/tests-v19'
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/multilingual-20261010/publish-language-v19'
```

48/48 相关 Release 测试、无跳过，publish／打包成功。用户检查 v19 后回复“已检查”；没有助手桌面视觉巡检、全仓或资源长测，不扩大为历史整体或正式发布验收。源码 source-v19/、source-v19.json、source-v19.patch 与报告 report.md 均在上述 artifact 目录；完整 dirty 恢复包含继承工作，不能整体盲目集成。旧版日志、包和 source-v18 快照不可变。

工作继续在 claude-recognition-rnd 的维护源码，先读 BRIEF 最前面的当前候选与“用户有效要求与后续代理身份”。新代理是负责开发与维护的 GPT／Codex；v19 当时没有后续开发目标，现有授权见前述 v20。不能擅自启用多语言、放宽识别／NSFW／KunGal 规则、无限预取或替换正式程序。正式资料、凭据、日用程序保持原边界。


## 历史简体中文 Test v18：封面填满裁剪、日常玻璃与本地横幅（2026-10-10，已由 v19 取代）

正常退出旧 Test 后使用已有 Start-The-Order-Test.cmd 或以下固定入口；profile 仍为 experience-profile-v2，不使用正式资料。

```powershell
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/multilingual-20261010/launch-language-v18.ps1'
```

候选 candidate-language-v18，647 项 payload；入口核验 manifest SHA256 A8623A3D2A6995B2EE61539B5955B04E7275E3101FE79DCF8ECF0C4F9FCCD09D。普通启动遵循保存的材质偏好，无诊断 Gallery；当前人工检查实例附加了既有请求文件控制入口，用于安全启动／退出，不等于正式发布验收。

封面使用 CoverCropping 的最小等比裁剪窗口保证铺满；分析限制为 128×160 采样和 65 个候选位置，按弱身份缓存结果，保留底部标题细节但不能保证所有作品审美。角色及选择预览使用完整显示。横幅源文件和选中的 CG 准备上限提高至 3840，显示按窗口／DPI 请求；源素材低清仍是限制。详情“…”可选择本地 PNG/JPEG/WebP 或恢复自动横幅；本地原文件受既有数量、容量、路径与原子写入保护，储存在 profile/native-personal-artwork-v1，旧书架备份不包含该伴随目录，备份须保留整个正常退出后的 Test profile。

日常玻璃采用历史第二候选 Daily，关闭色散；移除手动关闭透明效果选项，旧 Opaque 兼容映射 Standard。系统保护条件不撤销。CG 四角由外围遮罩及复用的 composition clip 处理，生命周期离开时释放。

相关 Release 回归 43/43、无跳过，覆盖裁剪、个人横幅、图片缓存及材质偏好；日志为 .artifacts/multilingual-20261010/tests-v18.log 和 tests-v18/artwork-v18.trx。首次 build-language-v18.log 编译失败，修复后 build-language-v18-repair1.log 成功；package-language-v18.log 成功。用户要求自行检查界面，不重复助手桌面验证、全仓／资源长测。

当前源码副本 source-v18/、完整清单 source-v18.json、tracked 差异 source-v18.patch 与 report.md 均在上述 artifact 目录，包含继承源码，不可整体盲目集成主开发。回退只更换程序候选，不能恢复旧 profile 覆盖用户较新编辑；v17 的留边封面已被用户否定。主开发与正式程序保持原状。


## 历史简体中文 Test v16：封面四角裁切（2026-10-10，已由上节取代）

接续下节 v13。v14 去除紧凑声优链接下划线，并以一次有界批量请求补全实体相关作品缺少的封面，保留 Galgame／KunGal、返回编号、分页和取消检查。v15 的四角裁切仍有图片溢出问题，用户截图判定未解决；v16 改为裁切 CoverPresenter 的实际显示边界，用户随后确认“解决了”。失败历史和此前候选保留。

```powershell
# 普通权限，仅既有隔离 Test；先正常退出旧 Test。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/multilingual-20261010/launch-language-v16.ps1'
# 同目录 Start-The-Order-Test.cmd 指向 v16。
```

固定包 candidate-language-v16，647 项 payload；manifest SHA256 `8A0D53005776A23631F83A5016C4791152E0E3707BBF40466639EECD5EE6AEE7`。有效 Standard、诊断关闭、Advanced 资格关闭，当前固定简体中文。四种语言实现保留，复用时需另行验证。Test profile、凭据和图片缓存不在包或源码恢复副本中。

Release publish／打包成功。v14 相关 Release 126/126、v15 图片／性能定向 60/60 按原版本保留；v16 未重复这些测试，也未运行助手桌面核验，由用户检查显示效果。用户确认仅对应本次圆角修复。v16 未生成完整源码恢复副本，不能声称存在 source-v16 清单。当前 v18 恢复副本与版本边界见 `.artifacts/multilingual-20261010/report.md`、`source-v18.json`、`source-v18/` 和 `source-v18.patch`；包含继承修改，需结合记录的 HEAD，不是独立集成补丁。

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/multilingual-20261010/publish-language-v16'
```

正常退出 v16 后可用同目录 launch-language-v14.ps1 回退程序，保留声优链接／相关封面补全，但会恢复旧圆角行为。不要恢复旧资料、删除缓存或覆盖较新编辑；v15 是已知圆角失败候选。没有主开发集成、提交、推送、发布、日用替换、Production 写入或真实游戏执行。

## 历史简体中文 Test v13：无图角色两行布局（2026-10-10，已由上节取代）

接续下面 v11 的全局关联规则，只改 WinUI 角色显示。没有图片地址时显示两行姓名／声优，不分配空图片框；姓名与声优仍可在当前窗口导航，长内容不换行并提供提示。有图卡片、剧透边界、缓存和书架事务不变。四种核心语言代码与旧候选保留，当前入口固定简体中文。

```powershell
# 普通权限，仅已有隔离 Test；先正常退出旧 Test。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/multilingual-20261010/launch-language-v13.ps1'
# 同目录 Start-The-Order-Test.cmd 指向 v13。
```

固定包 `candidate-language-v13`，647 项 payload；入口核验 manifest SHA256 `F21815F9F94E03AE3AE5A80BCA0463D297C0D68421D0FF7B4FCEBFA8511650D2` 后调用包内 Test 身份保护入口。Test 根仍是 `experience-profile-v2`；有效 Standard、诊断 0、Advanced 资格 0。程序包不含 profile、凭据、私有输入或图片缓存。完整源码清单及副本为 `.artifacts/multilingual-20261010/source-v13.json`、`source-v13/`、`source-v13.patch`；含继承工作，不能整体盲目集成。

Release publish／打包成功。v13 的 Domain、Application、Infrastructure、Presentation、Platform.Windows 五个 DLL 与 v11 相同，保留 v11 的 230/230 定向回归证据；布局修改用真实桌面检查，未重复全仓或 Phase 7 长测。74 个无图角色的两行显示、长内容省略、两类链接和返回均完成实机检查；v12 分隔符问题已修复并保留历史记录。结果与截图见同目录 `report.md`。

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/multilingual-20261010/publish-language-v13'
```

程序回退：正常退出 v13，运行同目录 `launch-language-v11.ps1`，保留全局 Galgame 关联修复，恢复此前角色布局。只回退程序，不恢复旧资料、不删除缓存、不覆盖较新编辑。历史多语言 v7 入口仍保留，不能冒充带 v13 修复的新版。没有集成主开发、提交、推送、发布、日用替换或 Production 写入。

## 历史简体中文 Test v11：全局 Galgame 关联与缓存（2026-10-10，已由上节取代）

源码在 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`，`codex/library-ui-refinement`，HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加未提交工作。恢复使用 `.artifacts/multilingual-20261010/source-v11.json`、完整 tracked 差异 `source-v11.patch` 和新文件／源码副本 `source-v11/`；包含继承成果，不能盲目套用主开发或整体引入 `5b574f0`。

```powershell
# 普通权限，仅已有隔离 Test，不替换日用。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/multilingual-20261010/launch-language-v11.ps1'
# 也可双击该目录 Start-The-Order-Test.cmd。
```

固定包 `candidate-language-v11`，启动前校验 manifest SHA256 `FA2EE271BBFACC0F783ED45E0778A92E028A2B9711953A7A296B5674C72F8171` 和 647 项 payload。默认 Test 根仍为 `.artifacts/ui-refinement-20261009/experience-profile-v2`，37 项隔离书架。有效 Standard、诊断 0、Advanced 资格 0。启动拒绝非 Test 身份；不要把此入口改名当 Production。普通入口不启用桌面控制文件。测试包没有 profile、真实凭据、私有输入或图片缓存。

目前固定简体中文，四种核心语言实现留存。后续复用可构建 `-p:NativeMultilingualEnabled=true` 并重新验证；历史 `Start-The-Order-Multilingual.cmd` 固定 v7。当前发布命令沿用本机 SDK 与锁文件，不升级依赖：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true -p:NativeMultilingualEnabled=false --no-restore -o '.artifacts/multilingual-20261010/publish-language-v11'
```

新关联契约为 `CatalogRelatedWorks.Schema=1`，媒体准备版本 2：只展示接口明确给出的 Galgame／KunGal 页面事实。`KunGalGalgameId` 独立于 NextMoe Id；null 字段不写入旧缓存序列化，兼容旧签名。旧缓存先过滤不明关联，再排队补齐展示事实，复用仍有效的已选 CG；离线保留既有资料和图片、不清空用户库、不重跑 OCR。覆盖作品、角色、制作人员、公司、引擎、系列、标签和发现入口。分页游标保留，不能以一页过滤为空推定后续没有结果。NSFW 仍按既有 KunGal 分类，仅作用于搜索及标签；不改变已入库作品身份。

图片持久缓存为独立 profile 内派生数据，2 GiB／20000 文件上限，解码 64 MiB，有限并发；预热当前作品及直接相关卡片，不遍历关联图谱。导入后失败的准备任务不会冒充导入回滚或覆盖个人编辑；用户主动打开未准备条目时优先排队，取消及过期结果保护保留。

最终相关 Release 230/230、0 skipped；历史 tests16 的 3 项失败为旧语言测试夹具缺少新的 Galgame／KunGal 契约，修订夹具后 tests17 通过，未删测／跳过。此前 tests11／12 失败及修复、v9 全量 1633/1633、v10 定向 73/73 保留原版本范围。最终 Release publish／647 文件打包成功。实机验证旧缓存升级后 3 张关联封面正常、关联游戏在当前窗口打开／返回、21 张非剧透角色图片可用；6 个资料／加密凭据文件 hash 未变。实际证据见本轮 report 与 probe；不声称全部 37 项在任何网络状态下绝对零等待。

程序回退：正常退出 Test 后运行 `.artifacts/multilingual-20261010/launch-language-v9.ps1`，保留旧包／入口。v9 不具备全局关联过滤，可能再次显示动画关联；只回退程序，不恢复旧资料、删除缓存或覆盖较新编辑。多语言 v7 为另一个保留选择，不包含 v11 修复。原主开发 HEAD45b77f4／34 项修改未集成；未提交、推送、发布、改 Production 或运行真实游戏。

## 历史 Catalog v9：隔离体验、标签分组与 NSFW 设置（2026-10-10，已由上节取代）

工作区 `D:/Projects/The-Order-worktrees/claude-recognition-rnd`，分支 `codex/library-ui-refinement`，HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加未提交源码。应用父版本 `03f1f7bb0dfcc35957fe5ba4301cf21551a8dc88`；最终恢复清单为 `.artifacts/ui-catalog-refinement-20261009/source-catalog-v9.json`、完整 tracked 差异 `source-catalog-v9.patch` 和源码／新文件副本 `source-catalog-v9/`。包含继承成果，不能当作可以整体套用主开发现场的补丁；禁止整体引入 `5b574f0`。

```powershell
# 普通权限，仅独立 Test；不替换日用入口。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/ui-catalog-refinement-20261009/launch.ps1'
# 或双击同目录 Start-The-Order-Test.cmd。
```

入口固定 `candidate-catalog-v9`，先核验 manifest SHA256 `910F5D93B2165E86E7E41BF2C649C7BBDD3708592CF1C3AACC7AC29B50D9187D` 及 647 项 payload，再绑定已核验的 `experience-profile-v2` Test 根。默认有效 Standard、诊断 0、受控 Advanced 资格 0；不要将 Test 入口改名当作 Production。该 Test 保留 37 项隔离资料，目录关联仅供只读资料验证，本轮不启动游戏。凭据、profile、日志和缓存不在源码交付或程序包内。

标签分组依据完整已提交书架的作品编号，不依赖当前搜索／筛选可见范围。愿望条目属于书架，多安装不重复计数；原版／重制版仍保留各自身份。分页追加前后均归组，计数只表示已加载结果。资料导航、CG 准备／缓存、Markdown、CV 链接、居中标题和最终按钮间距的实际验证索引见本轮 `report.md`。

NSFW 偏好使用独立 `native-catalog-browse-v1.json`（Version 1、ProfileId、StoreId、ShowNsfw），默认 false；未知版本或身份不匹配时只读并保持隐藏。该开关不改书架／识别／封面选择或原有 `native-ui.json`。发现与标签通过浏览专用客户端使用 `catalog/works?q=...`／`tag_id=...` 和 `content_limit=sfw`；`nsfw=true` 是独立的事实年龄闸，保留 KunGal 标为 SFW 的 R18 作品。关闭时必须取得明确 `content_limit`，缺失或未知报错，不能推定安全。设置变化使旧搜索、标签页面和在途结果失效；提交中的资料事务不因此中断。

回归：v8 标签相关 Release 128/128，最终 v9 浏览／设置／网络／异步专项 Release 165/165，无跳过；真实 Test 验证回车搜索、标签分组与分页、开关开／关对照、保存退出重开，两次 v9 exit 0，37 项书架 hash 未变。此前 1012 项 Debug 和 83 项 Release CG／缓存回归按其版本继承；没有重新运行全仓、资源长测或整套虚拟机验收。历史失败和后续修复记录保留，不改成首次通过。

程序回退：从托盘正常退出新版，运行 `.artifacts/ui-catalog-refinement-20261009/launch-catalog-v8.ps1`，固定旧包 hash `225DF57FC36FE7B15CC3C9D1F01CE99945CC61DF2FC7E9B11979B37742324876`。同一 Test 身份，旧程序忽略新增 NSFW 偏好；v8 不具备新筛选行为。更早候选及入口保留。程序回退不恢复旧资料，不删除新缓存，不覆盖较新编辑。正式日用更新和 Production 资料恢复仍需各自授权。

本轮没有提交、主开发集成、日用替换、Production 写入、游戏执行或推送。源码身份、包 hash、进程与 Profile／Store、公开分类 API 证据、截图和完整回退边界见同目录 `report.md`／`checkpoint-progress.json`。

## 历史 UI v5／Recognition v7：隔离体验与可回退源码（2026-10-09，已由上节取代）

工作分支 `codex/library-ui-refinement`，HEAD `7facc502fde52532518171384cd77b45c9a60c9c` 加本轮未提交源码；应用父版本 `03f1f7bb0dfcc35957fe5ba4301cf21551a8dc88`。本轮没有创建应用提交，完整恢复须同时使用 `.artifacts/ui-refinement-20261009/delivery-source.json`、`candidate-ui-v5-runtime-tests.patch` 与同目录新文件副本。禁止用 HEAD 或相同 apphost 哈希代表当前候选，也禁止整体引入 `5b574f0` 基线复制。

普通权限体验入口：

```powershell
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/ui-refinement-20261009/launch.ps1'
# 或双击同目录 Start-The-Order-Test.cmd。
```

固定 `candidate-ui-v5`，核验 641 项 payload 和 manifest SHA256 `EBE6912CFD8FC9A367884B4A8DA393D50B055F8BE2E20B46217C49442A312222`。默认绑定 `experience-profile-v2` 的已核验独立 Test 身份，三项公开资料样本、三项隔离 dummy 安装、启动 EXE 为空、无真实 Session；Standard、诊断关闭、受控 Advanced 资格关闭。不访问 Production；不要把此入口改名当日用入口。设置 API 密钥使用现有隔离资料的凭据机制，密钥、profile、缓存及私人输入不进 Git／包。

体验 C1 前先从托盘正常退出普通 Test，再运行同目录 `launch-controlled-glass.ps1`，在 Settings 手动选 Auto 或高级优先；它使用另一 Test 根 `controlled-glass-profile-v2`，初始仍为 Standard，含明确标注的合成继续游玩记录与待核对建议。`launch-diagnostic.ps1` 独立开启诊断，使用自己的 Test 根，不改变普通入口 250 ms JSON 轮询关闭的设置。不要并行启动这些入口来冒充不同 profile 的独立窗口。

本轮新增识别选项存入独立 `native-recognition-ui-v1.json`：大小上限、严格版本／Profile／Store 身份、原子保存及备份保留，未来版本拒绝写回。既有 library/profile/store/writer 与 `native-ui.json` 契约未变；旧程序忽略附加选项，程序回退不自动恢复旧资料快照。个人编辑、封面选择、取消和异步提交保护继续有效。

识别 `recognition-v2-release-aware-7` 仅扩展检索层：最多 6 条有界查询、24 条近似索引记录，精确与同名集合优先；原有总搜索／富化预算不扩张。模糊命中只供候选召回，不能作为独立身份／精确索引证明。32 项新公开合成错字召回 0→32，不是自动应用结果；真实公共响应回放仍 54/60 正确自动、6 人工、20 负例拒绝，24 项真实在线样本回放 24/24；离线模拟单独报告。没有本轮新 HTTP 请求、公开索引重建或对私库定制名称／ID规则，普遍优于 PotatoVN 未证明。

验证：全量 native Debug 1994/1994、Release 相关 576/576，无跳过，native optical 回归；最后 Debug WinUI 0 warning／0 error、Release publish。v5 七项核心组件与受测版本 hash 一致；后续 XAML／布局修改完成真实点击／键盘、Light／Dark 与 480 DIP 请求尺寸补验。稳定 10 秒零绘制，最终 Library 4 创建／4 释放、Details 2／2、正常退出 0。不能据此覆盖新的全量 Private／GPU Gate；历史 Phase 7 证据保留原范围，未重跑资源长测。

集成准备仅提供补丁与新文件清单，**没有应用到主开发、替换日用或提交**。原主开发和长期源码工作区的 958 个文件／状态／分支未变。若后续获准集成，先保存主开发既有修改，按上轮 v6 的限定 runtime/tests 基线，再应用本轮 runtime/tests 差异及新文件；Lab、研究资料、生成器不是发行依赖。当前完整 diff 的逆向补丁只适用于其记录的基线且须先检查，不能直接在混有后续修改的工作区反向应用。

体验程序回退只需正常退出新版，再使用已保留 v6 `.artifacts/rnd/gpt-generalization-20261009/launch.ps1`；两入口使用各自 Test 根，不恢复或覆盖较新资料。需要共享 Test 根时先一致性快照，遵守原 Store 身份／writer 保护；旧算法建议需重查。正式日用仍保持当前已批准版本，Production 程序／资料回退分别授权。

实际结果、失败修复、源码身份、命令与截图索引：`.artifacts/ui-refinement-20261009/report.md`、`review.html`、`evidence-index.json`。没有录制新视频；系统主题托盘外观和宿主策略没有在本轮重做实机切换。正常入口启动后 Sky 前台识别错误的日志单列，不把工具故障写成应用 FAIL。

## 历史 Recognition v6：公共泛化复核与集成准备（已由上节取代，2026-10-09）

源码03f1f7bb0dfcc35957fe5ba4301cf21551a8dc88，算法recognition-v2-release-aware-6，codex/recognition-v2-generalization。**集成／日用更新仍待用户最终批准**。用户的普遍优于PotatoVN目标尚未由配对评测证明；主开发45b77f4和34项修改未动，历史候选与错误保留。

```powershell
# 普通权限，绑定既有隔离Test；Standard、诊断0、Advanced资格0。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/rnd/gpt-generalization-20261009/launch.ps1' -Wait
# 可显式传 -DataDirectory ABSOLUTE_TEST_PATH 使用新Test目录；非Test身份拒绝。
```

该入口核验640项payload及固定manifest SHA256 `500201A24746398FF6B9C41E998D91DDDFA2EB9CAE081EA048F931D522CB4823`。独立 `launch-diagnostic.ps1` 使用自己的Test根并显式开启诊断，不能改名作为Production入口。凭据仅通过现有Settings／隔离DPAPI机制；程序包与Git不含profile、密钥、私人输入或缓存。

**获批后集成**：先核验主工作区，为其34项既有修改建立主开发自己的检查点，再于集成分支应用本目录 `integration-runtime-tests.patch`（5b574f0→03f1f7b，19运行时＋9测试文件）。实际主工作区 `git apply --check` 已通过，未应用；排除基线复制、Lab和替代索引。Claude后的代码链38ace0f→78427b4→12320f6→c703898→03f1f7b；不得整体cherry-pick5b574f0。集成后补受影响回归并构建独立程序；获批切换沿已验证普通宿主流程备份，核验物理资料／Profile／Store／process后进行。旧算法建议成为NeedsRecheck，不静默重新识别整个已关联库。

**完整回退**：同目录 `rollback-runtime-tests.patch` 覆盖排名、身份、自动策略、提交复核、DTO／PE证据、DI、算法版本与相关UI/tests；只换ranker不足。保留程序回退 `D:/Projects/The-Order-candidates/phase8-2-electron-algorithm-parity-20261009/start-daily-electron-parity.ps1 -UserApprovedSwitch`，正常退出新版后使用，本轮未执行。程序回退不恢复旧资料快照；保留较新编辑。writer/profile/store/library schema未变，CatalogIdentity和ReleaseContext是有界内存事实，持久化契约不变。

最终全量Debug1969/1969、Release受影响533/533、定向88/88，无跳过；WinUI Debug／Release成功。合法既有Electron工具环境用于全量依赖测试，无删测／伪PASS。本次未重新操作桌面，复用v3实际交互的版本范围见报告。公共开发集真实NextMoe同响应51→54/60；新181正例离线首次盲测168正确＋1错误，修复后同集168正确＋0错误（不是新盲测）；新24真实API盲测及最终回放均24/24。用户36项作品正确36、确认版本35＋WA2安装年份未知1。

6查询／128候选／2详情2发行／5展示／60秒预算保留。繁简／副标题不擦除原身份；官方非机器中文等价受语言、长度、冲突、唯一性保护。没有新索引、游戏执行、Production改动或图形／资源调查。离线392/887ms p50/p95不代表联网或整页输入延迟。详情 `.artifacts/rnd/gpt-generalization-20261009/report.md`；当前足以供限定集成判断，不能宣布任意条件胜过PotatoVN。

## 历史 Recognition v3：验证、集成及回退记录（已由上节取代，2026-10-09）

本轮只准备隔离候选，**集成与日用更新仍需用户最终批准**。源码 `78427b406c6a58ba2a17be5d51a2e5865fc1e8e9`，算法 `recognition-v2-release-aware-3`，分支 `codex/recognition-v2-validation`；文档提交不改变已冻结程序。结果和证据在 `.artifacts/rnd/gpt-verification-20261009/report.md`。原主工作区HEAD45b77f4和34项既有修改未变，Claude原分支27b7166及其未入库资料保留。

```powershell
# 普通权限、仅隔离Test；默认Standard、诊断关闭、Advanced资格关闭。
& 'D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/rnd/gpt-verification-20261009/launch.ps1' -Wait
```

该入口核验559项候选hash和manifest身份，使用已验证的独立3条目Test资料；不会绑定Production。没有启动游戏。`launch-diagnostic.ps1`是单独的诊断入口，不能改名作为Production入口。普通体验可使用设置页现有凭据机制，密钥不写入报告或包。私有Test资料、回放数据、凭据和游戏路径不进入Git或交付程序目录。

**批准后的最小集成顺序（本轮未执行）**

1. 再次核实主工作区实际HEAD、34项原修改和协作者状态；先按原文件清单建立属于主开发的本地基线检查点，保留无关修改。`5b574f0`只用于差异参照，不cherry-pick。
2. 从该检查点建立集成分支，应用已冻结的 `integration-runtime-tests.patch`（相对5b574f0→78427b4，共16运行时文件和7测试文件）。当前现场 `git apply --check` 已通过，未修改主工作区。源码提交依赖链ba6c333→b2d8936→27b7166→38ace0f→78427b4；6ab590f是早期文档，不能作为最新结论。优先审阅限定补丁，避免混入Lab、VNDB索引生成实验和历史文档。
3. 同步原BRIEF／DEVELOPMENT的当前结果，运行受影响回归并构建独立集成候选，核对全部DLL／manifest。随后复用已验证的普通宿主Production切换流程，在资料锁下补做一致性备份并核对原Profile/Store、物理路径及实际进程；不能把Start-Test改名作为Production入口。
4. 日用切换只替换程序入口，保留原程序和较新资料。旧算法建议重开时成为NeedsRecheck；不静默重新识别整个已关联库，不清空个人编辑或Session数据。

**完整回退**

- 程序回退：正常退出新实例，使用已保留的 `D:/Projects/The-Order-candidates/phase8-2-electron-algorithm-parity-20261009/start-daily-electron-parity.ps1 -UserApprovedSwitch`。不自动恢复资料快照；后续较新编辑继续保留。
- 源码回退：回到上面主开发基线检查点，或在核对无后续冲突后应用完整 `rollback-runtime-tests.patch`。覆盖排名层、自动策略、提交复核、证据读取／身份、版本标记、DI装配和相关界面／测试；**仅切换Composition中的ranker不足以完整回退**。
- profile/store/writer/library schema未变化。ReleaseContext仅限内存且JsonIgnore；资料快照恢复与程序回退分别授权，恢复旧快照会丢失快照之后的修改。

最终算法v3的全量Debug1927/1927（界面说明修订前），最终源码Debug识别477/477、Release识别及FilesPresentation492/492、Debug／Release WinUI成功。已有Electron环境合法复用，历史3项依赖阻塞在这次全量中实际执行通过，不删／跳测试。真实NextMoe回放与离线模拟独立标记；390项独立样本找到的Dahlia误应用已修复，同集重跑只能称修复后回归。

在线查询6、聚合候选128、自动富化2、展示5、识别预算60秒及队列边界沿用；新增自动复核上下文最多本轮2个作品的有界发行结果，凭据失效时清空，未持久化。离线模拟p50/p95为438/1041ms，只代表本地回放；网络延迟和大库吞吐不据此推算。未升级依赖、启用新索引或重跑图形／资源长测。

## 历史 Recognition v1 候选交接（Claude 原报告，以下早期建议已由上节取代）

**集成顺序**
1. 先由 GPT／Codex 按自身流程提交 `codex-phase7-finalization` 中的 34 个未提交文件（即 `5b574f0` 的内容）。
2. 再 cherry-pick Claude 的识别提交（`5b574f0` 之后的提交）。冲突只可能出现在：
   - `RecognitionReviewService.cs`
   - `AutomaticRecognitionPolicy.cs`
   - `AutomaticRecognitionTests.cs`
   - `Composition.cs`
3. 早期回退建议曾仅切换 `Composition.cs` 的排名器并还原自动策略／提交唯一性。这不足以覆盖最终候选的完整修改，已作废；完整回退见上节。旧核对记录在版本变化时要求重新识别；`Homonyms` 字段缺省为空，双向兼容。

**代码位置**（均在 `native/`）
- `Infrastructure/Recognition/ReleaseAwareRanking.cs`：版本解析、发行标题／长别名提升、唯一性、具体性平局判定。
- `LocalTitleCleaner.cs`：发行格式噪声清洗。
- `RecognitionIdentityBuilder.cs`：清洗、每来源单一索引编号、同名记录、查询规划。
- `ExecutableEvidenceParser.cs`、`Platform.Windows/WindowsRecognitionEvidence.cs`：PE 健壮性。
- `Application/AutomaticRecognitionPolicy.cs`、`RecognitionReviewService.cs`：唯一首位与上下文复核。
- 测试：`Tests/RecognitionV2Tests.cs`。

**限额与数据**
- 查询 6、候选 128、富化 2、展示 5 均未改变。
- 自适应富化实验无收益，已撤回。
- 默认仍使用 PotatoDBMapper 索引。
- `scripts/build-title-index-vndb.py` 可由 VNDB 转储（ODbL／DbCL）生成同格式索引：
  - 实测 3.6 MB，保留内存 105 MB（现索引 52 MB），加载约 2.7 s。
  - 仅在 dev 上 +1 正确／−1 误应用。
  - 是否随包发布需评估许可（衍生数据库须 ODbL 及署名）与内存，本分支未接入。

**研究工具** `TheOrder.RecognitionLab`（未加入解决方案、不发布）：
- `inventory`：用生产扫描器与证据读取器只读采集，输出到指定私有路径。
- `replay`：用回放目录、记录的证据与指定引擎重放。
- `indexstats`、`rankbench`：测量索引加载成本与排序 CPU。

私有数据、目录转储和回放结果仅存于 `.artifacts/rnd/`，不入 Git。回放不是实时服务测试。实时 NextMoe 验证需用户提供 key，并在隔离 Test 进程中有限次执行。

## Phase 8.2：识别与封面历史规则补齐版已切换日用（2026-10-09）

原生识别核心已与实际Electron源码差分验证，本轮补齐默认EXE标题只读线索；可在识别页关闭，不执行游戏。封面采用用户确认的最终规则：资料源默认＋个人选择，本地个人封面优先；Details ⋯ 可选择作品封面或恢复资料源默认。同作品刷新保留选择，保存复核远程ID／URL／分级、凭据代次及资料token。没有更改schema、writer或Session契约。最新 >0.9 自动规则及书架Session自动同步继承不变。既有待核对条目需明确重新识别，不启动全库重扫。

```powershell
# 已批准并实际完成的日用切换；后续正常退出旧实例后可使用新入口。
# 普通宿主PowerShell，真实Production；Standard、诊断0、Advanced资格0。
& 'D:/Projects/The-Order-candidates/phase8-2-electron-algorithm-parity-20261009/start-daily-electron-parity.ps1' -UserApprovedSwitch
# 程序回退到上一版自动识别候选，不恢复任何资料快照。
& 'D:/Projects/The-Order-candidates/phase8-2-auto-recognition-20261009/start-daily-auto-recognition.ps1' -UserApprovedSwitch
```

558项候选文件及全部hash核验；新入口清除Test环境覆盖、检查原Production identity和物理资料边界，失败不回落。本次用户于普通宿主运行 `switch-parity-and-verify.ps1` 后，实际进程／DLL hash／writer owner一致；预先新建15文件资料锁内备份并验证独立恢复，启动时36作品／36安装／4Session／4次／466秒不变。程序留给用户正常使用，没有恢复旧快照、修改快捷方式或覆盖旧程序。资料恢复需要另外授权并先保护较新修改。

Debug／Release相关回归各1283/1283；最终Debug构建及独立Release publish通过；合成Test实际鼠标键盘选择／取消／恢复默认与正常退出已验证，未调用真实服务或启动游戏。源码仍 `45b77f4` 加继承及本轮未提交差异，精确文件清单／源码快照／包identity／失败尝试／实机与切换回执在 `.artifacts/phase8-2-electron-algorithm-parity-20261009/`。旧候选与历史证据不覆盖。Advanced仍限受控Test，未重跑资源长测，不代表Phase8全部完成或发布。

## 历史 Phase 8.2：自动识别版已批准并完成日用切换（2026-10-09）

导入保存后自动排队识别，排序分严格大于0.9时选择最高可安全应用候选；无高分、版本／身份冲突、证据变化及失败保留人工核对。启动补处理没有关联与既有核对记录的已安装条目；不重新识别已关联作品。候选来源URL新增浏览器跳转控件。未保存草稿、旧关联、个人字段和事务保护仍生效。详情见`.artifacts/phase8-2-auto-recognition-20261009/report.md`。

```powershell
# 原生Test身份，项目自有合成目录／HTTP／凭据，不访问私人库和真实网络。
# 此专用fixture需要Diagnostics=1；不是普通Production入口，不修改日用快捷方式。
& 'D:/Projects/The-Order-candidates/phase8-2-auto-recognition-20261009/try-auto-recognition.ps1' -Wait
# 已批准的新日用入口：先正常退出当前实例，在普通外部PowerShell运行。
# Standard，诊断关闭，Advanced资格关闭；不会恢复资料快照。
& 'D:/Projects/The-Order-candidates/phase8-2-auto-recognition-20261009/start-daily-auto-recognition.ps1' -UserApprovedSwitch
```

Debug／Release相关回归各548项通过，最终Debug构建及Release独立publish通过。源代码包含此前未提交的书架自动同步修复。用户批准“本轮完成替换”后，普通宿主PowerShell已启动新候选，实际PID4132／创建时间／WinUI DLL hash／真实Production identity／writer owner核验一致；启动核验时36个安装、4条Session、4次／466秒均未变。新建15文件一致性备份，旧程序与备份保留；启动后补识别可按授权继续更新未关联条目的资料，不要求后续library hash永远不变。诊断及Advanced资格关闭，不将合成fixture改名作日用。无schema或凭据格式变化，不需迁移或恢复旧快照。资料回退仍须另获授权，程序回退不恢复资料。

## 历史 Phase 8.2：书架自动同步修复版切换（2026-10-09）

本地修复使成功启动后的最近游玩、退出后的累计时长与继续游玩摘要自动同步，不依赖手动刷新。只查询当前应用-owned writer的已提交快照，不重载资料、不改变计时语义、日期定义或schema；选择与Details未保存草稿保留。源代码为 `45b77f4` 加本轮未提交差异；回归与候选身份见 `.artifacts/phase8-2-live-session-refresh-20261009/`。

```powershell
# 6项项目自有合成资料；不接触真实库。Standard，诊断／Advanced关闭。
& 'D:/Projects/The-Order-candidates/phase8-2-live-refresh-20261009/try-repair.ps1' -Wait
# 用户已批准本修复版；先从托盘正常退出当前实例，再于普通外部PowerShell运行。
& 'D:/Projects/The-Order-candidates/phase8-2-live-refresh-20261009/start-daily-repair.ps1' -UserApprovedSwitch
```

**DAILY-USE REPAIR SWITCH COMPLETE — OBSERVATION IN PROGRESS。** 用户已批准并通过普通宿主PowerShell实际启动修复版，进程创建时间／EXEhash、真实Production根及原Profile/Store identity、writer lock owner和2部作品数量均已核验；设置页Standard生效、诊断／Advanced资格关闭。资料自Phase8.1有变化，启动前在独占资料锁下另建15文件备份并验证独立恢复副本；启动前后library字节与数量不变，旧备份保留。程序留给用户，真实游戏自动更新仍待日用确认。未修改原正式程序目录或快捷方式。

入口复用Phase8.1物理路径及Production identity检查，核验独立包全部hash，清除Test环境覆盖，不允许失败回落；此执行器的LocalAppData虚拟化仍不适合直接打开真实Production。原 `phase8-1-20261009/start-daily-candidate.ps1 -UserApprovedSwitch` 可回到未改动的 `2b5e67a` 程序，后加 `-RollbackProgram` 则回到Phase6版本；两者均先正常退出当前实例，均不恢复资料。新备份及实际切换索引见本轮report；资料恢复需另获明确授权并先保护较新资料。源码未暂存或提交，不代表Phase8全部完成／正式发布获准。

## 历史 Phase 8.2：首次切换2b5e67a（2026-10-09）

**DAILY-USE SWITCH COMPLETE — OBSERVATION IN PROGRESS。** 用户已明确批准候选 `2b5e67a` 正常读写现有原生 Production，仅用于真实日用验证，不含公开发布或推送。应用未改动／重建，普通宿主 PowerShell 从既有 `start-daily-candidate.ps1 -UserApprovedSwitch` 启动；真实物理根、原 Profile/Store identity、实际 EXE、写入锁 owner 和资料数量已核对。原资料自 Phase 8.1 备份后所有权威文件哈希未变，因此沿用原独立不可变备份，无需另建相同快照。启动后的 library 字节及数量未变。窗口已返回真实书架并留给用户使用。

```powershell
# 普通 PowerShell 7，先从托盘正常退出当前实例；不要从 Codex 内置终端启动真实资料。
& 'D:/Projects/The-Order-candidates/phase8-1-20261009/start-daily-candidate.ps1' -UserApprovedSwitch
# 仅程序回退；同样先正常退出，不恢复或覆盖较新资料。
& 'D:/Projects/The-Order-candidates/phase8-1-20261009/start-daily-candidate.ps1' -UserApprovedSwitch -RollbackProgram
```

Standard 在实际设置页为选中且生效，普通入口关闭诊断轮询／Advanced资格。未修改现有正式程序目录或快捷方式。真实游戏必须由用户选择并启动；计时和结束记录、Production编辑保存、托盘隐藏恢复、退出重开及真实发生的异常恢复仍待实际观察，不以既有 Test 证据代替。本轮没有资源负载、ETW或系统设置更改。报告／宿主启动与身份回执见 `.artifacts/phase8-2-20261009/report.md`。资料恢复需要单独的明确授权，并先保护较新资料，不能随程序回退自动执行。

## 历史 Phase 8.1：准备完成时，实际日用切换待用户批准（2026-10-09）

**PHASE 8.1 PASS — READY FOR USER-APPROVED DAILY-USE SWITCH。** 分支 `codex/phase8-native-profile-protection`；复用Phase7冻结应用 `2b5e67a`，不重建。真实原生profile/store/library/UI版本均为1，不执行Electron迁移。12文件完整备份与逐字节恢复、受控Test身份副本、新旧reader兼容、真实副本GUI保存／重开、旧程序回退和资料恢复已完成；原始Production在宿主普通PowerShell最终核验12个hash不变。private payload／加密凭据字节只留在源码及程序包外的受保护私有根，不提交。详细身份、局限、失败尝试和恢复步骤见 `.artifacts/phase8-1-20261009/report.md`。

普通隔离体验：

```powershell
& 'D:/Projects/The-Order-candidates/phase8-1-20261009/try-private-copy.ps1' -Wait
```

候选完整目录为同根 `2b5e67a/win-x64`，635/635文件核验。入口绑定已恢复的真实资料Test副本，Standard、250ms诊断关闭、Advanced资格关闭。不要直接双击EXE，避免其Production build默认选择。

真实切换另待用户确认；批准后才在普通WindowsPowerShell运行同根 `start-daily-candidate.ps1 -UserApprovedSwitch`，程序回退另加 `-RollbackProgram`。该入口不是改名的Start-Test：显式Production授权、固定真实根、物理路径／身份／版本检查、全部程序hash核验、清除继承Test覆盖；虚拟化视图直接拒绝、不回落。旧程序位于同根 `rollback-phase6-7a3a739/win-x64`（553个文件）。程序回退不恢复资料；恢复旧快照会丢失快照后的修改，需先保留新快照并另获明确丢弃授权，不能自动覆盖。当前阶段禁止执行这两个Production入口，原程序／正式快捷方式不变。

本轮只生成和核验操作helper，没有修改native应用；继承Phase7按实际版本与范围的既有回归／资源／桌面／独立Windows证据，不重跑长测。真实游戏计时、Production保存、托盘与异常恢复留待用户批准后的日用观察。

## 历史 Phase 7 最终 RC 与体验入口（2026-10-09）

**PHASE 7 COMPLETE — RELEASE CANDIDATE READY FOR PHASE 8。** 不等于正式发布或 Production 授权。本轮仅归档已有验收与用户明确接受项，没有修改应用、重建、测试负载、系统设置或冻结包。

应用源码 **2b5e67a55d8eda905b8200652d189d7754dff112**；分支 `codex/phase7-finalization`，冻结 tag `codex/phase7-app-desktop-final-20261008`。完整程序 ZIP `.artifacts/phase7-app-acceptance-20261008/frozen-rc/2b5e67a/The-Order-x64-engineering-2b5e67a.zip` 为平铺根，SHA256 `1FD295C9B96CB8A319648481C634937ABB174490ECCA15822B80FF0B63C5D501`。目录／ZIP 内635个manifest项全部核验一致；运行时、许可、启动入口齐全。最终文档 HEAD、离线回执与历史报告身份在 `.artifacts/phase7-final-rc-confirmation-20261009/checkpoint.json` / `delivery-verification.json`；准确矩阵见 [PHASE7_FINALIZATION](docs/ai/PHASE7_FINALIZATION.md)。

普通隔离入口（不在本轮执行）：

```powershell
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-app-acceptance-20261008/frozen-rc/2b5e67a/win-x64/launch-test.ps1' -Wait
```

完整解压后也可双击 `Start-Test.cmd`。默认 `%LOCALAPPDATA%/TheOrder-RC/Test`、Standard、无250ms诊断；启动前核对所有文件并拒绝 Production 根／已有非Test资料。不要直接双击 EXE 绕过隔离入口。需现有384项合成内容时可用下节既有 `try-candidate.ps1`（依赖本机helper/seed/covers，和无开发依赖的便携入口不同）。只有显式 `-ControlledAdvanced` 才允许手动选择 C1；`-Diagnostics` 是独立诊断入口，不是普通体验。

**程序回退**：退出本次 Test 后，将完整旧包 `.artifacts/phase7-finalization/frozen-rc/The-Order-x64-engineering-91b98d6.zip` 解压到独立目录（ZIP 含 `win-x64` 根），核验旧manifest，再用旧包的 `launch-test.ps1` 指向明确的隔离Test资料。旧包 SHA256 `FD620EBF41DB500A11E7799E81FABFD06A653FBD71A5FFA5033FA406292F71F8`，不覆盖新冻结包、不 reset 源码。

**资料回退**：程序回退不会自动恢复资料。先退出 Test、保存当前闭合快照，再校验预备快照文件哈希、同一物理root／ProfileId／StoreId，确认是否有较新修改；没有显式丢弃确认不得覆盖。来宾已验证快照位于 `C:/Users/TheOrderTest/AppData/Local/TheOrder-RC-Acceptance/run-20261009-070202/snapshots/old-ui-saved` 和 `snapshots/before-explicit-discard`，只适用于对应 `profiles/upgrade` 的合成资料。不能把演练脚本当作正式产品的自动恢复功能，不能对 Production 运行。本轮不执行任何回退。

宿主默认高对比度方案名空字符串→“高对比黑色”及未来默认方案等价性未知，现为**用户接受的宿主测试环境残留**；不是精确恢复、应用修复或测试PASS，禁止继续为此改宿主设置。历史 FAIL／原协议／冻结包中生成时的未验收文案不改写，使用独立最终回执给出当前 Gate。用户接受只限此差异，未豁免其他应用／数据保护／资源要求。VM 本轮为 Off、原干净检查点保留，不自动启动或还原。

下一阶段需另行执行现有原生资料快照、版本升级兼容、真实日用、回退及正式发布验收；不执行 Electron→WinUI 迁移。本轮不进入Phase8、不替换正式程序、不推送或发布。

## 历史 Phase 7 独立 Windows 验收（2026-10-09，当时 RC GATE BLOCKED）

应用、冻结包和Standard默认均保持下节 **2b5e67a** 身份，没有新构建。Hyper-V来宾 `TheOrder-RC-Win11` / `DESKTOP-7GKL4M7` 的独立标准账户 `TheOrderTest` 已实际完成无SDK部署、首次空库／保存重开／单实例、旧91→新2b升级、程序移除重装、同根合成快照回退及真实注销重登录后读回。包内.NET／WinUI／WindowsAppRuntime模块路径核验，635/635一致，未安装开发工具或应用helper。原干净检查点保留；任务已清理，VM仍运行。

以下仅在该来宾标准账户执行，准确路径是平铺解压后的目录（旧91 ZIP另含win-x64）：

```powershell
& 'C:/Users/TheOrderTest/AppData/Local/TheOrder-RC-Acceptance/run-20261009-070202/programs/current/launch-test.ps1' -DataDirectory 'C:/Users/TheOrderTest/AppData/Local/TheOrder-RC-Acceptance/run-20261009-070202/profiles/upgrade' -Wait
```

仍为隔离合成Test、Standard、Advanced资格／诊断关闭；不直接双击EXE，不访问Production。未确认的新资料变更不能静默覆盖；闭合合成回退保留 `snapshots/old-ui-saved` 与 `snapshots/before-explicit-discard`，是操作演练，不是Phase8产品能力。日志／协议／进程和运行时身份／原始截图及hash索引在 `.artifacts/phase7-isolated-windows-20261009/run-20261009-070202/`。

用户将非常规Windows取消注销排除为问题／阻塞；其真实观测仍保留：app在取消前结束，重开恢复，不外推同一进程恢复。正常注销后的新Session2、资料hash一致及托盘exit0已实测。无资源长测、ETW、源码变动或系统安全配置变更；复用既有完整回归，不为换执行器重跑。

**ISOLATED WINDOWS VALIDATION: PASS（规定范围）。RC唯一剩余阻塞：宿主HC默认scheme原空串仍为“高对比黑色”**；只读重验开关／31色／动画一致，不盲改设置。不得报告Phase7全部完成、自动进入Phase8、替换正式程序或发布。下节08日记录及全部历史FAIL、候选不改写。

## 历史 Phase 7 App 桌面验收候选（RC GATE BLOCKED）

应用源码 **2b5e67a55d8eda905b8200652d189d7754dff112**，工作区 `D:/Projects/The-Order-worktrees/codex-phase7-finalization`，分支 `codex/phase7-finalization`，tag `codex/phase7-app-desktop-final-20261008`。新包 `.artifacts/phase7-app-acceptance-20261008/frozen-rc/2b5e67a/win-x64`，程序ZIP SHA256 `1FD295C9B96CB8A319648481C634937ABB174490ECCA15822B80FF0B63C5D501`；635/635目录/ZIP一致。完整解压，可双击Start-Test.cmd；旧435/f628/91保留作回退，不覆盖冻结候选。

```powershell
# 可移植空Test，默认Standard，无250ms诊断，不依赖SDK/helper/seed。
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-app-acceptance-20261008/frozen-rc/2b5e67a/win-x64/launch-test.ps1' -Wait
# 本机384项合成体验：依赖现有helper/seed/covers，每次独立Test。
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-app-acceptance-20261008/try-candidate.ps1'
# 显式受控Test允许手动选择C1，启动仍Standard。
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-app-acceptance-20261008/try-candidate.ps1' -ControlledAdvanced
```

普通入口无probe/250ms文字timer；独立`-EnableReadOnlyProbes`只按需getter，不冒充物理输入；包内`launch-test.ps1 -Diagnostics`是完整诊断。数据/权限/运行时安全规则沿用下节，不直接运行EXE绕过Test隔离。×隐藏、托盘正常退出仍处理草稿/写入/Session。

本轮修复caption对比度、卡片放大文字截断、跨页面搜索query/焦点竞态、复合按钮可访问名称。435完整native1813/1813，2b只新增三项Name、Debug/Release与28/28相关回归及实机provider通过，复用未变的核心/C1/资源证据，不重跑长测。真实OS策略/DPI/文字/键盘、人工Narrator指定流程和托盘通过，版本范围见report。第一次工具中止未归因于用户，reset后补验成功，原记录保留。

宿主已恢复App接管快照，最初CLI默认HC方案名空串仍未精确恢复；干净Windows/独立普通账户/实际结束会话另待。**RC仍BLOCKED，不允许Production使用或发布**。报告/候选/索引/检查点在 `.artifacts/phase7-app-acceptance-20261008/`，矩阵 [PHASE7_FINALIZATION](docs/ai/PHASE7_FINALIZATION.md)。程序回退与资料恢复分开，不静默覆盖较新资料，不reset其他工作区。

## 历史 Phase 7 Full Closure：独立便携工程候选（RC GATE BLOCKED）

工作区 `D:/Projects/The-Order-worktrees/codex-phase7-finalization`，分支 `codex/phase7-finalization`；当前应用源码 **f62860cd7b42dd92ca55887655e1e7a605127374**，新工程tag `codex/phase7-closure-engineering-frozen-20261008`。新包在 `.artifacts/phase7-closure-20261008/frozen-rc/f62860c/win-x64`，ZIP与同提交源码归档在其上一级；旧91b98d6候选／tag保留不变。先核对 `files.json` / `archive-identity.json`；解压必须保留整个目录。包内.NET10.0.12、WindowsAppSDK2.5.1、x64 VC14.50.35710、PRI/XBF、索引和许可已齐备；不依赖SDK、源码、helper、合成seed或covers。本机空profile medium权限的无seed/helper启动、app-local modules与635/635文件核验通过，未代替无SDK干净Windows。

```powershell
# 普通入口：Windows自带PowerShell5.1也已实测启动。默认空Test资料，Standard。
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-closure-20261008/frozen-rc/f62860c/win-x64/launch-test.ps1' -Wait
# 或解压后双击Start-Test.cmd；默认资料%LOCALAPPDATA%/TheOrder-RC/Test。
# 独立诊断入口仍有250ms诊断，因此不作为普通体验或发行资源证据。
& 'D:/Projects/The-Order-worktrees/codex-phase7-finalization/.artifacts/phase7-closure-20261008/frozen-rc/f62860c/win-x64/launch-test.ps1' -Diagnostics -Wait
# 仅显式受控Test授权Advanced；默认不选择/启用它。
# 同一入口可另加-ControlledAdvanced，再在设置中手动选择。
```

另用绝对 `-DataDirectory` 选择Test目录；入口在任何读取前拒绝Electron/native Production根，已有非Test资料拒绝打开。普通入口清除继承的Test/诊断变量，保存Advanced或Auto仍实际为Standard。设置独立文件 `native-material-v1.json` 不改变严格的旧UI schema；Opaque是「关闭透明效果」内部值。关闭×隐藏到托盘，真正退出仍走既有写入/草稿/Session收尾流程。日志 `native-runtime.log` 最多64KiB，不记录密钥、用户内容、路径或异常消息。

重建：按锁文件restore；`dotnet publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release -p:Platform=x64 -p:NativeProductionCandidate=true --no-restore -o ABSOLUTE_FRESH_PUBLISH`；随后 `native/scripts/package-native-rc.ps1 -PublishedDirectory ABSOLUTE_FRESH_PUBLISH -OutputDirectory ABSOLUTE_FRESH_WORKTREE_ARTIFACT`，必须使用新的.artifacts目录，不覆盖候选。构建需要开发SDK/VS，候选运行不需要。依赖开发机的合成体验入口与独立包入口不是同一种部署承诺。

最终clean f628 Debug／Release publish、90相关与完整native Release1813/1813回归通过。三项Electron合成密钥互操作对照在完整suite实际通过；测试工具使用本地 `node_modules/electron/dist/electron.exe`，不得把缺依赖当PASS或把Electron加进发行包。重复display surface分配修复后，两轮正常候选四组按原冻结384条／2×24／30秒静置／64MiB门槛与原分析器全部PASS，旧FAIL不改写。source／ZIP／manifest／执行DLL绑定，OpticalMaterial.dll与旧工程包字节相同。

真实HC／动画／透明／System主题／144%文字及CJK窄窗子集已验；实际keyboard／Narrator／accent／100/150%DPI与HWND视觉仍因Computer Use kernel-assets os error3 **BLOCKED_TOOL**。host默认HC方案名仍与原空字符串不同，其余采得flags／颜色／动画及registry文字／透明／appTheme精确恢复。干净Windows与真实宿主结束会话另留独立环境；**RC仍BLOCKED，不允许Production使用或发布**。可恢复检查点 `.artifacts/phase7-closure-20261008/checkpoint.md`，完整矩阵／前后对照／hash与回退见 [PHASE7_FINALIZATION](docs/ai/PHASE7_FINALIZATION.md) 及同目录report.md。普通源码与旧冻结候选可回退查看，不reset其他工作区。

## Phase 7.2 已冻结 Test 候选（2026-10-08）

本机最终验收应用源码 `14d8ede`，557文件Release候选 `.artifacts/local-development-integration/frozen-phase7-2-final/win-x64`。完整native 1793/1793、修复后Debug/Release及普通交互/诊断释放短验通过。历史资源协议FAIL和已获批有限开发集成例外原样保留；不允许默认高级材质或正式发布。

```powershell
# 当前推荐体验：启动前逐文件核验冻结哈希；每次全新合成 Test profile。
# Standard默认，高级材质默认OFF，不运行250 ms JSON诊断文字更新。
& 'D:/Projects/The-Order-worktrees/codex-claude-ui-integration/.artifacts/phase7-2-final-acceptance/launch-frozen.ps1' -Wait

# 独立诊断入口：同一冻结二进制、另一新Test profile；不改历史测试配置。
& 'D:/Projects/The-Order-worktrees/codex-claude-ui-integration/.artifacts/phase7-2-final-acceptance/launch-frozen.ps1' -Diagnostics -Wait
```

入口依赖已准备的本机manifest、profile-helper、384项representative payload及合成cover，不是可搬走的正式安装包。执行目录不影响上述绝对入口；不要直接运行EXE，以免丢失Test隔离参数。×隐藏，托盘菜单真正退出；不终止游戏。启动拒绝重复Run及候选hash变化。报告见本工作区 `.artifacts/phase7-2-final-acceptance/report.md`。

回退定位：最终应用 `14d8ede`，验收前分支 `8c4ddab` / 应用 `aa7dd7f`，旧557文件副本 `frozen-phase7-2/win-x64` 与原candidate-release保留；不需要reset其他工作区。下节旧普通入口仍绑定验收前候选，保留作历史，不是当前冻结体验入口。

## 当前本地开发集成 · 隔离 Test 体验（2026-10-08）

工作区 `D:/Projects/The-Order-worktrees/codex-claude-ui-integration`，分支 `codex/claude-ui-integration`，可运行源码 `aa7dd7f`。只在这个工作区操作；主开发、原视觉、Claude、审查工作区和冻结候选保留不变。用户已批准有限资源例外，完成开发集成；不代表资源协议全部通过或正式发布获准。

```powershell
# 普通体验：新建独立 Test profile，标准材质默认开启，高级材质默认关闭。
# 不运行 250 ms JSON 诊断文字更新；在设置中可手动选择 Auto/高级优先。
& 'D:/Projects/The-Order-worktrees/codex-claude-ui-integration/native/scripts/run-integrated-ui.ps1'

# 独立诊断入口：同一二进制，新 Test profile，显式开启现有诊断与受限 fixture。
& 'D:/Projects/The-Order-worktrees/codex-claude-ui-integration/native/scripts/run-integrated-ui-diagnostics.ps1'
```

本机入口已准备 Release 候选 `.artifacts/local-development-integration/candidate-release/win-x64`、profile-helper、Test seed 与原始合成 covers。每次生成独立身份，拒绝已存在的 Run；可以加 `-Wait` 等待正常退出并记录 exit/hash。两入口清除子进程继承的实验变量并恢复调用者环境，显式 Test，fixtureProfile=0，不复制凭据。合成图片仅在真实 Test manifest + 显式 covers 路径下启用，与诊断开关解耦；Production/Development 不选该图片传输。

右上角 × 隐藏到托盘，真正退出用托盘菜单。入口不扫描真实游戏、不运行游戏、不访问正式资料；请不要直接运行未指定 Test 环境的 EXE。材质设置只对本次会话有效。历史诊断入口与冻结协议未改；新诊断开关不改变那些历史 FAIL。

Debug/Release 构建、294 项相关回归、native optical 及两个新入口短验已完成；无需为体验重跑 GA1/RC1。构建使用锁定 SDK 10.0.401 / 现有依赖，未升级或安装。后续构建仍按本文件的原生构建流程，输出到本工作区新候选目录，不覆盖冻结候选或正式程序。集成/回退映射与准确证据范围见 [审查第11节](docs/design/claude-phase7/07_INDEPENDENT_REVIEW.md)。下方早期说明是历史记录。

## Phase 7.1 closure / Phase 7.2A Round 3（2026-10-07）

Phase 7.1 已正式 **PASS — PHASE 7.1 MEMORY MAJOR CLOSED / B0/M0/m0 / PASS FOR PHASE 7.2**。主工作区 closure 见 `D:/Projects/The-Order/docs/ai/PHASE7_PERFORMANCE_AND_RELIABILITY.md`；原 memory 诊断源码不复制进视觉分支。anonymous owner、global bitmap/GPU/COM、约3MiB residual、storage scaling、Level3独立验证均为 OBS，不再阻塞视觉工作。

Round 3 仅修复原六项：移除 Shell enclosing glass；窄窗零选择隐藏 bulk、范围/排序进入筛选、低频命令 overflow；Details 首屏去重标题并上移状态，无安装显示 compact next-action；Filter body 滚动而 footer 固定（条件仍即时应用，完成只关闭）；Dark brand semantic control resources 保持灰绿，系统 focus/high-contrast 不改；共享 owned Acrylic fallback 覆盖 Navigation/Toolbar/Details 与公开 FlyoutPresenter/MenuFlyoutPresenter，ContentDialog 用实色。OS caption/system menu 不在自有材质承诺内。

计时使用固定 240DIP 等宽数字区域；Test-only visual fixture 增加 bounded screenshot/layout/keyboard evidence 入口。Library 默认虚拟化与 inactive ItemsSource=null 保留；未修改 CoverPresenter/cache/业务服务/Session accounting。Level3 Gallery 保留 Test-only，未接入 Shell/Toolbar/Details。

本源码 checkpoint 后重新 build/publish，再补齐实际 DPI、键盘、text scale、motion/transparency 与六项 targeted evidence；完成后更新此处结果。新候选绑定与原始证据保存在 `.artifacts/phase7-2a-round3/`。旧轮 checkpoint/MVID 的证据保留为历史，不冒称本轮 final。视觉 Gate 只重审六项，仍须用户实际体验确认，不自动 merge/push/Productionize。


## Phase 7.2A 独立视觉审阅

从 `C:/Users/MUSK/.codex/worktrees/winui-phase7-visual/The-Order` 工作。主开发目录及 Phase 7.1 frozen candidate 不用于视觉构建。固定 SDK 与 Electron 合成测试 runtime 均已复制到视觉 worktree 的独立生成目录；没有升级依赖。

```powershell
# 可交互候选：始终指定当前视觉 worktree 的 Test profile，无 production 授权参数
./native/scripts/run-visual-review.ps1 -Candidate review -Direction B
# 同一候选的小范围 A 试作；先正常退出 B 再执行
./native/scripts/run-visual-review.ps1 -Candidate review -Direction A
```

只有显式 Test + diagnostics + VISUAL_TEST 入口提供材质实验室和合成会话状态展示。`visual-command.ps1` 是这个入口的有界测试控制协议：只接受页面、主题、布局、resize、search、material/motion、gallery 和 exit 等白名单操作；没有任意命令、游戏启动或正式资料操作。诊断文件不作为生产设置。

启动脚本在创建子进程后恢复调用者环境。`-TestProfile empty`、`installations`、`readonly`分别是独立空库、两份非可执行文本安装与未来Session格式的raw兼容fixture；最后一种不经过authoritative Native store，也不启用材质实验室/命令timer。不要把这些变体作为正式profile或游戏启动验收。等待窗口初始化后再发送Test控制，初始化会忽略旧nonce。

候选、合成素材和运行证据位于 `.artifacts/phase7-2a/`，不提交。直接双击 EXE 不等价于隔离审阅，请使用上述入口。完整视觉决策及验证边界见 [Phase 7.2A记录](docs/ai/PHASE7_2A_VISUAL_REVIEW.md)。下方 Phase 7.1 自检是历史记录，当前 memory MAJOR 尚未关闭。

## WinUI Phase 7.1：自检完成，等待 independent Gate

Phase 6 独立 Gate 已为 **PASS FOR DAILY NATIVE USE / PHASE 7**（`.artifacts/native-phase6-2-independent-gate/gate-audit.json`，受测代码 `7a3a739a3a758216419e8d5c0273a74e8e15561c`）。此前宿主虚拟化阻断已由 host handoff 与独立证据接续；下方历史段落不代表当前 Production 状态。真实 Production 现在是用户 authoritative store，Phase 7 不读取/复制/压力测试该目录，不访问 Electron 资料或真实 credential。

Phase 7.1自检已完成：Debug/Release完整各1763/0/0，locked restore/build/publish通过，两个最终连续两小时负载正常结束，资源检查PASS；B0/M0/m0/OBS9，首次失败与未知根因仍保留。**READY FOR PHASE 7.1 INDEPENDENT GATE**。详细方法、源码 checkpoint、容量边界、测量与完整soak见 [PHASE7_PERFORMANCE_AND_RELIABILITY](docs/ai/PHASE7_PERFORMANCE_AND_RELIABILITY.md)。不进入 Phase 7.2 / Liquid Glass，不做 schema、SQLite、业务语义或正式程序替换。

### 重现 Phase 7.1 隔离工作负载

从仓库根执行，固定 SDK，不运行默认正式入口。每次重测用新的 artifact 子目录；生成目录和 cover 文件不提交。

```powershell
python native/scripts/generate-performance-covers.py --output .artifacts/native-phase7-1/covers --count 10000 --seed 7101
./.artifacts/native-phase1/tools/dotnet/dotnet.exe restore native/TheOrder.slnx --locked-mode
./.artifacts/native-phase1/tools/dotnet/dotnet.exe publish native/TheOrder.Performance/TheOrder.Performance.csproj -c Release --no-restore -o .artifacts/native-phase7-1/harness
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll bench D:/Projects/The-Order/.artifacts/native-phase7-1/new-bench D:/Projects/The-Order/.artifacts/native-phase7-1/covers
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll session-checkpoints D:/Projects/The-Order/.artifacts/native-phase7-1/new-checkpoints D:/Projects/The-Order/.artifacts/native-phase7-1/covers
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll session-appends D:/Projects/The-Order/.artifacts/native-phase7-1/new-appends D:/Projects/The-Order/.artifacts/native-phase7-1/covers
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll media D:/Projects/The-Order/.artifacts/native-phase7-1/new-media D:/Projects/The-Order/.artifacts/native-phase7-1/covers
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll soak D:/Projects/The-Order/.artifacts/native-phase7-1/new-soak D:/Projects/The-Order/.artifacts/native-phase7-1/covers 120
```

除 soak 时长外，后两个可选参数是 entry count CSV / history count CSV；默认 `1000,5000,10000` / `1000,10000,50000`，seed 可由 `THE_ORDER_PERF_SEED` 指定。50k 默认 mixed history 的组成必须在结果中声明；full native 50k 超过既有 writer 容量，不能偷偷放宽合同。

Published WinUI 必须指定 `-p:NativeProductionCandidate=true`，这里仍只生成新的 artifact candidate。随后使用 `native/scripts/run-performance-ui.ps1`，明确传入 Candidate/Profiles/Covers/Output 四个绝对 artifact 路径；它设置 **Test** kind、禁用 raw fixture/online scenario、保存并恢复本次环境变量。先用 `generate` 模式建立相应 Test profile。默认 1k/5k/10k × (cold-ish + 5 warm)；`-Counts 10000 -Samples 0 -Rounds 0 -Minutes 120 -PaceMs 4000` 驱动同一实际 WinUI 进程两小时。真实托盘菜单和宿主 DPI/power 不由该 fixture 操作。

```powershell
./.artifacts/native-phase1/tools/dotnet/dotnet.exe publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:NativeProductionCandidate=true -o .artifacts/native-phase7-1/new-publish/win-x64
./.artifacts/native-phase1/tools/dotnet/dotnet.exe .artifacts/native-phase7-1/harness/TheOrder.Performance.dll generate D:/Projects/The-Order/.artifacts/native-phase7-1/new-ui-profiles D:/Projects/The-Order/.artifacts/native-phase7-1/covers
./native/scripts/run-performance-ui.ps1 -Candidate D:/Projects/The-Order/.artifacts/native-phase7-1/new-publish/win-x64/TheOrder.WinUI.exe -Profiles D:/Projects/The-Order/.artifacts/native-phase7-1/new-ui-profiles -Covers D:/Projects/The-Order/.artifacts/native-phase7-1/covers -Output D:/Projects/The-Order/.artifacts/native-phase7-1/new-ui -Rounds 360
```

`write-performance-binding.ps1` 对冻结 artifact EXE/DLL 保存 SHA256/MVID、PDB 与当前源码校验；回执的 checkpoint 才是二进制身份。新 harness 的 environment 同时保存运行 binary 的 informational version、MVID、SHA256 与 runtime repository HEAD，后者不能替代二进制身份。运行中的 host 必须来自冻结 publish 目录；继续构建时可指定独立 OutputPath，避免重写正在使用的 DLL。`check-performance-results.py` 检查完整 bench/checkpoints/media/ui/soak 输出，初次生成机器局部阈值，后续通过 `--thresholds` 比较；全量日志、失败、慢样本均保留。

实际 UI 的持续负载还应独立启动 `watch-performance-resources.ps1`，传入已核对的 Test 进程 PID、Candidate 和 Output 绝对路径。它按 executable path + creation generation 验证目标，每次关闭采样 handle，正常进程结束写入 PROCESS_EXITED；不枚举真实 Production 或其他应用。检查器示例（这些目录须先由各 workload 完成，不能以短测冒充两小时）：

```powershell
python native/scripts/check-performance-results.py --bench .artifacts/native-phase7-1/bench-605addd --checkpoints .artifacts/native-phase7-1/checkpoint-605addd --appends .artifacts/native-phase7-1/native-appends-final --media .artifacts/native-phase7-1/media-605addd --ui .artifacts/native-phase7-1/ui-gate --soak .artifacts/native-phase7-1/soak-605addd --ui-soak .artifacts/native-phase7-1/ui-soak-gate/10000-0 --ui-resources .artifacts/native-phase7-1/ui-soak-gate-os --output .artifacts/native-phase7-1/acceptance-append
```

完成后 `acceptance-append/thresholds.json` 是本机数据分布参考，含原生Session追加；`--thresholds <该文件>` 用于后续独立复测。首次生成阈值的运行只验证硬资源、accounting、媒体错误路径与持续负载条件；不能称为对既有 latency baseline 的独立回归证明。startup 的冷缓存定义、样本量和慢样本必须一并报告。`session-appends` 使用真实SessionEngine与native持久化、纯合成filesystem/process/clock/token适配器；每规模1 warmup +3 measured，cleanup不计入追加耗时，正常single-owned admission保持，不启动OS游戏进程。

## WinUI Phase 6.2：历史宿主阻断记录（后续独立 Gate 已通过）

Phase 6.1 checkpoint `ba37df62dbda23020dd1b85684b42953f999b430` 已独立 **PASS FOR PHASE 6.2**，证据 `.artifacts/native-phase6-1-provenance-regate/gate-audit.json`。该 Gate 实跑 Release 1724/1/0；新增 OBS6 为真实 handoff fixture 一次 RunningLimited，单独复跑通过，未建立与 profile provenance 的因果关系；原五项 observation 保留。

本轮用户首次明确授权 `%LOCALAPPDATA%\TheOrder\Profiles\Production` 的 fresh bootstrap 与最小真实验收，包括外部 safety snapshots 和一次受控 restore drill。Electron `%APPDATA%\KunGalManager` 仍完全禁止 enumerate/read/copy/import，旧资料不是回滚目标。初始 preflight 在当前宿主视图返回 absent，证据 `.artifacts/native-phase6-2/preflight.json`；随后发现 package 文件虚拟化，因此它不能证明未虚拟化的实际 Production root 不存在。正式数据一经创建不得 reset；异常先停用并保留快照。本阶段完成还需独立 Gate，不能提前进入 Phase 7 或批量日用。

启动前发现 6.1 刻意保留的 authorization API 未连到 WinUI/runner。6.2 最小接入 `--allow-production-profile`：仅显式 argv 设置当前 request 的 authorization；不能用于 fixture 或非 Production kind，默认启动仍拒绝正式 Production，授权不跨进程/请求自动保留。`NativeProfilePaths.Select` 只传递能力，既有 `ProfilePathGuard` 未修改；正式根的子目录/父目录及 Electron 仍不能由该授权放行。设置页仅纠正“隔离开发”旧文案，不改变数据/密钥模型。

显式启动参数形式如下；**当前已暂停，不能从这个 packaged 宿主继续重试正式启动**。后续须先证明真实根的只读 preflight 与启动均不经过虚拟化：

```powershell
& ./native/scripts/native.ps1 -Action Run -Published -PublishedPath '<当前 checkpoint-bound candidate 的绝对 EXE 路径>' -ProfileKind Production -AllowProductionProfile
```

未传 `-AllowProductionProfile` 时，runner 仍要求显式隔离 `-DataDirectory`。不得把此文档或启动参数当成未来真实资料操作的独立授权。候选、receipt、自动化与真实验收证据放在 ignored `.artifacts/native-phase6-2/`；真实验收结果尚待完成，不能把本节设计与接入说明当作验收通过。

本轮代码 checkpoint `7a3a739a3a758216419e8d5c0273a74e8e15561c`；locked restore、Debug/Release solution build 均成功（0 warnings/errors），全量各 **1734/0/0**，Debug targeted **648/0/0**、暂停后最终 Release targeted **648/0/0**；9 项新增授权测试，旧测试期望未改。发布目录 `.artifacts/native-phase6-2/publish-final/win-x64`，receipt 绑定 EXE、WinUI MVID、Application/Domain/Infrastructure/Platform.Windows/Presentation 程序集。独立辅助工具只绑定这份发布程序集，不是另一个生产应用。

同一候选在隔离 Production-kind 目录正常显示空库并退出。随后名义正式路径启动：13 个空 bootstrap 文件被创建，manifest 为 Ready，但读取真实文件身份时抛 `DataIsolationException`，未进入可用 UI。只读 handle 检查确认请求 `C:\Users\MUSK\AppData\Local\TheOrder\Profiles\Production` 的文件实际位于 `C:\Users\MUSK\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\TheOrder\Profiles\Production`，每个文件 links=1；证据 `production-read-only-file-identity.json`。此为 packaged 宿主的 LocalAppData 虚拟化环境阻断，精确物理路径保护按预期拒绝，不能因此放宽保护，也不能声称真正 Production bootstrap 成功。

已正常关闭失败进程并停止写入；外部 `safety/first-bootstrap-paused` 完整保存 13 个文件，`production-paused-snapshot.json` 记录来源、时间、checkpoint、逐文件哈希。此快照是失败现场保护，**不是成功的 clean Production 验收快照**。后续只读比较全部哈希不变。名义 root 与虚拟化物理 root 都不得为测试方便删除、重建、移动或改变身份。ProfileId `72bc5132-4033-033b-3abe-c528c67e3d7a`，StoreId `ed629dd4-3207-8e47-3c0e-d71d2f212efd`，Production/format 1；空 library，无 install/session/count/playtime/cover，默认 preferences，credential unconfigured。未读取 Electron 正式资料或凭据。

真实 library/cwd/cover/preferences 保存与重开、direct game、Game-B handoff、pause/tray、manager restart recovery、第二实例、正常 populated backup、一次 restore drill、恢复后写入、实际 Dev/Test/Prod 隔离及资源稳定性均**未验证**；不以自动化或隔离 smoke 代替。没有真实游戏启动、剧情/存档操作或 host sleep/reboot。当前结论 **PRODUCTION USE PAUSED**；环境 BLOCKER 1，尚无新确认产品 MAJOR/MINOR，历史六项 observations 保留。数据保留但尚不能接受为未来 authoritative profile，未 READY FOR PHASE 6.2 INDEPENDENT GATE。完整状态见 `production-readiness-summary.json`；停止后保存文档和证据，未推送、发布正式版或进入 Phase 7。

## WinUI Phase 6.1：Read Provenance / Stale-Write Remediation（等待 targeted re-Gate）

本轮从 `codex/winui-phase5` / `a0befa5895dd05181047be609d4545041f054a64` 干净基线开始，核对 `.artifacts/native-phase6-1-regate/gate-audit.json`、`child-race-results.json` 及上一轮 checkpoint receipt。最新独立结论 **REMEDIATION REQUIRED / MAJOR 1 / OBSERVATION 5** 优先于下方历史自检。已通过的路径 guard 和原 ownership 修复保留；scope reset `62da4b46829602e3f4852d094489bbf15d10113b` 不变，不进入 6.2。

根因：native borrow 用新 main 建立比较 baseline，后续 Phase 2 `Open/ReadMain` 独立读取遇到短暂锁，通用 fallback 却加载旧 backup。随后普通分类保存仍与未变的新 main baseline 比较，放行旧备注/收藏覆盖新 main。问题是 editable data 与 stale baseline 的 generation/source 不一致。

生产修改仅在 `NativeProfileStore` adapter：

- writer 持有同一份已验证 authoritative main envelope：其 payload 是加载来源，完整 envelope bytes 是 stale comparison token，既有 `RestoredFromBackup` 保留 recovery 来源，不增加 schema。constructor 无 I/O，验证/恢复/readback 全部成功后才转移 ownership。
- 实际加载时在 owner gate 内复核当前 main 与 baseline 字节一致，再从 baseline envelope 解出数据。sharing/temporary I/O/AccessDenied 抛 `LibraryTemporarilyUnavailable`，不会被 Phase 2 当作 corruption fallback；factory/Open 既有失败清理释放 writer。borrow 前的访问失败同样直接失败，解锁后重试重新读取当前 main。
- 已借出的 writer 不允许 `ReadBackup` 选择另一 generation。baseline 后 main 消失/替换/损坏会拒绝 Open，不能 silent backup writable open；外部修改后的旧 writer Save 仍被 `LibraryChangedOutsideWriter` 拒绝，baseline 不自动跟随外部文件变化。
- main 被证明 malformed/truncated/structurally invalid 或缺失，且 backup 有效、身份/版本/能力兼容时，在 native borrow 阶段先用既有原子 primitive 恢复 main，readback 验证后，以恢复后的 main 建立 baseline。此处是 native recovery-before-borrow，不是 Phase 2 `TransactionalLibrary.Open` 写盘；通用 Phase 2 Open、事务、业务 mutation 和 raw adapter 全部未改。主文件读取容量超 48 MiB 仍只返回 `LibraryRecoveryRequired`，要求显式 Restore 后 borrow。临时访问失败从不授权自动恢复。
- 沿用 owner gate、stream 的 using 和原子写 temp 清理，无新增长期 handle、timer、task owner。恢复替换前失败保持损坏 main/有效 backup 且无 borrower；替换后故障按既有已提交语义 readback，不伪报回滚。backup 恢复标记继续传给原 Session/recovery 逻辑，不改变 accounting。

验证和源码/二进制身份统一记录在 ignored `.artifacts/native-phase6-1-provenance/`；最终数值见下方验证记录。25 项新回归覆盖原 Gate 新备注/收藏 + 旧 backup + 普通分类编辑，真实子进程/同进程各三次 baseline 后锁、锁前/无锁对照、AccessDenied 注入、缺失/原子替换、恢复后外部 valid edit、bounded invalid backup、恢复发布故障、并发失败/赢家保护及失败路径资源循环。没有修改任何旧测试期望或资源阈值。

未修改 `ProfilePathGuard`、Phase 2 contract/atomic primitive、Session/accounting、Electron、共享数据模型、凭据格式或 durable schema；未访问正式 native Production、正式 Electron profile、正式凭据，未启动真实游戏、publish、替换程序或推送。AccessDenied 用注入验证分类，真实 Windows sharing 用实际文件/子进程锁验证；不声称真实 ACL 拒绝、长时间运行、跨机器或断电测试。原五项 observation 保留；本轮自检不代替独立 targeted re-Gate。

验证结果（2026-10-06，本轮实际执行）：

| 验证 | passed / failed / skipped |
| --- | --- |
| 新增 provenance/source 回归 | 25 / 0 / 0 |
| Profile / persistence / transaction 相关 targeted | 708 / 0 / 0 |
| 完整 native Debug | 1725 / 0 / 0 |
| 完整 native Release | 1725 / 0 / 0 |
| 历史 recognition remediation 分组单独复跑 | 44 / 0 / 0 |
| Windows Session 分组单独复跑 | 9 / 0 / 0 |

原 1700 项逐名称保留。最终两个全量均一次通过，顺序执行且没有与外部探针重叠；没有重现历史两项失败，也未声称其根因解决。locked restore 成功，Debug/Release solution build 均 0 warnings / 0 errors，Release 指定 `NativeProductionCandidate=true`。Electron 未改，不重复 Electron 全量。

原 Gate child-race probe 保留在原位置；本轮复制到独立证据目录，改为新输出路径、期望拒绝断言和解锁后分类保存断言，链接当前 Release DLL。锁前/无锁对照 + baseline 后真实 child 锁 3 次全部通过：main/backup 在失败 Open 前后字节不变，未产生 writable backup，retry/save 保留 new main/收藏 true，play count=3、total=71 不变，second owner exit=0、temp=0、dataLost=false。probe 加载的 Platform DLL 与全量受测 DLL hash 一致。

新增 30 轮 late-lock → Open fail → cleanup → unlock → retry/save 资源结果：单独 targeted **468→468**，相关 targeted **507→507**，Debug 全量 **671→671**，Release 全量 **676→676**，各自全部采样保持相同值，temp=0，后继 owner 可借用。既有混合失败循环 Release **655→657**（中间 658 平台期），原阈值通过，未改阈值。所有任务有界等待/结束，无新增常驻任务。初期新增测试的 ImmutableArray 比较重载错误保留在 `provenance-initial.trx/log`，修正断言方式后验证实际 Notes/Favorite/Category，不放宽期望。

本轮整改自检 **BLOCKER 0 / MAJOR 0 / MINOR 0 / OBSERVATION 5**；**READY FOR PHASE 6.1 TARGETED RE-GATE**，仍需独立复审。当前构建内嵌父 checkpoint a0befa5；`validation-audit.json` 绑定源码/程序集/TRX hashes，提交后 `checkpoint-receipt.json` 绑定 remediation commit，不冒称构建内嵌事后生成的 commit。完成独立整改提交后暂停。

## WinUI Phase 6.1：Guard + Writer Exception-Safety Remediation（等待 targeted re-Gate）

本轮开始核对 `codex/winui-phase5` / `f797809983111e3f0f6becfc484dc13a803b203f` 干净基线，以及 `.artifacts/native-phase6-1-gate/gate-audit.json` / `independent-probes.json`。独立 Gate 的 **REMEDIATION REQUIRED / MAJOR 2 / OBSERVATION 5** 优先于下方历史自检结论。只修 M1/M2；scope reset checkpoint `62da4b46829602e3f4852d094489bbf15d10113b` 与 Phase 6 范围不变。

### M1：共享 pre-I/O namespace / alias guard

旧 raw/fixture guard 用保留 `\\?\` 前缀的字符串与普通 DOS 正式根比较，扩展路径未匹配禁区，就先执行父目录/marker 查询。新增 `ProfilePathGuard` 统一供 `NativeProfilePaths.Validate` 与 `IsolatedLibrarySource.ValidateDirectory` 使用：先纯字符串分类、规范化、父子 overlap 与授权判断，返回规范 DOS root 后才进入原 filesystem / reparse / pin / final-path 检查。

支持本地绝对普通 DOS 与 extended DOS；统一斜杠、末尾分隔符、`.`/`..` 和 Windows 大小写比较。普通/extended UNC、device/NT/GLOBALROOT/volume namespace、相对路径、ADS、短名 `~`、歧义末尾点/空格与设备保留名在 I/O 前拒绝。没有环境变量展开。Electron 正式根及其父子路径始终拒绝；默认 native Production 仅匹配 exact root + Production kind + explicit authorization；默认 Development 仅匹配 exact root + Development kind。raw/fixture 不获上述 capability，不能碰默认 native roots。

正常 Program 的 profile selector / `ForNativeProfile`、fixture 的 `ForProfile`、explicit Test、bootstrap/open-existing、library factory/source/raw persistence、credentials/preferences、Session recovery、launch positions、review/cache，以及 owner 的 backup/restore/borrow/read/write 均汇入上述两处边界。子进程 ProfileProbe 也先验证 root，再读取 synthetic parent marker；未复制另一份路径规则。已打开 owner 在 `ValidateAccess` 复核授权，新增恢复基线路径没有绕过它。

`ProfilePathGuard.Probe` 是 internal、AsyncLocal 的测试 seam，没有环境变量/CLI 配置入口：用不存在且不访问的 synthetic-equivalent known roots 替代正式根，并在两条路径的**首个文件访问边界**记录/抛错。它不是系统级 I/O tracing，也不声称覆盖恶意 filesystem 替换攻击。新增 M1 **321** 项：298 个 alias/入口组合、12 类不支持命名空间各遍历 20 入口、9 个已打开 capability 操作、纯授权/正向边界和有效隔离 extended-root 实际 I/O。拒绝矩阵全部 `RejectedBeforeIo=true / FilesystemQueryCount=0`；正向 seam 对照为 1，隔离 extended-root 能正常读写。未拿真实 Production 目录试探。

### M2：先验证状态，成功才交付 borrower

旧实现先置 `libraryOwned=true`，constructor 字段初始化再读 main；锁冲突/超限读取抛错后无 borrower 负责释放，导致 retry/restore/dispose 均被 phantom ownership 阻止。

现在在原 owner `gate` 内检查已有 borrower，核对 backup compatibility，再有界读取 main 并验证 main 或允许的 fallback；constructor 仅接收已建立的基线，不执行 I/O。最后一步才置 `libraryOwned=true` 并返回。任何此前异常不改变所有权；没有可能清除其他成功 borrower 的 catch rollback。成功 borrower 由原 Dispose 幂等释放；factory 取消或后续 Phase 2 open 失败沿原 cleanup 释放。没有新增 OS handle、timer、CTS 或持久化字段。

锁定 main 允许失败，解除锁后重试成功。malformed/truncated/缺失 main 的有界比较 token 与经验证的 backup 保留原 Phase 2 fallback；打开本身不写盘。外层超过 48 MiB 时无法安全建立比较基线：验证 same-store backup 后明确 `LibraryRecoveryRequired`，不借出 writer、不隐式恢复，允许 `Restore("library.json")` 后再 borrow。覆盖 48 MiB + 1 byte、49 MiB 与 80 MiB，不对 Gate 文件大小写特例。Restore、borrow、dispose 使用同一 gate；`CloseLibraryBeforeRestore` 仍只对应真正 active borrower。原逐字节 main 比较、manifest identity/version/kind/minimum writer/capabilities 与 backup future/foreign/schema 门禁保留。

新增 M2 **16** 项：真实第二进程独占 main、失败/解锁重试/失败后 dispose/真实 second-owner borrow；三类损坏组合恢复；错/未来/损坏/未知字段 backup 拒绝；容量边界；十轮双线程竞争及失败方不能释放成功方；取得 lease 前/后的 recovery 失败；factory 取消前/中；失败资源循环。组合路径覆盖 Ready→锁定失败→重试写入→backup→损坏→重开/recovery→再写→重开，ProfileId/StoreId/Kind、workingDirectory、count/total 和数据保持，无 temp 泄漏；恢复后外部改 main 仍拒绝 stale write。

### 本轮验证与边界

本机日志/TRX 在 ignored `.artifacts/native-phase6-1-remediation/`。相关 targeted **753 passed / 0 failed / 0 skipped**，包含新增 M1/M2 **337**、原 profile **85**、Session/persistence/transaction **331**。旧测试及阈值未改。初次新测试编译发现 helper 名称/collection expression/analyzer 问题；首轮新测试误将 credential 的既有脱敏异常当原始隔离异常，且 thumbnail 输入不符合既有 key 格式。仅修正新测试的有效输入和既有异常契约，保持零 I/O 断言及生产行为不变，失败日志/TRX 保留。

最终完整 Debug/Release 各 **1700 passed / 0 failed / 0 skipped**，逐名称核对原 1363 项全部保留；两配置全量一次通过，本轮未再出现历史 recognition 数组越界或 Windows Session timeout。locked restore 成功，两配置 solution build 均 **0 warnings / 0 errors**；Release 使用 `NativeProductionCandidate=true`。没有重新 publish 或 UI 手动验收，旧 publish 仍为旧 checkpoint。源码/程序集/TRX hash 见本轮 `validation-audit.json`，提交后由 `checkpoint-receipt.json` 绑定整改 SHA；构建内嵌父 checkpoint f797809，不冒称内嵌后来生成的 remediation commit。

失败路径资源：预热 5 轮后 30 轮共 120 次 sharing failure、30 次 capacity failure、每轮 recovery/restore/reborrow；targeted 句柄 **472→472**，Debug 全量中 **672→672**，两者 30 个采样均不变。Release 全量 **650→653**，前 9 个采样 650，后 21 个均为 653，无持续线性增长，原 +6 阈值通过。每次测试 35 个 owner 均释放、临时文件 0，最终新 owner 可借 writer。该证据仅覆盖本机短轮次，不等于长期无泄漏保证。

未改 Phase 2 transaction / atomic primitive、Phase 5A/5B facts/accounting、Electron 或共享兼容层，不重跑 Electron 全量。未访问真实 native Production、正式 Electron profile 或正式凭据，未启动真实游戏，未替换程序/重新 publish/推送，不进入 6.2。原 5 项 observation 保留：多组件备份非全局原子快照、性能、本机短轮次资源、历史 recognition 数组越界、历史 Windows Session timeout；不因本轮回归通过宣称根因已解决。真实断电、长期/跨机器、UI 手动操作与恶意 filesystem 竞态不在本轮新验证范围。修复候选仍需独立 targeted re-Gate。

**READY FOR PHASE 6.1 TARGETED RE-GATE**。整改自检 BLOCKER 0 / MAJOR 0 / MINOR 0，保留 OBSERVATION 5，不代替独立复审结论；独立 remediation checkpoint 完成后暂停。

## WinUI Phase 6.1：Production Store Bootstrap（自检完成，等待 independent Gate）

开始时核对 `codex/winui-phase5` / `b2a360a005aecd8bea1b18571eb78b75259c115b` 干净基线。范围重设纯文档 checkpoint：**62da4b46829602e3f4852d094489bbf15d10113b**。旧 [PHASE6_MIGRATION_AND_STORE.md](docs/ai/PHASE6_MIGRATION_AND_STORE.md) 顶部标记 **SUPERSEDED**，仅保留历史；其重要 Electron 资料必须无损迁移的前提已失效。当前规范与真实文件布局见 [PHASE6_PRODUCTION_PROFILE_BOOTSTRAP.md](docs/ai/PHASE6_PRODUCTION_PROFILE_BOOTSTRAP.md)。Phase 6.2 已改为 Production Readiness / First Real Profile，以后另获用户授权；下方旧阶段 migrated-store/cutover 义务由本节物理隔离与 fresh store 方案取代。

### 当前实现

- `NativeProfilePaths` 区分 Development / Test / Production；native Production=`%LOCALAPPDATA%\TheOrder\Profiles\Production`，Development 为同级 `Development`，Test 必须显式提供隔离目录。与 Electron `%APPDATA%\KunGalManager` 完全分离。路径规范化、父链 pin、拒绝重解析/硬链接文件/歧义别名、真实 Production 授权检查均在资料访问前执行。真实 Production 只保留显式授权 API 接入点，当前 GUI/runner 不开放该授权。
- `NativeProfileStore` 从初始化前到应用退出持有唯一 `.the-order-writer.lock`；库、Session、偏好、credential、核对建议、启动位置共用 owner。借出的 library writer 也只能一个。身份 manifest 与 component/backup 同时验证 ProfileId / StoreId / Kind / physical binding；minimum writer、capability、未来格式、未知字段拒写，包括 main 正常但 backup 属于未来或其他 store 的情况。
- Initializing 身份锚→空库/原生默认偏好/空凭据与 companion backups→Ready 原子发布→重开验证。GUID 根据物理目录身份确定，manifest temp/replace 前中断仍保持同一身份。缺旧身份而调用者要求已有 identity 时拒绝，不能另建库。续作先检查所有已有数据只能是预期空值；Ready 缺件或身份损坏不默默重置。
- Phase 2 原子写入实现提取为共享 `WindowsAtomicFile`；现有隔离 persistence 和新 profile adapter 共同复用。候选 round-trip、串行库 transaction、last-good、stale commit、提交后 warning 语义保留。唯一相邻接口扩展是可选 `ILibraryRecoveryProvenance`，使显式 restore 后的 main 仍以 Backup 来源交给原 SessionEngine；普通编辑不能清掉待恢复 Session 的备份来源。
- `CheckpointBackups()` 与 `Restore(name)` 为可执行 API；Restore 要求关闭库，验证 backup 和 identity，再原子重建 main 并重开验证。逐组件 last-good 不等于跨组件原子快照；未知/未来/异库 backup 不被正常保存覆盖。库损坏/缺失也能沿原 Phase 2 自动读 backup，明确保存后写回。无高级 backup UI、跨物理根 adopt 或 Electron rollback。
- 新增 manifest/envelope v1；现有库内部、Session、workingDirectory、cover Base64、DPAPI payload 与 UI preference model 保留。外层 Data Base64 包装有约 1/3 磁盘开销。新库空 history/count/total，无 historical Electron floor 导入。凭据只测试 synthetic 值；无 legacy credential 或 localStorage 读取。

### 验证入口与证据

证据统一在 ignored `.artifacts/native-phase6-1/`，最终 counts/source hash/binary hash/TRX/UI记录由 `validation-audit.json` 与 `checkpoint-receipt.json` 绑定。新增测试为 `NativeProfileTests`、`NativeProfileBoundaryTests`、`NativeProfileIntegrationTests`；`TheOrder.ProfileProbe` 是受显式 synthetic parent marker 约束的真实子进程测试 host，不是产品入口。

```powershell
& ./.artifacts/native-phase1/tools/dotnet/dotnet.exe restore native/TheOrder.slnx --locked-mode
& ./.artifacts/native-phase1/tools/dotnet/dotnet.exe build native/TheOrder.slnx --no-restore -c Debug
& ./.artifacts/native-phase1/tools/dotnet/dotnet.exe build native/TheOrder.slnx --no-restore -c Release -p:NativeProductionCandidate=true
& ./.artifacts/native-phase1/tools/dotnet/dotnet.exe test native/TheOrder.Tests/TheOrder.Tests.csproj --no-build --no-restore -c Debug
& ./.artifacts/native-phase1/tools/dotnet/dotnet.exe test native/TheOrder.Tests/TheOrder.Tests.csproj --no-build --no-restore -c Release
```

开发 build 默认 Development；publish 必须显式 `-p:NativeProductionCandidate=true`，runner 已提供此标记，发布包默认 Production，真实路径仍受 guard 拒绝。`native.ps1 -Action Run` 必须给绝对 `-DataDirectory`；可显式 `-ProfileKind Production` 选择临时 production-like 根。新 profile 不要求 Phase 1 marker，也不自动 adopt 旧目录。旧 raw fixtures 必须加 `-FixtureProfile`，`check-published-runner.ps1` 同样支持此显式参数；历史命令省略参数不再代表可直接复用。禁止把此处命令当成正式 profile 操作授权。

实际 Windows UI 已用隔离 `.artifacts/native-phase6-1/ui-production-like` 从 Debug 启动空库、切换列表、正常退出，再由 Release 发布入口重开；相同 ProfileId/StoreId、空库和列表偏好保留。还验证发布构建不传 `PROFILE_KIND` 时默认 Production。初次 UI 点击两次遇到工具 `coordinate input geometry is unavailable`，重新观察后操作成功；失败点击未冒称保存/退出。只测试 bootstrap/startup/preferences/exit，没有扩大 UI、Tray、Single Instance 功能。

故障覆盖：root 创建、manifest 前/temp/replace、library/settings 中断、单文件/companion 缺失、Ready receipt lost、reopen 前中断、writer 冲突、manifest 损坏、身份/类型/未来字段、backup/main/restore 失败。每个 bootstrap 点同时做 exception injection 与真实子进程 `Environment.Exit(86)`，不执行 managed finally，重启后重开验证。两子进程同时首次启动只有一个 owner；另一个明确冲突。真实 synthetic game 覆盖 fresh→launch→Session/count/time→管理器退出不 kill→同身份恢复→退出→backup→corrupt main→restore→reopen；备份来源本身不能重新接管活动进程。另覆盖 cwd、remote/personal cover、credentials、preferences 和跨 Dev/Test/Prod-like 的全部写入隔离。

最终源码冻结后，Debug `full-debug-verified.trx` 与 Release `full-release-verified.trx` 各 **1363 passed / 0 failed / 0 skipped**；原基线 1278 项逐名称全部保留，新增 85 项。Recognition 分组 `recognition-recheck.trx` **44/0/0**、Windows Session 分组 `windows-recheck.trx` **9/0/0**、独立性能/资源 `performance-resource.trx` **4/0/0**。locked restore 成功；最终 Debug/Release solution build 各 0 warnings/errors，Release publish 成功。早期定向 252 项通过属于迭代证据，不替代最终完整测试。

最终候选为 `.artifacts/native-phase6-1/publish-final/win-x64`；Platform DLL SHA256=`B5108FB78136A31DD2181BE97DE2C99650DB3FE4740D5E70F57C6471DDBC15ED`，MVID=`aaab99ab-17af-4bf9-820f-8e03216dd325`。最终 UI 重开并正常退出，进程退出码 **0**，见 `ui-final-completion.json`。184 个 native source/project/lock/script 文件及 12 个 fixture 文件纳入 audit；源码最终提交由 `checkpoint-receipt.json` 绑定。二进制构建时 HEAD 为 scope-reset checkpoint 加本轮实现工作区，不冒称程序集内嵌了后续 implementation commit。

独立 Release 性能采样（synthetic native store；单位 ms，四项均为实际 profile API）：

| 条目 | Open | Write | Backup | Restore |
| --- | ---: | ---: | ---: | ---: |
| 1k | 68 | 50 | 67 | 195 |
| 5k | 987 | 623 | 562 | 1682 |
| 10k | 1280 | 841 | 1104 | 3929 |

对应文件约 1.34 / 6.68 / 13.36 MB；该独立测试进程最高记录 peak 约 315 MiB（累计进程峰值，不是单操作或每个规模的独立内存测量）。30 轮 open/conflict/backup/restore/close handles **435→437**，在原有容差内；最终完整 Debug 同项 **741→741**。这证明短轮次有界，没有声称长期零增长或大封面压力已通过。

两项中途失败完整保留，不修改原测试或阈值：`full-debug-complete.trx` 中既有 `InvalidLiveCandidateStructureCannotRestore(fault: "conflicts")` 在准备 candidate 时数组越界；`full-release-complete.trx` 中既有 `RealFiveAProcessFactsCreateSingleHistoryAndFinalExitUsesOsOccurrence(mode: "game")` 等待 terminal 超时。随后各自分组及最终完整 Debug/Release 均通过；根因未确定，不能据此断言与本轮无关。列为 **OBSERVATION 2**，独立 Gate 需留意其稳定性。

**READY FOR PHASE 6.1 INDEPENDENT GATE**。自检未发现新的 BLOCKER / MAJOR / MINOR，独立 Gate 尚未进行。完成 implementation checkpoint 后暂停，不推送、不进入 6.2。

### 范围与未验证边界

Electron 源码、格式和 writer 未改，因此本轮不重跑全量 Electron；隔离 legacy fixture 继续被改写时 native hashes 不变。没有 migration engine、SQLite、localStorage exporter、credential migration、media refactor 或 Phase 6.2 工作。正式 Electron profile **未访问/未复制**，真实 native production profile **未创建/未打开/未访问**，正式 credential **未读**；没有启动真实游戏、替换正式程序、删除 Electron 或推送。

OBSERVATION：恢复演练只覆盖本机同一物理 profile；跨机器/根 adopt、真实断电耐久、真实首份 profile 和长期大封面压力尚未验证。原 5B host sleep/hibernate/logoff/reboot 与终态 occupancy 生命周期边界保持。备份是组件 last-good，非跨组件全库原子快照；fresh schema 的 v1→v2 upgrader 未实施。独立 Gate 尚未执行，自检不替代独立通过。

## WinUI Phase 5B.2：Persistence Retry Remediation（等待 targeted re-Gate）

本轮从 `codex/winui-phase5` / `9f1aef2e944f8ad07c31a5b43f6f7c84b97d678d` 的干净工作区开始。已核对它相对受测生产 checkpoint `a11704b902d0c643385efa2209713f3f7fff8c01` 只有两份文档变化。独立 Final Gate 的 **REMEDIATION REQUIRED / MAJOR 2** 优先于下方历史自检结论；本次仅整改初始化与终态持久化重试，尚未通过独立 re-Gate，不代表 Phase 5B 完成。

### 两项修复及所有权

- MAJOR 1：`AppSessionHost` 原来永久缓存 faulted initialization task，recovery commit 的瞬时失败同时阻断后续初始化和正常退出。现在将 host lifetime 的 `creation` 与可替换的 initialization attempt 分开；engine 创建回调只调用一次，复用同一 writer/engine。原 `gate` 只允许一个 attempt，并发 Initialize/Explicit Exit 等待同一任务；成功后逻辑初始化保持幂等，失败或取消后可重试。Explicit Exit 在存储恢复后先重试 recovery，再走原退出保存；持续失败仍取消退出并保持游戏运行。
- Host 拥有一个创建任务和当前初始化任务，不保留失败任务链，不新增 semaphore、timer、OS handle 或 cancellation source。Dispose 先封闭新初始化/退出请求，等待在途初始化及退出，再释放唯一 engine；在途完成不能把已开始清理的 host 重新置 Ready，清理后不允许 retry。
- MAJOR 2：`CheckpointForExitAsync` 原来把内存 terminal 等同于 durable terminal，跳过未保存的 Incomplete/Completed。现在仅对 nonterminal 执行退出状态转换，但两类状态都进入原 Save 路径；通过既有 `Runtime.Record` 与 `Runtime.Durable`（成功 transaction receipt 才更新）判断同一 terminal candidate 是否已经提交。终态且两者相同则 no-op，不引入独立 SaveFailed 布尔或 durable schema。
- 终态候选的 final delta、AccountingCutoff、EndedAt 和 SessionId 固定；失败、重试不再刷新其时间边界，不重新打开 Session、不重新计时。pending revision 继续由最后确认的 Durable 推导；成功回执才 acknowledge。普通 checkpoint 已保存同一终态时，后续 Exit 无额外写入；较旧 checkpoint 先完成时不能吞掉排队的终态提交。Phase 2 原子事务及 revision replay 规则保持不变。

### 验证与证据边界

新增 `LifecyclePersistenceRetryTests.cs`，共 **22** 项：IOException/UnauthorizedAccessException/取消、持续失败后恢复、Explicit Exit 触发 retry、并发 Initialize/Exit、成功幂等、Dispose 与在途 retry、Incomplete/Completed 最后 delta、普通 checkpoint 前后交错、两项故障组合、真实隔离原子文件替换前/后故障及替换后丢回执重试。旧测试与阈值未改。新测试首次编译有一处错误 namespace；首次运行 3 项把 Explicit Exit 完成后的 tracking 误期望为 CurrentProcessOnly，按既有退出契约区分 Detached 后通过；未将生产退出行为改为继续观察。

本机证据位于 `.artifacts/native-phase5b2-remediation/`。新增测试 **22/0/0**，lifecycle/Session/recovery/occupancy/fault injection targeted **192/0/0**；locked restore、Debug/Release solution build 均通过，两配置 **0 warnings / 0 errors**。最终完整 native suite：Debug/Release 各 **1278 passed / 0 failed / 0 skipped**，逐名称确认原 **1256** 项全部保留。

首次 Release 完整运行 **1277 passed / 1 failed**：未修改的 `RepeatedRealHandoffReleasesProcessJobSnapshotAndObserverHandles` 测得 `[644,644,644,644,644,642,645,652,652,652]`，超过原第 3 轮 +6 的阈值。保留初次 TRX/log；不改代码、测试或阈值，单项复验通过（`[458,460,462,462,462,462,462,462,462,463]`），随后重新运行完整 Release 1278 项全部通过。Debug 完整运行此项为 `[653,653,653,653,653,651,651,651,651,651]`。这是一次未稳定复现的资源阈值波动，归入本机短轮次资源观察项，不把重跑绿灯写成长期无泄漏保证。

未改原 Gate probe 的 `Program.cs`，通过单独忽略目录中的项目引用当前 Debug DLL 复跑，探针加载 DLL 与受测 Application DLL hash 相同：原三条路径现在退出重试全部 true；Incomplete/Completed 各仅补一次写（2→3），后续退出 no-op；同一 Session/count，游戏仍运行。独立探针的 CS0649 未赋值替身字段警告与 solution build 分开记录。

资源短轮次：80 次 recovery commit 失败及 40 次终态 Exit retry，临时 observer handles 每轮均为 0，最终成功并释放 engine lease；没有创建第二 engine/writer。真实 Windows 相关 fixture 通过：manager host 释放后合成进程存活、同 Session 恢复；30 轮 tray/subclass handles **556→557**、USER **13→13**、GDI **0→0**；recovered occupancy 23 次拒绝 B 的 handles **544→544**；暂停/非暂停各 50 轮窗口策略通过。

仅生产 `AppSessionHost.cs` / `SessionEngine.cs` 改动；无 durable schema、共享 codec、Electron、Phase 5A Process Fact、Phase 2 atomic storage、recovery identity、occupancy、Single Instance、Tray、Presentation 或 PublishedPath 修改。已有 Electron/import 兼容实现不受共享代码变更影响；本轮不重复 Electron 测试，native 完整 suite 仍包含既有 import/codec 回归。未重新发布：旧 publish-final 仍只对应 a11704b，不能作为本次修复二进制。整改 build 内嵌父 checkpoint 9f1aef2，源码与程序集 hash 由本机 audit/提交后 receipt 绑定，不冒称内嵌后生成的提交。

未启动真实游戏、未访问正式 profile、未替换程序、未推送。保留原 3 项 OBSERVATION：真实 host sleep/hibernate/logoff/reboot 未执行；Stop Observing 后 terminal occupancy 仅覆盖 engine 生命周期；资源为本机短轮次证据。后续只做两项 MAJOR 的 targeted re-Gate，不进入 Phase 6。

**READY FOR PHASE 5B.2 TARGETED RE-GATE**。整改自检未发现新的 BLOCKER/MAJOR/MINOR；上述资源波动已记入观察项。本机 `validation-audit.json` 绑定最终源码、程序集和测试证据，`checkpoint-receipt.json` 在提交后绑定整改 SHA；独立复审结论仍待取得。

## WinUI Phase 5B.2：App Lifecycle / Tray / Single Instance（自检完成，等待独立 Gate）

本轮从 `codex/winui-phase5` / `5f57ad8e0a071c2f3c4aed673f546fcf9c2ab792` 的干净工作区开始。该基线已通过 5B.1 targeted re-Gate；下方 5B.1 的“等待 re-Gate”属于历史时点。本阶段接入应用生命周期，不进入 Phase 6，不使用正式资料。**READY FOR PHASE 5B.2 INDEPENDENT GATE**：实现与本轮自检已完成，不代替独立 Gate。生产源码 checkpoint 为 `a11704b902d0c643385efa2209713f3f7fff8c01`；后续最终验收文档 checkpoint 由本机 `checkpoint-receipt.json` 绑定，二进制仍明确对应 a11704b，没有冒称内嵌文档提交。

### 生命周期装配

- `TheOrder.WinUI/Program.cs` 使用官方 Windows App SDK `AppInstance`，在 `Application.Start` / DI / writer 之前按 AppID、用户 SID、隔离 profile 物理身份注册；先订阅再注册，覆盖同时启动的 activation 窗口。secondary 只转交当前启动参数、请求 primary 唤回并退出，失败也不能创建第二 writer。当前参数没有自动启动游戏语义。Phase 2 writer lease 仍是独立防线。
- `AppSessionHost` 是应用生命周期 owner：只初始化一次 writer/engine/recovery；完成前不接受 launch，UI mutation 禁用。`MainWindow` 只持有查询和命令接口。`OfflineLibraryClient.LoadAsync` 在 engine lease 存在时刷新同一 writer 的快照，不能因普通 UI reload 重开 writer。没有新增 durable schema。
- `WindowsTrayLifecycle` 持有一个 shell icon、一个窗口 subclass；图标使用共享系统资源，popup menu 每次销毁。`TaskbarCreated` 重新注册；不可用时显示警告并保留可访问窗口。Close Window 隐藏同一窗口并请求 checkpoint，engine、观察、计时与导航保留。Show/secondary activation 恢复窗口；托盘包含标题、计时能力、暂停/恢复和明确退出。
- Explicit Exit 禁用命令、处理草稿、等待进行中的启动事实、freeze/checkpoint 到 RecoveryPending、确认保存、排空页面任务和 writer 后释放 engine/tray。保存失败保留应用并允许重试；不 kill game。页面的启动取消按钮只在启动进行中可用；不把旧取消 token 当作普通“停止观察”入口。核心 StopObserving API 与 recovered occupancy 保持原契约。
- `WM_QUERYENDSESSION` 的 shutdown/logoff 共用 4 秒保存预算、最多 4.2 秒等待，不否决 OS shutdown；已经开始的原子保存不取消。`WM_ENDSESSION(false)` 取消退出准备：仅把本次 RecoveryPending 恢复为同一 Open invocation，保留 paused/count/累计并建立新 baseline；terminal 不重开。`WM_ENDSESSION(true)` 移除托盘，OS 可以立即终止进程，不能声称所有异步清理保证完成。没有 crash cleanup 的虚假保证。
- Session 顶部区域和 tray 从 `SessionEngineSnapshot` 派生标题、能力、已确认 duration、历史 total 和 count；无 UI stopwatch 或 UTC 差值。Paused 明示游戏可能仍在运行，恢复后的 CurrentProcessOnly 明示受限，Unknown 不声称进程活跃，Incomplete 不显示正常退出。错误提示与 Session 状态分开。

主要新增源码：`TheOrder.Application/AppSessionHost.cs`、`TheOrder.Platform.Windows/{NativeInstanceIdentity,WindowsTrayLifecycle}.cs`、`TheOrder.Presentation/SessionPresentation.cs`；接入 `App`、`MainWindow`、`Composition`、`LaunchViewModel`。SessionEngine 只增加取消系统退出准备的局部调用，不改 5A Process Fact、Phase 2 原子 transaction、accounting、recovery identity 或 terminal occupancy 持久化边界。

### 验证和明确候选入口

`AppLifecycleTests` 覆盖 startup 等待、唯一初始化、普通/paused/recovered Exit、保存失败、进行中 write drain、系统预算/重复/取消/失败、Close 策略、同 writer reload、presentation 和 pause gap。`AppLifecycleWindowsTests` 在真实 Windows fixture 上验证 host 退出后进程仍活着、同 Session 恢复、次数与离线 gap、物理实例键，以及自有隐藏测试窗口上的 shell icon/subclass/shutdown 协议。后者仅向测试窗口发消息，不广播或请求真实系统退出；不能替代真实 WinUI 桌面交互验收。

证据统一位于 `.artifacts/native-phase5b2/`。首次 147 项相关回归有 4 项新测试准备失败（无效 terminal 样本、resume 后尚未建立新采样基线）；修正测试前置条件后 147/147，通过的生产契约没有放宽。最终新增 **28** 项，完整 Debug/Release 各 **1256 passed / 0 failed / 0 skipped**，原 **1228** 项逐名称保留。locked restore、两配置 solution build **0 warnings / 0 errors**、Electron **665/0/0**、artifact tests **32/0/0** 均通过，缺少 PublishedPath 的实际启动调用已确认拒绝。

真实 WinUI 合成链路：首次双进程同时启动只留下 PID 3352，secondary exit 0，activation=1，library hash 未变。启动 fixture PID 26472 只增加一次 count（3→4）；暂停固定 82 秒，Close 后两进程仍活着、窗口隐藏，secondary 唤回同 Session。Explicit Exit 保存 RecoveryPending，游戏存活；双进程重启只恢复一次，原 ID/installation/count/82 秒和暂停意图不变。托盘菜单“恢复计时”由用户操作并确认，随后 UI/JSON 复核为同一 Session 的 unpaused / CurrentProcessOnly。受限状态下隐藏继续从 157 秒累计到 178 秒，未增加次数。未暂停退出为 195 秒 / 1958732436 ticks，离线约 55 秒再启动仍为完全相同 ticks，recovery count 只 +1。模拟托盘初始化失败时点击关闭仍保留窗口、警告与继续计时；fixture 通过正常 stop-file 退出后为 Incomplete / RecoveredProcessEnded / null real exit，累计 243 秒、total=720+243、count=4；用户明确退出入口仍可用。没有伪造 Completed。

资源：真实 WinUI 连续 23 次 secondary exit 0；预热后再 20 次，primary handles **1077→1077**，只有一个窗口/engine。平台测试 30 轮 shell icon/subclass 加入/删除，回调次数精确、移除后不再收到事件，USER/GDI 保持稳定；核心/Windows host 每会话 50 轮 Close/Show 策略，完整数值见 TRX。桌面连接一度超时，恢复后重做了上述链路；早期 PID 16284 的终止没有可归因的退出证据，不纳入成功结论。Windows UI 证据保存在 `windows-ui/` 与 `synthetic-final/`，共 8 组 manifest，是 MVID `84a6158e-515a-4af5-8e84-2e1cb48f536e` 的 Debug 自检；下面最终包的 9 组 `published-ui/` manifest 单独绑定，不混称同一 build。UI Automation 缓存过旧的捕获被证据工具拒绝，刷新焦点后重新读取，不降低 freshness 校验。

`native.ps1 -Action Run -Published` **现在必须指定绝对 `-PublishedPath` EXE**，省略即失败，不再暗选旧 Phase 1 包。`check-published-runner.ps1` 同样显式传路径。历史下面的旧调用只描述旧时点，复用时必须补此参数：

```powershell
& ./native/scripts/native.ps1 -Action Run -Published -PublishedPath (Join-Path $PWD '.artifacts/native-phase5b2/publish-final/win-x64/TheOrder.WinUI.exe') -DataDirectory (Join-Path $PWD '.artifacts/native-phase5b2/acceptance-profile')
```

目录必须事先按隔离 fixture 规则准备；以上不是读取正式资料的授权。最终候选为 `.artifacts/native-phase5b2/publish-final/win-x64/TheOrder.WinUI.exe`，Release / win-x64，隔离 profile 为 `.artifacts/native-phase5b2/acceptance-profile`。发布前工作区干净，`published-identity.json` 记录源码 a11704b、各应用 DLL hash/MVID、profile、配置与验证日志；WinUI MVID **058e7e46-b091-40ab-ad95-3d2682ff50f7**，EXE SHA256 **2C5FBF2E98A9E9EDE80D0DC6F0B522DECF46AB66B90D83FCD2D120220AACE1B9**，WinUI DLL SHA256 **AFF764056B78975940CEEF468F6DF2BD14635DCEDE4A4215E0C4A9F575EC8BE7**。测试 build 内嵌父 SHA 5f57ad8，但受测源码 hash 与 a11704b 相同；发布 build 内嵌 a11704b，分别记录。`final-validation-audit.json` 将原始 TRX 的 counts/times 显式复制成标量，纠正早期 audit 的空数组序列化展示；最终 receipt 再绑定发布、截图、TRX、真实验收和文档提交。

### Published candidate 真实游戏验收（2026-10-05）

只使用已有 Phase 5A 样本 A/B，隔离库从 count/total/history 全零开始，没有复制正式资料或凭据。Game-A（ATRI）启动 **2 次**；Game-B（戦国†恋姫 BRAVE 弐，已核对的 `cs2.exe`）启动 **1 次**。只停留主菜单、暂停/恢复管理器计时、隐藏/唤回管理器、明确退出/重启管理器以及使用游戏正常退出菜单。没有推进剧情、读取存档内容、改补丁/配置、安装更新或强杀游戏。

| 实际链路 | Session / 进程与结果 |
| --- | --- |
| A 第一次，正常完整会话与 Tray | Session `1e2b1203-fe1f-4b12-a71b-45d17e72d29a`，PID 41480；暂停约 **55.656 秒**，累计始终 **729608347 ticks / 72 秒**，count=1；恢复后关窗隐藏，游戏存活，同 Session 从 93→118 秒，secondary 退出后窗口恢复。游戏主菜单 EXIT/YES 后 **Completed / ExitConfirmed，164 秒，count=1，history=1**。 |
| A 第二次，Explicit Exit / recovery | Session `76e97f14-f64f-4e60-b10e-4cc624486345`，PID 45344；manager 35500 正常退出后游戏仍在主菜单，durable **RecoveryPending / ManagerExit，2568789350 ticks**。manager 40652 恢复同 ID / `install-Game-A`，首次 durable 累计仍精确 **2568789350 ticks**，recovery count=1，play count=2，未补约 138 秒离线间隔；UI 明示 **仅观察当前进程**。游戏主菜单正常退出后诚实记录 **Incomplete / RecoveredProcessEnded / null endedAtUtc，312 秒**，A 总计 **476=164+312 秒、2 次、2 条 history**。 |
| B 唯一一次，handoff 与完整结束 | Session `c1ba40f6-ccee-4483-886b-05601d2e543d`，入口 PID **44140→45024**；handoff **2972.03ms**，主菜单稳定、LaunchScope、count=1。父进程退出未提前终结 Session。游戏“終了”正常退出后 **Completed / ExitConfirmed，45 秒，total=45，count=1，history=1**；真实退出时间与稍后的保存 cutoff 分开，无接续等待补时。 |

重启 A 时一次人工命令错用了 Electron 的环境变量；native 入口以 **DataIsolationException** 在 writer/engine 之前拒绝，未读取正式资料，原 RecoveryPending 未变。关闭提示后使用正确 `THE_ORDER_NATIVE_DATA_DIR` 重启取得上述成功证据；拒绝记录单独保留为 `game-a-second-restart-rejected-missing-native-profile.json`，不当作 recovery 成功。全部真实游戏和候选进程已正常退出，`real-acceptance-audit.json` 记录清理结果和会话汇总；A/B 目录前后文件数量、大小、mtime 无变化，EXE hash 不变。这是目录元数据与 EXE hash 核验，不是全文件内容或内核 IO 审计。

Game-C **本轮未启动**：未修改 cwd/launch payload，沿用下方 Phase 5A.1 的历史 cwd/主菜单/正常退出证据；不冒称最终 5B.2 实测。未访问 `%APPDATA%/KunGalManager`、没有凭据迁移、没有正式 cutover、没有推送。自检 **BLOCKER 0 / MAJOR 0 / MINOR 0**；保留 3 项观察边界：真实 host sleep/hibernate/logoff/reboot 未执行；终态 Stop Observing 的 occupancy 仍仅限 engine 生命周期；资源是短轮次本机证据，托盘 Resume 为用户操作加独立读回，非自动点击或长期/跨机器保证。Phase 5B.0/5B.1 产品及 accounting/identity 契约没有偏离。

后续真实 host 人工验收（本轮不执行 sleep/hibernate/reboot）：用同一候选和隔离 profile 启动一个合成 fixture，记录 SessionId/count/累计；分别由用户触发一次 sleep/resume、hibernate/resume、logoff 或 reboot。恢复后核对没有睡眠/离线补时、无新增 count，跨 boot 按原恢复规则失效而不接管。对比 main JSON 的真实 lifecycle/reason 与 UI，保留 OS 时间和候选身份。异常关机只以实际落盘 checkpoint 为准。

官方机制参考：[AppInstance single-instancing](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance)、[notification area](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)、[WM_QUERYENDSESSION](https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-queryendsession)、[WM_ENDSESSION](https://learn.microsoft.com/en-us/windows/win32/shutdown/wm-endsession)。

## WinUI Phase 5B.1：Recovery Occupancy Remediation（2026-10-05）

独立 Gate 在 `3cb1984d2bf2e4186b6fc95aca36f436d72f2b80` 实际复现：recovery 后 Stop Observing 终结 Session/释放 observer，launch admission 因没有 nonterminal Session 而允许第二次 owned launch，但原游戏仍活着。普通未 recovery 路径已有 5A reservation，行为正确。本节取代下方历史实现自检的当前状态；整改不重新实施 5B.1，也不进入 5B.2。

### Occupancy 与 history 分离

唯一生产修改是 `native/TheOrder.Application/SessionEngine.cs`。恢复事务成功后保留已验证的完整 `SessionRecoveryIdentity` 为独立内存记录；不依赖 `Runtime.Native.IsTerminal`、observer 或 JSON 中 token 是否仍存在。复用原 identity 和 installation binding，不引入只看 PID 的弱身份。Stop Observing 原行为保持：Incomplete、无真实 exit 时间、无后续 timing、observer 释放、游戏继续运行。保存失败重试、checkpoint、普通编辑乃至终态条目移除均不能隐式释放 occupancy。

每次后续 LaunchAsync 在既有 `launching` 保护中先做一次有限查询：临时 Attach 核验完整进程身份，再核验原安装及 folder/exe/cwd 身份。Active 返回 AlreadyRunning；查询失败、身份无法确认、安装变化或不可访问均 Unknown/AlreadyRunning。Attach 无法建立时只接受原 5A `RevalidateExternal` 的 Inactive 证明（进程不存在、已退出或 creation generation 不同）；该接口仅按 generation 得到 Active 不足以替代完整 identity 验证。相同 PID 的另一 generation 可以释放原 occupancy，image/file 差异本身不能作为退出证明。

明确 Inactive 时，在同一启动 task 中清除记录后调用原 5A launch admission；第二个并发请求得到 AlreadyLaunching，后续请求仍受原 single-owned 规则保护。被阻止的请求不新增进程、Session、次数、时长或第二条 history。无 kill、无伪造 ExitConfirmed、无重新打开 terminal Session。普通路径的 5A service/reservation 完全未改。

记录由 SessionEngine 拥有；每次 Attach handle 局部 `using`，fallback 使用既有临时 handle，无新增 timer/worker/cancellation source/长期 handle。Active/Unknown 可重复查询；Inactive 清除，Dispose 等待当前 launch 与队列清理后清除。此记录只保证当前 engine 生命周期的占用；terminal 后重新启动管理器不新增恢复资格，跨管理器的 durable occupancy 留在既有恢复/后续宿主边界内。没有扩展 v1 JSON、Phase 2 commit 或 5A Process Fact contract，没有改 accounting。

### 回归与证据

新增 `RecoveredOccupancyTests.cs` 和 `RecoveredOccupancyWindowsTests.cs`：Active/Unknown、Attach/Query/fallback 查询异常、image/file/installation 不匹配、明确退出与 PID reuse、事务失败重试及终态资料编辑、三个并发分支、取消/cleanup、普通 5A 对照、连续 Active/Unknown 复核、真实 Windows 无 kill/占用/退出后只创建一次及 handle 稳定性。原测试期望保持不变。确定性替身的 generation fallback 与生产 Windows 接口保持一致，不能把任意 image 差异误当退出；最初测试替身不一致的失败日志保留。

复验（独立合成 profile；不访问正式资料或真实游戏）：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Debug --filter 'FullyQualifiedName~RecoveredOccupancy|FullyQualifiedName~Session'
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' build native/TheOrder.slnx -c Debug --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Debug --no-build --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' build native/TheOrder.slnx -c Release --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-build --no-restore
```

日志、TRX、原 Gate probe 复验与最终 audit 放在 `.artifacts/native-phase5b1-remediation/`。不要同时运行同配置 build 和 tests，以免 Windows 锁定测试依赖；首次 Debug build 的复制重试警告保留，待测试退出后单独构建。Electron 源码、共享数据类型、codec 和兼容层均未改，本轮不重复 Electron regression，下方 665 项结果是原实现 checkpoint 的历史证据。

最终结果：新增 **19** 项；Session/recovery targeted **128 passed / 0 failed / 0 skipped**；Debug/Release 完整 native suite 各 **1228/0/0**，逐名称确认原 **1209** 项全部保留；两配置最终 solution build 各 **0 warnings / 0 errors**。真实 Windows fixture 在 A 存活且停止观察后连续 23 次拒绝 B，history bytes 不变；A 正常退出后 B 创建/计次一次。占用复核 handle 数 Debug **727→727**、Release **677→677**；确定性 Active/Unknown 交替 30 轮及各异常分支 20 轮后 temporary handles=0，最终 Inactive 与 engine cleanup 都通过。既有 5A 10 轮资源及 Session power/clock 回归也均通过，具体数组见 audit/TRX。

未修改原 Gate probe 源码，以当前 Debug Application DLL 重新构建后复验：A PID **20360** 仍存活，B **AlreadyRunning**、PID=null、sessions=0、PlayCount 保持 **5**；日志 `original-gate-windows-probe.log`，audit 另核对 probe 与测试 Application DLL SHA256 相同。该临时 probe 的既有 CA1416 平台标注警告与 solution build 分开报告。

**READY FOR PHASE 5B.1 TARGETED RE-GATE**。本轮自检未发现新的 BLOCKER/MAJOR/MINOR；独立 Gate 结论待复审。`validation-audit.json` 绑定最终源码与 Debug/Release 程序集 hash，提交后的 `checkpoint-receipt.json` 绑定整改 SHA；构建嵌入的是父 checkpoint `3cb1984`，不声称嵌入随后生成的提交。未验证真实游戏、长时间运行/真实休眠、终态 occupancy 跨管理器重启或 UI/Tray/Single Instance 集成；未操作正式资料/发布目录、未推送。checkpoint 后停止。

## WinUI Phase 5B.1：Session / Playtime Core & Lifecycle Engine（2026-10-05）

开始于 `codex/winui-phase5` / `17b91810fa0db9d967a56fc426892608bb6294c1`，工作区干净；这是 5B.0 纯设计 checkpoint，5A.1 实现基线仍为 `041ddca804a134aa146ee7b884d6eff596f451d0`。本轮用户授权覆盖原设计 5B.1—5B.3 的 core，**不包含桌面 UI / Tray / Single Instance**。完成后只交独立 Gate，不自动开始 5B.2。下方 5B.0 的未实施声明和阶段编号均描述历史时点。

### 实际生产实现与装配边界

| 文件 / 符号 | 实际职责 |
| --- | --- |
| `TheOrder.Domain/Sessions.cs`、`Library.cs` | nullable native v1 扩展；生命周期、跟踪能力、计时状态分离；开始/真实结束/cutoff、100ns 累计、revision/writer/recovery marker；366 天上限、兼容四字段投影。没有 native 的记录保持 legacy，不捏造安装或真实退出事实。 |
| `TheOrder.Application/SessionContracts.cs` | 可注入时钟、power、token 接口及累计器；两端查询确认区间，55 秒触发 checkpoint、60 秒未提交硬上限；先累计 ticks 再取整。 |
| `SessionEngine.cs` | 自有 `GameLaunchService` 与独立 sampler handle；256 项有界单消费者队列、单个 1 秒 timer；成功结果 fallback、fact 接续、pause/stop、power、恢复及显式退出 checkpoint。Changed snapshot 给后续 UI 使用；本轮没有把它装入现有 WinUI 窗口。 |
| `SessionTransactions.cs`、`Transactions.cs` | 同一 library writer 队列中的会话 mutation；原始 incarnation/安装绑定、pending create 保护、单 engine lease；同文件 Session/count/total/token 原子提交；导出副本及导入清除恢复资格、native 重复历史合并。提交后仍只进行预先准备好的内存发布/退休，未放松 Phase 2 保证。 |
| `TheOrder.Infrastructure/Legacy/NativeSessionCodec.cs`、`NativeLibraryCodec.cs` | 严格 v1 schema 与未知版本只读；保留 legacy UTF-16 及路径拼写；native 扩展也计入 32 MiB 文件预算；不截断历史。 |
| `TheOrder.Platform.Windows/WindowsSessionClock.cs` | `QueryUnbiasedInterruptTimePrecise` 计 awake；前后 `QueryInterruptTimePrecise` 检测 sleep bias 断点，另有 power epoch；不从 UTC 推导 duration。精确函数从 `api-ms-win-core-realtime-l1-1-1.dll` 导入，本机 kernel32 无相应导出。 |
| `WindowsSessionPowerEvents.cs`、`WindowsSessionRecoveryTokens.cs` | 无窗口 power 注册/注销；DPAPI token、只读 BootId、物理 profile identity；不读凭据、不恢复旧 Job。调用方拥有 library/power，engine 拥有 service/timer/sampler；退出调用方必须先检查 checkpoint 返回值。 |

以上文件相对 `native/`。Application 对测试程序集增加 `InternalsVisibleTo`，用于确定性迟到 fact 注入；真实 Windows tests 仍走生产 5A 事件。没有修改 5A service、Process Fact 或 Windows identity binder，没有新增包或升级依赖。

### 状态、计时和 exactly-once

- `LaunchStatus.ProcessCreated` 的实际 operation ID 建立 invocation；不使用 preview ID。身份未知/保留 lease 失败仍有零秒 Session。首次存储失败保留可见错误和 pending reservation，禁止再创建游戏；重试不增加第二次 count。第一笔持久化前管理器被终止仍可能没有记录，这是规范允许的初始窗口。
- `Open / RecoveryPending / Completed / Incomplete / LegacyUnknown` 与 `LaunchScope / CurrentProcessOnly / Unknown / Detached`、独立 timing/pause 组合。仅 5A 当前身份的最终 ProcessExited 携带一致真实 exit UTC 才 Completed；sampler 先见 gone 但完整 monitor 仍在时等待最终 fact。observer delay、UTC 前后跳及最后未确认尾段不计入时长。
- verified handoff 复用 ID/start/game/install/count，更新当前 identity/token。仅同 epoch、未暂停/未知/失败且 6 秒内桥接一次；其他 gap 丢弃。Limited 用自己的受限查询 handle 只计可证明区间；Unknown 冻结，恢复身份后重建 baseline。有限观察中的 gone 记 Incomplete，不造 exit。
- 单个 timer 每次等待上一条采样完成，不堆叠 tick；所有写盘都冻结 baseline，故慢盘/队列等待会保守少计而不外推。55 秒请求 checkpoint，未确认差额最多 60 秒。366 天容量或保存失败冻结并给出错误，游戏继续运行。
- pause 不新建、不计次、不 kill；suspend 丢弃不确定尾段，resume 核对进程及原安装。安装核验失败保持锁存，后续 tick 不能绕过它。重复 resume 不抹掉已建立区间；漏通知由双时钟断点补充防线。
- `floor(newAccumulated/10^7) - oldSession.seconds` 与 count 创建标记在同一 JSON 原子替换；旧 aggregate surplus 不重算。revision/owner/incarnation 防止迟到写回；重复、低版本、重复 terminal 均不 double-count，冲突拒绝。普通 notes/preferred 编辑保留，活动条目删除、合并或安装集合更换拒绝。

### 恢复及兼容

仅 canonical main 中唯一非终态候选可尝试恢复。DPAPI 资格绑定 Session/game/install、BootId、物理 profile、PID+creation+image file identity、原 folder/exe/cwd 观察。重新核验当前身份和安装、再次核对 boot 后，原子领取新 writer/recovery marker；同一 writer 重复初始化不增加 marker。恢复后的 baseline 从新观察开始，offline gap=0，能力仅 CurrentProcessOnly；后来消失记 RecoveredProcessEnded / Incomplete。旧匿名 Job 不可跨管理器恢复，外部同名进程不接管。

无 token、解密/boot/路径/代际不符、backup、导入及多个候选均不能恢复；多个候选在一个事务内失效，防止部分失败后误选最后一项。真实 exit 与 Incomplete cutoff 始终分开。`CheckpointForExitAsync(ManagerExit/SystemShutdown)` 冻结并提交 RecoveryPending，失败返回 false；5 秒系统退出预算、窗口提示、关闭到托盘和主实例激活留给后续宿主集成，本轮不声称已完成系统退出路径。`DisposeAsync` 只释放资源，不冒充 graceful checkpoint，也不关闭游戏。

legacy 缺扩展继续合法；ID、次数、聚合时长、原历史均保留，不由 history sum 降低总量。future native version/field/enum 导致只读，不能自动退 backup 丢字段；损坏 v1 显式无效。新 invocation ID 全库唯一；历史别名可暂存内容完全相同的 terminal native ID，显式合并只扣重复 invocation 一次（含零秒），冲突拒绝。这是对原规范“新 ID 唯一 / 历史重复合并”的实现澄清。导出处理副本不改源库；导入即使带 token 也不赋予恢复资格。旧 Electron writer 不保留 native 字段，不能共用可写 profile；正式迁移仍在 Phase 6。

### 验证与证据

新测试为 `SessionCoreTests`、`SessionPersistenceTests`、`SessionHardeningTests`、`SessionWindowsTests`，共用 `SessionTestRig`。fake clock/facts 的精确断言与真实 Windows fixture 分层；没有以测试注入冒充真实睡眠、真实游戏或强制断电。

结果以 `.artifacts/native-phase5b1/validation-audit.json`、`tests/debug-full.trx`、`tests/release-full.trx` 和本节最终记录为准。初期失败保留：缺少 installation 验证异常捕获、kernel32 precise API 入口不存在、native extension 重序列化未配套 UTF-16 converter；均已局部修复后重跑，没有放松断言或资源门槛。最终构建前增加容量/文件预算反例。

最终结果：locked restore 成功；Debug/Release solution build 各 **0 warnings / 0 errors**，全量 C# 各 **1209 passed / 0 failed / 0 skipped**。逐名称对照上一阶段最终 TRX，原 **1108** 项全部保留；新增 **101** 项为 Core 39、Persistence 30、Hardening 23、Windows 9（含两个注入双时钟用例）。Electron **665/0/0**、build、desktop import（37 新增、重复 0 新增/37 跳过、个人资料/重读/游戏文件保留、rendererErrors=0）、artifact collector **32/0/0** 均通过。没有重新发布或替换正式程序。

13 个会话原子持久化边界用例使用真实隔离文件：替换前失败保留旧值、AfterReplace 返回已提交 warning；重开验证 count、30 秒差额和 terminal 状态始终在同一版本。另有 1 个替换成功后丢失 receipt 的模拟 crash，重开再 recovery/replay 不重复入账。资源输出：新增 power/clock Debug **669→672**、Release **722→725**；既有 5A 10 轮真实接续 Debug **645×10**，Release **642×7、645×3**，均通过原门槛。20 轮 fake launch/recovery/disposal 每轮 sampler handles=0。

**READY FOR PHASE 5B.1 INDEPENDENT GATE**。最终 checkpoint 由 Git 与本机 `checkpoint-receipt.json` 绑定，`build-identity.json` 保存源文件/两配置程序集 hash；本轮程序集内嵌的是构建时父 checkpoint `17b9181`，不冒称嵌入之后的实现提交。当前自检未发现未解决的 BLOCKER/MAJOR/MINOR；OBSERVATION 是下述已声明的宿主与真实环境覆盖边界，仍需独立 targeted Gate。提交后停止，不进入 5B.2、不推送。

复验命令（从仓库根运行；测试自建临时合成 profile，**不设置正式数据路径**）：

```powershell
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' restore native/TheOrder.slnx --locked-mode
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' build native/TheOrder.slnx -c Debug --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Debug --no-build --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' build native/TheOrder.slnx -c Release --no-restore
& '.artifacts/native-phase1/tools/dotnet/dotnet.exe' test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-build --no-restore
node --test native/scripts/capture-ui-evidence.test.mjs
npm test
npm run build
Remove-Item Env:KGM_TEST_EXECUTABLE -ErrorAction SilentlyContinue
node tests/desktop-import.mjs
```

资源审查：有界队列不丢 critical fact；5A 后台 publisher 可背压，native power callback 只合并标记不等磁盘；一个 timer 串行采样；退出取消 timer、退订事件、drain、释放 service 与 sampler，power adapter 由调用方注销。20 轮 fake lifecycle 逐轮要求 sampler handles=0；真实 Windows 40 轮 power 注册/800 次 clock 读取及原 5A 10 轮 lifecycle handle 回归保留门槛。这是短期稳定性证据，不是长期无泄漏保证。

本轮没有新产品 UI 截图、发布包、Tray/Single Instance、实际宿主 sleep/hibernate/reboot/Fast Startup、强制断电、真实游戏/API/credential 或正式 profile 访问。真实 Windows fixture 只证明合成直接/launcher/chain、退出事实、no-kill、同代恢复及 DPAPI/API 可用；特殊 DRM/UAC/broker、breakaway、UNC、不同 Windows 机器和长时运行仍未扩大承诺。

## WinUI Phase 5B.0：Session / Playtime Semantic Design Gate（2026-10-05）

**READY FOR PHASE 5B IMPLEMENTATION**。正式规范为 [docs/ai/PHASE5B_SESSION_SEMANTICS.md](docs/ai/PHASE5B_SESSION_SEMANTICS.md)，后续实现和测试必须遵循其中的状态转移、awake 时钟、恢复资格、同文件原子差额记账和 legacy 映射；按 5B.1—5B.5 分段 Gate。

设计开始时实际分支 `codex/winui-phase5`、HEAD `041ddca804a134aa146ee7b884d6eff596f451d0`、工作区干净。此前 Phase 5A.1 独立 targeted Gate 已通过且未改代码，该 SHA 仍是实现基线；本次只建立纯文档 checkpoint，不产生新构建。下方自检/真实样本记录保持其原有版本及覆盖边界。

本轮只读核对源码、现有测试和 Microsoft API 文档，并检查本次文档。未运行新的构建、桌面或真实游戏验收；未修改 production/test code、接管正式 profile、读取凭据、迁移数据或推送。Session Engine、sleep/recovery、Tray、Single Instance 均尚未实施，本阶段完成后暂停。

## WinUI Phase 5A.1：Process Fact Remediation（整改自检完成）

本轮从 `codex/winui-phase5` / `9b9b15983124aaf3622056475037b665dd68a2e4` 修复独立 Gate 的四项 MAJOR 和 external AlreadyRunning 占用问题。保留 Application / Platform / Presentation 分层；下方 Phase5A「本机通过」只证明历史样本，不再代表可以直接进入5B。仍不写 playtime、次数、session/history，不实现托盘或恢复，不访问正式资料。

### 生命周期完整性与启动范围

- `WindowsProcessLifetimeScope` 使用 Windows 10+ `PROC_THREAD_ATTRIBUTE_JOB_LIST`，在 `CreateProcessW` 的初始线程运行前原子建立独立 Job membership。不是启动后再 Assign，也不是靠高频快照推断没漏进程。不设置 KILL_ON_JOB_CLOSE、资源限额、UI 限制或继承 job handle；关闭/取消仅释放观察句柄，后代继续运行。创建失败不回退到未受观察的第二次启动。为传递启动属性，Windows 创建边界改用直接 CreateProcessW；EXE 单独传参，结构化 arguments 只做 Windows argv 编码，不经过 shell；空参数、引号、反斜杠、Unicode 与原始命令行由真实 fixture 回读。
- `ProcessTreeObservation.Complete` 只表示本次快照关系核对完整，不能作为生命周期结束证明。内核范围额外记录 active membership 和累计创建数，范围中缺失的短命 helper 不会被当成从未存在。
- authoritative checkpoint：当前有窗口的已跟踪进程必须被 Job membership 证明为唯一存活者，记录该时刻的累计创建数。最终 `ProcessExited` 还要求该进程保留句柄确认退出、有真实退出时间、Job 已为空、累计创建数未增加，且不存在未解决的候选歧义/观察失败。Handoff 的 child 需要自己的 authoritative checkpoint，不能继承 launcher 的结束事实。
- Job accounting / membership 不是单次原子查询：读取 accounting → PID list → accounting，前后 active/total 与列表数量必须一致才接受。期间出生、退出或同数量换代均返回 incomplete；不能用旧 total 配上新列表建立错误 checkpoint。6个确定性用例直接调用生产 reconciliation，实际 Windows fixture 验证完整链。
- 后续有新出生进程会使原 checkpoint 失效；若稍后仍能证明当前游戏是唯一存活者，才可建立新的 checkpoint。已漏掉中间进程而最终游戏仍活着时，空 ancestry snapshot 不足以发布 Exited。未能建立最终身份/时间时保留 Limited，即使后来范围为空，也只释放 admission，不补造游戏退出时间。
- Limited/Detached 后保留一个独立 Job 观察 lease。后续 Launch 进行有界复核：scope 确认空才释放；仍活跃或 Unknown 就明确拒绝重复启动。运行中的 monitor 和启动请求仍受原 single-active gate 串行保护。外部 AlreadyRunning 保存的是 external identity 集合；下次请求分别复核 PID+creation/实际退出，不接管 session。Unknown 不解释为退出，失效 reservation 释放后仍重新执行正常 preflight/existing 检查。

### 代际、路径和事件契约

- Toolhelp 记录 capture 开始时间，候选创建时间晚于 capture 的快照父关系被拒绝。候选保留句柄通过 `NtQueryInformationProcess` 取得该代真实 parent PID，必须与快照一致；父进程仍运行时重新核对其完整 identity。已退出父代使用此前保留句柄取得的封闭 creation/exit 区间，不要求重新打开已退出 PID。测试注入仅替换 snapshot/open，运行同一生产 binder；`Attach` 继续核对完整身份。
- EXE 的普通路径同一性检查保持；cwd 转换后重新 Inspect，目录类型、volume/file ID、最终路径必须全部一致，否则 WorkingDirectoryInvalid，不回退。原 explicit cwd 优先、缺省 exe 目录、260 长度限制、安装边界和启动时路径固定保持。
- `ProcessEvent.ObservedAtUtc` 是发布/确认时间；`OccurredAtUtc` 对 Started 是 OS creation、对最终 Exited 是 OS exit。`ExitOccurredAtUtc` 同时保留在最终事件与 LaunchStatus，不能使用通知延迟作为 session 边界；handoff 事件仅表示接续观察，launcher exit 不发布为最终游戏 exit。未来5B持续时间仍需单独设计 monotonic accounting，本阶段没有实现。
- LaunchAccepted 由带 operation/game incarnation/实际 installation 的 Starting 状态表达，不等于已创建；ProcessStarted、TrackingAttached、ProcessHandoff(old,new)、TrackingLimited、ProcessExited、Detached 独立表达。发生时间不可取得时保持 null，不用 UtcNow 伪装。external AlreadyRunning 不产生 owned ProcessStarted。

### 验证与发布入口

**PASS FOR PHASE 5B（本轮整改自检）**；当前阶段提交即Phase5A.1 checkpoint，精确SHA由Git及本机checkpoint receipt绑定。提交后暂停，未进入5B。结果、TRX、诊断 JSON 和候选包均在 `.artifacts/native-phase5a1/`。隔离真实复验副本为该目录 `real-profile`，只从历史测试 profile 复制 synthetic marker、library 和 launch-position DPAPI，不复制 credential。最终候选为 `publish-final/win-x64/TheOrder.WinUI.exe`；较早`publish/win-x64`保留历史A/B/C证据，不作为最新启动入口。旧 `native.ps1 -Action Run -Published` 默认位置不代表本轮包。

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.artifacts/native-phase1/tools/dotnet'
$env:THE_ORDER_NATIVE_DATA_DIR = Join-Path $PWD '.artifacts/native-phase5a1/real-profile'
$candidate = Join-Path $PWD '.artifacts/native-phase5a1/publish-final/win-x64/TheOrder.WinUI.exe'
Start-Process -FilePath $candidate -WorkingDirectory (Split-Path $candidate) -WindowStyle Normal
```

- locked restore、Debug/Release solution build各0 warnings/errors，完整C#各**1108/0/0**；相对5A新增**31**项production-path回归，Launch及整改共**96**项。旧differential/filesystem/online/recognition/fault injection全部包含在完整套件中。Electron完整**665/0/0**、build/import（37新增、重复0新增37跳过、个人数据与重读保留、rendererErrors0）、artifact collector**32/0/0**、Release publish通过。未升级依赖。
- `test-audit-final.json`、`targeted-test-results-final.json`及`deterministic-evidence-final.json`来自实际TRX：漏helper不伪造退出且最终空scope可再启动、生产PID代际binder、cwd同一性/尾点非等价拒绝、external退出重开、最终child时间均通过。6秒延迟：Release真实exit08:41:38.4503144Z，Observed08:41:44.5132429Z，事件ExitOccurred与前者精确相同。
- 每配置10轮真实fixture handoff/no-kill/正常stop，Debug句柄`653,643,643,640,640,640,640,640,640,640`，Release`675,663,663,663,654,654,654,654,654,654`。snapshot/process/ancestor/job SafeHandle、native临时buffer及linked cancellation的所有权已检查，无后台native watcher。此前一次全量资源阈值失败（654→661）保留为`Debug-final-resource-threshold-failure.trx`；未改门槛，单独10轮452→约456及最终双配置全量均通过，不把短期审计写成长时无泄漏保证。
- 最终WinUI MVID **f5bffdd1-7798-4d10-a408-35b583972075**；内嵌版本仍是父SHA9b9b1598，最终source/bundle hashes与提交后receipt绑定，不能声称二进制嵌入未来提交SHA。中间1102结果由最终1108/TRX supersede，原始失败记录保留。

范围边界：Job 证明本次创建的直接后代范围，不能发现通过既有外部服务/RPC broker 启动的游戏；不支持把这类启动宣称为完整 session。Job 默认不允许 breakaway，需要主动脱离 Job 或不兼容嵌套 Job 的程序不在已验证范围内；不以放开 breakaway 降低完整性标准。未知/无窗口/歧义接续仍 Limited。UNC 实机、干净机签名安装和长期压力未验证。没有作品名、cs2.exe 或 SCRIPT.PAK 特例。

### 本轮真实样本与证据边界

A/B/C使用本轮隔离4条库，没有API credential；candidate PID9716、MVID `dc324977-1087-49dd-89b1-c301e76f2a2e`，具体bundle hashes见`real-candidate-build.json`。这轮真实样本在最后的Job accounting一致性加固之前完成，不能把其MVID冒充最终发布包。

| 样本 | 本轮实际结果 |
| --- | --- |
| A | PID19056，creation134356619830625282；直接启动、主菜单、实际cwd、正常退出，应用与独立OS观察均为2026-10-05T08:22:21.0360229Z |
| B | 正确入口39256→child14568，child creation134356621890414019；父子接续、主菜单、实际cwd、正常退出，应用与独立OS观察均为2026-10-05T08:24:57.6979752Z |
| C | PID11160，creation134356623605491216；实际cwd为原有files子目录、主菜单、标准确认正常退出，应用报告最终退出。独立PowerShell watcher在退出瞬间读到0 FILETIME，`Game-C-exit.json`的时间字段明确INVALID，不用于交叉验证真实退出时间；应用manifest仍有效，精确时间由A/B及生产synthetic延迟测试支持 |

`artifact-audit.json`校验6组UI manifest/tree、6张UI截图与3张游戏主菜单截图，requested==runtime、长度/哈希一致，9个截图SHA256均不同。candidate随后正常exit0。隔离library SHA256仍为`841096FF850FA3805C16AFE6C497B711973DCB1DBEEC8C419E7CCD8791688D04`，无playtime/count/history写入。A73/B352文件元数据不变；C44文件仅既有AIR_startup_log.txt变化，无新增/删除文件，没有代理/产品写游戏目录的操作；这不是内核级IO归因。旧5A真实no-kill仍为历史证据，新Job的no-kill由重复fixture关service后子进程仍存活验证，不宣称本轮又做了真实A关管理器no-kill。

最终包补验B：candidate46636，入口35468→child31244/creation134356634689715214，实际cwd/唯一窗口/主菜单/正常退出通过；OS独立等待和最终Application均为2026-10-05T08:45:42.0073080Z。最后Job跨查询加固因此有最终包真实接续证据，A/C的路径创建链由上述真实样本及最终完整fixture回归覆盖，不冒充最终包重跑了A/C。`ui-final`两组manifest和`Game-B-final-menu.jpg`单独保存；最终管理器exit0。全部合计8组UI manifest、12张截图，build身份按两批分别核对。

## WinUI Phase 5A：Game Launch & Process Continuity（本机 Gate 通过）

从 `7965bee95c49750e26b85302da037582b96a54fc` 创建 `codex/winui-phase5`。本阶段启动和进程连续性已完成本机合成、真实游戏与发布验收；建立Phase5A checkpoint后暂停，**不进入Phase5B**。不写时长、次数、session history或active-session恢复。真实样本仅在合成Gate通过后从用户授权的 `D:\entertainment\PC` 有界选择；关系和资料全部在隔离profile。不是正式部署或全部游戏兼容性承诺。

### 旧 Electron 行为表（实际读取源码）

| 行为 | 当前源码事实 |
| --- | --- |
| 默认安装 | `electron/playtime.ts:launch`：preferred ID存在则选它；否则只有恰好一个安装才允许启动。多安装未选preferred报错；启动接口仅gameId，无explicit安装参数 |
| 并发政策 | 一个PlaytimeTracker只有一个active；active、launching、finishing任一存在就拒绝其他启动，包括不同作品及同作品不同安装；main另外用identity lock串行启动/删除/导入 |
| 参数 / cwd | `spawn(file, [...arguments], {cwd:dirname(file), shell:false, windowsHide:false, stdio:'ignore'})`；数组保留，旧版实际不消费native新增workingDirectory字段 |
| 启动检查 | realpath安装与exe，检查最终exe在目录内、扩展名exe且是文件；没有持久机器级filesystem identity核对 |
| 初始退出 | Node child exit事件；spawn失败不计时。退出前成功创建的PID加入knownPids |
| continuation | 退出后最多6000ms，每次最多等待750ms；PowerShell/CIM枚举全系统后过滤目录；排除已知PID、早于启动约1秒的进程，按createdAt最新选一条；不验证parent relation、不保护PID复用 |
| 替代进程退出 | 每1000ms重新查目录PID；查询失败会结束记录，缺少query failure/真实exit区分；相同目录其他进程有误绑定风险 |
| 窗口关闭 / 应用退出 | 活动或launching时关窗口仅隐藏到托盘；明确退出等待启动、保存记录并退出。finishSession注释及实现明确不signal游戏；shutdown不kill游戏 |
| 已在运行 / 重开 | 已active直接拒绝；首次启动前existing目录PID只用于handoff排除，没有跨进程重启恢复或可靠external AlreadyRunning接管 |
| 测试依据 | `tests/playtime.test.ts`：数组/cwd、重复active拒绝、spawn失败、exit、目录内新PIDhandoff、失败保存重试及no-kill；Windows API查询另有临时空目录只读测试 |

### 本阶段明确的改进方向

保留single-active政策与structured arguments；explicit installation只影响本次启动，不暗改preferred。有效explicit workingDirectory优先，缺省才用exe目录，无效不静默fallback。使用Windows进程句柄、PID+creation time+image观察，有限parent/child证据替代“同目录最新进程”；证据不足返回TrackingLimited。应用关闭真正退出但不结束游戏（托盘/计时属于5B，旧隐藏窗口语义本阶段不实现）。

Phase3B的PathObservation明确是临时观察，旧library没有持久机器身份，不能声称可以凭旧JSON发现启动前的目录替换。新启动授权需要独立的本机安装观察基线；旧/未核对关系必须先显式核对，再保存机器身份，启动时再校验；不得自动把变化目标重新信任。该记录不是active-session persistence，也不能随书架导出授予另一台机器启动权限。

### Phase 5A 当前实现与已验证范围

- `LaunchViewModel` → `GameLaunchService` / `ILaunchLibrary` → `WindowsGameProcesses`。详情只做UI wiring，进程观察由Application拥有，离开详情不会结束游戏。独立launch incarnation保护删除/恢复、合并、移除后重加安装；同一安装参数编辑只影响下一次snapshot。序列化library admission中的最终创建不写个人/游玩数据。
- snapshot包含operation/generation、game epoch/incarnation、installation/preferred、原结构化参数和folder/exe/cwd三份filesystem observation。每次启动前固定祖先/reparse目标与exe句柄，再复核Phase3B identity；无shell、`ArgumentList`、继承环境、明确cwd，拒绝无效/越界目录。明确cwd优先，否则exe目录；长度达到260的cwd显式拒绝，不做静默fallback。ordinary/junction/symlink/SUBST retarget均有实际Windows测试。
- `native-launch-positions.dpapi`仅保存本机核对位置，使用同一隔离根/安全打开/原子写与DPAPI helper；最多10000项/16MiB，损坏不伪装缺失，不自动信任新目标。旧library没有该机器授权记录，第一次启动需显式核对。不是session persistence，不改变legacy备份格式。DPAPI helper从原credential store原样抽出，无本阶段凭据读取或网络请求。
- 句柄+PID+creation FILETIME+image filesystem identity共同标识进程；Windows查询失败为Unknown/Limited，不是Exited。创建成功后附着/观察失败只返回StartedButTrackingLimited；single-active reservation保持，不能因此再开一份。已有相同image进程阻止新启动，但不冒称跨重启恢复/接管。
- continuation：初始12秒观察、退出后6秒观察，250ms轮询、稳定运行后1秒；最多8192系统行/32候选/240轮树观察，候选稳定750ms。验证parent lifetime、creation time、安装边界及唯一可见非辅助窗口；多个候选/不完整查询/无法附着保守Limited。同目录无关进程、缺失creation、仅选择最新进程均不接纳。Windows不能补回从未观察到的极短中间进程，broker/脱离树/长期延迟接续仍有限制。
- 所有cancel/dispose/shutdown只停止观察和释放句柄；没有Kill/TerminateProcess。UI本阶段关闭真正退出，不实现旧Electron活动时隐藏托盘；计时、次数、历史、tray session、active-session恢复留给5B。
- 项目自有WinForms fixture支持直接窗口、launcher→game、launcher→helper→game、空入口、快速子进程、多候选、参数/cwd/原始Windows命令行回报及正常stop文件关窗。不会复制到真实游戏目录。新增65项production-path测试，包括13项实际Electron oracle检查/差分；旧同目录最新进程策略与native严格接续的差异明确标注。
- 实际WinUI发现缺失exe提示误写“目录不存在”，已分为启动文件/安装目录/工作目录文案并扩展现有回归。一次全量测试在fixture窗口被观察前就关窗，正确得到Limited而使测试失败；测试改为等待生产monitor实际消费HasWindow=true，不改变生产策略。保留失败日志，最终`launch-stable-window.trx` **61/0/0**、`Debug-complete.trx`/`Release-complete.trx`各 **1073/0/0**。
- locked restore、两配置solution build各0 warnings/errors、Release publish通过；原102 transaction、13 filesystem/cover、32 online、254 recognition差分和14fault保留；Electron完整665/0/0、build、37条真实UI合成导入/重复0新增37跳过/重启个人数据保留通过；artifact collector32/0/0。没有升级依赖。

最终真实验收发现的通用修复（supersede上面的1073中间结果）：

- 正常关闭时先dispose host，随后WinUI Details.Unloaded又调用Launch.Leave，触发已释放CTS的Cancel。早期candidate exit `0xc000027b`，不能把游戏仍活着当作正常退出通过。新增2项production VM迟到回调测试先失败；仅为Leave/Cancel增加disposed保护。最终两次真实正常关闭均exit0，no-kill由保留进程句柄验证。
- Game-B用户指定的正确入口在旧`\\?\`物理路径启动下只出现无窗口子进程，而普通路径、相同cwd/空参数、同样UseShellExecute=false的对照出现正常主菜单。Windows为普通路径保存的GpuPreference注册值只读确认，未改GPU/驱动/系统设置，也未直接测量GPU归属，因此这是已验证的路径兼容问题，不宣称确定的驱动根因。新增2项真实fixture回归读取GetCommandLineW，在旧实现中失败（单独QueryFullProcessImageName会被运行时规范化，不足以证明实际启动字符串）。`WindowsGameProcesses`保留扩展路径作身份/固定/复核，仅当短普通路径再次Inspect得到同一file ID和final path时，用普通写法创建进程。不能等价转换的路径保留扩展语法，无shell、参数、权限、reparse或候选选择规则放宽，无游戏特例。
- 最终`win32-path-fixed.trx` **65/0/0**；`Debug-win32-final.trx` / `Release-win32-final.trx`各 **1077/0/0**，两配置build各0 warnings/errors。Electron665、artifact32、build/import结果仍有效（其源码未变）；最新Release publish为下述win32-fixed。保留修复前失败TRX，不能覆盖为成功。

### 真实样本、隔离与最终门禁

关系在`.artifacts/native-phase5a/real-profile`，有合成标记及4条别名记录，不是正式资料。A/B/C实际执行，D只作备用选择未执行；B已提供真实接续，不再扩大执行样本。真实args为空，未注入未知flags；复杂quoting由fixture验证。未进入剧情、读写存档或更改游戏设置，均通过标准关闭及游戏自身确认退出。

| 样本 | 最终结果 |
| --- | --- |
| Game-A，x86直接入口，空格/中文路径 | 初次直接启动、实际cwd/PID+creation、AlreadyRunning且仅一个实例、正常退出检测通过。最终包管理器7904→游戏44384，creation134356586222243292；管理器正常exit0后保留同一游戏句柄/PID/creation且主菜单可用，随后游戏正常退出 |
| Game-B，x86自重启入口，日文复杂路径 | 用户纠正入口后发现扩展路径兼容问题；普通路径对照成功，最终WinUI入口31608（creation134356583555791717）→同一exe子进程28316（creation134356583556359697），parent=31608，唯一可见主菜单、实际cwd、RunningConfirmed接续与正常退出检测通过。没有把较早错误bootmenu的安装提示当作真实游戏不兼容 |
| Game-C，x64直接入口，显式工作目录 | 原exe目录cwd找不到SCRIPT.PAK；只读文件名确认资源位于已有files子目录，仅调整隔离安装的workingDirectory并重新显式核对。最终PID2572/creation134356584994899275，实际cwd与声明一致且不同于exe目录；主菜单、RunningConfirmed和正常退出检测通过，未移动资源、无产品cwd规则修改 |
| Game-D，备用日文launcher | 已有界选择及元数据基线，但未执行；不列为通过样本 |

用户曾同时关闭管理器和所有游戏的那一次，明确不作为no-kill证据。早期`resume-a-*`虽然游戏存活但manager崩溃，也不是clean shutdown PASS。`fixed-a-no-kill.json`证明关窗修复后exit0；最终`win32-a-no-kill.json`进一步绑定最新包、退出前后保留句柄，manager exit0且游戏同一身份继续运行。没有强制结束游戏。未自然出现可单独核验的helper/updater混合主窗口，真实helper场景不冒称覆盖；确定性多候选、辅助进程、未知身份和PID复用回归继续通过。

隔离library在B入口/C cwd/D样本设置完成后记录基线；最终SHA256 `841096FF850FA3805C16AFE6C497B711973DCB1DBEEC8C419E7CCD8791688D04`逐字节不变，个人字段、720秒/3次/空history均未被启动写入。正式profile、credential、API和正式Electron未访问/替换。A73文件、B352文件前后元数据无变化；C44文件只有已有启动日志大小/mtime变化，无新增/删除文件，文件名和时序对应游戏启动日志（未读取日志内容）。The Order/代理没有向游戏目录写文件的操作；这是元数据审计与操作范围证明，不是内核级逐进程IO归因。

### 当前发布启动入口

`native/scripts/native.ps1 -Action Run -Published`仍指Phase1默认发布目录，不能据此启动最新Phase5A。当前验收包为仓库下`.artifacts/native-phase5a/win32-fixed/win-x64`；从仓库根使用下列显式入口。该目录是本机生成候选，不替换正式程序。

```powershell
$env:DOTNET_ROOT = (Resolve-Path '.artifacts/native-phase1/tools/dotnet').Path
$env:THE_ORDER_NATIVE_DATA_DIR = (Resolve-Path '.artifacts/native-phase5a/real-profile').Path
$env:THE_ORDER_NATIVE_DIAGNOSTICS = '1'
$phase5Candidate = (Resolve-Path '.artifacts/native-phase5a/win32-fixed/win-x64/TheOrder.WinUI.exe').Path
Start-Process -FilePath $phase5Candidate -WorkingDirectory (Split-Path $phase5Candidate) -WindowStyle Normal
Remove-Item Env:THE_ORDER_NATIVE_DATA_DIR, Env:THE_ORDER_NATIVE_DIAGNOSTICS
```

### 证据身份、作废记录与baseline

`.artifacts/native-phase5a/`的`final-artifact-audit.json`核验26组manifest、27张截图，requested与fresh runtime逐字段相同、tree/runtime一致、文件字节长度/SHA256相同，没有重复截图hash。最终真实游戏截图另以PID/creation/实际image/cwd receipt关联，不伪装游戏内部也有The Order runtime。关键画面已实际检查。

- `synthetic-direct/handoff/limited-accepted`和`final-missing-executable`分别证明合成直接/接续/受限与缺失文件文案；`synthetic-exit-debug`仅调试历史，旧`synthetic-missing-accepted`由最终missing文案替代。
- `real-a-*`为首次直接/重复/退出，`win32-a-before-manager-close` + `win32-a-survives-manager-close` + `win32-a-no-kill.json`为最新包完整no-kill证据。
- `real-b-launcher-*`是用户纠正前的错误入口，`fixed-b-correct-entry-limited`是路径兼容修复前的真实反例；最终`win32-b-real-handoff`、`win32-b-real-menu-foreground`、`win32-b-exit-detected` supersede旧不兼容判断。`b-ordinary-path-control-visible`只证明独立ProcessStart对照，不当成WinUI启动。
- **`win32-b-real-menu`截图被前景管理器遮挡，明确INVALID**，即使tree/PID/hash校验成功也不能接受；已由置前重新采集的`win32-b-real-menu-foreground`替代。旧文件保留，防止把采集成功当作内容正确。
- `win32-c-explicit-cwd-running`、`win32-c-real-menu`、`win32-c-exit-detected`证明已有explicit cwd功能在真实资源目录可用，原SCRIPT.PAK错误是历史记录。
- `build-bundles.json`保存accepted/final/shutdown-fixed/win32-fixed各层DLL/EXE hashes。最终WinUI MVID `db300925-99ba-455e-bc36-93e2ee5d55b3`；程序集仍嵌入父SHA7965，源码hash及提交后receipt绑定Phase5A checkpoint，不声称已嵌入最终提交。较早bundle不能单凭相同UI MVID当成全部程序集相同。

Phase5A少量baseline（非SLA）：合成直接Start60.0ms/附着75.1ms，合成launcher Start55.8ms/最终附着2541.3ms/观察退出后handoff845.5ms；最终Game-A Start39.3ms/附着54.7ms，Game-B Start40.6ms/最终附着3343.8ms/handoff3018.2ms。Game-C数值见最终manifest。旧Game-A运行期间管理器全进程（含可见UI/diagnostics）5.04秒耗1937.5ms CPU、handles984→984、working set约255MiB，不能当成独立observer成本或正式性能SLA。生产250ms有界树观察/稳定后1秒轮询上限由回归验证，未进行长期压力测量。

**Phase5A本机Gate通过，可提交独立评审；建立checkpoint后暂停，不进入5B。** 剩余边界：UAC需提升时明确拒绝、不绕过DRM；broker/脱离树/未观察到的快速中间进程、超时后才出现窗口及无窗口接续保守Limited。UNC、需要扩展语法的长路径、按链接别名保存的图形偏好、干净机签名/安装及长期压力未实测；不保证所有真实游戏。路径对照证明本次兼容恢复，不证明GPU驱动内部行为。跨重启进程恢复、托盘及游玩数据属于后续5B，当前未实现。

## WinUI Phase 4R：真实 API / CDN 验收与局部 WebP 兼容修复（2026-10-05）

可信起点 `codex/winui-phase4` / `c077fd257c5195e790a540f77c571269375a7f0d`，用户确认4B.1.1 targeted re-Gate已通过。本轮结论 **REAL API SMOKE PASS；PHASE 4 COMPLETE — READY FOR PHASE 5**。保持当前分支，建立Phase4R checkpoint后暂停，不进入Phase5。旧4A/4B的未联网声明属于其历史证据范围，不能用旧合成证据替代本节真实服务结果。

### 隔离、DNS与网络边界

数据/凭据根均为仓库下 `.artifacts/native-phase4r/profile`，只含3条合成条目与不可执行的占位素材。专用测试凭据由运行时读取用户明确指定的任务附件，经当前Windows用户DPAPI保存；没有把明文写到脚本、参数、日志、文档或Git。正式 `%APPDATA%/KunGalManager`、旧safeStorage、真实游戏目录均未访问，也未执行游戏或替换正式应用。测试凭据最终按授权留在隔离DPAPI；截图时输入已清空，输入期间PasswordBox仅显示掩码。

首先重新解析 `image.kungal.iloveren.link`：172.67.144.240、104.21.10.82、2606:4700:3037::6815:a52、2606:4700:3033::ac43:90f0，全部经过原生产IsPublic判为公网。没有放行198.18/15、fdfe fake range、私网或其他非公网地址，没有改变DNS/IP绑定、HTTPS/TLS、超时、大小上限或重定向规则。当前普通环境API默认代理策略与图片UseProxy=false路径均成功；不能由一次IsBypassed值推断实际采用了某个系统代理。没有更改系统代理/VPN/DNS设置。

### Phase 4R.1：唯一产品兼容修改

真实133封面返回HTTP200、`image/webp`，RIFF/WEBP签名，14982字节；原实现只接纳PNG/JPEG，属于真实服务兼容问题。先添加 `WebpCompatibilityTests`：原始自制4×6纯色有损/无损alpha素材，无远程作品素材或密钥。修改前 **3失败/7通过**；修复后专项100通过，新增10项全部通过。

- `RemoteCoverSource` 只增加明确的 `image/webp` MIME，不接受任意octet-stream或host例外。
- `WindowsRasterCodec` 校验RIFF总长度、chunk长度/填充、VP8X头及画布；拒绝ANIM/ANMF及动画flag，再交给原Windows WIC。保留单帧、4000万像素、65535单轴、20MiB输入、1600目标边长、原解码并发限制；解码后再次检查目标尺寸。没有新decoder依赖。
- 回归穿过生产remote source、Windows decoder、ImagePipeline及隔离磁盘缩略图缓存；涵盖有损/alpha无损、坏长度/截断/动画/大画布及旧Fake-IP拒绝。PNG/JPEG和个人照片导入扩展名政策不改，没有重写Recognition、Review或事务。

### 实际服务结果与证据范围

| 场景 | 本机真实结果 |
| --- | --- |
| 鉴权与搜索 | 隔离DPAPI回读一致；真实Test Connection成功；此前日文查询4条，中文千恋万花1条、ID15；安全零结果为Empty |
| 分页 | 查询“恋”首20条→累计40条，cursor正常、无重复、首屏顺序保留；重复cursor未自然遇到，原确定性回归保留 |
| 详情身份 | 133、15、919均requested=response=canonical；aliases14/14/4，release2008-09-26/2016-07-01/2008-11-28，sources35/34/13，covers6/10/6 |
| 错误 | 单个明确无效ID，HTTP400、INVALID_PARAMETER、Problem JSON shape，由原逻辑映射InvalidRequest；未新增错误映射 |
| CDN | image.kungal.iloveren.link，HTTPS200、无redirect；3张均image/webp且签名一致；14982/103658/12588字节；缩略图256×178、229×320、245×320 |
| 缓存 | 真实A→B→A回到memory，3loads/1hit/0errors；生产缩略图缓存以及最终WinUI封面、重启书架封面通过；无错误旧图覆盖 |
| Schema | 实际ID为string，可选latin为null、无后续页时next_cursor缺失，bulk详情顺序由客户端按搜索ID还原；原DTO接纳，不需schema/nullability/pagination修复。只保存字段名/类型/安全状态摘要，未保存原始response body |
| Merge / rate limit | **NOT ENCOUNTERED**；没有为了发现merge枚举作品或主动制造429 |
| 其他图片格式 | 此次3张CDN探针均WebP，JPEG/PNG/非标准MIME未自然遇到；保留现有确定性格式回归，不扩大格式范围 |

`real-service-harness.json` 来自实际production catalog/transport/VM/image pipeline/WIC及真实HTTP。快速A→B、取消和旧page2→新query用诊断transport暂缓**已从真实服务收到的响应**的交付，未伪造或改动body；结果B保持current、旧A/旧分页丢弃，取消后Idle/空结果/非busy，生产取消文案为“搜索已取消”。它证明受控迟到的接纳规则，不宣称自然网络恰好发生了乱序。移除和保存同一测试凭据也在该受控窗口进行，epoch递增，旧连接回到Idle，持久状态与操作一致。

最终候选 `.artifacts/native-phase4r/publish/win-x64/TheOrder.WinUI.exe` 无online/recognition fixture，真实Windows界面完成：连接、搜索、20→40分页、919详情及WebP、local→real enrichment、候选详情、Review/Apply、Manual、重启。Summer Pockets有4在线候选和1本地索引：作品4得分0.820且版本不确定，其他独立编号冲突，均不能直接Apply。千恋万花15得分0.985，vndb/bangumi/steam三来源一致；选择并重新校验详情后显式Apply提交，UI“修改已经保存”。不会因真实服务返回高分取消veto或自动决定。

隔离Manual对明确错误ID为ValidationFailure且library字节不变；明确指定真实ID4经lookup/canonical/事务保存。重启后15和4关联保留，全部实际个人字段（名称/个人名标记、notes、personalRating、customTags/categories、installations/preferredInstallationId、favorite/status、totalPlaySeconds/playCount/sessions及日期）逐项比较通过。日期由Z规范化为.000Z，时刻不变。原库无个人封面，未把“无字段被删除”宣称成非空个人封面实测。

实际UI移除测试凭据后，remote候选/详情撤销，本地候选和证据保留；随后恢复，重启设置页确认隔离凭据仍存在。正常重启实例PID41956退出码0。早期活动关闭观察到Enriching但观察与关窗存在采样间隔，相关 `close-request*` / `active-exit.json` 只作历史诊断，不单独判活动关闭通过。

为补足精确关闭证据，仅最终一个候选进程通过环境变量使用临时loopback CONNECT通道，唯一允许目的地为api.nextmoe.dev:443。通道转发不解密的端到端TLS、仅解析外层记录长度，对真实server ciphertext延迟30秒，不记录payload/凭据，不修改TLS验证或产品代码，不改变系统网络设置。`final-close-active`实际runtime为Connection Loading；真实server ciphertext于05:19:59Z进入延迟，客户端05:20:13Z关闭，尚未到放行时间；候选PID35284 **exit0**，通道随后停止。此项明确属于受控网络延迟；其他最终综合操作均在正常网络完成。离线转换未人为断网，以原确定性offline回归为准。

### 完整回归、artifact核验与身份

- locked restore通过；Debug/Release solution build各 **0 warnings / 0 errors**。
- 完整C# Debug、Release各 **1012 passed / 0 failed / 0 skipped**，新增10 WebP；102 original differential（54 mutation+43 codec+5 lifecycle，另1 oracle freshness）、13 filesystem/cover、32 online、254 recognition（98+122+34）、14fault（13 persistence边界+1失败类别）全部保留并由TRX核对。
- Electron完整 **665/0/0**、build通过；实际desktop import新增37、重复跳过37、个人字段/重读保留、game files保留、renderer errors0。Release publish成功。未修改Electron源码/依赖。
- `.artifacts/native-phase4r/` 中最终 **13组JPEG/UI tree/manifest**：connection、search-page1、search-page2、detail-webp、recognition-multiple-veto、review-ready、apply-saved、auth-local-retained、manual-rejected、manual-saved、restart、credential-restored、close-active，均带final前缀。requested逐字段等于runtime，manifest与tree一致，截图文件SHA256和13个不同hash核对；关键画面实际检查。状态尚未刷新或凭据尚未初始化导致的requested/runtime不一致会直接中止采集，没有输出伪场景。
- `artifact-sanity.json`、`validation-audit.json`、`restart-personal-fields.json`、`real-service-harness.json`、`secret-audit.json`、build-identity和提交后receipt供核验。整个仓库（含ignored/hidden/generated/log/artifact/Git/untracked）测试凭据完整字符串 **0明文匹配**；只排除旧Phase3B不可读broken-junction本身，无可读内容被略去。最终截图输入为空/未显示，UI tree/manifests无凭据。
- 早期 `blocker-summary.json`、`cover-probe.json`、`detail-133-cover-blocked.*` 描述旧Fake-IP网络失败，保留历史并由本节、`cover-probe-realip.json`及最终WebP证据明确supersede，不改成旧测试通过。旧3A已作废证据继续作废。
- 最终WinUI MVID `fb541fb8-ed14-4fd9-8194-141a177e22eb`；构建时程序集仍嵌入父SHA c077，build-identity保存源文件与各层DLL/EXE SHA256，提交后的receipt绑定最终checkpoint，不能声称已嵌入最终提交。

剩余风险：本机少量真实服务样本不是SLA/负载测试，也不证明全部真实作品/环境；自然merge/429和其他CDN格式未遇到。Windows WIC/WebP在当前系统及发布包已验证，干净机codec/安装/签名/升级仍须后续验证。既有UNC/reparse、10k unique images和正式库写入前workingDirectory降级门禁保持。没有开放正式数据写入、真实游戏执行或进入Phase5。

## WinUI Phase 4B.1.1：Final Recognition Gate Remediation（2026-10-05）

基线 `codex/winui-phase4` / `41fa1e0a573d83f232e3d5e8d2d2011bf884acc6`。上一轮 targeted re-Gate 为 **NO-GO**，仅剩 M1（PE 排序与截断）和 M5（初始化接纳竞态）。本节 supersede 下方 Phase4B.1 对这两项已充分修复及 Gate 放行的判断；旧 artifacts 仍是原场景的历史证据，不能证明本轮两个反例已消失。本轮没有修改评分/veto、Review UI、provider-name exclusion、历史校验或 auth/local 规则，没有进入 Phase5、访问真实 API/credential/正式资料或真实游戏。

### M1：一个确定的有效输入

`ExecutableEvidenceParser` 对 native 资源先按 resource/language identity（数值 Name，再 Target）排序，再沿用每级4项的有界选择。字段继续使用原清洗/标题可用性规则；候选标题按 ProductName、FileDescription、OriginalFilename 优先级，再按 Compact 值及原值的 Ordinal 顺序排列，之后按来源组+Compact 去重，最后保留16标题。相同程序标题优先 ProductName，不再取决于资源到达顺序。公司名先 Ordinal 去重排序再保留8项。没有扩大每个程序的标题、资源或读取字节预算。

`pe-evidence-v2` 直接序列化这份实际返回的 ordered/bounded Titles、Companies、RejectExecutable；同时保留四种已消费字段的规范化资源内容及 missing/empty 区别。未消费字段仍不参与。digest 不再另行改变有效输出顺序。整个本地输入在 `WindowsRecognitionEvidence` 中按显式来源优先级及 Ordinal 标题排序后截取40条；这份实际传给 recognition 的 Titles/References/Companies/AuxiliaryDirectory 同时纳入 `native-local-evidence-v3`。相同语义资源的枚举重排不改变有效输入，实际 top-40 变化则失效。评分和 veto 实现未改。

Electron-compatible raw PE fingerprint 保持原有遍历选择/顺序、原始资源字节、basename及启用条件。兼容摘要与 native 资源选择共享有界读取缓存，避免重复 IO；两条路径合计仍受原总读取字节预算约束，不能因兼容路径无界读取。`electron-review-identity-v1` 不变。只有算法身份升为 `electron-recognition-v1-native-4`，旧建议恢复仍须重新识别，不继承旧决定权限。

### M5：初始化也是接纳事务

`ReviewInitializationState` 为 Uninitialized → Initializing → Ready；读取损坏/IO失败为 Failed（明确降级，新的合法结果只在内存可用，原文件禁止覆盖）。取消或未处理初始化错误不发布半成品，回到 Uninitialized，可重试；并发 Initialize 由同一 semaphore 串行，成功后不会再次读/合并。

Uninitialized/Initializing 期间不把 queue event 写入 Review authoritative dictionary。由现有有界 `RecognitionQueue` 保留最新 job/generation 状态；初始化完成时在同一 publication gate 下取最新 queue snapshot，与已校验、已复核的历史状态合并，统一执行原 `PrepareAdmission`（deterministic retirement、10000条/16MiB精确序列化、结构与候选限制），通过后一次性替换状态并进入 Ready。延迟事件总是读取最新 queue snapshot，不重放旧 generation。

无法容纳受保护的新建议时，保留已预算的合法基线，记录 ReviewCapacity 并撤销本次未接纳 job 的决定资格；不会先发布超限结果。Ready 后继续使用同一投影/预算/发布入口。所有增长或替换都先 PrepareAdmission，单项移除不会增加预算；历史仍最多256、按 CreatedAt/Guid 最旧先退役，actionable review 受保护。Ignore/Apply/Manual 的公开入口等待初始化屏障；Flush 的 Loaded 判断与快照捕获在同一锁内，初始化前不会把空快照覆盖历史。业务决定、canonical 校验、durable transaction 规则未改。

### 验证及证据

新增 `RecognitionFinalGateTests` **14项**，没有复制生产 digest/retirement/validation 算法。PE builder 仅构造合成资源；真实 Windows reader、parser、ranker、engine、queue、review service 与事务路径参与验证。暂停的 storage 是可控 IO 边界，生产 codec/service 执行实际预算与发布。

- M1：两语言资源重排；超过40原始标题的20次固定种子随机排列；超过4语言资源时先排序再截断；3个程序的48条PE标题组成实际40条输入并重复8次重排；39目录标题+chapter1/2 的原 Gate 反例通过真实 Windows reader及Apply；真实有效标题变化使旧Review stale且库字节不变。沿用6组 FileDescription/OriginalFilename等变更的 zero-write 回归、missing/empty/culture以及34组 Electron evidence 差分，legacy fingerprint 未改。
- M5：暂停近16MiB历史读取→1MiB live result→统一退役后发布；紧接 Ignore和Apply均成功；不可容纳的17MiB新结果显式拒绝；Ready阶段已有9MiB active再来9MiB结果，原状态与权限保留。10000历史+多个live结果+同条目old/new generation+两次并发Initialize只读一次、最终256历史+2新建议；损坏/IO失败保留文件，取消初始化可重试，初始化期间Flush不写盘。快照及发布通知观察中实际序列化校验均不超过10000/16MiB。
- Apply容量用例中的1MiB建议用于压力输入；提交时 fresh detail 返回符合既有书架字段限制的内容。早期测试曾被正确的 description 长度校验拒绝，已调整合成场景，未放宽产品校验。

| 最终验证 | 结果 |
| --- | --- |
| locked restore | 成功；锁文件/依赖未改 |
| Debug / Release solution build | 各0 warnings / 0 errors |
| Debug / Release完整C# | 各 **1002 passed / 0 failed / 0 skipped**；988既有全部保留+14新增 |
| 原差分及fault | **102 original、13 filesystem/cover组、32 online、254 recognition、14 fault injection**全部通过 |
| Electron | 完整 **665/0/0**；build通过；真实Electron隔离导入37项、重复0新增/37跳过、个人字段及reload/游戏文件保留、renderer errors0 |
| Release publish | 成功，`.artifacts/native-phase4b11/publish/win-x64` |

本机证据在 ignored `.artifacts/native-phase4b11/`：restore/build/test/publish/Electron日志、两份完整TRX、targeted TRX及`validation-audit.json`，源码/发布hash见`build-identity.json`，提交后由`checkpoint-receipt.json`绑定。发布二进制的源身份仍嵌入父SHA `41fa1e0a...`，必须连同源码hash和receipt识别，不能冒称嵌入最终checkpoint。未新增桌面截图或复用旧截图作为本轮证明；正常recognition/review/Apply由生产路径集成测试验证。

**M1 fully fixed / M5 fully fixed：本轮自检通过，PASS FOR REAL API SMOKE（self-check）。** 这不是已完成独立re-Gate或真实API smoke。正式profile、真实credential/API/游戏均未使用；不证明真实catalog/CDN、所有真实PE、长期压力或干净机安装。旧UNC/图片/workingDirectory等边界继续保留。仅建立Phase4B.1.1 checkpoint，然后暂停，不进入Phase5、不开始真实API smoke。

## WinUI Phase 4B.1：Recognition / Review 边界整改（2026-10-05）

本轮从 `codex/winui-phase4` / `7da6eebab3c79b66af65e628daad8ae1788644d4` 开始，只修独立 Gate 的 M1–M5 和两个 MINOR。不是下方 Phase4B 开发过程中的同名内部子步骤。本节 supersede 旧 Phase4B 关于 PE freshness 完整、候选详情绑定、重新识别标题独立性、历史候选安全及队列预算已经充分成立的判断；保留旧功能与测试记录的历史身份。未修改事务、Filesystem、Online、Image 核心架构、依赖或旧 Electron 实现，未进入 Phase5。

### 整改契约与生产路径回归

| Gate | 当前生产实现 | 本轮回归与实际结果 |
| --- | --- | --- |
| M1 PE digest | `ExecutableEvidenceParser` 保留旧 `Fingerprint`；新增 `pe-evidence-v1`，`WindowsRecognitionEvidence` 将其纳入 `native-local-evidence-v2` | 6组真实 Windows reader → engine → queue → review → Apply 的合成 PE 原地变化：物理身份不变，旧决定 Stale，库字节不变、持久化 probe 0 calls；全部通过 |
| M2 candidate/detail | `RecognitionViewModel` 独立 candidateSelectionGeneration；改变选择即取消并清空，接纳同时检查 review/candidate/selection/request/auth/cancellation | 已加载切换、200迟到、ABA、跨review、所选/显示ID及Apply目标均通过；真 WinUI 延迟200后切201，放行200仍显示201，Apply持久保存201 |
| M3 provider title | `RecognitionEntrySnapshot.EvidenceEntry` 仅在 Reidentify、有旧metadata且 NameIsCustom!=true 时去掉自动名称与旧metadata；原 `Input.Entry` 不变 | false/null/true三路径通过；目录/程序/配置/说明/索引仍由原reader提供。真UI中旧provider名称仍是快照展示，但新识别线索只有通用目录game，候选0；原关联和个人数据保留 |
| M4 corrupt history | `RecognitionReviewCodec.Validate` 对 Result=null 的 historicalCandidates 也逐个验证；`RecognitionReviewService` 保留初始化损坏诊断及原文件 | null、999、NaN/Infinity、缺ID、超长/超量、坏evidence/veto/conflicts、有效历史回归通过。codec→storage→Presentation选择无NRE；坏文件写入0次。真UI主库5条可用，重新识别5项完成且持续显示明确损坏提示，坏文件SHA256不变 |
| M5 admission | codec `PrepareAdmission` + service 在接纳前对拟定状态检查；启动生命周期说明、RefreshValidity及Manual清理也先预算后发布 | 9999/10000历史、无安全可退役时拒绝且旧状态/文件不变、反复rerun、Ignore/Apply、重启、算法过期、先触及字节预算及说明文字增长均通过；真UI容量闭环见下 |
| MINOR version | `RankedRecognitionCandidate.VersionNeedsConfirmation` 与 `Conflicts` 分开；`ReviewSafeRanking`/Presentation使用不同理由 | 不确定版本、章节冲突均阻止直接Apply，但只有后者称身份冲突；真实FD场景显示“版本信息不足，需要确认” |
| MINOR auth | `RecognitionResult.WithoutEnrichment` 保留 Input/Local/Identity，仅撤销 remote candidates/auth；queue末端和已发布review均处理 | credential clear/replace发生于最终current检查时，本地索引候选/证据不变；generation/lifecycle变化仍整项过期。真UI模拟替换后Summer Pockets的1条本地索引及VNDB/Bangumi来源仍在 |

新增测试集中于 `native/TheOrder.Tests/RecognitionRemediationTests.cs`，**44例**，生产路径优先。既有 `DifferentWorkRecognitionNeverAutomaticallyReplacesExistingRelation` 改为明确独立目录标题，避免测试本身依赖被禁止的旧provider自动名称；安全断言仍保留。

PE摘要只纳入parser实际消费的 ProductName / FileDescription / CompanyName / OriginalFilename、有效有界输出及helper拒绝状态。清洗仍沿原契约；字段值、资源集合及有效输出以Ordinal稳定排序，SHA-256使用固定版本JSON，不依赖culture、字典迭代或进程hash。缺失字段`[]`与存在但空字段`[""]`分开，无版本资源为显式missing。受原PE读取字节/资源/块数/标题数量上限约束。LegalCopyright等未消费字段不影响新摘要（可能仍影响兼容旧原始资源hash）；资源遍历顺序改变且有效证据相同时摘要相同，包括tr-TR测试。若资源顺序改变实际截断后的有效输出，则摘要也变化，拒绝旧Apply。Apply在既有事务beforeCommit内再次读取同一EvidenceEntry，差异先置Stale再拒绝，零库写入。旧 `electron-review-identity-v1` 及 Electron PE fingerprint 契约不变；algorithm提升为 **`electron-recognition-v1-native-3`**，旧算法建议恢复后不能获得提交权限。

候选详情在加载时显示loading、失败/取消显示“详情不可用”，绝不保留其他候选的封面和说明。DisplayedCandidateId绑定请求身份，remote canonical身份仍由既有catalog严格验证；合法remote merge不绕过显式本地决定。

### Review 数据、预算与退役策略

Review是可重新生成的建议，不是authoritative library。版本化文件格式继续为 `native-recognition.json` v1；不迁移正式资料。坏历史文件只读保留、拒绝自动覆盖，主书架可以打开；当前识别可在内存继续，但页面明确“历史核对队列无法安全读取，原文件已保留；本次建议暂不持久保存”。后台保存不能把此诊断覆盖为泛化失败。清理/重建坏文件须另有明确处理，不引入自动删除或额外backup系统。

历史候选要求非null、目录ID为1–20位数字、title/original/reason各<=2000字符且无NUL、confidence有限且0–1、每项<=5候选；非历史result另校验证据数组、分数、veto/conflict/uncertain一致性和mode/status枚举。Result=null不能成为NeedsReview，也不能跳过历史检查。NaN/Infinity非标准JSON或字符串都按损坏处理。

接纳同时限制 **10000总条目、16MiB精确有界序列化、每条5候选**。NeedsReview是受保护的actionable状态，不因空间不足删除；Stale/NeedsRecheck（包括算法过期历史）可退役，按CreatedAt升序再Guid排序，最旧先退役。历史常规最多保留**256条**，再按总数量/字节继续退役。无历史可安全退役且新状态仍超预算时，返回明确ReviewCapacity、撤销新job的决定资格，不部分修改旧review字典或已保存文件。Ignore释放精确ID，Apply后清理同样受预算与CommittedWithWarning语义保护。启动和手动关联新增的中文状态说明也可能增加字节，必须重新预算，不能绕过接纳约束。

### 本轮验证及证据范围

产物均在 ignored `.artifacts/native-phase4b1/`，不提交。

| 验证 | 重新执行的结果 / 文件 |
| --- | --- |
| locked restore | 成功，`restore.log`，未更新锁文件或依赖 |
| Debug / Release solution build | 各 **0 warnings / 0 errors**，`final-debug-build.log` / `final-release-build.log` |
| Debug / Release完整C# | 各 **988 / 0 / 0**，`final-debug.trx` / `final-release.trx`；944既有+44整改 |
| 保留差分 / fault | **102 original、13 filesystem/cover组、32 online、254 recognition、14 fault**全部通过；13组包含原source freshness检查，254为220标题/排序+34PE/text，不把其他单元测试算为差分。`test-audit.json`从两份TRX交叉提取 |
| Artifact pipeline | **32 / 0 / 0**，`artifact-tests.log`；实际采集先assert requested/runtime再写文件 |
| Electron | 完整 **665 / 0 / 0**；build成功；import回归37新增、重复0新增/37跳过、个人字段/reload/游戏文件保留、renderer errors0；`electron-*.log` |
| Release publish | `publish-final/win-x64`，`final-publish.log`成功；最终包真WinUI启动、识别、Apply/Ignore/Manual、正常退出 |

真UI全部使用 `.the-order-synthetic` 标记隔离profile、合成HTTP和内存fixture credential。`generate-recognition-remediation-profile.mjs`拒绝复用已存在目录和正式目录边界，生成5条与不可执行的占位文件，可选normal/corrupt/capacity。只有DIAGNOSTICS+ONLINE_FIXTURE+RECOGNITION_FIXTURE+RECOGNITION_BOUNDARY_FIXTURE显式同时启用时才显示候选延迟/放行/模拟凭据变化按钮；没有真实网络/凭据验收。PE原地变化由生产Windows reader集成回归验证，没有实际游戏EXE桌面操作。

真实容量过程：初始10000条旧历史 → 打开退役为256 → 5条新建议接纳为261 → Apply目标201后260 → Ignore精确建议后259 → 同条目Manual目标300后258（256历史+2 actionable，132887B）。原个人名称/笔记保留，Ignored ID落盘消失；`capacity-before.json`、`capacity-open-runtime.json`、`capacity-apply-runtime.json`、`capacity-ignore-runtime.json`及`capacity-after.json`可交叉核对。最终corrupt实例PID8288、capacity实例PID22668均exit0，见`final-*-exit.json`。坏文件在两轮打开、识别及关闭后SHA256仍为 `D3828B380D87E94531E7C281A67DA2C6C02A015D1EBD55B50A09B4D2C64A6785`。

新UI证据共**11组manifest/UI tree/JPEG，11个不同截图hash**；全部requested/runtime、tree内runtime、文件长度和SHA256独立复核，`artifact-audit.json`记录结果，关键已保存图片再次目视核验。分两批：

- `publish`（WinUI MVID `01e9cb52-bd8c-4e54-9bfd-31b111dfd403`）8组：`candidate-loaded-200-to-201`、`candidate-200-delayed`、`candidate-late-200-discarded`、`apply-selected-201`、`version-needs-confirmation`、`reidentify-provider-title-excluded`、`auth-change-local-preserved`、`manual-link-preserved-local-fields`。随后发现并修正损坏提示被后台保存覆盖，以及状态说明预算收尾；此批不能证明这些最终修复。
- `publish-final`（WinUI MVID `7d69294c-61ee-411f-baad-1df14773ac6e`）3组：`review-storage-corrupt`、`review-capacity-admission`、`review-capacity-decisions`；复核最终保存和退役路径。各层DLL/EXE hash分别保存在`build-smoke1.json` / `build-final.json`，完整回归针对最终源码。

两批程序集checkpoint都仍指向构建父提交`7da6eeb...`，不是即将创建的4B.1提交；MVID+全层hash区分工作区构建，提交后补本机checkpoint receipt绑定，不能冒称二进制内已嵌入新SHA。旧`.artifacts/native-phase4b`仅为当时功能证据，不能证明本轮七项整改；本轮corrupt最终证据取代早期泛化告警观察。旧Phase3A作废证据仍作废。

**M1–M5和两个MINOR整改自检通过；PASS FOR PHASE 5仅指本轮整改自检，不代表独立targeted re-Gate已执行，也不是Phase5实施授权。** 接下来应先targeted re-Gate，再安排用户授权的真实API smoke。本轮真实API明确未执行（按整改范围禁止），正式profile/真实credential/游戏目录均未访问。合成验收不证明真实catalog/CDN、全部游戏、长期压力或干净机安装/签名/升级。此前UNC/generic reparse、10k unique images及正式库写入前workingDirectory门禁继续保留；性能未重新优化或建立新SLA。建立Phase4B.1 checkpoint后暂停。

## WinUI Phase 4B：Recognition / Review（历史本机验收；独立 Gate NO-GO，整改见上，2026-10-05）

基线 `0de0b342623c0e6a047fd354843903779e564e2e`，保持 `codex/winui-phase4`。用户已授权4B，禁止Phase5/正式资料或凭据/真实游戏执行。先4B.1纯识别、差分和lifecycle，再4B.2持久核对/提交/WinUI；下表来自本轮重新阅读旧源码，不是完成声明。

4B.1 内部 checkpoint `7f858ad54b6d90e2668ffd24dfdf2bcffc3fd0a2` 先于 Review/UI：纯 `RecognitionEngine` 不持库，`RecognitionQueue` 仅依赖 `IRecognitionLibrarySource`；两 worker、1000 项调度上限。每项最多6个搜索、2个详情/发行补充；先对全体有界候选评分再保留5个 review options。索引128项饱和时不作唯一身份桥接，显式提示候选不完整。随后完成下面4B.2与最终验收；内部checkpoint不单独代表整个4B通过。

核心测试历史证据 `.artifacts/native-phase4b/core-*`：Debug/Release各911/0/0（原609+新增302）。254组实际 Electron 差分（220标题/排序+34文本/PE），生产指纹六向量（含孤立surrogate）；另有索引炸弹、物理文件变化、ABA/新请求/安装/关联变化、双worker/取消、网络降级/错误ID/auth变化回归。下面最终结果含新增Review/Presentation验收，不再以核心测试数替代全量。

有意差异：建议默认不自动提交；新增8MiB单次文本总预算、候选128/结果5上限、无效索引保留本地证据、在线补充失败不终止其他项。索引和PE保留原文件/资源大小限制；PE默认关闭。所有重工作在后台；SHA256只比对输入，生命周期仍由Phase2队列及独立recognition generation保护。暂不做跨请求识别结果缓存，避免把缓存当review权限。

| 旧行为 | 本轮实际源码审计 / 原生迁移约束 |
| --- | --- |
| Evidence sources | `game-identification.ts`：优先安装排序，最多100个安装的目录/所选exe/1MiB PotatoVN sidecar/128B steam_appid；sidecar标题与ID同源，不独立投票。preferred安装补充PE/档案/Steam配置，以及固定manual/help/StartData下的文档；既有关联在reidentify时不作为独立标题，个人名称仍保留 |
| bounded local reads | `local-game-evidence.ts`：目录512项，所选exe+最多4个补充exe，可选PE总资源1MiB、header额外64KiB、96 sections/32 directories/256 entries/1024 blocks/深度5，全部语言成功后才采用；不执行模块。配置128KiB，档案名最多5。`local-text-evidence.ts`：固定5目录各128项、8文件、文件128KiB、只读32KiB前缀、16标题/8品牌；HTML仅标题/明确产品标签，不执行或跟随链接 |
| Encoding | manual/config按BOM UTF16 LE/BE、允许的charset、严格UTF8、再Shift-JIS与GB18030的既有启发顺序；truncated prefix允许尾部半字符。sidecar/readme旧路为UTF8→GB18030，Steam配置UTF16LE BOM或UTF8。不得统一猜码后无界读文件 |
| Normalization | `title-matching.ts`：500 UTF16单元、NFKC、仅移除包装/语言噪声、Latin重音折叠但保留日文浊点、Unicode lower、标点空格、最多8 query variants；章节/FD/remake/edition保留独立marker。比较60%bigram Dice+40%编辑相似度、round至0.001并设版本/编号/短词上限；不是概率 |
| Title index | 本轮解包核对现有 `data/title-index.json.gz`：2,024,873B压缩/5,851,882B解压，v1，**56,064 records / 97,038 titles**；旧上限8MiB/32MiB/150k行。unique exact normalized title lookup，不模糊推导强ID；必须同一索引行连接本地观察、官方名称、ID。保留Apache2许可及THIRD_PARTY_NOTICES，不重新生成来源 |
| Candidate generation | 名称/引用各8、证据标题/引用各40、index proofs20、独立primary query优先后最多6搜索；refs每批100且4096字符。在线同ID去重，完整详情shortlist3，发行辅助最多2，review options5；全体竞争候选不能因补详情而消失 |
| Score factors | `evidence-matching.ts`：folder/custom-name、PE、resource分别对官方/全部可信名称比较；物理Steam+独立标题0.99(level3)，正式发行0.985(level2)，同一索引行0.98，ID+标题约0.975–0.99，无ID完整标题0.95；品牌最多+0.01且不授予资格；sidecar弱支持，不增加独立票 |
| Veto / tie | 章节/版本、Latin主标题矛盾、独立ID冲突、补丁/备份目录独立取消eligible，不能靠score恢复。eligible优先→evidence level→score→ID；唯一候选还需margin≥0.075、无分裂refs/同名无独立ID/不同VNDB身份。原生采用显式veto并固定排序tie契约 |
| Automatic/manual | 旧`MetadataMatcher.run`在全详情复核后允许显式策略auto apply；different-work reidentify只建review。新Recognition本身不得持writable repository；结果只建议，默认显式Apply，排序分数不等于提交授权。manual仍验证远端canonical、当前生命周期并走CommitLinkAsync |
| Pending / ignore | `metadata-matching.json`最多16MiB/10000 reviews/options5，随机reviewId、source快照、stale标记；启动running→cancelled，不自动联网。Ignore仅移除精确reviewId、原子保存失败恢复，非永久忽略游戏/候选；新识别仍可出现 |
| Fingerprint | 旧reviewIdentity=SHA256(JS JSON `[name,nameIsCustom,metadata.id或null,preferredInstallationId,installations[id,path,exe]]`)；personal notes/rating/launch args不影响。decision hash另结合epoch/generation/request；本地内容变化未纳入旧hash。继续复用Phase1 fixture；新增版本化evidence摘要补齐本地内容和filesystem身份，绝不把hash当token |
| Stale / lifecycle | 旧sourceCurrent+captureReview/assertReviewCurrent+事务beforeCommit防乱序、删除同ID重建、安装/关联ABA；个人字段可兼容保留。重启新token，旧UI决定无效，既有stale记忆不可洗白。原生复用Phase2 token、单独recognition generation、review身份和Apply前重验；无法跨重启证明的旧decision保守NeedsRecheck |
| Network failures | 旧凭据前置及某些ref/API错误会终止batch；新要求明确加强为本地候选可用、enrichment unavailable，每项失败独立。旧auth迟到结果丢弃，auth epoch不是recognition generation |

已读相关 title/evidence/game-identification/local-game/local-text/metadata-matching tests，以及main的review resolve/manual link/refresh和library token/merge契约；危险用例包括copied sidecar、polluted alias、跨语言同一索引行、正式发行与不同VNDB竞争、queued cancellation、review persistence失败、关联ABA、same-ID重建、late UI decision。实现期间以生产路径回归补证，不能用此审计表代替测试或桌面验收。

### 4B.2 最终架构、决定语义与有意加强

`RecognitionEntrySnapshot` 固定 immutable entry 与 epoch/generation/revision/request；`RecognitionQueue` 在第一次 await 前保留独立随机 generation，旧同ID工作不得胜出。auth epoch只由4A.1共享adapter管理。Windows读取器复用物理身份/祖先目录保护并限制文件数量、字节、目录、编码、PE资源；不执行EXE或加载DLL。索引异步惰性加载，上限8MiB压缩/32MiB解压/150k行。查询与候选有界、同ID去重、排序固定；工作者无可写库引用。

最终 algorithm version **`electron-recognition-v1-native-2`**。纯 `EvidenceRanking` 继续与实际Electron oracle相符；`ReviewSafeRanking` 在其外增加保守版本规则：非弱本地标题显式FD/remake/edition等，与目标官方/主名相比版本不确定且无同版本明确匹配时，设veto、取消high-confidence，不抬低原排序分。完整golden `FD → base` 用例暴露了旧helper对此仅降分的盲点，因此此加强是记录在案的行为差异。分数仍是排序系数；所有候选，包括高置信，均非自动批准。

`RecognitionReviewService` 是独立写入用例层。`native-recognition.json` version1，上限16MiB/10000建议/5候选，合并保存通知、串行落盘、Flush+原子替换；不复制个人笔记/分类/游玩记录/本地封面作为第二份用户库。旧metadata-matching只读导入为历史；重启重新核验源指纹并降为NeedsRecheck/保留Stale，进程权限不序列化。坏历史文件拒绝覆盖并显示持久化错误，不把它当空队列。

- **Apply**：精确review ID、当前generation、未veto候选、源快照 → 原事务token → refresh canonical详情/必要发行/重新rank → 事务队列内再次读本地证据及auth/cancellation/current-result → 原CommitLinkAsync。取消或stale发生在durable边界前不写；开始原子写后沿用原有一致性/drain。允许后来兼容的个人编辑并保留；提交后的review cleanup故障返回CommittedWithWarning，UI刷新状态仍独立，不能误报“保存失败”或自动重复提交。
- **Ignore**：先持久保存移除该ID的候选队列，再移除内存/标记Completed；失败保留建议。不是永久黑名单，不修改游戏/个人资料；以后可重新识别。
- **Manual Link**：显式编号意图与自动证据分开，仍验证严格编号/canonical/当前token并通过事务内核；错误ID/missing/非法merge不落库。成功后同条目的旧建议及队列计数变Stale。
- **Re-identify / batch**：从书架所选、详情或review进入，已有资料也能识别；缺项模式只选本地安装且metadata/cover缺失，愿望条目不参加。最多1000、2worker；各项失败独立。已有work只作为候选，识别本身不更换关联。取消撤销本轮未决建议，历史NeedsRecheck仍是历史，不能获得新权限。
- **Offline**：保留本地证据、同一索引行/独立引用与local candidates，在线关闭/故障标记EnrichmentUnavailable。在线使用4A生产transport/canonical/auth/cache，60秒识别补充预算，不读取正式credential。未新增跨识别结果缓存；算法版本与fingerprint不充当授权。
- **WinUI**：RecognitionViewModel跟踪排队、决定及详情任务，按需详情、分数/来源/veto/失效说明、显式Apply/Ignore/Manual；100ms更新dirty投影。实测后保留相等卡片实例、支持键盘SelectionChanged、切换重置详情滚动、清除旧详情，快照标明“识别时关联”。关闭先取消非durable识别并等待受跟踪决定/持久化，再关闭原workspace。

### 最终验证和证据身份

所有下列本机生成物位于 `.artifacts/native-phase4b/`，不提交。

| 验证 | 实际结果 / 文件 |
| --- | --- |
| locked restore | 成功，`restore.log`，无依赖升级 |
| Debug / Release solution build | 各0 warnings/errors，`final-debug-build.log` / `final-release-build.log` |
| 完整C# | 各 **944/0/0**，`final-debug-verified.trx` / `final-release-verified.trx`；最后只加强fixture生成测试，发布产品字节未变 |
| Recognition新增 | **335**：core222、boundary18、PE/text36、online17、queue9、review15、acceptance14、presentation4。core包含220 oracle+2契约，PE/text包含34 oracle+2契约；335不是335个差分 |
| 保留回归 | original transaction102（该类103含freshness）、filesystem/cover13、online32、fault injection14，均在完整套件 |
| Golden / invariant | 三例实际engine→queue→review：正例、章节冲突、FD歧义；另验证固定排序、增加无关候选不改top evidence、veto不能high、stale/cancel拒绝Apply、同ID/ABA/重启/个人字段保留 |
| Artifact collector | **32/0/0**，`final-artifact-tests.log`；含8个recognition嵌套状态错配；真实采集一次目标切换未完成时assert拒绝保存，随后观察实际状态再采集 |
| Electron | 完整 **665/0/0**，`electron-full.log`；build成功；`electron-import.log`：37新增、重复0/37、个人字段/reload/游戏文件保留、renderer errors0 |
| Release publish | `publish-final/win-x64`成功，`final-publish.log`；真实启动、决定、取消、重启、关闭 |
| 真实API | **SKIPPED — no safe development credential**；不以合成HTTP冒充真实服务 |

基线工具测试在通常套件不运行耗时测量，另以 `THE_ORDER_RECOGNITION_BASELINE=1` 显式执行，`baseline.trx`实际1通过/33秒。fixture生成测试每次在临时隔离目录真正运行Node生成器并以生产codec读取7条，不依赖之前遗留的UI profile。正式资料、真实游戏目录、密钥均未读取。

### Phase 4B baseline（非SLA）

实际分布索引+Windows有界本地读取+识别engine/双worker；在线走生产transport与1ms合成HTTP。一个测试进程顺序跑六组，使用重复合成标题/目录，存在JIT/详情缓存复用，非1000个独立真实游戏、非UI review持久化总耗时或公网基准。真实索引每组均56064行/97038标题。数据在`recognition-baseline.json`。

| 模式 | entries | 总耗时 ms | entries/s | working set MiB | index load ms | 最大jobs / HTTP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| local | 1 | 1614.8 | 0.62 | 181.3 | 1515.8 | 1 / 0 |
| local | 100 | 1576.1 | 63.45 | 216.5 | 962.7 | 2 / 0 |
| local | 1000 | 6457.0 | 154.87 | 220.0 | 724.0 | 2 / 0 |
| synthetic online | 1 | 853.0 | 1.17 | 248.8 | 762.1 | 1 / 1 |
| synthetic online | 100 | 2871.1 | 34.83 | 241.9 | 838.5 | 2 / 2 |
| synthetic online | 1000 | 19612.5 | 50.99 | 246.9 | 706.7 | 2 / 2 |

在线实际HTTP数4/302/3002；本地模式0。首组本地含冷启动，不能从1-entry行声称在线更快。baseline发生在最后纯显示/队列Stale计数修复前，识别算法及证据读取源码未变，但不是最后发布包重新计时；无FPS或正式SLA声明。

### 真实WinUI smoke与23组artifact

`native/scripts/generate-recognition-profile.mjs <new-directory>` 只生成新合成目录，拒绝存在目的地和正式profile边界。使用7个虚构安装（game.exe是非可执行纯文本），其中Summer Pockets仅借用公开索引标题测试，不读取真实游戏。启动显式设置 `THE_ORDER_NATIVE_DATA_DIR`、`THE_ORDER_NATIVE_DIAGNOSTICS=1`、`THE_ORDER_NATIVE_ONLINE_FIXTURE=1`、`THE_ORDER_NATIVE_RECOGNITION_FIXTURE=1`。合成handler覆盖搜索/详情/发行/图像；生产transport、mapper、cache、事务、WinUI和图片pipeline仍执行。普通装配不启用该fixture。

| 真实场景 | Artifact ID |
| --- | --- |
| 单条本地Steam+标题高置信；已有资料也能重新识别 | `local-high-confidence`；最终`final-high-confidence-selected` |
| 明确Apply，提交成功且个人资料保留 | `review-apply-committed`；最终`final-apply-clears-details` |
| 批量缺失资料、活动进度 | `batch-progress`（总7，含先前已完成1） |
| 章节冲突veto、应用禁用 | `conflict-veto`；最终`final-reidentify-veto` |
| 多候选、非high、两个明确编号 | `multiple-candidates` → **`final-multiple-candidates` supersede其错误高亮** |
| 无候选 | `no-candidate` |
| 在线连接失败仍保留本地线索 | `offline-enrichment-failure` |
| 纯本地模式实际索引命中/来源编号 | `final-offline-local-index` |
| review持久化与重启NeedsRecheck，不恢复Apply权限 | `final-restart-review-queue` |
| 重新识别使旧review Stale；真实计数一致 | `final-stale-review` |
| 手动错误86→90拒绝、正确300保存 | `manual-wrong-id-rejected` / `manual-link-committed` |
| 最终手动301保存、旧review与计数Stale | `final-manual-link-progress` |
| Ignore只移除精确建议，已有详情清空 | `final-ignore-clears-details` |
| 键盘切换同步详情和高亮 | `final-keyboard-selection` |
| 识别活动→取消→超过旧响应窗口不复活 | `final-recognition-active` / `final-recognition-cancelled` / `final-cancel-after-old-response` |
| 活动时关闭 | `final-active-before-close` + `final2-exit.json`，PID42136 exit0；17:49:25.841Z请求关闭、17:49:26.366Z退出，约0.52秒，非退出SLA |

采集pipeline每次重新读真实窗口，requested字段（含nested recognition）等于actual才允许写artifact，manifest最后落盘。`artifact-sanity.json`实读23manifest/23UI tree/23JPEG，逐项requested/runtime/tree/字节数/hash一致，23个不同截图hash；又实际重开落盘关键截图检查。9组来自早期`publish`，14组来自最终`publish-final`；不把早期截图说成最终UI修复证据。早期Apply/Manual不证明后来的详情清理和stale计数修复，以上对应final场景负责补证。旧Phase3A作废截图保持作废。

两批程序集checkpoint均为构建前 `1.0.0+7f858ad54b6d90e2668ffd24dfdf2bcffc3fd0a2`，必须结合manifest MVID、`build-bundles.json`各层hash、最终源码清单及`checkpoint-receipt.json`绑定；不可把该assembly version当最终提交SHA。初批MVID `25c1d592-b817-4022-bc68-df143ffdb94a`，最终MVID见manifest。最终包重启两次正常；最后PID31524 exit0。`active-close-data-receipt.json`记录活动退出后7条库/7历史建议；后续多候选复核新增1建议，最终`data-receipt.json`为7条库/8建议。关联200/300/301、名称、个人笔记、分类、安装均实读保留；其余未关联条目未自动改变。

范围限制：这是隔离合成验收，不证明真实catalog/CDN内容、所有PE/编码/游戏、长时间大规模review IO、断电/干净机安装/签名升级；既有UNC及generic filter环境缺口仍在。早期人工恢复窗口/读取几何失败属于工具状态，刷新窗口后恢复，没有为绕过automation改变业务语义。可进入独立 **Phase4B Gate Review**；本阶段checkpoint后暂停，Phase5不获自动授权。

## WinUI Phase 4A.1：Online Result Acceptance Remediation（2026-10-05）

基线 `f737bf7c4174cdabe66d25ffa60629d939ca69dd`，保持 `codex/winui-phase4`。本节 supersede 下方 Phase4A 的结果接纳及凭据操作完成判断；原 Gate 为 NO-GO、无 BLOCKER，原有分层保留。没有 Phase4B、识别/匹配/review queue、正式资料/凭据、真实游戏操作、依赖升级或正式程序替换。以下为整改自检证据，不把确定性测试说成真实API兼容保证。

### Gate 自检：生产位置、回归与结果

所有新增回归位于 `native/TheOrder.Tests/OnlineAcceptanceTests.cs`，最终 Debug/Release 各52条全部通过。

| 项目 | 生产位置 / 契约 | 回归与实际结果 |
| --- | --- | --- |
| M1 fixed | `Application/OnlineAuthContext.cs` 的 AuthenticatedCredentials 排队并立即作废旧auth；`Presentation/OnlineViewModel.cs` 的 CredentialMutation/TestConnectionAsync 分离，`WinUI/Views/SettingsPage.xaml.cs` 成功后才清当前输入 | CredentialIntentSupersedesUncooperativeConnectionTest、FailedCredentialMutationDoesNotClaimSuccess、CredentialWritesKeepInvocationOrderAndLatestIntent、FailedQueuedIntentDoesNotDropNewerSave；save/clear均真正执行，SaveA→B、Clear→Save最终为新值，失败不误报。真UI慢连接期间删除/替换成功，失败保存输入仍在 |
| Auth generation centralized | composition root 同时向 ICredentialProvider/ICredentialSettings 注入同一 AuthenticatedCredentials；transport 每operation CaptureAsync/Accept，details缓存带epoch；VM检查用户请求generation及auth generation | AuthEpochInvalidatesCachedDetailsWithoutCallerClearCache、AuthChangeRejectsOldProductionResult、OldSearchSuccessOrUnauthorizedCannotReplaceNewAuthenticatedSearch；旧连接/搜索/详情和401均不能覆盖新上下文，无需调用者主动清缓存 |
| M2 fixed | `Infrastructure/Online/NextMoeCatalog.cs` 在Map后严格比较work.Id和已验证merge链current，失败不DeferCache | OnlyValidatedMergeChainCanChangeIdentity六例含直接错配、多跳正确/错误、重复HTTP计数；WrongDetailIdentityCannotMutateWishlistOrRefresh实际生产OnlineLibraryClient/事务路径保持库文件字节不变，拒绝在CommitLinkAsync之前；真UI86→work90显示无效数据且Details为空 |
| M3 fixed | `CatalogTransport.cs` ReadBoundedAsync在EOF也检查取消；RequestBudget的BodyComplete/Parsed/Mapped/BeforeCache/AfterCache/BeforeReturn gates；Operation在auth锁中完成最终接纳；NextMoeCatalog延迟提交缓存并可回滚，ReadCurrent保护并发读 | EofCancellationRejectsResultAndDoesNotFillCache、CancelledAcceptanceStageNeverLeavesReusableCache、OverallDeadlineIncludesMappingAndFinalReturn、RetryBoundaryCancellationDoesNotIssueAnotherRequest、CancellationAfterMappingDoesNotPublishDetails、CancelledPaginationNeverAppendsResults、AuthChangeAtAcceptanceRejectsAndRollsBackOldCache、CacheReadersCannotObserveProvisionalAdmission；取消/过期后新请求真实走HTTP，无分页追加或provisional cache泄露 |
| Corrupt-vs-missing fixed | `Platform.Windows/IsolatedCredentialStore.cs` HasCredentialAsync复用ReadAsync验证；`Application/OnlineContracts.cs` CredentialStorageCorrupt；VM映射CredentialCorrupt | CredentialStatusAndReadAgreeAndNeverOverwriteCorruption七例：missing/empty/oversized/random/tampered/invalid-plaintext/valid；真实DPAPI与原文件字节保留；CorruptCredentialStatusHasIndependentPresentationState及真UI损坏提示通过 |
| DNS callback tested | `Infrastructure/Online/RemoteCoverSource.cs` CreateHandler注入resolver/connector，CreateClient仍使用系统DNS和原ConnectSocket；检查所有地址公开后直接把同一地址数组传给connector | ProductionHttpHandlerUsesOnlyOnceResolvedValidatedAddresses从HttpClient.SendAsync穿过真实ConnectCallback：public允许、private/mixed/IPv4-mapped private拒绝且connector0次、rebinding仅解析一次；ImageRedirectToPrivateIsNotFollowedOrAuthenticated证明不跟随/不带auth或cookie。synthetic connector在socket/TLS之前终止，无内网访问 |

凭据意图优先于连接测试，调用即推进进程内epoch，串行完成各个显式save/clear；排队期间新请求等到凭据稳定，不读取中间值。失败旧意图不吞掉后续意图。epoch只代表进程内上下文，既不持久化也不是secret或credential hash。已有可复用详情只在epoch一致时命中；旧epoch条目可留在有界缓存中等待替换/淘汰但不可复用。匿名图片缓存与auth无关，不为形式统一清空。新Phase4B caller必须复用共享adapter/transport，并继续执行自己的请求时效和本地事务token检查；本轮没有实现该阶段。

结果接纳覆盖完整body→parse→map/identity→cache→return。默认45秒仍包括排队、网络、重试及CPU映射，max retry2、8MiB、严格UTF-8、merge最多3跳/防环不变；测试以受控elapsed 44.9→45.1秒验证后半段deadline。缓存写入与最终检查失败回滚位于同一auth锁，cache读取也在该锁内，不让并发读取得临时写入；取消可以淘汰无关的可丢缓存项，但不会把取消结果作为缓存留下。成功的最终acceptance之后才发生的取消不追溯撤销已完成请求。旧Electron未检查最终identity，这里是有意加强，32个既有online oracle并未为了兼容而豁免本规则。

凭据仍保存为 **raw DPAPI blob，尚无应用层format/version envelope**。仅文件不存在是NoCredential；无效长度、解密或格式失败是CredentialStorageCorrupt，自动检查保留原文件；只有用户明确保存/移除才覆盖/删除。Phase6/migration前评估versioned envelope与legacy raw-DPAPI兼容。PasswordBox仅在保存成功、该意图仍最新且输入未被再次编辑时清空，不能把旧文档的“保存立即清空输入”理解为当前行为。

### 实际回归

| 验证 | 最终结果 / ignored证据 |
| --- | --- |
| locked restore | 成功，`restore.log`；无依赖变化 |
| Debug / Release solution build | 各0 warnings / 0 errors，`debug-build-final.log` / `release-build-final.log` |
| 完整C# | 各609 passed / 0 failed / 0 skipped，`tests/debug-final.trx` / `tests/release-final.trx`，557+52 |
| 差分 / fault | 原transaction102（TRX类合计103含freshness）、filesystem/cover13、online32、fault injection14，两个配置均通过 |
| Artifact tests | 24/0/0，`artifact-tests.log`，保留错配拒绝、fresh snapshot、禁止覆盖旧场景 |
| Electron | 完整665/0/0，`electron-full-unit.log`；早期relevant192也通过，不用缩小集合代替完整回归 |
| Electron build / import | `electron-build.log` / `electron-import.log`：37新增、重复0新增/37跳过，个人字段/重启/游戏文件保留，renderer errors0 |
| Release publish | `publish-final/win-x64`成功，`publish-final.log`；实际启动及请求活动期间正常关闭exit0 |
| 真实API | **SKIPPED — no safe development credential**；未读取正式Electron凭据来补验收 |

以上位于 `.artifacts/native-phase4a1/`。最终测试和发布之后只更新原交接文档及ignored证据，不再改产品源码。`artifact-sanity.json`逐场景核对，`build-bundles.json`保存两批所有TheOrder DLL/EXE hash；`validation.json`保存最终测试计数与日志hash，`checkpoint-receipt.json`将证据绑定到新checkpoint。程序集InformationalVersion仍为构建前基线 `1.0.0+f737bf7c4174cdabe66d25ffa60629d939ca69dd`，不是新checkpoint SHA；必须结合实际包路径、MVID、各层hash和源码清单核对。

### 真实WinUI synthetic-network smoke与证据范围

使用 `.artifacts/native-phase4a1/ui-profile` 的已标记隔离3条库。显式设置 `THE_ORDER_NATIVE_DATA_DIR`、`THE_ORDER_NATIVE_DIAGNOSTICS=1`、`THE_ORDER_NATIVE_ONLINE_FIXTURE=1`、`THE_ORDER_NATIVE_ONLINE_REAL_STORE=1`、`THE_ORDER_NATIVE_ONLINE_SLOW_CONNECTION=1`。REAL_STORE仅指实际 **隔离DPAPI store**，不访问正式凭据；所有输入为合成token。HTTP由SyntheticOnline生成，生产transport/parser/catalog/VM/事务/图片解码仍执行；slow connection/detail为40秒且响应取消。正常装配不启用这些seam。结束后不保留进程环境覆盖、不替换正式应用。

| 真实观察 | 新Artifact ID |
| --- | --- |
| 空凭据文件显示损坏并保留；失败保存PasswordBox仍有输入 | `credential-corrupt`、`save-failure-input-retained` |
| 慢连接→删除：Loading/credential present→Idle/absent，实际隔离文件已移除 | `connection-active-before-clear`、`credential-removed-during-test`；最终包复验 `final-connection-active`、`final-credential-removed` |
| 慢连接→替换：新值成功保存、输入清空、auth epoch递增；超过旧响应窗口仍Idle | `connection-active-before-replace`、`credential-replaced-during-test`、`replacement-after-old-deadline` |
| 86返回work90被拒绝，无详情/库变化；初批再次点击确实增加请求计数 | `invalid-detail-identity`；最终包 `final-invalid-detail` |
| 慢详情→取消→再次普通点击（非refresh）重新发HTTP，计数5→6 | `detail-active-before-cancel`、`detail-cancelled`、`cancelled-detail-reissues-http`；最终包 `final-detail-before-cancel`、`final-detail-cancelled`、`final-reissued-request-before-close` |
| 搜索2项→分页去重3项/下一页disabled；canonical1详情与合成远程渐变封面 | `final-normal-pagination-details` |
| 显式愿望单加入：3→4，重复加入仍4，Committed/refresh Success | `final-wishlist-deduplicated` |
| 请求Loading时关闭，PID21428 exit0 | `final-reissued-request-before-close` + `final-ui-exit.json`；16:02:43.885Z发出关闭、16:02:44.327Z退出，约0.44秒，仅本机观察非SLA |

共 **19份manifest / 19份UI tree / 19张JPEG**，requested==actual、tree/runtime、文件长度/hash全部通过，19个不同截图SHA-256；逐次观察真实候选窗口，并重新打开关键落盘截图检查损坏/失败保留/删除/替换。初批 `publish` 为11场景，WinUI MVID `f41b84a0-0a95-4a5c-8553-e0febc16a37f`；随后收紧cache并发读取与mutation入口可见性并追加4项回归，最终 `publish-final` 为8场景，MVID `4f81d976-2eb5-43be-8ec1-67ed3d5c6d87`，Infrastructure SHA-256 `10e05d04ebed9a3c9af2199450faaa075c57a304bff93212e929682e7011be82`。不能只凭一个assembly的MVID认定全部层相同，也不能把初批证据标成最终包采集。

UI计数器是合成handler的累计详情分支计数，初始值含旧合成库图片fallback请求；只用同进程、图片完成后的5→6增量证明重发，不把绝对初值当作纯用户点击数。精确EOF/CPU映射deadline、失败clear和不合作迟到回调由确定性生产路径/VM回归证明；真实桌面验证的是可取消延迟HTTP，不能声称通过点击精确命中EOF。首批退出未留exit code；exit0证据仅归最终进程。

旧 `.artifacts/native-phase4a/` 的26组保留历史功能用途，不作为M1–M3整改通过证据。旧Phase3A已作废截图仍作废，不修改其历史文件或假装补齐。新证据继续使用requested→fresh runtime assertion→capture→manifest，失败不保存成功manifest，不扩大截图数量或重做性能baseline。

**PASS FOR PHASE 4B（Phase4A.1整改自检）**。本轮六项Gate整改均有生产位置、回归和实际结果；建立checkpoint后暂停，不进入Phase4B。后续release/migration Gate仍需真实API/CDN/代理兼容、WebP/跳转fallback范围、长时间网络故障压力、干净机安装；既有UNC/generic reparse/10k unique images边界及正式库写入前workingDirectory降级门禁继续保留。没有正式credential迁移或版本envelope完成声明。

## WinUI Phase 4A：Online Data / API / Remote Assets（历史本机验收；后续 Gate 与整改见上）

起点 `9ae2199aba6aedbb8e8012ab38e4a3ec8eb1a1a2`，分支 `codex/winui-phase4`。仅在线数据基础，无 metadata matching、title-index识别、ranking/veto/review queue、game launch/playtime、Liquid Glass或正式数据迁移。旧Electron产品源码未修改，未升级NuGet/npm依赖。

### 实际旧实现对照与分层

| Electron源码行为（已读当前源码） | 原生对应 |
| --- | --- |
| `nextmoe.ts` 搜索返回hit，批量works补字段，按hit顺序展示；cursor前缀/上限校验 | `NextMoeCatalog` 两步共享budget；去重/恢复顺序/保留next cursor，卡片不冒充完整详情 |
| work ID为十进制string；localized标题、详情include、elected cover优先、explicit过滤 | `RemoteWorkDto` confined于Infrastructure，映射Domain `WorkMetadata`，处理missing/null/unknown/partial与大ID |
| 45秒overall deadline，最多2次重试，502/503/504与429 RATE_LIMITED，Retry-After | `CatalogTransport` 每operation共享CTS/剩余预算/重试计数；验证、401/403、JSON错误、取消不重试，网络连接错误也不盲重试 |
| 流式8MiB上限，拒绝HTTP重定向，404 ENTITY_MERGED有限跟随 | ResponseHeadersRead + bounded stream + strict UTF-8/JSON；最多3次作品合并跳转，visited防环 |
| settings safeStorage仅主进程持密钥，renderer只见状态 | 新隔离DPAPI存储，不迁移旧密钥；接口区分settings状态操作与Infrastructure secret读取 |
| library.add按metadata ID避免重复，online-only无安装，refresh保护个人编辑 | `OnlineLibraryClient`复用 `TransactionalLibrary` 和request token，不建第二套写盘路径 |
| Vue在线请求时效/分页、详情按需、remote图片显示 | `OnlineViewModel`独立search/detail generation；WinUI虚拟列表、明确Loading/Empty/Offline等状态，RemoteCoverSource扩展既有管线 |

依赖方向保持：Domain无网络DTO；Application声明 `IOnlineCatalog` / `ICredentialSettings` / `ICredentialProvider` / `IRemoteCoverRegistry`；Infrastructure实现HTTP、mapping、bounded cache和remote source；Platform.Windows实现DPAPI与原WIC；Presentation处理用户意图；WinUI页面只绑定与调用。`CatalogNetwork` / `ImageNetwork`持有并释放各自复用的HttpClient，普通请求不new client。

### 请求、认证与数据边界

- 正常endpoint固定为 `https://api.nextmoe.dev/v2/`，不向用户暴露调试endpoint。连接复用5分钟、连接timeout10秒，catalog每主机最多4连接，gzip/deflate/br解压；共享45秒包含搜索两步、merge与retry。响应大小在解压后读取期间控制8MiB，不先任意ReadAsString。Body/response/request按using释放；204/malformed/坏UTF-8是InvalidResponse。
- 分类包含NoCredential、InvalidCredential、Unauthorized、Forbidden、NotFound、RateLimited、Server、Offline、Timeout、InvalidResponse、ResponseTooLarge、InvalidRequest、RedirectBlocked、MergeLoop/Limit和CredentialStorage。Presentation将认证问题归Unauthorized状态但保留具体中文原因。HTTP408比旧版通用API错误更精确地映射Timeout，是记录在案的差异；differential只对这一分类做显式映射，不豁免请求序列或成功DTO差异。
- retry默认500/1000ms，Retry-After delta/date/小数秒取合理等待，不超剩余budget；配额429不重试。日志 `NetworkEvent`仅method、host、status、elapsed、retry count、随机request ID，无URL query、Authorization、cookie、response body。正常装配不另写网络日志；redaction测试覆盖攻击性错误内容与secret。
- `IsolatedCredentialStore`只访问带synthetic标记的明确目录，复用正式目录排除、reparse/目录pin检查、16KiB密文限制；`native-online-key.dpapi`以当前用户DPAPI加密后原子替换。managed原始字节和unmanaged buffer清零；临时使用的.NET string仍遵循GC生命周期，不宣称可完全清除所有内存副本。PasswordBox不显示保存值，保存立即清空输入。测试只用合成token；正式settings/safeStorage未读取。
- 详情TTL缓存32项/8MiB源JSON预算/5分钟；refresh绕过、credential变化清空，cache generation防旧完成污染新cache。搜索不缓存完整详情。分页最多2000项、重复ID去重、重复cursor停止；page2完成不能替换新page1。每次改词立刻取消并废弃旧generation；离开页/关闭取消非关键HTTP，不把用户取消显示为错误。
- 愿望单显式加入：planned、无安装，重复canonical ID返回原条目而不新增；沿用既有事务返回Committed语义（不据此推断必有数据变化）。refresh捕获token后联网，最终事务检查token并保留个人名称/字段；404或超时不删除资料。移除仅允许无安装条目，不访问游戏文件。已有原子写盘仍按Phase2/3关闭drain规则完成。

### 远程图片与缓存

只接受经API/已有书架登记的HTTPS DNS来源：443、无userinfo/fragment、URL≤8192、不接受IP literal/localhost/.local/.internal。实际ConnectCallback解析DNS并拒绝任何非公开地址，再连接已验证IP，避免第二次DNS解析；无自动redirect、cookie或auth，图片client禁用系统proxy。该策略允许API返回的公开CDN，而不是承诺所有公开域名都可信；其请求仍受下载/解码预算限制。

远程response≤20MiB/20秒，Content-Type只接受PNG/JPEG，复用WIC header、单帧、4000万像素、最大decode edge1600及复用控件source/request generation保护。**WebP/其他类型或CDN跳转当前回退占位**，未为在线功能放宽本地decoder。URL+source revision经SHA-256，decode bucket进入既有thumbnail key；不以URL/query明文命名缓存。个人cover为authoritative数据，优先于remote，不进入可丢原图缓存。

远程encoded cache单独64项/32MiB/10分钟；registry2048项，库来源按snapshot建索引并在需要时重新登记。原thumbnail内存64MiB/磁盘128MiB、并发2保持。API cache、原图cache、thumbnail和持久用户metadata各自独立。缓存损坏回退/重取，不删除愿望条目。

### 验证结果

| 项目 | 本轮实际结果 |
| --- | --- |
| 锁定restore；Debug/Release solution build | 成功，各0 warnings / 0 errors |
| 完整C# | Debug / Release各557 passed / 0 failed / 0 skipped；原472+新增85 |
| 新增online测试 | Transport44（含32 Electron在线differential与oracle freshness）、Presentation/事务集成13、RemoteCover28 |
| 历史关键回归 | 102 transaction differential、14 fault injection、13 filesystem/cover differential保留；TRX中TransactionDifferentialTests共103含freshness |
| Artifact pipeline | 24 passed / 0 failed / 0 skipped（增加online状态/count/details/id/cover/connection错配反例） |
| Electron relevant | 252 passed / 0 failed / 0 skipped，原183集合加NextMoe/settings相关测试 |
| Electron build /真实桌面import | 通过；37新增，重复0新增/37跳过，个人字段/重启/文件保留，renderer errors0 |
| Release publish | `native-phase4a/publish-checkpoint/win-x64`成功，实际启动并正常退出 |
| 真实API | **skipped**：没有安全可用的隔离服务凭据，不读取正式credential补测 |

Deterministic handler覆盖200/204/400/401/403/404/408/429/500/502/503/504、Retry-After、坏JSON/UTF-8、超大/慢stream、断线、HTTP redirect、merge chain/loop/limit、DTO部分字段/大ID、乱序分页、旧缓存完成、TTL/预算。远程图覆盖JPEG/PNG、坏图、错误type、超大body/尺寸、redirect、危险scheme/内部地址、取消、视觉复用/源变化、cache corruption。旧ImagePipeline测试提供正确PNG CRC的超像素预分配拒绝证据；不能只用坏CRC图片证明像素预算。

Oracle生成器 `native/scripts/generate-online-oracle.mjs` 实际import `electron/nextmoe.ts`，输出32个请求/映射/错误结果到 `native/fixtures/online-oracle.json`。fixture记录源SHA-256，测试验证没有漂移；不记录token，仅authorizationPresent。快速改词的可控测试强制旧响应忽略取消后迟到，真实UI则验证取消传播与最新查询稳定，两种证据不混淆。

### 真实WinUI smoke及artifact范围

隔离库 `.artifacts/native-phase4a/ui-profile`，初始3条synthetic，加入在线作品1后4条；无真实游戏/正式资料。诊断装配同时要求 `THE_ORDER_NATIVE_DIAGNOSTICS=1` 和 `THE_ORDER_NATIVE_ONLINE_FIXTURE=1`，生产transport/parser/VM/decoder均保留，只替换credential与HTTP handler。普通装配不会安装该handler。正常模式重启关闭ONLINE_FIXTURE，未保存密钥，书架/缓存封面仍完整可用；缺凭据连接测试在HTTP之前失败。

| 真实场景 | Artifact ID / 观察 |
| --- | --- |
| 初始离线书架 | `offline-startup`：3条，正常Grid |
| 在线搜索/分页 | `online-search-loaded`：2；`online-pagination-end`：3且下一页disabled（两页重叠ID去重） |
| 详情及封面 | `remote-details-cover`：canonical1、Loaded、有效渐变图，未自动入库 |
| 愿望单 | `wishlist-added`：Committed、4条；再次加入仍4条；`wishlist-restart`重启仍4条且缓存封面可见 |
| 空/认证/限流/离线 | `online-empty` / `online-unauthorized` / `online-rate-limited` / `online-offline`：独立state与中文提示，本地4条不变 |
| 坏图 | `invalid-cover-fallback`：work99详情Loaded，列表+详情占位，Errors2、Outstanding0；onlineHasCover代表有来源key，不代表decode成功 |
| 取消 | `cancel-search-active-final` → `search-cancelled`：Loading0→Idle0，进度终止无错误 |
| 改词 | `rapid-query-ab` / `rapid-query-ab-after-old-deadline`：A进行中输入B，AB加载2条；超过原等待窗口后hash仍AB；旧响应无权改状态 |
| 连接测试 | `connection-test-synthetic`：接口形状/鉴权均通过才Loaded |
| 活动请求关闭 | `close-during-network-active`：Loading；14:57:21.484Z发关闭，14:57:21.846Z exit0，约0.36秒，仅本机观察非SLA |
| 正常装配重启 | `normal-mode-offline-restart`：4条/缓存cover；`normal-mode-no-credential`：缺密钥明确提示；随后exit0 |
| 最终状态修复复测 | `checkpoint-startup` / `checkpoint-search-loaded` / `checkpoint-edited-query-idle` / `checkpoint-cancel-active` / `checkpoint-cancelled` / `checkpoint-close-active`：启动4条、搜索2条、改词Idle0、活动取消、活动关闭exit0 |

26组manifest、26张JPEG、26份UI tree，逐项核对expected→actual、数量/页面/build/状态、文件长度/SHA-256，26个不同截图hash。所有验收截图均实际观察为候选UI，正常/错误/坏图不同。`artifact-sanity.json`保存矩阵；`final-ui-exit.json`、`normal-restart-exit.json`、`checkpoint-ui-exit.json`保存退出证据。最终PID35364于15:09:36.385Z发起关闭、15:09:36.719Z退出，约0.33秒。

采集器自己取得fresh snapshot，错配直接失败，manifest最后写，不覆盖旧场景。现场一次重复加入预期NotCommitted而实际Committed、一次早期10秒查询在点击取消前已完成的预期Idle均被拒绝，没有保存成功manifest。`search-active-before-cancel`只证明早期Loading，**不证明取消**，由上述最终取消证据替代；不是降低Gate。

构建证据分三批：`publish` 为MVID `f1aec2ab-5989-4195-94c4-acd52ecccb4f`，完成独立在线状态/图片/加入愿望单；`publish-final` 为MVID `f0c3cc82-958e-4e2f-9bcb-66f778c83ee0`，仅SyntheticOnline诊断等待10→40秒（低于45秒deadline），五核心层相同，完成取消/改词/重启/关闭；随后只在Presentation修复“改词清空后回Idle”和“移除凭据清除旧连接成功/详情/封面状态”，增加2项测试，生成真正交付的 `publish-checkpoint` 并完成上述六项复测。最终Debug/Release build零警告错误，full tests各557全部重跑通过。

**WinUI MVID不是整个Solution指纹**：`publish-final` 与 `publish-checkpoint` 的WinUI MVID相同，但Presentation变化。最终Presentation SHA-256为 `1884A3CC532FB41D5D15F25E3F7F0E62BEAD87DE089CB7C6D0539538B1C6ED6A`。必须结合manifest的实际window.app发布路径、process ID和 `build-bundles.json` 的全部应用DLL哈希识别批次；不能宣称早期所有截图均来自最终二进制。最终修复未改变transport/DTO/credential/remote image/事务层，其他既有场景保持原有适用范围。移除凭据后的状态由生产Presentation集成测试直接调用验证，没有额外自动点击删除凭据。旧Phase3A无效证据继续作废，其他历史证据不提升覆盖范围。

### 复验入口与停点

从仓库根目录（不直接运行默认正式Electron入口）：

```powershell
node --import tsx native/scripts/generate-online-oracle.mjs
node --test native/scripts/capture-ui-evidence.test.mjs
./.artifacts/native-phase1/tools/dotnet/dotnet.exe build native/TheOrder.slnx -c Release --no-restore
./.artifacts/native-phase1/tools/dotnet/dotnet.exe test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-build
./.artifacts/native-phase1/tools/dotnet/dotnet.exe publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -o .artifacts/native-phase4a/publish-checkpoint/win-x64
```

运行UI前设置已创建的标记isolated `THE_ORDER_NATIVE_DATA_DIR`，合成在线验收才开启两项诊断开关；结束清除环境覆盖。发布目录整体保留，不替换正式程序。数据与证据在 `.artifacts/` ignored，仅维护源码、fixtures和两份原文档提交。

**Phase 4A 本机自检完成，建议独立Gate审查通过后再授权Phase4B；本轮checkpoint后暂停。** 未验证真实API/真实CDN与代理部署、WebP兼容、长期网络故障压力、干净机安装。没有重新跑10k图片baseline（本轮不改变其预算），UNC/generic reparse/10k unique images边界保留。`workingDirectory`降级写入门禁仍最迟Phase6并先于首次正式库写入；不能把候选与Electron指向同一可写正式profile。

## WinUI Phase 3B.1：Filesystem Boundary Remediation（整改自检完成）

起点 `e75663c42fff9b0eea194cbca7d2706cbae45412`，保持 `codex/winui-phase3`。Phase 3B 独立 Gate 判为 NO-GO；本节 supersede 下方历史阶段关于扫描范围、不可用历史 identity 和候选连续性已经可靠的判断。只修复三项 filesystem MAJOR、深度提示和过期导航；没有 Phase 4、在线 API、识别、启动、计时、SQLite、Liquid Glass 或正式迁移。

### 授权根与枚举边界

- `BoundedScanner` 首次取得的 root observation 始终不变：stored/display/final path、volume + 128-bit file ID、目录属性、reparse tag 和 lexical reparse components。出队、枚举后及结束前确认根仍是该观察；根不可用、替换或重定向使整次扫描 Failed，UI 不接受本次候选。
- 队列保存子目录 observation，出队必须仍与原 observation 一致。范围判断结合目录属性、相同卷、final path 组件边界、根当前 identity 和 reparse 链，不只靠字符串前缀。支持显式选择 root alias，但根下不跟随额外子级 reparse，即使指回 root 内。普通目录换成 junction/symlink、另一个普通目录或删除时跳过并提示。
- Windows adapter 枚举时对 lexical/final 两条祖先链持有短期 no-follow 句柄、限制 write/delete sharing；持有后重验 root/目录，再进入顶层枚举。句柄随枚举器退出、异常、取消释放。占用或无法安全枚举时跳过并提示，可重新检查；不是永久文件系统锁，也不承诺任意第三方 filter 的绝对 sandbox。
- 预算仍为深度6、目录5000、条目50000、候选2000。`ScanResult.Truncation=DepthLimit` / `IsPartial` 表示因深度未入队的普通子目录；保留已找到候选，零候选也说明深度限制。发现 exe 后不再递归该游戏目录的既有策略保留。

### 重复性、连续性与导航

- `DuplicateStatus` 区分 NotChecked、ConfirmedUnique、ConfirmedDuplicate、Indeterminate。候选 identity 不可用仍拒绝；历史安装无法比较时，同规范路径保守判重复，不同路径进入 `PreflightStatus.DuplicateIndeterminate`，携带 `Unresolved` 路径/状态。核对页解释原因，不能全选为可导入；即使手动勾选，事务仍拒绝。
- 这是比旧 Electron 更保守的 intentional strengthening：不同字符串不能证明不同目标。不可比较的历史关系可能阻止新关系写入，但记录不删除。本轮无人工强行放行。恢复历史路径/权限后使用“重新检查当前目录”重新判定，或到安装管理明确处理关系。
- `PreparedInstallation.ReviewedCandidate` 保留原核对 observation。预处理必须与它一致；事务处理及 final precommit 先重验原候选，再检查 prepared folder/exe/working directory、重复与范围。变化返回结构化 `ClientOutcome.CandidateStale`，要求重新扫描/核对。同卷 rename 即使保留 ID，原路径失效或 final path 改变也不能自动接受；99 stable + 1 stale 整批零写入。一次串行原子事务保留，最后重验至持久化之间仍有窄 TOCTOU 窗口。
- `InstallationNavigation` 为异步打开安装页设置 generation。其他导航及停止使旧意图失效；完成后只有当前意图能导航，连续请求最新者生效。既有 draft/token、mutation drain、已保存但刷新失败的语义保留。

### 验证与证据范围

新增 **32 项**：`FilesystemBoundaryTests` 27、`FilesPresentationTests` 新增5。前者在真实临时合成目录上运行 Windows identity/枚举器，注入确定性的变更时机；导入经过生产 `OfflineFilesClient` 和 `TransactionalLibrary`，计数 persistence 证明零写入/一次写入，不只是 helper 判定。部分不可用状态为受控 identity 故障注入，不冒充全部真实 OS 故障。

| Gate | 实际验证 |
| --- | --- |
| M1 | 排队目录换成库外 junction/symlink、库内 junction、普通新目录、删除或环路，不枚举新目标，库外 marker 不进入结果；根替换/重定向停止。Windows 枚举边界直接拒绝 observation 后替换；显式 root alias 正常扫描。 |
| M2 | 同路径判重复；AccessDenied/NotFound/IdentityUnavailable/OtherIOError 注入判 indeterminate；真实 junction 因目标 rename 断链、同 ID 新路径仍可用时判 indeterminate，恢复后 Retry 判 duplicate；无关 missing 不假装 unique，批次零写入并保留历史。 |
| M3 | 首次重验后、exe 检查前真实换目录/junction/删除/rename，CandidateStale 且零 persistence 调用；100项在 final precommit 换最后一项，整批零写入；正常 scan→import 一次提交。 |
| Depth | 真目录 no-deeper、deeper、另有有效候选三个分支；不进入更深目录，partial 原因与候选保留正确。FilesViewModel 显示 partial 提示，Retry 复用 root。 |
| Navigation | 生产 coordinator + FilesViewModel + WorkspaceViewModel 的受控异步集成回归：Library、Settings、仍当前、两次请求；旧结果不夺回页面。不是新的桌面点击/截图证据。 |

锁定 restore；Debug/Release build 各 **0 warnings / 0 errors**；full C# 各 **472 passed / 0 failed / 0 skipped**。保留102 differential、14 fault injection、13 filesystem/cover differential。Artifact **18/0/0**；相同 Electron relevant 集合 **183/0/0**；Electron build/desktop-import 通过（37新增、重复0/37、个人资料/重启保留、文件未删、renderer errors0）。Release publish 到 `.artifacts/native-phase3b1/publish/win-x64` 成功。

本轮 isolated filesystem smoke 是上述自动化 Win32 lab；没有使用真实游戏目录或正式资料。Navigation 为生产 Presentation 集成 smoke；没有新增 WinUI 桌面截图、重跑10k图片baseline，也不声称最终发布包重新通过整套桌面验收。旧38组Phase3B图保留历史用途，不证明本轮竞态整改。`.artifacts/native-phase3b1/` 保存 ignored 日志/TRX/发布文件/验证摘要。

图片架构、decoder、64MiB/128MiB cache与source transport未改，10k unique cover压力测试留到Phase7。UNC/非NTFS/ReFS/generic reparse没有新增真实fixture，仍为未验证/不支持边界；套件0 skipped不代表这些环境通过。

### 正式资料与降级 Gate

`workingDirectory` 是 native optional 字段，旧 Electron rewrite 会丢弃。**最迟 Phase 6，且先于首次允许 WinUI 写正式用户库**，必须建立独立 authoritative migrated store、版本/写入门禁或其他可执行机制，阻止旧 Electron 无提示回写丢字段；备份提醒或禁止同时打开不够。有损 backward export 必须明确损失。本轮不实施迁移。

五项整改与规定自动化验证自检通过：**PASS FOR PHASE 4（整改自检）**。建立 Phase3B.1 checkpoint 后暂停，不进入 Phase4；后续独立 Gate 应按上述证据层级审查。

## WinUI Phase 3B：文件系统、安装与图片（历史本机验收；后续 Gate 与整改见上）

用户在 Phase 3A.1 Gate 通过后明确授权本阶段。分支仍为 `codex/winui-phase3`，起点 `55069ac4fddd1491e3122fd4c69a24487a3b2d86`；以下旧阶段的“不进入 Phase 3B”是当时的停点，不覆盖此次新授权。只使用带 `.the-order-synthetic` 标记的隔离 profile，没有接管正式资料、读取真实凭据、联网匹配、执行游戏、计时、SQLite、Liquid Glass 或删除 Electron。最终 checkpoint 后暂停，不自动进入 Phase 4。

### 架构与兼容性

| 层 | 本阶段职责 |
| --- | --- |
| Domain | Installation 保留路径和结构化参数；新增可选 WorkingDirectory。文件 identity 不写入便携书架。 |
| Application | `FilesystemContracts`、有界 `BoundedScanner`、安装/封面事务 token、`OfflineFilesClient`、`ImagePipeline` 与来源/解码/缓存接口。 |
| Infrastructure/Legacy | `NativeLibraryCodec` 先执行既有 legacy 校验，再保留安装路径原始拼写；不改变旧差分 oracle 的路径归一化实现。 |
| Platform.Windows | 句柄 identity/preflight、顶层目录枚举、WinRT WIC PNG/JPEG 解码/归一化、隔离缩略图磁盘缓存。 |
| Presentation | `FilesViewModel` 管理扫描、安装草稿、受跟踪 mutation、取消/关闭收尾；`ImageRequestSlot` 管理复用容器的请求身份。 |
| WinUI | Windows App SDK 标准 picker、候选核对/确认、安装管理、CoverPresenter；View 不自行访问 Win32、文件或解码原图。 |

七项目 Solution 与既有事务内核保留，无新增 NuGet 包、无依赖升级。扫描、identity、图片 decode/normalize、磁盘缓存都在后台执行；UI 只接收进度、展示轻量 projection，并把已限制尺寸的 BGRA 上传至 WriteableBitmap。所有安装/个人封面写入仍经过完整校验、串行事务、原子持久化和已有 Committed/CommittedWithWarning/刷新失败语义。

`StoredPath`、`DisplayPath`、`FinalPath`、`FilesystemIdentity(volume serial + 128-bit file ID)` 分开。通过 `CreateFileW` / `GetFileInformationByHandleEx(FileIdInfo)` / `GetFinalPathNameByHandleW` 得到实际对象身份；不使用 lowercase 字符串冒充 identity。目录需读取属性和列举权限，逐级检查重解析 tag；只接受普通路径、junction/mount point、symlink，未知 tag 和取不到可靠 identity 时拒绝新关系写入。

Preflight 区分 Valid、NotFound、AccessDenied、FileInsteadOfDirectory、UnsupportedReparseCase、DuplicateExistingInstall、SameTargetViaAlias、BrokenLink、IdentityUnavailable、OtherIOError，提供中文提示。重复检查覆盖已有同游戏/其他游戏安装及同批候选；真实 identity 相同的别名为重复，已失效历史记录只对相同规范显示路径作保守重复判断。离线历史路径不会自动删除；可用别名、Missing、AccessDenied/Unknown 有独立状态。祖先/子目录重叠保守拒绝。

identity 只代表当前机器/卷上的观察：同卷 rename/move 通常保留 file ID，但原路径 snapshot 会失效；跨卷复制/move 产生新身份；删除重建、恢复、卷序号冲突或文件 ID 重用均不能视为永久身份。网络 share 还依赖服务器是否支持 FileIdInfo 和 final path；本机没有获授权 SMB fixture，未声称网络可靠性。不可用路径没有字符串降级写入通道。

PreparedInstallation 绑定原始值、目录/exe/工作目录 observation。事务生成 candidate 前及持久化前重新确认 identity、final path、范围和重复，拒绝检查后消失/替换的目标。没有对整个文件系统加事务锁，也不一直持有安装句柄：最后重验与写盘之间仍有窄 TOCTOU 窗口；未来真正使用路径时仍须重新验证。

### 扫描、导入与多安装

- BFS 显式队列；深度 **6**、目录 **5,000**、目录项 **50,000**、候选 **2,000**。目录项同时包括文件和子目录；队列本身也受目录预算约束。每目录/每项检查取消，进度至少每 50 ms 合并一次，读取提示最多 100 条。本阶段不读 metadata/说明文本，读取预算为 0。
- 沿用旧 utility exe 排除、补丁/备份和系统目录保护；子级 reparse 不递归，另用已访问目录 identity 防环。根目录发现 exe 即作为候选，不再递归其子目录。目录不可读/消失会跳过并明确告知部分未检查；全局失败、取消、真空候选分别展示。
- 选择目录 → 扫描 → 候选名称/路径/exe 线索/已有关系 → 手动选择 → 确认 → **一次事务**。一个 exe 自动选中，多个 exe 保留待用户选择；不做 metadata recognition。100 条批量导入测试实际只持久化一次。重复提交跳过已有关系，不增加安装；纯重复批次仍可更新导出时间/执行一次事务，不能拿整文件 hash 变化推断重复新增。
- 安装支持新增、编辑 folder/exe/working directory/args array、切换首选、移除关系；已有模型没有独立安装标签字段，本阶段没有虚构 rename 字段。exe 和工作目录必须在解析后的安装范围内；args 保留独立字符串、空格、引号与空参数，不拼 shell command。移除首选关系令 preferred 为 null，保留其余安装；永不删游戏文件。
- 安装编辑从 draft 经 preflight 提交，token 校验 epoch/game lifecycle/原安装集合及首选项，防乱序覆盖。提交后草稿退役；刷新失败仍表达“已经保存”，不重试 mutation。退出取消扫描/图片并等待已接受的 mutation；关闭过程中不抛出未处理 async void。

旧版 v1 JSON / Base64 JPEG 封面继续保留。`workingDirectory` 是 optional 扩展，空值不输出；原 Electron 不使用它，并会在重新保存时丢弃未知字段。回退测试前保留 native profile 副本；禁止两端同时打开同一 profile。原始路径拼写保留是相对 Electron canonicalization 的有意加强，legacy backup reader 的既有差分行为保持原样。更早限制队列预算、拒绝无 exe/伪造候选、真实 identity 去重、保守范围检查也是明确差异。

### 图片与缓存

- `IImageSourceProvider` 为未来 remote source 保留接口，当前只取书架中的 local personal JPEG，未下载远程图片。`CoverSources.Selected` 的本地/当前作品个人选择/默认封面及内容分级优先级已与 Electron 做纯行为对照，但远程 transport 尚未接入；没有本地图片的条目仍显示占位。
- 标准图片 picker 只选 PNG/JPEG，源文件最多 **20 MiB**，解码前检查宽高（每边至多 65,535）和总像素（至多 **4,000 万**），只接受单帧。请求目标最大边 1,600；卡片按实际尺寸×DPI向上取 64px 桶，详情最低目标 512。WIC 使用缩放 transform、EXIF orientation、sRGB；应用不先为整张原图分配 BGRA，但不宣称限制 WIC 内部所有临时分配。
- 个人封面归一化为白底 JPEG、去源 metadata，质量/尺寸有限次回退至旧格式 **128 KiB** 上限；唯一用户副本始终在 library.json。坏图/截断/不支持格式/超尺寸/缺失/锁定/取消不替换原封面；已存坏图显示占位，错误计数不记录图像内容。
- 默认 decode concurrency **2**（配置 clamp 1–8），LRU 原始像素缓存 **64 MiB**，按 BGRA 字节估算；派生磁盘缓存 **128 MiB**、最多枚举 4,097 个文件后拒绝继续写，按旧写入时间淘汰。key = 源内容 SHA-256 + 尺寸桶，像素文件有尺寸/长度/hash 校验；profile/cache 路径继续检查 reparse/hardlink。缓存删除不会改 personal cover。
- Load/Unloaded/SourceKey/尺寸变化推进请求 generation 并取消旧任务；完成时再核对 generation/key/loaded，A 的晚结果不能写入复用后的 B。退出统一取消并 drain pipeline。待处理任务没有独立全局队列上限，目前由虚拟化容器数量和取消收敛；后续调优应关注并发请求合并、重复坏图负缓存和 pending queue，不能把两个解码槽宣称为所有 task 的硬上限。

### Gate 与证据范围

实际完成锁定 restore，Debug/Release build（0 warnings/errors），两配置完整 C# **440/0/0**，保留原 102 differential / 14 fault injection。新增 61 项：FilesystemIdentity 11、OfflineFilesystem 17、FilesystemDifferential 13、ImagePipeline 10、FilesPresentation 10。FilesystemDifferential 的 13 项含 1 项源 hash 新鲜度、7 项封面、4 项扫描边界、1 项复合扫描/安装行为。`generate-filesystem-oracle.mjs` 调用现有 Electron 实现生成参考；重复生成 SHA-256 不变（`oracle-repeat.json`）。

Artifact tests **18/0/0**；Electron relevant **183/0/0**、build、desktop-import 均通过：37 新增、重复 0/37、个人字段/重启保留、文件未删、renderer errors 0。日志/TRX 在 `.artifacts/native-phase3b/`，不是提交的源码。正式 release 未替换。

| 项目 | 本机实际证据 |
| --- | --- |
| identity | trailing、dot/dotdot、slash/case、长路径、junction/断链、symlink、8.3、subst、hardlink 文件 ID、rename/替换、ACL拒绝均执行。`filesystem-capabilities.json` 明确 UNC 与未知 generic reparse fixture **skipped**，不是 passed；unknown-tag 拒绝只有代码检查。旧 lab 描述中的 pending 由此报告 supersede。 |
| scan | 实际普通目录、utility 排除、候选/重复；所有预算、取消正反回归。测试中的 unreadable/disappeared 和 symlink/junction loop 是受控 IScanFilesystem 故障/链接项注入；不冒充真实 OS 环路挂载试验。 |
| UI import | 标准 picker 选 synthetic root、扫描、取消、再次扫描、核对并一次导入 2 条；重复选 1 条返回新增 0/跳过 1，readback 比较 games 完全不变。 |
| UI installations | 第二安装、工作目录及含空格/引号参数写盘并重启；首选切换与参数编辑；临时移走仅自己创建的 synthetic folder 后显示“路径不可用，记录保留”，随后目录已恢复。移除关系/首选移除由事务测试验证，未另做桌面删除操作。 |
| UI image | 有效 PNG 变为个人 JPEG；坏 JPG 被拒绝且原图保留；发布版重启/磁盘缓存读取、Grid/List 正常；legacy-valid 但 WIC 不可解码的封面仅占位、Errors=2、书架 normal。 |
| close | 受控 scan-start gate 期间关闭，进程结束、library hash 不变；45 个图片请求在 source-read gate 挂起时关闭，发布进程 exit 0。此时 ActiveDecodes=0，证明请求取消/drain，不冒充 WIC 内部阻塞中断。accepted mutation 收尾继续由全量状态/事务测试覆盖。 |
| authorized ordinary folder | 用户明确指定 `D:\entertainment\PC`，仅只读扫描得到 36 个候选；没有导入，隔离库仍为 2 个 synthetic entries，没有访问正式 profile/凭据、执行 exe 或改真实游戏文件。 |

当前可信 UI 场景保存在 `.artifacts/native-phase3b/ui/`。每次先捕获新 runtime，Assert requested == actual，再写截图/UI tree，最后写 manifest；增加 import stage/root/candidate/duplicate/installation/draft/local cover/image metrics 和嵌套属性匹配，继续校验 build、timestamp、count、mode/theme/state/readOnly、viewport 与文件 SHA-256。实际曾请求 ActiveDecodes=2 而 runtime=0，采集正确拒绝；观察确认暂停位于 source-read 后，使用 Outstanding=45 / ActiveDecodes=0 精确记录，没有伪造 decode 活动。

- `debug-import-review` / `debug-import-committed` / `debug-authorized-readonly-scan`：较早 Debug 构建上的范围限定 smoke。
- `debug-multi-install`：**发现旧草稿区残留的诊断图，不能当作成功关闭草稿的证据**。由 `fixed-edit-draft-closed` / `fixed-multi-install-restart` supersede。修复使用 HasDraft bool 可见性，避免 XAML null 函数绑定短路。
- `fixed-preferred-changed`、`fixed-missing-install`、`fixed-personal-cover`、`fixed-invalid-cover-preserves-personal`、`fixed-import-{scan-held,cancelled}`、`fixed-duplicate-{review,import}`、`fixed-close-scan-before`。
- `release-personal-cover-{restart,list}`、`release-invalid-cover-placeholder`、`release-images-held-before-close`。
- `images-{1000,5000,10000}-{grid,list}-{initial,scroll}`、`images-10000-grid-repeat-initial`、`images-10000-{grid,list}-{return,light}`。

旧 Phase 3A 无效截图和 Phase 3A.1 有效证据的分类不变。新增 `artifact-index.json` / `artifact-sanity.json` 逐文件验证大小、hash、ui.txt/runtime 与场景，`image-baseline.json` 保存数值；生成产物仍被 Git 忽略。预提交 assembly informational version 指向 `55069ac...`，不是新 checkpoint SHA；必须结合实际 exe 路径、MVID、全部 TheOrder DLL hashes 和源码清单判定构建身份，不能只凭标题或 MVID。

最终为 **38 screenshots / 38 manifests / 38 unique screenshot hashes**，其中 `debug-multi-install` 是上述已 supersede 的 bug 诊断，不计成功功能证据。索引 SHA-256：`BDE721B240F8FB492F6BBFB9A0E8A7EB1901750D8967541698C2981646B7A503`。最后只修正 SettingsPage 的 Phase 3B 名称，并加强超大图片 fixture 的 IHDR CRC/尺寸拒绝断言；最终两配置 build 仍各 0 warnings/errors、full tests 各440/0/0。`build-source-manifest-ui.json` / `build-source-manifest-final.json` 记录源码和发布 DLL/XBF/PRI hashes，`build-equivalence-final.json` 确认五个核心分层程序集字节一致；WinUI MVID 从 `317efdf4-fba3-49dc-8f0d-973d8e1d7aab` 变为 `c923ac56-5f7e-47f4-8970-c27611d5d95d`。最终 `publish-final` 实际重启显示持久化封面/安装、正确阶段名称，`final-published-{persistence,settings-phase3b}` 对应新路径和 MVID，PID2932 正常关闭 exit0。早期 Debug 图不作为最终 build 的逐字节证明。

### Phase 3B image baseline（不是 SLA）

发布版，固定实际 viewport **1042 × 455.5 DIP**。四张 40×60 的原创可解码 JPEG 重复分配给 1k/5k/10k 条目，library 约 1.91/9.59/19.19 MB；不是 10,000 张大原图的解码压力测试。另有实际 600×900 PNG 个人封面与目标尺寸/缓存淘汰/超尺寸单测。没有 FPS、峰值内存或长时间稳定性结论。

| 条目数 | Grid 首容器 ms | List 切换首容器 ms | Grid 首屏→滚动 | List 首屏→滚动 | 首屏 working set Grid/List MiB |
| --- | ---: | ---: | --- | --- | --- |
| 1,000 | 848.4 | 12.2 | 45→51 | 28→30 | 295.4 / 342.7 |
| 5,000 | 2359.1 | 11.2 | 45→46 | 28→30 | 373.9 / 427.1 |
| 10,000 | 3090.0 | 11.5 | 45→46 | 28→30 | 451.7 / 485.1 |

10k 首次首屏后用户手动关闭窗口，工具随后的滚动返回 foreground PID 错误；确认用户关闭后才重新启动。Grid 滚动、回滚、List 来自第二次进程（已有磁盘缓存），Grid 首容器 **3014.2 ms / 462.8 MiB**；没有将两进程说成同一次测量。1k/5k 均为同进程切换。所有快速滚动后的 offset 为 6825 DIP；10k 两视图回滚到 0 并再次检查对应条目颜色，没有可见 stale overwrite、几何跳动或永久加载，稳定后 Outstanding=0 / Errors=0。

| 条目/视图（滚动后累计） | requests | 实际 decode loads | memory hits / misses | memory hit ratio | max concurrent decode |
| --- | ---: | ---: | --- | ---: | ---: |
| 1k Grid | 166 | 6 | 160 / 6 | 96.4% | 2 |
| 1k List | 242 | 10 | 232 / 10 | 95.9% | 2 |
| 5k Grid | 151 | 4 | 146 / 5 | 96.7% | 2 |
| 5k List | 235 | 8 | 226 / 9 | 96.2% | 2 |
| 10k Grid（复测） | 156 | 0 | 151 / 5 | 96.8% | 0 |
| 10k List（复测） | 360 | 4 | 351 / 9 | 97.5% | 2 |

miss 是 memory miss，可能命中 disk，所以不等于 decode loads；首次 10k Grid decode=4、最高并发2，复测命中 disk 无需 decode。重复图片本身使命中率很高，不外推真实库。容器数量没有随 1k→10k 线性增长，但整库 JSON/Base64 加载和缓存索引仍有规模成本。Phase 3A.1 无图片的 1k/5k/10k Grid 为 1038.6/1670.1/2283.0 ms，working set 281.9/314.7/329.7 MiB；本次 5k/10k 时间和内存上升，不能宣称整体性能优于无图基线。旧 viewport 高度466.5，亦非完全相同条件。

### 复验入口与后续边界

```powershell
# 从仓库根目录；只向 ignored artifacts 写构建结果。
& native/scripts/native.ps1 -Action Restore
& native/scripts/native.ps1 -Action Build -Configuration Debug
& native/scripts/native.ps1 -Action Build -Configuration Release
& native/scripts/native.ps1 -Action Test -Configuration Debug
& native/scripts/native.ps1 -Action Test -Configuration Release
node --test native/scripts/capture-ui-evidence.test.mjs
node native/scripts/generate-filesystem-oracle.mjs
node native/scripts/generate-offline-library.mjs --out D:\Projects\The-Order\.artifacts\new-image-profile --count 1000 --cover-json D:\Projects\The-Order\.artifacts\native-phase3b\image-fixtures\cover-pool.json
```

最后一个命令要求输出目录尚不存在，cover-pool 是本轮测试生成的合成 JPEG，不是用户图片。`native.ps1` 的默认日志/Publish 仍沿用 native-phase1 路径，不能误称它会自动更新本轮发布目录。Phase 3B 实际发布通过 pinned dotnet `publish TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -o ../.artifacts/native-phase3b/publish-final/win-x64`；运行时必须设置 `THE_ORDER_NATIVE_DATA_DIR` 为标记目录，交互验收才开启 `THE_ORDER_NATIVE_DIAGNOSTICS=1`。`THE_ORDER_NATIVE_HOLD_IMAGES=1` 与“延迟扫描”仅诊断装配生效，退出/取消可解除等待。

剩余边界：UNC/非 NTFS/ReFS/generic filter 真实环境、真实大图片与大型 session 库、长期内存、干净机安装/签名/升级、突然断电未验收；没有正式资料迁移、在线图片下载/NextMoe、完整识别、游戏启动/计时或高级材质。Phase 4 应先独立定义范围和风险 Gate，不能把本阶段 checkpoint 当成正式数据/API/启动授权。

**本机 Phase 3B 验收完成，可提交独立 Gate Review。** 本地/可创建 alias、隔离事务与图片、必需 UI smoke、回归和发布已通过；UNC/generic reparse 的实际环境验证仍明确待补。建议先 review 本 checkpoint 再授权下一阶段，不把未测场景或未来功能写成 PASS。

## WinUI Phase 3A.1：状态整改与场景证据（验收完成）

本轮只在 `codex/winui-phase3` 修复 Phase 3A Gate 问题。基线 `29e5577c0e82384b66ce9d12fd6b06b996dc8b9c`，完成全部验证后才建立新 checkpoint；不进入 Phase 3B。

### 状态契约

- **M1**：`WorkspaceViewModel` 在详情请求开始捕获草稿修订号；SetDraft 与用户编辑均推进修订号。完成时同时核对请求 generation、取消、修订号、同条目 epoch/generation。Cancel 后仍允许继续输入，但旧结果不能覆盖新输入。离开 Details 释放已处理草稿；保存开始使旧详情请求失效。
- **M2**：偏好有 Uninitialized/Loading/Ready/LoadFailed。Loading 时只记录用户真正更改的 Theme/Layout/Sort 字段；读取后与完整旧偏好合并，再排队持久化。常用筛选在 Ready 前禁用。读取失败保留旧文件，后续主动外观/排序操作仅本次有效，本次运行永不重写偏好；修复文件后须重启。
- **M3**：`PersistenceResult` 是事务结果，`LastRefresh` 是刷新结果。已提交草稿退役，重新获取权威 projection；刷新失败仍首先说明已保存，并保留 commit warning。查询 helper 返回 structured outcome，不擅自覆盖提交提示，不重试 mutation。
- **M4**：`ApplyBatchAsync` 在后台解析前进入 tracked operation / IsSaving。解析、校验、Application 调用与结果处理属于同一个 task；关闭等待此 task。主动取消在 admission 前检查，durable 阶段仍遵守原事务契约。异常统一处理，View 入口另有最后一层保护。
- **Search**：Reload 取消并接管旧搜索，立即清理旧 busy 状态。打开新库前的新查询只更新意图；新库打开后的新查询可以取代 Reload 查询。旧结果和旧错误不能修改当前结果或 busy 状态。
- **Selection**：选择使用明确清空规则，不依赖控件保留对象引用。Grid/List、Sort/Filter/Search、Reload、任何 projection 替换及重新进入 Library 都清空；即使 ID 仍存在也不恢复。被隐藏后重现、删除、同 ID 新 lifecycle 恢复同样不继承旧选择/草稿。当前没有 delete UI；事务 epoch/generation 校验保持不变。

### 新证据采集

`THE_ORDER_NATIVE_DIAGNOSTICS=1` 显示合成验收控制和 `RuntimeEvidence`。只有此开关开启时才装配 WinUI 内部 `SyntheticScenario`：可以受控延迟详情、后台批量预处理、搜索，触发下一次重读或提交后查询故障。`THE_ORDER_NATIVE_HOLD_PREFERENCES=1` 可暂停首次偏好读取，界面“放行”解除；只在 diagnostics 模式生效。底层仍是原 `IsolatedLibraryFactory` / `IsolatedUiPreferencesStore` 和真实事务，不改库格式、隔离校验或只读能力。普通启动没有这些控制和 timer。

“延迟批量”必须在确认批量之前设置：现在暂停于后台 `IClientTextRules.SplitLabels` 入口，而非解析之后的 `BatchAsync`。诊断门只阻塞 Task.Run 工作线程，拒绝在 UI 线程等待，并有两分钟超时；放行后仍调用原文本规则。取消不会中断同步解析器，解析返回后、事务准入前观察取消。搜索门使用可取消异步等待。首次采集的 `m4-batch-held-*` 仅证明旧门处的事务准入等待；精确预处理证据为 `m4-preprocessing-*` / `m4-close-awaits-preprocessing`。

运行时记录来自实际 ViewModel、ActualTheme、Grid/List panel、进程与 assembly：itemCount/totalCount、viewMode、theme、readOnly、state、page、viewport/scrollOffset、realized/maxRealized、firstContainerMs、内存、事务/刷新结果、MVID 和 assembly informational version。首容器计时是 LibraryPage 构造到首 Grid 容器，List 为切换模式到首 List 容器；不是完整冷启动 SLA。

Computer Use 中先观察和执行单步 UI 操作，再调用仓库模块：

```javascript
const capture = await import('file:///D:/Projects/The-Order/native/scripts/capture-ui-evidence.mjs');
const result = await capture.captureEvidence({
  sky, window: targetWindow,
  scenarioId: 'published-10000-grid-scroll',
  outputDirectory: 'D:/Projects/The-Order/.artifacts/native-phase3a1/ui',
  expected: { itemCount: 10000, totalCount: 10000, viewMode: 'grid', theme: 'Dark', readOnly: false, state: 'normal', page: 'Library' }
});
```

模块内部自行捕获新 snapshot，不引用 REPL 外部 `state`。先验证场景身份、时间有效性、可视 viewport，再创建目录；任何不符直接失败且不生成成功 manifest。同名目录拒绝覆盖。manifest 最后写入，包含实际 runtime metadata、期望状态、窗口、输出文件 SHA-256。截图 hash 相同本身不判错，场景身份必须一致。

### 验证状态

证据写入 `.artifacts/native-phase3a1/`。当前锁定依赖 restore 成功；最终 Debug/Release solution build 各 0 warnings / 0 errors，full C# 各 **379 passed / 0 failed / 0 skipped**。原 102 differential / 14 fault-injection 保留全部通过，新增 `PresentationRemediationTests.cs` 30 项。采集反例 `node --test native/scripts/capture-ui-evidence.test.mjs` **10/0/0**。Electron 七个相关文件 **130/0/0**，build 成功，desktop import 37 新增、重复 0 新增/37 跳过、renderer errors 0。最终 Release publish 输出 `.artifacts/native-phase3a1/publish-checkpoint/win-x64`，旧采集批次 publish / publish-final 保留用于追溯；没有替换正式程序。最终发布 PID 33764 正常退出 exit 0，见 `checkpoint-publish-exit.json`。

`debug-final.trx` / `release-final.trx` 与 `*-build-final.log` 是最终回归；Electron 与 artifact 日志沿用本轮已通过且对应源码未再改变的记录。普通产品路径没有同步等待或 ViewModel 文件 IO；诊断 `SyntheticScenario.TextRules` 与单测 `GatedText` 有明确受限的后台解析等待，用于确定性时序，不能当作生产等待方式。

| Gate 项 | 实际 UI 证据与结果 |
| --- | --- |
| M1 | `m1-cancel-details-held` 确认 Cancel 后 detailsLoading=true；`m1-new-draft-survives` 放行后笔记仍为新输入、dirty=true、detailsLoading=false。 |
| M2 | `debug-preferences-held` Loading 时改 Light/List/name；`debug-preferences-merged-light-list` Ready、savedFilterCount=1。`preference-merge-readback.json` 核对实际文件中原 keep 筛选及最新三项 override。 |
| M3 | `m3-committed-refresh-failed` 为 Committed + Failed，提示“修改已经保存…不要重复提交”，Save 禁用。`m3-readback.json` 主库为新笔记、备份仍为旧笔记，未自动重复提交。CommittedWithWarning 的查询/详情刷新故障由 C# 确定性测试覆盖，未在桌面注入真实写后警告。 |
| M4 | `m4-preprocessing-held` / `m4-close-awaits-preprocessing` 均为 saving=true、batch-preprocessing-held；关闭等待，放行后退出并实际写入标签，`m4-preprocessing-readback.json` 核验。发布版主动取消后库 SHA-256 不变，`m4-cancel-readback.json` 核验；没有未处理 async void 导致的崩溃。诊断暂停点改进不改变事务实现。 |
| Search | `search-active-held` 确认 searching/query-held 与实际进度条；重读故障后 `search-active-reload-failed-ended` 为 error/reload-failed，进度结束，界面明确尚未加载书架。先前 `search-filter-reload-failed` 只是查询条件存在，不用它证明搜索并发。 |
| Selection | List→Grid 的前后截图为 selectedCount 1→0；`selection-journal.jsonl` 逐对记录 Grid→List、Search、Filter、Reload 的 1→0，既有 ID 继续存在也不保留选择。 |
| Artifact pipeline | `pipeline-negative-runtime.json`：故意请求 10k Grid Light normal、实际为 0/10k List Dark zero-results，采集拒绝且不创建场景目录。`artifact-sanity.json` 全量核验 manifest 与实际 ui.txt metadata、文件大小与 SHA-256；`artifact-index.json` 保存逐场景摘要及 manifest hash。 |
| 大库/主题/状态 | 1k/5k/10k Grid/List 首屏及快速滚动、Light/Dark、Normal/Read-only/Corrupt/Error/Empty/Zero-results 均有独立真实截图，详见下表及场景索引。 |

### Phase 3A.1 baseline（本机合成数据）

相同实际 viewport 1042 × 466.5 DIP；快速滚动后的 offset 均为 6989 DIP，截图中条目已改变。Grid 为 LibraryPage 构造到首次容器事件，List 为模式切换到首次容器事件；working set 是首屏采集时的进程值，包含诊断/桌面验收开销，不能解释成峰值或生产 SLA。基础批次每规模一次，10k 在补强诊断后的发布版重复一次。

| 实际库规模 | Grid 首容器 ms | List 首容器 ms | Grid 容器首屏→滚动 | List 容器首屏→滚动 | Grid/List 采集 working set MiB |
| --- | ---: | ---: | --- | --- | --- |
| 1,000 | 1038.6 | 19.3 | 45→51 | 29→31 | 281.9 / 299.1 |
| 5,000 | 1670.1 | 12.3 | 45→51 | 29→31 | 314.7 / 329.5 |
| 10,000 | 2283.0 | 18.0 | 45→51 | 29→31 | 329.7 / 377.8 |
| 10,000 复测 | 2282.6 | 11.8 | 45→51 | 29→31 | 331.7 / 376.6 |

两个视图均实际接受快速滚动并显示新条目，容器没有随 1k→10k 近线性增长；没有进行 FPS、帧时间、图片解码或长期内存 benchmark。封面仍为占位，本阶段不宣称真实图片系统性能。

场景目录（每项含 screenshot、ui.txt、manifest.json）：

- `release-{1000,5000,10000}-{grid,list}-{initial,scrolled}`：12 个基础规模/滚动场景。
- `final-release-10000-{grid,list}-{initial,scrolled}`：4 个 10k 复测场景。
- `release-readonly`（2 条、200k sessions、编辑禁用）、`release-corrupt-error`（错误而非空库）、`release-empty`、`release-10000-zero-results`。
- `debug-preferences-held`、`debug-preferences-merged-light-list`：Dark/Light 及启动偏好。
- `m1-*`、`m3-*`、`m4-*`、`search-*`、`selection-*`、`final-release-smoke-reopened`：状态与重启证据；完整 ID、规模、状态、容器数与 SHA-256 以 `artifact-index.json` 为准。
- `checkpoint-preprocessing-held`、`checkpoint-batch-cancelled`：最后取消文案修正的实际发布版复验；`checkpoint-cancel-readback.json` 确认文件未变。此前 `m4-preprocessing-cancelled` 截图保留为发现误导文案的历史证据，最终文案由此场景 supersede。

最终 sanity 为 **39 screenshots / 39 manifests / 39 unique screenshot hashes**，全部 manifest 与 ui.txt runtime 一致、文件大小及 SHA-256 一致，规模/主题/状态覆盖完整。`artifact-index.json` SHA-256：`3E91DBF0853F1A17C522591C70E681EBBBE25AA571F48CBD77E5C87448ABEBE9`。最终源码/程序集清单 SHA-256：`1FC45A10480B1775027BE74B53174284A88D3C38497F4E25ED555D8E26D97536`。

### 构建与证据可追溯性

场景 manifest 自报 assembly MVID、进程、时间、基线 informational version；未提交阶段的 informational version 仍指向 `29e5577...`，不能把它误称成最终提交 SHA。`build-source-manifest.json`、`build-source-manifest-diagnostic.json`、`build-source-manifest-final.json` 分别记录各批次的完整 native 源码哈希；最后的 checkpoint 与最终清单绑定。

MVID 只标识 WinUI 模块，不唯一标识全部依赖：最后只修改 Presentation 文案，WinUI MVID 仍为 `87a7e110-842f-4126-b958-4871597f8cf7`。因此还核对 manifest 的实际 executable 路径/PID、对应发布目录中各 TheOrder DLL 的 SHA-256，以及源文件 SHA-256 / Git blob；不能只凭 MVID 相同断言完整构建相同。

基础 Release MVID 为 `6e9c983a-1826-40ab-9011-2aa85f744ccb`；补强诊断后的 Release 为 `87a7e110-842f-4126-b958-4871597f8cf7`。中间只改变四个诊断相关文件，五个核心分层 DLL 字节一致，证据 `build-equivalence-diagnostic.json`。最后一次真实取消发现共用文案错误，按 detailId 区分批量/详情提示，并在既有取消测试中增加断言；最终构建清单另行记录该差异，不把先前 DLL 等价性扩大到最后文案修正。该修正不改变布局、搜索、选择、解析、事务或持久化，最终发布版再次实际确认取消文案和文件不变。

旧 `.artifacts/native-phase3a/` 的 31 PNG/31 txt 不作为有效 UI 证据，标记文件继续保留；本轮新 index/manifest 明确 supersede 旧截图与旧首容器表，不删除历史。旧 automated build/TRX/Electron 记录没有因截图错误而被作废。

重启 Codex 后原 `foreground window did not report a process id` 故障消失。验收中单次输入几何信息缺失和一次 monitor lookup 失败，均停止使用旧索引、重新选择窗口后仅重试一次即恢复；没有据此改产品业务逻辑。此次额外改动只包含精确合成验收门和实测暴露的批量取消提示。

**PASS FOR PHASE 3B**：本阶段要求的 M1–M4、Search lifecycle、Selection、artifact pipeline、大库、主题、只读/损坏/空/零结果均有对应证据。Phase 3A.1 checkpoint 后暂停，不将 Gate 结论当成 Phase 3B 授权。剩余边界：CommittedWithWarning + RefreshFailed 仅确定性回归；合成资料/单机诊断 baseline 不代表真实图片、大 session 库的长期运行、任意机器、干净机器安装/签名/升级或断电保证；不涉及正式数据、API、filesystem identity、游戏启动或 Liquid Glass。

## WinUI Phase 3A：隔离离线客户端与验收

当前候选分支 `codex/winui-phase3`，起点为 Phase 2.1 的 `d0702cafe1b6762069d63fe08bcba07b199ca713`。仅 Phase 3A；下面各阶段描述保留为历史证据。Phase 3A checkpoint 后暂停等待 Gate，不能自动进入 Phase 3B。

### 架构与写入边界

| 层 / 位置 | 职责 |
| --- | --- |
| WinUI/Views、MainWindow、Resources/DesignTokens.xaml | 原生三页面、导航/确认框、虚拟化容器、主题资源；无数据访问实现 |
| Presentation/WorkspaceViewModel、PersonalDraft | 草稿、dirty/保存/导航状态、最新请求保护、UI continuation；仅依赖 Application |
| Application/OfflineLibraryClient、OfflineContracts | 统一 capability/result、后台搜索索引、轻量 summary 和按需详情、保存结果解释 |
| Application/PersonalEditing | 既有 transaction kernel 的有限扩展：个人字段 token、最多 1000 ID 的一次批量 mutation |
| Infrastructure/Legacy/LegacyClientTextRules | 使用已验证的 legacy Unicode lower/空白规则；查询/分类标签差分支持 |
| Platform.Windows/IsolatedLibraryFactory、IsolatedUiPreferencesStore | 后台打开已隔离 profile；界面偏好独立存储、目录固定、大小/链接检查、临时文件替换 |

书架仍为 legacy v1 `library.json` / `library.json.bak`，原始大小、schema、完整校验、writer lease、串行事务、原子提交和读取恢复语义不变。主文件已经提交的 warning 不能报告为失败、不能自动重试。个人编辑 token 检查 epoch、条目 generation 和原个人字段，允许无关条目变化，拒绝删除/恢复身份和同条目个人字段过期。批量标签先规范化，再按旧规则移除和追加；不循环执行 N 次整库保存。

`native-ui.json` 是独立的 version 1 偏好 envelope，仅包含 Theme/Layout/Sort/SavedFilters。临时搜索和筛选条件只在内存里，只有“保存筛选”会创建命名项。不得读取真实 Electron settings/credentials/localStorage；localStorage 迁移后置。损坏偏好文件保留，本次外观可用但不覆盖它。偏好队列按顺序写入，关闭等待已经接受的偏好和事务。读取无效库不能当作空库。当前 writer 只支持带 `.the-order-synthetic` 标记并通过原隔离检查的目录。

### 生成与运行

从仓库根目录执行；使用既有锁定 .NET 10.0.401 / Windows App SDK 2.5.1，未增加 NuGet/npm 版本。生成器只接受**尚不存在**的绝对目录，不覆盖已有合成资料。输出不提交。

```powershell
./native/scripts/native.ps1 -Action Restore
./native/scripts/native.ps1 -Action Build -Configuration Debug
./native/scripts/native.ps1 -Action Build -Configuration Release
./native/scripts/native.ps1 -Action Test -Configuration Debug
./native/scripts/native.ps1 -Action Test -Configuration Release

$taskPhase3 = Join-Path (Get-Location) ('.artifacts/phase3-smoke-' + [guid]::NewGuid().ToString('N'))
node native/scripts/generate-offline-library.mjs --out $taskPhase3 --count 10000
$env:THE_ORDER_NATIVE_DIAGNOSTICS = '1'
./native/scripts/native.ps1 -Action Run -Configuration Release -DataDirectory $taskPhase3
Remove-Item Env:THE_ORDER_NATIVE_DIAGNOSTICS
```

分别生成 1000/5000/10000；`--count 0` 为合法空库，`--corrupt` 为损坏输入，`--readonly` 为两条、合计 200k sessions 的合法但不可写资料。路径内的 `Z:\SyntheticOnly` 和 `example.invalid` 都是数据字符串，本阶段不访问它们。诊断开关仅显示 first-container/query 时间和面板容器统计；默认关闭，计数使用 weak table，不保留旧容器。

差分与 Electron 回归：

```powershell
node native/scripts/generate-offline-ui-oracle.mjs
./node_modules/.bin/tsx.cmd --test tests/library.test.ts tests/bulk-library.test.ts tests/metadata-concurrency.test.ts tests/title-matching.test.ts tests/library-cover.test.ts tests/library-local-cover.test.ts tests/library-workspace.test.ts
npm run build
Remove-Item Env:KGM_TEST_EXECUTABLE -ErrorAction SilentlyContinue
node tests/desktop-import.mjs
```

新 oracle 在锁定 Electron 中执行实际 `filterLibraryGames`、`splitLabels`、`LibraryStore.updateGames`，记录四个参考文件 hash；重复生成 SHA-256 相同。旧 transaction/codec oracle 未修改。C# 新测试在 OfflineClientTests / OfflineClientPerformanceTests，包含 51 个 UI differential、草稿、冲突/删除恢复、失败保留、迟到请求、统一只读、损坏偏好、保存/关闭等待、1000 ID 单次持久化、单线程 UI context 和大库查询。性能 collection 不与其他 collection 并行运行。

本轮发布使用独立目录，未替换正式程序：

```powershell
./.artifacts/native-phase1/tools/dotnet/dotnet.exe publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -o .artifacts/native-phase3a/publish/win-x64
```

这是 unpackaged/self-contained 候选目录，必须保留整个目录，不能仅复制 EXE。正常 `native.ps1 -Action Publish` 仍使用既有 `.artifacts/native-phase1/publish/win-x64`；其 `-Published` 也只指向该默认目录。本轮独立发布目录应显式设置 `THE_ORDER_NATIVE_DATA_DIR` 后运行其中 EXE，结束后清除本次环境覆盖。

### 实际结果与证据范围（2026-10-04）

- Locked restore、Debug/Release 全 solution build：0 warnings / 0 errors；两配置全量 C# **349 passed / 0 failed / 0 skipped**（原 270 + 新增 79），原 102 differential / 14 fault-injection 测试全部保留通过。Release publish 成功，发布目录实际启动与正常退出。
- Electron 七个相关单元文件 **130 passed / 0 failed / 0 skipped**；build + desktop import 通过：37 条新增，重复 0 新增 / 37 跳过，个人字段和重启一致，游戏文件保留，renderer errors 0。未宣称运行了整个 npm test 或全部 desktop suite。
- **Gate 更正：旧 UI 场景证据无效，原首屏和 realized-container 表撤回。** 31 张 PNG 和 31 份 UI 文本全部来自同一初始 1000 条 Dark Grid 快照，不能证明 List、Light、只读、损坏、空库、10k 或滚动场景。原始材料保留并由 `.artifacts/native-phase3a/INVALID-UI-EVIDENCE.txt` 明确标记，不用于新 Gate。
- 根因来自原采集代码：`saveState = async name => { ... state.accessibility ... state.screenshots ... }` 闭包捕获第一次 REPL cell 的 `state`；后续 cell 的 `var state = await ...` 是新绑定。两 cell 的最小复现输出 `current=second, captured=first`。不是依据 hash 猜测 UI 操作未生效。
- 原 build/TRX/Electron 日志及 Application 微基准可继续按各自范围查阅；旧进程内存文件没有完整场景身份，不作为新 UI baseline。

布局按 [Microsoft GridView/ListView 虚拟化指导](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview)：有限 viewport、保留默认 ItemsWrapGrid/ItemsStackPanel、无外层整页 ScrollViewer，inactive view 的 ItemsSource 为 null。窗口尺寸换算参考 [Microsoft 窗口管理](https://learn.microsoft.com/zh-cn/windows/apps/develop/ui/manage-app-windows)。

**Application 微基准**使用另一组更小的、无 metadata 的合成 entry，每项预热 1 次 + 测量 3 次；仅计后台查询、不计 120 ms 防抖、XAML 渲染或图片。它与真实 UI 表是不同指标。

| 条目 | 打开库 ms（单次） | 建索引 ms（单次） | 搜索 median ms | 评分排序 median ms | 组合筛选 median ms |
| --- | --- | --- | --- | --- | --- |
| 1,000 | 21.9 | 3.2 | 0.21 | 0.42 | 0.13 |
| 5,000 | 87.5 | 22.4 | 0.79 | 2.81 | 0.40 |
| 10,000 | 179.7 | 41.9 | 0.50 | 4.94 | 0.41 |

证据目录 `.artifacts/native-phase3a/`（不提交）：`restore.log`、`debug-build.log`、`release-build.log`、`debug-full.trx`、`release-full.trx`、`publish.log`、`electron-unit.log`、`electron-build.log`、`electron-import.log`、`oracle-repeat.json`；`ui-*.txt/png` 与 `published-*-{initial,scroll}.txt/png` 已作废，不支持对应场景；`published-memory-*.json` 只保留为缺少完整场景身份的历史进程记录；`ui-close-persistence.json`、`published-batch.json` 为合成文件回读。所有测试窗口已正常关闭。复验应从新生成目录开始，不把本次编辑过的 profile 当原始基准。

**Gate 剩余边界**：Phase 3A 尚待独立 review，不自行宣告 Phase 3B GO。长时间运行、全 Unicode/其他 ICU/NLS 版本比较、全屏幕尺寸与无障碍、真实用户库、断电/网络盘、干净机器安装/签名/升级未验收。图片仍是占位，数据中的本地 cover 未当作缓存删除；安装、文件系统 identity/preflight、扫描/导入、ImageService/ThumbnailService 都属于 Phase 3B。通过 Gate 前不实现，不运行真实游戏，不接入 API/凭据、匹配、SQLite 或 Liquid Glass。

## WinUI Phase 2.1：Gate 整改与验证边界

本轮从 `codex/winui-phase2` / `f495d06c6b3b6d8dc2b6125405490876b5d7fab1` 整改，不新增架构层或依赖，不允许正式 profile / 凭据，也没有 Phase 3 授权。以下规则替代旧 Phase 2 中相应的输入预处理和同步释放描述。

| Gate 项 | 生产位置 | 专项回归 |
| --- | --- | --- |
| B1 | Application/Transactions.cs 的 LibraryAccess、Open、Mutate；Infrastructure/LegacyLibraryCodec.cs 的 Inspect/Decode；LegacyLibraryReader.ParseCanonical | ValidLargeMainRemainsReadOnlyNeverFallsBackOrWrites（旧有效、损坏、无备份三种）；ValidWritableAndInvalidInputAreDistinct（两种） |
| B2 | TransactionalLibrary.Open 默认 Now：TimeProvider → UTC → CultureInfo.InvariantCulture；Legacy codec/validation 日期解析均显式 invariant | DefaultProductionTimestampIsUtcInvariantAcrossCalendars（5 种 culture，含 Buddhist / UmAlQura） |
| M1 | TransactionalLibrary.Enqueue / ShutdownAsync / DisposeAsync；IsolatedLibraryPersistence 内部 await；WinUI App.Closing / NativeHost.DisposeAsync | SingleThreadUiCanCloseWhileKernelDrains（7 种状态）；CancellationDuringCandidateThenShutdownDoesNotPersist；UiEntryPointsDoNotNormalizeOrDecodeOnCaller（3 个入口） |
| M2 | TransactionalLibrary.LinkCoreAsync 的 sessionIndices | EightyThousandSessionMergePreservesOrderTotalsAndLimits（6 种）；真实 Electron 新增 partial / reverse / equal-seconds whole-source / duplicate-input 差分 |
| 异步 token | CommitLinkAsync 必填 RequestToken；FinishRequestAsync / 取消登记和退休清理 | AsyncCommitRequiresTokenAndRejectsDeletedThenRestoredId；CancelledRequestEventuallyClearsPendingWithoutCancellingNewerRequest（3 种） |

**输入有效性与可写能力**：外部输入始终先检查 32 MiB，再做完整 schema 校验。只有 codec 自身生成的规范化 JSON 可走 internal ParseCanonical（完整校验仍保留），输出字节上限单独由 Encode 执行。InvalidInput 才属于损坏输入；ValidReadOnly 不读旧备份，构造不再 Encode，Snapshot.Access/ReadOnlyReason 可供未来 UI 展示。只读 mutation 在调用 operation 前拒绝，主/备份字节保留。旧 unknown-field 白名单没有为大小边界作特例，未删除/压缩 session 或改写用户内容来绕过限制。真实 Electron validateLibraryBackup 已验证同一形状的 200k sessions 紧凑输入合法；具体字节记录在 oracle 的 largeInputReference。

**线程与关闭协议**：UI 只发起操作并 await。admission lock 内同步登记 SemaphoreSlim.WaitAsync 保持接单顺序，随后 Task.Run 执行业务和 IO，不在 UI 线程创建 candidate、规范化 metadata 或解析导入。导入 ReadOnlyMemory 的调用方必须在任务结束前保持缓冲区内容稳定，工作线程先复制完整输入再解析/验证。不要把 cancellation 当作 stale token，也不要从 UI 同步等待 transaction。

| 关闭时状态 | 行为 |
| --- | --- |
| 空闲 | 异步释放资源；重复关闭幂等 |
| 已接收但排队 | 停止新增接单，已有工作继续按序；已取消的排队工作跳过 |
| 正在构造/校验 candidate | 允许完成验证，在进入持久化前最后检查取消；未持久化则无内存/磁盘提交 |
| 备份或主文件持久化中 | 不把关闭/取消传入 durable IO；完成最小安全提交或按原 IO 故障语义失败 |
| 主文件已替换 | 必须发布准备好的内存 snapshot；不能报告回滚 |
| 提交后检查警告 | 返回 committed-with-warning，再完成 drain/release |

生产窗口在 AppWindow.Closing 中 Cancel=true，await provider.DisposeAsync 后通过 DispatcherQueue 发起真正关闭，保持关闭等待期间 dispatcher 存活。容器尚未注册 writer；“保存中关闭”证据来自实际生产内核/文件适配器置于真实单线程 SynchronizationContext 的测试，不等于已迁移完整 WinUI 保存命令。同步 Dispose 仅为无事务的旧调用保留，忙时抛明确错误，不能用来等待。

**异步结果**：即时明确点击采用 LinkAsync；远程响应/后台识别/延迟结果采用 CommitLinkAsync（无 token 默认值），token 携带 epoch/generation/request/revision。BeginRequestAsync 可用请求 cancellation 安排 Pending 清理；显式 FinishRequestAsync 的 cleanup 不受已取消 token 阻止。CommitLinkAsync 在完成、失败、取消后清理同一 token，较新请求不会被旧清理取消。关闭中的剩余登记在排空后清除。

**性能方法**：WarmedMultiRunOrdinaryTransactionAndMergeGrowth 独占测试 collection，10k 普通条目及 20k/40k/80k 每侧 session 合并，各 1 轮预热 + 3 轮测量。计时覆盖发起事务、候选、完整验证、备份/主文件 IO 和提交，不包括打开库。报告 median/min/max；三个样本不足以宣称稳定 P95。仅检查字典循环结构并观察增长，不设置机器相关绝对毫秒门槛；不借此优化普通整库事务。

复验使用下方历史 Phase 2 的同一组命令。当前日志和两配置 TRX 保存到 `.artifacts/native-phase2.1/`（不提交）；测试工具仍默认覆盖 `.artifacts/native-phase1/test-results/compatibility.trx`，每次结束后复制为本阶段 `debug-tests.trx` / `release-tests.trx`。不升级 NuGet/npm 依赖，也不替换正式程序。

### Phase 2.1 实际结果（2026-10-04）

- Locked restore 成功；Debug / Release 全 solution build 均 **0 warnings / 0 errors**。完整 C# suite 两配置均 **270 passed / 0 failed / 0 skipped**，较 Phase 2 的 234 新增 **36**：32 个专项实例 + 4 个真实 Electron 小型差分。
- 原 98 个 differential 全保留并通过，现共 **102**（54 mutation + 5 lifecycle + 43 codec）；原 **14** 个 FaultInjection 标记用例也全部通过。Electron oracle 重新生成且哈希与测试时 fixture 一致：54 mutation（41 接受 / 13 预期拒绝）全部验证旧端重启，另有 5 lifecycle / 5 recovery / 43 codec 参考。大主库参考为 **20,978,558 bytes compact / 34,178,884 bytes pretty**，Electron 校验合法；C# 三种备份组合均加载当前 2 条/200k sessions，拒绝修改且主/备份逐字节不变。
- 默认时间实测 en-US / zh-CN / ja-JP（Gregorian）、th-TH（ThaiBuddhist）、ar-SA（UmAlQura）均为 `2026-10-04T00:00:00.000Z`。单线程 UI context 专项同时断言 codec/probe 在非 UI 线程、SynchronizationContext=null；覆盖空闲、排队、candidate、取消、持久化、已替换 warning，关闭后 writer lease 释放、重开成功、disk/snapshot 一致。
- Electron 6 个相关单元文件 **120/0/0**；Electron build 与真实 desktop-import 通过：37 新增、重复 0 新增/37 跳过、个人字段/重启一致、游戏文件保留、renderer errors 0。原生 Debug/Release EXE 均实际启动至窗口，通过正常窗口关闭消息退出（exit 0），合成资料 SHA-256 未变。此 smoke 只验证启动/关闭，不代表完整 UI 功能或发布包验收。

| 完整事务（每项 warmup 1 + measured 3） | Debug median [min, max] ms | Release median [min, max] ms |
| --- | --- | --- |
| 10k 普通条目修改 | 228.8 [224.9, 236.2] | 182.1 [173.0, 183.6] |
| 20k + 20k 逆序完全重叠合并 | 200.4 [198.2, 228.8] | 160.1 [149.8, 211.1] |
| 40k + 40k 逆序完全重叠合并 | 391.3 [378.2, 412.6] | 294.3 [290.1, 332.4] |
| 80k + 80k 逆序完全重叠合并 | 717.0 [704.3, 748.9] | 604.7 [562.4, 620.5] |

本机完整事务增长与线性 session 索引一致；这些是合成、无并行测试干扰的有限样本，不能当作所有机器/真实库的 SLA，也不宜直接与历史单次并行套件数字比较。80k+80k 不相交的 160k 候选按原 100k 上限整笔拒绝；部分重叠合并到 100k 成功，逆序、重复 ID、startedAt 冲突和历史累计保持契约。大库整库解析/重建仍有 CPU 与内存成本，ValidReadOnly 不承诺可编辑。

证据：`restore.log`、`debug-build.log`、`release-build.log`、`debug-test.log`、`release-test.log`、两配置 TRX、`oracle.log`、`electron-unit.log`、`electron-build.log`、`electron-import.json`、`native-close.json`，均在 `.artifacts/native-phase2.1/`。B1/B2/M1/M2 自检有生产位置、专项回归和实际通过结果，结论 **PASS FOR PHASE 3（仅表示可交接下一阶段；没有实施授权）**。仍未验证正式用户资料、真实凭据、断电/非 NTFS/网络盘、完整 WinUI 写入集成和干净机器发布；本轮没有 SQLite、完整 UI 功能或用户迁移。checkpoint 后暂停。

## WinUI Phase 2：事务内核约束与复验（历史 checkpoint）

Phase 2 在 `codex/winui-phase2`，基于 Phase 1.1 checkpoint `d988d2b`。它没有将 WinUI 页面连接到 writer。禁止把 `TransactionalLibrary` 的合成验证解释成真实用户迁移已经获准；正式 `%APPDATA%\KunGalManager`、真实 settings/密钥/游戏目录仍不可作为输入。Phase 3 必须另获用户明确指令。

### 实际旧版 oracle 与分层

- `electron/library.ts` 的真实 `mutate`：queue → structuredClone → operation → parseBackup(envelope) → stringify + 32 MiB 检查 → beforeCommit → atomicWrite(backup,lastGood) → atomicWrite(main,candidate) → 生命周期更新 → games/lastGood → clone(operation result)。没有数据库，也没有写后通知总线。
- 旧 atomicWrite 是同目录唯一 `wx` 临时文件，writeFile、sync、close、rename，finally 尝试删本次 temp。initialize 在主文件缺失/损坏且备份有效时会恢复主文件；主文件存在时还先复制 corrupt 文件；两者缺失会写空库；两者不可读拒绝启动。
- 新 Application 层只依赖 `ILibraryCodec` / `ILibraryPersistence`，不依赖 XAML、JSON、Windows API。Domain 仍是 immutable records；Legacy codec 和命名保护在 Infrastructure；文件句柄、隔离与原子替换在 Platform.Windows。`AddInstallationAsync` 是已授权安装**值**的提交入口，不是任意文件路径访问授权，未来目录选择/扫描必须另建受限 filesystem service。
- `native/scripts/generate-transaction-oracle.mjs` 在真实锁定 Electron 的 Node 模式调用上述原类；临时合成数据全部自行创建/清理，不传入 KGM_DATA_DIR 或真实 profile。固定时钟和 UUID 是测试依赖控制；C# add 的 ID 工厂注入 oracle 输出 ID，其余保留/跳过/合并 ID 均直接比较。真实生成的 GUID 唯一性由平台提供，不以两种实现碰巧生成同一个随机数为验收标准。
- JSON 比较检查属性存在性、精确 UTF-16 字符串（包括孤立 surrogate）、数字、数组顺序、操作结果、主/备份和重启结果；不依赖属性排列和空白。write codec 保持 legacy v1 字段，已知嵌套属性按旧白名单映射；未知属性首次显式写入时按旧版丢弃，没有扩展保存框架。不得声称这能无损降级未来客户端的新 schema。

### 事务和保存保证

1. 一个实例内所有写操作和请求意图注册共用串行队列，排队任务执行时才取最新 authoritative snapshot，不能在队列外先基于旧值构造“全库替换”。
2. 业务只改 immutable candidate；先验证完整候选（包括全局路径/ID、首选安装、时长下限、条数和字节上限），再做任何保存。输入备份在入队前完整解析并冻结；失败不能只导入半份。
3. `IsolatedLibraryPersistence` 只允许显式绝对目录+合成标记，复用既有正式目录及父子目录/重解析/硬链接拒绝规则。固定目录祖先句柄，保留 marker 句柄，并持有 `.the-order-writer.lock` 的独占句柄；没有自动创建父目录。锁文件是空的协调文件，打开后保留，不能当成用户数据。
4. 先把 canonical lastGood 保存到 `.bak`，再保存新主文件。每个目标使用本目录独占创建的 GUID temp，写入、`FileStream.Flush(true)`、关闭，然后调用 `MoveFileExW(REPLACE_EXISTING | WRITE_THROUGH)`。没有先删除/截断目标，也不使用跨卷 copy/delete。只清理本次 temp；遗留的其他 temp 不作为恢复来源，也不自动删除。
5. 主替换前任何失败：authoritative 内存仍是旧版，主文件未变，备份最多推进到 lastGood。主替换已成功后的检查异常：发布新 snapshot，返回 warning；不能抛“保存失败”却让磁盘保存了新版。备份替换后的检查 warning 也保留在返回结果中。
6. 这保证了本机测试覆盖的正常退出和可预期 I/O 失败一致性，不宣称所有文件系统或断电绝对原子。微软的 [MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw) 和 [Flush(true)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0) 文档不能被解释成目录项/硬件缓存的全面断电保证。未测试 SMB、云同步目录、不同 ACL 继承策略或硬件故障。Electron 不遵循 native writer lock，禁止双写。

### 恢复矩阵：选库一致，打开时不自愈

| 主文件 | 备份 | Native 打开结果 | 文件动作 |
| --- | --- | --- | --- |
| 有效（包括真空库） | 有效/损坏 | Main，选主库 | 不修改主/备份 |
| 损坏 | 有效 | Backup，明确报告恢复来源 | 不修改主/备份 |
| 缺失 | 有效 | Backup | 不创建主文件 |
| 损坏 | 损坏/缺失 | 错误 | 不当作空库 |
| 缺失 | 损坏 | 错误 | 不当作空库 |
| 缺失 | 缺失 | New，真正新库 | 显式 mutation 前不创建主/备份 |

这是明确偏离旧 initialize 自动写回的设计，以满足本阶段“读取不偷偷自愈”的要求。恢复后的第一次显式 mutation 会以已恢复的有效内容更新 backup，再替换主文件；当前不另外生成旧 Electron 的 `library.corrupt-*` 副本，所以需要保留损坏原件的诊断工作必须先在调用者授权范围内另行保存。没有 UI 自动保存恢复状态。

### 合并字段表（source 主动关联到 target 的作品 ID）

| 字段 | 与旧版一致的规则 |
| --- | --- |
| 游戏 ID / 加入时间 / 顺序 | 保留 source ID、addedAt；删除 target，其他条目相对顺序不变 |
| installations | source 后接 target，不悄悄去重；重复安装 ID 或全局目录导致全事务拒绝 |
| preferredInstallationId | source 非 null 优先，否则 target；完整校验必须指向现存安装 |
| categories / customTags | source 顺序后追加 target 未出现项；区分大小写 |
| name / nameIsCustom | 缺省来源标记视为可能个人名；显式版本/章节名、与原 metadata.title 不同的名称受保护；否则采用新资料标题；preserveName/preserveMergedName 分别遵循旧优先级 |
| status | 仅 source 为 planned 时采用 target |
| personalRating / favorite | source rating 非 null 优先，0 不能视为缺失；favorite 取 OR |
| notes | target 非空且不同才以两个换行追加；相同不重复 |
| sessions | source 顺序，追加新 ID；同 ID 不同 startedAt 拒绝；同开始时间保留 seconds 较大的完整 session，等长保留 source |
| totalPlaySeconds | 两侧累计之和减重叠 session 的较小 seconds；不从 session 重新计算历史累计 |
| playCount | 两侧累计之和减两侧 seconds 都大于 0 的重复 session 数 |
| lastPlayedAt | 取较新的非 null ISO 时间 |
| metadata | 使用本次规范化资料，只按明确相同 work ID 合并，名称不是身份 |
| cover | 删 source 不再对应 work ID 的资料封面；source 无个人封面时才继承 target localCover 或对应作品 coverChoice |
| cover display | source 缺省时才继承；source 显式 null 不等于缺省 |

### 取消、生命周期与尚未移植的边界

- cancellation 只决定是否继续排队/进入提交，token 决定结果是否仍有权限提交，两者不得混为一谈。进入备份持久化前最后检查取消；之后完成提交并用 `CancellationAfterCommit` 回报。主替换前真实 IO 失败仍是失败，即使同时收到取消。
- `BeginRequestAsync` 在同一队列中登记最新意图。token 带 epoch、generation、request、revision；旧结果、跨实例 token、rename/metadata/installations 改变前 token、delete→restore 同 ID 前 token 全部拒绝。个人 notes/rating/favorite 等不改变资料证据身份。每次 link 即使内容相同也消费原 generation。
- 合并目标的 pending request 或晚于 source token 的 revision 阻止合并；较新请求即使失败/finish，其意图 revision 仍有效。当前只迁移代表性的单一显式请求通道，**没有**完整移植 automatic-batch sibling exception、refresh/association/cover 多通道；后续迁移 matching 时必须以原 `metadata-concurrency.test.ts` 的相应场景补齐，不能宣称本内核已经覆盖完整识别协议。
- append/restore 保留旧 ID、顺序、重复 ID/work ID/路径跳过和个人数据；目录键为 legacy Windows lexical identity。本阶段不访问安装指向的文件系统，因此不等价于旧 importBackup 对真实 junction/目录别名的 `realpath` 解析。未来正式导入需要独立受限预检。新增 remove-installation 只删记录，移除首选时置 null，不自动猜选另一个安装，不删游戏文件。
- 命名保护仅移植旧 predicate 所需规则，没有引入完整匹配引擎。任意手工非标准 Date.parse/WHATWG URL 输入、完整图像解码/EXIF 和所有未来 Unicode normalization 边界未证明全等；当前固定导出数据与列出的 oracle 才是已验证范围。

### 实际复验命令与证据

```powershell
# 从仓库根目录运行。已装锁定 npm 依赖与 portable SDK，无需升级包。
node native/scripts/generate-transaction-oracle.mjs
& ./native/scripts/native.ps1 -Action Restore
& ./native/scripts/native.ps1 -Action Build -Configuration Debug
& ./native/scripts/native.ps1 -Action Build -Configuration Release
& ./native/scripts/native.ps1 -Action Test -Configuration Debug
& ./native/scripts/native.ps1 -Action Test -Configuration Release
& ./node_modules/.bin/tsx.cmd --test tests/library.test.ts tests/bulk-library.test.ts tests/metadata-concurrency.test.ts tests/title-matching.test.ts tests/library-cover.test.ts tests/library-local-cover.test.ts
Remove-Item Env:KGM_TEST_EXECUTABLE -ErrorAction SilentlyContinue
npm run build
node tests/desktop-import.mjs
```

`native.ps1 Test` 默认覆盖 `.artifacts/native-phase1/test-results/compatibility.trx`，每种配置完成后需复制到自己的证据目录；日志输出路径请使用绝对路径，避免脚本内部 Push-Location 导致相对路径变化。Phase 2 已保存 `.artifacts/native-phase2/{restore.log,debug-build.log,release-build.log,debug-tests.trx,release-tests.trx,electron-unit.log,electron-build.log,electron-import.json}`。上述 native 两配置各 234/0/0；比 Phase 1.1 新增 150，含 98 个差分、14 个标记故障注入测试；Electron 单元 120/0/0，desktop import 37 条与重启校验全部通过。生成 fixture 是可提交的虚构参考；TRX、bin/obj、SDK、临时凭据均不是维护源码。

性能测试有 10k 简化 entries 和单条 10k sessions 两种形状。前者 JSON 5,019,006 字节，本机 Debug/Release 完整套件内事务约 577/703 ms；后者 1,759,515 字节，约 299/334 ms。进程累计分配观测约 198/185 MB，受同进程并行测试影响，只用于量级观察。当前候选共享不可变记录，但验证会重建整个库，约三次序列化/两次 parse，成本随总 JSON/session 历史增长；32 MiB 是硬上限。这是后台事务和未来 SQLite 评估依据，不是大库 UI 性能验收，也没有以优化为理由削弱验证或迁入 SQLite。

## 项目边界

这是 The Order 的长期源码仓库。Windows 正式工作目录为 `D:\Projects\The-Order`；Claude Code、终端和编辑器都从这里开始工作。

维护 `src/`、`electron/`、`public/`、`data/`、`scripts/`、`tests/`、项目配置、锁文件及许可证。`node_modules/`、`dist/`、`dist-electron/`、`release/` 和 `.artifacts/` 均为本机生成内容，不提交 Git。

历史发布 ZIP、界面截图、验证报告、旧交付说明和候选源码保留在原工作区中，不作为本仓库的开发输入。

## 技术栈与资源

- Electron 桌面主进程与 preload：`electron/`，TypeScript，esbuild 生成 CommonJS 文件。
- Vue 3、KunUI、Tailwind CSS 与 Vite：`src/`，浏览器入口为 `index.html`。
- `package-lock.json` 固定依赖版本，使用 Node.js 24 与 npm 安装。
- `public/` 保存应用图标及浏览器预览资料，Vite 将其复制到 `dist/`。
- `data/title-index.json.gz` 是运行时所需的公开标题索引，连同授权说明一起版本管理。
- Python 仅用于手动重建标题索引，普通构建和运行不需要 Python。重建命令为 `python scripts/build-title-index.py <vn_mapper.db>`；外部数据库不提交。

## 安装、启动与构建

在仓库根目录执行：

```powershell
npm ci
npm run dev
```

`npm run dev` 同时启动 Vite 与 Electron；`npm run dev:ui` 仅启动浏览器预览。

```powershell
npm test
npm run build
npm start
npm run package
```

`npm start` 使用已经构建的 Electron 与前端文件，首次执行前需运行 `npm run build`。`npm run package` 包含类型检查和构建，生成 `release\win-unpacked\The Order.exe`；运行时保留完整 `win-unpacked` 目录。

## 应用数据与隔离验证

正式应用继续使用 `%APPDATA%\KunGalManager` 保存书架、设置及游玩记录。搬迁源码不会改变这一路径。API 密钥由 Windows 当前用户加密保存，不属于项目源码。

日常 `npm run dev`、`npm start` 和正式 EXE 默认会使用正式应用数据。验证、调试或尝试修改时，先选择独立目录：

```powershell
$env:KGM_DATA_DIR = Join-Path $env:TEMP 'the-order-development-profile'
npm run dev
# 完成并退出程序后恢复默认数据目录：
Remove-Item Env:KGM_DATA_DIR
```

不要把真实 API 密钥写入源码、命令参数或提交。需要连接服务时，在应用设置中填写。`.env`、加密凭据、个人 profile 和本地备份已由 `.gitignore` 排除。

第六轮三套界面候选使用独立的六条虚构书架：

```powershell
& 'D:\Projects\The-Order\scripts\start-iteration-candidate.ps1' -Iteration 06
```

程序为`.artifacts/iteration-06/candidate/win-unpacked/The Order.exe`，数据固定为`.artifacts/iteration-06/trial-profile`。在「偏好设置 → 界面风格」选择经典书架、Windows或Liquid Glass并保存；明暗主题独立。没有预置密钥，不复制正式或旧候选资料，重复启动保留自己的试用修改。第四、第五轮对应`-Iteration 04`、`-Iteration 05`，继续保留独立程序与数据。

本轮重建先`npm run build`，再`npm exec --offline -- electron-builder --win dir --config.directories.output=.artifacts/iteration-06/candidate`；不要用会覆盖原`release/`的默认打包命令。实际生产界面回归为`node tests/desktop-appearance-styles.mjs --executable '.artifacts/iteration-06/candidate/win-unpacked/The Order.exe'`，真实像素折射检查为`node tests/desktop-glass-optics.mjs --executable '.artifacts/iteration-06/candidate/win-unpacked/The Order.exe'`。两者各自创建临时合成profile、阻断HTTP/S，不需要密钥；按顺序运行，避免原生窗口焦点互相影响。后者用测试图案暂时替换工具栏背后的内容，是光学验证用例，不是产品外观截图。准确版本、图像索引及限制见[第六轮记录](docs/ai/ITERATION.md#第六轮三套可切换界面2026-10-04)。

夜间最终候选使用独立打包目录及六条虚构书架，启动命令为：

```powershell
& 'D:\Projects\The-Order\scripts\start-overnight-candidate.ps1'
```

程序位于`.artifacts/overnight-2026-10-04/candidate/win-unpacked/The Order.exe`，资料固定为`.artifacts/overnight-2026-10-04/trial-profile`。入口保持试用修改，未知或不完整目录拒绝写入；首次生成依赖本仓库已有Node/Electron，使用真实codec编码原创PNG/JPEG。未预置密钥，游玩记录明确为模拟，没有游戏EXE。不要直接双击候选EXE来做隔离试用。候选提交、正常入口检查及全部夜间交付见[OVERNIGHT](docs/ai/OVERNIGHT.md#晨间交付)。

显式隔离打包命令为`node node_modules/electron-builder/cli.js --win dir --config.directories.output=.artifacts/overnight-2026-10-04/candidate`，应先构建；默认`npm run package`仍会写原`release/`。夜间候选已打包，无需试用前再执行。生产包烟测为`node tests/packaged-overnight.mjs`，需先取得上述夜间writer guard并将其token设入`KGM_OVERNIGHT_GUARD_TOKEN`；它只用临时合成profile，不应在用户试用实例运行时同时执行。

睡前第三轮候选继续保留原程序和独立虚构书架，启动命令为：

```powershell
& 'D:\Projects\The-Order\scripts\start-iteration-candidate.ps1' -Iteration 03
```

程序位于 `.artifacts/iteration-03/candidate/win-unpacked/The Order.exe`，资料固定为 `.artifacts/iteration-03/trial-profile`。启动脚本保留已有试用记录，不复制正式书架或上一轮试用资料。`synthetic-photos/` 内有原创合成 PNG 可用于本地封面选择。具体提交、截图和验证见 [迭代记录](docs/ai/ITERATION.md#第三轮全局封面偏好本地图片与栏目装饰2026-10-03)。

本地封面仅在桌面版通过文件选择器读取静态 PNG/JPG；保存有界 JPEG 副本，不引用原文件路径，也不上传。照片副本随书架 JSON 备份保存，全局展示偏好属于设置，不在书架备份中。新程序兼容旧 version 1 备份；旧候选可能不认识新增照片或“跟随全局”字段，回退应使用旧版本对应的备份，不能视作无损逆向迁移。

## 回归验证

```powershell
npm test
npm run test:desktop
npm run package
npm run test:packaged
```

桌面回归使用临时独立 profile、合成游戏和模拟 API 响应。清除旧的 `KGM_TEST_EXECUTABLE` 环境变量，确保测试启动当前项目构建的程序。界面截图与测试报告写入 `.artifacts/`。

`tests/private-library-compat.mjs` 是可选的个人书架兼容检查工具，不属于默认测试链。必须显式提供 `--allow-private-profile --source-data-dir <绝对数据目录>` 才会读取所选数据，清除复制设置中的密钥，并在独立临时目录中只读验证。普通迁移与开发验证不需要这个工具，也不会运行它。

## WinUI 3 并行候选（Phase 1）

`native/TheOrder.slnx` 包含六个项目，不替代现有 npm 工程。Presentation 在 WinUI 项目内部；Domain 无平台/序列化依赖，Application 定义读取与设置能力，Infrastructure 做旧 DTO/校验/映射，Windows 适配器负责安全打开文件，WinUI 组合 DI、环境配置与 Debug 日志。无 Service Locator、数据库、API 客户端、游戏扫描/启动或高级效果实现。主题仅本次运行有效。

工具链：.NET SDK 10.0.401（`native/global.json` 禁止自动换 SDK）、Windows App SDK 2.5.1、WinUI 3 子包 2.3.9；所有 NuGet 项目有提交的锁文件。目标 x64 / `net10.0-windows10.0.26100.0`，最低 Windows 11 build 22000。本机 Build Tools 18.4 与 Windows SDK 10.0.26100 已存在；实际 C# CLI 构建使用 SDK/NuGet 工具，无需额外安装 VS/MAUI 工作负载。

从仓库根执行（安装 SDK 步骤只在缺少时使用）：

```powershell
# 官方精确版本 ZIP，SHA-512 校验，解压到 .artifacts/native-phase1/tools/dotnet；不修改系统 PATH。
& ./native/scripts/native.ps1 -Action Restore -InstallSdk
# 已有本地 SDK 时：
& ./native/scripts/native.ps1 -Action Restore
& ./native/scripts/native.ps1 -Action Build -Configuration Debug
& ./native/scripts/native.ps1 -Action Test -Configuration Debug
& ./native/scripts/native.ps1 -Action Run
& ./native/scripts/native.ps1 -Action Build -Configuration Release
& ./native/scripts/native.ps1 -Action Publish
& ./native/scripts/native.ps1 -Action Run -Published
```

构建/发布前先关闭相应候选实例，否则 Windows 会锁住 EXE；脚本不会杀掉用户窗口。也可传 `-DotnetPath <官方 SDK 的 dotnet.exe>`。运行脚本默认选 `native/fixtures/normal`；直接双击 EXE 时若未显式设置 `THE_ORDER_NATIVE_DATA_DIR`，界面会报告未指定目录，绝不使用 `KGM_DATA_DIR` 或正式 `%APPDATA%\KunGalManager` 回退。

候选只接受绝对资料目录，且必须含 `.the-order-synthetic`，内容精确为 `the-order-native-phase1-synthetic-v1`（允许末尾换行）。标记仅用于合成资料。不能给真实库加标记来绕过本阶段边界。拒绝正式目录及其父子目录、路径重解析和多硬链接，按已验证文件句柄有界读取；不存在或损坏时不写空库、不读取 `.bak` 自动恢复、不创建配置文件。

错误/空库实际窗口验证：

```powershell
& ./native/scripts/native.ps1 -Action Run -Published -DataDirectory (Join-Path $PWD 'native/fixtures/corrupt')
& ./native/scripts/native.ps1 -Action Run -Published -DataDirectory (Join-Path $PWD 'native/fixtures/empty')
```

关闭前一个窗口再运行下一项。正常库应显示 3 条记录；损坏库必须显示错误且不能显示成功空库；空库必须明确已成功读取。导航到设置切换浅色/深色后返回，窗口右上角关闭或 Alt+F4 应退出进程。测试前后核对 fixture 哈希，无写回。`node native/scripts/generate-fixtures.mjs` 可重建确定性虚构样本及指纹向量；它仅使用仓库源码/合成输入，不接受私库路径。图像样本是用于结构校验的 header-only JPEG，不代表原生 codec 验证。

测试含正常/空/多安装/自定义名称/0 与 null/缺项与 false/大字符串 ID/历史累计时长/Base64/格式版本错误/损坏截断、目录隔离、文件占用和硬链接等。safeStorage 实验只在运行时生成虚构随机秘密，在临时 profile 执行当前 Electron；密文与摘要通过管道传递，无明文落盘或日志。临时合成 profile 保留在 `%TEMP%\the-order-secret-probe-*`。实验表明密文依赖旧 profile `Local State` 的封装主密钥，不能只复制 settings.json；未来生产迁移需要独立适配和重新确认，缺少所需环境时重新填写凭据。官方实现参考 [Electron 44.5.1 safeStorage](https://github.com/electron/electron/blob/v44.5.1/shell/browser/api/electron_api_safe_storage.cc) 与 [同步加密后端补丁](https://github.com/electron/electron/blob/v44.5.1/patches/chromium/revert_oscrypt_remove_sync_backend.patch)。

发布目录 `.artifacts/native-phase1/publish/win-x64/` 必须整体保留，包括 EXE、.NET 10.0.12、Windows App Runtime 2.5.1、WinUI、本应用 `.pri` / `.xbf` 及 Views 子目录。项目显式声明生成资源的发布输入，遗漏 PRI 会使发布失败。当前不裁剪、不 AOT、不打单文件、不签名、不注册 MSIX，不替换 `release/win-unpacked`。本机真实运行确认 CLR / WinUI / App Runtime 从候选目录加载；`MSVCP140.dll`、`VCRUNTIME140.dll`、`VCRUNTIME140_1.dll` 仍使用系统已安装的 VC++ runtime，因此不能宣称可在完全干净的 Windows 上零前置运行。

验证证据：`.artifacts/native-phase1/test-results/compatibility.trx`、`loaded-runtime-modules.json`、`fixture-hashes.json`、`ui-observations.json`。Debug 与 Release 构建通过，39 项测试通过；原生导航/主题/错误/退出及发布目录启动已实测。干净 VM、ARM64、高对比/读屏完整验收、大型库帧时间、完整 JPEG/EXIF、真实用户凭据和资料迁移未验证。

Electron 保持原入口：本轮 `npm run build` 与 `node tests/packaged-b-smoke.mjs` 通过。`node tests/desktop-import.mjs` 在第76行等待 compact grid 中不存在的启动按钮超时，之前导入37条的断言已通过；属于本次观察到的旧回归脚本/界面不一致，未作范围外修复。

### Phase 1.1 兼容整改与复验

Phase 1.1 在 `775450e` 上修复 Gate 的两个 MAJOR 和三个 MINOR。旧 JSON 的字符串/整数例外仅存在于 `LegacyJson`；验证与 DTO 使用同一规则，保留孤立 UTF-16 单元。安装原始路径继续映射原文，身份键单独按旧 Windows/JavaScript 规则计算；root-relative 旧记录与 Electron 一样依赖运行进程的当前驱动器，绝不能把身份键作为扫描/打开路径的授权。`IsolatedLibrarySource` 的合成目录与文件句柄检查保持不变。

`generate-compatibility-vectors.mjs` 自动在已安装的锁定 Electron 的 Node 模式下加载当前 `validateLibraryBackup`，提取真实 `pathKey`；不启动 Chromium/UI 或打开用户 profile。它生成 7 组字符串、29 组路径、7 组数值正反参考。路径大小写不能使用 .NET 简单小写替代 JavaScript 全量小写：`LegacyUnicodeData.json` 是 Electron 44.5.1 内置 Node 24.21.0 / Unicode 17.0 的参考快照，只有 Legacy 路径比较使用，并随程序集嵌入。生成器同时生成 case-property 范围；C# 处理 contextual final sigma，保持单独 surrogate。参考 hash、Electron 版本、向量数量与内嵌快照 hash 在测试中检查。参考生成需先按 npm 锁文件安装现有依赖；若旧源码或参考引擎升级，先审查行为差异，再显式重建，不能只为消除 freshness 失败而无审查更新。

```powershell
# 仅显式维护参考时运行；正常 C# 测试消费提交的静态向量。
node native/scripts/generate-fixtures.mjs
node native/scripts/generate-compatibility-vectors.mjs
# 发布后，每次只开一个窗口，观察正常合成书架，再正常关闭。
& ./native/scripts/check-published-runner.ps1
& ./native/scripts/check-published-runner.ps1 -WithoutSdkLookup
```

`Run`（含 `Run -Published`）不再寻找或安装 SDK；其他动作继续按精确版本构建。`check-published-runner.ps1` 实际调用 `native.ps1 -Action Run -Published`，返回启动 PID/窗口标题并保留窗口供检查。`-WithoutSdkLookup` 只在本次脚本进程收窄 PATH，并以失败探针拦截 `dotnet` 和本地 SDK 查找，不改系统 PATH、不移动 SDK。该验证不能代替无开发工具/VC++ 的干净机器 Release Gate。

本次锁定 restore、Debug/Release build（0 warnings/errors）、两配置完整测试均通过，**84 passed / 0 failed / 0 skipped**，比 Phase 1 新增45项。生产链测试在自己的临时合成目录通过实际 `IsolatedLibrarySource` 加载，逐 UTF-16 单元断言字符串并核对原文件字节。Electron build 和完整 import regression 已通过：改用详情中的既有启动可用状态/收藏操作，未启动游戏；重复导入、个人资料保留、重启持久化断言全部抵达。没有修改 Electron 产品源码。两种 published runner 的真实窗口均显示3条合成记录并正常退出。

本次证据在 `.artifacts/native-phase1.1/`：`debug-tests.trx`、`release-tests.trx`、构建/发布记录、`runner-normal.json`、`runner-no-sdk.json`、`native-ui.json`。原 `.artifacts/native-phase1/` 的已编译候选目录被正常重建，正式 `release/win-unpacked` 未更新；早期 UI 回执只代表其当时构建。Gate 为 **PASS FOR PHASE 2**，不代表已经启动 Phase 2，也不包含真实资料、凭据或正式发布授权。

## Git checkpoint

初始基线记录迁移后的可构建源码。运行前后的构建、截图、日志和本机凭据不会进入基线。使用个人提交身份时，仅在此仓库配置：

```powershell
git config user.name "你的姓名"
git config user.email "你的提交邮箱"
git status
```

每次提交前检查暂存内容；不要提交正式应用数据，也不要把发布目录作为新的源码目录。

## ACGN 书籍出版资料候选（2026-10-15）

隔离工作区：`D:/Projects/The-Order-worktrees/acgn-books-20261015`，`codex/acgn-books-20261015`。来源为Argon当前未发布dirty的逐文件快照，不是旧Electron主目录或正式安装输出。继承快照与本轮增量分开；未经确认不得把候选安装到日用入口，也不得访问Production开发测试。

模型在Application `BookBibliography.cs` / `BookBibliographyService.cs` / `BangumiBookRelationsService.cs`；NDL SRU/OAI与来源保护在Infrastructure `NdlBibliographyProvider.cs`，Bangumi新增BookFacts并复用原关系transport；Windows `IsolatedBookBibliographyStore.cs`将可清缓存和用户确认映射分目录；Presentation `BookDetailsViewModel.cs`及WinUI `BookPublicationView.cs`复用AniList媒体host。不要按标题、ISBN、文库品牌或MANGA/NOVEL格式推测同一系列／版本；旧Book确认指纹增加层级后须重新核对，保留旧文件。没有书库schema迁移、OAuth改动或新第三方依赖。

固定SDK仍为 `D:/Projects/The-Order-worktrees/claude-recognition-rnd/.artifacts/native-phase1/tools/dotnet/dotnet.exe`。新checkout先 `restore native/TheOrder.WinUI/TheOrder.WinUI.csproj --locked-mode -p:Platform=x64`，测试项目同样locked restore。`publish ... -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -p:UseSharedCompilation=true -o .artifacts/acgn-books-20261015/<新候选>` 为本地构建，不是正式发布。不要并行构建同一Native输出目录。

定点回归：`test native/TheOrder.Tests/TheOrder.Tests.csproj -c Release --no-restore -p:Platform=x64 -p:UseSharedCompilation=true --filter "FullyQualifiedName~Book|FullyQualifiedName~Ndl|FullyQualifiedName~AniList|FullyQualifiedName~Bangumi|FullyQualifiedName~MusicBrainz|FullyQualifiedName~Related|FullyQualifiedName~InstallationNavigation|FullyQualifiedName~NextMoeOAuth"`。模拟TRX、真实匿名API、指定Test窗口截图分别记录在本证据根；真实research脚本仅为一次有界调查，不能作为客户端批量抓取。

最终GUI候选 `publish-v4`：用本证据根 `Start-IsolatedTest.ps1 -RunName <新的名称> -PublishName publish-v4`，受限Control的 `bangumi` 动作新增bookExpand/Collapse/Relations/Open/Search/Preview/Confirm/Forget/Reload/Next/Back/Cancel及固定section inspect。Open/Preview/Forget只作用于当前已见ID；回退使用 `-Action bangumi -Value back`，正常退出用 `-Action exit`，不可强杀。所有写入仅合成Test的缓存／映射，图片WGC限制精确PID、EXE、Test manifest和本证据输出。

NDL数据来源许可与加工说明已加入THIRD_PARTY_NOTICES；不搬NDL正文/书影/第三方Item、MangaDex阅读资源或上游采集脚本。当前按需NDL无key，1秒全局间隔，25秒Provider预算（服务串行等待另计并可取消），请求/响应限额与失效缓存不要放宽。AniList许可/缓存边界仍需澄清；NDL持续访问联系方式尚未登记，不宣称许可审批或长期SLA通过。
## ACGN 专业源深化候选（2026-10-15）

最新工作区 `D:/Projects/The-Order-worktrees/acgn-deepening-20261015`、分支 `codex/acgn-deepening-20261015`，基线bf81180。原Books工作区不重置、原Production不读取。固定SDK、NuGet锁和平台版本不变；新契约BangumiEpisodes/CatalogReleases与Music Work为显示数据，不编码进书架/识别/安装。

构建：`publish native/TheOrder.WinUI/TheOrder.WinUI.csproj -c Release --no-restore -p:Platform=x64 -p:NativeProductionCandidate=true -p:UseSharedCompilation=true -o .artifacts/acgn-deepening-20261015/<新目录>`（本地候选输出）。不要并行写Native构建目录。此次最终目录publish-v4，运行后不得直接以默认环境打开Production。

`.artifacts/acgn-deepening-20261015/Start-IsolatedTest.ps1 -RunName <新名称> -PublishName publish-v4`仅使用有manifest的Test profile；seed/verify runner只复制合成library组件，不复制配置/凭据/用户库。Control的bangumi动作新增aniEpisodes、musicTrack（已显示第一碟位置）、releaseExpand/Refresh/Inspect、catalogOpen（Test且合法公开目录ID）。所有内部原站导航必须保留出处，禁止用fixture伪造真实关系。退出用Action exit正常关闭，不强杀。

901相关回归TRX为tests/deepening-regression.trx；最终95定点TRX为tests/deepening-final.trx，不能相加成独立测试数量。最终定点filter：BangumiEpisodes、BangumiEpisodeAuthorization、BangumiEpisodeExpiry、MusicBrainzDeepening、CatalogReleases、AniListVoiceRoles；使用既有dotnet test命令和独立结果目录。截图及accepted证明明确区分110研究请求、真实GUI Provider路径与合成故障测试。

Episodes复用SubjectDetails同一transport/auth队列与25秒预算，50条分页，仅授权canonical subject_id+limit50+offset（50倍数≤10000）。会话cache32页/1小时fresh/7天最大stale，500行UI上限，追加新鲜页不能隐去旧页过期提示；OAuth到期通过RequireCurrent阻止迟到结果。NextMoe display releases32页/8MiB/5分钟cache，100条分页/500条UI上限，普通ClearCache包括它；网络失败可保留已显示旧行并提示，明确拒绝/格式损坏必须清除。Music Work从既有album include返回，不逐曲请求；App.xaml模板编译生成WinUI类型信息，不再运行时XamlReader解析自定义控件。
