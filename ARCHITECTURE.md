# AI Automations — Slack triage assistant

> This document covers the Server + Client.Web architecture and the shared pipeline
> (Core/Infrastructure/Shared), plus Slack app setup and running history. For the WinUI3
> **Desktop app** (now the primary day-to-day runtime - see [CLAUDE.md](CLAUDE.md)), see
> [docs/DESKTOP_APP.md](docs/DESKTOP_APP.md) and [docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md).

Listens for Slack DMs, personal @mentions, and mentions of Slack user groups
you belong to, classifies them with an AI model, and produces a reply draft,
a self-hosted task on the Tasks board, or a detailed task brief (with a
suggested repository) for implementation/planning work — all from a dashboard
that can be reached from more than one device. Low-confidence or incomplete
classifications are left for you to fill in rather than guessed at, and
anything the AI decides needs no action is still logged, never silently
dropped. Every message seen (live or backfilled) stays in a local log that's
reused as context for later messages, instead of re-fetching the same
conversation from Slack repeatedly.

## Decisions made so far

- **Runtime:** always-on local service (Socket Mode, no public URL required).
- **Reply mode:** every decision carries a confidence score; drafted for your
  approval by default, promotable to auto-send per rule as trust builds.
- **Implementation/planning trigger (`ActionType.TaskBrief`):** no Anthropic key
  was available this version, so this does *not* launch a real Claude Code
  session by default (`Claude:AutoLaunch` is `false`) - approving one just
  finalizes a detailed, ready-to-copy markdown brief (conversation context +
  task details + suggested repo) for you to hand to Claude yourself. The
  `IClaudeSessionLauncher`/`ClaudeCodeSessionLauncher` real-process-launch path
  still exists and works (see `ActionExecutor`) - flip `AutoLaunch` to `true`
  once a key exists and it starts launching `claude -p ...` for real, still
  never auto-pushing or merging on its own.
- **Repository routing:** a `RepositoryProfile` registry (name, path, keywords)
  you manage from the dashboard's Settings page. The classification prompt is
  given this list and asked to suggest the best match; genuinely unclear cases
  get `null` rather than a guess, left for you to pick in the UI.
- **Low-confidence / incomplete classifications are never silently forced into
  a "complete" task.** The prompt is told to use `null` + lower confidence
  rather than invent detail. The dashboard shows a "Needs review" badge below
  confidence 0.5, and the task brief / repository are always editable, not
  just pre-filled.
- **Nothing the AI triages just disappears.** `FyiOnly` (or any intent with no
  derived action) still gets fully persisted and is visible on the dashboard's
  History page - "discarded" means "no action created," not "deleted."
- **Mentions include Slack user groups, not just you personally.** `SlackGateway`
  fetches which `usergroups` you belong to (refreshed every 30 min) and treats
  a message mentioning any of them (`<!subteam^ID>`) the same as a direct
  `<@UserId>` mention - both live events and backfill search. Needs the
  `usergroups:read` user scope (added to the manifest; requires reinstalling
  the Slack app once per scope change - see "Creating the Slack app").
- **Conversation context is logged, not re-fetched.** Every message ingested
  (live or via backfill) stays in the `Messages` table even if it never needed
  its own action, and is reused as context for classifying later messages in
  the same thread/channel - Slack's API is only called when the local log
  doesn't already have enough. See `MessageIngestPipeline.GetConversationContextAsync`.
- **Self-hosted task tracking (`WorkItem`):** `TaskNeeded` messages (and manual
  entries) become tasks on a local Tasks board (Jira/OpenProject-style fields:
  status, priority, type, repo, labels, due date), not an external ticket -
  there's no Jira/OpenProject integration today. `ExternalSystem`/`ExternalId`/
  `ExternalUrl` are reserved, unused columns so a later sync integration is a
  matter of filling those in and writing a sync job, not redesigning the model.
- **Architecture:** server-client, so other clients can be added later.
- **Stack:** full .NET. Blazor Server is the first client since it shares
  the server's `Shared` contract types directly and fits an always-on,
  stateful backend naturally.
- **Transport:** REST for commands/queries, SignalR for server→client push
  (`InboxHub`). Chosen over tRPC/GraphQL/polling for being the idiomatic
  .NET answer to "REST + WebSocket" and because it isn't tied to TypeScript,
  leaving room for non-.NET clients later if needed.
- **Remote access:** clients may run off-machine, so the server requires an
  `X-Api-Key` header on every REST call and the SignalR handshake. That
  header check is not a substitute for network-level protection — put the
  server behind Tailscale/VPN, never expose it to the open internet directly.
- **AI provider:** `IAiDecisionEngine` has two implementations behind an
  `Ai:Provider` config switch - `AnthropicDecisionEngine` (direct Claude API)
  and `OpenAiCompatibleDecisionEngine` (any OpenAI-style `/chat/completions`
  endpoint: OpenRouter, Groq, xAI, a local Ollama, etc., just by changing
  `BaseUrl`/`Model`). Defaults to the latter, currently pointed at
  OpenRouter's free-model router, because a real Anthropic key wasn't
  available this session (needs billing set up at console.anthropic.com) -
  see "Choosing a free AI provider" below for the full story and how to
  switch back to Anthropic once a key exists.
- **UI:** no CSS framework - a small custom design system in `app.css`
  (CSS variables, `prefers-color-scheme: dark` support) replaces Bootstrap.
  Class names like `.card`/`.btn`/`.badge`/`.form-control` were kept so
  existing markup didn't need rewriting, but every color/spacing/radius value
  is custom, not Bootstrap's.
- **Keeping classification cheap:** the prompt sent to the AI model is kept
  deliberately small - the message text, each context line, and the repo
  keyword list are all length-capped (`ClassificationPrompt`), and only 6
  prior messages (not 10) are pulled as fallback context. This matters more
  for free-tier models (smaller context windows, rate limits) than for cost.

## Solution layout

```
AiAutomations.sln
src/
  AiAutomations.Core/            Domain models + enums (SlackMessage, MessageDecision incl.
                                  TaskDetails/SuggestedRepositoryName, ActionItem, ClaudeSession,
                                  AutoActionRule, RepositoryProfile, WorkItem incl. reserved
                                  ExternalSystem/ExternalId/ExternalUrl for future sync) and the
                                  interfaces Infrastructure implements: ISlackGateway (incl.
                                  IsConnected, GetRecentHistoryAsync), IAiDecisionEngine (takes the
                                  repo registry too), IClaudeSessionLauncher, IRealtimeNotifier
                                  (incl. WorkItemChangedAsync). No framework dependencies.
  AiAutomations.Shared/          Wire DTOs + HubEvents constants used by both the Server's
                                  API/hub and any client - incl. TaskBriefPayload, RepositoryProfileDto,
                                  HealthStatusDto, WorkItemDto/UpsertWorkItemRequest.
  AiAutomations.Infrastructure/  EF Core (SQLite) AppDbContext (Repositories, WorkItems tables),
                                  SlackGateway (real SlackNet Socket Mode connection +
                                  message.im/app_mention handling + paginated date-range search +
                                  GetRecentHistoryAsync fallback-context fetch + IsConnected +
                                  periodic Slack user-group membership refresh for group-mention
                                  detection), AnthropicDecisionEngine + OpenAiCompatibleDecisionEngine
                                  (both real; share prompt/parsing/length-capping via
                                  ClassificationPrompt), ClaudeCodeSessionLauncher (real,
                                  Process.Start against the `claude` CLI - only invoked when
                                  Claude:AutoLaunch=true), MessageIngestPipeline (classify + persist
                                  + derive actions + build the task brief + notify - the one path a
                                  message takes whether it arrived live or via backfill; also the
                                  local-first conversation-context cache/log).
  AiAutomations.Server/          ASP.NET Core host: REST controllers (Messages incl. backfill,
                                  Decisions, Actions, Sessions, Repositories, Health, WorkItems),
                                  InboxHub (SignalR), SlackIngestHostedService (bridges the
                                  gateway's events to the scoped pipeline), ClaudeSessionStatusHostedService
                                  (bridges the launcher's completion events back to the DB),
                                  ApiKeyMiddleware, wires it up in Program.cs (also seeds the repo
                                  registry on first run).
  AiAutomations.Client.Web/      Blazor Server dashboard, custom CSS (no Bootstrap) with dark mode.
                                  ServerApiClient (typed HttpClient) + RealtimeClient (one shared
                                  SignalR connection fanned out to all circuits via C# events) in
                                  Services/. Pages: Inbox (`/`, pending actions as editable cards -
                                  see ActionCard.razor), Tasks (`/tasks`, a self-hosted Kanban-style
                                  board over WorkItems - manual add + drag-free status-move dropdown),
                                  History (`/history`, every decision incl. ones with no action),
                                  Settings (`/settings`, repo registry CRUD). HealthStrip.razor polls
                                  `/api/health` + SignalR state for a small status row in the layout.
  AiAutomations.Desktop.Core/    In-process equivalent of the Server's controllers (TriageAppService)
                                  for the Desktop app - see docs/DESKTOP_APP.md.
  AiAutomations.Desktop/         WinUI3/MSIX desktop app - see docs/DESKTOP_APP.md and
                                  docs/DESKTOP_DEPLOYMENT.md.
tests/
  AiAutomations.Tests/           xUnit + EF Core InMemory. ActionExecutorTests covers the Reply/
                                  TaskBrief (both with and without AutoLaunch)/Ticket dispatch paths
                                  with hand-rolled fakes (Fakes/) for ISlackGateway,
                                  IClaudeSessionLauncher, IRealtimeNotifier.
```

Dependency direction: `Infrastructure → Core`, `Server → Core + Infrastructure + Shared`,
`Client.Web → Shared` only (it never touches Core/Infrastructure/EF directly — everything
goes through the Server's REST API and hub, which is the point of the server-client split).

## Data flow

**Live:** Slack event (`message.im`, a message mentioning `<@SelfUserId>` or `<!subteam^ID>` for
any user group the user belongs to, or `app_mention`) arrives → `SlackIngestHostedService` hands
it to `IMessageIngestPipeline.IngestAsync`.

**Backfill:** `POST /api/messages/backfill {From, To}` → `ISlackGateway.SearchMessagesInRangeAsync`
finds past messages sent to/mentioning the user via Slack's Search API → each result goes through
the *same* `IMessageIngestPipeline.IngestAsync` call as a live message, so catching up on messages
that arrived before the service was running produces the same decisions/actions it would have
produced live.

Either way, `IngestAsync`:
1. Skips the message if its (channel, ts) is already stored (unique index on `Messages`) -
   safe to re-run a backfill over an overlapping range, or to have backfill and live listening
   both see the same message.
2. Persists the `SlackMessage`, notifies clients (`MessageReceived`).
3. Gathers conversation context via `GetConversationContextAsync`: checks the local `Messages`
   log first (thread messages, or the last 6 in that channel/DM) and only calls Slack
   (`GetThreadContextAsync`/`GetRecentHistoryAsync`) when the log doesn't already have enough -
   whatever Slack returns is then folded into the log so the next message in that conversation
   doesn't need to re-fetch it. Either way, an un-threaded back-and-forth still gets classified
   with memory of what was already said, not in isolation.
4. Loads the `RepositoryProfile` registry and calls `IAiDecisionEngine.ClassifyAsync` (message +
   context + registry) → `MessageDecision` (`Intent`, `Urgency`, `Summary`, `SuggestedReply`,
   `TaskDetails`, `SuggestedRepositoryName`, `Confidence`). The prompt is told to return `null` +
   lower confidence rather than guess when it lacks enough detail. Persists it, notifies
   (`DecisionReady`).
5. Derives at most one `ActionItem` from the intent - `ReplyNeeded`→Reply, `TaskNeeded`→Ticket,
   `ImplementationNeeded`/`PlanningNeeded`→**TaskBrief** (looks up `SuggestedRepositoryName` in
   the loaded registry and, via `BuildBrief`, renders a full markdown brief - summary, urgency,
   repo, the conversation, and the task details - into `TaskBriefPayload.Brief`). `FyiOnly` (and
   anything else with no mapping) derives **no action** - the message and decision are still
   fully persisted and show up in the dashboard's History page, so nothing is silently lost, it
   just didn't need anyone to act on it. Any created action starts as `PendingApproval`, is
   persisted, and notifies (`ActionStatusChanged`).
6. `AutoActionRule`s (not yet evaluated anywhere — see Known gaps) are meant to decide whether
   an action can skip approval.
7. Approving an action (`POST /api/actions/{id}/approve`) flips it to `Approved`, notifies, then
   calls `IActionExecutor.ExecuteAsync`, which dispatches by `ActionItem.Type`:
   - **Reply** → `ISlackGateway.SendReplyAsync`, replying in the message's existing thread
     (`ThreadTs`) or starting one anchored on the original message (`SlackTs`) if there wasn't one.
   - **TaskBrief** → parses `Payload` as `TaskBriefPayload {RepoName, RepoPath, Prompt, Brief}`.
     With `Claude:AutoLaunch` false (the default, no Anthropic key needed) this is a no-op beyond
     marking the action `Executed` - the brief was already fully built at ingest time, and the
     dashboard lets you edit the repo and the brief text itself before approving (so an unclear
     or wrong AI guess is just overwritten, not blocking). With `AutoLaunch` true, it instead
     requires `RepoPath` and calls `IClaudeSessionLauncher.StartAsync`; the returned `ClaudeSession`
     is persisted and its `Running` status pushed immediately, and when the process later exits,
     `ClaudeCodeSessionLauncher` raises `OnSessionStatusChanged`, which `ClaudeSessionStatusHostedService`
     (its own DI scope, since this fires long after the original request ended) uses to update the
     DB row and notify clients with the final `Completed`/`Failed` status.
   - **Ticket** → creates a `WorkItem` (Title from `Payload`/`decision.Summary`, Description
     from `decision.TaskDetails`, Priority mapped from `decision.Urgency`, RepositoryName from
     `decision.SuggestedRepositoryName`, `SourceDecisionId` set for traceability) on the local
     Tasks board, status `Todo`. No external tracker involved - see "Self-hosted task tracking".
   Either way `ExecuteAsync` sets the action to `Executed` or `Failed` and notifies again.

The dashboard adds one more layer on top of confidence: any decision under 0.5 confidence shows
a "Needs review" badge on its card (`ActionCard.razor`), and the repo dropdown / brief text are
always editable regardless of confidence - low-confidence AI output is a starting point, not a
final answer.

## Creating the Slack app

[`slack-app-manifest.yaml`](slack-app-manifest.yaml) has the full scope/event/Socket Mode config.
Why it's shaped this way: catching mentions of the *human* user (not the app) and DMs *to* that
user requires seeing everything they can see, which only **user token events**
(`message.channels`/`groups`/`im`/`mpim`) deliver - `app_mention` only fires when the bot/app
itself is @-mentioned, so it's kept as a bot event just for completeness. `search:read` is
user-only too (bot tokens can't call `search.messages` at all), which is why backfill needs a
user token.

1. Go to <https://api.slack.com/apps> → **Create New App** → **From an app manifest** → pick the
   target workspace (confirmed: **XT Online Software**) → paste in `slack-app-manifest.yaml` →
   review → **Create**.
2. **Socket Mode** (left sidebar) → it should already show enabled from the manifest; generate an
   app-level token there (any name, scope `connections:write` is added automatically) → copy the
   `xapp-...` value.
3. **OAuth & Permissions** → **Install to Workspace**. This prompts for *both* the bot scopes and
   the user scopes from the manifest - approve both. After installing you'll see two tokens on
   that page: **Bot User OAuth Token** (`xoxb-...`) and, further down under "OAuth Tokens", your
   **User OAuth Token** (`xoxp-...`).
4. Invite the bot to any channels you want it watching mentions in (`/invite @ai-automations-triage`)
   - user-token events see channels *you're* in regardless, but inviting the bot too means its
     own token (the fallback path in `GetThreadContextAsync`) can also read there if the user
     token is ever absent.

Your Slack member ID (`Slack:SelfUserId`) is already set - confirmed as `U04PH9SU87K`
(XT Online Software workspace) and stored via `dotnet user-secrets`.

**If the app was created before `usergroups:read` was added to the manifest** (it was added
after group-mention detection was built), you'll need to re-paste the updated manifest content
into the app's **App Manifest** settings page (or recreate from manifest) and reinstall via
**OAuth & Permissions** again to grant the new scope - Slack requires a fresh install per new
scope, same as any other scope change. Until that's done, `SlackGateway` logs a clear
`missing_scope` warning on startup and simply skips group-mention detection (personal mentions
and DMs keep working normally).

**Caveat, unverified against a live workspace:** the backfill query uses `to:{SelfUserId}` for
DMs and `<@{SelfUserId}>` for mentions, both with `after:`/`before:` date filters. Slack's `to:`
search modifier is documented against `@username`, not a raw user ID - if it doesn't resolve,
switch it to the user's @handle in `SlackGateway.SearchMessagesInRangeAsync`.

### Storing the tokens

The Server project now has `dotnet user-secrets` initialized (`UserSecretsId` in its `.csproj`),
which keeps secrets out of `appsettings.json` and out of source control entirely. Once you have
the three tokens from steps 2-3 above, run these yourself (values never need to pass through
chat or get typed into a file Claude writes):

```bash
cd src/AiAutomations.Server
dotnet user-secrets set "Slack:AppLevelToken" "xapp-..."
dotnet user-secrets set "Slack:BotToken" "xoxb-..."
dotnet user-secrets set "Slack:UserToken" "xoxp-..."
```

## Self-hosted task tracking

`WorkItem` (Tasks page, `/tasks`) is a small Kanban-style board - Todo / In progress / In review
/ Done / Cancelled - with the field set most work trackers use (status, priority, type, repo,
labels, due date), populated two ways:
- **From Slack:** a `TaskNeeded` decision becomes a `Ticket` action; approving it creates a
  `WorkItem` via `ActionExecutor.ExecuteTicketAsync`, carrying over the decision's summary,
  task details, urgency→priority mapping, and suggested repository, plus `SourceDecisionId` for
  traceability back to the originating message.
- **Manually:** the Tasks page has its own "+ Add task" form for anything that didn't come
  through Slack at all.

**Designed for an easy future sync, not an actual one today.** `ExternalSystem`, `ExternalId`,
and `ExternalUrl` columns exist on `WorkItem` specifically so that wiring up Jira/OpenProject
later is "add a sync job that fills these in," not "redesign the schema" - there's no sync logic
or external API client yet, these columns are just reserved and currently always `null`.

The Blazor dashboard's board has no drag-and-drop; moving a card between columns is a `<select>`
per card (`PUT /api/workitems/{id}/status`), which keeps the UI simple and avoids JS-interop drag
libraries for what's a personal single-user tool. (The Desktop app's own Tasks board *does* have
drag-and-drop, native WinUI3 - see docs/DESKTOP_APP.md.)

**Layout bug found and fixed while building this:** the board's columns (5 × 260px, wider than
the viewport) blew out the whole page layout instead of scrolling within themselves - the browser
showed a correctly-populated DOM (confirmed via the accessibility tree and `get_page_text`) with
elements like the health strip and "+ Add task" button pushed thousands of pixels off-screen,
which only a direct `getBoundingClientRect()` check surfaced clearly. Classic flexbox cause: a
flex item (`main` in `MainLayout.razor.css`) without `min-width: 0` won't shrink below its
content's intrinsic width, so a wide child stretches its ancestor instead of scrolling inside it.
Worth remembering for any future wide/scrollable content area in this app.

## Choosing a free AI provider

A real Anthropic key wasn't obtainable this session (console.anthropic.com needs billing set up
first), so classification runs on `OpenAiCompatibleDecisionEngine` instead, pointed at
OpenRouter's free-model router. Getting there took several dead ends worth recording so they're
not repeated:

- **Groq** (console.groq.com) - the original recommendation, free with no credit card, but
  region-restricted and unavailable from this location.
- **"grok.com/dashboard/api-keys"** - this is xAI's *consumer* product (the Grok chat app), not
  the developer API. Its keys don't work against `api.x.ai` (the real xAI API host, which needs
  a *separate* signup at console.x.ai) - the API rejected it with "Incorrect API key provided."
- **GitHub Models** (`models.github.ai`) - looked right from search results and official-looking
  docs snippets, but the service was **fully retired on 2026-07-30**. The host now silently
  returns `200 OK` (plain text) for any request instead of a clear error, which looks like a
  misconfigured request until you check whether the product still exists at all.
- **OpenRouter** (openrouter.ai) - this one worked. Free via `openrouter/free`, which routes to
  whichever free model is currently available - meaning the backing model varies per request.
  One real issue this caused: a reasoning model can spend its entire token budget "thinking" and
  return `null` content with the actual answer only in a `reasoning` field. Fixed in
  `OpenAiCompatibleDecisionEngine` by falling back to `reasoning` when `content` is null, and by
  raising `MaxTokens` to 2048 so reasoning models have room to finish.

**Lesson for next time an API integration misbehaves unexpectedly:** test the raw HTTP call
directly (`Invoke-RestMethod`/`curl`) with the exact request shape the code sends, independent of
the app, before assuming the code is wrong - this caught the xAI key mismatch, the GitHub Models
retirement, and confirmed OpenRouter's response shape (including the reasoning-model edge case)
well before touching the running server each time.

**Switching back to Anthropic** once a real key exists: set `Ai:Provider` to `Anthropic` in
config and `dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."` - no code changes needed.

## Verified working

Built and smoke-tested end-to-end on 2026-10-20: solution builds clean, `AppDbContext.EnsureCreated()`
creates the SQLite schema, `/health` and the API-key-protected `/api/*` endpoints respond correctly
(401 without the key). `ActionExecutor`'s dispatch logic (Reply/TaskBrief with and without
AutoLaunch/Ticket, including the missing-payload and missing-RepoPath-under-AutoLaunch failure
paths) is covered by 7 passing xUnit tests against an EF Core InMemory database.

**Live-tested against a real workspace** (XT Online Software, user `U04PH9SU87K`), repeatedly
across this session:
- Socket Mode connects for real (`Slack Socket Mode connected`) and live DMs are captured,
  persisted, and classified via OpenRouter's free-model router.
- The repo registry was seeded with the user's actual 6 repositories (read from their READMEs,
  not guessed), and a real message ("the betslip component in player-ui needs a code fix...")
  was correctly matched to `player-ui`, with `RepoPath` pre-filled from the registry into a
  `TaskBrief` action - confirmed via `GET /api/actions` and in the dashboard. Overriding the
  suggested repo via the dropdown and approving persisted the override correctly.
- A vague message ("something's broken, can you look into it") classified at confidence 0.4,
  correctly triggering the dashboard's "Needs review" badge.
- A plain greeting classified as `FyiOnly` with no action created, and was confirmed visible on
  the History page - nothing vanished.
- The dashboard round-trips `GET /api/health` into the layout's status strip (Slack/DB/AI/Live,
  all green against the live setup), and both light and dark mode render correctly with the
  custom CSS (no Bootstrap).

**Group-mention scope confirmed live:** the Slack app was reinstalled with the updated manifest
(tokens didn't rotate - same Bot/User tokens as before, just the new scope granted), and the
server logged `Refreshed Slack user group memberships: 1 group(s)` on startup with no
`missing_scope` warning. Detection itself (an actual message mentioning that group) hasn't been
exercised yet - mentioning a user group notifies everyone in it, so this is left to happen
organically on the next real mention rather than triggering one just to test.

Still untested against real credentials: search-based backfill (pagination is implemented but
not exercised against a range with more than one page of results), and approving a TaskBrief
with `Claude:AutoLaunch=true` to launch a real headless session.

**Tasks board, live-tested same session:** created a manual task (title, High priority, repo
`player-ui`, description) via the "+ Add task" form, confirmed it rendered correctly in the Todo
column with the priority-colored accent border, and moved it to "In progress" via the status
dropdown - card moved columns and counts updated correctly. The Reply/TaskBrief/Ticket dispatch
paths (including Ticket now creating a real `WorkItem`) are covered by the 7 xUnit tests.

## Known gaps / next steps

- **AutoActionRule isn't evaluated anywhere** — rules exist as a model/DTO but nothing reads
  them yet to decide auto-approval.
- **EF migrations**: currently using `EnsureCreated()` for speed while the schema is unstable.
  Switch to `dotnet ef migrations add Initial -p src/AiAutomations.Infrastructure -s src/AiAutomations.Server`
  once the model settles.
- **Secrets**: all three Slack tokens and `OpenAiCompatible:ApiKey` (OpenRouter) are set via
  `dotnet user-secrets` and confirmed working live. `Anthropic:ApiKey` still isn't set - getting
  one needs a console.anthropic.com account with billing set up, which wasn't available this
  session; switch `Ai:Provider` back to `Anthropic` once it is (see "Choosing a free AI provider").
  `ApiKey` (the dashboard's own auth) also belongs in user-secrets for anything beyond local dev.
  `appsettings.json` keeps all of these blank and `appsettings.Development.json` has local-only
  dev placeholders — never commit real keys to either.
- **Every key used this session was pasted into chat** (Slack tokens, the xAI key, the OpenRouter
  key) because of the `dotnet user-secrets` environment mismatch below - treat all of them as
  exposed and rotate if that matters for this workspace/account.
- **Note on `dotnet user-secrets`**: when setting these, a command run in an external terminal
  reported success but didn't write to the `secrets.json` this project's tooling could see -
  likely two different views of the `X:` drive. Setting them by running `dotnet user-secrets set`
  through the same tooling that will run the server resolved it. If tokens seem "not configured"
  despite a successful-looking `set`, verify with `dotnet user-secrets list` from that same context.
- **Backfill has no progress UI** — `SearchMessagesInRangeAsync` now auto-paginates (capped at
  20 pages/query as a safety limit), but the dashboard shows only a final summary, no live
  progress while a large range is running.
- **No EF migration for the `Repositories`/`WorkItems` tables yet** - created via `EnsureCreated()`
  same as everything else; see the EF migrations gap above, same caveat applies.
- **No external work-tracker sync** — `WorkItem.ExternalSystem`/`ExternalId`/`ExternalUrl` are
  reserved columns, always `null` today. Building an actual Jira/OpenProject sync (push on
  create/update, or pull/reconcile) is unstarted.
- **Group-mention detection is scope-granted but not yet exercised** — `usergroups:read` was
  added and the app reinstalled; the server confirms 1 group membership on startup, but an actual
  mention of that group hasn't come through yet to verify end to end (see "Verified working").
- **Tasks board has no filtering/search** — all work items render in their column regardless of
  age; fine at low volume, would want a filter (by repo, by age, archive old `Done`/`Cancelled`
  items) if this fills up.

## Running it locally

```bash
dotnet run --project src/AiAutomations.Server --urls http://localhost:5115
dotnet run --project src/AiAutomations.Client.Web --urls http://localhost:5200
```

Both need matching `ApiKey` values (`appsettings.Development.json` already has `dev-local-key`
in both projects for local testing — replace before using real data).

## Running in Docker

Two containers (`server`, `client`) via `docker-compose.yml`, built from the repo root since
both Dockerfiles need to see the shared `Core`/`Shared`/`Infrastructure` projects they reference:

```bash
cp .env.example .env    # fill in your real tokens/keys - .env is gitignored, never commit it
docker compose up -d --build
docker compose logs -f server    # watch it connect to Slack, etc.
```

- **Secrets become environment variables**, not `dotnet user-secrets` (that's a local-dev-only
  mechanism that doesn't exist inside a container). The compose file maps `.env` values to the
  standard ASP.NET Core `Section__Key` env var convention (e.g. `Slack__BotToken`), which
  `appsettings.json` already defines the shape for - no code changes needed, config binding
  picks env vars up automatically and they take precedence over the (blank) `appsettings.json`
  defaults baked into the image.
- **`API_KEY`** is the one value both containers need (server validates it, client sends it) -
  `docker compose` will refuse to start without it set (`${API_KEY:?Set API_KEY in .env}`), to
  avoid accidentally running with the dashboard's own auth disabled.
- **The client reaches the server over the Docker network by service name** (`Server__BaseUrl:
  http://server:8080`), not `localhost` - the two containers aren't on the host's network
  namespace. Verified live: the dashboard's health strip and real-time connection both work
  end-to-end container-to-container.
- **SQLite persists on a named volume** (`server-data:/app/data`), separate from the container's
  own filesystem - confirmed live by restarting the server container and seeing it skip
  re-seeding the repository registry (data survived).
- **Ports exposed to the host:** `5115` (server REST/SignalR - same port as local dev, for
  convenience hitting the API directly) and `5200` (dashboard) - matches the local `dotnet run`
  ports so nothing else needs to change if you're used to those.
- **No HTTPS inside the container** - same reasoning as local dev (see the "Remote access"
  decision above): put a reverse proxy or Tailscale in front if this needs to be reached beyond
  `localhost`, rather than managing a cert inside the image.
- **`Claude:AutoLaunch`** still defaults to `false` here too - and even if enabled, the `claude`
  CLI isn't installed in the server image, so a real headless session wouldn't have anywhere to
  run from inside the container as-is. Repository paths in the registry (e.g. `X:\xt\player-ui`)
  are Windows host paths anyway, meaningless inside a Linux container - fine today since
  `TaskBrief` actions are manual-handoff only, but worth remembering if `AutoLaunch` is ever
  wired up for a containerized deployment.
