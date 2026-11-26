import assert from 'node:assert/strict'
import { test } from 'node:test'
import { promises as fs } from 'node:fs'
import vm from 'node:vm'
import { transform } from 'esbuild'
import * as vue from 'vue'
import * as libraryWorkspace from '../src/library-workspace'
import * as libraryInsights from '../src/library-insights'
import * as savedLibraryViews from '../src/saved-library-views'
import * as coverDisplay from '../src/cover-display'
import * as importWorkspace from '../src/import-workspace'
test('cover lists cannot leak across A-B-A navigation, retry or picker reopen', async t => {
  const { a, api, state } = await harness(t), A = completeGame('A'), B = completeGame('B')
  a.snapshot.value = state([A, B]); await vue.nextTick(); await a.showGame(A)
  const first = deferred<WorkMetadata>(), second = deferred<WorkMetadata>(), third = deferred<WorkMetadata>()
  const requests = [first, second, third]
  api.getWork = () => requests.shift()!.promise
  const old = a.toggleCoverPicker()
  await a.showGame(B); const middle = a.toggleCoverPicker()
  await a.showGame(A); const newest = a.toggleCoverPicker()
  first.resolve({ ...A.metadata!, description: 'stale A' }); await old
  assert.equal(a.coverOptionsWork.value, null)
  assert.equal(a.busy.has('cover-options'), true)
  second.reject(new Error('stale B error')); await middle
  assert.equal(a.coverError.value, '')
  third.resolve({ ...A.metadata!, description: 'fresh A', covers: [] }); await newest
  assert.equal(a.coverOptionsWork.value.description, 'fresh A')
  assert.equal(a.busy.has('cover-options'), false)
  const closed = deferred<WorkMetadata>(); api.getWork = () => closed.promise
  const pending = a.loadCoverOptions(); a.closeCoverPicker()
  api.getWork = async () => ({ ...A.metadata!, description: 'reopened A', covers: [] })
  await a.toggleCoverPicker(); closed.reject(new Error('late closed error')); await pending
  assert.equal(a.coverOptionsWork.value.description, 'reopened A')
  assert.equal(a.coverError.value, '')
})

test('cover save captures plain arguments, preserves unsaved management drafts and reflects effective artwork', async t => {
  const { a, api, state, messages } = await harness(t), A = completeGame('A')
  const cover = { id: '301', url: 'https://fixture.invalid/chosen.png', width: 1000, height: 600, votes: 0, sexual: 'safe' as const }
  a.snapshot.value = state([A]); await vue.nextTick(); await a.showGame(A)
  api.getWork = async () => ({ ...A.metadata!, covers: [cover] })
  await a.toggleCoverPicker()
  a.editor.notes = 'unsaved notes'; a.editor.name = 'unfinished name'; a.editor.personalRating = '8'
  const response = deferred<LibraryGame>(), calls: unknown[][] = []
  api.setGameCover = (...args: unknown[]) => { calls.push(args); return response.promise }
  api.getState = async () => state([{ ...A, coverChoice: { workId: A.metadata!.id, cover }, nonPortraitCoverDisplay: 'plain' }])
  const pending = a.saveCover('301', 'plain')
  assert.deepEqual(structuredClone(calls), [[A.id, A.metadata!.id, '301', 'plain']])
  await a.saveCover(null, 'crop')
  assert.equal(calls.length, 1)
  response.resolve(A); await pending; await vue.nextTick()
  assert.equal(a.editor.notes, 'unsaved notes'); assert.equal(a.editor.name, 'unfinished name')
  assert.equal(a.editor.personalRating, '8')
  assert.equal(a.detailCoverUrl.value, cover.url)
  assert.equal(a.busy.has('cover-save'), false)
  assert.equal(messages.at(-1)?.kind, 'success')
})

test('late cover save failures cannot pollute a reopened picker or clear its new pending save', async t => {
  const { a, api, state, messages } = await harness(t), A = completeGame('A')
  a.snapshot.value = state([A]); await vue.nextTick(); await a.showGame(A)
  api.getWork = async () => ({ ...A.metadata!, covers: [] }); await a.toggleCoverPicker()
  const old = deferred<LibraryGame>(), fresh = deferred<LibraryGame>(), requests = [old, fresh]
  api.setGameCover = () => requests.shift()!.promise
  const first = a.saveCover(undefined, 'plain'); a.closeCoverPicker(); await a.toggleCoverPicker()
  const second = a.saveCover(undefined, 'crop')
  old.reject(new Error('old save error')); await first
  assert.equal(a.coverError.value, ''); assert.equal(a.busy.has('cover-save'), true)
  fresh.reject(new Error('disk write failed')); await second
  assert.equal(a.coverError.value, '封面保存失败，请重试。')
  assert.equal(a.busy.has('cover-save'), false)
  assert.equal(messages.length, 0)
  api.setGameCover = async () => A; api.getState = async () => state([A])
  await a.saveCover(null, 'blur')
  assert.equal(a.coverError.value, '')
  assert.equal(messages.at(-1)?.kind, 'success')
})

test('cover options refuse merged work details and close on reassociation or removal', async t => {
  const { a, api, state } = await harness(t), A = completeGame('A')
  a.snapshot.value = state([A]); await vue.nextTick(); await a.showGame(A)
  api.getWork = async () => ({ ...A.metadata!, id: 'merged-work' }); await a.toggleCoverPicker()
  assert.equal(a.coverOptionsWork.value, null)
  assert.match(a.coverError.value, /已合并/)
  a.selectedWork.value = { ...A.metadata!, id: 'different-work' }
  assert.equal(a.coverPickerOpen.value, false)
  await a.showGame(A); api.getWork = async () => A.metadata!; await a.toggleCoverPicker()
  a.snapshot.value = state([]); await vue.nextTick()
  assert.equal(a.coverPickerOpen.value, false)
})
test('unlinked local cover selection captures null identity, keeps editor drafts and skips remote requests', async t => {
  const { a, api, state, messages } = await harness(t), A = game('A'), B = game('B')
  a.snapshot.value = state([A, B]); await vue.nextTick(); await a.showGame(B)
  let fetches = 0
  api.getWork = async () => { fetches++; throw new Error('No linked work to fetch') }
  await a.toggleCoverPicker()
  a.editor.name = 'Unfinished local name'; a.editor.notes = 'Unfinished local notes'
  const local = { dataUrl: 'data:image/jpeg;base64,synthetic', width: 800, height: 500 }
  const updated = { ...B, localCover: local }, calls: unknown[][] = []
  api.pickLocalCover = async (...args: unknown[]) => { calls.push(args); return updated }
  api.getState = async () => state([A, updated])
  await a.pickLocalCover(null); await vue.nextTick()
  assert.deepEqual(structuredClone(calls), [[B.id, null, null]])
  assert.equal(fetches, 0); assert.equal(a.selectedGameId.value, B.id)
  assert.equal(a.detailCoverUrl.value, local.dataUrl)
  assert.equal(a.editor.name, 'Unfinished local name'); assert.equal(a.editor.notes, 'Unfinished local notes')
  assert.equal(messages.at(-1)?.kind, 'success')
  api.setGameCover = async (...args: unknown[]) => { calls.push(args); return B }
  api.getState = async () => state([A, B])
  await a.saveCover(null, 'crop'); await vue.nextTick()
  assert.deepEqual(structuredClone(calls.at(-1)), [B.id, null, null, 'crop'])
  assert.equal(a.detailCoverUrl.value, null)
  a.snapshot.value = state([A]); await vue.nextTick()
  assert.equal(a.coverPickerOpen.value, false, 'removing B cannot silently target another unlinked game')
})

test('native cancellation has no success, reload or write and failed decode leaves current cover', async t => {
  const { a, api, state, messages } = await harness(t), A = completeGame('A')
  a.snapshot.value = state([A]); await vue.nextTick(); await a.showGame(A)
  api.getWork = async () => A.metadata!; await a.toggleCoverPicker()
  let reloads = 0
  api.getState = async () => { reloads++; return state([A]) }
  api.pickLocalCover = async () => null
  await a.pickLocalCover('plain')
  assert.equal(reloads, 0); assert.equal(messages.length, 0); assert.equal(a.busy.has('cover-save'), false)
  assert.equal(a.detailCoverUrl.value, A.metadata!.coverUrl)
  api.pickLocalCover = async () => { throw Error('invalid raster') }
  await a.pickLocalCover(null)
  assert.equal(reloads, 0); assert.equal(messages.length, 0)
  assert.equal(a.detailCoverUrl.value, A.metadata!.coverUrl); assert.match(a.coverError.value, /本地图片保存失败/)
})

test('late native dialog replies cannot alter a reopened picker or clear a later pending cover operation', async t => {
  const { a, api, state, messages } = await harness(t), A = game('A'), B = game('B')
  a.snapshot.value = state([A, B]); await vue.nextTick(); await a.showGame(A); await a.toggleCoverPicker()
  const old = deferred<LibraryGame | null>(), fresh = deferred<LibraryGame | null>(), calls: unknown[][] = [], responses = [old, fresh]
  api.pickLocalCover = (...args: unknown[]) => { calls.push(args); return responses.shift()!.promise }
  const previous = a.pickLocalCover('plain')
  await a.showGame(B); await a.toggleCoverPicker()
  const current = a.pickLocalCover(null)
  old.reject(Error('stale A failure')); await previous
  assert.equal(a.coverError.value, ''); assert.equal(a.busy.has('cover-save'), true)
  fresh.resolve(null); await current
  assert.equal(a.busy.has('cover-save'), false); assert.equal(messages.length, 0)
  assert.deepEqual(structuredClone(calls), [[A.id, null, 'plain'], [B.id, null, null]])
  api.getWork = async () => completeGame('A').metadata!
  await a.showGame(A); await a.toggleCoverPicker()
  const late = deferred<LibraryGame | null>(); api.pickLocalCover = () => late.promise
  const afterRelink = a.pickLocalCover('crop')
  a.snapshot.value = state([completeGame('A'), B]); await vue.nextTick()
  assert.equal(a.coverPickerOpen.value, false)
  late.reject(Error('stale linkage')); await afterRelink
  assert.equal(a.coverError.value, '')
})

test('global cover defaults only affect followers after a saved snapshot and explicit preferences remain intact', async t => {
  const { a, api, state } = await harness(t), A = completeGame('A')
  a.snapshot.value = state([A]); await vue.nextTick(); await a.showGame(A)
  assert.equal(a.globalCoverDisplay.value, 'blur'); assert.equal(a.detailCoverDisplay.value, 'blur')
  a.settingsForm.nonPortraitCoverDisplay = 'crop'
  assert.equal(a.detailCoverDisplay.value, 'blur', 'unsaved preference does not change the library')
  const calls: unknown[] = []
  api.saveSettings = async (input: unknown) => { calls.push(structuredClone(input)); return { ...state().settings, nonPortraitCoverDisplay: 'crop' } }
  api.getState = async () => ({ ...state([A]), settings: { ...state().settings, nonPortraitCoverDisplay: 'crop' } })
  await a.saveSettings(); await vue.nextTick()
  assert.equal((calls[0] as any).nonPortraitCoverDisplay, 'crop')
  assert.equal(a.detailCoverDisplay.value, 'crop')
  a.snapshot.value = { ...state([{ ...A, nonPortraitCoverDisplay: 'plain' }]), settings: { ...state().settings, nonPortraitCoverDisplay: 'crop' } }
  await vue.nextTick()
  assert.equal(a.detailCoverDisplay.value, 'plain')
  a.snapshot.value = { ...state([{ ...A, nonPortraitCoverDisplay: null }]), settings: { ...state().settings, nonPortraitCoverDisplay: 'crop' } }
  await vue.nextTick()
  assert.equal(a.detailCoverDisplay.value, 'crop')
})

import type { AppSnapshot, BulkGamePatch, LibraryGame, MetadataMatchingState, ScanCandidate, WorkMetadata } from '../src/shared/types'

function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const plain = <T>(value: T): T => JSON.parse(JSON.stringify(value))
function game(id: string): LibraryGame {
  return { id, name: `Voyage ${id}`, nameIsCustom: true, metadata: null, installations: [{ id: `install-${id}`, folderPath: `C:\\Synthetic\\${id}`, executablePath: null, arguments: [] }], preferredInstallationId: `install-${id}`, status: id === 'B' ? 'playing' : 'planned', personalRating: null, favorite: false, categories: [id], customTags: [], notes: `Saved ${id}`, totalPlaySeconds: 0, playCount: 0, sessions: [], addedAt: '2026-10-03T00:00:00.000Z', lastPlayedAt: null }
}
function candidate(id: string, folder = id): ScanCandidate { return { id, name: `Candidate ${id}`, folderPath: `C:\\Synthetic\\${folder}`, executables: [`C:\\Synthetic\\${folder}\\Game.exe`] } }
function completeGame(id: string): LibraryGame {
  const metadata: WorkMetadata = { id: `work-${id}`, title: `Work ${id}`, originalTitle: '', aliases: [], coverUrl: 'https://fixture.invalid/cover.png', bannerUrl: null, description: 'Already linked', releaseDate: null, releaseDatePrecision: null, developers: [], tags: [], ratings: [], characters: [], references: [], webUrl: null, contentRating: null, fetchedAt: '2026-10-03T00:00:00.000Z' }
  return { ...game(id), metadata }
}
function job(id = 'fixture-job', status: MetadataMatchingState['status'] = 'running', total = 1): MetadataMatchingState {
  return { id, status, total, processed: status === 'completed' ? total : 0, matched: 0, unmatched: 0, failed: 0, currentName: null, reviews: [] }
}
// Every case runs the same production source in its own Vue scope and VM.
// Compile once so the test subject cannot change midway through the suite.
const compiledHarness = (async () => {
  const source = await fs.readFile(new URL('../src/App.vue', import.meta.url), 'utf8')
  const script = source.split('<script setup lang="ts">')[1]!.split('</script>')[0]!
  const expose = '\nglobalThis.audit = { snapshot, page, busy, libraryQuery, statusFilter, favoritesOnly, filteredGames, selectionMode, selectedIds, batchStatus, batchFavorite, batchCategory, batchTag, batchCategoryMode, batchTagMode, enterSelection, leaveSelection, selectVisible, toggleSelection, organizeSelected, navigate, resetFilters, scanOpen, candidates, chosen, autoMatchOnImport, scanQuery, scanView, importError, scanReloadNeeded, setScanSelection, scanRows, newCandidates, scan, importSelected, startMatching, cancelMatching, matchingLabel, reidentifying, matchingUnavailable, localGameIds, selectedLocalIds, identifySelected, identifyCurrentGame, identifyScannedGames, scannedLibraryIds, showGame, detailOpen, selectedGameId, selectedWork, editor, detailCoverUrl, coverPickerOpen, coverOptionsWork, coverError, toggleCoverPicker, closeCoverPicker, loadCoverOptions, saveCover, pickLocalCover, globalCoverDisplay, detailCoverDisplay, settingsForm, saveSettings, reload };'
  const compiled = await transform(script.replace(/^import .*$/gm, '') + expose, { loader: 'ts', format: 'cjs', target: 'es2022' })
  return { script, compiled }
})()
async function harness(t: { after(callback: () => void): void }) {
  const { script, compiled } = await compiledHarness
  const messages: Array<{ message: string; kind: string }> = []
  const sandbox: any = { ...vue, ...libraryWorkspace, ...libraryInsights, ...savedLibraryViews, ...coverDisplay, ...importWorkspace, onMounted: () => {}, onUnmounted: () => {}, isPreview: false,
    useKunMessage: (message: string, kind: string) => messages.push({ message, kind }), useKunAlert: async () => true, api: {},
    document: { documentElement: { dataset: {}, classList: { toggle() {} } } }, matchMedia: () => ({ matches: false }) }
  for (const line of script.split('\n').filter(line => /^import \{/.test(line))) for (const name of line.match(/^import \{([^}]+)\}/)![1]!.split(',').map(name => name.trim())) if (!(name in sandbox)) sandbox[name] = name
  const scope = vue.effectScope(); t.after(() => scope.stop())
  vm.createContext(sandbox); scope.run(() => vm.runInContext(compiled.code, sandbox))
  const a = sandbox.audit, games = ['A', 'B', 'C'].map(game)
  const state = (items = games): AppSnapshot => ({ games: structuredClone(items), settings: { baseUrl: 'https://fixture.invalid/v2/', hasApiKey: true, includeNsfw: false, theme: 'light', readExecutableMetadata: false }, activeSession: null, dataPath: 'synthetic-only', version: 'fixture' })
  a.snapshot.value = state(); sandbox.api.getState = async () => state()
  await vue.nextTick()
  return { a, api: sandbox.api, games, state, messages }
}

test('bulk submission captures cloneable IDs and relative changes once, independently of later draft mutations', async t => {
  const { a, api } = await harness(t), response = deferred<LibraryGame[]>(), calls: Array<{ ids: string[]; patch: BulkGamePatch }> = []
  api.updateGames = (ids: string[], patch: BulkGamePatch) => { calls.push({ ids, patch }); return response.promise }
  a.enterSelection(); a.toggleSelection('A'); a.toggleSelection('B')
  a.batchStatus.value = 'completed'; a.batchFavorite.value = 'no'
  a.batchCategory.value = ' Story ， Story, Short '; a.batchCategoryMode.value = 'add'
  a.batchTag.value = ' Finished\nFinished '; a.batchTagMode.value = 'remove'
  const pending = a.organizeSelected()
  assert.equal(a.busy.has('bulk'), true)
  assert.equal(vue.isProxy(calls[0]!.ids), false)
  assert.equal(vue.isProxy(calls[0]!.patch), false)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A', 'B'], patch: { status: 'completed', favorite: false, addCategories: ['Story', 'Short'], removeTags: ['Finished'] } })
  a.selectedIds.value.push('C'); a.batchStatus.value = 'paused'; a.batchCategory.value = 'Later'; a.batchTag.value = 'Later'
  await a.organizeSelected()
  assert.equal(calls.length, 1, 'double submission is locked while the first batch is pending')
  assert.deepEqual(plain(calls[0]), { ids: ['A', 'B'], patch: { status: 'completed', favorite: false, addCategories: ['Story', 'Short'], removeTags: ['Finished'] } })
  response.resolve([]); await pending
  assert.equal(a.selectionMode.value, false)
  assert.deepEqual(plain(a.selectedIds.value), [])
  assert.equal(a.busy.has('bulk'), false)
})

test('a cancelled and reopened selection keeps its new state when the previous batch completes', async t => {
  const { a, api } = await harness(t), response = deferred<LibraryGame[]>()
  api.updateGames = () => response.promise
  a.enterSelection(); a.toggleSelection('A'); a.batchFavorite.value = 'yes'
  const pending = a.organizeSelected()
  a.leaveSelection(); a.enterSelection()
  // Challenge completion ownership directly. Current controls lock selection
  // during the request; a newer selection state must still never be erased.
  a.selectedIds.value = ['B']; a.batchTag.value = 'Next batch'
  response.resolve([]); await pending
  assert.equal(a.selectionMode.value, true)
  assert.deepEqual(plain(a.selectedIds.value), ['B'])
  assert.equal(a.batchTag.value, 'Next batch')
  assert.equal(a.busy.has('bulk'), false)
})

test('bulk failure retains the original selection and operation so an explicit retry submits the same request', async t => {
  const { a, api, messages } = await harness(t), response = deferred<LibraryGame[]>(), calls: unknown[] = []
  api.updateGames = (ids: string[], patch: BulkGamePatch) => { calls.push(plain({ ids, patch })); return calls.length === 1 ? response.promise : Promise.resolve([]) }
  a.enterSelection(); a.toggleSelection('B'); a.batchCategory.value = 'Old category'; a.batchCategoryMode.value = 'remove'
  const pending = a.organizeSelected(); response.reject(new Error('Synthetic save failure')); await pending
  assert.equal(a.selectionMode.value, true)
  assert.deepEqual(plain(a.selectedIds.value), ['B'])
  assert.equal(a.batchCategory.value, 'Old category')
  assert.equal(a.batchCategoryMode.value, 'remove')
  assert.equal(a.busy.has('bulk'), false)
  assert.equal(messages.at(-1)?.kind, 'error')
  await a.organizeSelected()
  assert.deepEqual(calls[1], calls[0])
  assert.equal(a.selectionMode.value, false)
})

test('filters and external removal synchronously prune hidden selections without restoring them on reset', async t => {
  const { a, api, games, state } = await harness(t), calls: unknown[] = []
  api.updateGames = async (ids: string[], patch: BulkGamePatch) => { calls.push(plain({ ids, patch })); return [] }
  a.enterSelection(); a.selectVisible()
  assert.deepEqual(new Set(a.selectedIds.value), new Set(['A', 'B', 'C']))
  a.statusFilter.value = 'playing'
  assert.deepEqual(plain(a.selectedIds.value), ['B'])
  a.resetFilters()
  assert.deepEqual(plain(a.selectedIds.value), ['B'], 'revealing a previously hidden game does not implicitly reselect it')
  a.selectVisible(); a.libraryQuery.value = 'VoyageA'
  assert.deepEqual(plain(a.selectedIds.value), ['A'])
  a.snapshot.value = state(games.filter(item => item.id !== 'A'))
  assert.deepEqual(plain(a.selectedIds.value), [])
  a.batchFavorite.value = 'yes'; await a.organizeSelected()
  assert.equal(calls.length, 0, 'removed/hidden games cannot be included in the next batch')
})

test('empty bulk operations and empty selections do not send writes', async t => {
  const { a, api, messages } = await harness(t)
  let writes = 0; api.updateGames = async () => { writes++; return [] }
  a.enterSelection(); a.batchFavorite.value = 'yes'; await a.organizeSelected()
  a.batchFavorite.value = ''; a.toggleSelection('A'); a.batchCategory.value = ' , ，\n '; await a.organizeSelected()
  assert.equal(writes, 0)
  assert.equal(messages.at(-1)?.kind, 'info')
  assert.equal(a.selectionMode.value, true)
})

test('bulk completion after navigation does not return to the library or reopen selection', async t => {
  const { a, api } = await harness(t), response = deferred<LibraryGame[]>()
  api.updateGames = () => response.promise
  a.enterSelection(); a.toggleSelection('A'); a.batchFavorite.value = 'yes'
  const pending = a.organizeSelected(); a.navigate('stats')
  response.resolve([]); await pending
  assert.equal(a.page.value, 'stats')
  assert.equal(a.selectionMode.value, false)
  assert.deepEqual(plain(a.selectedIds.value), [])
})

test('cancelling a new folder dialog preserves the existing scan, filters and selection without an empty-result message', async t => {
  const { a, api, messages } = await harness(t)
  api.scanFolder = async () => [candidate('old-first', 'First'), candidate('old-second', 'Second')]
  await a.scan()
  a.setScanSelection(['old-second']); a.scanQuery.value = 'Second'; a.scanView.value = 'new'
  a.importError.value = 'An existing scan warning'
  const before = plain(a.candidates.value), count = messages.length
  const dialog = deferred<ScanCandidate[] | null>(); api.scanFolder = () => dialog.promise
  const pending = a.scan()
  assert.equal(a.busy.has('scan'), true)
  dialog.resolve(null); await pending
  assert.equal(a.scanOpen.value, true)
  assert.deepEqual(plain(a.candidates.value), before)
  assert.deepEqual(plain(a.chosen.value), ['old-second'])
  assert.equal(a.scanQuery.value, 'Second'); assert.equal(a.scanView.value, 'new')
  assert.equal(a.importError.value, 'An existing scan warning')
  assert.equal(a.scanReloadNeeded.value, false)
  assert.equal(a.busy.has('scan'), false); assert.equal(messages.length, count)
})

test('a confirmed folder with no programs opens a real empty scan and clears the previous scan context', async t => {
  const { a, api, messages } = await harness(t)
  api.scanFolder = async () => [candidate('old', 'Old')]
  await a.scan()
  a.scanQuery.value = 'Old'; a.scanView.value = 'new'
  a.importError.value = 'Old read failure'; a.scanReloadNeeded.value = true
  const count = messages.length
  api.scanFolder = async () => []
  await a.scan()
  assert.equal(a.scanOpen.value, true)
  assert.deepEqual(plain(a.candidates.value), [])
  assert.deepEqual(plain(a.chosen.value), [])
  assert.deepEqual(plain(importWorkspace.filterScanRows(a.scanRows.value, a.scanQuery.value, a.scanView.value)), [])
  assert.equal(a.scanQuery.value, ''); assert.equal(a.scanView.value, 'all')
  assert.equal(a.importError.value, ''); assert.equal(a.scanReloadNeeded.value, false)
  assert.equal(messages.length, count, 'an empty scan is represented in the modal rather than described as cancellation')
})

test('scan selection and submission reject ghost IDs, duplicates, known directories and overlapping installations', async t => {
  const { a, api, games } = await harness(t), calls: unknown[] = []
  const known = { ...candidate('known'), folderPath: games[0]!.installations[0]!.folderPath }
  const conflict = { ...candidate('overlap'), folderPath: games[1]!.installations[0]!.folderPath + '\\Nested' }
  const fresh = candidate('fresh', 'Fresh')
  api.scanFolder = async () => [known, conflict, fresh]
  api.importLocalGames = async (ids: string[], match: boolean) => { calls.push({ ids, match }); return { added: 1, skipped: 0 } }
  await a.scan()
  a.setScanSelection(['ghost', 'known', 'overlap', 'fresh', 'fresh'])
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  a.setScanSelection(['ghost', 'known', 'overlap'])
  await a.importSelected()
  assert.equal(calls.length, 0)
  // A stale consumer bypassing the selection callback still cannot submit IDs
  // that are missing, already owned, overlapping or duplicated.
  a.chosen.value = ['ghost', 'known', 'fresh', 'overlap', 'fresh']
  await a.importSelected()
  assert.deepEqual(structuredClone(calls), [{ ids: ['fresh'], match: true }])
  assert.equal(vue.isProxy((calls[0] as any).ids), false)
})

test('a failed import keeps a persistent error and choices, then retry clears the error without accepting edits while pending', async t => {
  const { a, api, messages } = await harness(t)
  const first = deferred<{ added: number; skipped: number }>(), retry = deferred<{ added: number; skipped: number }>()
  const responses = [first, retry], calls: string[][] = []
  api.scanFolder = async () => [candidate('fresh', 'Fresh'), candidate('other', 'Other')]
  api.importLocalGames = (ids: string[]) => { calls.push([...ids]); return responses.shift()!.promise }
  await a.scan(); a.setScanSelection(['fresh'])
  const failed = a.importSelected(); first.reject(new Error('Synthetic disk write failure')); await failed
  assert.equal(a.scanOpen.value, true); assert.equal(a.scanReloadNeeded.value, false)
  assert.match(a.importError.value, /导入未完成.*选择已保留/)
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  assert.equal(a.busy.has('import'), false); assert.equal(messages.at(-1)?.kind, 'error')
  const pending = a.importSelected()
  assert.equal(a.importError.value, ''); assert.equal(a.busy.has('import'), true)
  a.setScanSelection(['other'])
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  await a.importSelected()
  assert.deepEqual(calls, [['fresh'], ['fresh']], 'double clicking the retry cannot enqueue another write')
  retry.resolve({ added: 1, skipped: 0 }); await pending
  assert.equal(a.importError.value, ''); assert.equal(a.scanReloadNeeded.value, false)
  assert.equal(a.scanOpen.value, false); assert.equal(messages.at(-1)?.kind, 'success')
})

test('an acknowledged import with a recognition-start warning keeps saved games and reports only the unfinished follow-up', async t => {
  const { a, api, state, games, messages } = await harness(t)
  const warning = '本地游戏已保存，但自动补全未能启动。请稍后在“资料整理”中点击“只补缺失”。'
  const imported = game('Fresh'), calls: unknown[] = []
  let reads = 0
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  api.importLocalGames = async (ids: string[], match: boolean) => { calls.push({ ids, match }); return { added: 1, skipped: 0, matchingStarted: false, warning } }
  api.getState = async () => { reads++; return state([...games, imported]) }
  await a.scan(); await a.importSelected()
  assert.deepEqual(structuredClone(calls), [{ ids: ['fresh'], match: true }])
  assert.equal(reads, 1)
  assert.equal(a.scanOpen.value, false); assert.equal(a.page.value, 'library')
  assert.equal(a.importError.value, ''); assert.equal(a.scanReloadNeeded.value, false)
  assert.equal(a.busy.has('import'), false)
  assert.ok(a.snapshot.value.games.some((item: LibraryGame) => item.id === imported.id))
  assert.deepEqual(plain(a.chosen.value), [], 'the saved installation is removed from import selection')
  assert.equal(messages.at(-1)?.kind, 'info')
  assert.match(messages.at(-1)!.message, /已添加 1 部游戏/)
  assert.match(messages.at(-1)!.message, /补全未能启动/)
  assert.doesNotMatch(messages.at(-1)!.message, /导入未完成|请.*再次导入/)
  assert.equal(messages.some(message => message.kind === 'error'), false)
  await a.importSelected()
  assert.equal(calls.length, 1); assert.equal(reads, 1, 'a handled follow-up warning does not enter import or read recovery again')
})

test('an acknowledged import with a failed snapshot switches to read recovery and never imports again even after selection becomes empty', async t => {
  const { a, api, state, games, messages } = await harness(t)
  let writes = 0, reads = 0
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  api.importLocalGames = async () => { writes++; return { added: 1, skipped: 0 } }
  api.getState = async () => { reads++; throw new Error('Synthetic snapshot read failure') }
  await a.scan(); await a.importSelected()
  assert.equal(writes, 1); assert.equal(reads, 1)
  assert.equal(a.scanOpen.value, true); assert.equal(a.scanReloadNeeded.value, true)
  assert.match(a.importError.value, /导入已完成.*无需再次导入/)
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  assert.equal(messages.some(message => message.kind === 'success'), false)
  a.setScanSelection([])
  await a.importSelected()
  assert.equal(writes, 1); assert.equal(reads, 2)
  assert.equal(a.scanReloadNeeded.value, true)
  assert.match(a.importError.value, /导入已完成.*重新读取/)
  const recovered = deferred<AppSnapshot>()
  api.getState = () => { reads++; return recovered.promise }
  const pending = a.importSelected()
  assert.equal(a.busy.has('import'), true)
  await a.importSelected()
  assert.equal(writes, 1); assert.equal(reads, 3)
  const imported = game('Fresh')
  recovered.resolve(state([...games, imported])); await pending
  assert.equal(a.scanReloadNeeded.value, false); assert.equal(a.importError.value, '')
  assert.equal(a.scanOpen.value, false); assert.equal(a.page.value, 'library')
  assert.ok(a.snapshot.value.games.some((item: LibraryGame) => item.id === imported.id))
})

for (const phase of ['write', 'snapshot'] as const) test(`a stale ${phase} failure cannot pollute the error, recovery flag or candidates of a reopened scan`, async t => {
  const { a, api } = await harness(t)
  const oldWrite = deferred<{ added: number; skipped: number }>(), oldRead = deferred<AppSnapshot>(), readStarted = deferred<void>()
  let scans = 0
  api.scanFolder = async () => ++scans === 1 ? [candidate('old', 'Old')] : [candidate('new', 'New')]
  api.importLocalGames = () => oldWrite.promise
  if (phase === 'snapshot') api.getState = () => { readStarted.resolve(); return oldRead.promise }
  await a.scan()
  const pending = a.importSelected()
  if (phase === 'snapshot') {
    oldWrite.resolve({ added: 1, skipped: 0 })
    await readStarted.promise
    assert.equal(a.scanReloadNeeded.value, true, 'the acknowledged old import reached snapshot recovery')
  }
  a.scanOpen.value = false; a.navigate('discover'); await a.scan()
  a.scanQuery.value = 'New'; a.scanView.value = 'new'
  assert.equal(a.scanReloadNeeded.value, false)
  if (phase === 'write') oldWrite.reject(new Error('Late old write failure'))
  else oldRead.reject(new Error('Late old snapshot failure'))
  await pending
  assert.equal(a.scanOpen.value, true); assert.equal(a.page.value, 'discover')
  assert.equal(a.importError.value, ''); assert.equal(a.scanReloadNeeded.value, false)
  assert.deepEqual(plain(a.candidates.value).map((item: ScanCandidate) => item.id), ['new'])
  assert.deepEqual(plain(a.chosen.value), ['new'])
  assert.equal(a.scanQuery.value, 'New'); assert.equal(a.scanView.value, 'new')
  assert.equal(a.busy.has('import'), false)
})

test('scan preselects only new directories and filters a stale known candidate before invoking the bridge', async t => {
  const { a, api } = await harness(t), calls: unknown[] = []
  const known = { ...candidate('known', 'A'), folderPath: 'c:/SYNTHETIC/a/' }, fresh = candidate('fresh', 'Fresh')
  api.scanFolder = async () => [known, fresh]
  api.importLocalGames = async (ids: string[], match: boolean) => { calls.push({ ids, match }); return { added: 1, skipped: 0 } }
  await a.scan()
  assert.equal(a.scanOpen.value, true)
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  assert.deepEqual(plain(a.scanRows.value.filter((row: importWorkspace.ScanRow) => row.status === 'known').map((row: importWorkspace.ScanRow) => row.candidate.id)), ['known'])
  assert.deepEqual(plain(a.newCandidates.value).map((item: ScanCandidate) => item.id), ['fresh'])
  a.chosen.value = ['known', 'fresh']
  await a.importSelected()
  assert.equal(vue.isProxy((calls[0] as any).ids), false)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['fresh'], match: true })
  assert.equal(a.scanOpen.value, false)
  assert.equal(a.page.value, 'library')
})

test('a library notification removes a newly known candidate from selection before import', async t => {
  const { a, api, games, state } = await harness(t)
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  let writes = 0; api.importLocalGames = async () => { writes++; return { added: 0, skipped: 1 } }
  await a.scan(); assert.deepEqual(plain(a.chosen.value), ['fresh'])
  const imported = game('Imported'); imported.installations[0]!.folderPath = 'C:\\Synthetic\\Fresh'
  a.snapshot.value = state([...games, imported])
  assert.deepEqual(plain(a.chosen.value), [])
  await a.importSelected()
  assert.equal(writes, 0)
  assert.equal(a.scanOpen.value, true)
})

test('pending import captures candidate IDs and recognition preference once, and blocks duplicate submits', async t => {
  const { a, api } = await harness(t), response = deferred<{ added: number; skipped: number }>(), calls: unknown[] = []
  api.scanFolder = async () => [candidate('first-scan-1', 'Fresh1'), candidate('first-scan-2', 'Fresh2')]
  api.importLocalGames = (ids: string[], match: boolean) => { calls.push({ ids, match }); return response.promise }
  await a.scan(); a.autoMatchOnImport.value = false
  const pending = a.importSelected()
  a.chosen.value.splice(0, 1); a.autoMatchOnImport.value = true
  await a.importSelected()
  assert.equal(calls.length, 1)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['first-scan-1', 'first-scan-2'], match: false })
  response.resolve({ added: 2, skipped: 0 }); await pending
  assert.equal(a.busy.has('import'), false)
  assert.equal(a.scanOpen.value, false)
})

test('closing a pending import leaves a later page untouched when its successful result arrives', async t => {
  const { a, api, games, state } = await harness(t), response = deferred<{ added: number; skipped: number }>()
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]; api.importLocalGames = () => response.promise
  await a.scan(); const pending = a.importSelected()
  a.scanOpen.value = false; a.navigate('stats')
  const imported = game('Fresh'); api.getState = async () => state([...games, imported])
  response.resolve({ added: 1, skipped: 0 }); await pending
  assert.equal(a.scanOpen.value, false)
  assert.equal(a.page.value, 'stats')
  assert.ok(a.snapshot.value.games.some((item: LibraryGame) => item.id === 'Fresh'), 'the persisted import is still reflected in the library')
})

test('an old import completion cannot close a reopened scan or replace its new candidates and choices', async t => {
  const { a, api } = await harness(t), response = deferred<{ added: number; skipped: number }>()
  let scans = 0
  api.scanFolder = async () => ++scans === 1 ? [candidate('old-scan', 'Old')] : [candidate('new-scan', 'New')]
  api.importLocalGames = () => response.promise
  await a.scan(); const pending = a.importSelected()
  a.scanOpen.value = false; a.navigate('discover'); await a.scan(); a.scanQuery.value = 'New'
  response.resolve({ added: 1, skipped: 0 }); await pending
  assert.equal(a.scanOpen.value, true)
  assert.equal(a.page.value, 'discover')
  assert.deepEqual(plain(a.candidates.value).map((item: ScanCandidate) => item.id), ['new-scan'])
  assert.deepEqual(plain(a.chosen.value), ['new-scan'])
  assert.equal(a.scanQuery.value, 'New')
  assert.equal(a.busy.has('import'), false)
})

test('import failure preserves choices for retry, while all-known scans never submit', async t => {
  const { a, api, messages } = await harness(t), response = deferred<{ added: number; skipped: number }>()
  let writes = 0
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  api.importLocalGames = () => { writes++; return response.promise }
  await a.scan(); const pending = a.importSelected(); response.reject(new Error('Synthetic import failure')); await pending
  assert.equal(a.scanOpen.value, true)
  assert.deepEqual(plain(a.chosen.value), ['fresh'])
  assert.equal(a.busy.has('import'), false)
  assert.equal(messages.at(-1)?.kind, 'error')
  a.scanOpen.value = false
  api.scanFolder = async () => [candidate('known-A', 'A'), candidate('known-B', 'B')]
  await a.scan(); await a.importSelected()
  assert.deepEqual(plain(a.chosen.value), [])
  assert.equal(a.newCandidates.value.length, 0)
  assert.equal(writes, 1)
})

for (const condition of ['missing-key', 'already-running', 'start-pending'] as const) test(`import skips starting a second recognition job when ${condition}`, async t => {
  const { a, api, messages, state } = await harness(t), calls: unknown[] = []
  if (condition === 'missing-key') {
    a.snapshot.value.settings.hasApiKey = false
    api.getState = async () => { const snapshot = state(); snapshot.settings.hasApiKey = false; return snapshot }
  } else if (condition === 'already-running') a.snapshot.value.matching = { id: 'existing', status: 'running', total: 1, processed: 0, matched: 0, unmatched: 0, failed: 0, currentName: null, reviews: [] }
  else a.busy.add('matching-start')
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  api.importLocalGames = async (ids: string[], match: boolean) => { calls.push({ ids, match }); return { added: 1, skipped: 0 } }
  await a.scan(); await a.importSelected()
  assert.deepEqual(structuredClone(calls[0]), { ids: ['fresh'], match: false })
  assert.ok(!messages.some(message => message.message.includes('正在自动识别封面与资料')))
  const importMessage = messages.at(-1)!.message
  assert.match(importMessage, /“资料整理”.*“只补缺失”/, 'both deferred recognition paths point to the current maintenance action')
  assert.doesNotMatch(importMessage, /补全资料/, 'the import hint must not direct users to the removed library button')
  if (condition === 'missing-key') assert.match(importMessage, /配置 API 密钥后/)
  else assert.match(importMessage, /当前识别任务结束后/)
})

test('an actual pending recognition start allows local import but never starts a second automatic job', async t => {
  const { a, api, messages, state, games } = await harness(t), response = deferred<MetadataMatchingState>(), imported: unknown[] = []
  api.startMetadataMatching = () => response.promise
  const recognition = a.startMatching(['A'], 'reidentify')
  assert.equal(a.busy.has('matching-start'), true)
  assert.equal(a.snapshot.value.matching, undefined, 'the initial start has not delivered a running snapshot yet')
  api.scanFolder = async () => [candidate('fresh', 'Fresh')]
  api.importLocalGames = async (ids: string[], match: boolean) => { imported.push({ ids, match }); return { added: 1, skipped: 0, matchingStarted: false } }
  await a.scan(); await a.importSelected()
  assert.deepEqual(structuredClone(imported[0]), { ids: ['fresh'], match: false })
  assert.match(messages.at(-1)!.message, /已添加 1 部游戏/)
  assert.match(messages.at(-1)!.message, /当前识别任务结束后.*“资料整理”.*“只补缺失”/)
  assert.ok(!messages.at(-1)!.message.includes('正在自动识别封面与资料'))
  assert.equal(a.busy.has('matching-start'), true)
  api.getState = async () => ({ ...state(games), matching: job('original') })
  response.resolve(job('original')); await recognition
})

test('a fully illustrated library can explicitly reidentify all local games while missing-only completion stays idle', async t => {
  const { a, api, state, messages } = await harness(t), calls: unknown[] = []
  const virtual = { ...completeGame('Virtual'), installations: [], preferredInstallationId: null }
  const games = [completeGame('A'), completeGame('B'), virtual]
  a.snapshot.value = state(games)
  const running = { ...state(games), matching: job('all-local', 'running', 2) }
  api.getState = async () => running
  api.startMetadataMatching = async (ids: string[], mode: string) => { calls.push({ ids, mode }); return running.matching }
  assert.equal(await a.startMatching(), false)
  assert.equal(calls.length, 0)
  assert.match(messages.at(-1)!.message, /没有需要补全/)
  assert.equal(a.matchingUnavailable.value, false)
  assert.deepEqual(plain(a.localGameIds.value), ['A', 'B'])
  assert.equal(await a.startMatching(undefined, 'reidentify'), true)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A', 'B'], mode: 'reidentify' })
  assert.equal(vue.isProxy((calls[0] as any).ids), false)
  assert.deepEqual(plain(a.snapshot.value.games), games, 'starting recognition never clears the existing metadata in the UI')
})

test('missing-only completion sends only incomplete local games and retains its explicit mode', async t => {
  const { a, api, state } = await harness(t), calls: unknown[] = []
  const games = [completeGame('A'), game('B'), { ...completeGame('C'), metadata: { ...completeGame('C').metadata!, coverUrl: null } }]
  a.snapshot.value = state(games)
  const running = { ...state(games), matching: job('missing', 'running', 2) }
  api.getState = async () => running
  api.startMetadataMatching = async (ids: string[], mode: string) => { calls.push({ ids, mode }); return running.matching }
  await a.startMatching()
  assert.deepEqual(structuredClone(calls[0]), { ids: ['B', 'C'], mode: 'missing' })
})

test('recognizing selected games captures plain local IDs without clearing the newer selection or its drafts', async t => {
  const { a, api, state } = await harness(t), response = deferred<MetadataMatchingState>(), calls: unknown[] = []
  const virtual = { ...completeGame('Virtual'), installations: [], preferredInstallationId: null }
  const games = [completeGame('A'), completeGame('B'), virtual]
  a.snapshot.value = state(games)
  const running = { ...state(games), matching: job('selected') }
  api.getState = async () => running
  api.startMetadataMatching = (ids: string[], mode: string) => { calls.push({ ids, mode }); return response.promise }
  a.enterSelection(); a.toggleSelection('A'); a.toggleSelection('Virtual')
  const pending = a.identifySelected()
  a.leaveSelection(); a.enterSelection(); a.toggleSelection('B'); a.batchTag.value = 'Next operation'
  await a.identifySelected(); await a.startMatching(undefined, 'reidentify')
  assert.equal(calls.length, 1)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A'], mode: 'reidentify' })
  response.resolve(running.matching); await pending
  assert.equal(a.selectionMode.value, true)
  assert.deepEqual(plain(a.selectedIds.value), ['B'])
  assert.equal(a.batchTag.value, 'Next operation')
})

test('per-game recognition targets the original local game and preserves a subsequently opened detail and draft', async t => {
  const { a, api, state } = await harness(t), response = deferred<MetadataMatchingState>(), calls: unknown[] = []
  const games = [completeGame('A'), completeGame('B')]
  a.snapshot.value = state(games)
  const running = { ...state(games), matching: job('detail') }
  api.getState = async () => running
  api.startMetadataMatching = (ids: string[], mode: string) => { calls.push({ ids, mode }); return response.promise }
  await a.showGame(games[0]); const pending = a.identifyCurrentGame()
  a.detailOpen.value = false; await a.showGame(games[1]); a.editor.notes = 'Unsaved B note'
  await a.identifyCurrentGame()
  assert.equal(calls.length, 1)
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A'], mode: 'reidentify' })
  response.resolve(running.matching); await pending
  assert.equal(a.detailOpen.value, true)
  assert.equal(a.selectedGameId.value, 'B')
  assert.equal(a.editor.notes, 'Unsaved B note')
})

test('explicit recognition excludes absent and virtual IDs, and an empty local selection never calls the API', async t => {
  const { a, api, state, messages } = await harness(t), calls: unknown[] = []
  const virtual = { ...completeGame('Virtual'), installations: [], preferredInstallationId: null }
  const games = [completeGame('A'), virtual]
  a.snapshot.value = state(games)
  api.startMetadataMatching = async (ids: string[], mode: string) => { calls.push({ ids, mode }); return job() }
  await a.startMatching([], 'reidentify'); await a.startMatching(['Virtual', 'missing'], 'reidentify')
  await a.showGame(virtual); await a.identifyCurrentGame()
  assert.equal(calls.length, 0)
  assert.match(messages.at(-1)!.message, /没有可重新识别的本地游戏/)
  api.getState = async () => ({ ...state(games), matching: job() })
  await a.startMatching(['A', 'A', 'Virtual', 'missing'], 'reidentify')
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A'], mode: 'reidentify' })
})

test('recognition with no saved key guides to settings and closes the initiating overlays without sending a job', async t => {
  const { a, api, games, messages } = await harness(t)
  a.snapshot.value.settings.hasApiKey = false
  let calls = 0; api.startMetadataMatching = async () => { calls++; return job() }
  await a.showGame(games[0]); a.scanOpen.value = true
  assert.equal(await a.startMatching(['A'], 'reidentify'), false)
  assert.equal(calls, 0)
  assert.equal(a.page.value, 'settings')
  assert.equal(a.detailOpen.value, false)
  assert.equal(a.scanOpen.value, false)
  assert.match(messages.at(-1)!.message, /先保存 API 密钥/)
})

test('an already running recognition task prevents every new entry point from submitting', async t => {
  const { a, api, games } = await harness(t)
  a.snapshot.value.matching = job('already-running')
  let calls = 0; api.startMetadataMatching = async () => { calls++; return job() }
  a.enterSelection(); a.toggleSelection('A'); await a.showGame(games[0])
  a.candidates.value = [candidate('known-A', 'A')]; a.scanOpen.value = true
  await a.startMatching(undefined, 'reidentify'); await a.identifySelected(); await a.identifyCurrentGame(); await a.identifyScannedGames()
  assert.equal(a.matchingUnavailable.value, true)
  assert.equal(calls, 0)
  assert.equal(a.scanOpen.value, true)
})

test('mixed scanning recognizes existing library IDs once per game and preserves the new import selection', async t => {
  const { a, api, state } = await harness(t), calls: unknown[] = []
  const existing = completeGame('A')
  existing.installations.push({ ...existing.installations[0]!, id: 'alternate', folderPath: 'C:\\Synthetic\\Alternate' })
  const games = [existing, completeGame('B')]
  a.snapshot.value = state(games)
  api.scanFolder = async () => [{ ...candidate('scanner-A', 'A'), folderPath: 'c:/synthetic/a/' }, candidate('scanner-alt', 'Alternate'), candidate('scanner-new', 'New')]
  api.importLocalGames = async () => { throw new Error('Recognition must not reimport existing games') }
  const running = { ...state(games), matching: job('known-scan') }
  api.getState = async () => running
  api.startMetadataMatching = async (ids: string[], mode: string) => { calls.push({ ids, mode }); return running.matching }
  a.page.value = 'discover'; await a.scan()
  assert.deepEqual(plain(a.scannedLibraryIds.value), ['A'])
  assert.deepEqual(plain(a.chosen.value), ['scanner-new'])
  await a.identifyScannedGames()
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A'], mode: 'reidentify' })
  assert.equal(a.scanOpen.value, true)
  assert.equal(a.page.value, 'discover')
  assert.deepEqual(plain(a.chosen.value), ['scanner-new'])
  assert.deepEqual(plain(a.snapshot.value.games), games)
})

test('recognizing an all-known scan closes only its own modal and returns to the library', async t => {
  const { a, api, state, games } = await harness(t)
  api.scanFolder = async () => [candidate('scanner-A', 'A')]
  const running = { ...state(games), matching: job('all-known') }
  api.getState = async () => running
  api.startMetadataMatching = async () => running.matching
  a.page.value = 'discover'; await a.scan(); await a.identifyScannedGames()
  assert.equal(a.scanOpen.value, false)
  assert.equal(a.page.value, 'library')
})

test('a late recognition start from an old scan cannot close a new scan or change its current page', async t => {
  const { a, api, state, games } = await harness(t), response = deferred<MetadataMatchingState>(), calls: unknown[] = []
  let scans = 0
  api.scanFolder = async () => ++scans === 1 ? [candidate('old-known', 'A')] : [candidate('new-known', 'B'), candidate('new-choice', 'New')]
  api.startMetadataMatching = (ids: string[], mode: string) => { calls.push({ ids, mode }); return response.promise }
  const running = { ...state(games), matching: job('old-scan') }
  api.getState = async () => running
  await a.scan(); const pending = a.identifyScannedGames()
  a.scanOpen.value = false; a.navigate('stats'); await a.scan(); a.scanQuery.value = 'New'
  response.resolve(running.matching); await pending
  assert.deepEqual(structuredClone(calls[0]), { ids: ['A'], mode: 'reidentify' })
  assert.equal(a.scanOpen.value, true)
  assert.equal(a.page.value, 'stats')
  assert.deepEqual(plain(a.candidates.value).map((item: ScanCandidate) => item.id), ['new-known', 'new-choice'])
  assert.deepEqual(plain(a.chosen.value), ['new-choice'])
  assert.equal(a.scanQuery.value, 'New')
})

for (const newer of ['cancelled', 'different-job'] as const) test(`a late start reply cannot revive or claim a ${newer} recognition state`, async t => {
  const { a, api, state, games, messages } = await harness(t), response = deferred<MetadataMatchingState>()
  api.scanFolder = async () => [candidate('known', 'A')]
  api.startMetadataMatching = () => response.promise
  const latest = { ...state(games), matching: newer === 'cancelled' ? job('original', 'cancelled') : job('newer-job') }
  api.getState = async () => latest
  await a.scan(); const pending = a.identifyScannedGames()
  a.snapshot.value = latest
  response.resolve(job('original')); await pending
  assert.deepEqual(plain(a.snapshot.value.matching), latest.matching)
  assert.equal(a.scanOpen.value, true, 'a superseded start does not dismiss the current scan')
  assert.ok(!messages.some(message => message.kind === 'success'))
  assert.equal(a.busy.has('matching-start'), false)
})

test('recognition empty result and request failure retain the scan for retry and report the actual outcome', async t => {
  const { a, api, state, games, messages } = await harness(t)
  api.scanFolder = async () => [candidate('known', 'A')]
  api.startMetadataMatching = async () => job('empty', 'completed', 0)
  api.getState = async () => ({ ...state(games), matching: job('empty', 'completed', 0) })
  await a.scan(); await a.identifyScannedGames()
  assert.equal(a.scanOpen.value, true)
  assert.match(messages.at(-1)!.message, /没有可处理/)
  api.startMetadataMatching = async () => { throw new Error('Synthetic start failure') }
  await a.identifyScannedGames()
  assert.equal(a.scanOpen.value, true)
  assert.equal(messages.at(-1)!.kind, 'error')
  assert.equal(a.busy.has('matching-start'), false)
})

test('recognition progress labels distinguish reidentification from ordinary and legacy completion jobs', async t => {
  const { a } = await harness(t)
  for (const status of ['running', 'completed', 'cancelled'] as const) {
    a.snapshot.value.matching = { ...job('reidentify', status), mode: 'reidentify' }
    assert.equal(a.reidentifying.value, true)
    assert.match(a.matchingLabel.value, /重新识别/)
    assert.doesNotMatch(a.matchingLabel.value, /补全/)
    a.snapshot.value.matching = job('legacy', status)
    assert.equal(a.reidentifying.value, false)
    assert.match(a.matchingLabel.value, /补全/)
  }
})

test('a delayed cancellation reply cannot replace the state of a newer recognition job', async t => {
  const { a, api, state, games } = await harness(t), response = deferred<MetadataMatchingState>()
  a.snapshot.value.matching = { ...job('old'), mode: 'reidentify' }
  api.cancelMetadataMatching = () => response.promise
  const pending = a.cancelMatching()
  const newer = { ...state(games), matching: { ...job('newer'), mode: 'reidentify' as const } }
  a.snapshot.value = newer; api.getState = async () => newer
  response.resolve({ ...job('old', 'cancelled'), mode: 'reidentify' }); await pending
  assert.deepEqual(plain(a.snapshot.value.matching), newer.matching)
  assert.equal(a.matchingUnavailable.value, true)
  assert.equal(a.busy.has('matching-cancel'), false)
})
