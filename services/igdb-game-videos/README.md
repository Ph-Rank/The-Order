# IGDB game video adapter

Independent, disabled-by-default application adapter. It does not change or deploy
the Bangumi OAuth Worker. It neither serves nor proxies video bytes.

## Developer access

Follow [IGDB's official setup](https://api-docs.igdb.com/#getting-started): a Twitch
developer account with 2FA and a **Confidential** application supplies the client
ID and secret. The app grant uses `client_credentials`, not an end-user login.
Check the Twitch developer agreement and IGDB's non-commercial/commercial usage
conditions before enabling a public service. This repository does not supply
credentials or claim an approved public deployment.

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
   Never start the candidate without those Profile flags. Production composition
   refuses HTTP loopback and accepts only a configured HTTPS service root.

Public deployment requires separate authorization. `workers_dev=false`, no route,
and `IGDB_ENABLED=false` intentionally prevent accidental exposure. A future
deployment needs its own secret bindings, an HTTPS service URL, usage/abuse review,
and actual API qualification. It must route every query for the same IGDB app to
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
  275 ms apart, with one upstream request active and at most eight queued callers.
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
