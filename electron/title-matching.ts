export interface GameTitleComparison {
  /** A retrieval/ranking coefficient, not a probability of identifying a work. */
  score: number
  exact: boolean
  conflict: string | null
  version: 'same' | 'unspecified' | 'conflict'
  distinctive: boolean
}

export const MAX_TITLE_LENGTH = 500
const MAX_VARIANTS = 8
const GENERIC_NAMES = new Set([
  'game', 'games', 'gamedata', 'game32', 'game64', 'data', 'datafiles', 'engine', 'content', 'assets', 'plugins',
  'binaries', 'win32', 'win64', 'windows', 'x86', 'x64', 'www', 'backup', 'save', 'saves', 'savedata', 'managed',
  'runtime', 'resources', 'system', 'start', 'startup', 'launch', 'launcher', 'main', 'config', 'settings',
  'setup', 'install', 'installer', 'uninstall', 'directx', 'dotnet', 'advhd', 'siglusengine', 'onscripter',
  'nscripter', 'kirikiri', 'krkr', 'krkrz', 'artemis', 'bgi', 'rio', 'reallive', 'malie', 'unityplayer',
  '游戏', '游戏目录', '游戏文件', '游戏本体', '本体', '新建文件夹',
])
const PACKAGING = /^(?:(?:pc|windows|win(?:32|64)|x(?:86|64)|chs|cht|cn|jp|en)|(?:简体|繁体|官方)?中文(?:版)?|官中(?:版)?|汉化(?:硬盘|整合)?版?|(?:日文|英文|日语|英语|简中|繁中)版?|免安装(?:版)?|硬盘版|绿色版|直装版|解压即玩)$/i
const PACKAGING_SUFFIX = /(?:[\s._-]+(?:chs|cht|x64|x86|win32|win64)|[\s._-]*(?:(?:简体|繁体|官方)中文(?:版)?|官中(?:版)?|汉化(?:硬盘|整合)?版|免安装(?:版)?|硬盘版|绿色版|直装版|解压即玩))$/i

function prepared(value: string): string {
  if (typeof value !== 'string' || value.length > MAX_TITLE_LENGTH) return ''
  return stripTitlePackaging(value.normalize('NFKC').replace(/[\u0000-\u001f\u007f]/g, ' '))
}

// Both evidence collection and title comparison exclude the same packaging labels.
export function stripTitlePackaging(value: string): string {
  let result = value.trim()
  result = result.replace(/\[([^\[\]]*)\]|【([^【】]*)】|\(([^()]*)\)/g, (all, square, chinese, round) => {
    const tokens = String(square ?? chinese ?? round).trim().split(/[\s,+/|・]+/).filter(Boolean)
    return tokens.length && tokens.every(token => PACKAGING.test(token)) ? ' ' : all
  })
  let previous: string
  do { previous = result; result = result.replace(PACKAGING_SUFFIX, '').trim() } while (result !== previous)
  return result.replace(/\s+/g, ' ')
}

function foldLatinAccents(value: string): string {
  // Remove Latin accents, but preserve Japanese dakuten and other non-Latin
  // combining marks: パ and ハ must never become an exact title match.
  return value.normalize('NFD').replace(/(\p{Script=Latin})\p{M}+/gu, '$1').normalize('NFC')
}

/** Normalize layout without discarding a sequel number or edition identifier. */
export function normalizeGameTitle(value: string): string {
  return foldLatinAccents(prepared(value)).toLocaleLowerCase('en-US')
    .replace(/[^\p{L}\p{N}\p{M}]+/gu, ' ').replace(/\s+/g, ' ').trim()
}

function compact(value: string): string { return value.replace(/\s+/g, '') }
function meaningful(value: string): boolean { return !!value && !GENERIC_NAMES.has(compact(value)) }

interface Marker { key: string; pattern: RegExp }
const EDITIONS: Marker[] = [
  { key: 'remake', pattern: /\bremake(?:\s+for\s+(?:fhd|qhd|hd))?(?:\s+edition)?\b/gi },
  { key: 'remastered', pattern: /\bremaster(?:ed)?(?:\s+edition)?\b/gi },
  { key: 'reboot', pattern: /\bre\s*boot(?:\s+edition)?\b/gi },
  { key: 'reflection-blue', pattern: /\breflection\s*blue(?:\s+edition)?\b/gi },
  { key: 'sana', pattern: /\bsana(?:\s+edition)?\b/gi },
  ...['qhd', 'fhd', 'hd', '4k'].map(key => ({ key: `resolution:${key}`, pattern: new RegExp(`\\b${key}(?:\\s+edition)?\\b`, 'gi') })),
  { key: 'r18', pattern: /\br\s*18(?:\s+edition)?\b/gi },
  { key: 'all-ages', pattern: /\ball\s*ages(?:\s+edition)?\b|全年[齢龄齡](?:版)?/gi },
  ...['complete', 'ultimate', 'deluxe', 'definitive', 'extended', 'renewal', 'special', 'perfect'].map(key => ({ key, pattern: new RegExp(`\\b${key}\\s+edition\\b`, 'gi') })),
  { key: 'remake', pattern: /リメイク(?:版)?|重[製制]版/gu },
  { key: 'remastered', pattern: /リマスター(?:版)?/gu },
]
const EXPANSIONS: Marker[] = [
  { key: 'fd', pattern: /\b(?:fd|fan\s*disc)(?:\s+edition)?\b|ファンディスク/gi },
  { key: 'after', pattern: /\bafter(?:\s*story)?\b|アフターストーリー/gi },
  { key: 'new-episode', pattern: /\bnew\s*(?:episode|chapter)\b|新[章篇編编]/gi },
]
const NUMBER_WORDS: Record<string, string> = { one: '1', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', i: '1', ii: '2', iii: '3', iv: '4', v: '5', vi: '6', vii: '7', viii: '8', ix: '9', x: '10' }
const EAST_ASIAN_DIGITS: Record<string, number> = { '〇': 0, '零': 0, '一': 1, '壱': 1, '壹': 1, '二': 2, '弐': 2, '貳': 2, '贰': 2, '三': 3, '参': 3, '參': 3, '四': 4, '五': 5, '六': 6, '七': 7, '八': 8, '九': 9 }

function chapterNumber(value: string): string {
  if (/^\d+$/.test(value)) return String(Number(value))
  const characters = Array.from(value)
  if (characters.every(character => character in EAST_ASIAN_DIGITS)) return String(Number(characters.map(character => EAST_ASIAN_DIGITS[character]).join('')))
  let total = 0, digit = 0
  for (const character of characters) {
    if (character in EAST_ASIAN_DIGITS) digit = EAST_ASIAN_DIGITS[character]
    else { total += (digit || 1) * (character === '百' ? 100 : 10); digit = 0 }
  }
  return String(total + digit)
}

interface ParsedTitle {
  normalized: string
  base: string
  editions: Set<string>
  expansions: Set<string>
  numbers: string[]
  chapters: string[]
}

function parse(value: string): ParsedTitle {
  const normalized = normalizeGameTitle(value), editions = new Set<string>(), expansions = new Set<string>()
  let base = normalized
  // Inspect the unmodified title for each marker, because one marker may include
  // another ("Remake for FHD" has both a remake and a resolution identifier).
  for (const marker of EDITIONS) {
    marker.pattern.lastIndex = 0
    if (marker.pattern.test(normalized)) editions.add(marker.key)
    marker.pattern.lastIndex = 0
    base = base.replace(marker.pattern, ' ')
  }
  base = base.replace(/\b(\d+)(?:st|nd|rd|th)?\s+anniversary(?:\s+edition)?\b/g, (_, number: string) => { editions.add(`anniversary:${Number(number)}`); return ' ' })
    .replace(/(\d+)周年(?:記念|纪念|紀念)?(?:版)?/g, (_, number: string) => { editions.add(`anniversary:${Number(number)}`); return ' ' })
  base = base.replace(/\bv\s*(\d+(?:\s+\d+)*)\b/g, (_, number: string) => { editions.add(`version:${number.replace(/\s+/g, '.')}`); return ' ' })
  for (const marker of EXPANSIONS) {
    marker.pattern.lastIndex = 0
    if (marker.pattern.test(base)) expansions.add(marker.key)
    marker.pattern.lastIndex = 0
    base = base.replace(marker.pattern, ' ')
  }
  const chapters: string[] = []
  const numberPattern = '(\\d+|viii|vii|iii|vi|iv|ii|ix|one|two|three|four|five|six|seven|eight|nine|ten|[ivx])'
  base = base.replace(new RegExp(`\\b(?:episodes?|ep|chapters?|chap|volumes?|vol|part|act)\\s*${numberPattern}\\b`, 'g'), (_, number: string) => {
    const canonical = NUMBER_WORDS[number] ?? String(Number(number))
    chapters.push(canonical)
    return ` episode ${canonical} `
  })
  base = base.replace(/第([0-9〇零一二三四五六七八九十百壱壹弐貳贰参參拾]+)(?:章|話|话|巻|卷|部|作)/g, (_, number: string) => {
    const canonical = chapterNumber(number); chapters.push(canonical); return ` episode ${canonical} `
  })
  // Only isolated numerals are treated as sequence labels. 九 in 九州 and the
  // numerals inside an ordinary Japanese word must remain untouched.
  base = base.replace(/(^|\s)([〇零一二三四五六七八九十百壱壹弐貳贰参參拾]+)(?=\s|$)/g, (_, before: string, number: string) => `${before}${chapterNumber(number)}`)
  base = base.replace(/\b([ivx]+)$/g, (all, roman: string) => NUMBER_WORDS[roman] ?? all)
  const numbers = [...base.matchAll(/\d+/g)].map(match => String(Number(match[0])))
  return { normalized, base: base.replace(/\s+/g, ' ').trim(), editions, expansions, numbers, chapters }
}

/** Whether renaming from a generic work title could hide an explicit edition,
 * expansion, or chapter label. Plain numbers alone do not imply an edition. */
export function hasExplicitGameVersion(value: string): boolean {
  const title = parse(value)
  return !!(title.editions.size || title.expansions.size || title.chapters.length)
}

/** Query variants are retrieval hints only; a base-title fallback does not prove
 * that an edition/expansion and its base work are the same release. */
export function gameTitleVariants(value: string): string[] {
  if (typeof value !== 'string' || value.length > MAX_TITLE_LENGTH) return []
  const primary = value.trim(), cleaned = prepared(value), normalized = normalizeGameTitle(value)
  if (!meaningful(normalized)) return []
  const parsed = parse(value), candidates = [primary, cleaned]
  const accentFolded = foldLatinAccents(cleaned)
  if (accentFolded !== cleaned) candidates.push(accentFolded)
  // Remove punctuation while retaining spaces; then also try joining hyphenated
  // romanization pieces. Never generate translations or new name aliases.
  candidates.push(cleaned.replace(/[^\p{L}\p{N}\p{M}]+/gu, ' ').trim())
  candidates.push(cleaned.replace(/(?<=[\p{L}\p{N}])[-‐‑‒–—.]+(?=[\p{L}\p{N}])/gu, '').replace(/[!:;☆†]/gu, '').trim())
  if (/^[a-z]{1,4}\s+\d/i.test(cleaned)) candidates.push(cleaned.replace(/^([a-z]{1,4})\s+(?=\d)/i, '$1:'))
  candidates.push(normalized)
  if (/\bedition$/.test(normalized)) {
    const withoutEditionWord = normalized.replace(/\s+edition$/, '')
    candidates.push(withoutEditionWord)
    if (/^[a-z0-9 ]+$/i.test(withoutEditionWord)) candidates.push(compact(withoutEditionWord))
  }
  if (parsed.editions.size || parsed.expansions.size) {
    if (meaningful(parsed.base)) {
      candidates.push(parsed.base)
      // This compact Latin-only hint finds APIs that index DRACURIOT rather than
      // DRACU-RIOT. It remains a query, never an automatically equivalent title.
      if (/^[a-z0-9 ]+$/i.test(parsed.base)) candidates.push(compact(parsed.base))
    }
  }
  const seen = new Set<string>()
  return candidates.map(candidate => candidate.replace(/\s+/g, ' ').trim()).filter(candidate => {
    const key = candidate.toLocaleLowerCase('en-US')
    if (!meaningful(normalizeGameTitle(candidate)) || seen.has(key)) return false
    seen.add(key); return true
  }).slice(0, MAX_VARIANTS)
}

function equalSet(a: Set<string>, b: Set<string>): boolean { return a.size === b.size && [...a].every(value => b.has(value)) }
function sameNumbers(a: string[], b: string[]): boolean { return a.length === b.length && a.every((number, index) => number === b[index]) }

function markerRelation(a: Set<string>, b: Set<string>): 'same' | 'unspecified' | 'conflict' {
  if (equalSet(a, b)) return 'same'
  if (!a.size || !b.size) return 'unspecified'
  if (![...a].some(value => b.has(value))) return 'conflict'
  for (const family of [['remake', 'remastered', 'reboot'], ['sana', 'reflection-blue'], ['r18', 'all-ages']]) {
    const left = family.filter(value => a.has(value)), right = family.filter(value => b.has(value))
    if (left.length && right.length && !left.some(value => right.includes(value))) return 'conflict'
  }
  for (const dimension of ['resolution:', 'anniversary:', 'version:']) {
    const left = [...a].filter(value => value.startsWith(dimension)), right = [...b].filter(value => value.startsWith(dimension))
    if (left.length && right.length && !left.some(value => right.includes(value))) return 'conflict'
  }
  return 'unspecified'
}

function dice(a: string, b: string): number {
  if (a === b) return a ? 1 : 0
  const left = Array.from(a), right = Array.from(b)
  if (left.length < 2 || right.length < 2) return 0
  const pairs = new Map<string, number>()
  for (let i = 1; i < left.length; i++) { const key = left[i - 1] + left[i]; pairs.set(key, (pairs.get(key) ?? 0) + 1) }
  let common = 0
  for (let i = 1; i < right.length; i++) { const key = right[i - 1] + right[i], count = pairs.get(key) ?? 0; if (count) { common++; pairs.set(key, count - 1) } }
  return 2 * common / (left.length + right.length - 2)
}

function editSimilarity(a: string, b: string): number {
  const left = Array.from(a), right = Array.from(b)
  if (!left.length || !right.length) return 0
  let previous = Array.from({ length: right.length + 1 }, (_, index) => index)
  for (let i = 1; i <= left.length; i++) {
    const row = [i]
    for (let j = 1; j <= right.length; j++) row.push(Math.min(row[j - 1] + 1, previous[j] + 1, previous[j - 1] + Number(left[i - 1] !== right[j - 1])))
    previous = row
  }
  return 1 - previous[right.length] / Math.max(left.length, right.length)
}

function informative(value: string): boolean {
  const key = compact(value)
  if (!meaningful(value)) return false
  return /^[a-z0-9]+$/i.test(key) ? key.length >= 4 : Array.from(key).length >= 2
}

/** Compare names, not works: callers must still validate IDs, aliases, candidate
 * uniqueness, and the fetched work's primary title before changing a library. */
export function compareGameTitles(local: string, candidate: string): GameTitleComparison {
  const left = parse(local), right = parse(candidate)
  const a = compact(left.normalized), b = compact(right.normalized)
  const distinctive = informative(left.normalized) && informative(right.normalized)
  if (!meaningful(left.normalized) || !meaningful(right.normalized)) return { score: 0, exact: false, conflict: '目录或引擎名称，缺少作品标识', version: 'unspecified', distinctive: false }
  const exact = a === b
  if (exact) return { score: 1, exact: true, conflict: null, version: 'same', distinctive }
  const edition = markerRelation(left.editions, right.editions), expansion = markerRelation(left.expansions, right.expansions)
  let version: GameTitleComparison['version'] = edition === 'conflict' || expansion === 'conflict' ? 'conflict' : edition === 'unspecified' || expansion === 'unspecified' ? 'unspecified' : 'same'
  let conflict: string | null = null
  let ceiling = 0.96
  if (version === 'conflict') { conflict = '版本或追加篇标识不同'; ceiling = 0.35 }
  if (!sameNumbers(left.numbers, right.numbers)) {
    if (left.numbers.length && right.numbers.length) { version = 'conflict'; conflict = '作品或篇章编号不同'; ceiling = 0.25 }
    else { version = version === 'conflict' ? 'conflict' : 'unspecified'; conflict ??= '作品编号未完整对应'; ceiling = Math.min(ceiling, 0.65) }
  }
  if ((left.expansions.has('new-episode') && right.chapters.length) || (right.expansions.has('new-episode') && left.chapters.length)) {
    version = 'conflict'; conflict = '新章与编号章节不同'; ceiling = Math.min(ceiling, 0.25)
  }
  let score = 0.6 * dice(a, b) + 0.4 * editSimilarity(a, b)
  const baseA = compact(left.base), baseB = compact(right.base)
  if (baseA && baseA === baseB) score = Math.max(score, version === 'same' ? 0.96 : 0.88)
  if (version === 'unspecified') ceiling = Math.min(ceiling, 0.88)
  // A word occurring inside an unrelated longer ASCII word is weak evidence,
  // especially for short titles such as AIR versus CHAIR or FAIRY.
  const shorter = a.length <= b.length ? a : b, longer = a.length <= b.length ? b : a
  const shorterTitle = a.length <= b.length ? left.normalized : right.normalized
  if (/^[a-z]+$/i.test(shorterTitle) && longer.includes(shorter)) {
    const longerTitle = a.length <= b.length ? right.normalized : left.normalized
    if (!longerTitle.split(' ').includes(shorter)) ceiling = Math.min(ceiling, shorter.length <= 4 ? 0.3 : 0.6)
  }
  if (!distinctive) ceiling = Math.min(ceiling, 0.45)
  return { score: Math.round(Math.max(0, Math.min(ceiling, score)) * 1000) / 1000, exact: false, conflict, version, distinctive }
}
