# PotatoVN 资料导入步骤

## 在设置页导入到当前书架

1. 在 PotatoVN 的「设置 → 其他设置 → 导出数据」导出 `.pvnExport.zip`。无需解压，不要选择安装目录压缩包或游戏压缩包。
2. 打开 The Order，进入「设置 → 从其他管理器导入」，点击「选择 PotatoVN 备份 ZIP…」。
3. 选择刚才的 ZIP，等待预览。核对作品名称以及新增、补全、待核对、已导入的数量。
4. 点击「备份并导入」。只新增明确的新条目或补全安全缺项；冲突和候选跳过，不自动覆盖已有笔记、评分或累计时长。选择「取消」不会修改书架。
5. 完成后回到「书架」或「统计」查看。再次选择同一份备份，已导入条目会跳过，不重复增加游戏或时长。

导入直接作用于当前打开的书架；无需命令行或开发工具。开始前请结束计时并处理待恢复会话。源 ZIP 严格只读，不搬动游戏、存档或账号，不导入第三方启动脚本、工具或未经核验的参数。只支持当前已验证的 PotatoVN 导出格式，其他管理器 ZIP 不可使用。

导入前自动保存并回读校验当前书架，不受「自动备份」开关影响；备份失败则不导入。需要撤回整批导入时，进入「设置 → 备份与恢复 → 查看自动备份」，选择「导入前」记录并使用「恢复整份书架」。这会一并恢复到导入前的个人记录；若导入后有新的编辑，请先导出当前备份。自动云端同步在导入时暂停，核对结果后可自行重新开启。

只有累计时长的历史仍为无日期历史，不会伪造每日统计、启动次数或会话；来源时间缺少时区时保留原始时间证据。未知格式、文件损坏或容量超过现有安全上限会停止导入。较旧程序可能只能只读打开含迁移来源的书架；程序回退前，请在新版内恢复导入前备份。不要直接覆盖 Profile 文件。

本轮自动验证使用公开上游合成夹具及隔离 Test Profile；没有读取私人导出或日用资料，也没有进行桌面点击验收。实际操作请按上述步骤试用。

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

当前安全操作会列为 Create 或 FillMissing；Candidate、Conflict 和无效记录会跳过。此版不提供强制匹配或人工消解入口；同名和相似标题不会直接合并。

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
