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
import type { AppSnapshot, LibraryGame, MatchReview, WorkMetadata } from '../src/shared/types'

function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const plain = <T>(value: T): T => JSON.parse(JSON.stringify(value))
function work(id: string): WorkMetadata {
  return { id, title: `Work ${id}`, originalTitle: `Original ${id}`, aliases: [], coverUrl: null, bannerUrl: null, description: `Details ${id}`, releaseDate: null, releaseDatePrecision: null, developers: [], tags: [], ratings: [], characters: [], references: [], webUrl: null, contentRating: null, fetchedAt: '2026-10-03T00:00:00.000Z' }
}
function game(id: string): LibraryGame {
  return { id, name: `Local ${id}`, nameIsCustom: true, metadata: work(`old-${id}`), installations: [{ id: `install-${id}`, folderPath: `C:\\Synthetic\\${id}`, executablePath: null, arguments: [] }], preferredInstallationId: `install-${id}`, status: 'planned', personalRating: 0, favorite: true, categories: ['personal'], customTags: ['mine'], notes: `Saved ${id}`, totalPlaySeconds: 0, playCount: 0, sessions: [], addedAt: '2026-10-03T00:00:00.000Z', lastPlayedAt: null }
}
function review(id: string): MatchReview {
  return { gameId: id, reviewId: `review-${id}`, name: `Local ${id}`, folderPath: `C:\\Synthetic\\${id}`, reason: 'Synthetic explicit review', sourceStatus: 'current', decisionFingerprint: `fingerprint-${id}`, options: [{ work: work(`candidate-${id}`), confidence: 0.9, reason: 'A clue, not confirmed' }] }
}
// Compile the real App setup once for this suite. Each case receives its own
// Vue effect scope; mocked bridge replies control asynchronous timing only.
const compiledHarness = (async () => {
  const source = await fs.readFile(new URL('../src/App.vue', import.meta.url), 'utf8')
  const script = source.split('<script setup lang="ts">')[1]!.split('</script>')[0]!
  const expose = '\nglobalThis.audit = { snapshot, busy, currentReview, reviewOpen, reviewGameId, reviewChoice, reviewError, reviewReloadNeeded, openMatchReviews, selectReview, laterReview, resolveReview, searchReviewAgain, reviewDetailOpen, reviewDetailWork, reviewDetailError, showReviewDetail, bindOpen, bindTargetId, bindReviewContext, bindQuery, detailOpen, selectedGameId, selectedWork, editor, showGame };'
  const compiled = await transform(script.replace(/^import .*$/gm, '') + expose, { loader: 'ts', format: 'cjs', target: 'es2022' })
  return { script, compiled }
})()
async function harness(t: { after(callback: () => void): void }) {
  const { script, compiled } = await compiledHarness
  const messages: Array<{ message: string; kind: string }> = []
  const sandbox: any = { Error, ...vue, ...libraryWorkspace, ...libraryInsights, ...savedLibraryViews, ...coverDisplay, ...importWorkspace,
    onMounted: () => {}, onUnmounted: () => {}, isPreview: false, useKunMessage: (message: string, kind: string) => messages.push({ message, kind }), useKunAlert: async () => true, api: {},
    document: { documentElement: { dataset: {}, classList: { toggle() {} } }, querySelector: () => null }, matchMedia: () => ({ matches: false }) }
  for (const line of script.split('\n').filter(line => /^import \{/.test(line))) for (const name of line.match(/^import \{([^}]+)\}/)![1]!.split(',').map(name => name.trim())) if (!(name in sandbox)) sandbox[name] = name
  const scope = vue.effectScope(); t.after(() => scope.stop())
  vm.createContext(sandbox); scope.run(() => vm.runInContext(compiled.code, sandbox))
  const a = sandbox.audit, games = ['A', 'B'].map(game), reviews = ['A', 'B'].map(review)
  const state = (items = games, queue = reviews): AppSnapshot => ({ games: structuredClone(items), settings: { baseUrl: 'https://fixture.invalid/v2/', hasApiKey: true, includeNsfw: false, theme: 'light', readExecutableMetadata: false }, activeSession: null, dataPath: 'synthetic-only', version: 'fixture', matching: { id: 'fixture-job', status: 'completed', total: 2, processed: 2, matched: 0, unmatched: 2, failed: 0, currentName: null, reviews: structuredClone(queue) } })
  a.snapshot.value = state(); sandbox.api.getState = async () => state()
  a.openMatchReviews(); await vue.nextTick()
  return { a, api: sandbox.api, games, reviews, state, messages }
}

function gameUnderReview(state: { snapshot: vue.Ref<AppSnapshot>; reviewGameId: vue.Ref<string | null> }) {
  const game = state.snapshot.value.games.find(item => item.id === state.reviewGameId.value)
  assert.ok(game, 'the selected review still has a game in the current snapshot')
  return game
}

test('review confirmation captures its exact identity and choice once, blocks duplicate decisions and queue switches while pending', async t => {
  const { a, api, reviews, state } = await harness(t), response = deferred<unknown>(), calls: unknown[][] = []
  a.reviewChoice.value = reviews[0]!.options[0]!.work.id
  api.resolveMetadataMatch = (...args: unknown[]) => { calls.push(args); return response.promise }
  const pending = a.resolveReview()
  await a.resolveReview(); await a.resolveReview(true); a.selectReview('B')
  assert.equal(calls.length, 1)
  assert.deepEqual(plain(calls[0]), ['A', 'candidate-A', 'review-A', 'fingerprint-A'])
  assert.equal(a.currentReview.value.gameId, 'A')
  assert.equal(a.busy.has('matching-resolve'), true)
  api.getState = async () => state(undefined, [reviews[1]!])
  response.resolve({}); await pending
  assert.equal(a.currentReview.value.gameId, 'B')
  assert.equal(a.reviewChoice.value, '')
  assert.equal(a.busy.has('matching-resolve'), false)
})

test('a rejected decision retains the current choice and a recoverable inline error without claiming success', async t => {
  const { a, api, reviews, state, messages } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  let calls = 0
  api.resolveMetadataMatch = async () => { calls++; throw Error('Synthetic catalog unavailable') }
  await a.resolveReview()
  assert.equal(a.reviewChoice.value, 'candidate-A')
  assert.match(a.reviewError.value, /Synthetic catalog unavailable|未完成|失败/)
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.busy.has('matching-resolve'), false)
  assert.ok(!messages.some(message => message.kind === 'success'))
  api.resolveMetadataMatch = async () => { calls++; return {} }
  api.getState = async () => state(undefined, [reviews[1]!])
  await a.resolveReview()
  assert.equal(calls, 2)
  assert.equal(a.reviewError.value, '')
})

test('a rejection after committing the association refreshes the actual current work, clears the old choice and reports incomplete completion without resubmitting', async t => {
  const { a, api, games, reviews, state, messages } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  const updated = { ...games[0]!, metadata: work('candidate-A') }
  const retainedReview = { ...reviews[0]!, sourceStatus: 'stale' as const, decisionFingerprint: 'fingerprint-after-library-commit' }
  let decisions = 0, reads = 0, associationCommitted = false
  api.resolveMetadataMatch = async (...args: unknown[]) => {
    decisions++
    assert.deepEqual(plain(args), ['A', 'candidate-A', 'review-A', 'fingerprint-A'])
    associationCommitted = true
    throw Error('Synthetic remaining queue persistence failure')
  }
  api.getState = async () => {
    reads++
    assert.equal(associationCommitted, true)
    return state([updated, games[1]!], [retainedReview, reviews[1]!])
  }
  await a.resolveReview()
  assert.equal(decisions, 1, 'a rejected response must not cause an automatic second association write')
  assert.equal(reads, 1, 'the actual association is refreshed before another decision is offered')
  assert.equal(gameUnderReview(a).metadata.id, 'candidate-A')
  assert.equal(a.currentReview.value.reviewId, 'review-A')
  assert.equal(a.currentReview.value.sourceStatus, 'stale')
  assert.equal(a.currentReview.value.decisionFingerprint, 'fingerprint-after-library-commit')
  assert.equal(a.reviewChoice.value, '')
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.match(a.reviewError.value, /操作未完整结束.*核对当前关联/)
  assert.match(a.reviewError.value, /Synthetic remaining queue persistence failure/)
  assert.equal(a.busy.has('matching-resolve'), false)
  assert.equal(gameUnderReview(a).notes, games[0]!.notes)
  assert.equal(gameUnderReview(a).personalRating, games[0]!.personalRating)
  assert.ok(!messages.some(message => message.kind === 'success'))
})

test('a rejected decision whose actual snapshot cannot be read enters read-only recovery and never retries the decision automatically', async t => {
  const { a, api, state, messages } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  let decisions = 0, reads = 0
  api.resolveMetadataMatch = async () => { decisions++; throw Error('Synthetic uncertain decision failure') }
  api.getState = async () => { reads++; throw Error('Synthetic unavailable association snapshot') }
  await a.resolveReview()
  assert.equal(decisions, 1); assert.equal(reads, 1)
  assert.equal(a.reviewReloadNeeded.value, true)
  assert.match(a.reviewError.value, /操作未完整结束.*重新读取/)
  assert.doesNotMatch(a.reviewError.value, /已提交/, 'a rejected decision is not proof that its association was committed')
  assert.ok(!messages.some(message => message.kind === 'success'))
  await a.resolveReview(true)
  assert.equal(decisions, 1)
  assert.equal(reads, 2); assert.equal(a.reviewReloadNeeded.value, true)
  assert.match(a.reviewError.value, /仍未能读取.*重新读取/)
  assert.doesNotMatch(a.reviewError.value, /已提交/, 'another failed read must not turn the unknown decision into an acknowledged commit')
  assert.ok(!messages.some(message => message.kind === 'success'))
  api.getState = async () => { reads++; return state() }
  await a.resolveReview()
  assert.equal(decisions, 1, 'successful read recovery does not replay the failed decision')
  assert.equal(reads, 3)
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.reviewError.value, '')
  assert.equal(gameUnderReview(a).metadata.id, 'old-A')
  assert.equal(a.currentReview.value.reviewId, 'review-A')
  assert.equal(a.busy.has('matching-resolve'), false)
})

test('a temporarily removed suggestion restored after queue-save rejection still shows its incomplete decision error and actual association', async t => {
  const { a, api, games, reviews, state, messages } = await harness(t), response = deferred<unknown>()
  a.reviewChoice.value = 'candidate-A'
  const updated = { ...games[0]!, metadata: work('candidate-A') }
  const restored = { ...reviews[0]!, sourceStatus: 'stale' as const, decisionFingerprint: 'fingerprint-after-partial-commit' }
  let decisions = 0, reads = 0
  api.resolveMetadataMatch = (...args: unknown[]) => {
    decisions++
    assert.deepEqual(plain(args), ['A', 'candidate-A', 'review-A', 'fingerprint-A'])
    return response.promise
  }
  const pending = a.resolveReview()
  // Matcher dismissal optimistically removes its exact item while the queue
  // save is pending. A failed save restores that item after the library commit.
  a.snapshot.value = state([updated, games[1]!], [reviews[1]!])
  assert.equal(a.currentReview.value.gameId, 'B')
  assert.equal(a.reviewChoice.value, '')
  api.getState = async () => { reads++; return state([updated, games[1]!], [restored, reviews[1]!]) }
  response.reject(Error('Synthetic optimistic queue-save rejection')); await pending
  assert.equal(decisions, 1, 'temporary queue removal must not cause an automatic repeated decision')
  assert.equal(reads, 1)
  assert.equal(a.currentReview.value.gameId, 'A')
  assert.equal(a.currentReview.value.reviewId, 'review-A')
  assert.equal(gameUnderReview(a).metadata.id, 'candidate-A')
  assert.equal(a.reviewChoice.value, '')
  assert.match(a.reviewError.value, /Local A.*操作未完整结束.*核对当前关联/)
  assert.match(a.reviewError.value, /Synthetic optimistic queue-save rejection/)
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.busy.has('matching-resolve'), false)
  assert.ok(!messages.some(message => message.kind === 'success'))
})

test('an acknowledged decision followed by snapshot failure retries only reading, even when reading fails again', async t => {
  const { a, api, reviews, state } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  let decisions = 0, reads = 0
  api.resolveMetadataMatch = async () => { decisions++; return {} }
  api.getState = async () => { reads++; throw Error('Synthetic snapshot unavailable') }
  await a.resolveReview()
  assert.equal(decisions, 1); assert.equal(reads, 1)
  assert.equal(a.reviewReloadNeeded.value, true)
  assert.match(a.reviewError.value, /已完成|已保存|已确认|已处理|已提交/)
  await a.resolveReview(true)
  assert.equal(decisions, 1, 'Keep Current must not issue a second decision in read recovery')
  assert.equal(reads, 2); assert.equal(a.reviewReloadNeeded.value, true)
  api.getState = async () => { reads++; return state(undefined, [reviews[1]!]) }
  await a.resolveReview()
  assert.equal(decisions, 1); assert.equal(reads, 3)
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.reviewError.value, '')
  assert.equal(a.currentReview.value.gameId, 'B')
})

test('closing and reopening after an acknowledged decision retains read-only recovery until the actual queue can be read', async t => {
  const { a, api, reviews, state } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  let decisions = 0, reads = 0
  api.resolveMetadataMatch = async () => { decisions++; return {} }
  api.getState = async () => { reads++; throw Error('Synthetic snapshot unavailable after acknowledged decision') }
  await a.resolveReview()
  assert.equal(decisions, 1); assert.equal(reads, 1)
  assert.equal(a.reviewReloadNeeded.value, true)
  const error = a.reviewError.value
  a.laterReview(); await vue.nextTick()
  assert.equal(a.reviewOpen.value, false)
  assert.equal(a.reviewChoice.value, '')
  assert.equal(a.reviewReloadNeeded.value, true)
  assert.equal(a.reviewError.value, error)
  a.openMatchReviews(); await vue.nextTick()
  assert.equal(a.reviewOpen.value, true)
  assert.equal(a.currentReview.value.reviewId, 'review-A')
  assert.equal(a.reviewReloadNeeded.value, true)
  api.getState = async () => { reads++; return state(undefined, [reviews[1]!]) }
  await a.resolveReview(true)
  assert.equal(decisions, 1, 'reopening a stale visible suggestion must not replay its acknowledged decision')
  assert.equal(reads, 2)
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.reviewError.value, '')
  assert.equal(a.currentReview.value.gameId, 'B')
  assert.equal(a.busy.has('matching-resolve'), false)
})

for (const field of ['reviewId', 'decisionFingerprint'] as const) test(`a changed ${field} clears a same-game choice synchronously even when the candidate ID stays identical`, async t => {
  const { a, reviews, state } = await harness(t)
  a.reviewChoice.value = 'candidate-A'
  a.reviewError.value = 'Old error'
  const replacement = { ...reviews[0]!, [field]: `new-${field}` }
  a.snapshot.value = state(undefined, [replacement, reviews[1]!])
  assert.equal(a.reviewChoice.value, '', 'a choice belongs to an exact suggestion, not only a work ID')
  assert.equal(a.reviewError.value, '')
  assert.equal(a.currentReview.value[field], `new-${field}`)
})

test('selecting another queued game clears the previous choice and error; unknown IDs do not change the current target', async t => {
  const { a } = await harness(t)
  a.reviewChoice.value = 'candidate-A'; a.reviewError.value = 'A error'
  a.selectReview('B')
  assert.equal(a.currentReview.value.gameId, 'B')
  assert.equal(a.reviewChoice.value, ''); assert.equal(a.reviewError.value, '')
  a.selectReview('missing')
  assert.equal(a.currentReview.value.gameId, 'B')
})

test('an old rejected decision cannot put its error into a replacement same-game suggestion', async t => {
  const { a, api, reviews, state } = await harness(t), response = deferred<unknown>()
  a.reviewChoice.value = 'candidate-A'; api.resolveMetadataMatch = () => response.promise
  const pending = a.resolveReview()
  a.snapshot.value = state(undefined, [{ ...reviews[0]!, reviewId: 'new-A', decisionFingerprint: 'new-fingerprint' }, reviews[1]!])
  response.reject(Error('Late previous suggestion error')); await pending
  assert.equal(a.currentReview.value.reviewId, 'new-A')
  assert.equal(a.reviewError.value, '')
  assert.equal(a.reviewChoice.value, '')
  assert.equal(a.reviewReloadNeeded.value, false)
  assert.equal(a.busy.has('matching-resolve'), false)
})

test('closing and reopening the queue invalidates a late decision error for the same game and suggestion', async t => {
  const { a, api } = await harness(t), response = deferred<unknown>()
  a.reviewChoice.value = 'candidate-A'; api.resolveMetadataMatch = () => response.promise
  const pending = a.resolveReview()
  a.reviewOpen.value = false; await vue.nextTick(); a.openMatchReviews(); await vue.nextTick()
  response.reject(Error('Late closed queue error')); await pending
  assert.equal(a.reviewOpen.value, true)
  assert.equal(a.currentReview.value.gameId, 'A')
  assert.equal(a.reviewError.value, '')
  assert.equal(a.reviewReloadNeeded.value, false)
})

test('independent candidate details reject late A success and retain the pending B view without changing the personal editor', async t => {
  const { a, api, games } = await harness(t), first = deferred<WorkMetadata>(), second = deferred<WorkMetadata>()
  await a.showGame(games[0]); a.editor.notes = 'Unsaved personal draft'; a.editor.personalRating = '7.2'
  const editor = plain(a.editor), selectedWork = plain(a.selectedWork.value)
  let requests = 0; api.getWork = () => ++requests === 1 ? first.promise : second.promise
  const old = a.showReviewDetail(work('candidate-A'))
  const newer = a.showReviewDetail(work('candidate-B'))
  first.resolve({ ...work('candidate-A'), description: 'Late A details' }); await old
  assert.equal(a.reviewDetailWork.value.id, 'candidate-B')
  assert.equal(a.busy.has('review-detail'), true)
  second.resolve({ ...work('candidate-B'), description: 'Full B details' }); await newer
  assert.equal(a.reviewDetailWork.value.description, 'Full B details')
  assert.equal(a.busy.has('review-detail'), false)
  assert.deepEqual(plain(a.editor), editor)
  assert.deepEqual(plain(a.selectedWork.value), selectedWork)
})

test('closed and reopened same-work candidate details ignore an old error and cannot release the new request busy flag', async t => {
  const { a, api } = await harness(t), first = deferred<WorkMetadata>(), second = deferred<WorkMetadata>()
  let requests = 0; api.getWork = () => ++requests === 1 ? first.promise : second.promise
  const old = a.showReviewDetail(work('candidate-A'))
  a.reviewDetailOpen.value = false; await vue.nextTick()
  const newer = a.showReviewDetail(work('candidate-A'))
  first.reject(Error('Late closed candidate error')); await old
  assert.equal(a.reviewDetailOpen.value, true)
  assert.equal(a.reviewDetailError.value, '')
  assert.equal(a.busy.has('review-detail'), true)
  second.resolve(work('candidate-A')); await newer
  assert.equal(a.busy.has('review-detail'), false)
})

test('manual search for a stale suggestion carries its precise review identity without changing library records or personal drafts', async t => {
  const { a, games, reviews, state } = await harness(t)
  await a.showGame(games[0]); a.editor.notes = 'Preserve this draft'
  const stale = { ...reviews[0]!, sourceStatus: 'stale' as const, decisionFingerprint: 'current-stale-fingerprint' }
  a.snapshot.value = state(undefined, [stale, reviews[1]!]); await vue.nextTick()
  const before = plain(a.snapshot.value.games), editor = plain(a.editor)
  a.searchReviewAgain(a.currentReview.value)
  assert.equal(a.reviewOpen.value, false); assert.equal(a.bindOpen.value, true)
  assert.equal(a.bindTargetId.value, 'A'); assert.equal(a.bindQuery.value, games[0]!.name)
  assert.deepEqual(plain(a.bindReviewContext.value), { reviewId: 'review-A', fingerprint: 'current-stale-fingerprint' })
  assert.deepEqual(plain(a.snapshot.value.games), before)
  assert.deepEqual(plain(a.editor), editor)
})
