# Desktop app (WinUI3)

`src/AiAutomations.Desktop` + `src/AiAutomations.Desktop.Core`. A WinUI3/MSIX Windows app that
runs the same Slack-listening/classification/task-tracking pipeline as the Server, in-process,
with its own local SQLite database and its own Slack Socket Mode connection - no dependency on
the Server at runtime. Runs minimized to the system tray; meant to just be left open.

For how to actually build/run/redeploy it, see [DESKTOP_DEPLOYMENT.md](DESKTOP_DEPLOYMENT.md) -
this file is about what it does and how it's put together, not how to launch it.

## Composition root

`App.xaml.cs`: builds an `IHost` from `AddDesktopHost(configuration)` (loads `settings.json` via
`DesktopConfiguration.Load()`), runs `AppDbContext.EnsureCreated()` +
`DatabaseSchemaUpgrader.ApplyAsync()` + `DefaultRepositorySeed`, starts the host, then creates
`MainWindow`, `TrayIconService`, and `ToastNotificationService` in that order (strict ordering
matters for `AppNotificationManager` - see the comment in `App.xaml.cs`). Single-instance guard
via `AppInstance.FindOrRegisterForKey`.

`TriageAppService` (`Desktop.Core`) is the in-process equivalent of the Server's REST controllers
- same EF queries and `IActionExecutor`/`IRealtimeNotifier` calls, just called as plain methods
instead of over HTTP. It's a singleton that resolves a scoped `AppDbContext`/service per call via
`IServiceScopeFactory`, since the DbContext itself is scoped.

## Pages

- **Dashboard** (`DashboardPage`, the app's home page) - overview: a "N items need your review"
  banner linking to Inbox when anything's pending, stat tiles (Pending / Processing now / New in
  24h / Active tasks / AI calls), a recent-activity feed, a live health card (Database/Slack/AI
  provider/OpenProject dots, same `HealthStatusDto` the title-bar health strip uses), tasks-by-status
  breakdown, AI-usage-by-model breakdown. Refreshes on a timer plus realtime notifier events.
- **Inbox** (`InboxPage`) - the pending-approval card list (`ActionCardControl` per item) plus a
  collapsed-by-default "Catch up on past messages" backfill control (`Expander`). Deliberately
  kept action-focused, not a dashboard - see CLAUDE.md's standing decisions.
- **Tasks** (`TasksPage`) - a Kanban board over `WorkItem`s (Todo/In progress/In review/Done/
  Cancelled), drag-and-drop between columns (see "Kanban drag-and-drop" below), manual add form.
  A card synced from OpenProject shows a "View in OpenProject" link and, if its project couldn't
  be confidently matched to a registered repository, an inline "needs a repository" picker (see
  "OpenProject sync" below).
- **History** (`HistoryPage`) - every decision ever made, including ones with no derived action.
  Every row with a source Slack message gets a "View in Slack" button.
- **Settings** (`SettingsPage`) - Connections (Slack/AI credentials, editable without hand-editing
  `settings.json`, export/import), OpenProject (base URL/token/sync interval/status filter/
  auto-assign confidence threshold, plus Test connection and Sync now buttons), Theme
  (Light/Dark/Auto), Backup & restore (database), the repository registry (add/edit/remove).

## OpenProject sync

See [ARCHITECTURE.md](../ARCHITECTURE.md)'s "OpenProject sync (Desktop only)" section for the full
design. In short: `OpenProjectSyncHostedService` periodically pulls work packages assigned to the
configured account into `WorkItem`s; pushing a comment or `% complete` back is a separate, explicit
per-task action (buttons on `WorkItemDetailDialog`, visible only when the task is linked), never
automatic. Repository assignment is AI-confidence-triaged, not blind string matching - an
unconfident match surfaces as an inline picker on the Tasks board card instead of guessing.
- **About** (`AboutPage`) - app icon/version, contact, local data path.

## Inbox approval flow

Each `ActionCardControl` offers three ways to approve, plus a fourth to decline-with-feedback:

- **Approve** (default) - sends/creates immediately with whatever's currently in the card.
- **Refine and approve** (flyout on the Approve split button) - asks the AI to polish the draft
  (grounded in repo context for Ticket/TaskBrief) and approves immediately with the refined text,
  no manual review step.
- **Check first** (flyout) - opens `ActionReviewDialog`, a pre-filled editable copy of the card's
  fields, with its own Approve/Discard.
- **Reply** (hidden for `ActionType.Reply` items, since Approve already *is* the reply there) -
  declines the item but, via `DeclineFeedbackDialog`, drafts an editable, approval-gated message
  explaining why, posted to the originating thread only if explicitly confirmed.

After any approval path succeeds, the card also offers (never auto-sends) a drafted
acknowledgment reply on the originating thread via `AcknowledgmentDialog` - same "always explicit
approval" rule as everything else that posts to Slack.

## Health, activity, and AI usage tracking

- `IPipelineActivityTracker` (Infrastructure, singleton) - live count of messages currently
  buffered/being classified, incremented in `SlackIngestHostedService.OnMessageReceivedAsync`,
  decremented when `ClassifyAsync` finishes. Surfaced as `HealthStatusDto.PendingMessageCount`.
- `IAiUsageTracker` (Infrastructure, scoped) - records provider/model/purpose/token counts (and a
  best-effort estimated cost from a small hardcoded pricing table) for every AI API call, called
  from inside `AnthropicDecisionEngine`/`OpenAiCompatibleDecisionEngine` themselves. Surfaced via
  `TriageAppService.GetAiUsageSummaryAsync()` (last 24h, with a per-model breakdown).
- `HealthStripControl` (title bar, visible from every page) polls `GetHealthAsync()` every 15s
  plus on realtime events, shows DB/Slack/AI/OpenProject dots and a "Processing N" indicator, and
  fires a Windows toast (`ToastNotificationService.ShowHealthChangedToast`) on any actual status
  flip (not on every poll). The OpenProject dot reflects the result of the most recent sync
  attempt (`IOpenProjectGateway.IsConnected`), not a live ping on every poll - same pattern as the
  Slack dot. Click the strip for `HealthDetailsDialog` (fuller status + AI usage with per-model
  proportional bars).

## Theming

`ThemeManager` (Services) persists the choice in `ApplicationData.LocalSettings` and applies it
by setting `RequestedTheme` on the window's root element. The title bar has its own quick
Light/Dark toggle (see CLAUDE.md: Auto is Settings-only). See CLAUDE.md's "Patterns and gotchas"
section for the theming traps this app already fell into once (transparent backgrounds, native
caption buttons, `NavigationView`'s pane background).

## Kanban drag-and-drop

Each column is a `Border` (not just a `StackPanel`) with an explicit `MinHeight` tied to the
board's actual viewport height and a non-null `Background` - both needed because a `Panel` with a
null `Background` is click/drop-through outside its children's own bounds, so without this, only
dropping directly on a card or the column header worked. The column highlights (blue tint +
border) on `DragEnter`/resets on `DragLeave`/`Drop`. Dragging near the board's edges
auto-scrolls it (`TasksPage.OnBoardScrollViewerDragOver`).

## Tray icon and notifications

`TrayIconService` - left-click/double-click opens the window; right-click menu has direct links
to every page plus a Health dialog shortcut. `ToastNotificationService` shows a toast when an
action enters `PendingApproval`, when one `Failed`, on an automated (Slack-linked) work item
comment, on a health status change, and once per OpenProject sync run that created or failed to
sync an item (never one toast per item, and silent on an uneventful poll) - clicking any of them
deep-links back into the app (`MainWindow.ShowActionDetail`/`ShowWorkItemDetail`/`ShowHealthDetail`).

## Settings storage

`settings.json` lives under the packaged app's `LocalState` folder (`DesktopConfiguration`
resolves this via `Windows.Storage.ApplicationData`, falling back to a plain `%LocalAppData%`
folder when there's no package identity, e.g. an unpackaged dev/test run). Same section shape as
the Server's `appsettings.json` (`Slack:*`/`Ai:*`/`Anthropic:*`/`OpenAiCompatible:*`/`Claude:*`/
`OpenProject:*`) - though `OpenProject:*` is Desktop-only in practice, since Server has no
settings UI or configuration for it.
`DesktopSettingsEditor` reads/writes the actual JSON values (distinct from
`DesktopConfiguration.Load()`, which builds the read-only `IConfiguration` the host was composed
from at launch) so the Settings page can edit them without the user opening the file - a change
there only takes effect after a restart, which the page prompts for.
