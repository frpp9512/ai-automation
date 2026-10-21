# CLAUDE.md

Context for AI assistants (Claude Code or otherwise) working in this repo. Read this first;
it links out to deeper docs rather than repeating them. Keep this file current whenever you
make a change that would otherwise surprise the next session — stale context here costs more
tokens later than updating it now.

## What this is

A personal Slack triage assistant. It watches Slack for messages that need a reply, a task, or
an implementation, classifies them with an AI model, and tracks the resulting work. Three
surfaces share the same core logic:

- **Server + Client.Web** — ASP.NET Core API/SignalR server + a Blazor Server dashboard, for
  remote/browser access. See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design.
- **Desktop** — a WinUI3/MSIX Windows app that runs the same pipeline in-process (own Slack
  connection, own SQLite DB), for day-to-day local use from the system tray. See
  [docs/DESKTOP_APP.md](docs/DESKTOP_APP.md) for its features and [docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md)
  for how to build/run/deploy it.

**Only one of Server or Desktop should have a live Slack Socket Mode connection at a time** —
Slack round-robins events across open connections for the same app, so both running together
silently splits message delivery between two separate SQLite databases. Desktop is the
user's primary day-to-day runtime; Server is for remote access.

## Solution layout

```
AiAutomations.slnx
src/
  AiAutomations.Core/            Domain models + enums, abstractions (ISlackGateway,
                                  IAiDecisionEngine, IMessageIngestPipeline, IAiUsageTracker,
                                  IPipelineActivityTracker, etc.). No framework dependencies.
  AiAutomations.Shared/          Wire DTOs + mapping extensions used by Server, Client.Web,
                                  and Desktop.Core alike.
  AiAutomations.Infrastructure/  EF Core (SQLite), SlackGateway (real SlackNet socket + search +
                                  resiliency), AnthropicDecisionEngine/OpenAiCompatibleDecisionEngine,
                                  MessageIngestPipeline, MessageAggregationBuffer (debounces AI
                                  classification across a burst of messages), ActionExecutor,
                                  DatabaseSchemaUpgrader (see "No EF migrations" below).
  AiAutomations.Server/          ASP.NET Core host: REST + SignalR, hosted services.
  AiAutomations.Client.Web/      Blazor Server dashboard (custom CSS, no Bootstrap).
  AiAutomations.Desktop.Core/    TriageAppService (in-process equivalent of the Server's
                                  controllers), DesktopRealtimeNotifier, DesktopConfiguration
                                  (settings.json load), DesktopSettingsEditor (read/write it
                                  from the UI).
  AiAutomations.Desktop/         WinUI3/MSIX app - tray icon, toast notifications, Dashboard/
                                  Inbox/Tasks/History/Settings/About pages. See docs/DESKTOP_APP.md.
tests/
  AiAutomations.Tests/           xUnit + EF Core InMemory. Run after any Core/Infrastructure/
                                  Shared change: dotnet test tests/AiAutomations.Tests/AiAutomations.Tests.csproj
```

Dependency direction: `Infrastructure → Core`, `Server → Core + Infrastructure + Shared`,
`Client.Web → Shared` only, `Desktop.Core → Core + Infrastructure + Shared`, `Desktop → Desktop.Core + Shared`.

## Build/test commands

```bash
# Core/Shared/Infrastructure/Server/Client.Web - plain dotnet build
dotnet build src/AiAutomations.Infrastructure/AiAutomations.Infrastructure.csproj
dotnet build src/AiAutomations.Server/AiAutomations.Server.csproj
dotnet build src/AiAutomations.Client.Web/AiAutomations.Client.Web.csproj

# Desktop (WinUI3) - ALWAYS pass -p:Platform=x64 (it targets x86/x64/ARM64, no default)
dotnet build src/AiAutomations.Desktop/AiAutomations.Desktop.csproj -p:Platform=x64

# Tests - run after any Core/Infrastructure/Shared change
dotnet test tests/AiAutomations.Tests/AiAutomations.Tests.csproj
```

Running/deploying the Desktop app is its own topic — see
[docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md) before touching it. Short version:
`dotnet build` alone does **not** produce something you can launch; it needs either Visual
Studio's own deploy, or the CLI `dotnet publish -p:RuntimeIdentifier=win-x64` + `Add-AppxPackage
-Register` recipe in that doc.

## Standing product decisions (don't "fix" these without asking)

These came from explicit user direction, not oversight — changing them silently would undo a
deliberate choice:

- **Nothing auto-sends.** Every Slack reply/acknowledgment the AI drafts (approval replies,
  decline-with-feedback, post-approval acknowledgments) requires explicit human review and an
  explicit "send" click. No code path should ever post to Slack without that.
- **Nothing auto-converts.** A drafted task/ticket/brief never promotes itself to approved or
  executed on its own, "even when confidence is high" - always a manual approve.
- **Desktop's title-bar theme toggle is Light/Dark only.** "Auto" (follow system) is a
  Settings-page-only choice, not offered in the quick toggle.
- **Desktop's Inbox page is action-focused, not a dashboard.** Overview/stats content (health,
  recent activity, AI usage, tasks-by-status) belongs on the separate Dashboard page, which is
  the app's home page; Inbox stays just the pending-approval card list + a collapsed/secondary
  backfill control.
- **A Failed action stays visible in the Inbox** (not just PendingApproval) until it actually
  succeeds - see `TriageAppService.GetPendingActionsAsync`. Don't narrow that filter back to
  PendingApproval only.

## Patterns and gotchas worth not re-discovering

- **Never surface a raw Slack user ID to a human or feed one to the AI.** Every `SlackMessage`
  has `SlackUserName` (resolved at ingest, nullable) alongside `SlackUserId`; use
  `SlackMessageDto.DisplayName` (falls back to the ID only when no name resolved) in the UI, and
  the equivalent `DisplayLabel` helper in `ClassificationPrompt`/`MessageIngestPipeline` when
  building any text that either a human reads or the AI model sees. The AI echoes back whatever
  identifies the sender in its prompt verbatim, so if the prompt has a raw ID, the generated
  summary/task details will too - fix it at the prompt, not just in the UI layer.
- **WinUI3 theming needs explicit brushes, not `Transparent`, almost everywhere "transparent"
  looks tempting.** This app has no `SystemBackdrop` (no Mica/Acrylic) configured, so an element
  with `Background="Transparent"` (custom title bar, `NavigationView`'s default pane background)
  just reveals whatever's underneath, which doesn't track light/dark theme and often renders
  dark regardless of the active theme. Use a `{ThemeResource ...}` brush that's actually meant to
  be opaque (`ApplicationPageBackgroundThemeBrush`, `SolidBackgroundFillColorBase`, etc.).
  `NavigationView` doesn't expose `PaneBackground` as a settable XAML property on this version -
  override it via `<NavigationView.Resources><SolidColorBrush x:Key="NavigationViewDefaultPaneBackground" .../></NavigationView.Resources>` instead.
- **The native title-bar caption buttons (minimize/maximize/close) are OS chrome, not XAML.**
  `ThemeResource` never reaches them. `MainWindow.ApplyTitleBarButtonColors` re-colors them by
  hand on startup and on every theme change - if you add another place theme can change, call it.
- **A plain `<Image Source="Assets/Foo.png">` can silently fail to render** even when the file is
  genuinely present and correctly copied (happened on the About page; the title bar's own icon
  with the same pattern worked fine). If an `Image` renders blank with no error, set its `Source`
  explicitly in code via `new BitmapImage(new Uri("ms-appx:///Assets/Foo.png"))` instead of
  trusting the bare relative XAML string.
- **MSIX visual assets must be explicit `<Content>` items, not left to MSIX-tooling
  auto-discovery.** `dotnet publish -p:RuntimeIdentifier=win-x64` (the CLI deploy path - see
  docs/DESKTOP_DEPLOYMENT.md) never runs the real MSIX packaging payload step, so
  `Square44x44Logo.png` etc. (referenced only from `Package.appxmanifest`) silently never get
  copied unless the `.csproj` also has `<Content Include="Assets\*.png"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`.
- **No EF Core migrations** - the schema is still evolving, so both Server and Desktop call
  `AppDbContext.EnsureCreated()` (creates from scratch only, never alters). New columns/tables on
  an *existing* database go through `DatabaseSchemaUpgrader` (`AiAutomations.Infrastructure/Data/`)
  instead - an additive-only, idempotent, hand-rolled SQL patcher (`PRAGMA table_info`/
  `sqlite_master` guards) called right after `EnsureCreated()` in both `App.xaml.cs` (Desktop) and
  `Program.cs` (Server). Add a new guarded block there for any new column/table, never assume
  `EnsureCreated()` alone handles an upgrade.
- **Database backup/restore uses SQLite's own primitives, not a plain file copy**: `VACUUM INTO`
  for backup (safe even mid-write, never a torn snapshot) and `SqliteConnection.ClearAllPools()`
  before overwriting the live file for restore (releases pooled connections holding the file
  open). See `TriageAppService.BackupDatabaseAsync`/`RestoreDatabase`.
- **AI usage/cost tracking lives inside the decision-engine implementations themselves**
  (`AnthropicDecisionEngine`/`OpenAiCompatibleDecisionEngine` call `IAiUsageTracker.RecordAsync`
  after every API response that includes a usage block). Any new code path that calls an AI
  provider directly (rather than through `IAiDecisionEngine.ClassifyAsync`/`GenerateTextAsync`)
  won't show up in the health dashboard's AI usage numbers unless it also records usage.
- **Slack replies sometimes need the user token, not the bot token.** A DM between the human
  user and someone else (captured via backfill search, not a bot-scoped event) is a conversation
  the bot was never invited to - posting as the bot fails with `channel_not_found` even though
  the channel ID is correct. `SlackGateway.SendReplyAsync` falls back to the user token with
  `AsUser = true` on that specific error; the user token needs its own `chat:write` scope for the
  fallback to actually work (see `ARCHITECTURE.md`'s Slack app setup section).
- **Message-burst debouncing**: a live message is persisted immediately (so it shows up in
  history right away) but AI classification is debounced per-conversation via
  `IMessageAggregationBuffer` - a few messages sent in quick succession cost one classification
  call (against the last message, whose stored context already covers the earlier ones), not one
  per message. See `SlackIngestHostedService`/`MessageAggregationBuffer`.

## Secrets

Never commit real Slack tokens, AI API keys, or the dashboard's own `ApiKey`. Server/Client.Web
use `dotnet user-secrets` locally and environment variables in Docker (`.env`, gitignored - copy
`.env.example`). Desktop stores them in a hand-edited (or now, Settings-page-edited)
`settings.json` under its packaged `LocalState` folder, never in the repo. If you ever see a real
token while working in this repo, flag it rather than echoing it back in full.

## Where to look next

- [README.md](README.md) - human-oriented overview and quick start.
- [ARCHITECTURE.md](ARCHITECTURE.md) - Server/Client.Web/pipeline design, decisions, data flow,
  Slack app setup, known gaps.
- [docs/DESKTOP_APP.md](docs/DESKTOP_APP.md) - Desktop app feature tour and internal design.
- [docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md) - how to actually build, run, and
  redeploy the Desktop app (CLI recipe plus the traps that make it non-obvious).
