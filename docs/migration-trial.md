# 管理器资料导入与兼容导出

## 在设置页导入到当前书架

1. 在 PotatoVN 的「设置 → 其他设置 → 导出数据」导出 `.pvnExport.zip`。无需解压，不要选择安装目录压缩包或游戏压缩包。
2. 打开 The Order，进入「设置 → 从其他管理器导入」，点击「选择备份 ZIP…」。其他已验证来源及格式见下方。
3. 选择刚才的 ZIP，等待预览。核对作品名称以及新增、更新、待核对、已导入的数量。作品编号与安装位置相符时自动匹配；附带其他 Steam 链接不会否定已有的同一本体编号。
4. 对「待核验」条目点击「核验对应与时长…」，核对作品、编号、安装位置与版本，再勾选核验框。两边累计非零且不同时，首选「相加两边时长」；若包含重复历史，请改选「保留本地」或「采用来源累计值（不相加）」。本地为空时默认采用来源。已有真实会话与启动次数保留，不能采用低于已知记录的总量。点「确认对应」返回预览，仍未写入书架；可以撤销本条核验。
5. 点击「备份并导入」。新增明确的新条目，或合并已确认条目的个人记录。进度取双方优先级较高者；本地已有笔记、评分、作品资料与名称不被覆盖。未核验条目暂不导入，不影响其他条目。选择「取消」不会修改书架。
6. 完成后回到「书架」或「统计」查看。确认随来源记录持久保存；重启和重复导入不会增加游戏或时长。来源发生其他变化时重新核验，不会自动反复覆盖累计值。

进度统一为：**玩过 > 在玩 > 想玩 > 搁置 > 放弃 > 未设置**。普通扫描、手动添加或在线添加默认为「未设置」；PotatoVN 迁移的新条目保留来源进度。以前的「未开始」显示为「未设置」，不会自动变成「想玩」。手动编辑允许自行降低或清除进度，优先级规则只用于导入和合并。旧版本迁移器把「想玩」合入「未开始」的记录，可在重新导入同份来源时修正，不重复导入其他资料。

导入直接作用于当前打开的书架；无需命令行或开发工具。开始前请结束计时并处理待恢复会话。源 ZIP 严格只读，不搬动游戏、存档或账号，不导入第三方启动脚本、工具或未经核验的参数。非 Galgame 及缺少可靠类型证据的条目列为「已排除」，同名不能证明类型或作品身份。

支持 PotatoVN 官方 `.pvnExport.zip`、Vnite 4.14.0 完整数据库备份 ZIP、Playnite 10.62 游戏库备份 ZIP、LunaBox 1.13.3 数据库备份 ZIP。ReinaManager 0.32.0 先用程序自带备份生成一致性 `.db`，将该文件压成 ZIP 后选择；不要复制正在使用的数据库主文件。详见 [格式与验证边界](manager-interchange.md)。

导入前自动保存并回读校验当前书架，不受「自动备份」开关影响；备份失败则不导入。需要撤回整批导入时，进入「设置 → 备份与恢复 → 查看自动备份」，选择「导入前」记录并使用「恢复整份书架」。这会一并恢复到导入前的个人记录；若导入后有新的编辑，请先导出当前备份。自动云端同步在导入时暂停，核对结果后可自行重新开启。

相加会一并保留来源可靠的逐日日期、真实会话和较晚的最近游玩时间；没有日期的剩余累计仍单独列出，不伪造日期、启动次数或会话。已计入的同来源记录不会重复累计，已有手动修改或撤销保持不变；有歧义的重叠记录会在预览提示。采用来源总量仍只调整无日期差额。来源时间缺少时区时保留原始证据。未知格式、损坏或超限停止导入；来源内部重复记录、矛盾数据及父子目录重叠不能靠人工核验绕过。日期来源凭据使用 v7（原累计相加为 v6），旧程序遇到新版本应只读；回退前先在新版恢复导入前备份，不直接覆盖 Profile 文件。

如果此前已经选择相加，但统计仍显示部分时长没有日期，可以重新选择当时的同一份 ZIP。原核验记录一致时，预览会显示「补全游玩日期」；确认导入只补回可靠日期，累计时长不变。来源仅保存总时长、备份已改变或无法确认重叠的部分，不能强行填入日期。

本轮验证仅使用公开格式、合成个人记录和隔离 Test Profile，包含外部引擎/部分原生应用恢复检查与短 WinUI 交互。没有执行真实用户导入；日用安装是否包含本增量须核对构建回执，本轮不自动替换。

## 导出给其他管理器

在「设置 → 备份与恢复 → 导出兼容 ZIP…」选择目标，查看字段及精度差异后保存。原「导出书架备份」继续保留，完整还原 The Order 应使用原生备份。

先备份目标管理器的现有库，再使用它自带的恢复功能选择 ZIP；ReinaManager 需解压后选择 `.db`。这些恢复通常替换目标库，并非合并。

整分钟精度、Playnite 缺少原生会话、LunaBox 缺少无日期累计等差异会在保存前说明。无法直接表达的数据放在补充文件中，目标管理器不会自动读取；兼容 ZIP 不承诺无损迁移全部资料。

## 可选：命令行隔离试导入（仍仅 Test）

候选可附带独立命令行工具，无需 SDK；当前源码支持上方五种固定格式。旧日用安装附带的工具不一定包含本增量。CLI 写入仍仅支持专用 Test Profile；下方以 PotatoVN 为例，其他来源更换 ZIP 路径即可。

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
