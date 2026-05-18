# OmniChat

> A local-first, BYOK AI chat client. Desktop-first PWA + a thin .NET MAUI WebView shell for iOS / Android / macOS / Windows.

OmniChat unifies access to multiple LLM providers (OpenAI, Anthropic, Azure OpenAI, Groq, custom REST) while keeping chat history, documents, and embeddings on-device by default. The product positioning is **privacy-first**: no telemetry, BYOK keys encrypted at rest, RAG embeddings computed locally.

## Status

Active spec + roadmap lives at [`docs/ROADMAP.md`](docs/ROADMAP.md). All eleven milestones have shipped foundational code; remaining work is described inline as `TODO` comments where production hardening is still needed.

| Milestone | Status |
|-----------|--------|
| M1 — Foundation hardening (CI, analyzers, race fixes, optimistic UI) | ✅ |
| M2 — Real LLM streaming core (OpenAI/Anthropic/Azure/Groq/Echo, SSE) | ✅ |
| M3 — UI rebuild (design tokens, light mode, markdown sanitize, icons) | ✅ |
| M4 — Composer + messages (hover toolbar, regenerate, toast) | ✅ |
| M5 — Persistent storage (SQLite via EF Core 9) + ONNX embedding scaffold | ✅ |
| M6 — Sidebar overhaul (search, grouping, pinning, settings drawer) | ✅ |
| M7 — Search + docs + branching + persona + pricing + export | ✅ |
| M8 — Onboarding carousel + tool-call card + aurora drift | ✅ |
| M9 — MCP stdio client + voice (WebSpeech) + command palette | ✅ |
| M10 — Loopback guard + CORS policy + rate limiting | ✅ |
| M11 — .NET MAUI WebView shell + native bridges | ✅ (scaffold) |

## Architecture

```
OmniChat.slnx              # primary solution (no MAUI workload required)
OmniChat.Maui.slnx         # MAUI shell solution (requires `dotnet workload install maui`)

src/
  OmniChat.Core/           # domain library, providers, persistence, MCP, embeddings
    Data/                  # EF Core context + entities
    Mcp/                   # IMcpClient + stdio JSON-RPC client
    Models/                # ChatSession, ChatMessage (Id + ParentId for branching)
    Providers/             # IChatProvider + OpenAI/Anthropic/Azure/Groq/Echo adapters
    Security/              # IKeyVault interface + fingerprint helper
    Services/              # repos, embeddings, doc extractors, search, personas, pricing
  OmniChat.Web/            # ASP.NET Core 9 Minimal API host + wwwroot/
    Security/              # DataProtectionKeyVault (cross-platform encrypted keys)
    wwwroot/
      icons/sprite.svg     # Lucide-style icons + OmniChat sigil (one sprite)
      styles/              # tokens.css → base.css → components.css → layout.css
      js/                  # state, api, render, keymap, app, native (ES modules)
      index.html           # HTML skeleton only — see DESIGN_SYSTEM.md
  OmniChat.Maui/           # MAUI WebView shell + SecureStorage / Share / FilePicker bridges
tests/
  OmniChat.Core.Tests/     # xUnit unit + provider parser tests
docs/
  DESIGN_SYSTEM.md         # token contract, accent rules, motion principle
  PRODUCT_OWNER_REVIEW.md  # readiness assessment
```

## Run

### Web (default — what you want during development)

```bash
dotnet build OmniChat.slnx --warnaserror
dotnet test  OmniChat.slnx
dotnet run   --project src/OmniChat.Web/OmniChat.Web.csproj
```

The app listens on `http://127.0.0.1:5078`. By default it refuses non-loopback binds — see [`appsettings.json`](src/OmniChat.Web/appsettings.json) for `OmniChat:AllowRemote`.

### Storage

Two modes, set via `OmniChat:Storage`:

- `sqlite` (default) — persists to `~/Library/Application Support/OmniChat/omnichat.db` (macOS), `%LOCALAPPDATA%\OmniChat\omnichat.db` (Windows), `~/.local/share/OmniChat/omnichat.db` (Linux).
- `memory` — process-only. Used in tests and screenshot automation.

### ONNX embeddings

The first time you do a RAG retrieve, OmniChat looks for `all-MiniLM-L6-v2.onnx` in `<data dir>/models/`. If the file is missing, a deterministic hash-based fallback embedding is used so the pipeline keeps working. Real on-device embedding ships when the model is present — see [`src/OmniChat.Core/Services/OnnxEmbeddingService.cs`](src/OmniChat.Core/Services/OnnxEmbeddingService.cs) and [`ModelDownloader.cs`](src/OmniChat.Core/Services/ModelDownloader.cs).

### MAUI shell

The MAUI project lives in [`src/OmniChat.Maui/`](src/OmniChat.Maui/) and targets `net9.0-android`, `net9.0-ios`, `net9.0-maccatalyst`, and (on Windows hosts) `net9.0-windows10.0.19041.0`. The shell is a thin `WebView` host with five JS bridges: `secureStorage`, `share`, `pickFile`, `haptic`, `biometric`. The web side feature-detects via [`wwwroot/js/native.js`](src/OmniChat.Web/wwwroot/js/native.js) and falls back to standard web APIs.

```bash
dotnet workload install maui
dotnet build OmniChat.Maui.slnx
```

The current shell renders a placeholder page; embedded-Kestrel hosting is the next M11 sub-task (`TODO(M11b)` in [`MainPage.xaml.cs`](src/OmniChat.Maui/MainPage.xaml.cs)).

## Providers

Real adapters are implemented for OpenAI, Anthropic, Azure OpenAI, and Groq. A built-in **Echo** provider streams a deterministic placeholder token-by-token so the UX is testable without any API key.

Keys are sealed via ASP.NET Data Protection (DPAPI on Windows, Keychain-backed on macOS, libsecret on Linux); the UI only ever shows a fingerprint (`sha256:a1b2c3…`), never the raw key.

## UI

See [`docs/DESIGN_SYSTEM.md`](docs/DESIGN_SYSTEM.md) for the token contract, accent rules (mint/amber/violet have one job each), markdown sanitize pipeline, and motion principle. Keyboard shortcuts: `⌘+Enter` to send, `⌘+K` to focus the composer, `⌘+/` to search sessions, `⌘+Shift+K` for the command palette, `⌘+Shift+L` to toggle theme, `⌘+Shift+M` for voice input.

## Tests

```bash
dotnet test OmniChat.slnx
```

CI runs build + tests on every push and PR via [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

UI screenshots:

```bash
python -m pip install playwright && python -m playwright install chromium
python scripts/capture_ui_screenshots.py
```

Outputs land in [`docs/screenshots/`](docs/screenshots/).

## License

TBD.
