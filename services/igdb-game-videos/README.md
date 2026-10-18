# IGDB game video adapter

Independent, disabled-by-default application adapter. It does not change or deploy
the Bangumi OAuth Worker. It neither serves nor proxies video bytes.

## Qualified public deployment — 2026-10-19

The owner approved first deployment and Secret setup. The existing account's
Workers usage page showed the Free plan's 100,000 requests/day limit; no plan
upgrade or paid resource was created. The live root is
`https://the-order-igdb-game-videos.zyx2251721565.workers.dev/`, matching the native
client default. Worker code is frozen at `29e8ef4`; the enabled version after the
tested disable/re-enable cycle is `188682bb-4b3c-4f97-9569-196049204c2e`.

Both developer values are Cloudflare Secrets. One SQLite namespace and
`single-app-quota-v1` remain the quota owner after redeployment. **Do not start
local or staging services with these same credentials.** Use synthetic upstreams,
a separately authorized development application, or the already shared namespace.
The local setup below is for an independently authorized development environment.

Public HTTPS passed 16 protocol/identity/cache/security checks. Two isolated
WinUI samples played after the owner clicked YouTube; native probes confirmed
`playing`. Disable returned 503, cached client metadata survived, re-enable
restored 200, and server cache timestamps survived. Clearing remote client cache
and restarting retained confirmation/default choices with five user files
unchanged. Nine sampled edge/DO events were `ok`, with CPU 1–6 ms and no exceptions.
These are low-load observations, not long-term or stress qualification. Daily
SQLite usage/storage dashboard metrics remain unverified. Desktop tooling failed;
new native render captures omitted video/text and are excluded from visual proof.

Full evidence is in
`.artifacts/igdb-production-ready-20261018/public-deployment-report.md`. Frozen
regression remains native 4428/0/0, Node 35/35, workerd/SQLite 7/7. This activation
did not publish a desktop update, change the installed daily app or touch a
Production profile. Existing public clients do not acquire this feature until a
separately authorized client release. Config still defaults to **off** on deploy;
use the reviewed operator script below to disable or explicitly re-enable.

## Developer access

Follow [IGDB's official setup](https://api-docs.igdb.com/#getting-started): a Twitch
developer account with 2FA and a **Confidential** application supplies the client
ID and secret. The app grant uses `client_credentials`, not an end-user login.
Check the Twitch developer agreement and IGDB's non-commercial/commercial usage
conditions before enabling a public service. This repository supplies no
credentials; the deployment above is limited to the owner's approved application.

1. Copy `.dev.vars.example` to `.dev.vars` **in this directory**, then fill the two
   developer values privately and set `IGDB_ENABLED=true`. Keep the file restricted
   to the developer's Windows account. Never put these values in the desktop
   configuration, a build argument, a screenshot, an issue, or a committed file.
2. For isolated local development, use the already pinned Wrangler installation:
   from this directory run
   `node ../bangumi-auth-worker/node_modules/wrangler/bin/wrangler.js dev --config wrangler.toml`.
   This uses only its runtime dependency, not the other Worker's code or deployment.
   No new account, payment, or production route is created by this task.
3. Set `THE_ORDER_NATIVE_IGDB_SERVICE_URL` only in the **Test app process**, e.g.
   `http://127.0.0.1:8787/`, together with the existing explicit Test Profile flags.
   Never start the candidate without those Profile flags. Production/Beta use the
   reviewed built-in HTTPS service root; only Test accepts explicit HTTP loopback.
   Test/Development never silently select the production service.

Public deployment requires separate authorization. `wrangler.toml` remains a
local, disabled-by-default configuration. The separately reviewed
`wrangler.production.jsonc` exposes only this independent Worker on workers.dev,
with its first deployment still **disabled**. It must route every query for the same IGDB app to
the single `single-app-quota-v1` Durable Object, including additional environments;
deploying independent coordinators with the same app credentials breaks the shared
quota guarantee. Do not reuse this credential pair in another uncoordinated client.

## Fixed protocol

`POST /v1/game-videos/query` accepts JSON containing only `title` (1–200 characters),
optional positive integer `id`, and optional numeric `steamId`. There are no raw
queries, arbitrary URLs, user credentials, or YouTube search operations.

- Exact IGDB IDs use an exact lookup. Steam's named external source is only a
  candidate lookup; VNDB and DLsite mappings are not assumed.
- The games request expands video record ID, owning game ID, original name and
  YouTube `video_id`, plus bounded identity and edition context.
  The current Game Type field is `game_type.type`, not `game_type.name`.
  `parent_game` and `version_parent` stay distinct; older cached game rows
  without parent context are refetched.
- At most 20 candidates are returned; the 21st row marks the result incomplete.
- Requests across all installations serialize through one coordinator, at least
  275 ms apart, with one upstream request active and at most eight unique queries.
  Identical queries coalesce with at most 32 waiters; cached reads bypass the slow
  upstream queue. Queued work expires after 12 seconds.
  Dispatch barriers and 429 cooldown survive Durable Object restart.
- App tokens stay in coordinator memory, renew before expiry, and get one renewal
  after 401. Tokens and secrets never enter responses or the cache.
- Upstream reads have an 8 second bound and 256 KiB cap. Redirects fail closed.
- Positive results cache for 12 hours, empty results for 30 minutes, at most 256
  query records. Authentication/network errors are never cached as an empty hit.

This is metadata from **IGDB · YouTube**, not proof that a video was uploaded by
the rights holder or matches a particular local release. Desktop attribution is
visible. Playback is the existing YouTube official embed, loaded by user action.

## Tests

`node --test test/worker.test.mjs` runs synthetic upstream fault/concurrency tests.
`test/run-local-fixture.mjs <owned artifact root>` is an opt-in synthetic Miniflare
contract server, using the repository's already installed runtime. Its fake IDs
are deliberately not a real title/video coverage fixture. Stop it with the
documented artifact marker; never expose this fixture to public users.

The authorized 2026-10-18 local run completed all 36 frozen samples: 31 identities
were manually reviewed, five original/version scopes remain unresolved, and 28
reviewed targets have video relations. This is not automatic-match accuracy.
Five sampled games actually played in WinUI, without public deployment. See
`docs/igdb-game-videos-v1.md` and `.artifacts/igdb-live-20261018/` evidence. Local
installation/version evidence and mock library inputs are reported separately.
The desktop remains usable when this service is missing.

The 36 samples in `test/samples.json` were frozen before IGDB queries. For future
authorized measurement, set `IGDB_TEST_SERVICE_URL` to your own adapter, then run
`node test/coverage.mjs --output=<new-report.json>` for the first ten, adding
`--all` for all 36. With no URL it writes an explicitly blocked report and makes
zero requests. Results require independent human identity/version review and
service-side evidence of the actual upstream; a reachable HTTPS adapter alone
does not prove a real IGDB run. This command never plays a video or binds a game.

An optional `--plan=<json>` supplies one to three title/alias queries per sample.
It must retain all 36 frozen IDs and labels; it cannot prefill IGDB or video IDs.
Keep the original run and plan, and report later supplementary lookups separately.

## Safe developer diagnostics

Set `IGDB_DIAGNOSTICS=true` only for an isolated development run. The error body
then contains a bounded `diagnostic` object: stage, HTTP status, whitelisted error
kind and network code. Token acquisition, games HTTP/network, response parsing,
mapping and internal adapter failures are distinct. The default remains false.
No raw exception/stack, upstream response body, headers, credential values or
authentication URLs are logged or returned. Do not enable verbose HTTP tracing
to investigate credentials. The 2026-10-18 503 was an invalid games field, not a
reason to change DNS/proxy settings or disable TLS validation.

## Public production candidate (2026-10-18)

Recommended URL: `https://the-order-igdb-game-videos.zyx2251721565.workers.dev/`.
The suffix follows the repository's existing first-party Worker address; it is
not a new purchased domain. **It is not yet a deployed or qualified public
endpoint.** Bootstrap verifies the address actually returned by Cloudflare. A
different account subdomain stops Secret setup and enabling until the client
address is corrected and requalified. The Bangumi service is never changed.

### Free plan and operating bounds

Official [DO pricing](https://developers.cloudflare.com/durable-objects/platform/pricing/)
allows SQLite objects on Workers Free. The current published free allowances are
100,000 DO requests/day, 13,000 GB-s/day, 5 million SQLite rows read/day, 100,000
rows written/day and 5 GB storage. KV API calls on SQLite objects also count as
row operations. The existing `new_sqlite_classes` migration is retained.
[Workers Free](https://developers.cloudflare.com/workers/platform/limits/) also
limits the account to 100,000 Worker invocations/day and 10 ms CPU/invocation.
These are **account-wide allowances**, shared with other Workers including the
existing OAuth Worker. No paid plan, extra database, cron, custom domain, queue
or analytics service is requested. No production resource was created in this
preparation task. Verify the account is still Free before bootstrap; CLI login
alone does not establish its billing plan.

The initial application limits are deliberately lower: 10,000 admitted queries,
1,000 games API dispatches and 1,000 token grants per UTC day; at most 60 games
dispatches/minute, with the existing 275 ms spacing. Counts are committed before
dispatch, include failed attempts/401 retries, and survive restart/deploy. All
counter changes serialize independently from the slow query queue. Corrupt
counter state fails closed rather than silently restoring a fresh budget.
The token is memory-only, so object recreation can require a new grant even
before expiry; the grant ceiling therefore covers cold starts rather than
assuming only 24 hourly renewals.
Cache entries remain bounded to 256; successful results last 12 h, true empty
results 30 min. Identity/Steam keys do not depend on whichever title was used.

The [edge Rate Limiting binding](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/)
provides 60 requests/minute per Cloudflare-observed IP. A missing/broken binding
or absent edge IP fails closed in public mode. IP limits can affect shared NAT
users, are approximate and local to each Cloudflare location, and **are not
global accounting or authentication**. There is no fixed client token to extract.
Even rejected traffic consumes Worker invocations. A distributed attack can
exhaust the account's free allowance and affect the other Worker; these controls
bound IGDB load but do not promise DDoS isolation or unlimited free availability.
On Free, quota exhaustion is a service failure, not permission to upgrade.

### Faults, monitoring and privacy

- Twitch and games 429 persist a bounded Retry-After cooldown, including HTTP-date
  values; token failures cannot repeatedly acquire grants after object restart.
- An invalid app credential opens a five-minute circuit; three transient failures
  open a 30-second circuit. One serialized recovery resets it. Cached metadata
  remains readable through upstream cooldown/circuit failures.
- Global budgets, cache writes or coordinator failures never produce an empty
  success. Oversize/slow bodies, unknown fields, arbitrary queries/URLs, redirects
  and browser origins are rejected. Input is bounded to 1 KiB, provider output
  to 256 KiB. Token POST bodies and upstream responses are never logged.
- Only public title and IGDB/Steam IDs cross the desktop protocol. No local entry
  ID, path, play record, game bytes, client key or Twitch login is sent. Headers
  from the desktop are not forwarded to the coordinator or upstream.
- Automatic invocation logs and traces are disabled. Only bounded circuit-open
  events and 80%/100% budget thresholds are emitted, without titles, IPs, tokens,
  response bodies or stacks. Use the existing Cloudflare dashboard's Worker
  request/error/CPU metrics, DO duration and SQLite read/write usage; review them
  after enabling and during the first day. Do not add paid monitoring. Budget
  alerts are operator notifications, not another billing cap.

### One app, one quota owner

There is exactly one production Worker name, DO namespace binding and object
name `single-app-quota-v1`. Do not add Wrangler named environments with the same
Twitch credentials. Stop **all** uncoordinated local/direct use of that application
before setting production Secrets/enabling it. The local testing runtime is not
a second live production quota owner. Future staging must omit these credentials
and either use synthetic responses, a separately authorized development app, or
bind to the production object's namespace via `script_name` and the same name;
it must never create another quota namespace for the shared app. This repository
cannot prevent an operator from reusing the credentials in unrelated software.

### Local qualification and first-deployment approval

From the repository root:

```powershell
node services/igdb-game-videos/scripts/check-deployment.mjs --bundle=.artifacts/igdb-production-ready-20261018/worker-candidate-final
node --test --test-concurrency=1 services/igdb-game-videos/test/*.test.mjs
node services/igdb-game-videos/test/public-contract.mjs --output=.artifacts/igdb-production-ready-20261018/contract-new-run
```

The bundle command stages only public code/configuration in an owned directory,
without `.dev.vars`, then performs `wrangler deploy --dry-run`. The contract runs
real workerd/SQLite with synthetic upstream, including restart and hard budgets.
Neither is a real public HTTPS acceptance test or a Cloudflare deployment.

**Do not run the following before direct user approval.** After reviewing the
frozen receipt, confirming Workers Free in the dashboard, stopping this Twitch
app's local use, and confirming the non-commercial usage terms, execute in order:

```powershell
$review = '.artifacts/igdb-production-ready-20261018/predeployment-review.json'
node services/igdb-game-videos/scripts/operate-approved.mjs bootstrap --user-approved --free-plan-confirmed --review=$review
node services/igdb-game-videos/scripts/operate-approved.mjs secrets --user-approved --free-plan-confirmed --local-use-stopped --review=$review
node services/igdb-game-videos/scripts/operate-approved.mjs enable --user-approved --free-plan-confirmed --local-use-stopped --review=$review
```

Flags never substitute for human authorization. The source/configuration hashes
must match the receipt. Bootstrap deploys **disabled** and verifies the actual
workers.dev hostname. The Secret step privately reads only the two developer
fields from the authorized `.dev.vars` and pipes JSON to `wrangler secret bulk`;
no value is in an argument, ordinary variable, log or desktop artifact. The
production bindings are **Cloudflare Secrets**, not a published `.dev.vars`.
The reviewed credential-free stage is also used for real deployment. Raw CLI
output is suppressed, including on failure.

After enabling, test the **actual HTTPS endpoint** from the native provider:
title candidate, exact original/remaster IDs, Steam edition, repeated query/cache,
concurrent duplicate, sanitized error and kill/re-enable. Then run the isolated
WinUI candidate against that HTTPS address, play two samples, return, restart and
check saved decisions. Record actual Cloudflare resource/plan metrics. Until then
zero-configuration client wiring is ready, but public end-to-end is **pending**.
Ordinary users will not configure a key, Twitch login, address or Wrangler.

### Disable and rollback

```powershell
node services/igdb-game-videos/scripts/operate-approved.mjs disable --user-approved --review=$review
```

The same reviewed deploy defaults to `IGDB_ENABLED=false`, even if a dashboard
variable was previously true. There is no `keep_vars` override. The edge returns
an unavailable state without calling the DO. Do not delete the Worker, namespace,
SQLite data or migration, and do not revert to the earlier service without the
public limits. Disable first, then select a separately reviewed prior Worker
version if required. Persisted budgets/dispatch barriers remain. The native
client can fall back to the previously retained candidate without migrating the
library; identity decisions are separate user data and no schema changes occur.
No daily installation is replaced or public release created by this task.
