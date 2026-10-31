# Phase 7.2A：独立视觉审阅

## Phase 7.1 closure / Phase 7.2A Round 3（2026-10-07）

Phase 7.1 已正式 **PASS — PHASE 7.1 MEMORY MAJOR CLOSED / B0/M0/m0 / PASS FOR PHASE 7.2**。主工作区 closure 见 `D:/Projects/The-Order/docs/ai/PHASE7_PERFORMANCE_AND_RELIABILITY.md`；原 memory 诊断源码不复制进视觉分支。anonymous owner、global bitmap/GPU/COM、约3MiB residual、storage scaling、Level3独立验证均为 OBS，不再阻塞视觉工作。

Round 3 仅修复原六项：移除 Shell enclosing glass；窄窗零选择隐藏 bulk、范围/排序进入筛选、低频命令 overflow；Details 首屏去重标题并上移状态，无安装显示 compact next-action；Filter body 滚动而 footer 固定（条件仍即时应用，完成只关闭）；Dark brand semantic control resources 保持灰绿，系统 focus/high-contrast 不改；共享 owned Acrylic fallback 覆盖 Navigation/Toolbar/Details 与公开 FlyoutPresenter/MenuFlyoutPresenter，ContentDialog 用实色。OS caption/system menu 不在自有材质承诺内。

计时使用固定 240DIP 等宽数字区域；Test-only visual fixture 增加 bounded screenshot/layout/keyboard evidence 入口。Library 默认虚拟化与 inactive ItemsSource=null 保留；未修改 CoverPresenter/cache/业务服务/Session accounting。Level3 Gallery 保留 Test-only，未接入 Shell/Toolbar/Details。

本源码 checkpoint 后重新 build/publish，再补齐实际 DPI、键盘、text scale、motion/transparency 与六项 targeted evidence；完成后更新此处结果。新候选绑定与原始证据保存在 `.artifacts/phase7-2a-round3/`。旧轮 checkpoint/MVID 的证据保留为历史，不冒称本轮 final。视觉 Gate 只重审六项，仍须用户实际体验确认，不自动 merge/push/Productionize。


本候选用于艺术方向审阅，不代表用户已经批准方向、memory Gate 已通过或折射已适合生产。所有工作位于 `C:/Users/USER/.codex/worktrees/winui-phase7-visual/The-Order`，分支 `codex/winui-phase7-visual`；未合并、未推送、未替换日用程序。

## 基线与方向

已提交基线 `14a96a1fc793b7fba231ab3b8fd1da607c3e86c5`。原工作区的未提交 memory observation / fixed-replay / plateau / checker 修改保留在原处，没有复制。原始冻结证据为 `.artifacts/phase7-2a/original-freeze-before.json`，最终比对另附运行回执。Phase 7.1 memory MAJOR 1 继续开放，本轮不运行 2h soak。

三名子智能体分别负责书架审计与实现、详情及辅助页面的 UX、材质原型与资源生命周期。Root 负责方向、公共 token、Shell、材质权限、集成及最终运行证据。Root 推荐 **B：轻盈悬浮收藏库**，采用共享浮动导航、安静的卡片与工具表面，借用 A 的紧凑操作密度。A 为紧凑一体式导航与工具栏；两种试作均实际运行，首轮截图明确标作 round1。A 的表面更紧，但侧栏/内容关系较平；B 更清楚区分导航、工具与作品。

原首屏最大的具体问题是空闲 Session 区、四行筛选/操作控件与小封面争夺高度。旧 Details 以通用“作品详情”和表单起手。修订把首屏让给封面、作品名称和启动位置，把低频筛选收进 Flyout；原查询、编辑、启动与保存操作仍走现有服务。

## 视觉系统与页面

| 项目 | 当前实现 |
|---|---|
| 字体 | Segoe UI Variable 页面标题 26 / 作品标题28；正文14、次级13、元信息12；计时Cascadia Mono12。中文/日文由系统字体回退，未加入字体依赖。 |
| 色彩 | 浅色背景F2F3EF、正文263230、次级53635F、强调32685E；深色背景1B2324、正文E5EBE7、次级B4C2BC、强调88BBAE。警告/错误/成功各独立语义色，不用 fill brush 作正文。 |
| 间距与密度 | 8/16/24基本节奏；页面28/20，书架B32/24；36DIP普通操作高度。折叠筛选不再留下空行间距。 |
| 圆角 | 按钮/输入6、内容卡8、工具/导航12；实验透镜20。没有嵌套的多层 blur。 |
| Shell | 保留真实系统标题栏、caption buttons、默认窗口行为；现有品牌图标与中文功能导航，设置置底。Session置底，空闲状态仅一行。 |
| Toolbar | 一处共享 surface，搜索、范围、排序、视图与筛选；较窄宽度重排原控件。选择与批量操作始终保留明确状态。 |
| Grid | 188×290卡片，204DIP封面槽，标题两行省略且tooltip完整；状态/收藏一行。原默认 ItemsWrapGrid、回收和 inactive ItemsSource=null 保留。 |
| List | 小封面、标题/标签、状态、评分、安装数对齐；窄宽度隐藏次要列；保留默认 ItemsStackPanel 和选择行为。 |
| Details | 156×208同一个CoverPresenter，作品标题优先；安装与启动在首屏，个人编辑/本地记录/目录操作分区，保存状态与动作置于固定底栏。 |
| 辅助页面 | Online/Review共享toolbar与分区；窄窗重排原双栏控件；Settings按外观/目录服务/资料目录分区；Import/Installations使用相同文字与surface。 |
| 会话 | 图标来自现有Session snapshot事实；原SessionPresentation文字与确认时长保持。个人书架“在玩”状态不被用作真实进程状态。Test状态展示明确标作合成，不创建会话或增加count/time。 |
| Dialog/系统状态 | 保留ContentDialog原键盘行为与三种草稿选择，统一标题/圆角；空库与zero-results保持不同事实，错误消息不被loading装饰遮蔽。 |
| Motion | 页面140ms透明度过渡，快速导航停止前一动画；Reduced Motion关闭过渡，既有状态/按钮立即变化。没有逐卡入场动画、周期GC或空闲连续动画。 |

`DesignTokens.xaml` 把基础色/间距/字阶映射到语义 brush 和 component styles；原 WinUI control styles 通过 BasedOn 保留 focus、hover、pressed、disabled 状态。高对比度使用系统 Window/WindowText/Highlight 色，并强制稳定实色。重点色不能独立承担状态含义。

## 两轮运行修订

首轮 A/B 与 Details 截图保留了真实缺陷，不被标成 final。第二轮处理了四个问题：

1. 材质检查不再遍历系统 lazy resources：只解析自己的 Light/Dark 四个画刷，解决浅色原生控件文字发白与过重的初始化。
2. 工具栏重排阈值900→780DIP，移除折叠行和空的 internal row spacing；书架viewport由首轮B约220DIP恢复到335.5DIP，旧版相同窗口约281DIP。
3. Details消除hero空行，安装与启动同行，使启动位置、原因与动作在常规首屏可见。
4. 新Test实例忽略旧控制指令；材质owner值未变时不再重建透镜/Palette，实色字幕不声称“真实像素位移”。

## 材质边界与真实折射

当前视觉候选的常规页面为 **Level 1 Solid / Level 2 Acrylic**。两个共享Acrylic token按真实系统透明效果、High Contrast、Composition能力、Energy Saver及remote状态切换；未知能力保守实色。没接入Mica backdrop，因此不声称Mica已实现。卡片不持有高级折射实例，没有逐卡 blur/shader owner。

实验室为 **Level 3几何折射原型**：一个实时 `CompositionVisualSurface` 采样应用内部棋盘格、细线与色块；18个有界rounded clip采样带使用magnification/translation映射。前景文字和图标不属于SourceVisual。背景位置0与70的实际运行截图显示采样内容及边缘线发生变化，字体不被折射。不是截图shader、桌面capture、完整物理光学或Level4 production material。

LiquidGlassWinUI经实际研究，MIT，上游提交647fd60c3ded87dc2a7472f813ec86a265529509；其SDK2.2.0/native IAT private hook机制不适合当前SDK2.5.1。没有复制其代码、增加依赖或引入私有hook。[上游README](https://github.com/luckyelysia/LiquidGlassWinUI#installation)、[许可证](https://raw.githubusercontent.com/luckyelysia/LiquidGlassWinUI/main/LICENSE)、[公开CompositionVisualSurface](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionvisualsurface)。

本宿主 `AccessibilitySettings.HighContrastChanged` 注册返回0x80070490，getter仍可成功读取真实状态。事件注册失败单独记录notification warning；保留已有owner refresh复核，MaterialPolicy/Gallery没有新增计时器，未知getter仍降级。独立 Test 命令入口有500ms timer；它在显式Test+diagnostics+VISUAL_TEST条件下创建，退出时停止并撤销Tick。不能把这个事件缺口写成已经解决的Windows兼容性问题。

Gallery active最多Surface1/Brush18/Visual19/Clip18/Geometry18；实色与Unload调用Dispose并清空owner引用，短循环中owner计数归零，卸载撤销可用事件，最终owner Dispose。Lens的native Dispose异常被捕获；`CleanupFailures=0`仅表示事件撤销未记录失败，不能证明全部GPU资源释放。283个背景XAML元素有界；Gallery没有owned device、timer或perpetual animation。`FiniteAnimationWithinRequestedWindow`只是请求时间窗指标；另有一次有限动画的四张实际采样帧，不能当作GPU状态测量。真实device-loss、remote session和battery transition仍需专门验收。

## 证据与未验证边界

严格before/after使用 `baseline-library-light/dark`、`baseline-details-light` 与 `review-final-library-light/dark`、`review-final-details-light`：同一24项原创合成书架、同一cover目录、相同1149×804截图尺寸、200%实际DPI、对应主题及相同读取完成提示。素材含中/日/英文、长短标题、横封面、缺封面与缺metadata；不使用真实credential或游戏。Test库与backup最终SHA256均为 `F857B332CB4598D7FB6B61BD1D66B8E3537D700C19BD947102CF0384583DC3D1`，与准备阶段一致。每份证据从UIA读取带候选MVID的即时runtime，断言requested state后保存原始截图与manifest；拒绝过期身份及覆盖旧证据。基线没有后来新增的visual.width/DPI诊断字段，尺寸由原截图核对，系统设置及A/B的实际Root scale另有证据；不把不存在的基线字段写成已断言。

运行证据在 `.artifacts/phase7-2a/evidence/`，各修订代次保留独立MVID。`review-final-*`为最后补齐会话图标后的构建；`ready-*`为上一修订的实际DPI/状态/响应式等证据，不能全部称作最终构建。最终构建、full native regression、resource loop、候选身份和original freeze verification回执在同目录上层 artifacts。被其他窗口遮挡的 `final-gallery-solid`、裁切的 `ready-library-large-1600`、未实际最大化的 `ready-library-large-maximized` 不作为对应验收证据；真实大窗使用 `ready-library-large-verified`。早期启动/主题/诊断失败均保留，不被改写为PASS。

Library Light实际核验100%/150%/200%DPI，Root.XamlRoot.RasterizationScale分别1/1.5/2；系统已恢复200%。没有将200%图片缩放冒充其他DPI。其他页面在100/150%、独立文字缩放、Narrator实读、完整Tab顺序/焦点恢复、真实High Contrast/透明效果/系统Reduced Motion切换、GPU device-loss、remote/battery transition仍未验证。Reduced Motion截图使用Test override，系统motionAllowed原值仍为true。SessionStates是现有presentation映射的显式Test fixture，真实session.id为空，不是游戏生命周期验收。24项库也不证明10k可视化性能或长期memory plateau。

唯一需要用户决定的艺术方向：是否接受B的导航/作品/工具关系。候选仅等待视觉审阅与原memory Gate，尚不合并。

## 会话、系统状态与辅助功能的实际覆盖

Shell和合成展示共用同一事实到glyph/semantic brush映射，时长仍由SessionPresentation提供。Active为播放图标/Success，Paused为暂停/Warning，TrackingLimited为警示三角/Warning，RecoveryPending为恢复箭头/Accent，Incomplete为未完成警示/Warning，Completed为勾选/Success。High Contrast使用系统WindowText，继续靠文字和glyph区分。没有增加Session、count、time或history。

ContentDialog的RequestedTheme跟随当前页面，修复了浅色页面弹出深色Dialog的实际缺陷；八个既有Dialog只增加主题属性，按钮顺序与业务handler保留。实际键盘核验包括Flyout中的Tab、Esc关闭筛选、Esc取消草稿离开Dialog并保留未保存内容、取消编辑恢复dirty=false，以及系统最大化/还原。中文UIA名称可读，但尚未做Narrator实读与完整焦点对比度验收。

空库来自独立Native Test profile；zero-results来自24项库的无结果查询；读取失败使用既有受控LoadFailure，显示Error和“尚未加载”，重试回到24项，未改写原文件。只读使用显式raw synthetic兼容fixture：同24项中仅一条Session改为未来native.version=2，既有codec进入ValidReadOnly，编辑/导入disabled。这不是authoritative Native store的只读入口；另一次writer lease冲突正确拒绝整个Native启动，不能说成只读页面。

多安装fixture只扩展一条合成作品；两份199字节非MZ文本文件不是可执行游戏。实际选择A/B并更新路径上下文，没有确认/启动游戏。个人/metadata各12 tags及462字长note存在于隔离变体，但长note滚动、极长warning/文字放大、辅助页窄窗未完整运行验收。Online与Review截图为初始空上下文，不覆盖真实API结果、冲突列表或手动关联全过程。

## 截图索引与前后比较

下表的路径相对于 `.artifacts/phase7-2a/evidence/`，每个目录有原始window-0.jpg、UIA文本与manifest SHA256。

| 目的 | 场景 |
|---|---|
| 严格浅色Library前后 | baseline-library-light → review-final-library-light |
| 严格深色Library前后 | baseline-library-dark → review-final-library-dark |
| 同一作品Details前后 | baseline-details-light → review-final-details-light |
| 真实A/B | direction-a-round1-library-light / direction-b-round1-library-light |
| 第二轮修订 | round2-library-light / round2-details-light；主题缺陷round2-dialog-theme-defect |
| 最终List | review-final-library-list-light |
| 筛选与草稿Dialog | review-library-filter-flyout / review-dialog-light / review-dialog-escape-returns-draft（上一修订） |
| 467DIP窄窗 | ready-library-narrow-480 / ready-details-narrow-480（上一修订） |
| 实际DPI | ready-library-dpi100 / ready-library-dpi150；200%为最终常规页 |
| 实际最大化 | ready-library-large-verified，1440×829.5DIP |
| 六种Session视觉 | review-final-session-states-top / review-final-session-states-terminal，最后一张可同时比较Incomplete/Completed |
| 实时折射移动 | review-final-gallery-live-0 / review-final-gallery-live-70 |
| 实色降级 | review-final-gallery-solid |
| 有限动画实际帧 | ready-gallery-animation-0 / 1 / 2 / 3（上一修订）；Reduced Motion为ready-gallery-reduced-motion |
| Settings/Online/Review | ready-settings-light / ready-online-light / ready-recognition-light（上一修订） |
| 空库/无结果/错误/只读 | ready-library-empty / ready-library-zero-results / ready-library-read-error / ready-library-read-only-future-session（上一修订） |
| 缺封面/缺metadata | ready-details-no-cover / ready-details-missing-metadata（上一修订）；跨详情返回后没有显示前一作品封面 |
| 多安装上下文 | ready-details-two-installations / ready-installation-selector-light / ready-details-installation-b（上一修订） |

基线可显示五个较小封面，新方向在同一宽度显示四个较大封面；这是书架密度的明确取舍，不把“首屏作品数更多”作为结论。Details首屏现在能看到安装选择、启动按钮与不可启动原因；基线相同窗口中动作被挤出首屏。Test诊断footer仍在截图中，不能当作未来生产UI的一部分。

## 修改范围、候选身份与启动

最后发布目录为 `.artifacts/phase7-2a/review/win-x64`，实际运行MVID `1c0fc641-165f-4a8a-a564-c685d739cbac`。HEAD仍为14a96a1，新增视觉源码未提交；可复核源码/二进制SHA在 `candidate-identity.json`，没有“memory leak fixed”checkpoint。

WinUI DLL SHA256：`21119F8BE7F2402501D1AFB8A255093A6A545B3C8A92BD71938FCD8AF5918C91`。身份清单记录27份修改/新增源码，186份受保护源码的规范化内容与HEAD一致；71份证据manifest及原图/UIA文件SHA复核通过。

| 文件组 | 内容 |
|---|---|
| Resources/DesignTokens.xaml | 主题、语义色、字阶、间距、圆角和保留原生默认control状态的style |
| MainWindow.xaml/.cs、MainWindow.Visual.cs、App.xaml.cs | Shell/Session视觉、响应式布局、有限motion、Test展示与材质生命周期 |
| Views/LibraryPage.xaml/.cs | Grid/List、筛选Flyout、toolbar响应式与选择上下文 |
| Views/DetailsPage.xaml/.cs | hero、启动区、编辑/记录分区及窄窗重排 |
| Views/OnlinePage、RecognitionPage、SettingsPage、ImportPage、InstallationsPage | 文字/布局/surface/主题一致性 |
| Materials/* | 公共能力检查与Test-only内部实时采样原型 |
| Assets/Brand.png、WinUI.csproj | 复用已有品牌资产并声明正常构建/发布复制 |
| native/scripts/run-visual-review.ps1、visual-command.ps1 | 固定隔离Test启动与有界控制；启动后恢复调用者环境 |
| BRIEF.md、DEVELOPMENT.md、本文件 | 原位置说明当前源码与证据，不把视觉设计写成已发布功能 |

Domain/Application/Infrastructure/Platform/SessionPresentation/CoverPresenter/image cache/decode/事务/凭据格式/共享模型/Electron均无语义diff。锁文件及Electron oracle源文件的物理EOL差异在inventory中单列，Git content diff为空；没有升级包、改oracle/test期望或写原工作区。

直接运行隔离启动入口（PowerShell 7），不要直接双击候选EXE：

```powershell
& 'C:/Users/USER/.codex/worktrees/winui-phase7-visual/The-Order/native/scripts/run-visual-review.ps1' -Candidate review -Direction B
```

也可双击 `.artifacts/phase7-2a/Launch-Visual-B.cmd`，它只调用同一隔离启动脚本。图文审阅入口是 `.artifacts/phase7-2a/visual-review.html`，直接显示原图及对应manifest链接；不是mockup或改图。

同一实例中使用设置页切换Light/Dark与Grid/List，侧栏“材质实验室 · Test”可操作实时折射开关/背景/透镜；“会话状态 · Test”查看六种明确标为合成的状态。先用“退出 The Order”正常退出，再用Direction A比较紧凑试作。`-TestProfile empty/installations/readonly`分别打开隔离状态变体；readonly为raw兼容fixture，不提供高级Test控制。运行入口固定Test目录、没有Production授权参数；关闭按钮仍执行已有关闭到托盘行为。

## 验证与待审边界

最终构建和回归的实际数字见本节末尾与 `validation-summary.json`。所有失败首次日志与修正后日志保留。早期完整native run因独立SDK/Electron路径与oracle EOL环境失败24项；修正环境后1763/0/0，未改测试期望。XAML首次初始化失败、白色控件及浅色Dialog缺陷均有前后证据，不以最后PASS抹去过程。

| 验证 | 实际结果与证据 |
|---|---|
| 最后Debug XAML/C# build | PASS，0 warning / 0 error，15.36s；tools/review-final-debug.log |
| 最后Release publish | PASS，独立review/win-x64；tools/review-final-publish.log |
| Targeted Presentation | 70 PASS / 0 failed / 0 skipped（Debug）；test-results/visual-targeted.trx |
| 最终完整native suite | **1763 PASS / 0 failed / 0 skipped（Release，3m46s）**；test-results/visual-native-review-final.trx；包含合成Electron DPAPI compatibility用例，未跑Electron全量桌面/正式credential验收 |
| 实际UI | Light/Dark、Grid/List、Details、六状态、Dialog/Flyout、空库/读取故障/只读、多安装上下文、467DIP/实际最大化、Library 100/150/200%DPI；覆盖代次见截图索引 |
| 资源短循环 | 5预热＋25测量，owner/queue/handle检查PASS；Private增长47.80MiB，**不是memory PASS** |
| 退出 | 使用既有RequestExit正常退出；退出前Outstanding/ActiveDecodes/PendingOperations/PendingWaiters=0，Gallery owner counts/events=0，dirty=false、session.id=null，随后无WinUI进程；final-runtime-before-exit.json |
| 文件/隔离 | 原主工作区627文件及Git状态不变；Test library及backup hash不变；无Production授权、真实密钥/游戏操作或程序替换；未做全系统文件访问trace |
| 基础维护检查 | git diff --check PASS，启动脚本语法与调用者环境恢复PASS；未新增依赖 |

资源循环按每次相同动作核验：live1/18/19/18/18，solid0/0/0/0/0，Unload events0，image outstanding/decodes/pending operations/waiters0；重复同状态probe不重建lens。上一修订5次预热＋25测量，handles1057→1052，范围1052–1059，Private +15,876,096 bytes（15.14MiB）。最终修订独立复测同样5＋25轮，handles1074→1066，范围1066–1075，Private **+50,118,656 bytes（47.80MiB）**，保留 `resource-loop-final.jsonl` 和 `resource-loop-final-result.json`，未覆盖旧数据。句柄/owned计数检查PASS不表示内存检查PASS；增长未归因，不证明native/GPU全部释放或memory plateau，不能排除视觉候选的额外native capacity/retention。

剩余视觉问题集中在：深色原生accent控件仍显示系统cyan，与teal token尚未完全统一；深色品牌图标对比偏低；Level3透镜采样带边界明显、固定图标字形仍需打磨，不能当作正式Liquid Glass；大文字尺度和复杂辅助页仍缺人工无障碍验收。Test-only Gallery/状态入口直接换展示内容，绕过离开草稿确认；草稿仍保留，返回普通导航仍有既有确认，普通Production没有这些入口。候选资源循环Private增长未归因，后续合并须继续检验视觉owner，而不能仅等待原候选soak。用户当前只需决定B的整体艺术方向，其他细节继续由开发方归纳。

原memory工作区最终核验：原分支 `codex/winui-phase5`、HEAD14a96a1、NUL Git status完全一致；627个冻结源码/候选/工具/profile/working-set文件SHA均一致，额外冻结文件0。原memory DLL为D94E995F02BAC1CBF579239C66C8EADD72E7776A6C2D02229EEF046FA092D61E、MVID0882b21e-ece4-4d0e-a629-bf4ce22da699。本轮只发布视觉worktree的可体验候选，未替换日用程序、未推送/合并、未运行2h soak。Phase7.1 memory MAJOR1仍开放。

**READY FOR VISUAL DESIGN REVIEW**

**ISOLATED WORKTREE — NOT MERGED**

这只表示已可审阅；等待用户视觉确认及Phase7.1 memory soak/targeted Gate，不自动推进下一阶段。
