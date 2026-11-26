<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { KunButton, KunModal, KunMessageProvider, KunAlertProvider, useKunMessage, useKunAlert } from '@kungal/ui-vue'
import { Library, Compass, LayoutGrid, BarChart3, Settings, Search, Plus, FolderPlus, ArrowUpRight, ChevronRight, Heart, Star, SlidersHorizontal, X, Play, Clock3, BookOpen, FolderOpen, Link2, RefreshCw, Check, CheckCircle2, ShieldCheck, Monitor, Tags, Sparkles, Square, CircleHelp, HardDrive, LoaderCircle } from 'lucide-vue-next'
import { api, isPreview } from './bridge'
import type { AppSnapshot, BulkGamePatch, LibraryGame, PlayStatus, WorkMetadata, ScanCandidate, Theme, AppearanceStyle, MatchReview, InstallationAttachmentScan, NonPortraitCoverDisplay, PublicSettings, ConnectionResult } from './shared/types'
import GameCard from './components/GameCard.vue'
import CoverPicker from './components/CoverPicker.vue'
import DetailCover from './components/DetailCover.vue'
import { gameCoverDisplay, gameCoverUrl, workCoverUrl } from './cover-display'
import SectionSignature from './components/SectionSignature.vue'
import OrderMark from './components/OrderMark.vue'
import DesktopTitlebar from './components/DesktopTitlebar.vue'
import LiquidGlassMaterial from './components/LiquidGlassMaterial.vue'
import LibraryOverview from './components/LibraryOverview.vue'
import LibraryCollections from './components/LibraryCollections.vue'
import LibrarySavedViews from './components/LibrarySavedViews.vue'
import PersonalRecord from './components/PersonalRecord.vue'
import PreferencesSettings from './components/PreferencesSettings.vue'
import ImportCandidates from './components/ImportCandidates.vue'
import MatchReviewWorkspace from './components/MatchReviewWorkspace.vue'
import LibraryMaintenance from './components/LibraryMaintenance.vue'
import PlayStatistics from './components/PlayStatistics.vue'
import { filterLibraryGames, folderKey, readLibraryPreferences, saveLibraryPreferences, splitLabels } from './library-workspace'
import type { LibraryFilters, LibraryLayout, LibrarySort, LibraryView } from './library-workspace'
import { createSavedView, readSavedViews, writeSavedViews, type SavedLibraryView, type SavedViewEditor } from './saved-library-views'
import { getLaunchInstallation } from './library-insights'
import { buildScanRows, validScanSelection } from './import-workspace'
import type { ScanView } from './import-workspace'

type Page = 'library' | 'discover' | 'categories' | 'stats' | 'settings'
const page = ref<Page>('library')
const navigation = [{ id: 'library' as Page, label: '我的书架', shortLabel: '书架', icon: Library }, { id: 'discover' as Page, label: '发现作品', shortLabel: '发现', icon: Compass }, { id: 'categories' as Page, label: '分类收藏', shortLabel: '分类', icon: LayoutGrid }, { id: 'stats' as Page, label: '游玩统计', shortLabel: '统计', icon: BarChart3 }]
const quickCollectionsOpen = ref(false)
const statuses: { value: PlayStatus; label: string }[] = [{ value: 'planned', label: '未开始' }, { value: 'playing', label: '在玩' }, { value: 'completed', label: '已完成' }, { value: 'paused', label: '搁置' }, { value: 'dropped', label: '放弃' }]
const snapshot = ref<AppSnapshot | null>(null)
const loading = ref(true)
const initialError = ref('')
const busy = reactive(new Set<string>())
const libraryQuery = ref('')
const statusFilter = ref<PlayStatus | 'all'>('all')
const favoritesOnly = ref(false)
const categoryFilter = ref('')
const tagFilter = ref('')
const minRating = ref('')
function uiStorage() { try { return typeof window === 'undefined' ? undefined : window.localStorage } catch { return undefined } }
const libraryPreferences = readLibraryPreferences(uiStorage())
const sort = ref<LibrarySort>(libraryPreferences.sort)
const libraryLayout = ref<LibraryLayout>(libraryPreferences.layout)
const overviewVisible = ref(libraryPreferences.overview)
const libraryView = ref<LibraryView>('all')
const initialSavedViews = readSavedViews(uiStorage())
const savedViews = ref(initialSavedViews.views)
const savedViewError = ref(initialSavedViews.error)
const savedViewsReadOnly = ref(!!initialSavedViews.error)
const savedViewEditor = ref<SavedViewEditor | null>(null)
const activeSavedViewId = ref<string | null>(null)
let savedViewOrigin: HTMLElement | null = null
const selectionMode = ref(false)
const selectedIds = ref<string[]>([])
let selectionGeneration = 0
const batchStatus = ref<PlayStatus | ''>('')
const batchFavorite = ref('')
const batchCategory = ref('')
const batchTag = ref('')
const batchCategoryMode = ref<'add' | 'remove'>('add')
const batchTagMode = ref<'add' | 'remove'>('add')
watch(() => [sort.value, libraryLayout.value, overviewVisible.value], () => saveLibraryPreferences(uiStorage(), { sort: sort.value, layout: libraryLayout.value, overview: overviewVisible.value }))
const filtersOpen = ref(false)
const games = computed(() => snapshot.value?.games ?? [])
const matching = computed(() => snapshot.value?.matching)
const reidentifying = computed(() => matching.value?.mode === 'reidentify')
const matchingRunning = computed(() => matching.value?.status === 'running')
const matchingUnavailable = computed(() => matchingRunning.value || busy.has('matching-start'))
const localGameIds = computed(() => games.value.filter(game => game.installations.length).map(game => game.id))
const selectedLocalIds = computed(() => localGameIds.value.filter(id => selectedIds.value.includes(id)))
const matchReviews = computed(() => {
  const currentIds = new Set(games.value.map(game => game.id))
  return (matching.value?.reviews ?? []).filter(review => currentIds.has(review.gameId))
})
const matchingPercent = computed(() => matching.value?.total ? Math.min(100, Math.max(0, matching.value.processed / matching.value.total * 100)) : 0)
const dismissedMatchingId = ref<string | null>(null)
const matchingVisible = computed(() => !!matching.value && matching.value.status !== 'idle' && (matchingRunning.value || matching.value.id !== dismissedMatchingId.value))
const matchingLabel = computed(() => reidentifying.value
  ? matchingRunning.value ? '正在重新识别本地游戏' : matching.value?.status === 'cancelled' ? '重新识别已停止' : '本次重新识别已完成'
  : matchingRunning.value ? '正在自动补全游戏资料' : matching.value?.status === 'cancelled' ? '资料补全已停止' : '本次资料补全已完成')
const announcedJobs = new Set<string>()
watch(matching, (current, previous) => {
  if (!current?.id || current.status === 'idle' || current.status === 'running') return
  dismissedMatchingId.value = current.id
  if (!previous || announcedJobs.has(current.id) || (previous.id === current.id && previous.status !== 'running')) return
  announcedJobs.add(current.id)
  useKunMessage(current.status === 'cancelled' ? '识别已停止，已完成的结果和待核对项目仍在资料整理中。' : `识别已结束：匹配 ${current.matched} 部，待核对 ${current.reviews.length} 项${current.failed ? `，失败 ${current.failed} 部` : ''}。`, current.failed ? 'info' : 'success')
})
const reviewOpen = ref(false)
const reviewGameId = ref<string | null>(null)
const reviewChoice = ref('')
const reviewError = ref(''), reviewReloadNeeded = ref(false)
let reviewGeneration = 0
const reviewModalOpen = computed({ get: () => reviewOpen.value, set: value => { if (!busy.has('matching-resolve')) reviewOpen.value = value } })
const currentReview = computed(() => matchReviews.value.find(review => review.gameId === reviewGameId.value) ?? matchReviews.value[0] ?? null)
const maintenanceOpen = ref(false)
const missingGames = computed(() => games.value.filter(game => game.installations.length && (!game.metadata || !game.metadata.coverUrl)))
const unconfiguredGames = computed(() => games.value.filter(game => game.installations.length && !getLaunchInstallation(game)?.executablePath))
function reviewIdentity() { return [currentReview.value?.gameId, currentReview.value?.reviewId, currentReview.value?.decisionFingerprint].join('|') }
watch(reviewIdentity, () => {
  reviewChoice.value = ''
  if (!busy.has('matching-resolve')) { reviewGeneration++; reviewError.value = ''; reviewReloadNeeded.value = false }
}, { flush: 'sync' })
watch(() => currentReview.value?.options.map(option => option.work.id).join(','), () => { if (!currentReview.value?.options.some(option => option.work.id === reviewChoice.value)) reviewChoice.value = '' }, { flush: 'sync' })
watch(reviewOpen, open => { if (!open) { reviewGeneration++; reviewChoice.value = ''; if (!reviewReloadNeeded.value) reviewError.value = ''; reviewDetailOpen.value = false } }, { flush: 'sync' })
const allCategories = computed(() => [...new Set(games.value.flatMap(g => g.categories))].sort())
const allTags = computed(() => [...new Set(games.value.flatMap(g => [...g.customTags, ...(g.metadata?.tags ?? [])]))].sort())
const libraryFilters = computed<LibraryFilters>(() => ({ query: libraryQuery.value, view: libraryView.value, status: statusFilter.value, favorites: favoritesOnly.value, category: categoryFilter.value, tag: tagFilter.value, minRating: minRating.value }))
const filteredGames = computed(() => filterLibraryGames(games.value, libraryFilters.value, sort.value))
const smartViews: { value: LibraryView; label: string }[] = [{ value: 'all', label: '全部' }, { value: 'local', label: '本地游戏' }, { value: 'wishlist', label: '愿望单' }]
const activeFilterCount = computed(() => Number(statusFilter.value !== 'all') + Number(favoritesOnly.value) + Number(!!categoryFilter.value) + Number(!!tagFilter.value) + Number(minRating.value !== ''))
watch(() => filteredGames.value.map(game => game.id).join('|'), () => { const visible = new Set(filteredGames.value.map(game => game.id)); selectedIds.value = selectedIds.value.filter(id => visible.has(id)) }, { flush: 'sync' })
function leaveSelection() { selectionGeneration++; selectionMode.value = false; selectedIds.value = []; batchStatus.value = ''; batchFavorite.value = ''; batchCategory.value = ''; batchTag.value = '' }
function enterSelection() { selectionGeneration++; selectionMode.value = true; selectedIds.value = [] }
function selectVisible() { if (!busy.has('bulk')) selectedIds.value = filteredGames.value.slice(0, 1000).map(game => game.id) }
function toggleSelection(id: string) { if (busy.has('bulk')) return; selectedIds.value = selectedIds.value.includes(id) ? selectedIds.value.filter(value => value !== id) : [...selectedIds.value, id] }
function applyOverviewFilter(value: string) { page.value = 'library'; resetFilters(); if (value === 'playing') statusFilter.value = 'playing'; else if (['all', 'local', 'wishlist', 'unmatched', 'unconfigured'].includes(value)) libraryView.value = value as LibraryView }
function showMaintenanceFilter(view: 'unmatched' | 'unconfigured') { maintenanceOpen.value = false; applyOverviewFilter(view) }
async function openMaintenanceGame(game: LibraryGame, configure = false) { maintenanceOpen.value = false; if (configure) await configureGame(game); else await showGame(game) }
async function organizeSelected() {
  const ids = [...selectedIds.value], generation = selectionGeneration; if (!ids.length) return
  const patch: BulkGamePatch = {}
  if (batchStatus.value) patch.status = batchStatus.value
  if (batchFavorite.value) patch.favorite = batchFavorite.value === 'yes'
  const categories = splitLabels(batchCategory.value), tags = splitLabels(batchTag.value)
  if (categories.length) patch[batchCategoryMode.value === 'add' ? 'addCategories' : 'removeCategories'] = categories
  if (tags.length) patch[batchTagMode.value === 'add' ? 'addTags' : 'removeTags'] = tags
  if (!Object.keys(patch).length) { useKunMessage('请选择要修改的状态、收藏，或填写分类和标签。', 'info'); return }
  await task('bulk', async () => { await api.updateGames(ids, patch); await reload(); if (generation === selectionGeneration) leaveSelection(); useKunMessage(`已整理 ${ids.length} 部作品。`, 'success') })
}
const hours = (seconds: number) => seconds < 3600 ? `${Math.floor(seconds / 60)} 分钟` : `${(seconds / 3600).toFixed(1)} 小时`
const sourceName = (source: string) => ({ bangumi: 'Bangumi', vndb: 'VNDB', erogamescape: '批评空间', dlsite: 'DLsite', howlongtobeat: 'HowLongToBeat', steam: 'Steam', nextmoe: 'NextMoe' }[source] ?? source)
const releaseDate = (work?: WorkMetadata | null) => !work?.releaseDate ? '发售日期未收录' : work.releaseDatePrecision === 'year' ? work.releaseDate.slice(0, 4) : work.releaseDatePrecision === 'month' ? work.releaseDate.slice(0, 7) : work.releaseDate
function notifyError(error: unknown) { const message = error instanceof Error ? error.message : '操作未完成，请重试。'; useKunMessage(message.replace(/nmk_(?:live|test)_[\w-]+/g, '[已隐藏密钥]'), 'error', 5500) }
async function task<T>(key: string, action: () => Promise<T>): Promise<T | undefined> { if (busy.has(key)) return; busy.add(key); try { return await action() } catch (e) { notifyError(e) } finally { busy.delete(key) } }
let reloadGeneration = 0
async function reload() {
  const generation = ++reloadGeneration
  try {
    const next = await api.getState()
    if (generation === reloadGeneration) snapshot.value = next
  } catch (error) {
    if (generation === reloadGeneration) throw error
  }
}
function navigate(target: Page) { if (target !== 'library') leaveSelection(); if (target !== page.value) closeSavedViewEditor(); page.value = target }
function resetFilters() { libraryQuery.value = ''; statusFilter.value = 'all'; favoritesOnly.value = false; categoryFilter.value = ''; tagFilter.value = ''; minRating.value = ''; libraryView.value = 'all'; activeSavedViewId.value = null }
function openSavedViewEditor(event: MouseEvent, id: string | null = null) {
  if (busy.has('bulk') || savedViewsReadOnly.value) return
  const view = savedViews.value.find(item => item.id === id)
  savedViewOrigin = event.currentTarget instanceof HTMLElement ? event.currentTarget : null
  savedViewError.value = null
  savedViewEditor.value = { id, name: view?.name ?? '' }
}
function renameSavedView(id: string, event: MouseEvent) { openSavedViewEditor(event, id) }
function closeSavedViewEditor() { savedViewEditor.value = null; if (savedViewOrigin?.isConnected) void nextTick(() => savedViewOrigin?.focus()) }
function persistSavedViews(next: SavedLibraryView[]): boolean {
  if (busy.has('bulk') || savedViewsReadOnly.value) return false
  const result = writeSavedViews(uiStorage(), next)
  if (!result.ok) { savedViewError.value = result.error; return false }
  savedViews.value = result.views; savedViewError.value = null; return true
}
function saveNamedView(name: string) {
  const editor = savedViewEditor.value
  if (!editor) return
  try {
    const existing = savedViews.value.find(view => view.id === editor.id)
    if (editor.id && !existing) throw Error('这个常用筛选已不存在，请重新打开。')
    const next = createSavedView(name, existing?.filters ?? libraryFilters.value, existing?.sort ?? sort.value, existing?.id)
    if (!persistSavedViews(existing ? savedViews.value.map(view => view.id === existing.id ? next : view) : [...savedViews.value, next])) return
    if (!existing) activeSavedViewId.value = next.id
    closeSavedViewEditor()
  } catch (error) { savedViewError.value = error instanceof Error ? error.message : '常用筛选未能保存。' }
}
function updateSavedView(id: string) {
  const view = savedViews.value.find(item => item.id === id)
  if (!view) return
  try {
    const next = createSavedView(view.name, libraryFilters.value, sort.value, id)
    if (persistSavedViews(savedViews.value.map(item => item.id === id ? next : item))) activeSavedViewId.value = id
  } catch (error) { savedViewError.value = error instanceof Error ? error.message : '常用筛选未能更新。' }
}
function removeSavedView(id: string) {
  if (persistSavedViews(savedViews.value.filter(view => view.id !== id))) {
    if (activeSavedViewId.value === id) activeSavedViewId.value = null
    if (savedViewEditor.value?.id === id) closeSavedViewEditor()
  }
}
function retrySavedViews() { const result = readSavedViews(uiStorage()); savedViews.value = result.views; savedViewError.value = result.error; savedViewsReadOnly.value = !!result.error }
async function applySavedView(view: SavedLibraryView) {
  if (busy.has('bulk')) return
  leaveSelection(); closeSavedViewEditor(); page.value = 'library'
  const f = view.filters
  libraryQuery.value = f.query; libraryView.value = f.view; statusFilter.value = f.status; favoritesOnly.value = f.favorites
  categoryFilter.value = f.category; tagFilter.value = f.tag; minRating.value = f.minRating; sort.value = view.sort
  activeSavedViewId.value = view.id
  await nextTick(); window.scrollTo(0, 0); document.querySelector<HTMLElement>('.library-heading h1')?.focus()
}
async function favorite(game: LibraryGame) { await task(`favorite-${game.id}`, async () => { await api.updateGame(game.id, { favorite: !game.favorite }); await reload() }) }
async function launch(game: LibraryGame) { await task('launch', async () => { await api.launchGame(game.id); await reload(); useKunMessage('游戏已启动，开始记录游玩时间。', 'success') }) }
async function configureGame(game: LibraryGame) { if (!game.installations.length) { await prepareAttachment(game); return }; await showGame(game); detailTab.value = 'manage' }
async function external(url?: string | null) { if (url) await task('external', () => api.openExternal(url)) }
async function startMatching(gameIds?: string[], mode: 'missing' | 'reidentify' = 'missing'): Promise<boolean> {
  if (matchingUnavailable.value) return false
  const requested = gameIds === undefined ? null : new Set(gameIds)
  const ids = games.value.filter(game => (!requested || requested.has(game.id)) && game.installations.length && (mode === 'reidentify' || !game.metadata || !game.metadata.coverUrl)).map(game => game.id)
  if (!ids.length) { useKunMessage(mode === 'reidentify' ? '没有可重新识别的本地游戏，请先添加本地安装。' : '当前没有需要补全资料的本地游戏。', 'info'); return false }
  if (!snapshot.value?.settings.hasApiKey) {
    useKunMessage(isPreview ? '自动识别需要在桌面版中连接 NextMoe。' : '请先保存 API 密钥，再识别本地游戏。', 'info')
    if (detailOpen.value && !await requestCloseDetail()) return false
    scanOpen.value = false; navigate('settings'); return false
  }
  const started = await task('matching-start', async () => {
    dismissedMatchingId.value = null
    // Snapshot plain IDs before crossing the desktop bridge. A late start reply
    // must not overwrite a newer cancellation or job notification.
    const result = await api.startMetadataMatching(ids, mode)
    await reload()
    if (!result.total) { useKunMessage('本次没有可处理的本地游戏。', 'info'); return false }
    const current = matching.value
    if (current?.id !== result.id || current.status === 'cancelled') return false
    return true
  })
  return started === true
}
async function identifySelected() { await startMatching([...selectedLocalIds.value], 'reidentify') }
async function identifyCurrentGame() { const game = selectedGame.value; if (game) await startMatching([game.id], 'reidentify') }
async function cancelMatching() { await task('matching-cancel', async () => { await api.cancelMetadataMatching(); await reload() }) }
function openMatchReviews() { maintenanceOpen.value = false; reviewOpen.value = true; if (!matchReviews.value.some(review => review.gameId === reviewGameId.value)) reviewGameId.value = matchReviews.value[0]?.gameId ?? null }
function selectReview(gameId: string) { if (!busy.has('matching-resolve') && !reviewReloadNeeded.value && matchReviews.value.some(review => review.gameId === gameId)) reviewGameId.value = gameId }
function chooseReview(workId: string) { if (!busy.has('matching-resolve') && !reviewReloadNeeded.value && currentReview.value?.sourceStatus !== 'stale' && currentReview.value?.options.some(option => option.work.id === workId)) reviewChoice.value = workId }
function laterReview() { if (!busy.has('matching-resolve')) reviewOpen.value = false }
async function resolveReview(skip = false) {
  if (busy.has('matching-resolve') || !reviewOpen.value) return
  const review = currentReview.value; const choice = reviewChoice.value
  if (!reviewReloadNeeded.value && (!review || (!skip && (review.sourceStatus === 'stale' || !review.options.some(option => option.work.id === choice))))) return
  if (!reviewReloadNeeded.value && (!review?.reviewId || !review.decisionFingerprint)) { reviewError.value = '核对信息正在更新，请重新打开资料整理。'; return }
  const generation = reviewGeneration, reread = reviewReloadNeeded.value
  const currentSession = () => generation === reviewGeneration && reviewOpen.value
  reviewError.value = ''
  await task('matching-resolve', async () => {
    let committed = reread
    try {
      if (!reread) { await api.resolveMetadataMatch(review!.gameId, skip ? null : choice, review!.reviewId, review!.decisionFingerprint); committed = true }
      await reload()
      if (!currentSession()) return
      reviewReloadNeeded.value = false
      if (review && selectedGameId.value === review.gameId) selectedWork.value = games.value.find(game => game.id === review.gameId)?.metadata ?? selectedWork.value
      useKunMessage(reread ? '书架与核对清单已重新读取。' : skip ? '本次建议已结束，书架关联和个人记录未改变。' : '已关联你确认的作品资料。', 'success')
    } catch (error) {
      if (!currentSession()) return
      if (committed) { reviewReloadNeeded.value = true; reviewError.value = reread ? '书架与核对清单仍未能读取。请重新读取，不会重复提交。' : '本次决定已提交，但书架与核对清单读取失败。请重新读取，不会重复提交。' }
      else if (!matchReviews.value.some(item => item.gameId === review?.gameId && item.reviewId !== review?.reviewId)) {
        // A rejected decision can still follow a completed library commit if
        // persisting the remaining queue failed. Read the actual association
        // before offering another decision; never resubmit automatically.
        try {
          await reload()
          if (currentSession() && !matchReviews.value.some(item => item.gameId === review?.gameId && item.reviewId !== review?.reviewId)) reviewError.value = `「${review?.name}」的操作未完整结束，请核对当前关联后再决定。` + settingsFailure(error)
        } catch {
          if (currentSession()) { reviewReloadNeeded.value = true; reviewError.value = '操作未完整结束，当前关联和清单也未能重新读取。请先重新读取，再决定是否重试；不会自动重复提交。' }
        }
      }
    }
  })
}
const reviewDetailOpen = ref(false), reviewDetailWork = ref<WorkMetadata | null>(null), reviewDetailError = ref('')
let reviewDetailGeneration = 0
watch(reviewDetailOpen, open => { if (!open) { reviewDetailGeneration++; busy.delete('review-detail') } })
async function showReviewDetail(work: WorkMetadata) {
  if (busy.has('matching-resolve') || reviewReloadNeeded.value) return
  const generation = ++reviewDetailGeneration
  reviewDetailWork.value = work; reviewDetailError.value = ''; reviewDetailOpen.value = true; busy.add('review-detail')
  try { const detail = await api.getWork(work.id); if (generation === reviewDetailGeneration && reviewDetailOpen.value) reviewDetailWork.value = detail }
  catch (error) { if (generation === reviewDetailGeneration) reviewDetailError.value = error instanceof Error ? error.message : '详情读取失败，可以稍后重试。' }
  finally { if (generation === reviewDetailGeneration) busy.delete('review-detail') }
}

const attachmentOpen = ref(false), attachmentScan = ref<InstallationAttachmentScan | null>(null), attachmentGameId = ref<string | null>(null), attachmentError = ref('')
let attachmentGeneration = 0
watch(attachmentOpen, open => {
  if (!open) {
    attachmentGeneration++
    const requestId = attachmentScan.value?.requestId
    attachmentScan.value = null; attachmentGameId.value = null
    if (requestId) void api.cancelInstallationAttachment(requestId).catch(notifyError)
  }
})
async function prepareAttachment(game: LibraryGame) {
  if (busy.has('attachment-prepare')) return
  const generation = ++attachmentGeneration
  await task('attachment-prepare', async () => {
    const result = await api.prepareInstallationAttachment(game.id)
    if (!result) return
    if (generation !== attachmentGeneration || !games.value.some(item => item.id === game.id)) { await api.cancelInstallationAttachment(result.requestId); return }
    attachmentGameId.value = game.id; attachmentScan.value = result; attachmentError.value = ''; attachmentOpen.value = true
  })
}
async function attachCandidate(candidate: ScanCandidate) {
  const prepared = attachmentScan.value, generation = attachmentGeneration
  if (!prepared || !prepared.candidates.some(item => item.id === candidate.id)) return
  await task('attachment-commit', async () => {
    attachmentError.value = ''
    try {
      const updated = await api.attachLocalInstallation(prepared.requestId, candidate.id)
      if (!updated) return
      await reload()
      if (generation === attachmentGeneration) attachmentOpen.value = false
      useKunMessage('已添加本地安装，作品关联和个人记录已保留。', 'success')
    } catch (error) {
      if (generation === attachmentGeneration) attachmentError.value = error instanceof Error ? error.message : '添加失败，请重新选择。'
      await reload()
    }
  })
}

const searchQuery = ref('')
const submittedQuery = ref('')
const searchResults = ref<WorkMetadata[]>([])
const searchCursor = ref<string | null>(null)
const hasSearched = ref(false)
const searchError = ref('')
const searchUsedCursors = new Set<string>()
let searchGeneration = 0
// Editing invalidates the previous request immediately, including an A → B → A
// input change. Only submitting the form starts another API request.
watch(searchQuery, () => {
  searchGeneration++; busy.delete('search'); searchResults.value = []; searchCursor.value = null
  submittedQuery.value = ''; hasSearched.value = false; searchError.value = ''; searchUsedCursors.clear()
}, { flush: 'sync' })
async function search(more = false) {
  const q = more ? submittedQuery.value : searchQuery.value.trim()
  if (!q || busy.has('search') || (more && (!searchCursor.value || q !== searchQuery.value.trim()))) return
  const cursor = more ? searchCursor.value! : undefined
  if (!more) { submittedQuery.value = q; searchResults.value = []; searchCursor.value = null; hasSearched.value = false; searchUsedCursors.clear() }
  const generation = ++searchGeneration; busy.add('search'); searchError.value = ''
  const current = () => generation === searchGeneration && q === submittedQuery.value && q === searchQuery.value.trim()
  try {
    const result = await api.searchWorks(q, cursor)
    if (!current()) return
    const ids = new Set(searchResults.value.map(work => work.id))
    const additions = result.items.filter(work => { if (ids.has(work.id)) return false; ids.add(work.id); return true })
    searchResults.value = more ? [...searchResults.value, ...additions] : additions
    hasSearched.value = true
    if (cursor) searchUsedCursors.add(cursor)
    if (result.nextCursor && searchUsedCursors.has(result.nextCursor)) {
      searchCursor.value = null; useKunMessage('分页标记出现重复，已停止加载。可重新搜索后再试。', 'info')
    } else {
      searchCursor.value = result.nextCursor
      if (!additions.length && result.nextCursor) useKunMessage('此页没有新增结果，可继续加载。', 'info')
    }
  }
  catch (error) { if (current()) { searchError.value = error instanceof Error ? error.message : '搜索失败'; hasSearched.value = true } }
  finally { if (generation === searchGeneration) busy.delete('search') }
}
const detailOpen = ref(false)
const detailTab = ref<'overview' | 'characters' | 'manage'>('overview')
const selectedGameId = ref<string | null>(null)
const selectedWork = ref<WorkMetadata | null>(null)
const selectedGame = computed(() => selectedGameId.value
  ? games.value.find(g => g.id === selectedGameId.value) ?? null
  : selectedWork.value ? games.value.find(g => g.metadata?.id === selectedWork.value?.id) ?? null : null)
watch(() => selectedGame.value?.metadata, metadata => { if (detailOpen.value && selectedGameId.value && selectedGame.value) selectedWork.value = metadata ?? null })
const detailTitle = computed(() => selectedGame.value?.name ?? selectedWork.value?.title ?? '游戏资料')
const coverPickerOpen = ref(false)
const coverOptionsWork = ref<WorkMetadata | null>(null)
const coverError = ref('')
let coverLoadGeneration = 0, coverSaveGeneration = 0
const detailCoverUrl = computed(() => selectedGame.value
  ? gameCoverUrl({ ...selectedGame.value, metadata: selectedWork.value ?? selectedGame.value.metadata }, snapshot.value?.settings.includeNsfw ?? true)
  : workCoverUrl(selectedWork.value, snapshot.value?.settings.includeNsfw ?? true))
const globalCoverDisplay = computed(() => snapshot.value?.settings.nonPortraitCoverDisplay ?? 'blur')
const detailCoverDisplay = computed(() => gameCoverDisplay(selectedGame.value, globalCoverDisplay.value))
function resetCoverPicker() {
  coverLoadGeneration++; coverSaveGeneration++
  coverDraftDirty.value = false
  coverPickerOpen.value = false; coverOptionsWork.value = null; coverError.value = ''
  busy.delete('cover-options'); busy.delete('cover-save')
}
function currentCoverContext(gameId: string | null, workId: string | null, generation: number) {
  return detailOpen.value && coverPickerOpen.value && detailGeneration === generation
    && (selectedGame.value?.id ?? null) === gameId && (selectedWork.value?.id ?? null) === workId
}
async function loadCoverOptions() {
  const work = selectedWork.value, gameId = selectedGame.value?.id ?? null, detail = detailGeneration
  if (!detailOpen.value || !coverPickerOpen.value || !work) return
  const request = ++coverLoadGeneration
  busy.add('cover-options'); coverError.value = ''
  try {
    const full = await api.getWork(work.id)
    if (request === coverLoadGeneration && currentCoverContext(gameId, work.id, detail)) {
      if (full.id !== work.id) throw new Error('作品资料已合并，请先刷新资料后再选择封面。')
      coverOptionsWork.value = full
    }
  } catch (error) {
    if (request === coverLoadGeneration && currentCoverContext(gameId, work.id, detail)) coverError.value = error instanceof Error ? error.message : '封面列表读取失败，请重试。'
  } finally { if (request === coverLoadGeneration) busy.delete('cover-options') }
}
async function toggleCoverPicker() {
  if (coverPickerOpen.value) { await requestCloseCoverPicker(); return }
  const generation = detailGeneration
  coverPickerOpen.value = true; detailTab.value = 'overview'
  void nextTick(() => { if (detailOpen.value && coverPickerOpen.value && generation === detailGeneration) document.querySelector?.<HTMLButtonElement>('#detail-cover-picker .picker-close')?.focus() })
  await loadCoverOptions()
}
function closeCoverPicker() {
  const generation = detailGeneration
  coverLoadGeneration++; coverSaveGeneration++; coverPickerOpen.value = false
  busy.delete('cover-options'); busy.delete('cover-save')
  void nextTick(() => { if (detailOpen.value && !coverPickerOpen.value && generation === detailGeneration) document.querySelector?.<HTMLButtonElement>('.detail-cover-settings')?.focus() })
}
watch(() => [selectedGame.value?.id, selectedWork.value?.id], ([gameId, workId], [oldGameId, oldWorkId]) => {
  if (coverPickerOpen.value && (gameId !== oldGameId || workId !== oldWorkId)) resetCoverPicker()
}, { flush: 'sync' })
async function saveCover(coverId: string | null | undefined, display: NonPortraitCoverDisplay | null) {
  const game = selectedGame.value, workId = selectedWork.value?.id ?? null, detail = detailGeneration
  if (!detailOpen.value || !coverPickerOpen.value || !game || (game.metadata?.id ?? null) !== workId || busy.has('cover-save')) return
  const request = ++coverSaveGeneration
  busy.add('cover-save'); coverError.value = ''
  try {
    await api.setGameCover(game.id, workId, coverId, display)
    await reload()
    if (request === coverSaveGeneration && currentCoverContext(game.id, workId, detail)) useKunMessage('已保存封面与展示方式，之后仍可更换。', 'success')
  } catch (error) {
    if (request === coverSaveGeneration && currentCoverContext(game.id, workId, detail)) coverError.value = error instanceof Error ? error.message : '封面保存失败，请重试。'
  } finally { if (request === coverSaveGeneration) busy.delete('cover-save') }
}
async function pickLocalCover(display: NonPortraitCoverDisplay | null) {
  const game = selectedGame.value, workId = selectedWork.value?.id ?? null, detail = detailGeneration
  if (!detailOpen.value || !coverPickerOpen.value || !game || (game.metadata?.id ?? null) !== workId || busy.has('cover-save')) return
  const request = ++coverSaveGeneration
  busy.add('cover-save'); coverError.value = ''
  try {
    const result = await api.pickLocalCover(game.id, workId, display)
    if (!result) return
    await reload()
    if (request === coverSaveGeneration && currentCoverContext(game.id, workId, detail)) useKunMessage('本地图片已保存，之后仍可更换。', 'success')
  } catch (error) {
    if (request === coverSaveGeneration && currentCoverContext(game.id, workId, detail)) coverError.value = error instanceof Error ? error.message : '本地图片保存失败，请重试。'
  } finally { if (request === coverSaveGeneration) busy.delete('cover-save') }
}
const editor = reactive({ name: '', status: 'planned' as PlayStatus, personalRating: '', categories: '', tags: '', notes: '', launchArgs: '' })
const draftBaseline = reactive({ name: '', status: 'planned' as PlayStatus, rating: null as number | null, categories: [] as string[], tags: [] as string[], notes: '', launchArgs: '[]', nameEdits: 0 })
const coverDraftDirty = ref(false), detailClosePending = ref(false), detailReadNeeded = ref(false)
const detailSaveError = ref(''), argumentsSaveError = ref('')
const draftRating = () => String(editor.personalRating).trim() === '' ? null : Number(editor.personalRating)
const draftLabels = (text: string) => [...new Set(text.split(/[,，\n]/).map(value => value.trim()).filter(Boolean))]
const personalDraftDirty = computed(() => editor.status !== draftBaseline.status || draftRating() !== draftBaseline.rating || editor.notes !== draftBaseline.notes)
const bookDraftDirty = computed(() => editor.name.trim() !== draftBaseline.name || nameEditGeneration !== draftBaseline.nameEdits || JSON.stringify(draftLabels(editor.categories)) !== JSON.stringify(draftBaseline.categories) || JSON.stringify(draftLabels(editor.tags)) !== JSON.stringify(draftBaseline.tags))
function normalizedArguments(value: string) { try { const parsed = JSON.parse(value); return Array.isArray(parsed) && parsed.every(item => typeof item === 'string') ? JSON.stringify(parsed) : value } catch { return value } }
const argumentsDraftDirty = computed(() => normalizedArguments(editor.launchArgs) !== draftBaseline.launchArgs)
const detailDraftGroups = computed(() => [personalDraftDirty.value ? '个人记录' : '', bookDraftDirty.value ? '书架信息' : '', argumentsDraftDirty.value ? '启动参数' : '', coverDraftDirty.value ? '封面与展示' : ''].filter(Boolean))
const detailWriteBusy = computed(() => ['save-game', 'args', 'cover-save', 'preferred'].some(key => busy.has(key)))
const editingInstallation = computed(() => selectedGame.value?.installations.find(item => item.id === editorInstallationId))
const detailModalOpen = computed({ get: () => detailOpen.value, set: value => { if (value) detailOpen.value = true; else void requestCloseDetail() } })
function resetDetailSaveState() { detailSaveError.value = ''; argumentsSaveError.value = ''; detailReadNeeded.value = false; coverDraftDirty.value = false }
function draftSignature() { return JSON.stringify([editor, nameEditGeneration, coverDraftDirty.value]) }
async function confirmDiscardDetail(groups = detailDraftGroups.value): Promise<boolean> {
  if (detailClosePending.value || detailWriteBusy.value) return false
  if (!groups.length) return true
  const generation = detailGeneration, gameId = selectedGame.value?.id, signature = draftSignature()
  detailClosePending.value = true
  try {
    const confirmed = await useKunAlert({ title: '放弃未保存的修改？', message: `${groups.join('、')}尚未保存。选择继续编辑可保留当前输入；放弃不会撤回已经保存的内容。`, confirmText: '放弃修改', cancelText: '继续编辑', type: 'warning' })
    return confirmed && detailOpen.value && detailGeneration === generation && selectedGame.value?.id === gameId && draftSignature() === signature && !detailWriteBusy.value
  } finally { detailClosePending.value = false }
}
async function requestCloseCoverPicker() {
  if (detailWriteBusy.value || detailClosePending.value) return
  if (coverDraftDirty.value && !await confirmDiscardDetail(['封面与展示'])) return
  coverDraftDirty.value = false; closeCoverPicker()
}
async function requestCloseDetail(): Promise<boolean> {
  if (!detailOpen.value || detailClosePending.value || detailWriteBusy.value) return false
  if (detailReadNeeded.value) {
    const generation = detailGeneration, gameId = selectedGame.value?.id
    await reloadDetailState()
    if (!detailOpen.value || detailGeneration !== generation || selectedGame.value?.id !== gameId || detailReadNeeded.value || detailClosePending.value || detailWriteBusy.value) return false
  }
  if (detailDraftGroups.value.length && !await confirmDiscardDetail()) return false
  detailOpen.value = false; return true
}
async function showDetailTag(tag: string) { if (await requestCloseDetail()) { page.value = 'library'; resetFilters(); tagFilter.value = tag } }
let editorInitialName = ''
let editorGameId: string | null = null
let editorInstallationId: string | null = null
let nameEditGeneration = 0, savedNameEditGeneration = 0, argumentsEditGeneration = 0
function markNameEdited() { nameEditGeneration++ }
function markArgumentsEdited() { argumentsEditGeneration++ }
function editorInstallation(game: LibraryGame) { return game.installations.find(i => i.id === game.preferredInstallationId) ?? game.installations[0] }
function loadInstallationArguments(game: LibraryGame) {
  const installation = editorInstallation(game)
  editorInstallationId = installation?.id ?? null
  editor.launchArgs = JSON.stringify(installation?.arguments ?? [])
  draftBaseline.launchArgs = editor.launchArgs
  argumentsEditGeneration++
}
function fillEditor(game: LibraryGame) {
  resetDetailSaveState()
  editorInitialName = game.name; editorGameId = game.id; savedNameEditGeneration = nameEditGeneration
  Object.assign(editor, { name: game.name, status: game.status, personalRating: game.personalRating == null ? '' : String(game.personalRating), categories: game.categories.join('，'), tags: game.customTags.join('，'), notes: game.notes })
  Object.assign(draftBaseline, { name: game.name, status: game.status, rating: game.personalRating, categories: [...game.categories], tags: [...game.customTags], notes: game.notes, nameEdits: nameEditGeneration })
  loadInstallationArguments(game)
}
function clearEditor() {
  resetDetailSaveState()
  editorInitialName = ''; editorGameId = null; editorInstallationId = null; savedNameEditGeneration = nameEditGeneration; argumentsEditGeneration++
  Object.assign(editor, { name: '', status: 'planned', personalRating: '', categories: '', tags: '', notes: '', launchArgs: '[]' })
  Object.assign(draftBaseline, { name: '', status: 'planned', rating: null, categories: [], tags: [], notes: '', launchArgs: '[]', nameEdits: nameEditGeneration })
}
let detailGeneration = 0
let detailOrigin: HTMLElement | null = null
function closeDetailWithEscape(event: KeyboardEvent) {
  if (event.key === 'Escape' && detailOpen.value && !bindOpen.value && !reviewOpen.value && !scanOpen.value && !attachmentOpen.value && !reviewDetailOpen.value && !detailClosePending.value && !document.querySelector('[role="alertdialog"]')) {
    // KunUI listens on window; this same Escape must not cancel the alert just opened here.
    event.preventDefault(); event.stopPropagation(); void requestCloseDetail()
  }
}
async function showGame(game: LibraryGame) { detailGeneration++; resetCoverPicker(); busy.delete('detail'); detailOrigin = typeof HTMLElement !== 'undefined' && document.activeElement instanceof HTMLElement ? document.activeElement : null; selectedGameId.value = game.id; selectedWork.value = game.metadata; fillEditor(game); detailTab.value = 'overview'; detailOpen.value = true }
async function showWork(work: WorkMetadata) {
  const generation = ++detailGeneration
  resetCoverPicker()
  detailOrigin = typeof HTMLElement !== 'undefined' && document.activeElement instanceof HTMLElement ? document.activeElement : null
  const game = games.value.find(item => item.metadata?.id === work.id)
  selectedGameId.value = game?.id ?? null; selectedWork.value = work
  if (game) fillEditor(game); else clearEditor()
  detailTab.value = 'overview'; detailOpen.value = true; busy.add('detail')
  try {
    const full = await api.getWork(work.id)
    if (!detailOpen.value || generation !== detailGeneration || selectedWork.value?.id !== work.id) return
    selectedWork.value = full
    // A merged work ID can reveal a library entry only after the detail arrives.
    // Initialize that new target once; metadata refreshes never refill its draft.
    const current = selectedGame.value
    if (current && editorGameId !== current.id) { selectedGameId.value = current.id; fillEditor(current) }
  } catch (error) { if (detailOpen.value && generation === detailGeneration && selectedWork.value?.id === work.id) notifyError(error) }
  finally { if (generation === detailGeneration) busy.delete('detail') }
}
watch(detailOpen, value => { if (!value) { detailGeneration++; resetCoverPicker(); busy.delete('detail'); if (detailOrigin?.isConnected) setTimeout(() => detailOrigin?.focus(), 0) } }, { flush: 'sync' })
const splitTags = (text: string) => [...new Set(text.split(/[,，\n]/).map(v => v.trim()).filter(Boolean))]
function sameDetail(gameId: string, generation: number) { return detailOpen.value && detailGeneration === generation && selectedGame.value?.id === gameId }
// Synchronize the field before rendering a newly selected installation. A later
// IPC/reload callback must not overwrite edits made after that render.
watch(() => selectedGame.value && editorInstallation(selectedGame.value)?.id, () => {
  const game = selectedGame.value
  if (detailOpen.value && game && editorGameId === game.id && !argumentsDraftDirty.value && editorInstallationId !== (editorInstallation(game)?.id ?? null)) loadInstallationArguments(game)
}, { flush: 'sync' })
function syncInstallationArguments(game: LibraryGame, generation: number, previousInstallation: string | null, edits: number) {
  if (sameDetail(game.id, generation) && !argumentsDraftDirty.value && editorInstallation(selectedGame.value!)?.id === editorInstallation(game)?.id && editorInstallationId === previousInstallation && argumentsEditGeneration === edits && editorInstallationId !== (editorInstallation(game)?.id ?? null)) loadInstallationArguments(game)
}
const personalRecordError = ref('')
watch([detailOpen, () => selectedGame.value?.id], () => { personalRecordError.value = '' })
async function savePersonalRecord() {
  const game = selectedGame.value; if (!detailOpen.value || !game || editorGameId !== game.id || busy.has('save-game')) return
  if (detailReadNeeded.value) { await reloadDetailState(); return }
  const generation = detailGeneration
  personalRecordError.value = ''
  await task('save-game', async () => {
    let persisted = false
    try {
      const rating = String(editor.personalRating).trim() === '' ? null : Number(editor.personalRating)
      if (rating !== null && (!Number.isFinite(rating) || rating < 0 || rating > 10)) throw new Error('个人评分请填写 0 到 10 之间的数字。')
      const patch = { status: editor.status, personalRating: rating, notes: editor.notes }
      await api.updateGame(game.id, patch); persisted = true
      if (sameDetail(game.id, generation)) Object.assign(draftBaseline, { status: patch.status, rating: patch.personalRating, notes: patch.notes })
      await reload()
      if (sameDetail(game.id, generation)) {
        const newerDraft = editor.status !== patch.status || (String(editor.personalRating).trim() === '' ? null : Number(editor.personalRating)) !== patch.personalRating || editor.notes !== patch.notes
        useKunMessage(newerDraft ? '已保存先前的记录，刚输入的内容还未保存。' : '个人记录已保存。', newerDraft ? 'info' : 'success')
      }
    } catch (error) {
      if (sameDetail(game.id, generation)) {
        if (persisted) detailReadNeeded.value = true
        personalRecordError.value = persisted ? '记录已保存，但书架刷新失败。草稿已保留，请重新读取书架，不会重复保存。' : settingsFailure(error)
        if (persisted) useKunMessage(personalRecordError.value, 'warn'); else notifyError(error)
      }
    }
  })
}
async function saveGame() {
  const game = selectedGame.value; if (!detailOpen.value || !game || editorGameId !== game.id) return
  if (detailReadNeeded.value) { await reloadDetailState(); return }
  if (busy.has('save-game')) return
  const generation = detailGeneration, edits = nameEditGeneration
  detailSaveError.value = ''
  await task('save-game', async () => {
    let persisted = false
    try {
      const rating = draftRating()
      if (rating !== null && (!Number.isFinite(rating) || rating < 0 || rating > 10)) throw new Error('个人评分请填写 0 到 10 之间的数字。')
      const name = editor.name.trim() || editorInitialName, renamed = edits !== savedNameEditGeneration
      const patch = { ...(renamed ? { name } : {}), status: editor.status, personalRating: rating, categories: splitTags(editor.categories), customTags: splitTags(editor.tags), notes: editor.notes }
      await api.updateGame(game.id, patch); persisted = true
      if (sameDetail(game.id, generation)) {
        Object.assign(draftBaseline, { status: patch.status, rating: rating, categories: patch.categories, tags: patch.customTags, notes: patch.notes })
        if (renamed) { savedNameEditGeneration = edits; editorInitialName = name; draftBaseline.name = name; draftBaseline.nameEdits = edits }
      }
      await reload()
      if (sameDetail(game.id, generation)) useKunMessage(personalDraftDirty.value || bookDraftDirty.value ? '提交的书架信息已保存；之后的输入尚未保存。' : '书架信息已保存，启动参数和封面按各自按钮保存。', 'success')
    } catch (error) {
      if (sameDetail(game.id, generation)) {
        if (persisted) detailReadNeeded.value = true
        detailSaveError.value = persisted ? '书架信息已保存，但书架刷新失败。请重新读取，未保存的输入仍保留。' : settingsFailure(error)
      }
    }
  })
}
async function reloadDetailState() {
  const game = selectedGame.value, generation = detailGeneration
  if (!detailOpen.value || !game || !detailReadNeeded.value || detailWriteBusy.value || busy.has('detail-reload')) return
  await task('detail-reload', async () => {
    try {
      await reload()
      if (sameDetail(game.id, generation)) {
        if (!argumentsDraftDirty.value && editorInstallationId !== (editorInstallation(selectedGame.value!)?.id ?? null)) loadInstallationArguments(selectedGame.value!)
        detailReadNeeded.value = false; detailSaveError.value = ''; argumentsSaveError.value = ''; personalRecordError.value = ''; useKunMessage('书架状态已重新读取，未保存的输入仍保留。', 'success')
      }
    } catch (error) { if (sameDetail(game.id, generation)) detailSaveError.value = '书架仍未能读取，请重试；不会重复保存。' + settingsFailure(error) }
  })
}
async function addToLibrary() {
  const work = selectedWork.value, generation = detailGeneration
  if (!detailOpen.value || !work || selectedGame.value) return
  await task('add-game', async () => {
    const game = await api.addVirtualGame(work.id); await reload()
    if (detailOpen.value && detailGeneration === generation && selectedWork.value?.id === work.id) {
      selectedGameId.value = game.id; selectedWork.value = game.metadata
      if (editorGameId !== game.id) fillEditor(game)
    }
    useKunMessage(isPreview ? '已加入预览书架，刷新页面后重置。' : '已加入愿望单。', 'success')
  })
}
async function pickExecutable(targetInstallationId?: string) { const game = selectedGame.value; if (!game) return; if (!game.installations.length) { await prepareAttachment(game); return }; const generation = detailGeneration, edits = argumentsEditGeneration, installationId = editorInstallationId; await task('executable', async () => { const updated = await api.pickExecutable(game.id, targetInstallationId); if (updated) { await reload(); syncInstallationArguments(updated, generation, installationId, edits); useKunMessage('已设置游戏启动文件。', 'success') } }) }
async function saveArguments() {
  const game = selectedGame.value, installation = editingInstallation.value, generation = detailGeneration
  if (!detailOpen.value || !game || editorGameId !== game.id || !installation || busy.has('args')) return
  if (detailReadNeeded.value) { await reloadDetailState(); return }
  argumentsSaveError.value = ''
  await task('args', async () => {
    let persisted = false
    try {
      let args: unknown
      try { args = JSON.parse(editor.launchArgs) } catch { throw new Error('启动参数请使用 JSON 字符串数组，例如 ["-windowed"]。') }
      if (!Array.isArray(args) || args.some(item => typeof item !== 'string')) throw new Error('启动参数须为字符串数组。')
      const submitted = JSON.stringify(args)
      await api.setLaunchArguments(game.id, installation.id, args); persisted = true
      if (sameDetail(game.id, generation) && editorInstallationId === installation.id) draftBaseline.launchArgs = submitted
      await reload()
      if (sameDetail(game.id, generation) && editorInstallationId === installation.id) {
        const newer = argumentsDraftDirty.value
        if (!newer && editorInstallation(selectedGame.value!)?.id !== installation.id) loadInstallationArguments(selectedGame.value!)
        useKunMessage(newer ? '提交的参数已保存，之后的输入尚未保存。' : '启动参数已保存。', 'success')
      }
    } catch (error) {
      if (sameDetail(game.id, generation) && editorInstallationId === installation.id) { if (persisted) detailReadNeeded.value = true; argumentsSaveError.value = persisted ? '启动参数已保存，但书架刷新失败。请重新读取，不会重复保存。' : settingsFailure(error) }
    }
  })
}
async function removeGame() {
  const game = selectedGame.value, generation = detailGeneration
  if (!detailOpen.value || !game) return
  await task('remove', async () => {
    const confirmed = await useKunAlert({ title: '从书架移除？', message: `移除「${game.name}」的书架记录。磁盘中的游戏文件会保留。` })
    if (!confirmed || !sameDetail(game.id, generation)) return
    await api.removeGame(game.id)
    // The library notification can remove the entry before this reply returns.
    if (detailOpen.value && detailGeneration === generation && selectedGameId.value === game.id) detailOpen.value = false
    await reload(); useKunMessage('已从书架移除。', 'success')
  })
}
async function refreshMetadata() { const game = selectedGame.value; if (!game) return; const generation = detailGeneration; await task('refresh', async () => { const updated = await api.refreshGame(game.id); if (sameDetail(game.id, generation)) selectedWork.value = updated.metadata; await reload(); useKunMessage('游戏资料已更新。', 'success') }) }

const bindOpen = ref(false)
const bindTargetId = ref<string | null>(null)
const bindReviewContext = ref<{ reviewId: string; fingerprint: string } | null>(null)
const bindQuery = ref('')
const bindResults = ref<WorkMetadata[]>([])
const bindCursor = ref<string | null>(null)
const bindSubmittedQuery = ref('')
const bindHasSearched = ref(false)
const bindError = ref('')
const bindNotice = ref('')
const bindRetryMore = ref(false)
const bindUsedCursors = new Set<string>()
let bindGeneration = 0
function resetBindingSearch() {
  bindGeneration++; busy.delete('bind-search'); bindResults.value = []; bindCursor.value = null
  bindSubmittedQuery.value = ''; bindHasSearched.value = false; bindError.value = ''; bindNotice.value = ''; bindUsedCursors.clear()
}
// Editing is local: only submitting the form calls the API. Invalidate immediately
// so a response cannot become valid again after changing the input back.
watch(bindQuery, resetBindingSearch, { flush: 'sync' })
watch(bindTargetId, resetBindingSearch, { flush: 'sync' })
watch(bindOpen, open => { if (!open) resetBindingSearch() }, { flush: 'sync' })
watch(selectedGameId, (current, previous) => { if (current !== previous && bindOpen.value) bindOpen.value = false }, { flush: 'sync' })
watch(() => games.value.some(game => game.id === bindTargetId.value), exists => { if (!exists && bindOpen.value) bindOpen.value = false }, { flush: 'sync' })
async function beginBind() { resetBindingSearch(); bindReviewContext.value = null; bindTargetId.value = selectedGame.value?.id ?? null; bindQuery.value = selectedGame.value?.name ?? ''; bindOpen.value = true }
function searchReviewAgain(review: MatchReview) { if (busy.has('matching-resolve') || reviewReloadNeeded.value || currentReview.value?.reviewId !== review.reviewId) return; resetBindingSearch(); bindReviewContext.value = review.reviewId && review.decisionFingerprint ? { reviewId: review.reviewId, fingerprint: review.decisionFingerprint } : null; bindTargetId.value = review.gameId; bindQuery.value = games.value.find(game => game.id === review.gameId)?.name ?? review.name; reviewOpen.value = false; bindOpen.value = true }
async function searchBinding(more = false) {
  const query = more ? bindSubmittedQuery.value : bindQuery.value.trim(), target = bindTargetId.value
  if (!bindOpen.value || !target || !games.value.some(game => game.id === target) || !query || busy.has('bind-search')) return
  if (more && (!bindCursor.value || query !== bindQuery.value.trim())) return
  const cursor = more ? bindCursor.value! : undefined, generation = ++bindGeneration
  busy.add('bind-search'); bindError.value = ''; bindNotice.value = ''
  if (!more) { bindResults.value = []; bindCursor.value = null; bindHasSearched.value = false; bindSubmittedQuery.value = query; bindUsedCursors.clear() }
  const current = () => bindOpen.value && generation === bindGeneration && target === bindTargetId.value && query === bindQuery.value.trim() && games.value.some(game => game.id === target)
  try {
    const result = await api.searchWorks(query, cursor)
    if (!current()) return
    const ids = new Set(bindResults.value.map(work => work.id))
    const additions = result.items.filter(work => { if (ids.has(work.id)) return false; ids.add(work.id); return true })
    bindResults.value = more ? [...bindResults.value, ...additions] : additions
    bindHasSearched.value = true
    if (cursor) bindUsedCursors.add(cursor)
    if (result.nextCursor && bindUsedCursors.has(result.nextCursor)) {
      bindCursor.value = null; bindNotice.value = '暂时无法继续加载，请重新搜索。'
    } else {
      bindCursor.value = result.nextCursor
      if (!additions.length && result.nextCursor) bindNotice.value = '此页没有新增结果，可继续加载。'
    }
  } catch { if (current()) { bindRetryMore.value = more; bindError.value = more ? '更多结果加载失败，请重试。' : '搜索失败，请重试。' } }
  finally { if (generation === bindGeneration) busy.delete('bind-search') }
}
async function bindWork(work: WorkMetadata) {
  const game = games.value.find(item => item.id === bindTargetId.value)
  if (!game || !bindResults.value.some(item => item.id === work.id)) { useKunMessage('这部游戏或搜索结果已经失效，请重新搜索。', 'info'); return }
  const generation = bindGeneration, target = bindTargetId.value, detail = detailGeneration
  await task('bind', async () => {
    const context = bindReviewContext.value
    const linked = await api.linkGame(game.id, work.id, context?.reviewId, context?.fingerprint); await reload()
    if (detailOpen.value && detailGeneration === detail && selectedGameId.value === game.id) { selectedGameId.value = linked.id; selectedWork.value = linked.metadata }
    if (generation === bindGeneration && target === bindTargetId.value) bindOpen.value = false
    useKunMessage('已关联你选择的作品资料。', 'success')
  })
}

const scanOpen = ref(false)
const candidates = ref<ScanCandidate[]>([])
const chosen = ref<string[]>([])
const autoMatchOnImport = ref(true)
const scanQuery = ref('')
const scanView = ref<ScanView>('all')
const importError = ref('')
const scanReloadNeeded = ref(false)
const scanRows = computed(() => buildScanRows(candidates.value, games.value))
function candidateOwner(candidate: ScanCandidate): LibraryGame | undefined {
  const root = folderKey(candidate.folderPath)
  return games.value.find(game => game.installations.some(installation => { const owned = folderKey(installation.folderPath); return root === owned || root.startsWith(owned + '/') || owned.startsWith(root + '/') }))
}
const scannedLibraryIds = computed(() => {
  const folders = new Set(candidates.value.map(candidate => folderKey(candidate.folderPath)))
  return games.value.filter(game => game.installations.some(installation => folders.has(folderKey(installation.folderPath)))).map(game => game.id)
})
const newCandidates = computed(() => scanRows.value.filter(row => row.status === 'new').map(row => row.candidate))
const selectedScanIds = computed(() => validScanSelection(chosen.value, scanRows.value))
watch(() => scanRows.value.map(row => `${row.candidate.id}:${row.status}`).join('|'), () => { chosen.value = [...selectedScanIds.value] }, { flush: 'sync' })
function setScanSelection(ids: string[]) { if (!busy.has('import')) chosen.value = validScanSelection(ids, scanRows.value) }
let scanGeneration = 0
let scanOrigin: HTMLElement | null = null
watch(scanOpen, value => {
  if (value) return
  const generation = ++scanGeneration, origin = scanOrigin
  // The entry is temporarily disabled during the native scan, so the modal's
  // focus trap may have captured the document body instead of the entry.
  if (typeof requestAnimationFrame !== 'undefined') void nextTick(() => requestAnimationFrame(() => {
    if (generation === scanGeneration && !scanOpen.value && !detailOpen.value && !bindOpen.value && !reviewOpen.value && !attachmentOpen.value && origin?.isConnected) origin.focus({ preventScroll: true })
  }))
}, { flush: 'sync' })
async function scan(event?: MouseEvent) {
  if (busy.has('scan')) return
  if (!scanOpen.value && typeof HTMLElement !== 'undefined') scanOrigin = event?.currentTarget instanceof HTMLElement ? event.currentTarget : document.activeElement instanceof HTMLElement ? document.activeElement : null
  await task('scan', async () => { const result = await api.scanFolder(); if (result === null) return; scanGeneration++; candidates.value = result; chosen.value = newCandidates.value.map(candidate => candidate.id); scanQuery.value = ''; scanView.value = 'all'; importError.value = ''; scanReloadNeeded.value = false; scanOpen.value = true })
}
async function identifyScannedGames() {
  const generation = scanGeneration, ids = [...scannedLibraryIds.value]
  const started = await startMatching(ids, 'reidentify')
  if (started && scanOpen.value && generation === scanGeneration && !newCandidates.value.length) { scanOpen.value = false; page.value = 'library' }
}
async function importSelected() {
  const generation = scanGeneration, ids = [...selectedScanIds.value]
  if (scanReloadNeeded.value) {
    await task('import', async () => {
      try {
        await reload()
        if (generation === scanGeneration) { scanReloadNeeded.value = false; importError.value = ''; scanOpen.value = false; page.value = 'library' }
      } catch (error) {
        if (generation === scanGeneration) importError.value = '导入已完成，书架仍未能读取。请重新读取，或稍后重启程序。'
        throw error
      }
    })
    return
  }
  if (!ids.length) return
  await task('import', async () => {
    if (generation === scanGeneration) importError.value = ''
    const requestedMatch = autoMatchOnImport.value, waitingForMatching = matchingUnavailable.value
    const canMatch = requestedMatch && !!snapshot.value?.settings.hasApiKey && !waitingForMatching
    let saved = false
    try {
      const result = await api.importLocalGames(ids, canMatch)
      saved = true
      dismissedMatchingId.value = null
      if (generation === scanGeneration) scanReloadNeeded.value = true
      await reload()
      if (generation === scanGeneration) { scanReloadNeeded.value = false; scanOpen.value = false; page.value = 'library' }
      const nextStep = result.matchingStarted ? '识别进度和结果可在资料整理中查看。'
        : requestedMatch && !snapshot.value?.settings.hasApiKey ? '本地游戏已保留，配置 API 密钥后，可在“资料整理”中点击“只补缺失”。'
        : requestedMatch && waitingForMatching ? '本地游戏已保留，当前识别任务结束后，可在“资料整理”中点击“只补缺失”。' : ''
      useKunMessage(`已添加 ${result.added} 部游戏，跳过 ${result.skipped} 个重复条目。${result.warning || nextStep}`, result.warning ? 'info' : 'success', result.warning ? 8000 : 6000)
    } catch (error) {
      if (generation === scanGeneration) importError.value = saved ? '导入已完成，但书架读取失败。请重新读取，无需再次导入。' : '导入未完成，选择已保留。请检查目录与磁盘后重试。'
      throw error
    }
  })
}

const settingsForm = reactive({ baseUrl: 'https://api.nextmoe.dev/v2/', apiKey: '', includeNsfw: false, theme: 'light' as Theme, appearanceStyle: 'windows' as AppearanceStyle, readExecutableMetadata: true, nonPortraitCoverDisplay: 'blur' as NonPortraitCoverDisplay })
const settingsFields = ['baseUrl', 'includeNsfw', 'theme', 'appearanceStyle', 'readExecutableMetadata', 'nonPortraitCoverDisplay'] as const
type SettingsField = typeof settingsFields[number]
const settingsBaseline = reactive({ ...settingsForm })
const settingsEdits = { baseUrl: 0, includeNsfw: 0, theme: 0, appearanceStyle: 0, readExecutableMetadata: 0, nonPortraitCoverDisplay: 0, apiKey: 0 }
let adoptingSettings = false
let settingsSubmission: typeof settingsEdits | null = null
let connectionEpoch = 0
const settingsError = ref('')
const settingsWarning = ref('')
const connectionFeedback = ref<(ConnectionResult & { baseUrl: string }) | null>(null)
const settingsDirty = computed(() => settingsFields.some(field => settingsForm[field] !== settingsBaseline[field]) || !!settingsForm.apiKey.trim())
const connectionDraftDirty = computed(() => settingsForm.baseUrl !== settingsBaseline.baseUrl || settingsForm.includeNsfw !== settingsBaseline.includeNsfw || !!settingsForm.apiKey.trim())
const recognitionSettingsDirty = computed(() => settingsForm.baseUrl.trim() !== settingsBaseline.baseUrl || settingsForm.includeNsfw !== settingsBaseline.includeNsfw || settingsForm.readExecutableMetadata !== settingsBaseline.readExecutableMetadata || !!settingsForm.apiKey.trim())
function settingsValues(settings: PublicSettings) { return { baseUrl: settings.baseUrl, includeNsfw: settings.includeNsfw, theme: settings.theme, appearanceStyle: settings.appearanceStyle ?? 'windows', readExecutableMetadata: settings.readExecutableMetadata ?? true, nonPortraitCoverDisplay: settings.nonPortraitCoverDisplay ?? 'blur' } }
function setSettingsField(field: SettingsField, value: typeof settingsForm[SettingsField]) { Object.assign(settingsForm, { [field]: value }) }
function adoptSettings(action: () => void) { adoptingSettings = true; try { action() } finally { adoptingSettings = false } }
function invalidateConnection() { connectionEpoch++; connectionFeedback.value = null }
for (const field of [...settingsFields, 'apiKey'] as const) watch(() => settingsForm[field], () => { if (!adoptingSettings) { settingsEdits[field]++; settingsError.value = '' } }, { flush: 'sync' })
watch(() => JSON.stringify(snapshot.value?.settings), (_current, previous) => {
  const settings = snapshot.value?.settings
  if (!settings) return
  const values = settingsValues(settings)
  adoptSettings(() => {
    for (const field of settingsFields) {
      // Notifications may arrive before a save acknowledgement. Keep subsequent edits,
      // including a deliberate return to the value that was saved before this request.
      if (settingsForm[field] === settingsBaseline[field] && (!settingsSubmission || settingsEdits[field] === settingsSubmission[field])) setSettingsField(field, values[field])
    }
    Object.assign(settingsBaseline, values)
  })
  if (previous !== undefined && _current !== previous) invalidateConnection()
  document.documentElement.dataset.appearance = settings.appearanceStyle ?? 'windows'
  applyTheme(settings.theme)
}, { flush: 'sync' })
watch(() => JSON.stringify([snapshot.value?.settings.baseUrl, snapshot.value?.settings.includeNsfw, snapshot.value?.settings.hasApiKey]), (_current, previous) => { if (previous !== undefined) { searchGeneration++; searchResults.value = []; searchCursor.value = null; submittedQuery.value = ''; hasSearched.value = false; searchError.value = ''; busy.delete('search') } })
function applyTheme(theme: Theme) { document.documentElement.classList.toggle('kun-dark-mode', theme === 'dark' || (theme === 'system' && matchMedia('(prefers-color-scheme: dark)').matches)) }
const systemThemeChanged = () => { if (snapshot.value?.settings.theme === 'system') applyTheme('system') }
function workspaceShortcut(event: KeyboardEvent) {
  if (event.defaultPrevented || detailOpen.value || bindOpen.value || scanOpen.value || reviewOpen.value || maintenanceOpen.value || attachmentOpen.value || reviewDetailOpen.value || event.altKey || event.isComposing) return
  if (savedViewEditor.value) { if (event.key === 'Escape') { event.preventDefault(); closeSavedViewEditor() }; return }
  if ((event.ctrlKey || event.metaKey) && ['f', 'k'].includes(event.key.toLowerCase())) {
    event.preventDefault(); navigate('library'); void nextTick(() => { const input = document.querySelector<HTMLInputElement>('input[aria-label="搜索书架"]'); input?.focus(); input?.select() })
  } else if (event.key === 'Escape' && selectionMode.value) { event.preventDefault(); leaveSelection() }
}
function settingsFailure(error: unknown) { return (error instanceof Error ? error.message : '操作失败，请重试。').replace(/nmk_(?:live|test)_[\w-]+/g, '[已隐藏密钥]') }
function resetSettingsDraft() {
  if (busy.has('settings') || busy.has('connection')) return
  adoptSettings(() => { for (const field of settingsFields) setSettingsField(field, settingsBaseline[field]); settingsForm.apiKey = '' })
  settingsError.value = ''
}
async function saveSettings(clear = false): Promise<boolean> {
  if (busy.has('settings') || busy.has('connection') || !snapshot.value) return false
  if (!clear && !settingsDirty.value && !settingsError.value) return true
  const edits = { ...settingsEdits }
  // Removing a saved credential is independent of every unsaved form field.
  const input = clear ? { ...settingsValues(snapshot.value.settings), clearApiKey: true } : {
    ...settingsValues({ ...settingsForm, hasApiKey: false }), baseUrl: settingsForm.baseUrl.trim(),
    ...(settingsForm.apiKey.trim() ? { apiKey: settingsForm.apiKey.trim() } : {})
  }
  settingsError.value = ''; settingsWarning.value = ''; invalidateConnection(); settingsSubmission = edits
  return (await task('settings', async () => {
    let persisted = false
    try {
      const { warning, ...saved } = await api.saveSettings(input)
      persisted = true
      settingsWarning.value = warning ? settingsFailure(new Error(warning)) : ''
      const values = settingsValues(saved)
      adoptSettings(() => {
        if (!clear) {
          for (const field of settingsFields) if (settingsEdits[field] === edits[field]) setSettingsField(field, values[field])
          if (settingsEdits.apiKey === edits.apiKey) settingsForm.apiKey = ''
        }
        Object.assign(settingsBaseline, values)
      })
      snapshot.value = { ...snapshot.value!, settings: saved }
      await reload()
      useKunMessage(settingsWarning.value || (clear ? '已移除保存的密钥；未保存的输入仍保留。' : isPreview ? '预览偏好已更新；密钥不会保存在浏览器。' : settingsDirty.value ? '提交的设置已保存；之后的编辑尚未保存。' : '全部设置已保存。'), settingsWarning.value ? 'warn' : 'success')
      return true
    } catch (error) {
      settingsError.value = persisted ? `设置已保存，但刷新失败：${settingsFailure(error)}` : `设置保存未确认：${settingsFailure(error)}`
      return false
    } finally { settingsSubmission = null }
  })) ?? false
}
async function testConnection() {
  if (busy.has('settings') || busy.has('connection')) return
  const epoch = connectionEpoch, baseUrl = snapshot.value?.settings.baseUrl ?? settingsBaseline.baseUrl
  await task('connection', async () => {
    try {
      const result = await api.testConnection()
      if (epoch === connectionEpoch) connectionFeedback.value = { ...result, message: settingsFailure(new Error(result.message)), baseUrl }
    } catch (error) {
      if (epoch === connectionEpoch) connectionFeedback.value = { ok: false, message: settingsFailure(error), baseUrl }
    }
  })
}
async function saveAndTestConnection() { if (await saveSettings()) await testConnection() }
async function importBackup() { await task('backup-import', async () => { const result = await api.importLibrary(); if (result) { await reload(); useKunMessage(`已导入 ${result.added} 条书架记录，跳过 ${result.skipped} 条。`, 'success') } }) }
async function exportBackup() { await task('export', async () => { if (await api.exportLibrary()) useKunMessage('书架已导出。', 'success') }) }
async function openDataFolder() { await task('data', () => api.openDataFolder()) }
async function openGameFolder() { if (selectedGame.value) await task('folder', () => api.openGameFolder(selectedGame.value!.id)) }
async function finishSession() { await task('finish', async () => { await api.finishSession(); await reload(); useKunMessage('本次计时已结束，游戏会继续运行。', 'info') }) }
async function setPreferred(id: string) {
  const game = selectedGame.value; if (!game || detailWriteBusy.value || detailReadNeeded.value || id === game.preferredInstallationId) return
  const generation = detailGeneration, edits = argumentsEditGeneration, installationId = editorInstallationId, signature = normalizedArguments(editor.launchArgs), discard = argumentsDraftDirty.value
  if (discard && !await confirmDiscardDetail(['启动参数'])) return
  if (!sameDetail(game.id, generation)) return
  await task('preferred', async () => {
    const updated = await api.updateGame(game.id, { preferredInstallationId: id })
    if (discard && sameDetail(game.id, generation) && argumentsEditGeneration === edits && normalizedArguments(editor.launchArgs) === signature) loadInstallationArguments(updated)
    await reload(); syncInstallationArguments(updated, generation, installationId, edits)
  })
}
const activeSeconds = computed(() => snapshot.value?.activeSession?.elapsedSeconds ?? 0)
const activeGame = computed(() => games.value.find(g => g.id === snapshot.value?.activeSession?.gameId))
let unsubscribe: (() => void) | undefined
let themeQuery: MediaQueryList | undefined
onMounted(async () => { document.addEventListener('keydown', closeDetailWithEscape); document.addEventListener('keydown', workspaceShortcut); themeQuery = matchMedia('(prefers-color-scheme: dark)'); themeQuery.addEventListener?.('change', systemThemeChanged); try { await reload(); unsubscribe = api.onChanged(() => { reload().catch(notifyError) }) } catch (error) { initialError.value = error instanceof Error ? error.message : '书架读取失败' } finally { loading.value = false } })
onUnmounted(() => { document.removeEventListener('keydown', closeDetailWithEscape); document.removeEventListener('keydown', workspaceShortcut); themeQuery?.removeEventListener?.('change', systemThemeChanged); unsubscribe?.() })
</script>

<template>
  <DesktopTitlebar @home="navigate('library')" />
  <LiquidGlassMaterial v-if="snapshot?.settings.appearanceStyle === 'glass'" />
  <div class="app-shell" :class="{ 'library-page': page === 'library' }">
    <aside class="sidebar compact-sidebar" aria-label="应用导航">
      <button class="brand" aria-label="The Order，返回我的书架" title="The Order" @click="navigate('library')"><span class="brand-mark"><OrderMark /></span></button>
      <nav aria-label="主导航"><button v-for="item in navigation" :key="item.id" :class="['nav-item', { active: page === item.id }]" :aria-label="item.label" :title="item.label" :aria-current="page === item.id ? 'page' : undefined" @click="navigate(item.id)"><component :is="item.icon" :size="20" /><span class="nav-compact-label">{{ item.shortLabel }}</span></button><button class="nav-item maintenance-entry" aria-label="资料整理" title="资料整理" @click="maintenanceOpen = true"><Sparkles :size="20" /><span class="nav-compact-label">整理</span><span v-if="matchReviews.length" class="nav-review-dot" aria-hidden="true"></span></button><button class="nav-item" aria-label="快捷收藏" title="快捷收藏" :aria-expanded="quickCollectionsOpen" @click="quickCollectionsOpen = true"><Heart :size="20" /><span class="nav-compact-label">收藏</span></button></nav>
      <div class="sidebar-bottom"><button :class="['nav-item', { active: page === 'settings' }]" aria-label="偏好设置" title="偏好设置" :aria-current="page === 'settings' ? 'page' : undefined" @click="navigate('settings')"><Settings :size="20" /><span class="nav-compact-label">设置</span></button></div>
    </aside>

    <div :class="['workspace', { 'library-workspace': page === 'library', 'library-busy': matchingRunning || !!snapshot?.activeSession }]">
      <div v-if="isPreview" class="preview-notice"><Monitor :size="14" /><span>界面预览 · 示例封面来自真实资料，书架操作仅在内存中生效。打开桌面版使用本地功能。</span></div>
      <header class="topbar" :class="{ 'compact-library-topbar': page === 'library' }"><div v-if="page === 'library'" class="library-heading"><h1 tabindex="-1">我的书架 <span class="heading-count">{{ games.length }}</span></h1><SectionSignature name="Avalon" /></div><div v-else class="breadcrumb">The Order <ChevronRight :size="13" /> {{ page === 'settings' ? '偏好设置' : navigation.find(n => n.id === page)?.label }}</div>
        <div v-if="page === 'library'" class="library-tools"><label class="search-field"><Search :size="16" /><input v-model="libraryQuery" placeholder="搜索名称、别名、标签…" aria-label="搜索书架" /><button v-if="libraryQuery" aria-label="清空搜索" @click="libraryQuery = ''"><X :size="14" /></button></label><select v-model="sort" aria-label="游戏排序" class="sort-select"><option value="added">最近添加</option><option value="recent">最近游玩</option><option value="name">名称排序</option><option value="rating">我的评分</option><option value="playtime">游玩时长</option></select><div class="library-view-actions"><button aria-label="封面视图" :aria-pressed="libraryLayout === 'grid'" :class="{ active: libraryLayout === 'grid' }" @click="libraryLayout = 'grid'"><LayoutGrid :size="17" /></button><button aria-label="列表视图" :aria-pressed="libraryLayout === 'list'" :class="{ active: libraryLayout === 'list' }" @click="libraryLayout = 'list'"><Library :size="17" /></button></div></div>
        <div class="topbar-actions"><button v-if="page === 'library' && !selectionMode && games.length" class="filter-button" @click="enterSelection"><CheckCircle2 :size="16" /> 选择整理</button><span v-if="page !== 'library'" class="local-badge"><ShieldCheck :size="14" /> 本地书架</span><KunButton color="primary" variant="flat" size="sm" :loading="busy.has('scan')" @click="scan"><Plus :size="16" /> 添加游戏</KunButton></div></header>
      <section v-if="matchingVisible && matching" class="matching-banner" :class="{ running: matchingRunning }" :aria-label="reidentifying ? '游戏重新识别进度' : '游戏资料补全进度'">
        <span class="matching-icon"><LoaderCircle v-if="matchingRunning" :size="20" class="spin" /><CheckCircle2 v-else-if="matching.status === 'completed'" :size="20" /><Square v-else :size="17" /></span>
        <div class="matching-banner-main"><div class="matching-heading"><strong>{{ matchingLabel }}</strong><span>{{ matching.processed }} / {{ matching.total }}</span></div><p v-if="matchingRunning" role="status" aria-live="polite">{{ matching.currentName ? `正在识别：${matching.currentName}` : '正在准备下一部作品…' }}</p><p v-else>{{ reidentifying ? '已核实' : '已补全' }} {{ matching.matched }} 部 · 未自动匹配 {{ matching.unmatched }} 部<span v-if="matching.failed"> · 失败 {{ matching.failed }} 部</span>。已完成的结果已保留。</p><div class="matching-progress" role="progressbar" :aria-valuemin="0" :aria-valuemax="matching.total || 1" :aria-valuenow="matching.processed" :aria-label="matchingLabel"><i :style="{ width: `${matchingPercent}%` }"></i></div></div>
        <div class="matching-banner-actions"><KunButton v-if="matchReviews.length" variant="flat" color="primary" size="sm" @click="openMatchReviews">待核对 {{ matchReviews.length }} 项 <ChevronRight :size="14" /></KunButton><KunButton v-if="matchingRunning" variant="light" size="sm" :loading="busy.has('matching-cancel')" @click="cancelMatching">{{ reidentifying ? '取消识别' : '取消补全' }}</KunButton><button v-else class="matching-dismiss" :aria-label="reidentifying ? '收起重新识别结果' : '收起资料补全结果'" @click="dismissedMatchingId = matching.id"><X :size="16" /></button></div>
      </section>
      <main :class="{ 'library-main': page === 'library' }">
        <div v-if="loading" class="empty-state"><LoaderCircle class="spin" :size="32" /><h2>正在打开你的书架</h2></div>
        <div v-else-if="initialError" class="empty-state"><CircleHelp :size="32" /><h2>书架暂时无法打开</h2><p>{{ initialError }}</p><KunButton @click="task('retry', async () => { await reload(); initialError = '' })">重新读取</KunButton></div>
        <template v-else-if="page === 'library'">
          <section class="library-controls">
            <div class="library-filter-line">
              <div class="library-smart-views" role="group" aria-label="书架快捷视图"><button v-for="view in smartViews" :key="view.value" :class="{ active: libraryView === view.value }" :aria-pressed="libraryView === view.value" @click="libraryView = view.value">{{ view.label }}</button></div>
              <div class="status-tabs" role="group" aria-label="游玩状态"><button :class="{ active: statusFilter === 'all' }" :aria-pressed="statusFilter === 'all'" @click="statusFilter = 'all'">全部进度</button><button v-for="status in statuses" :key="status.value" :class="{ active: statusFilter === status.value }" :aria-pressed="statusFilter === status.value" @click="statusFilter = status.value">{{ status.label }}</button></div>
              <button :class="['filter-button', { selected: filtersOpen || activeFilterCount > 0 }]" :aria-expanded="filtersOpen" aria-controls="library-expanded-filters" @click="filtersOpen = !filtersOpen"><SlidersHorizontal :size="16" /> 筛选 <small v-if="activeFilterCount">{{ activeFilterCount }}</small></button>
            </div>
            <div v-if="filtersOpen" class="expanded-filters" id="library-expanded-filters"><label>分类<select v-model="categoryFilter"><option value="">全部分类</option><option v-if="categoryFilter &amp;&amp; !allCategories.includes(categoryFilter)" :value="categoryFilter">{{ categoryFilter }}（已不存在）</option><option v-for="c in allCategories" :key="c">{{ c }}</option></select></label><label>标签<select v-model="tagFilter"><option value="">全部标签</option><option v-if="tagFilter &amp;&amp; !allTags.includes(tagFilter)" :value="tagFilter">{{ tagFilter }}（已不存在）</option><option v-for="t in allTags" :key="t">{{ t }}</option></select></label><label>我的评分<select v-model="minRating"><option value="">不限</option><option value="8">8 分及以上</option><option value="6">6 分及以上</option><option value="0">已评分</option></select></label><label class="filter-check"><input v-model="favoritesOnly" type="checkbox" /> 只看收藏</label><label class="filter-check"><input v-model="overviewVisible" type="checkbox" /> 显示继续游玩</label><button class="text-button" @click="resetFilters">重置筛选</button><button class="text-button" :disabled="busy.has('bulk') || savedViewsReadOnly" @click="openSavedViewEditor($event)">保存当前筛选</button></div>
            <div v-if="activeFilterCount || libraryQuery" class="active-filter-chips" aria-label="当前筛选"><button v-if="libraryQuery" @click="libraryQuery = ''">搜索：{{ libraryQuery }} <X :size="12" /></button><button v-if="categoryFilter" aria-label="移除分类筛选" @click="categoryFilter = ''">分类：{{ categoryFilter }} <X :size="12" /></button><button v-if="tagFilter" aria-label="移除标签筛选" @click="tagFilter = ''">标签：{{ tagFilter }} <X :size="12" /></button><button v-if="favoritesOnly" @click="favoritesOnly = false">特别喜欢 <X :size="12" /></button><button v-if="minRating !== ''" @click="minRating = ''">{{ minRating === '0' ? '已评分' : `评分 ≥ ${minRating}` }} <X :size="12" /></button><button v-if="statusFilter !== 'all'" @click="statusFilter = 'all'">{{ statuses.find(status => status.value === statusFilter)?.label }} <X :size="12" /></button></div>
          </section>
          <LibrarySavedViews :views="savedViews" :games="games" :filters="libraryFilters" :sort="sort" :active-id="activeSavedViewId" :editor="savedViewEditor" :error="savedViewError" :disabled="busy.has('bulk')" :read-only="savedViewsReadOnly" :can-save="true" @apply="applySavedView" @create="openSavedViewEditor" @rename="renameSavedView" @save="saveNamedView" @update="updateSavedView" @remove="removeSavedView" @close="closeSavedViewEditor" @retry="retrySavedViews" />
          <LibraryOverview v-if="games.length && overviewVisible && !snapshot?.activeSession && !selectionMode && libraryView === 'all' && !libraryQuery && statusFilter === 'all' && !favoritesOnly && !categoryFilter && !tagFilter && minRating === ''" :games="games" :include-nsfw="snapshot?.settings.includeNsfw ?? true" @open="showGame" @play="launch" @filter="applyOverviewFilter" />
          <div v-if="libraryQuery || libraryView !== 'all' || statusFilter !== 'all' || favoritesOnly || categoryFilter || tagFilter || minRating !== ''" class="library-result-bar"><span>{{ filteredGames.length }} 部作品<span v-if="libraryView === 'unmatched' || libraryView === 'unconfigured'"> · {{ libraryView === 'unmatched' ? '缺资料' : '待设启动程序' }}</span></span><button v-if="libraryView === 'unmatched' || libraryView === 'unconfigured'" class="text-button" @click="resetFilters">查看全部书架</button></div>
          <section v-if="selectionMode" class="selection-toolbar" aria-label="批量整理"><div><strong>已选择 {{ selectedIds.length }} 部</strong><small>只整理当前结果中的勾选项</small></div><button :disabled="busy.has('bulk')" @click="selectVisible">全选当前结果{{ filteredGames.length > 1000 ? '（前1000部）' : '' }}</button><button :disabled="matchingUnavailable || !selectedLocalIds.length" @click="identifySelected">识别所选</button><button @click="leaveSelection">取消整理</button></section>
          <section v-if="selectionMode" class="batch-form" aria-label="整理选中的游戏">
            <label>游玩状态<select v-model="batchStatus" aria-label="批量游玩状态" :disabled="busy.has('bulk')"><option value="">保持原状态</option><option v-for="status in statuses" :key="status.value" :value="status.value">{{ status.label }}</option></select></label>
            <label>收藏<select v-model="batchFavorite" aria-label="批量收藏" :disabled="busy.has('bulk')"><option value="">保持原收藏</option><option value="yes">标记为喜欢</option><option value="no">取消喜欢</option></select></label>
            <label class="batch-label-field">分类<div><select v-model="batchCategoryMode" aria-label="分类操作" :disabled="busy.has('bulk')"><option value="add">添加</option><option value="remove">移除</option></select><input v-model="batchCategory" aria-label="批量分类" placeholder="分类名，逗号分隔" :disabled="busy.has('bulk')" /></div></label>
            <label class="batch-label-field">个人标签<div><select v-model="batchTagMode" aria-label="标签操作" :disabled="busy.has('bulk')"><option value="add">添加</option><option value="remove">移除</option></select><input v-model="batchTag" aria-label="批量个人标签" placeholder="标签，逗号分隔" :disabled="busy.has('bulk')" /></div></label>
            <KunButton color="primary" :loading="busy.has('bulk')" :disabled="!selectedIds.length" @click="organizeSelected">应用到 {{ selectedIds.length }} 部</KunButton>
          </section>
          <section v-if="snapshot?.activeSession" class="library-session" role="status"><Play :size="18" /><div><strong>{{ activeGame?.name || '正在游玩' }}</strong><span>计时中 · {{ hours(activeSeconds) }}</span></div><KunButton size="sm" variant="bordered" :loading="busy.has('finish')" @click="finishSession">结束计时</KunButton></section>
          <div v-if="filteredGames.length" :class="['game-grid', 'library-grid', { 'game-list': libraryLayout === 'list' }]"><GameCard v-for="game in filteredGames" :key="game.id" :game="game" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :global-display="globalCoverDisplay" :layout="libraryLayout" :selection-mode="selectionMode" :selected="selectedIds.includes(game.id)" :launch-disabled="!!snapshot?.activeSession || busy.has('launch')" @open="showGame(game)" @favorite="favorite(game)" @play="launch(game)" @configure="configureGame(game)" @toggle-selected="toggleSelection(game.id)" /></div>
          <section v-else class="empty-state"><span class="empty-brand"><OrderMark /></span><h2>{{ games.length ? '这里暂时还没有游戏' : '让第一段故事，住进书架' }}</h2><p>{{ games.length ? '换一个关键词，或调整筛选条件试试看。' : '导入电脑里的游戏，或从鲲的数据源发现下一部想玩的作品。' }}</p><div class="button-row"><KunButton v-if="games.length" variant="flat" @click="resetFilters">清空筛选</KunButton><template v-else><KunButton color="primary" :loading="busy.has('scan')" @click="scan"><FolderPlus :size="17" /> 导入本地游戏</KunButton><KunButton variant="bordered" @click="navigate('discover')"><Compass :size="17" /> 发现作品</KunButton></template></div><p v-if="!games.length && !snapshot?.settings.hasApiKey" class="empty-tip"><button @click="navigate('settings')">先配置 NextMoe API 密钥 <ArrowUpRight :size="12" /></button></p></section>
          <footer class="content-footer"><span>Ctrl + K 搜索书架 · 排序和视图自动记住</span><span>KunUI × NextMoe</span></footer>
        </template>

        <template v-else-if="page === 'discover'">
          <section class="page-heading"><div><h1>发现作品</h1><SectionSignature name="Planetarian" /><p>从鲲的多源资料中，找到下一次心动。</p></div><span class="connection-pill" :class="{ ready: snapshot?.settings.hasApiKey }"><span></span>{{ isPreview ? '预览资料' : snapshot?.settings.hasApiKey ? 'NextMoe 已配置' : '等待配置密钥' }}</span></section>
          <form class="discovery-search" @submit.prevent="search()"><Search :size="23" /><input v-model="searchQuery" placeholder="输入中文名、日文名或游戏别名…" aria-label="搜索作品名称" /><KunButton type="submit" color="primary" :loading="busy.has('search')" :disabled="!searchQuery.trim()">搜索作品 <ArrowUpRight :size="16" /></KunButton></form>
          <div class="discovery-note"><span>游戏资料来自 NextMoe · 鲲 Galgame</span><span>{{ snapshot?.settings.includeNsfw ? '包含成人分级作品' : '全年龄内容优先' }}</span></div>
          <div v-if="searchError" class="inline-error"><CircleHelp :size="18" /><span>{{ searchError }}</span><button @click="page = 'settings'">检查设置</button></div>
          <div v-if="busy.has('search') && !searchResults.length" class="loading-grid"><div v-for="i in 6" :key="i" class="skeleton-card"></div></div>
          <div v-else-if="searchResults.length" class="game-grid"><GameCard v-for="work in searchResults" :key="work.id" :work="work" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :global-display="globalCoverDisplay" @open="showWork(work)" /></div>
          <div v-else-if="hasSearched && !searchError" class="empty-state"><Search :size="36" /><h2>{{ searchCursor ? '此页暂时没有可见作品' : '没有找到匹配的作品' }}</h2><p>{{ searchCursor ? '仍有下一页，可以继续加载更多作品。' : '试试完整原名或其他别名；分级设置也可能影响结果。' }}</p></div>
          <div v-else-if="!searchError" class="discover-welcome"><div class="orbit-mark"><OrderMark /></div><h2>很多故事，只差一次相遇</h2><p>搜索名称即可查看封面、角色、制作公司和多源评分。<br />喜欢的作品可以先加入愿望单。</p><div class="discovery-features"><span><BookOpen :size="16" /> 作品与角色资料</span><span><Star :size="16" /> 多源评分</span><span><Heart :size="16" /> 你的愿望清单</span></div><KunButton v-if="!snapshot?.settings.hasApiKey && !isPreview" variant="flat" color="primary" @click="page = 'settings'">配置 API 密钥 <ChevronRight :size="15" /></KunButton></div>
          <div v-if="searchCursor" class="load-more"><KunButton variant="bordered" :loading="busy.has('search')" @click="search(true)">加载更多作品</KunButton></div>
        </template>

        <template v-else-if="page === 'categories'">
          <section class="page-heading"><div><h1>分类收藏</h1><SectionSignature name="Clover" /><p>用自己的方式，整理值得珍藏的故事。</p></div><KunButton v-if="games.length" variant="bordered" @click="applyOverviewFilter('all'); enterSelection()"><Tags :size="16" /> 批量整理书架</KunButton></section>
          <LibrarySavedViews :views="savedViews" :games="games" :filters="libraryFilters" :sort="sort" :active-id="activeSavedViewId" :editor="savedViewEditor" :error="savedViewError" :disabled="busy.has('bulk')" :read-only="savedViewsReadOnly" :can-save="false" @apply="applySavedView" @create="openSavedViewEditor" @rename="renameSavedView" @save="saveNamedView" @update="updateSavedView" @remove="removeSavedView" @close="closeSavedViewEditor" @retry="retrySavedViews" />
          <LibraryCollections :games="games" :tags="allTags" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :global-display="globalCoverDisplay" @favorites="page = 'library'; resetFilters(); favoritesOnly = true" @category="value => { page = 'library'; resetFilters(); categoryFilter = value }" @tag="value => { page = 'library'; resetFilters(); tagFilter = value }" />
        </template>

        <template v-else-if="page === 'stats'">
          <section class="page-heading"><div><h1>游玩统计</h1><SectionSignature name="Memoria" /><p>看见最近的游玩节奏，也记住一路读过的故事。</p></div></section>
          <PlayStatistics :games="games" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :active-session="snapshot?.activeSession" @open="showGame" />
        </template>

        <template v-else-if="page === 'settings'">
          <section class="page-heading"><div><h1>偏好设置</h1><SectionSignature name="Harmonia" /><p>连上喜欢的数据源，保留自己的使用习惯。</p></div></section>
          <PreferencesSettings :draft="settingsForm" :saved="snapshot?.settings ?? null" :dirty="settingsDirty" :saving="busy.has('settings')" :testing="busy.has('connection')" :error="settingsError" :warning="settingsWarning" :affects-matching="recognitionSettingsDirty" :connection="connectionFeedback" :connection-dirty="connectionDraftDirty" :matching="matchingRunning" :data-path="snapshot?.dataPath ?? ''" :importing="busy.has('backup-import')" :preview="isPreview" @change="Object.assign(settingsForm, $event)" @save="saveSettings()" @save-test="saveAndTestConnection" @test="testConnection" @clear-key="saveSettings(true)" @reset="resetSettingsDraft" @export="exportBackup" @import="importBackup" @data="openDataFolder" @external="external" />
        </template>
      </main>
      <div v-if="snapshot?.activeSession && page !== 'library'" class="session-player"><span class="playing-pulse"></span><div><strong>{{ activeGame?.name ?? '游戏运行中' }}</strong><small>正在计时 · {{ hours(activeSeconds) }} · 关闭窗口后在托盘继续</small></div><KunButton variant="flat" color="primary" size="sm" @click="finishSession"><Square :size="13" /> 结束计时（游戏继续）</KunButton></div>
    </div>
  </div>

  <KunModal v-model="quickCollectionsOpen" title="快捷收藏" size="sm" scroll-behavior="inside" inner-class-name="quick-collections-modal">
    <div class="quick-collections-list"><button class="collection-link" @click="navigate('library'); resetFilters(); favoritesOnly = true; quickCollectionsOpen = false"><Heart :size="18" /><span>特别喜欢</span><small>{{ games.filter(g => g.favorite).length }}</small></button><button v-for="category in allCategories" :key="category" class="collection-link" :title="category" @click="navigate('library'); resetFilters(); categoryFilter = category; quickCollectionsOpen = false"><span class="category-dot"></span><span>{{ category }}</span><small>{{ games.filter(g => g.categories.includes(category)).length }}</small></button></div>
    <p v-if="!allCategories.length" class="modal-help">在作品详情中添加分类，即可在这里快速打开。</p><div class="quick-collections-footer"><span>The Order · v{{ snapshot?.version ?? '0.7.1' }}</span><button class="text-button" @click="external('https://developer.nextmoe.dev/')">NextMoe · 鲲 <ArrowUpRight :size="13" /></button></div>
  </KunModal>
  <KunModal v-model="detailModalOpen" :title="detailTitle" size="xl" inner-class-name="game-detail-modal" scroll-behavior="inside" :is-dismissable="!detailWriteBusy && !detailClosePending" :is-close-request-dismissable="!detailWriteBusy && !detailClosePending">
    <div class="detail-layout"><aside class="detail-aside"><div class="detail-cover-block"><DetailCover :url="detailCoverUrl" :title="detailTitle" :display="detailCoverDisplay" /><button v-if="selectedWork || selectedGame" type="button" class="detail-cover-settings" :aria-expanded="coverPickerOpen" aria-controls="detail-cover-picker" @click="toggleCoverPicker">封面与展示</button></div><div class="detail-aside-controls"><KunButton v-if="selectedGame && getLaunchInstallation(selectedGame)?.executablePath" color="primary" full-width :loading="busy.has('launch')" :disabled="!!snapshot?.activeSession" @click="launch(selectedGame)"><Play :size="17" fill="currentColor" /> 开始游戏</KunButton><KunButton v-else-if="selectedGame" color="primary" full-width :loading="busy.has('executable')" @click="pickExecutable()"><FolderPlus :size="17" /> {{ selectedGame.installations.length ? '设置启动程序' : '添加本地游戏' }}</KunButton><KunButton v-else color="primary" full-width :loading="busy.has('add-game')" @click="addToLibrary"><Plus :size="17" /> 加入愿望单</KunButton><button v-if="selectedGame" :class="['detail-favorite', { active: selectedGame.favorite }]" @click="favorite(selectedGame)"><Heart :size="16" :fill="selectedGame.favorite ? 'currentColor' : 'none'" />{{ selectedGame.favorite ? '特别喜欢' : '标记为特别喜欢' }}</button><div v-if="selectedGame" class="detail-time"><Clock3 :size="15" /><span>{{ hours(selectedGame.totalPlaySeconds) }}</span><small>游玩 {{ selectedGame.playCount }} 次</small></div><div class="detail-facts"><span>发行日期</span><strong>{{ releaseDate(selectedWork) }}</strong><span>制作公司</span><strong>{{ selectedWork?.developers.join(' / ') || '暂未收录' }}</strong></div></div></aside>
      <div class="detail-main"><aside v-if="detailDraftGroups.length || detailWriteBusy || detailReadNeeded" class="detail-draft-status" role="status"><strong>{{ detailWriteBusy ? '正在保存，完成前请留在此页' : detailReadNeeded ? '保存已完成，书架状态需要重新读取' : '有未保存的修改' }}</strong><span v-if="detailDraftGroups.length">{{ detailDraftGroups.join('、') }} · 按对应区域的按钮保存</span><button v-if="detailReadNeeded" type="button" :disabled="busy.has('detail-reload')" @click="reloadDetailState">{{ busy.has('detail-reload') ? '正在读取…' : '重新读取书架' }}</button></aside><p v-if="selectedWork?.originalTitle && selectedWork.originalTitle !== detailTitle" class="original-title">{{ selectedWork.originalTitle }}</p><div class="detail-tabs"><button :class="{ active: detailTab === 'overview' }" @click="detailTab = 'overview'">作品资料</button><button :class="{ active: detailTab === 'characters' }" @click="detailTab = 'characters'">角色 <small>{{ selectedWork?.characters.length ?? 0 }}</small></button><button v-if="selectedGame" :class="{ active: detailTab === 'manage' }" @click="detailTab = 'manage'">管理</button><LoaderCircle v-if="busy.has('detail')" :size="17" class="spin" /></div>
      <CoverPicker v-if="coverPickerOpen" v-show="detailTab === 'overview'" id="detail-cover-picker" :work="coverOptionsWork?.id === selectedWork?.id ? coverOptionsWork : selectedWork" :game="selectedGame" :loading="busy.has('cover-options')" :saving="busy.has('cover-save')" :error="coverError" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :global-display="globalCoverDisplay" @save="saveCover" @pick-local="pickLocalCover" @retry="loadCoverOptions" @dirty="coverDraftDirty = $event" @close="requestCloseCoverPicker" /><template v-if="detailTab === 'overview'"><PersonalRecord v-if="selectedGame && !coverPickerOpen" :status="editor.status" :rating="editor.personalRating" :notes="editor.notes" :saving="busy.has('save-game')" :error="personalRecordError" @status="editor.status = $event" @rating="editor.personalRating = $event" @notes="editor.notes = $event" @save="savePersonalRecord" /><div v-if="selectedWork?.ratings.length" class="ratings-row"><div v-for="rating in selectedWork.ratings" :key="rating.source"><small>{{ sourceName(rating.source) }}</small><strong><Star :size="14" />{{ rating.score.toFixed(1) }}<em>/ {{ rating.scale }}</em></strong><span>{{ rating.votes != null ? `${rating.votes.toLocaleString()} 人评价` : '来源评分' }}</span></div></div><h3 class="detail-section-title">关于这段故事</h3><p class="description-text">{{ selectedWork?.description || '还没有关联简介。你可以在管理页搜索并关联准确的作品资料。' }}</p><div v-if="selectedWork?.tags.length" class="tag-cloud detail-tags"><button v-for="tag in selectedWork.tags" :key="tag" @click="showDetailTag(tag)">{{ tag }}</button></div><div v-if="selectedWork?.references.length || selectedWork?.webUrl" class="source-links"><h3 class="detail-section-title">资料出处</h3><button v-if="selectedWork.webUrl" @click="external(selectedWork.webUrl)">鲲 Galgame <ArrowUpRight :size="13" /></button><button v-for="reference in selectedWork?.references.filter(r => r.url)" :key="`${reference.source}-${reference.id}`" @click="external(reference.url)">{{ sourceName(reference.source) }} <ArrowUpRight :size="13" /></button></div></template>
      <template v-else-if="detailTab === 'characters'"><div v-if="selectedWork?.characters.length" class="character-grid"><article v-for="character in selectedWork.characters" :key="character.id"><img v-if="character.imageUrl" :src="character.imageUrl" :alt="character.name" loading="lazy" referrerpolicy="no-referrer" /><div v-else class="character-placeholder"><BookOpen :size="22" /></div><strong>{{ character.name }}</strong><small>{{ character.voiceActors.length ? `CV · ${character.voiceActors.join(' / ')}` : character.originalName }}</small></article></div><div v-else class="small-empty"><BookOpen :size="28" /><p>该作品暂未收录角色资料。</p></div></template>
      <template v-else-if="selectedGame"><div class="manage-form"><label class="field-label">书架名称<input v-model="editor.name" @input="markNameEdited" /></label><div class="form-columns"><label class="field-label">游玩状态<select v-model="editor.status"><option v-for="status in statuses" :key="status.value" :value="status.value">{{ status.label }}</option></select></label><label class="field-label">我的评分 <small>0–10，可留空</small><input v-model="editor.personalRating" type="text" inputmode="decimal" placeholder="还未评分" /></label></div><label class="field-label">分类<input v-model="editor.categories" placeholder="例如：视觉小说、待补票（逗号分隔）" /></label><label class="field-label">个人标签<input v-model="editor.tags" placeholder="例如：周末想玩、收藏版（逗号分隔）" /></label><label class="field-label">游玩笔记<textarea v-model="editor.notes" rows="4" placeholder="记下感想，或下一次想接着看的地方…"></textarea></label><KunButton color="primary" :loading="busy.has('save-game')" :disabled="detailReadNeeded" @click="saveGame"><Check :size="16" /> 保存书架信息</KunButton><p v-if="detailSaveError" class="detail-save-error" role="alert">{{ detailSaveError }}</p><div class="settings-divider"></div><h3>本地安装与资料</h3><div v-for="installation in selectedGame.installations" :key="installation.id" class="installation-row"><HardDrive :size="16" /><div><strong>{{ installation.executablePath ?? '尚未选择启动文件' }}</strong><small>{{ installation.folderPath }}</small></div><button class="text-button" :disabled="busy.has('executable')" @click="pickExecutable(installation.id)">设置程序</button><button v-if="selectedGame.installations.length > 1" class="text-button" @click="setPreferred(installation.id)">{{ selectedGame.preferredInstallationId === installation.id ? '默认启动' : '设为默认' }}</button></div><div class="button-row"><KunButton variant="bordered" size="sm" @click="pickExecutable()"><FolderPlus :size="14" /> {{ selectedGame.installations.length ? '设置启动程序' : '添加本地游戏' }}</KunButton><KunButton v-if="selectedGame.installations.length" variant="bordered" size="sm" @click="openGameFolder"><FolderOpen :size="14" /> 打开目录</KunButton><KunButton variant="bordered" size="sm" @click="beginBind"><Link2 :size="14" /> 关联作品</KunButton><KunButton v-if="selectedGame.installations.length" variant="bordered" size="sm" :disabled="matchingUnavailable" @click="identifyCurrentGame"><Sparkles :size="14" /> 重新识别此游戏</KunButton><KunButton v-if="selectedGame.metadata" variant="light" size="sm" :loading="busy.has('refresh')" @click="refreshMetadata"><RefreshCw :size="14" /> 刷新资料</KunButton></div><details v-if="selectedGame.installations.length" class="advanced-options"><summary>高级启动参数</summary><p class="arguments-target">参数对应：{{ editingInstallation?.folderPath || '原安装已不存在，请重新选择安装' }}<strong v-if="editingInstallation && editingInstallation.id !== selectedGame.preferredInstallationId">当前正在保留原安装的参数草稿，保存只作用于这个安装。</strong></p><label class="field-label">参数数组<input v-model="editor.launchArgs" @input="markArgumentsEdited" placeholder='["-windowed"]' /></label><KunButton variant="flat" size="sm" :loading="busy.has('args')" :disabled="detailReadNeeded || !editingInstallation" @click="saveArguments">保存参数</KunButton><p v-if="argumentsSaveError" class="arguments-save-error" role="alert">{{ argumentsSaveError }}</p></details><button class="remove-button" @click="removeGame">从书架移除这部游戏</button></div></template>
      </div></div>
  </KunModal>

  <KunModal v-model="bindOpen" title="选择要关联的作品" size="lg"><p class="modal-help">请核对名称与封面后选择，避免关联到同系列的其他作品。</p><form class="modal-search" @submit.prevent="searchBinding()"><input v-model="bindQuery" aria-label="关联作品搜索" placeholder="输入游戏名" /><KunButton type="submit" color="primary" :loading="busy.has('bind-search')">搜索</KunButton></form><div class="bind-results" :aria-busy="busy.has('bind-search')"><button class="binding-result" v-for="work in bindResults" :key="work.id" :disabled="busy.has('bind')" @click="bindWork(work)"><img v-if="workCoverUrl(work, snapshot?.settings.includeNsfw ?? true)" :src="workCoverUrl(work, snapshot?.settings.includeNsfw ?? true)!" :alt="work.title" /><div><strong>{{ work.title }}</strong><small>{{ work.originalTitle }}</small><span>{{ work.developers.join(' · ') }} · {{ releaseDate(work) }}</span></div><Link2 :size="17" /></button><div class="binding-pagination"><p v-if="bindError" class="binding-error" role="alert">{{ bindError }}</p><p v-else-if="bindNotice" role="status">{{ bindNotice }}</p><p v-else-if="busy.has('bind-search')" role="status">正在加载作品…</p><p v-else-if="bindHasSearched && !bindResults.length">没有找到作品，请试试其他名称。</p><p v-else-if="bindHasSearched && !bindCursor" role="status">已显示全部结果</p><KunButton v-if="bindError" size="sm" variant="bordered" :loading="busy.has('bind-search')" @click="searchBinding(bindRetryMore)">重试</KunButton><KunButton v-else-if="bindCursor" size="sm" variant="bordered" :loading="busy.has('bind-search')" :disabled="busy.has('bind-search')" @click="searchBinding(true)">{{ busy.has('bind-search') ? '加载中…' : '加载更多' }}</KunButton></div></div></KunModal>
  <KunModal v-model="scanOpen" title="确认导入本地游戏" size="xl" scroll-behavior="inside" inner-class-name="import-candidates-modal" :is-dismissable="!busy.has('import')" :is-close-request-dismissable="!busy.has('import')" :is-show-close-button="!busy.has('import')">
    <ImportCandidates :rows="scanRows" :chosen="selectedScanIds" :query="scanQuery" :view="scanView" :auto-match="autoMatchOnImport" :has-api-key="!!snapshot?.settings.hasApiKey" :matching-unavailable="matchingUnavailable" :busy="busy.has('import')" :scanning="busy.has('scan')" :known-game-count="scannedLibraryIds.length" :error="importError" :reload-needed="scanReloadNeeded" @selection="setScanSelection" @query="scanQuery = $event" @view="scanView = $event" @auto-match="autoMatchOnImport = $event" @cancel="scanOpen = false" @confirm="importSelected" @identify="identifyScannedGames" @rescan="scan" />
  </KunModal>
  <KunModal v-model="reviewModalOpen" title="核对游戏资料" size="xl" inner-class-name="matching-review-modal" scroll-behavior="inside" :is-dismissable="!busy.has('matching-resolve')" :is-close-request-dismissable="!busy.has('matching-resolve')" :is-show-close-button="!busy.has('matching-resolve')">
    <MatchReviewWorkspace :reviews="matchReviews" :games="games" :current="currentReview" :choice="reviewChoice" :busy="busy.has('matching-resolve')" :include-nsfw="snapshot?.settings.includeNsfw ?? true" :error="reviewError" :reload-needed="reviewReloadNeeded" :running="matchingRunning" @select="selectReview" @choice="chooseReview" @detail="showReviewDetail" @search="searchReviewAgain" @resolve="resolveReview" @later="laterReview" />
  </KunModal>
  <LibraryMaintenance v-model="maintenanceOpen" :matching="matching" :missing="missingGames" :unconfigured="unconfiguredGames" :unavailable="matchingUnavailable" :has-local="!!localGameIds.length" @start="startMatching(undefined, $event)" @cancel="cancelMatching" @review="openMatchReviews" @open="openMaintenanceGame" @configure="openMaintenanceGame($event, true)" @filter="showMaintenanceFilter" />
  <KunModal v-model="attachmentOpen" title="添加本地游戏" size="lg" scroll-behavior="inside" inner-class-name="attachment-modal"><p class="modal-help">为「{{ games.find(game => game.id === attachmentGameId)?.name }}」附加安装。原作品资料和个人记录会保留。</p><p class="attachment-root"><strong>所选根目录</strong>{{ attachmentScan?.rootPath }}</p><p v-if="attachmentError" class="inline-error" role="alert">{{ attachmentError }}</p><p v-if="!attachmentScan?.candidates.length" class="modal-help">没有发现可用的程序目录。请确认选择的是游戏目录，再重新选择。</p><div v-for="candidate in attachmentScan?.candidates" :key="candidate.id" class="attachment-candidate"><div><strong>{{ candidate.name }}</strong><span>{{ candidate.folderPath }}</span><small v-if="candidateOwner(candidate)">与「{{ candidateOwner(candidate)?.name }}」的安装目录重叠，请先核对已有条目。</small><small v-else>选择这个根目录中的程序；取消选择不会添加安装。</small></div><KunButton size="sm" :disabled="!!candidateOwner(candidate)" :loading="busy.has('attachment-commit')" @click="attachCandidate(candidate)">选择程序并添加</KunButton></div><div class="modal-footer"><KunButton variant="light" @click="attachmentOpen = false">取消</KunButton><KunButton variant="bordered" :disabled="busy.has('attachment-commit')" @click="attachmentOpen = false; scan()">返回普通导入</KunButton></div></KunModal>
  <KunModal v-model="reviewDetailOpen" title="候选作品详情" size="lg" scroll-behavior="inside" inner-class-name="review-detail-modal"><div v-if="reviewDetailWork" class="review-detail-body"><img v-if="workCoverUrl(reviewDetailWork, snapshot?.settings.includeNsfw ?? true)" :src="workCoverUrl(reviewDetailWork, snapshot?.settings.includeNsfw ?? true)!" :alt="reviewDetailWork.title" referrerpolicy="no-referrer" /><section><h3>{{ reviewDetailWork.title }}</h3><p v-if="reviewDetailWork.originalTitle !== reviewDetailWork.title">{{ reviewDetailWork.originalTitle }}</p><p>{{ releaseDate(reviewDetailWork) }} · {{ reviewDetailWork.developers.join(' / ') }}</p><p v-if="busy.has('review-detail')" role="status">正在读取完整资料…</p><p v-if="reviewDetailError" class="inline-error" role="alert">{{ reviewDetailError }}<button @click="showReviewDetail(reviewDetailWork)">重试</button></p><p class="description-text">{{ reviewDetailWork.description || '暂未收录简介。' }}</p><p>{{ reviewDetailWork.tags.join(' · ') }}</p></section></div></KunModal>
  <KunMessageProvider /><KunAlertProvider />
</template>

<style scoped>
.detail-cover-settings{width:100%;margin:-3px 0 12px;padding:9px 10px;border:1px solid var(--line);border-radius:8px;font-size:12px;color:var(--blue);background:var(--surface)}
.detail-cover-settings:hover{background:var(--blue-soft)}
.detail-cover-settings:focus-visible{outline:2px solid var(--blue);outline-offset:2px}
.detail-cover-block,.detail-aside-controls{min-width:0}.detail-facts strong{overflow-wrap:anywhere}
@media(max-width:700px){.detail-aside{display:grid;grid-template-columns:110px minmax(0,1fr);gap:14px;align-items:start}.detail-cover-block{min-width:0}.detail-cover-block .detail-artwork{width:110px;margin:0 0 8px}.detail-cover-block .detail-cover-settings{margin:0;font-size:12px;padding:8px 5px}.detail-aside-controls{display:flex;flex-direction:column;gap:8px}.detail-aside-controls .detail-favorite{justify-content:flex-start;padding:7px 0;grid-column:auto}.detail-aside-controls .detail-time{margin:0;padding:8px 0;grid-column:auto;flex-wrap:wrap}.detail-aside-controls .detail-facts{display:grid;grid-template-columns:max-content minmax(0,1fr);gap:5px 10px;margin:0;font-size:12px}.detail-aside-controls .detail-facts>span{margin:0}.detail-aside-controls .detail-facts>strong{line-height:1.5}}
</style>
