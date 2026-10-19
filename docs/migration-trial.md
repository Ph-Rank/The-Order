# PotatoVN 资料导入步骤

## 在设置页导入到当前书架

1. 在 PotatoVN 的「设置 → 其他设置 → 导出数据」导出 `.pvnExport.zip`。无需解压，不要选择安装目录压缩包或游戏压缩包。
2. 打开 The Order，进入「设置 → 从其他管理器导入」，点击「选择 PotatoVN 备份 ZIP…」。
3. 选择刚才的 ZIP，等待预览。核对作品名称以及新增、更新、待核对、已导入的数量。作品编号与安装位置相符时自动匹配；附带其他 Steam 链接不会否定已有的同一本体编号。
4. 对「待核验」条目点击「核验对应与时长…」，选择当前书架中的对应作品，展开查看编号、安装位置和版本差异。确认是同一作品及版本后，勾选核验框。时长默认保留本地非空记录，也可明确选择「采用来源累计值（不相加）」；本地为空时默认采用来源。已有真实会话与启动次数保留，不能选择低于已知记录的累计值。点「确认对应」返回预览，仍未写入书架；可再次打开并「撤销本条核验」。
5. 点击「备份并导入」。新增明确的新条目，或合并已确认条目的个人记录。进度取双方优先级较高者；本地已有笔记、评分、作品资料与名称不被覆盖。未核验条目暂不导入，不影响其他条目。选择「取消」不会修改书架。
6. 完成后回到「书架」或「统计」查看。确认随来源记录持久保存；重启和重复导入不会增加游戏或时长。来源发生其他变化时重新核验，不会自动反复覆盖累计值。

进度统一为：**玩过 > 在玩 > 想玩 > 搁置 > 放弃 > 未设置**。普通扫描、手动添加或在线添加默认为「未设置」；PotatoVN 迁移的新条目保留来源进度。以前的「未开始」显示为「未设置」，不会自动变成「想玩」。手动编辑允许自行降低或清除进度，优先级规则只用于导入和合并。旧版本迁移器把「想玩」合入「未开始」的记录，可在重新导入同份来源时修正，不重复导入其他资料。

导入直接作用于当前打开的书架；无需命令行或开发工具。开始前请结束计时并处理待恢复会话。源 ZIP 严格只读，不搬动游戏、存档或账号，不导入第三方启动脚本、工具或未经核验的参数。只支持当前已验证的 PotatoVN 导出格式，其他管理器 ZIP 不可使用。

导入前自动保存并回读校验当前书架，不受「自动备份」开关影响；备份失败则不导入。需要撤回整批导入时，进入「设置 → 备份与恢复 → 查看自动备份」，选择「导入前」记录并使用「恢复整份书架」。这会一并恢复到导入前的个人记录；若导入后有新的编辑，请先导出当前备份。自动云端同步在导入时暂停，核对结果后可自行重新开启。

只有累计时长的历史仍为无日期历史，不会伪造每日统计、启动次数或会话；来源时间缺少时区时保留原始时间证据。人工采用来源总量也不把两边相加，只更新无日期差额。未知格式、文件损坏或容量超过现有安全上限会停止导入；来源内部重复记录、矛盾数据及父子目录重叠不能靠人工核验绕过。较旧程序只能只读打开含未知扩展的书架；本版人工核验记录使用 v5，回退到旧程序前，请在新版内恢复核验导入前的备份。不要直接覆盖 Profile 文件。

验证使用公开上游合成格式及隔离 Test Profile；另在用户明确授权下只读核对其导出与当前书架，未执行真实用户导入。没有进行桌面点击验收。实际操作请按上述步骤试用；日用安装是否包含本增量须核对最新构建回执。

## 可选：命令行隔离试导入（仍仅 Test）

本机版本附带独立命令行导入工具，无需安装 .NET SDK。当前仅支持 PotatoVN 已知官方导出格式；Vnite、ReinaManager、Playnite 和 LunaBox 尚不能导入。

下述命令行工具仍是**专用测试资料库试导入**，与上面的应用内入口不同：不会写入、合并或替换日用书架，命令行导入的条目不会出现在平时打开的书架中。不要直接把测试资料文件复制进正式 Profile。

## 1. 从 PotatoVN 导出

在 PotatoVN 的「设置 → 其他设置 → 导出数据」生成 `.pvnExport.zip`，把文件保存到你自己选择的导出目录。官方说明：https://potatovn.net/usage/advance/data-exchange.html 。

保留原导出包，无需解压或改写其中的文件。不要用管理器实时数据库或 `.PotatoVN` 单游戏备份代替。当前压缩包上限 512 MiB；超过上限时先停在这里，不删减原始备份。

## 2. 在 PowerShell 准备路径

打开普通 PowerShell，无需管理员权限。复制以下内容，**只把 `$source` 改成你的实际导出文件路径**：

```powershell
$tool = Join-Path $env:LOCALAPPDATA 'Programs\The Order\Tools\LibraryMigration\TheOrder.MigrationTool.exe'
$source = 'D:\Exports\PotatoVN_2026-10-19.pvnExport.zip'
$trial = Join-Path $env:LOCALAPPDATA ('TheOrder-Migration-Trials\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$target = Join-Path $trial 'TestProfile'
```

每次独立试验使用新 `$trial`，不要把 `$target` 改为正式资料目录。该目录由工具创建，不必提前复制资料进去。

## 3. 只读预检来源

```powershell
& $tool preview --source $source --out (Join-Path $trial 'source-preview') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '来源预检未通过，请保留错误提示，不要继续提交。' }
Get-Content -LiteralPath (Join-Path $trial 'source-preview\preview.md') -Encoding UTF8
```

检查原始条目数、可迁移数量、无效记录、时长与未导入字段。报告中的路径仅经过语法校验，程序不会读取或运行游戏。报告不包含笔记正文、标题、敏感绝对路径或账户凭据。

## 4. 创建测试资料库并生成绑定计划

```powershell
& $tool init-test --target $target
if ($LASTEXITCODE -ne 0) { throw '测试资料库创建失败。' }
& $tool preview --source $source --target $target --out (Join-Path $trial 'target-preview') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '目标预览未通过。' }
Get-Content -LiteralPath (Join-Path $trial 'target-preview\preview.md') -Encoding UTF8
```

命令行安全操作会列为 Create 或 FillMissing；Candidate、Conflict 和无效记录会跳过。CLI 不提供人工决策参数；逐条核验请使用上方应用内入口。同名和相似标题不会直接合并。

如果来源的无 offset 时间明确采用中国时区，可以在**所有 preview 和 apply 命令**中一致加入 `--source-time-zone Asia/Shanghai`。其他时区使用其真实标识；未指定时完整保留来源本地时间证据，不猜成 UTC。若改变时区选项，使用新的报告目录重新生成目标计划。

## 5. 确认预览后试导入

只有确定要将可迁移项写入上述专用测试库时，执行：

```powershell
$plan = (Get-Content -LiteralPath (Join-Path $trial 'target-preview\preview.json') -Raw -Encoding UTF8 | ConvertFrom-Json).planId
& $tool apply --source $source --target $target --commit $plan --out (Join-Path $trial 'committed') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '提交或重读验证未通过，请保留本次目录和错误提示。' }
Get-Content -LiteralPath (Join-Path $trial 'committed\preview.md') -Encoding UTF8
```

正常结果包含 `Reopen verification: PASS`、写入前后条目数和有效时长。无日期历史仍无日期，不会均摊到每天；原导出包不修改。报告写入失败可能发生在数据已提交之后，此时先看终端状态，不要盲目重复旧计划。

## 6. 检查重复导入

```powershell
& $tool preview --source $source --target $target --out (Join-Path $trial 'repeat-preview') | Out-Null
if ($LASTEXITCODE -ne 0) { throw '重复预览未通过。' }
Get-Content -LiteralPath (Join-Path $trial 'repeat-preview\preview.md') -Encoding UTF8
```

同一批成功导入的记录应显示 AlreadyImported，可迁移新增为 0；此前冲突和跳过项仍保留。来源发生变化时需要重新预览，不会直接叠加时长。

完成后保留 `$trial` 中的 JSON/Markdown 报告。反馈时优先说明计数、错误代码和问题字段；不要上传原导出包、账户凭据、私人笔记或未经脱敏的库文件。关闭 PowerShell 不影响日用程序。删除试验只应处理你明确创建的 `$trial`，不要改动原导出和正式资料目录。
