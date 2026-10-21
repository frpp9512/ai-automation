# AI Automations — Slack triage assistant

A personal Slack triage assistant. It watches Slack for messages that need a reply, a task, or
an implementation, classifies them with an AI model, and tracks the resulting work so nothing
gets lost in a busy channel or DM.

Two ways to run it, sharing the same core logic:

- **Desktop app** (WinUI3/MSIX, Windows) - runs in the background from the system tray, own local
  database, own Slack connection. The primary way to use this day-to-day.
- **Server + web dashboard** (ASP.NET Core + Blazor) - for remote/browser access, or running in
  Docker on another machine.

> Run only one of these against the same Slack app at a time - Slack splits event delivery across
> whichever connections are open, so running both together silently divides messages between two
> separate databases.

## Features

- Classifies incoming Slack messages (DM, personal @mention, or a mention of a Slack user group
  you belong to) by intent and urgency, and drafts a reply, a tracked task, or a full
  implementation/planning brief depending on what's needed.
- Everything the AI drafts requires your explicit approval before it's sent or acted on - nothing
  auto-sends, nothing auto-converts.
- A self-hosted Kanban-style task board, a full history of every message ever triaged, a live
  health/status view, and (on Desktop) a dashboard overview of all of it.
- Catch-up backfill for messages that arrived before the assistant was running.
- Works with Anthropic's API or any OpenAI-compatible endpoint (OpenRouter, Groq, a local Ollama,
  etc.) behind a single config switch.

## Getting started

### Desktop app (Windows)

See [docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md) for how to build and run it (it's a
single-project MSIX app with a couple of non-obvious steps). Once running, open **Settings →
Connections** to enter your Slack and AI provider credentials - no need to hand-edit a config
file.

### Server + web dashboard

```bash
dotnet run --project src/AiAutomations.Server --urls http://localhost:5115
dotnet run --project src/AiAutomations.Client.Web --urls http://localhost:5200
```

Needs a matching `ApiKey` in both projects' config (see `appsettings.Development.json` for a
local-dev default) and your Slack/AI credentials via `dotnet user-secrets` - see
[ARCHITECTURE.md](ARCHITECTURE.md#storing-the-tokens).

### Docker

```bash
cp .env.example .env    # fill in your real tokens/keys - .env is gitignored, never commit it
docker compose up -d --build
docker compose logs -f server
```

### Setting up the Slack app

See [ARCHITECTURE.md](ARCHITECTURE.md#creating-the-slack-app) - `slack-app-manifest.yaml` has the
full scope/event config to paste in when creating the Slack app.

## Solution structure

```
src/
  AiAutomations.Core             Domain models + abstractions
  AiAutomations.Shared           Wire DTOs shared by every client
  AiAutomations.Infrastructure   EF Core, Slack connection, AI providers, the triage pipeline
  AiAutomations.Server           ASP.NET Core API + SignalR host
  AiAutomations.Client.Web       Blazor Server dashboard
  AiAutomations.Desktop.Core     In-process pipeline wrapper for the Desktop app
  AiAutomations.Desktop          WinUI3/MSIX Windows app
tests/
  AiAutomations.Tests            xUnit tests (dotnet test tests/AiAutomations.Tests)
```

## Documentation

- [CLAUDE.md](CLAUDE.md) - context for AI coding assistants working in this repo (also a good
  map for a human picking it back up after a while).
- [ARCHITECTURE.md](ARCHITECTURE.md) - design decisions, data flow, Slack app setup, Docker,
  known gaps.
- [docs/DESKTOP_APP.md](docs/DESKTOP_APP.md) - Desktop app feature tour.
- [docs/DESKTOP_DEPLOYMENT.md](docs/DESKTOP_DEPLOYMENT.md) - building/running/redeploying the
  Desktop app.

## License

Personal project, no license file yet - treat as all-rights-reserved unless/until one is added.
