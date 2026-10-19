# PotatoVN 资料试导入步骤

本机版本附带独立命令行导入工具，无需安装 .NET SDK。当前仅支持 PotatoVN 已知官方导出格式；Vnite、ReinaManager、Playnite 和 LunaBox 尚不能导入。

目前是**专用测试资料库试导入**：不会写入、合并或替换日用书架，也没有图形导入按钮。导入的条目暂不会出现在平时打开的书架中。先检查报告，后续正式入库需另行实现与验收；不要直接把测试资料文件复制进正式 Profile。

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
