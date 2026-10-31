> **SUPERSEDED — revision 1, rejected by the Product Owner on 2026-10-08.** Kept for history only. Nothing in this file is an active requirement (side inspector, single-click select, fixed gray-green accent, clear-rim + frosted-centre glass). See ../01–05 (revision 2).

# 04 · Implementation Plan and Review Handoff

Roles (user update 2026-10-08): after the Product Owner approves the direction, **Claude implements** the WinUI/XAML
redesign in the isolated worktree, starting with one vertical slice. **GPT is the independent engineering reviewer.**
Merge, push, release, Production data and the installed app remain out of scope for both.

> **Gate:** implementation has **not** started. It begins only after D1 (01 §6) is approved. Everything below is the
> plan.

## 1. Source baseline

| Item | Value |
|---|---|
| Authoritative workspace | `C:\Users\USER\.codex\worktrees\winui-phase7-visual\The-Order`, `codex/winui-phase7-visual`, HEAD `ad59c6e` + 47 uncommitted files (all match `design-handoff-to-claude/source-manifest.json`, re-verified twice on 2026-10-08) |
| Isolated workspace | `D:\Projects\The-Order-worktrees\claude-design-phase7`, branch `claude/design-phase7` |
| Baseline snapshot commit | `7d74e24`, a byte copy of the 47 files on `ad59c6e`; status identical to the authoritative tree. **Never integrate this commit.** |
| Claude commits | `f18d609` frost capability (05) · design-package commit (docs only) |
| Toolchain | .NET SDK 10.0.401 (pinned, `D:\Projects\The-Order\.artifacts\native-phase1\tools\dotnet`), WinAppSDK 2.5.1, net10.0-windows10.0.26100, min OS 10.0.22000, x64, locked restore |

## 2. Vertical slice 1 — Shell + Library Grid/List

Order of work. Each step builds, keeps the existing tests green, and is reviewable on its own.

1. **Tokens.**
   - `DesignTokens.xaml`: typography ramp, Raised, status colours, FrostTint, radii and motion constants (03 §2).
   - Page titles change to 20/28. No behaviour change.
2. **Shell frame.**
   - Mica `SystemBackdrop`; extended title bar (48) with drag regions; NavigationView LeftCompact rail 72 with labels;
     content inset corner.
   - Session chip + flyout replacing `SessionSurface`. It binds the same `SessionPresentation`, `PauseTiming`/
     `ResumeTiming` handlers and `LifecycleNotice`. Exit moves to the rail footer and calls the same `OnExitApp`.
   - Floating vs inline notices.
   - Test diagnostics move into a Ctrl+Shift+D drawer (Test only).
   - Narrow: LeftMinimal and title-bar search icon.
3. **Title-bar search** bound to the existing Library query path (`OnSearch` semantics, same normalisation). Typing
   elsewhere navigates to Library.
4. **Library command row + active filter chips + filter flyout restyle.** Same query fields and values; pills are
   single-select mappings of the existing ComboBox values.
5. **Grid.**
   - Borderless tile template; adaptive `ItemWidth`; `SelectionMode=Extended` + 选择 mode (`Multiple`).
   - Keep `ContainerContentChanging`, CoverPresenter `SourceKey`, inactive `ItemsSource=null` and the existing image
     request and recycle contracts.
6. **List** with sticky header, responsive columns (02 §5). Header click maps to existing sort keys.
7. **Inspector (Standard rendering first).**
   - XAML ambient cover + Acrylic plate. Opaque and HC variants.
   - Binds a `LaunchViewModel` for the selected item: `OpenAsync`, `SelectAsync`, `ConfirmAsync`, `LaunchAsync`,
     `Cancel` exactly as Details uses them today.
   - Multi-selection batch panel calls the existing `OnRecognize` / `OnBatch` paths.
8. **Material modes in Settings** (preference + effective-state line). This is the only Settings change in slice 1.
9. **Advanced stage (optional in slice 1, required in slice 2).** Inspector lens via `OpticalRenderer` +
   `OpticalSceneImageBgra` + frost. Lifecycle per 03 §5.

Slice 2: Details redesign + hero stage. Slice 3: Discover, Recognition, Import, Installations, dialogs.

## 3. Component contracts

| Component | Inputs (existing) | Must not |
|---|---|---|
| `LibraryInspector` | selected `GameSummary`(s), a `LaunchViewModel` instance owned by the page (disposed on leave), `WorkspaceViewModel` commands | Create personal drafts; write data; launch without `CanLaunch`; bypass position confirmation |
| `ArtworkStage` | cover key → existing decoded-pixel source; theme; DPI; size class; `MaterialPolicy` effective state; quality mode | Capture XAML; hold covers beyond the selection; render while hidden; run timers; show a previous item's art under a new title |
| `SessionChip` | `SessionPresentation`, `CanPause`/`CanResume`, `LifecycleNotice` | Change timing or accounting; auto-dismiss errors |
| `TitleSearch` | Library query text | Search online; change `LibraryQuery` semantics |

## 4. Acceptance criteria — slice 1

Visual fidelity (against `direction-b.html` renders at the same widths and themes):

- regions, sizes and order per 02;
- deviations listed in a "Deviation log" table (what / why / evidence / approval needed?).

Functional, using the Test harness:

- [ ] Click selects and fills the inspector; double-click/Enter opens Details; Ctrl/Shift multi-select; 选择 mode
      checkboxes; Space toggles.
- [ ] 启动游戏 from the inspector follows the same states as Details: confirm-required → confirm → launch → running /
      cancel. Verify with the existing launch fixture only; no real games.
- [ ] Search, filter, sort, view switch and saved filters give identical result sets to the baseline for the same
      inputs (scripted comparison over the 384-item Test library).
- [ ] Session chip in the 9 session states (existing session-state fixtures). Pause/resume enablement is unchanged.
- [ ] Exit from the rail = previous exit behaviour; the tray hint text is still valid.
- [ ] Light / Dark / HC; widths 480, 720, 1008, 1280, 1600; DPI 100/150/200 % where available; text scale 150 %.
- [ ] Keyboard: tab order, F6 regions, focus visible on every stop, no focus loss on view switch.
- [ ] Material modes Auto / Advanced / Standard / Off show the documented effective state; force-solid and the policy
      fallbacks still apply.

Regression and resources:

- [ ] Full native test suite; Debug and Release builds with 0 warnings.
- [ ] Library virtualization probe unchanged (realized/containers comparable to baseline at the same size).
- [ ] Short Library↔Settings re-entry run (≤ 15 min) comparing Private / GPU Committed / handles with the baseline
      candidate.

  Shared is diagnostic only, per prior conclusions. This is not a GA1 repeat.

Evidence to deliver for review:

- diff (slice commits only);
- candidate hashes;
- TRX;
- runtime captures per state;
- deviation log;
- known limitations.

## 5. Technical dependencies

| Dep | Needed for | Notes |
|---|---|---|
| `LastPlayed` projection on `GameSummary` (from sessions) | List column, inspector, "继续游玩" | Read-only; no schema change |
| Material-mode field in UI preferences | Settings | Additive; preserve the existing preference file and its validation |
| `OpticalSceneImageBgra` + ambient blur in native source | Advanced stage | Extends the 05 change; same lifecycle tests |
| Title-bar passthrough regions | Shell | WinAppSDK `InputNonClientPointerSource`. Fallback: search in the command row |
| Japanese language tagging of titles | Typography | Pure presentation helper |

## 6. Unverified assumptions

- Mica + extended title bar behave correctly with the current single-instance and tray lifecycle (`WindowsTrayLifecycle`).
- `SelectionMode=Extended` keeps the existing selection-change handlers' semantics for batch actions.
- An extra `LaunchViewModel` instance for the inspector is safe alongside Details' instance. If not, share one owner
  per page.
- 340-DIP inspector + 5 columns is comfortable on 1280-wide displays at 150 % scale (to be checked at runtime).

## 7. Ready-to-send prompt — GPT independent review of the frost change (available now)

```text
你是 The Order 的独立工程评审，只读评审，不修改任何文件、不构建主工作区、不合并。

评审对象：Claude 隔离工作区 D:\Projects\The-Order-worktrees\claude-design-phase7，分支 claude/design-phase7。
只评审提交 f18d609（父提交 7d74e24 是你工作区 47 个未提交文件的字节副本，不属于评审范围、不得集成）。
补丁：.artifacts/phase7-2b/claude-frost/patch/0001-feat-material-frosted-stage-plate-body-with-clear-C1.patch。
说明：docs/design/claude-phase7/05_GLASS_ENGINEERING_REPORT.md。

请重点核查：
1. 关闭磨砂（frost amount 0 / 旧 20 浮点 OpticalDraw）是否与已认可 C1 着色器逐位一致；fixtures/accepted-c1-shader.h 是否为 7d74e24 中原文的逐字副本。
2. 常量缓冲 80→96 字节后，旧调用是否存在越界读取；OpticalDrawEx 的 count/范围校验是否完整。
3. 磨砂纹理的有效性：SceneBegin、Upload、尺寸变化后是否一定失效；无有效副本时是否失败关闭而非采样旧槽位。
4. 资源所有权：σ≤0 退役、Gallery 离页/隐藏/策略拒绝时的释放顺序是否沿用提前释放修复；D2D effect 输入是否及时解绑。
5. Gallery 仅 Test：FrostSwitch 默认关闭；高对比度下不会移除稳定文字面板。
6. 证据是否支持报告结论：native-regression 输出、targeted.trx（47/47）、pixel-comparison.json、contrast.json、runs/frost-b/lifecycle.jsonl 与 criteria.md（先冻结后运行）。

可以在你自己的隔离副本中复跑 native/scripts/test-optical-source.ps1 与相同过滤器的定向测试。不得访问 Production、真实游戏或密钥，不得替换正式程序。
输出：按严重程度排序的问题（文件:行号、触发条件、证据、建议回归用例），以及“是否可进入集成评审”的结论。未运行的检查不得写成通过。
```

## 8. Ready-to-send prompt — GPT review of vertical slice 1 (use after Claude delivers it)

```text
你是 The Order 的独立工程评审。Claude 已在隔离工作区 D:\Projects\The-Order-worktrees\claude-design-phase7（分支 claude/design-phase7）实现经用户批准的方向 B「舞台与检视」第一个垂直切片：Shell + 书架网格/列表。
设计依据：docs/design/claude-phase7/01–03；验收标准：04 §4；交付证据与偏差记录见切片交付说明。

只评审切片提交（不含 7d74e24 基线快照）。请核查：
1. 业务语义未变：搜索/筛选/排序/常用筛选结果集、启动（确认位置→启动→取消）、会话暂停/恢复、退出、批量识别/编辑入口均调用原有命令且可用性条件不变。
2. 虚拟化与图片生命周期：ContainerContentChanging、CoverPresenter 请求/回收、非活动 ItemsSource=null 保持；无新增常驻封面持有。
3. 选择模型从“单击切换多选”改为 Extended + 选择模式：批量操作范围仍只含当前可见勾选项。
4. 可访问性：Tab 顺序、F6 区域、焦点可见、控件名称、高对比度、150% 文字缩放、480 DIP 宽度无横向滚动。
5. 材质：Mica/Acrylic/不透明/高对比度四种模式与 03 §6 一致；高级舞台（如已实现）沿用提前释放与失败关闭规则。
6. 资源：Library↔Settings 短程对照只比较 Private/GPU Committed/handles，Shared 仅作诊断。
输出按严重程度排序的问题与复现方式，并单列“设计偏差是否已记录”。未执行的验证不得写成通过；不得 merge/push/发布或访问 Production。
```

## 9. Fallback prompt — if the Product Owner reassigns implementation to GPT

```text
请在你自己的新隔离工作区实现 docs/design/claude-phase7 的方向 B 第一个垂直切片（Shell + 书架网格/列表），严格按 02 的区域、尺寸与响应式规则、03 的令牌与材质角色、04 §2 的顺序与 §4 的验收标准执行。
业务语义、持久化、虚拟化、图片生命周期与既有资源修复不得改变；任何偏离设计之处记入“偏差记录”表（内容/原因/证据/是否需要设计评审），不得静默调整构图。
高级舞台材质可使用提交 f18d609（不得引入 7d74e24 基线快照）。完成后提交差异、候选哈希、测试 TRX、各状态运行截图与已知限制，交 Claude 做设计评审。
```
