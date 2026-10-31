# Claude UI 独立审查与集成准备 · 2026-10-08

**当前：已批准有限资源例外，完成开发集成；不代表资源协议全部通过或正式发布获准。** 用户明确批准第10节提出的例外后，已在独立本地分支完成集成、构建和受影响验证，详情见第11节。下方复核时的“待批准”和历史 BLOCKED/FAIL 原文保留，不作为当前批准状态；原审查工作区报告完全未改。

**建议有条件开发集成 — RC1 Private 代理指标例外待用户批准。** 本轮仅复核已有代码/协议/原始数据，没有新负载或应用修改。没有证实新的必须修复的应用资源缺陷；旧热端点FAIL、RC1三项FAIL及low Advanced PASS均保留。完整轨迹支持自然回落和重复容量复用，但不证明所有热态平坦或无限有界。RC1明文要求满足屏障才能READY，不能自行豁免；第10节给出独立风险判断及有限例外。第9节JSON文字ON/OFF的强制前置建议不再适用，不执行该实验。

允许继续隔离 Test 体验；没有批准默认启用或正式发布。此前修复、Debug/Release、294项相关回归、native optical与真实Shift证据按同一候选身份复用。本报告保留上轮FAIL与记录，不改变用户已经认可的视觉方向。

## 1. 审查对象与保护

| 工作区 | 分支 / 实际起始 HEAD | 状态 |
|---|---|---|
| `D:\Projects\The-Order` | `codex/winui-phase5` / `14a96a1fc793b7fba231ab3b8fd1da607c3e86c5` | 原有 dirty 保留 |
| `C:\Users\USER\.codex\worktrees\winui-phase7-visual\The-Order` | `codex/winui-phase7-visual` / `ad59c6eeab3ea964462fbacc6b7547b48a72808b` | 原有 dirty 保留 |
| `D:\Projects\The-Order-worktrees\claude-design-phase7` | `claude/design-phase7` / `7214dbc0e4842465f67c31d3c82ce43b7fd48ddc` | clean，原始交付不改 |
| **独立审查** `D:\Projects\The-Order-worktrees\codex-claude-ui-review` | `codex/claude-ui-review`，由完整 `7214dbc` 建立 | 修复仅在此处 |

原三工作区结束时分支、HEAD、Git 状态列表与开始时一致；原 candidate-3 全文件哈希重新核对未变。没有 reset/clean/stash、合并、推送、正式程序替换、Production 访问或系统设置更改。使用现有锁定 SDK 与依赖，没有安装或升级依赖。复用已有 Electron 44.5.1；锁文件中的 Electron 版本相同。

修复提交：**`95b0ad74296ae67ed0798675b08fc299b46aebe8`**，`fix: close artwork and shell lifecycle gaps in UI candidate`。6 个 UI/诊断源码文件，+96/-15。源码、最终补丁、三方状态和二进制清单保存在审查工作区 `.artifacts/claude-ui-independent-review/`。

candidate-3：WinUI.dll SHA256 `7FC9035C044CA067487EA601F3F47838EAD3B012200A272B8BD7397370181A9E`；OpticalMaterial.dll `4CBDA06A5595C35B4C754EBBFEA4EDF76897B48B430A67B86D8A674F30535E7A`。其 PDB 中 42 个 tracked WinUI 文档校验值全部与 Claude 最终源码一致；`695fc99..7214dbc` 没有 native 源码差异，后者为交付文档提交。

最终审查候选：`.artifacts/claude-ui-independent-review/candidate-final/win-x64/`。

- WinUI.dll SHA256：`2F0E0E10EEACAE859AF6847E2C25ECA21D50202515718EBF6449E24DFBD511FD`。
- OpticalMaterial.dll SHA256：`1476FA7CDE72C31697F56AC751526E03A4C07D4EC5DA85BEB45BDCD8B39D35CE`。
- WinUI MVID：`c91662b9-c60b-4c8e-add9-6335ed1ee0b4`。
- 构建发生在修复提交前，嵌入 informational version 仍为 `1.0.0+7214dbc...`。**不能靠该版本字符串绑定源码**；最终 PDB 的 43 个 tracked WinUI 文档哈希全部匹配 `95b0ad7` 的源码。全目录身份见 `candidate-final-manifest.json`，PDB 核验见 `pdb-review-final.json`。后续只有审查文档变更，没有再次改应用。

## 2. 有证据的缺陷及修复

| 严重性 / 状态 | 触发与影响 | 位置、修复与证据 |
|---|---|---|
| MAJOR · 已修复 | 系统材质策略不允许透明时，ArtworkStage 原先落到 Standard，又自行创建 Acrylic，绕过不透明降级 | `Views/ArtworkStage.cs:145`；`!allowed` 映射内部 Opaque。原 candidate-3 ForceSolid 证据仍为 Standard；最终真实 stage audit 证明 Solid、无 renderer。系统 HC 分支也通过注入检查 |
| MAJOR · 已修复 | CoverKey 改变后旧 swapchain / fallback bitmap 在新图缺失、失败或延迟时可能留下前一作品背景 | `Views/ArtworkStage.cs:64`；先退休旧 owner、清除 fallback，再开始新身份。最终 stage audit 证明 null key 即时清空 |
| MAJOR · 已修复 | 提前离开仅 Detach/Release，仍 loaded 且 request 有效；不合作的异步图片完成可在正式 Unloaded 前重新建立 renderer | `Views/ArtworkStage.cs:95`；先撤 loaded 与请求 lease，再解除订阅 / 释放。真实 Test stage 使用故意忽略取消的延迟 pipeline，离开后完成不再复活 |
| MAJOR · 已修复 | 正常退出期间新增标题搜索、页面命令和异步 QuickLaunch 入口没有完整服从 commandsBlocked；取消离开草稿后搜索仍可改查询 | `MainWindow.Shell.cs:58/84`、`MainWindow.xaml.cs:96/140`；封锁输入/导航并在 await 后复核；取消导航恢复原查询。实际草稿提示、取消退出后可继续编辑已补验 |
| MINOR · 已修复，异常路径有限验证 | native detach 失败时 managed Release 原先先丢掉 optical，再 catch，失去唯一重试 owner | `Views/ArtworkStage.cs:271`；保留 retirementPending 与 owner，禁止继续绘制/替换，后续生命周期重试。native 故障回归验证 detach retry；没有在真实页面上制造驱动故障 |
| MAJOR · 审查阻塞，owner 未知 | 两模式相同周期终点 Private 增量超出预定 32 MiB 屏障；静置明显回落，不能直接定性为 retained leak | 第 4 节及 `runs/accepted/boundaries.jsonl`；未改阈值、未修改 cache/GC/虚拟化来获得通过 |

没有修改 C1 shader、厚度/色散参数、Native optical 算法、通用图片 ownership/cache、业务 schema、Session/accounting、识别、launch admission 或 store/writer contract。保留同设备 D2D/DirectWrite/WIC Gallery 背景源与切页前释放 swapchain 的修复。新增 `ArtworkStage.Audit.cs` 和 opticalProbe 只由现有隔离 Test 能力入口调用。

## 3. 行为审查与验证范围

**已独立执行：**普通封面点击进入 Details；标题搜索；脏草稿离开提示/取消、取消退出后窗口继续可用；选择模式 Esc；文本框 Ctrl+A 不选择游戏；最终 Shift+click 的用户实机补验明确选中同排 1–5 共 5 项；返回与焦点恢复。Shift 使用用户实际输入，工具没有伪造组合点击。

上述交互主要对应首个审查候选（WinUI.dll `F3BE4857...439A24`）。它与最终候选的差别只有 detach 失败 owner 保留及 Test opticalProbe；相关 Library 输入、Shell 封锁和草稿代码相同。**没有把早期 Claude 录像当成最终 Shift 范围实现的验证。**

最终候选另补验：Light/Dark、620 DIP 窄窗、当前机器 200% DPI（RasterizationScale=2）、文本缩放=1；透明策略、Grid/List 重挂载、隐藏/恢复、页面释放。实际点击右上角 × 后：tray=true、hidden=true、closing=false、两处 renderer 均释放；恢复用 Test show 入口，未冒充真实托盘菜单点击。最终正常退出走 `App.RequestExitAsync`，exitCode=0，Test 库哈希前后相同。

Ctrl+Enter 的本轮快速连续输入没有可靠区分各异步导航完成边界，**不算新增独立通过项**。保留 Claude 原 I 组的有效 NotReady/右键证据；代码核查 QuickLaunch 仍只使用同一 `LaunchViewModel.OpenAsync/CanLaunch/LaunchAsync`，不 Ready 则进入 Details，确认/取消/选择语义未另造。合成库安装路径不存在，没有实际启动游戏。

托盘单图标/单窗口、托盘不可用正常退出、草稿保存/放弃与写入等待，复用 Claude 已保存 L/U 组证据并审查 App / WindowsTrayLifecycle：隐藏只 checkpoint；退出先解除后台工作、处理草稿、PrepareExit，再 Dispose；取消清除 exiting 并恢复操作。WM_QUERYENDSESSION/WM_ENDSESSION 有独立分支，不经过 close-to-tray。没有实际注销/关机、托盘故障或真实 Session/game 测试；294 项相关 tests 含已有替身和生命周期测试，不能替代这些 live 边界。

Windows accent 订阅在 DisposeShellInput 解除，排队回调被 visualStopped 拒绝；新最终候选 Test purple 更新 20 个共享刷，随后恢复实际系统 `0078D4`，没有更改个性化设置。真实 Windows accent 事件、Windows 高对比度切换、其他 DPI/文字缩放仍未 live 验证；HC 仅验证实际 ArtworkStage 的注入降级。已见既有 `subscribe.highContrast` COM 通知警告，当前状态读取成功且有既有 owner refresh 轮询；没有隐瞒成系统通知完全通过。

“最近游玩”只投影现有 `LastPlayedAt` 与 play-history 标记；缺失/无效时间分别显示从未游玩或时间未知。没有写入新日期。Details 保存栏仍通过原 Save/Discard；⋯ 菜单仍调用原编辑、安装、确认和错误路径。

两处玻璃确实使用当前作品 CoverKey 的 decoded pixels，经同设备 artwork scene 绘制为页面的真实 ambient band；它本身就是可见背景，**不是动态书架截屏或 live Library 像素捕获**。前景为独立 XAML；页面离开、CoverKey 变化、主题/DPI/几何与模式变化触发更新，无滚轮捕获循环。最终两模式稳定 idle 都没有额外 draw/revision/frost。C1 两种配置与用户认可位置保持，Gallery 保留。截图为实际 WinUI 合成 Test 数据，不是设计 mockup。

## 4. 最终资源短测：所有权通过，Private 屏障未通过

运行前固定 `protocol.md`，没有事后调门槛。PID 27020 / start `2026-10-08T12:59:06.0835429Z`，最终 MVID 如第 1 节。最终正式短测 310.64 秒；Standard 后 Advanced，每组 3 次 warm-up、20 次固定两条目 Library→Details→Library；同进程、窗口 1147×802 DIP、DPI2、Dark/Grid、同一 384 合成库。**顺序对照，有 warm-up/先后偏差；不能把 ON−OFF 当独立因果估计。**

每个边界先等 fixture 完成，检查 pending，读取外部进程/PDH 与内部有限 owner counters。93 个边界完整；无 ETW、heap trace、强制 GC、trim 或重建窗口。PDH GPU Process Memory Total Committed 按 PID 两 adapter 汇总；DXGI local usage 按 LUID 记录，不把 duplicate adapter 枚举或 Shared 加成物理显存。系统可用内存/系统 commit 未采集，不能声称这些保护线已验证。

所有数值 MiB=2^20 字节；列的时点不能拼成连续采样曲线。

| 指标 | Standard | Advanced |
|---|---:|---:|
| warm Private | 406.629 | 517.629 |
| 第 20 个 Library 完整周期 Private | 514.281 | 633.629 |
| 周期终点 Δ | **+107.652** | **+116.000** |
| 随后 idle 末 Private | 395.141 | 490.734 |
| idle 相对 warm Δ | −11.488 | −26.895 |
| 每 5 周期 Private median | 422.141 / 445.898 / 424.359 / 486.500 | 543.273 / 523.438 / 550.469 / 579.281 |
| warm → 第20周期 GC committed（最后完成 GC 的信息） | 69.492→67.766 | 69.449→68.820 |
| warm → 第20周期 handles | 1309→1304 | 1350→1337 |
| warm → 第20周期 GPU Total Committed | 188.621→180.836 | 242.383→208.371 |
| GPU Total Committed 全模式段峰值 | 216.227 | 258.113 |
| 两处 idle Δdraw/revision/frost | 全部 0 | 全部 0 |

idle 有 12 秒显式等待，两个端点含采样耗时，实际间隔为 16.77 / 17.11 秒。没有把相邻导航当 idle 或强制 GC。GC committed 与外部 Private 非同步，**不能相减得到精确 native 用量**。

| 冻结判据 | 实际值 | 判定 |
|---|---|---|
| 热循环端点 Private Δ≤32 MiB；成熟一致周期不得出现 >20 MiB 的持续回归信号 | 上表 +107.652 / +116.000；末两组 Advanced median 继续上移 | **FAIL，短测回归屏障命中**。不等于已定位 leak；Standard 也发生，不能归罪于高级材质 |
| handles 端点 Δ≤32，无持续积累 | −5 / −13 | PASS |
| 显式 native owner：可见高级仅 1；离开/隐藏/不透明为 0 | 93/93 边界满足；隐藏51/51、Settings56/56；最后新截图等操作后 Settings61/61 | PASS |
| Active C1，无失败/意外 Standard fallback | 所有受测可见高级状态匹配；stage failure / retirementPending 均无 | PASS |
| pending / stale / idle / cache | pending/stale 全部0；idle全0；cache max30.435MiB/98项；maximum active decode2；可见 bitmap计数1–52随页面变化 | PASS，bitmap数量不是物理 GPU surface 数 |
| 压力停止线 Private1.5GiB / GPUCommitted1GiB / local2GiB | Private峰633.629MiB；GPUCommitted峰258.113MiB；raw DXGI LUID记录未接近2GiB | 未触发；安全线不是稳定性 PASS |

Private 热循环上涨后无需 GC 干预就回落，GPU committed/handles 与显式 optical owner 不累积。因此**没有证据足以声明新的材质资源泄漏或具体 retaining owner**；也不能删掉失败门槛把全组改判 PASS。需要澄清 transient capacity / diagnostic perturbation，当前资源集成证据不闭合。Shared 原始数值保留，只作为计量记录；旧 GA1 与 Shared 历史结论不改。

启动过早 fixture 请求被初始化消费导致 timeout、随后日期字符串被 PowerShell 自动解析造成 PID 身份比较失败，分别保留为 INCONCLUSIVE / INTERRUPTED。修复的只是本轮外部采样入口 `ConvertFrom-Json -DateKind String`，没有弱化 PID/path/start 验证、改应用或抹去记录。正式 20+20 周期来自修正后完整运行。

三次 Test 窗口观察合计约 25 分钟：reference 约1.5分钟，首次审查约12.4分钟，最终约10.8分钟；包括失败准备/人工交互等待，另加 native GPU 单测仍低于30分钟新增资源负载预算。前两次没有 held-process exit-code watcher，不能补造退出码；确认进程消失与 Test 库 hash，不将其作为最终 exit=0 证据。最终 watcher 持有核验过的进程句柄，捕获实际 exit0。没有继续长测。

## 5. 构建、回归与持久化

Debug / Release publish：最终候选成功，0 warnings / 0 errors（Debug 12.40秒）。相关 managed tests：**294 passed / 0 failed / 0 skipped**，filter `Presentation|Session|Launch|Material|LensOptics|SafeStorage|Preference|ImageRequest`；TRX 与实际日志保存。native optical regression 全部 PASS：132 exact GPU readbacks、160 scene readbacks、同尺寸 upload reuse、failed detach retry、frost-off accepted C1 bit-exact、Clear/Continuous 边界与主题亮度检查。没有用测试通过替代视觉判断。

Claude 的 3 项历史失败是 SafeStorageExperimentTests 的 Electron executable 缺失（Win32 file-not-found），不是断言显示业务回归；本轮复用已存在且版本匹配的 Electron，**这3项均实际通过**。不删除/skip/改弱测试、不盲目安装整套环境。没有机械重跑全部1792项，复用历史 full suite 范围并明确本轮294项范围；最终 native optical、stage audit 和真实输入另外列出。

材质设置会话有效的解释准确：`IsolatedUiPreferencesStore.Decode` 对已版本化 NativeProfile v1 preferences 使用 UnmappedMemberHandling.Disallow，NativeProfileStore 对 component names 也为白名单。老的非-authoritative isolated reader较宽松，**不能据此推出实际 v1 store 可兼容新增字段**。偷偷加 JSON 字段或 sidecar 都会绕开/改变正式配置契约；不实施。UI 已明确“此选项只在本次运行中生效”。下一次需要持久化时，应单独设计可选 capability/版本化扩展及旧读写器保存未知扩展规则、降级/回退测试；不阻止会话模式的隔离开发。

## 6. 安全集成准备（尚未执行）

**绝不整体 cherry-pick/merge `7d74e2420e82e864efa969de240e792eda48aef6`。** 它是 GPT ad59 上47文件的 dirty 快照，混含已验证修复、背景源、诊断与后来废弃的 Toolbar 实验；不是 Claude 的新功能提交。完整清单见 `baseline-copy-files.txt`。Claude 实际 authored delta 清单见 `authored-files.txt`。

在新集成 worktree 中先逐文件协调目标开发分支自己的未提交改动与必要视觉前置，保持原工作区不动；目标 main14a 尚不含整个 ad59 视觉链。集成序列：

1. 从已批准旧视觉链确认 `14a..ad59` 的所需 UI/material/build 前置；不把 main 的 memory诊断 dirty 或旧运行证据自动带入。
2. 从 `ad59..7d74` **挑选并三方合并真正依赖**：GalleryScene/GallerySource、Native/managed OpticalRenderer 的同设备背景接口、RefractionGallery 和 MainWindow 切页前释放、MaterialTreeProbe 与 build-optical-material 适配。主/GPT树已有等价内容则验证复用；CaptureRepro/ImageBindingRepro、资源调查脚本不是新UI发布依赖。旧 ToolbarSnapshot/LibraryPage.Toolbar 被 Claude 后续删除，不恢复为产品方案。
3. 材质 implementation 依赖 `f18d609` 后 `09465c2`；最终 Clear/Continuous 依赖前者新增 renderer API。不要只拿 UI 的3个 checkpoint 而漏掉 native ABI/shader/helpers；若不保留已拒绝的第一版视觉，仍需合并最终 API 与实现依赖。
4. 应用 authored `d0748fe`（Shell/Library、LastPlayed只读投影与tests）、`6abb8ed`（Details）、`695fc99`（重挂载/强调色），然后本轮 `95b0ad7`。设计文档 `1369f99/7cea6a3/7214dbc` 可独立纳入，不能当构建依赖或认证。
5. 对 coherent 最终树构建并运行相关检查，等第7节资源阻塞闭合才建议集成。依赖合并中可能暂时不构建，只在完整组落地后验证，不通过临时改业务绕开依赖。

回退：新集成分支为各依赖组建立清楚的本地提交，按实际落地组使用逆提交恢复；不 reset 用户 dirty，不删除原 worktree/候选/历史记录。审查分支保留原 Claude 全链加修复，不将该分支直接整体 merge 到 main。当前没有执行任何集成。

## 7. 上一轮阻塞与建议（历史保留；本轮结果见第9节）

假设：当前 Private 的短期循环高点至少部分来自密集 Test diagnostics / tree traversal / diagnostic text 更新或短时容量，而不是跨离开后仍存活的材质 owner。支持：两模式均上涨、各自 idle下降119.14/142.89MiB，显式 owner平衡且GPU committed/handles不累积。**这些只支持假设，不证明具体原因。**

最小下一步是一次限定的、同一最终候选/两个固定条目/相同20+20周期的 **低扰动采样对照**，不修改应用或视觉：Test诊断条隐藏，避免每个导航边界执行 VisualTree bitmap/stale全树遍历；外部进程和PDH流式采样，较重 owner/正确性证据只在预热后、循环末和明确idle末读取。用已有 Test `THE_ORDER_NATIVE_CAPTURE_SKIP_TREE_PROBE=1`，明确相应字段为未采集，不能伪造0；开始/结束另做完整正确性检查。没有授权在本轮突破30分钟预算，故这里不再启动它。

若热循环同状态高点不再超门槛而前后重型 probe / idle恢复吻合，支持测量扰动，需要记录有依据的测量修订而保留本次FAIL。若低扰动下仍重复累积，或idle后同状态留下持久增量，则否定“仅诊断”的解释，按新增差异定位具体路径后做小修复。无需重启已结束的 Shared/PF/XAML池调查、无限长测或框架源码探索。

目前：**隔离Test可继续；开发集成BLOCKED（资源证据）；默认启用/发布未批准。** 系统live无障碍/其他缩放/实际关机等未验证项需后续覆盖，不单独混入本次开发集成门槛。高级材质的正式发布授权与GA1适用范围不变。

## 8. 体验、证据与清理

准确隔离入口（不要裸启动 EXE）：

```powershell
pwsh -NoProfile -File 'D:/Projects/The-Order-worktrees/codex-claude-ui-review/.artifacts/claude-ui-independent-review/launch.ps1'
```

每次生成新的384-item合成Test库，显式 `THE_ORDER_NATIVE_PROFILE_KIND=Test`；使用已保存合成封面，无凭据/真实游戏。启动入口会话模式沿用 Claude Auto；用户可以在设置切 Advanced/Standard/关闭透明效果，不代表正式程序默认已启用。

审查根目录 `.artifacts/claude-ui-independent-review/`：

- 身份/保护：`original-workspaces.jsonl`、`end-workspaces.json`、`frozen-candidate-preservation.json`、两个 candidate manifests、两个 PDB 核验、`final-review.diff`。
- 构建/测试：`build-debug-final.log`、`build-release-final.log`、`tests/targeted.trx`、`tests.log`、`optical.log`。
- 真输入：`shift-click-result.json` / `shift-click-uia.txt`、`escape-result.json`、`text-ctrl-a.json`；首审截图 dirty-details/search-unsaved-prompt/exit-blocked/shift-click-final。
- 最新实际截图：`screenshots/final-library-light-narrow.jpg`、`final-details-light-narrow.jpg`（最终source）；CtrlEnter快拍单独保留但不作为可靠键盘触发成功证据。
- 最终短测：`protocol.md`、`resource-check.ps1`、`runs/accepted/identity.json`、`boundaries.jsonl`、`checks.json`、`resource-summary.json`、`resource-run.json`、`control/stage-audit.json`、`actual-x-hidden.json`、`before-exit-optical.json`、`exit.json`。
- 初次准备失效：`runs/accepted/startup-incident.json`、`resource-interrupted-identity.json`，保留，不冒充完成负载。
- 旧真实动态证据：原 Claude worktree `.artifacts/phase7-2b/claude-ui/recordings/rec-open-details.gif`、`rec-back-library.gif`、`rec-hide-restore.gif`、`rec-accent.gif`、`storyboard-real-input.gif`。为实际运行帧序列，不是mockup，但不是新最终候选视频或精确时序性能证据；没有新录制视频，不能给新修复/Shift补验背书。

最终Test库SHA256前后均 `E61C4EE02370A12554CBFF232BA099ACF7E17D292CC37F5D41BF2F63F325517E`。13:09:56 UTC final进程正常退出0；61 renderer created /61 destroyed，Settings处两stage unsubscribed、无pending retirement/图像工作。此处的旧owned位图/cache仍可留在可复用页面，与已退休optical资源分开；最终退出交由原host/image dispose流程。没有本任务存活Test进程或ETW采集器；没有触碰历史PID或无关进程。记录结束，等待资源证据阻塞的定向处理。

## 9. RC1 · 资源阻塞定向闭合（同日后续）

起始实际HEAD `df56b9f5dcd0f51eabd2c2432fb1997bdef9ab82`，`codex/claude-ui-review` tracked clean。应用源码仍为95b0ad7；WinUI/native哈希同第1节，两新进程分别核验。结束完整manifest557项哈希无差异，原候选/既有三工作区未改。起始冻结audit的“371文件”为数量笔误，保留原文/hash并另记纠正，不冒充完整身份变化。新证据：`.artifacts/claude-ui-independent-review/resource-closure/`。

**运行前协议**：`protocol.md` SHA256 `0A3FEB3F30C4745A28AFB51B0912EF4328D96248932BA5B1AB0FE1C1ED5127E9`。同一384合成payload、两个固定作品、Dark/Grid/1147×802DIP/DPI2。full与low各新进程，均Standard→Advanced，每模式3次预热→20往返→12秒静置→相同20往返→12秒静置→Settings释放。页槽4秒；没有减少操作、截图/readback、强制GC/trim/cache清空、低画质或新ETW。只有既有Test-only `THE_ORDER_NATIVE_CAPTURE_SKIP_TREE_PROBE=0/1`不同，诊断条可见、250ms文字JSON、500msfixture与命令/PDH次数全部相同，避免把隐藏诊断条后的viewport变化混入。

表中Private/GC单位MiB；峰值为外部500ms采样，静置为明确边界，末1秒中位数与该值一致。低组bitmap/stale为未采集null，绝不写成0。

| 模式 / 诊断 | 预热基线 | 段1峰值 | 静置1 | 段2峰值 | 静置2 | GC committed 基线→1→2 | 判定 |
|---|---:|---:|---:|---:|---:|---|---|
| Standard / full | 294.543 | 423.965 | 322.762 | 456.754 | 325.863 | 72.063→72.465→69.410 | FAIL：峰值2−1=32.789 |
| Advanced / full | 395.836 | 505.879 | 391.523 | 526.195 | 424.313 | 69.910→70.035→78.543 | FAIL：静置2−1=32.789 |
| Standard / low | 323.746 | 422.289 | 290.637 | 424.129 | 324.426 | 67.977→69.195→65.258 | FAIL：静置2−1=33.789 |
| Advanced / low | 393.016 | 474.844 | 411.184 | 482.563 | 383.988 | 65.262→65.391→65.391 | PASS |

**已确认及限制：**

- Standard第二段热扩张full133.992/low133.492MiB（只减0.500），第一段减30.879（23.86%）。Advanced分别减28.215（25.64%）与63.293（47.00%）。未达到运行前“≥32MiB且≥50%”因果屏障；不能把遍历当作两模式全部增长的主要解释，但不能排除Advanced部分贡献。full/low Standard两段累计托管分配71.936/53.877、73.126/55.951MiB；去掉遍历确实减少分配扰动，不能宣称产品泄漏修好。
- 低扰动Standard峰值只增1.840，最终比预热+0.680MiB，支持容量复用；静置1落到低谷，静置2回到初始区间。其heap50.288→49.281→49.340、handles1291→1259→1257，无托管堆/句柄持续保留证据。GPUCommitted162.734→185.109（+22.375）与Private同向，不能相加为账目、等同物理显存或直接命名owner。现有数据支持暂态分配/回收与容量相位差，**尚缺具体native/图形类别及250ms诊断文字布局的分离证据**。
- 两组86次页面往返含预热、各172页槽、80正式周期全部完成；仅各1页槽超过250ms（<1%）。Advanced每个可见边界一owner、Standard零、Settings零；两组87/87 renderer created/destroyed，source/frost bytes0、attached=false、retirementPending=false。图像/详情/导航pending全0，全部idle draw/revision/frost增量0，cache28.414MiB/52项。full树bitmap有界1/3/52、stale0，无fallback/窗口/DPI/身份错误。low Advanced这些所有权检查与Private屏障都通过；共同Standard屏障不能被该PASS掩盖。
- 主动静置等待均12秒，含边界读取的实际间隔14.375–16.915秒；另按既有外部数据对齐+12秒（误差≤0.255秒，`fixed-idle-window.json`，不改判据）：low Standard290.637→324.426仍差33.789，low Advanced411.184→383.988。排除读数时长不同解释low Standard失败；full Advanced首次12秒378.996与最终probe391.523仍有12.527变化，不能靠单点判准确allocator因果。
- 应用内路径为250ms OnDiagnostics→VisualEvidence递归两次VisualTree→JSON→`RuntimeState.Text`，fixture命令还重复调用。没有app样本历史List、截图/像素读回；最新Text/receipt与bounded probe计数。外部采样器/Node保存对象不能解释app Private。现在只隔离了tree，仍未隔离文字绘制/native布局。
- 原报告Advanced“idle下降142.89”区间解释不准确：真正idle-start526.375→490.734，17.109887秒下降35.641；cycle-end633.629→490.734的142.895还包括idle-start之前107.254。Standard真正idle下降119.086/16.766825秒。第4/7节旧文字和原raw保留，本节明确纠正。
- 原CSV ageSeconds列混local/UTC，分析弃用该列，用ISO UTC−identity.start与monotonic elapsed。内存数/UTC未损坏；原脚本/数据/hash不动，未来修正副本仅语法核验。系统available/commit、native heap live/committed、精确COM引用链未采集，不虚造。

**结论：BLOCKED — RC1未整体通过，非已证实产品泄漏。** 旧热端点FAIL、新full两模式FAIL、low Standard FAIL均保留；low Advanced PASS范围仅本机该候选该负载。应用代码/二进制修改0，只新增诊断/证据与维护报告；没有明确应用缺陷可以据此修复。GA1历史适用范围不变。系统live无障碍/其他缩放/关机/持久化等仍是发布前及后续边界，不借此要求无限长测。

**唯一下一步：仅Standard的Test诊断JSON文字绘制ON/OFF对照。** 独立Test-only变体保持相同250ms完整快照与收据、固定诊断条几何，关闭时固定短行，隔离`RuntimeState.Text`的大量动态文字布局；同样20+20/idle，不改视觉/图片/操作数或冻结候选。若热波动与idle屏障消失，支持此具体诊断路径；若不变，排除它，剩下共同非托管路径需native allocated/committed证据，不能再猜tree/cache。该路径来自应用代码，不是外部sampler；本轮未据假设实施“修复”。完整一组配对需约14分钟，超过本轮余量，不追加不完整短轮凑结论。

两组13.73+13.64≈27.36分钟，正常退出开销后仍<30分钟；退出0、两个新Test库hash各自前后相同。无本轮存活Test进程/sampler，无本轮ETW。完整协议/原始CSV/边界/结果/衍生表、模式计数、退出与保留核验索引见resource-closure/report.md与evidence-index.json。依旧可隔离体验，不建议整体开发集成；不得整体拿7d74e24，安全依赖次序沿用第6节。


## 10. 资源证据与开发集成门槛复核（仅离线，2026-10-08）

**未证实新的必须修复的应用资源缺陷；建议有条件开发集成，不宣告协议PASS。** 开始实际HEAD为2a0f7bb16e569bb0cf3bf1a2336046e1a1a2c4f2，分支codex/claude-ui-review，tracked clean。native源码相对95b0ad7无差异，冻结候选557文件重新核验0不匹配；WinUI/native身份沿用第1节。应用、驱动、协议、候选修改0，新负载/构建/采集器0。第2/4/7/9节历史阻塞标签和FAIL保留，其当前集成解释由本节补充。

### 10.1 FAIL的意义与局限

单位MiB。原RC1的32门槛不变；端点为边界Process.Refresh单点，峰值为外部500ms样本。

| 模式/诊断 | 预热 | 峰值1 | 静置1 | 峰值2 | 静置2 | 实际失败项/原结果 |
|---|---:|---:|---:|---:|---:|---|
| Standard/full | 294.543 | 423.965 | 322.762 | 456.754 | 325.863 | 重复峰值+32.789：FAIL |
| Advanced/full | 395.836 | 505.879 | 391.523 | 526.195 | 424.313 | 静置差+32.789：FAIL |
| Standard/low | 323.746 | 422.289 | 290.637 | 424.129 | 324.426 | 静置差+33.789：FAIL |
| Advanced/low | 393.016 | 474.844 | 411.184 | 482.563 | 383.988 | PASS，仅本机/候选/负载 |

原审查协议把Private/handles称为supporting regression screens；32MiB来自此前约5MiB波动上的保守余量，RC1沿用为保留/容量代理，没有同负载置信区间或独立产品预算依据。Private1.5GiB等另为安全停止线。超限必须保留，但不能只凭超限定性应用泄漏。

low Standard峰值只增1.840、最终相对预热+0.680；静置1先低于预热33.109，静置2回到原区间。FAIL证明两次相同逻辑状态的静置值差33.789，没有证明预热以上又持续保留同等容量。不是因为超限小就忽略，而是增长方向/重复容量/owner证据不支持单调泄漏结论。

三端点Library/Grid/Dark、1147×802 DIP/DPI2、可见激活、同384内容/cover身份/selection0、材质及owner状态、pending0一致。固定12秒等待，含读取实际14.375–16.915秒；low Standard按+12秒、末1秒中位数及+8秒至结束窗口均重现290.637→324.426，不能当单点噪声或读取时差。逻辑状态相同不保证allocator/GC/回收相位相同。

low Standard两静置间GC committed69.195→65.258（−3.938），heap49.281→49.340（+0.059），各代collection count+8，handles1259→1257。不能把Private增加归为GC保留。静置1自然下降39.434时各代collection+1，committed不变；静置2下降38.719时collections/heap/committed不变。只能确认自然回落，不建立定量GC因果。GPUCommitted+22.375不能与Private相加/相减或等同物理显存；非托管类别仍未知。

### 10.2 轨迹与实际风险

每段20个Library同状态边界分为四组各5周期，中位数如下；这是解释数据，不是新阈值。

| 模式/诊断 | 段1 | 段2 |
|---|---|---|
| Standard/full | 361.4 / 364.6 / 359.8 / 368.8 | 392.1 / 422.6 / 391.0 / 423.6 |
| Advanced/full | 432.5 / 439.1 / 467.3 / 442.1 | 454.5 / 470.0 / 489.3 / 461.3 |
| Standard/low | 388.8 / 359.2 / 360.5 / 361.2 | 360.2 / 366.1 / 363.3 / 388.1 |
| Advanced/low | 409.9 / 408.0 / 412.3 / 446.2 | 418.8 / 452.9 / 448.2 / 449.1 |

full的第二段热带真实上移，low Advanced后段也更高，不能宣称全部平坦。每段外部≥32MiB自然回落多次；静置序列分别294.543→322.762→325.863、395.836→391.523→424.313、323.746→290.637→324.426、393.016→411.184→383.988。只有两个操作段，未建立每次固定持续保留序列，支持回收/复用而非无限稳定证明。full Advanced heap49.713→62.164（+12.451）是未定位对象的真实变化，未忽略。

两组各80正式周期正常完成，已有raw/checks无owner/pending/窗口/意外fallback错误；最终均87创建/87销毁，Settings source/frost0、attached/retirementPending=false。全部idle零绘制/修订/磨砂工作，cache28.414MiB/52项，handles终点未累积。full树/stale检查通过；low bitmap/stale未采集不能写0。退出0，Test库hash不变。全程Private最大526.195MiB、GPUCommitted最大253.395MiB，低于已采停止线；系统available/commit未采集。未发现摘要漏掉的新应用资源错误或功能退化；这些观测不覆盖所有框架/COM存活分配。

low Advanced PASS因此支持本机该候选、固定内容两段操作/静置、两处Clear/Continuous的高级材质路径可接受，不抵消Standard FAIL，不扩大到全部模式/环境或发布。

### 10.3 诊断文字不再作为必做实验

Startup/Composition.cs:17–21将DIAGNOSTICS=1作为显式开关，默认false；MainWindow.xaml.cs:71–76开250ms计时器，218–280同步序列化最新状态并覆盖Text，没有该路径无限历史List/队列或订阅残留证据。计时器本身没有Test-profile硬限制；完整fixture另受Diagnostics+Test+VISUAL_TEST限制。**当前launch.ps1确实启用DIAGNOSTICS=1，所以隔离体验运行此路径；普通无显式开关的启动不会启用，尚未另测其性能。** 同一Release二进制，不是另一个诊断构建。

fixtureBusy限制单在途命令，收据写盘，StopAsync/DisposeVisualSystem停止并解除计时器/订阅。Ctrl+Shift+D仅隐藏面板，不构成文字更新OFF。仅有频繁分配可能性，没有“持续保留”因果证据；外部采集器内存也不能解释app Private。full/low没有去掉该文字，只去树遍历，未达到原因果幅度条件。

未来ON/OFF若降低波动，只支持诊断开销/测试观测改善，不代表修好产品泄漏；若不变，只排除文字作为主要原因，不证明产品泄漏。两种结果本身不改变本轮有限集成建议，故撤销第9节其作为“唯一必做下一步”的要求，保留历史记录。本轮不需要新实验。

### 10.4 分类、例外与集成决定

| 分类 | 证据判断 | 决策 |
|---|---|---|
| A 必须修复缺陷 | 没有新资源缺陷被证明；95b0ad7修复保留 | 无新的应用修复任务 |
| B 实质阻塞未知 | 未见owner/pending/句柄持续累积、实际预算超限或功能退化的新风险证据 | 不因native归因未知单独阻止有限集成；协议例外批准仍是约束 |
| C 非阻塞限制 | 热带变化、full Advanced heap变化、非托管类别未闭合、默认无诊断运行/其他环境未测 | 后续相关开发/发布前验证，不作无限安全声明 |
| D 协议解释 | 32MiB代理与产品预算不同，单点/相位问题；RC1明文要求满足才READY | 保留FAIL，显式批准有限例外 |

RC1最后一段明确：**READY FOR DEVELOPMENT INTEGRATION only when the new retained/capacity/ownership/correctness requirements are supported**。该规则绑定当前流程，因此不能自行改为无条件READY。建议用户批准：旧热端点与RC1三项Private代理FAIL保留，但豁免其作为**本次有限开发集成**的硬性前置；不豁免实际owner释放、pending、背景/交互/退出正确性、安全降级或资源压力停止线。

范围为当前95b0ad7源码、锁定依赖、用户认可两处材质、隔离Test的可回退开发集成，不扩材质范围、不默认启用、不访问Production或发布。集成依赖/回退按第6节，不能整体拿7d74e24；新集成树仍做正常受影响检查，出现具体新owner/持续保留/错误则处理相关改动。例外未获用户批准前不执行合并，不自称READY。本轮合并0。

完整复算、GC/时序数据、证据hash及索引：.artifacts/claude-ui-independent-review/resource-threshold-review/report.md、offline-results.json、input-hashes.json、recompute.mjs。原RC1协议/14个raw文件内容未变。此结论是另一个有范围的集成风险建议，不改GA1/旧FAIL，不授予默认启用或正式发布资格。

## 11. 用户批准后的本地开发集成（2026-10-08）

**本地开发集成完成；高级材质仍默认关闭、隔离 Test。** 只豁免当前 Private 代理 FAIL 的集成硬性前置，不豁免应用 owner 释放、pending、交互、退出、安全降级与数据保护。第4/9/10节历史 FAIL 和未知非托管归因均保留；没有重跑资源长测、修改阈值、修好“未知泄漏”或宣布 RC1 整体 PASS。

### 11.1 分支、依赖和保护

新工作区 `D:/Projects/The-Order-worktrees/codex-claude-ui-integration`，分支 `codex/claude-ui-integration`，从干净的主开发提交 `14a96a1` 建立；原四个工作区的 HEAD、分支、状态及 dirty 文件 SHA256 前后完全一致。Claude candidate-3 的371个文件、最终审查候选557个文件重验均0不匹配。没有 reset/clean/stash、覆盖原修改、正式替换或 push。

先 fast-forward 新分支到已提交视觉依赖 `ad59c6e`。实际 `ad59c6e..7d74e24` 为41文件差异，选择其中16个真实运行依赖，形成 `3da3997`：同设备 Gallery D2D/DirectWrite/WIC 源、屏幕可见时提前释放、managed/native 光学适配、tree probe 及相应回归。剔除旧 Toolbar 钩子，不引入两种旧捕获/图像调查工程、GPU统计与资源实验驱动。没有把历史 baseline copy 中的 dirty 文档视为 Claude 新功能。

| 来源 | 本地集成 | 内容 |
|---|---|---|
| 选择性 baseline 依赖 | `3da3997` | 16文件，不引入复制提交 |
| `f18d609` | `33d2183` | 材质 API 前置，后续批准方案替换外观 |
| `1369f99` | `c8f014a` | 设计文档 |
| `09465c2` | `558a2d0` | Clear / Continuous |
| `7cea6a3` | `49bce9b` | 已认可 rev2 设计 |
| `d0748fe` | `9cea03f` | Shell / Library / 只读 LastPlayed |
| `6abb8ed` | `454b5e7` | Details |
| `695fc99` | `325a089` | 重挂载、Windows accent |
| `7214dbc` | `d98fe83` | Claude 交付记录 |
| `95b0ad7` | `1acae7f` | 第2节五项独立修复 |
| 本轮入口 | `aa7dd7f` | 默认 Standard、安静 Test 体验与独立诊断 |

Claude 与修复提交用 `cherry-pick -x` 保存来源。d0748fe 的三个冲突（MainWindow.Visual.cs、MainWindow.xaml.cs、LibraryPage.xaml）来自刻意省略的旧 Toolbar 上下文；逐块核对后采用该真正 UI 提交的最终内容，没有改数据事务/业务合同。`git merge-base --is-ancestor 7d74e24 HEAD` 返回1，复制提交不是祖先。

集成到 `1acae7f` 后，生产应用/构建/测试源码与95b0ad7一致；差异仅是未集成的18个历史调查文件。随后 `aa7dd7f` 只改变两处应用配置：默认 Standard；合成 Test cover 传输不再绑定 Diagnostics，其 Test manifest 与路径守卫仍完整。新增受限 profile-helper 与两个入口、fixture生成目录排除。没有改 shader/分辨率/C1、cache/虚拟化、业务语义或 profile/writer契约。

### 11.2 当前候选与验证

Release：本工作区 `.artifacts/local-development-integration/candidate-release/win-x64`，源码 `aa7dd7f673cb7f38fc3a69a377081c43d4fd0816`，557文件清单。WinUI.dll SHA256 `5E8C908D213623DB9ECE740005B3CA312D71AD433AD59451AAE2882A0F7A7775`，OpticalMaterial.dll `62A86D2B58A34752BBDF89415F60F315F91F4FCAAE56C8279B459A3443A5D110`。新的编译路径产生新身份，不冒充95b0ad7冻结二进制；PDB中43个 tracked WinUI 文档均与当前源码匹配。33个生成文件路径在构建后不可匹配，不声称全部102个PDB文档均已验证。

| 检查 | 结果 / 范围 |
|---|---|
| 锁定 restore、Debug / Release publish | 通过，SDK/依赖不升级，输出在新工作区 |
| 相关 managed 回归 | 294/294，0失败/0跳过；Presentation、Session、Launch、Material、LensOptics、SafeStorage、Preference、ImageRequest |
| native optical | 通过；132 exact readbacks、160动态 scene，C1 bit-exact、Clear/Continuous、纹理复用、detach失败重试与owner平衡 |
| 普通入口（DIAGNOSTICS=0） | 真实点击：默认标准、手动高级、继续游玩→Details→书架；不显示/更新250ms JSON诊断；合成图片正常；托盘菜单正常退出0 |
| 独立诊断入口 | 默认Standard；高级实际运行Continuous/Clear；两次stage audit通过过期背景、安全/HC注入、异步离开；离开至Settings后source/frost为0，9创建/9销毁，retirementPending=false；正常退出0 |
| 数据保护 | 两个新Test库各自前后hash一致，无凭据复制/Production访问/真实游戏 |
| 既有交互 | 复用第3节最终Shift+单击、退出取消/草稿等源码一致证据；没有冒称本轮又做全部实机输入 |

普通退出通过验证当前 PID+creation time+候选路径后投递托盘菜单通知，再实际点击“退出 The Order”；不是人工在系统托盘图标上右键的补验。诊断退出走原受限 `exit` fixture/App.RequestExitAsync。没有ETW采集器、新资源协议或长负载；两次启动仅集成/入口短验，不构成长期资源验收。

截图来自新普通入口，Library/Details两处材质保持认可方向；没有生成图/设计mockup替代实机。computer-use最初返回旧会话窗口，未据此操作或取作证据；重置工具会话并核对新PID/窗口后才操作。诊断短验最初的generation字符串比较因PowerShell自动解析DateTime而拒绝，改为同等UTC ticks核对后完成；仅修正本轮忽略目录中的短验辅助脚本，不修改冻结驱动/历史证据。

### 11.3 入口、回退与剩余限制

普通：`& 'D:/Projects/The-Order-worktrees/codex-claude-ui-integration/native/scripts/run-integrated-ui.ps1'`。独立诊断：同目录 `run-integrated-ui-diagnostics.ps1`。每次新建 `.artifacts/local-development-integration/runs/<unique>/profile`，高级默认OFF，设置仅本次会话有效。×隐藏到托盘，真正退出用托盘；源目录与prepared Test payload/10k原始合成covers均在新worktree内，无真实资料。

回退基点：原主开发 `14a96a1` 完整保留；原视觉已提交 `ad59c6e` 完整保留；新分支选择性前置 `3da3997`、独立修复 `1acae7f` 和完整应用集成 `aa7dd7f` 可追溯。需要撤销时仅在新集成分支按逆序 revert 对应新增组，勿 cherry-pick 7d74e24、勿 reset/覆盖四个原工作区；本轮未执行回退。未合入主开发分支，用户可以在此新本地开发分支继续开发。

历史资源FAIL与未知归因、其他机器/真正系统accent/HC/DPI和文字缩放/关机注销/真实游戏、设置持久化仍属后续及发布前边界；本轮未执行全1792-suite或新增资源长测，未宣称无限时长稳定。若未来新改动出现具体owner/交互/退出错误再定向检查，有限资源例外不能豁免这些错误。

证据根：新工作区 `.artifacts/local-development-integration/report.md`、candidate-manifest.json、pdb-integrated.json、source-hashes.json、preservation.json、integration-commits.txt、diff-from-review.patch、integration-source.patch、build-debug.log、build-release.log、tests/targeted.trx、optical.log、runs/*/identity.json/exit.json。历史证据仍位于 `D:/Projects/The-Order-worktrees/codex-claude-ui-review/.artifacts/claude-ui-independent-review/`，以及原Claude/GPT视觉目录，不虚构为当前相对路径中的副本。

## 12. Phase 7.2 最终集成验收与冻结（2026-10-08）

**PASS — DEVELOPMENT INTEGRATION ACCEPTANCE / TEST CANDIDATE FROZEN。** 本结论是有限范围开发候选验收，不改变第4/9/10节资源协议FAIL，不授权高级默认启用、Production、正式发布或Phase8。用户批准的有限Private代理指标例外继续有效，其余正确性、释放、安全和数据要求没有豁免。

开始时分支 `codex/claude-ui-integration`、HEAD `8c4ddab`、tracked clean。完整Release套件首轮1790/1792，两项失败分别是过期oracle源码身份与真实识别队列publication竞态。`WhenIdleAsync` 原先早于同步最终结果观察者完成，调用方可能flush空或旧review；修复提交 **14d8ede** 在worker及enqueue异常路径先发布再完成pending。新增阻塞观察者测试在旧代码RED、修复GREEN。现有Electron生成器实际重跑220例，全部cases/expected一致，只更新metadata-matching.ts源码hash；不改弱测试/跳过、不安装升级依赖。291项识别相关回归和最终完整 **1793/1793（0失败/0跳过）** 通过。修复后Debug/Release重新发布到新目录，没有覆盖历史候选。

冻结应用源码 `14d8ede1b2240f30b153aee9f919bbd123385f48`；候选 `.artifacts/local-development-integration/frozen-phase7-2-final/win-x64`，557文件。WinUI.dll SHA256 `4B85197D3E03E875D8317A1E16468A7DCF9D644AB218CA136F571A24ADF34AA8`；OpticalMaterial.dll仍为 `62A86D2B58A34752BBDF89415F60F315F91F4FCAAE56C8279B459A3443A5D110`，132 exact GPU readbacks/160动态scene证据依身份复用。当前manifest SHA256 `73CEBA940EA3E0AE505E2E0A95B4A66C2B9228302FD256C2CFCFC82A4AC4B377`。后续文档提交不改变已验证二进制的应用sourceCommit。

普通最终入口实际补验：搜索09987及文本Ctrl+A不进入全选、收藏筛选128/384、单击Details、选择模式/Ctrl+A/Esc、搜索和List返回恢复；草稿在实际×隐藏、恢复及托盘退出取消后保留，取消后能保存。退出提示与托盘菜单实际点击，正常退出0。系统托盘区域不能直接点击时，以核验本次PID+generation+HWND的Test自身通知回调打开菜单；不冒称物理图标右键。合成库只改变一个名称/custom标记，其余383条目与目标其余字段完全一致，不访问Production。

最终诊断短验：请求620DIP窗口，实际客户宽607DIP、raster scale2、截图609×795（工具缩放像素，不当成物理像素）；Light/Dark，Library Continuous/Details Clear均实际Advanced。18.698秒同状态静置 draws4→4、frost2→2、bitmap52→52，imageOutstanding/ActiveDecodes/PendingOperations/PendingWaiters均0，导航/详情pending=false，stale=0。材质开关、隐藏/恢复与离开到Settings后stage source/frostBytes=0、pending retirement=false；native8创建/8销毁。stale/safety/HC注入/lateCompletion audit全通过，库hash前后相同，退出0。不是新资源趋势PASS，不推断GPU物理存活字节。订阅highContrast的既有0x80070490 warning仍由原fallback notification route处理，currentRequiredStateKnown=true、materialFault=false；不是本轮修好该平台订阅能力。

普通入口关闭250ms诊断；高级默认OFF，模式仍会话有效。窄窗第一次鼠标drag没有实际resize，其命名带narrow的截图明确不作窄窗PASS证据；后续Test resize实际尺寸与probe闭合。复用既有最终Shift+click实机证据，不冒称本轮重做。实际OS HC/强调色切换、其他DPI/文字缩放、关机注销及真实游戏继续未验证；没有新录像，不把旧录像作为新发布二进制验证。

直接入口见DEVELOPMENT：`.artifacts/phase7-2-final-acceptance/launch-frozen.ps1`，普通与`-Diagnostics`入口启动前核验557文件。回退点 `8c4ddab` / `aa7dd7f` 和旧候选副本保留。四原工作区分支/HEAD/status/dirty哈希完全一致，Claude371/审查557/旧集成557/最终557均0不匹配，7d74e24仍不是祖先。本任务两个Test均正常退出，无ETW，无剩余自建实例。完整证据在新工作区 `.artifacts/phase7-2-final-acceptance/`，首轮失败与红测试保留。
