import { promises as fs } from 'node:fs'
import path from 'node:path'
import type { LibraryGame, WorkMetadata, ReleaseIdentity } from '../src/shared/types'
import { collectLocalGameEvidence } from './local-game-evidence'
import { readLocalTextEvidence } from './local-text-evidence'
import { gameTitleVariants, compareGameTitles, normalizeGameTitle, stripTitlePackaging, MAX_TITLE_LENGTH } from './title-matching'
import { rankEvidenceMatches, type IdentityEvidence, type TitleEvidence, type ReferenceEvidence } from './evidence-matching'

export interface TitleIndexRecord { names: string[]; references: string[] }
export interface GameIdentity {
  names: string[]
  references: string[]
  referenceSource: 'sidecar' | 'steam' | 'title-index' | 'none'
  evidence?: IdentityEvidence
  queries?: string[]
  releases?: Record<string, ReleaseIdentity[]>
}
export interface RankedMetadataMatch { work: WorkMetadata; confidence: number; reason: string; automatic: boolean }

const MAX_NAMES = 8
const MAX_REFERENCES = 8
const SOURCE_ORDER = ['vndb', 'bangumi', 'steam'] as const
const GENERIC_EXECUTABLE = /^(?:game(?:32|64)?|start|startup|launch(?:er)?|play|player|advhd|siglusengine|bgi|rio|entisgls|reallive|malie|kirikiri|artemis|unityplayer|main|config|settings|setup|uninstall)$/i

// Only packaging/language labels are removed. Episode numbers and edition names
// (FD, Remake, REFLECTION BLUE, etc.) remain part of a title's identity.
function cleanName(value: unknown): string {
  if (typeof value !== 'string' || value.length > MAX_TITLE_LENGTH) return ''
  return stripTitlePackaging(value.normalize('NFKC'))
}

function normalizedName(value: unknown): string {
  return cleanName(value).toLocaleLowerCase('en-US').replace(/[^\p{L}\p{N}]+/gu, '')
}

function names(values: unknown[]): string[] {
  const seen = new Set<string>(), result: string[] = []
  for (const value of values) {
    const cleaned = cleanName(value), key = normalizedName(cleaned)
    if (!key || seen.has(key)) continue
    seen.add(key); result.push(cleaned)
    if (result.length === MAX_NAMES) break
  }
  return result
}

function reference(source: unknown, value: unknown): string | null {
  if (typeof source !== 'string' || (typeof value !== 'string' && typeof value !== 'number')) return null
  let kind = source.trim().toLowerCase(), id = String(value).trim().toLowerCase()
  if (kind === 'bgm') kind = 'bangumi'
  if (!SOURCE_ORDER.some(item => item === kind)) return null
  if (kind === 'vndb') {
    if (!/^v?[1-9]\d{0,11}$/.test(id)) return null
    id = `v${id.replace(/^v/, '')}`
  } else if (!/^[1-9]\d{0,11}$/.test(id) || (kind === 'steam' && id === '480')) return null
  return `${kind}:${id}`
}

function parseReference(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const match = /^([^:]+):([^:]+)$/.exec(value)
  return match ? reference(match[1], match[2]) : null
}

function references(values: unknown[]): string[] {
  const unique = [...new Set(values.map(parseReference).filter((value): value is string => value !== null))]
  // Keep two conflicting IDs for every source even when many installations were
  // merged, so the result limit cannot accidentally turn a conflict into certainty.
  const evidence = SOURCE_ORDER.flatMap(source => unique.filter(value => value.startsWith(`${source}:`)).slice(0, 2))
  return [...new Set([...evidence, ...unique])].slice(0, MAX_REFERENCES)
}

function object(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function textValue(value: unknown): unknown {
  return object(value)?.Value ?? value
}

function contained(root: string, target: string): boolean {
  const relative = path.relative(root, target)
  return relative === '' || (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative))
}

async function readSmallFile(root: string, relative: string, limit: number): Promise<string | null> {
  try {
    const realFile = await fs.realpath(path.join(root, relative))
    if (!contained(root, realFile)) return null
    const file = await fs.open(realFile, 'r')
    try {
      const stat = await file.stat()
      if (!stat.isFile() || stat.size > limit) return null
      // A bounded read also handles a file growing after stat without allocating
      // or parsing an arbitrarily large file.
      const buffer = Buffer.alloc(limit + 1)
      let bytesRead = 0
      while (bytesRead <= limit) {
        const chunk = await file.read(buffer, bytesRead, buffer.length - bytesRead, bytesRead)
        if (!chunk.bytesRead) break
        bytesRead += chunk.bytesRead
      }
      if (bytesRead > limit || !contained(root, await fs.realpath(realFile))) return null
      const bytes = buffer.subarray(0, bytesRead)
      let result: string
      try { result = new TextDecoder('utf-8', { fatal: true }).decode(bytes) }
      catch { result = new TextDecoder('gb18030').decode(bytes) }
      return result.replace(/^\uFEFF/, '')
    } finally { await file.close() }
  } catch { return null }
}

function readSidecar(text: string | null): { names: unknown[]; references: string[] } {
  const result: { names: unknown[]; references: string[] } = { names: [], references: [] }
  if (!text) return result
  try {
    const parsed = object(JSON.parse(text)), game = object(parsed?.Game) ?? parsed
    if (!game) return result
    result.names = [game.Name, game.OriginalName, game.ChineseName, game.CnName].map(textValue)
    const ids = Array.isArray(game.Ids) ? game.Ids : []
    for (const [source, index] of [['vndb', 0], ['bangumi', 1], ['steam', 7]] as const) {
      const ref = reference(source, ids[index])
      if (ref) result.references.push(ref)
    }
    if (typeof ids[2] === 'string') {
      for (const token of ids[2].split(',').slice(0, 30)) {
        const ref = parseReference(token.trim())
        if (ref) result.references.push(ref)
      }
    }
  } catch { /* Invalid sidecars do not prevent a local game from importing. */ }
  return result
}

const indexCache = new WeakMap<TitleIndexRecord[], Map<string, TitleIndexRecord[]>>()
const readmeIndexCache = new WeakMap<TitleIndexRecord[], Array<{ key: string; value: string }>>()

async function readReadmeTitles(roots: Set<string>, index: TitleIndexRecord[]): Promise<string[]> {
  let candidates = readmeIndexCache.get(index)
  if (!candidates) {
    candidates = [...new Map(index.flatMap(row => row.names).map(value => [normalizedName(value), value])).entries()]
      .filter(([key]) => key.length >= 7 && key.length <= 160).map(([key, value]) => ({ key, value })).sort((a, b) => b.key.length - a.key.length)
    readmeIndexCache.set(index, candidates)
  }
  const found: string[] = []
  for (const root of roots) {
    const entries = await fs.readdir(root).catch(() => [] as string[])
    const files = entries.filter(name => /^(?:readme|read me|说明|必读|开始前请读|汉化说明|游戏说明|遊戲說明)[^\\/]*\.txt$/i.test(name)).slice(0, 3)
    for (const file of files) {
      const text = await readSmallFile(root, file, 64 * 1024)
      if (!text) continue
      // Only a short heading region is considered; prose and instructions are
      // never used as commands or uploaded to the metadata provider.
      const heading = text.split(/\r?\n/).slice(0, 24).join(' ').slice(0, 3000)
      const normalized = heading.normalize('NFKC').toLocaleLowerCase('en-US').replace(/[^\p{L}\p{N}]+/gu, '')
      if (!normalized) continue
      const matches = candidates.filter(row => normalized.includes(row.key)).slice(0, 3)
      // Reject a readme naming multiple unrelated works; nested short aliases of
      // the same heading are otherwise harmless.
      if (matches.length && matches.every(row => matches[0]!.key.includes(row.key))) found.push(matches[0]!.value)
    }
  }
  return names(found)
}

function indexedTitles(index: TitleIndexRecord[]): Map<string, TitleIndexRecord[]> {
  let result = indexCache.get(index)
  if (result) return result
  result = new Map()
  for (const record of index) {
    if (!Array.isArray(record.names) || !Array.isArray(record.references)) continue
    for (const key of new Set(record.names.map(normalizedName).filter(Boolean))) {
      const bucket = result.get(key)
      if (bucket) bucket.push(record)
      else result.set(key, [record])
    }
  }
  indexCache.set(index, result)
  return result
}

function uniqueIndexMatches(candidateNames: string[], index: TitleIndexRecord[]): TitleIndexRecord[] {
  const lookup = indexedTitles(index), matches = new Set<TitleIndexRecord>()
  for (const name of candidateNames) for (const match of lookup.get(normalizedName(name)) ?? []) matches.add(match)
  const identities = new Set<string>()
  for (const match of matches) {
    const refs = references(match.references)
    const primary = refs.find(ref => ref.startsWith('vndb:')) ?? refs.find(ref => ref.startsWith('bangumi:')) ?? refs[0]
    // A record without any usable ID cannot disambiguate another matching row.
    identities.add(primary ?? `unknown:${[...matches].indexOf(match)}`)
  }
  return identities.size === 1 ? [...matches] : []
}

/** Read only small, known metadata files in the game's selected installations. */
export async function readGameIdentity(game: LibraryGame, index: TitleIndexRecord[], options: { readExecutableMetadata?: boolean } = {}): Promise<GameIdentity> {
  const sidecarNames: unknown[] = [], folderNames: unknown[] = [], exeNames: unknown[] = []
  const sidecarRefs: string[] = [], steamRefs: string[] = []
  const installations = [...game.installations].sort((a, b) => Number(b.id === game.preferredInstallationId) - Number(a.id === game.preferredInstallationId))
  const roots = new Set<string>()
  for (const installation of installations.slice(0, 100)) {
    folderNames.push(path.basename(installation.folderPath))
    if (installation.executablePath) {
      const basename = path.basename(installation.executablePath, path.extname(installation.executablePath))
      if (!GENERIC_EXECUTABLE.test(basename)) exeNames.push(basename)
    }
    try {
      const root = await fs.realpath(installation.folderPath), key = process.platform === 'win32' ? root.toLowerCase() : root
      if (roots.has(key) || !(await fs.stat(root)).isDirectory()) continue
      roots.add(key)
      const [sidecarText, steamText] = await Promise.all([
        readSmallFile(root, path.join('.PotatoVN', 'meta.json'), 1024 * 1024),
        readSmallFile(root, 'steam_appid.txt', 128),
      ])
      const sidecar = readSidecar(sidecarText)
      sidecarNames.push(...sidecar.names); sidecarRefs.push(...sidecar.references)
      const steamRef = reference('steam', steamText?.trim())
      if (steamRef) steamRefs.push(steamRef)
    } catch { /* Missing/offline installations can still be identified by name. */ }
  }
  const candidateNames = names([...sidecarNames, game.name, ...folderNames, ...exeNames])
  const localRefs = references([...sidecarRefs, ...steamRefs])
  const supplemental = await collectLocalGameEvidence(game, options)
  const documents = await readLocalTextEvidence(game)
  const readmeTitles = await readReadmeTitles(roots, index)
  const titleEvidence: TitleEvidence[] = [
    ...folderNames.filter((value): value is string => typeof value === 'string').map(value => ({ value, source: 'folder' as const })),
    ...(!folderNames.includes(game.name) ? [{ value: game.name, source: 'custom-name' as const }] : []),
    ...supplemental.titles,
    ...documents.titles,
    ...readmeTitles.map(value => ({ value, source: 'readme-title' as const })),
    ...names(sidecarNames).map(value => ({ value, source: 'sidecar' as const })),
  ]
  // Previous PotatoVN IDs and titles have one shared origin. The independent
  // title index is queried from actual local observations, never copied titles.
  const matches = uniqueIndexMatches(names(titleEvidence.filter(title => title.source !== 'sidecar').map(title => title.value)), index)
  const indexedRefs = references(matches.flatMap(match => match.references))
  const indexProofs = matches.flatMap(record => titleEvidence.filter(title => !['sidecar', 'readme-title'].includes(title.source) && record.names.some(name => normalizedName(name) === normalizedName(title.value))).map(title => ({ observedTitle: title.value, source: title.source, recordNames: record.names.slice(0, 30), references: references(record.references) }))).slice(0, 20)
  const referenceEvidence: ReferenceEvidence[] = [
    ...sidecarRefs.map(value => ({ value, source: 'sidecar' as const })),
    ...steamRefs.map(value => ({ value, source: 'steam-file' as const })),
    ...supplemental.references,
    ...indexedRefs.map(value => ({ value, source: 'title-index' as const })),
  ]
  const primaryQueries = names([...documents.titles.map(title => title.value), ...supplemental.titles.filter(title => title.source === 'product-name' || title.source === 'file-description').map(title => title.value), game.name, ...folderNames, ...readmeTitles, ...supplemental.titles.map(title => title.value)]).filter(value => compareGameTitles(value, value).score > 0)
  const queryPool = [...primaryQueries.slice(0, 3), ...primaryQueries.flatMap(gameTitleVariants), ...primaryQueries.slice(3), ...matches.flatMap(match => match.names)]
  const seenQueries = new Set<string>()
  const queries = queryPool.filter(value => {
    const key = normalizeGameTitle(value).replace(/\s/g, '')
    if (!key || seenQueries.has(key) || compareGameTitles(value, value).score === 0) return false
    seenQueries.add(key); return true
  }).slice(0, 6)
  return {
    names: names([...candidateNames, ...documents.titles.map(title => title.value), ...supplemental.titles.map(title => title.value), ...readmeTitles, ...matches.flatMap(match => match.names)]),
    references: references([...localRefs, ...supplemental.references.map(ref => ref.value), ...indexedRefs]),
    referenceSource: sidecarRefs.length ? 'sidecar' : steamRefs.length || supplemental.references.length ? 'steam' : indexedRefs.length ? 'title-index' : 'none',
    evidence: { titles: titleEvidence.slice(0, 40), references: referenceEvidence.slice(0, 40), companies: [...new Set([...supplemental.companies, ...documents.companies])], warnings: [...supplemental.warnings, ...documents.warnings], fingerprints: supplemental.executableFingerprints, indexProofs, auxiliaryDirectory: game.installations.some(installation => /(?:^|[\\/])(?:汉化补丁|补丁|patches|_patch|backup|beifen)(?:[\\/]|$)/i.test(installation.folderPath)) },
    queries,
  }
}

function similarity(a: string, b: string): number {
  if (!a || !b) return 0
  if (a === b) return 1
  const left = Array.from(a), right = Array.from(b)
  if (left.length < 2 || right.length < 2) return 0
  const bigrams = new Map<string, number>()
  for (let i = 1; i < left.length; i++) {
    const pair = left[i - 1] + left[i]
    bigrams.set(pair, (bigrams.get(pair) ?? 0) + 1)
  }
  let overlap = 0
  for (let i = 1; i < right.length; i++) {
    const pair = right[i - 1] + right[i], count = bigrams.get(pair) ?? 0
    if (count) { overlap++; bigrams.set(pair, count - 1) }
  }
  return 2 * overlap / (left.length + right.length - 2)
}

function referenceMap(values: string[]): Map<string, Set<string>> {
  const result = new Map<string, Set<string>>()
  for (const ref of values) {
    const [source] = ref.split(':')
    if (!result.has(source)) result.set(source, new Set())
    result.get(source)!.add(ref)
  }
  return result
}

/** Run this on search results and again on fetched full details before linking. */
export function rankMetadataMatches(identity: GameIdentity, works: WorkMetadata[]): RankedMetadataMatch[] {
  if (identity.evidence) return rankEvidenceMatches(identity, works)
  const queries = names(identity.names).map(normalizedName)
  const expected = references(identity.references), expectedMap = referenceMap(expected)
  const hasRefs = expected.length > 0
  const primary = SOURCE_ORDER.flatMap(source => expected.filter(ref => ref.startsWith(`${source}:`)))[0]
  const expectedConflict = [...expectedMap.values()].some(values => values.size > 1)
  const uniqueWorks = [...new Map(works.map(work => [work.id, work])).values()]
  const candidates = uniqueWorks.map(work => {
    // A work may list many releases from the same source. The eight-reference
    // limit belongs to the local identity DTO, not to these returned releases.
    const refs = [...new Set(work.references.map(ref => reference(ref.source, ref.id)).filter((ref): ref is string => ref !== null))]
    const refSet = new Set(refs), actualMap = referenceMap(refs)
    const matchedRefs = expected.filter(ref => refSet.has(ref))
    const conflict = [...expectedMap].some(([source, expectedIds]) => {
      const actualIds = actualMap.get(source)
      return actualIds && ![...actualIds].some(value => expectedIds.has(value))
    })
    const direct = [work.title, work.originalTitle].map(normalizedName).filter(Boolean)
    const aliases = work.aliases.map(normalizedName).filter(Boolean)
    const exact = queries.some(query => direct.includes(query))
    const alias = queries.some(query => aliases.includes(query))
    const fuzzy = Math.max(0, ...queries.flatMap(query => [...direct, ...aliases].map(value => similarity(query, value))))
    return { work, matchedRefs, conflict, primaryMatch: !!primary && refSet.has(primary), exact, alias, fuzzy }
  })
  // Any second candidate supported by a different reference indicates a bad
  // cross-database mapping. Missing auxiliary refs alone are harmless.
  const supported = candidates.filter(candidate => candidate.matchedRefs.length)
  const directMatches = candidates.filter(candidate => candidate.exact)
  return candidates.map(candidate => {
    let confidence = candidate.exact ? 0.98 : candidate.alias ? 0.94 : Math.min(0.89, candidate.fuzzy * 0.89)
    let automatic = false
    let reason = candidate.exact ? '作品标题完整匹配' : candidate.alias ? '别名匹配，需确认作品及版本' : '名称相近，需确认作品及版本'
    if (hasRefs) {
      if (candidate.primaryMatch) { confidence = 1; reason = `${primary!.startsWith('vndb:') ? 'VNDB' : primary!.startsWith('bangumi:') ? 'Bangumi' : 'Steam'} 编号一致` }
      else if (candidate.matchedRefs.length) { confidence = 0.96; reason = '部分来源编号一致，需确认' }
      else { confidence = Math.min(confidence, 0.7); reason = '名称候选缺少一致的来源编号，需确认' }
      automatic = candidate.primaryMatch && !expectedConflict && !candidate.conflict && supported.length === 1
      if (expectedConflict || candidate.conflict || (candidate.matchedRefs.length && supported.length > 1)) {
        automatic = false
        confidence = Math.min(confidence, 0.9)
        reason = '来源编号存在冲突或指向多个作品，需确认'
      }
    } else if (candidate.exact && directMatches.length === 1) {
      automatic = true
      reason = '唯一作品的完整标题匹配'
    } else if (candidate.exact) {
      reason = '多个作品标题一致，需确认版本'
    }
    return { work: candidate.work, confidence, reason, automatic }
  }).sort((a, b) => Number(b.automatic) - Number(a.automatic) || b.confidence - a.confidence || a.work.id.localeCompare(b.work.id))
}
