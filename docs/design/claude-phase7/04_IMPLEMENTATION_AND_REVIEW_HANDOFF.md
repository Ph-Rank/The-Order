# 04 · Implementation Plan and Review Handoff — revision 2

Roles (user, 2026-10-08):
- After the Product Owner approves the direction, **Claude implements** the WinUI/XAML redesign in the isolated
  worktree.
- **GPT is the independent engineering reviewer.**
- Merge, push, release, Production data and the installed app stay out of scope.

> **Update 2026-10-08:** R1–R6 were approved and both slices are implemented; see `06_IMPLEMENTATION_RESULTS.md`.
> Where this plan differs from the approval, the approval wins. In particular, R5 removes the in-app Exit entirely (× closes to the tray; the tray menu exits).
> Everything in §2 is a plan.

## 1. Baseline and identities

| Item | Value |
|---|---|
| Authoritative tree | `C:\Users\USER\.codex\worktrees\winui-phase7-visual\The-Order` (`codex/winui-phase7-visual`, HEAD `ad59c6e` + 47 uncommitted files matching `design-handoff-to-claude/source-manifest.json`; re-verified unchanged) |
| Isolated worktree | `D:\Projects\The-Order-worktrees\claude-design-phase7`, branch `claude/design-phase7`; tag `claude-design-rev1` marks revision 1 |
| Never integrate | `7d74e24` (byte snapshot of the authoritative tree) |
| Material code for review | `f18d609` + `09465c2` (05) |
| Toolchain | .NET SDK 10.0.401 (pinned), WinAppSDK 2.5.1, net10.0-windows10.0.26100, min OS 10.0.22000, x64, locked restore |

## 2. Vertical slice 1 — Shell + Library Grid/List (after R1)

Each step builds, keeps the existing tests green and is reviewable on its own.

1. **Tokens → Fluent resources** (03 §2). Remove the gray-green hex values; keep the app keys as aliases.
2. **Accent live-change handler** (03 §3) with a Test verification seam.
3. **Shell.**
   - Mica; extended title bar with AutoSuggestBox; NavigationView with default thresholds; content-on-Mica layer.
   - Session chip + flyout replacing `SessionSurface`. It binds the same `SessionPresentation`, `PauseTiming` /
     `ResumeTiming` and `LifecycleNotice`.
   - ~~Exit moves into the navigation footer~~ (superseded by R5: no in-app Exit; × closes to the tray, the tray menu exits).
   - Floating vs inline notices. Test diagnostics drawer.
4. **Library header**, filter chips and a restyled filter flyout. Same `LibraryQuery` fields and values.
5. **Grid.**
   - Fluent cards with adaptive `ItemWidth`.
   - Interaction model (02 §3): `SelectionMode=None` + `IsItemClickEnabled` for normal browsing; `Multiple` in
     selection mode.
   - Context menu.
   - Keep `ContainerContentChanging`, CoverPresenter `SourceKey`, the inactive `ItemsSource=null` contract and the
     image request/recycle contracts.
6. **List** — table with responsive columns; header click maps to existing sort keys.
7. **Return path** (02 §6): scroll anchor and focus restoration.
8. **Settings appearance card** (material mode + effective state). The only Settings change in slice 1.

Slice 2 covers the Details redesign and the hero glass in product: an `OpticalSceneImageBgra`-style source fed from
decoded cover pixels at hero size, the polarity computation, and the Details-owned renderer lifecycle. Slice 3 covers
Discover, Recognition, Import, Installations and dialogs.

## 3. Acceptance — slice 1

**Visual**
- Matches `direction-r2.html` at 480 / 900 / 1280 / 1600, Light/Dark and HC.
- Deviations are logged as what / why / evidence / approval needed.
- Captures with the actual system accent, plus the Test-only accent seam labelled as simulation.

**Functional** (Test harness and synthetic library only)
- [ ] Single click, tap, Enter or Space on a card opens Details; no double-click needed.
- [ ] Back (button, breadcrumb, Alt+←, mouse back) restores search text, filters, sort, view, scroll anchor (± one
      row) and focus on the opened card.
- [ ] Selection mode via 选择, Ctrl+click, Shift+click and Ctrl+A. Click toggles and does not navigate; Shift range;
      Esc / 完成 exits. Batch actions only touch selected visible items.
- [ ] Context-menu 启动游戏 and Ctrl+Enter follow the existing launch states: they launch only when `Ready`,
      otherwise open Details showing the confirmation. Verified with the launch fixture only.
- [ ] Search/filter/sort result sets equal the baseline for scripted inputs on the 384-item Test library.
- [ ] Session chip in all 9 session states (existing fixtures). Pause/resume enablement unchanged. Exit and tray hint
      unchanged.
- [ ] No hardcoded accent: a code search finds no literal colours in accent/selection/focus paths; HC uses system
      colours.
- [ ] Keyboard: tab order, F6 regions, visible focus, names; 150 % text scale; 100/150/200 % DPI where available.

**Regression and resources**
- Full native suite; Debug/Release 0 warnings.
- Virtualization probe comparable to baseline.
- A ≤ 15-minute Library↔Settings re-entry comparison (Private / GPU Committed / handles; Shared diagnostic only). This
  is not a GA1 repeat.

## 4. Dependencies and unverified assumptions

- `ListViewPersistenceHelper` in WinUI 3 (fallback: `ScrollIntoView` + offset).
- `InputNonClientPointerSource` passthrough regions (fallback: search in the page header).
- Whether WinUI re-resolves accent ThemeResources on its own when the Windows accent changes. This must be verified;
  the handler (03 §3) covers both outcomes.
- A read-only `LastPlayed` projection; a material-mode field in UI preferences (additive, validated).
- `SelectionMode` switching at runtime keeps the existing selection handlers' semantics.

## 5. Prompt for GPT — independent review of the material commits (available now)

```text
你是 The Order 的独立工程评审，只读，不修改主工作区、不合并、不发布。

评审对象：Claude 隔离工作区 D:\Projects\The-Order-worktrees\claude-design-phase7（分支 claude/design-phase7）的提交 f18d609 与 09465c2。
补丁：.artifacts/phase7-2b/claude-glass-r2/patch/0001-*.patch 与 0003-*.patch（0002 为已作废的修订1文档；7d74e24 是你工作区的字节快照，禁止集成）。
说明：docs/design/claude-phase7/05_GLASS_ENGINEERING_REPORT.md；设计依据 03 §4。

请核查：
1. 玻璃主体模式 0（C1）在 20/24/28 常量下与已认可 C1 着色器逐位一致；fixtures/accepted-c1-shader.h 是否为原文逐字副本；112 字节常量缓冲是否存在旧调用越界读取。
2. 连续玻璃 / 完全清透：校验（极性 ±1、清透标志、与修订1互斥）、无有效模糊副本时失败关闭、完全清透不依赖副本。
3. 亮度自适应色调的对比度下界推导与原生测试（7 种极端背景 × 2 种极性）是否成立；伽马 2.2 与 sRGB 的差异是否有足够余量。
4. GallerySource 模糊图片：每次绘制独立 effect、EndDraw 前保留；按场景 DPI 创建位图是否引入其他 DPI 问题；默认清晰图片路径未改。
5. Gallery：连续/清透模式只把描边设为透明且不改 BorderThickness；离开后恢复 DesignTokens 主题画刷；高对比度下不移除稳定文字面板。
6. 证据：native-regression.log、targeted-d.trx（47/47）、pixel-comparison-r3.json、runs/*/lifecycle*.json 与 criteria.md（先冻结后运行；idle-12s 原始失败与后续复测均已记录）。

可在你自己的隔离副本中复跑 native/scripts/test-optical-source.ps1 与相同过滤器的定向测试。不得访问 Production、真实游戏或密钥。
输出按严重程度排序的问题（文件:行号、触发条件、证据、建议回归用例）与“是否可进入集成评审”的结论；未运行的检查不得写成通过。
```

## 6. Prompt for GPT — review of slice 1 (after Claude delivers it)

```text
你是 The Order 的独立工程评审。Claude 已在隔离工作区 D:\Projects\The-Order-worktrees\claude-design-phase7 实现经用户批准的“修订 2”第一个垂直切片（Shell + 书架网格/列表）。
设计依据：docs/design/claude-phase7/01–03；验收标准：04 §3；交付说明含提交范围、候选哈希、TRX、运行截图与偏差记录。

只评审切片提交（不含 7d74e24）。请核查：
1. 单击封面直接进入详情；返回恢复搜索、筛选、排序、视图、滚动与焦点；选择模式中单击只切换选择、不导航。
2. 业务语义不变：搜索/筛选/排序结果集、启动授权与安装选择（右键“启动游戏”/Ctrl+Enter 仅在 Ready 时启动，否则进入详情显示确认）、会话暂停/恢复、退出、批量识别/编辑范围。
3. 虚拟化与图片生命周期：ContainerContentChanging、CoverPresenter 请求/回收、非活动 ItemsSource=null 保持。
4. 强调色只来自 WinUI 主题资源，无硬编码色相；焦点不使用强调色；高对比度使用系统颜色；强调色运行时变更的处理与验证方式。
5. 可访问性：Tab 顺序、F6、焦点可见、控件名称、150% 文字缩放、480 宽度无横向滚动。
6. 资源：Library↔Settings 短程对照只比较 Private/GPU Committed/handles，Shared 仅诊断。
输出按严重程度排序的问题与复现方式，并单列“设计偏差是否已记录”。未执行的验证不得写成通过；不得 merge/push/发布或访问 Production。
```
