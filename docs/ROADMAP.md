# OmniChat — Spec & Roadmap

> **Provenance**: This document was authored as a plan in Claude Code's plan mode after a full codebase audit, then approved by the product owner before implementation. It is the single source of truth for the M1–M11 build-out — all sixteen feature commits on `claude/omnichat-roadmap-m1-m11` trace back to a section here.
>
> Keep this file in sync when scope changes. When a milestone ships, append a note (or convert the section to past-tense + a link to the merge commit).

## Context

User asked for: (1) full understanding of *why* OmniChat exists, (2) areas to improve, (3) UI/UX enhancements via the `frontend-design` skill, (4) performance + feature upgrades. This document is the consolidated audit and a prioritized roadmap.

---

## 1. What OmniChat actually is (vision vs reality)

**Vision (from [README.md](../README.md))**: a premium cross-platform mobile app — `.NET MAUI`, Telerik/Syncfusion UI, on-device SQLite + `sqlite-vec`, ONNX embeddings, multi-provider routing (OpenAI/Anthropic/Azure/Vertex/Bedrock/Groq), MCP tool integration, SSE streaming, RAG over PDF/DOCX/TXT, animated onboarding ("Your Data, Your Device" → "BYOK" → "Local Superpowers").

**Reality (at audit time)**:
- .NET 9 ASP.NET Core Minimal API + **one** 717-line vanilla-JS HTML page ([src/OmniChat.Web/wwwroot/index.html](../src/OmniChat.Web/wwwroot/index.html))
- **No MAUI project**. No native mobile shell.
- **No real LLM call** — assistant reply is `$"You said: {request.Content}..."` string template at Program.cs:77
- **`ProviderType` enum exists, zero adapters** ([ProviderType.cs](../src/OmniChat.Core/Models/ProviderType.cs), [ProviderConfig.cs](../src/OmniChat.Core/Models/ProviderConfig.cs))
- **No persistence** — `ConcurrentDictionary` only. Restart = data gone.
- **Embedding is 26-dim letter histogram** — RAG ranking essentially random
- **Web search returns 3 hardcoded `example.local` rows** regardless of query
- **`SseStreamParser` exists, unused by any endpoint**
- **No auth, no CORS policy, no rate limit, no CI workflow**
- **6 tests, 1 file**
- 11 total commits, all 2026-05-18 — solo/very small team

**Diagnosis**: vision-heavy, code-light. Demo-quality baseline shipped behind aspirational README copy. [docs/PRODUCT_OWNER_REVIEW.md](PRODUCT_OWNER_REVIEW.md) already lists most gaps.

---

## 2. Top correctness, perf & security findings

| # | Severity | Finding |
|---|---|---|
| 1 | High | LLM call is a string template — no provider integration exists |
| 2 | High | `GetSessionAsync` returns live mutable `List<ChatMessage>` while writers `lock`-on-add → enumerate-during-write race when serializing |
| 3 | High | `AppRuntimeSettings` mutable singleton, set from HTTP handlers without sync — torn reads |
| 4 | High | Frontend `refreshAll()` does 4 round trips + full `innerHTML` re-render per send |
| 5 | High | `/messages` calls `TokenBudgetManager.FitToBudget` twice per request |
| 6 | High | `/api/rag/retrieve` re-indexes full document content from request body each call — trivial DoS |
| 7 | High | Embedding is letter-frequency — retrieval correctness is broken |
| 8 | Med | Web search is stub — pollutes stored assistant messages |
| 9 | Med | No CI, no analyzers, no `TreatWarningsAsErrors` |
| 10 | Med | No auth/CORS/rate-limit on any endpoint |
| 11 | Med | No input-size guards on RAG endpoints (Kestrel default ~28MB) |
| 12 | Low | `ChatMessage` has no ID — blocks edit/regen/branching |

---

## 3. UI/UX state & redesign direction (frontend-design)

### Current UI reality (pre-M3)
- Single file, vanilla HTML+CSS+JS, no framework, no build step, no design tokens beyond 9 CSS vars
- Dark-only, no light mode, no `prefers-reduced-motion`, no icons (zero SVG)
- No markdown rendering, no code blocks, no syntax highlighting
- No model picker — just a `<input type="text">`
- No avatars, no message actions, no streaming indicator
- Sidebar's second card is "RAG & Storage Controls" — implementation chrome occupies prime real estate
- "AI generic dark admin dashboard" aesthetic — every card same border/radius/shadow, no surface hierarchy
- One breakpoint (`max-width: 1024px`) — desktop-first

### Redesign direction — *"A quiet command line for AI"*
Distinguishes from Claude (warm/soft), ChatGPT (neutral), T3 (playful), OpenWebUI (utilitarian). Linear-restraint + Raycast-density + thin cryptographic/circuit motif that **earns** the privacy claim.

**Foundation (M3):**
- **File split**: keep `index.html` as the only HTML, split CSS+JS into `wwwroot/styles/{tokens,base,components,layout}.css` and `wwwroot/js/{app,state,render,api,keymap,native}.js`
- **Color**: keep the existing palette as base. Add surface scale (`--surface-0..3`), three accent jobs: `--mint` = local/secure/streaming, `--amber` = BYOK/cost, `--violet` = RAG/citations
- **Light mode** via CSS `light-dark()` + persisted toggle
- **Type**: Inter (UI) + monospace stack (code, model IDs, token meter, key fingerprint)
- **Icons**: Lucide subset as one SVG sprite + custom **OmniChat sigil** (chevron-bracket "O") for brand mark
- **Texture signature**: keep radial-gradient backdrop, add slow-drifting aurora (paused on `prefers-reduced-motion`)
- **Motion principle**: *motion confirms; never decorates*. Three tokens: `--ease-out-soft`, `--ease-spring`, `--ease-stream` (linear, for token reveal only)
- **Markdown**: `marked` + `DOMPurify.sanitize` — **never raw `innerHTML`** for content

### Surfaces ranked for `frontend-design` skill use (highest ROI first)

1. **Onboarding carousel** — the 3-panel "Your Data, Your Device / BYOK / Local Superpowers" pitch. Where personality is forged.
2. **Empty chat state** — sigil + "Ask anything. Stays local." + 4 suggested-prompt chips
3. **Settings → Providers tab** — visual provider rack with status dots; key fingerprint never raw key
4. **Message bubble hover toolbar** — frosted-bg row of icon buttons: copy, regenerate, edit, branch
5. **Streaming/tool-call inline card** — live tool execution collapses to a citation pill on completion

Skip frontend-design for plain buttons/inputs/grid — those stay disciplined token-driven CSS.

### Information architecture changes
- **Remove** RAG & storage controls from sidebar — move to Settings page
- **Sidebar** top→bottom: search + new-chat button → pinned + folder tree → sessions grouped by Today/Yesterday/This Week/Older → provider/key status row + settings cog + theme toggle
- **Composer**: textarea / model picker + provider chip + send. Keys: `Cmd+K` focus, `Cmd+Enter` send, `Cmd+Shift+K` palette
- **Message bubble**: role label, markdown w/ sanitize, fenced code, hover toolbar, assistant model badge
- **Settings drawer** with tabs: RAG & Storage, Export

---

## 4. Feature gap matrix vs ChatGPT / Claude / T3.chat / OpenWebUI

| Feature | Pre-audit status | Effort |
|---|---|---|
| Real LLM call (any provider) | Missing | M |
| Token streaming | Missing | S (parser exists) |
| Multi-provider routing | Missing | M |
| Persistent storage | Missing | M |
| BYOK key vault | Missing | S |
| Conversation search | Missing | S (after SQLite) |
| Folders/pinning/tagging | Missing | S (schema only) |
| File upload + extract (PDF/DOCX) | Missing | M |
| Branching/regenerate | Missing | S (after `Message.ParentId`) |
| System prompt / persona library | Missing | XS |
| Token & cost tracking | Partial (estimator only) | XS |
| RAG over docs | Partial (fake embeddings) | M |
| Real web search | Stub | S (BYOK Brave/Serper) |
| MCP client | Missing | M |
| Vision / multimodal | Missing | S (passthrough) |
| Image generation | Missing | XS (passthrough) |
| Voice in / TTS out | Missing | S (browser WebSpeech) |
| Slash commands / Cmd-K palette | Missing | XS |
| Export (md/JSON/share) | Missing | XS |
| Memory / long-term context | Missing | M |
| Light mode | Missing | XS |
| Markdown + code blocks | Missing | XS |
| Keyboard shortcuts beyond Cmd+Enter | Missing | XS |
| CI workflow | Missing | XS |
| Mobile (MAUI per README) | Missing | XL |

**Effective implemented features (pre-audit)**: create session, append message (echo), word-stride chunking, cosine retrieval over toy vectors, trim/clear sessions, 3 runtime knobs.

---

## 5. Mobile decision — MAUI WebView wrapper around web UI (LOCKED)

**Approach**: thin MAUI shell hosting the web UI in `WebView` w/ native bridges. NOT a XAML rebuild.

Rationale:
- One UI codebase (web) drives desktop PWA + iOS + Android
- Native shell unlocks: `Microsoft.Maui.Storage.SecureStorage` for BYOK keys (Keychain on iOS, Keystore on Android), haptics, share sheet for export, file pickers, biometric unlock
- App-store presence without doubling design/dev surface
- README pitch ("native-feeling controls") preserved at meaningful cost (~1-2 wks for shell vs ~XL for full XAML rebuild)

**MAUI shell scope** (M11):
- `OmniChat.Maui/` project — net9.0-android + net9.0-ios + net9.0-maccatalyst + net9.0-windows
- Single `MainPage.xaml` with `WebView`
- Bridge over intercepted `omnichat://<bridge>/<method>?<json>` URLs + injected `window.omnichat.*` API:
  - `secureStorage.{get,set,delete}(key)` → `Microsoft.Maui.Storage.SecureStorage`
  - `share(data)` → native share sheet
  - `pickFile(types)` → `FilePicker.PickAsync`
  - `haptic(kind)` → `HapticFeedback`
  - `biometric.unlock()` → platform biometric APIs
- Web UI feature-detects `window.omnichat` and uses it when present; falls back to `localStorage` / `navigator.share` / file input in plain browser

**README action**: rewritten to position OmniChat as "Desktop-first PWA + native MAUI WebView shell for iOS/Android/macOS/Windows app-store distribution."

---

## 6. Milestones (each shippable)

### M1 — Foundation hardening (2–4 days)
- Add `.github/workflows/ci.yml` — `dotnet build -warnaserror` + `dotnet test`
- Add `.editorconfig` + analyzers + `TreatWarningsAsErrors` via `Directory.Build.props`
- Fix `InMemoryChatRepository` race: return deep snapshot of `Messages`, expose as `IReadOnlyList`
- Convert `AppRuntimeSettings` to immutable record swapped via `Interlocked.CompareExchange`
- Drop duplicate `FitToBudget` call
- Change `/messages` response to `{ userMessage, assistantMessage }`; client optimistic-append, kill `refreshAll()` on send
- Add `[RequestSizeLimit]` + content-length caps on `/api/rag/index` and `/api/rag/retrieve`
- Cap `Query` to 1k chars

### M2 — Real LLM streaming core (~1.5 weeks)
- Define `IChatProvider` in `OmniChat.Core/Providers/` w/ `IAsyncEnumerable<ChatChunk> StreamAsync(...)`
- Adapters: `OpenAiChatProvider`, `AnthropicChatProvider`, `AzureOpenAiChatProvider`, `GroqChatProvider`, `EchoChatProvider` (offline default) — typed `HttpClient` + `HttpCompletionOption.ResponseHeadersRead` + shared `SseHttpStreamReader`
- New endpoint `POST /api/sessions/{id}/stream` returning `text/event-stream` (manual writer)
- BYOK key storage: `IDataProtectionProvider` (cross-platform) backed by `LocalApplicationData/OmniChat/keys/`
- `Microsoft.AspNetCore.RateLimiting` token-bucket per session
- Frontend: consume SSE via `fetch` + `ReadableStream`, append deltas incrementally; streaming cursor (mint vertical bar)

### M3 — UI foundation rebuild + design tokens (~1 week, parallelizable with M2)
- Split `index.html` into HTML skeleton + `wwwroot/styles/*.css` + `wwwroot/js/*.js`
- Introduce token system, type scale, light mode toggle, Lucide sprite + sigil
- Vendor `marked`, `DOMPurify` (SRI deferred to M6 vendoring); pipe all message content through sanitize
- Add semantic landmarks, skip-link, `prefers-reduced-motion` global, focus-visible ring
- Add `docs/DESIGN_SYSTEM.md` as source of truth
- **Use `frontend-design` skill** for the new design tokens proposal + light-mode palette derivation

### M4 — Composer + messages overhaul (~1 week)
- Hover toolbar on every message bubble (copy + regenerate; edit/branch stubbed)
- Markdown render in bubbles via the sanitize pipeline
- Regenerate endpoint: removes target message + tail, re-streams from prior context
- Add `Id` + `ParentId` to `ChatMessage` so messages can be addressed
- Toast stack for non-blocking notifications

### M5 — Persistent storage + real embeddings (~2 weeks)
- EF Core 9 + `Microsoft.Data.Sqlite`, schema: `sessions / messages (w/ ParentId) / documents / chunks (embedding blob + dims) / provider_configs / personas / folders`
- Storage mode selectable via `OmniChat:Storage=sqlite|memory`
- Replace `LocalEmbeddingService` w/ ONNX scaffold: `Microsoft.ML.OnnxRuntime` lazily loading `all-MiniLM-L6-v2.onnx` (384-dim, 23MB)
- `CompositeEmbeddingService` prefers ONNX when ready, falls back to letter-frequency
- Model NOT committed; `ModelDownloader` w/ SHA-256 manifest does first-run download

### M6 — Sidebar + sessions + settings + provider UI (~1.5 weeks)
- Sidebar redesign: search (Cmd+/), Pinned / Today / Yesterday / This Week / Older grouping, right-click pin toggle
- Backend: `PATCH /api/sessions/{id}` (rename/pin/folder), `GET/POST/DELETE /api/providers` (returns key-presence only, never raw key + fingerprint)
- Settings drawer w/ RAG/storage controls, theme toggle, export buttons
- **Use `frontend-design` skill** for: Settings → Providers tab (the "visual provider rack")

### M7 — Feature wave A: search + docs + branching (~2 weeks)
- Hybrid search: LIKE scan now; FTS5 virtual table at M7b
- File upload + extract: `PdfPig` (PDF), `DocumentFormat.OpenXml` (DOCX), plain (code/md/txt) → chunk → embed → store
- Branching/regenerate (trivial w/ `ParentId`)
- Token & cost tracking (price-table per million tokens)
- System prompt / persona library
- Export (md / JSON) at `GET /api/sessions/{id}/export?format=md`

### M8 — Onboarding + motion polish (~1 week)
- First-run 3-panel carousel (Your Data, Your Device / BYOK / Local Superpowers)
- All empty states + toast system + aurora drift
- Streaming token-batch reveal (opacity-in, never letter-by-letter)
- Tool-call inline cards
- **Use `frontend-design` skill** for: onboarding carousel (highest design ROI in the whole roadmap)

### M9 — Feature wave B: MCP + multimodal + voice (~2 weeks)
- MCP client (stdio transport, JSON-RPC 2.0). Library primitive; tool dispatch into providers in a follow-up
- Vision input — placeholder; provider passthrough lands in M9b
- Voice in (browser `SpeechRecognition`) — zero server work, consistent w/ local-first
- Cmd-K command palette w/ fuzzy filter + arrow nav + Enter to run

### M10 — Security + observability + a11y + perf polish (~1 week)
- Loopback guard — refuse non-127.0.0.1 binds unless `OmniChat:AllowRemote=true`
- Explicit CORS policy via `OmniChat:AllowedOrigins`
- Serilog JSON sink + key/prompt redaction (M10b)
- OpenTelemetry traces around `IChatProvider.StreamAsync` (M10b)
- axe-core a11y pass, WCAG AA contrast, virtualized message list (~80 LOC, `IntersectionObserver`)
- Regenerate `docs/screenshots/` w/ light + dark variants

### M11 — MAUI WebView shell + native bridges (~1-2 weeks)
- New `src/OmniChat.Maui/` project, multi-target net9.0-android/ios/maccatalyst/windows
- `MainPage.xaml` hosts `WebView`; intercepts `omnichat://<bridge>/<method>` URLs and routes to BridgeRouter
- `OmniChat.Maui/Bridges/`:
  - `SecureStorageBridge.cs` → `Microsoft.Maui.Storage.SecureStorage`
  - `ShareBridge.cs` → native share sheet
  - `FilePickerBridge.cs` → `FilePicker.PickAsync` for PDF/DOCX upload
  - `HapticBridge.cs` → `HapticFeedback`
  - `BiometricBridge.cs` → platform biometric APIs (stub until Plugin.Fingerprint bundled)
- Web side: `wwwroot/js/native.js` feature-detects `window.omnichat`, falls back to web APIs on plain browser
- Separate `OmniChat.Maui.slnx` so default CI does not require the MAUI workload
- Embedded Kestrel hosting lands at M11b — until then the shell shows a placeholder page

---

## 7. Files added by the M1–M11 build-out

**Repo root**: `.github/workflows/ci.yml`, `.editorconfig`, `Directory.Build.props`, `OmniChat.Maui.slnx`

**Backend** (`src/OmniChat.Core/`): `Providers/{IChatProvider,ChatChunk,ChatStreamRequest,SseHttpStreamReader,ProviderException,ProviderErrorHelper,ChatProviderRegistry,IChatProviderRegistry,{OpenAi,Anthropic,AzureOpenAi,Groq,Echo}ChatProvider}.cs`, `Security/{IKeyVault,KeyFingerprint}.cs`, `Data/OmniChatDbContext.cs`, `Data/Entities/*.cs`, `Mcp/{IMcpClient,StdioMcpClient}.cs`, `Services/{IEmbeddingService,OnnxEmbeddingService,CompositeEmbeddingService,ModelDownloader,IDocumentExtractor,PlainTextExtractor,PdfDocumentExtractor,DocxDocumentExtractor,DocumentExtractorRegistry,DocumentService,HybridSearchService,ProviderPricing,SessionExporter,IProviderConfigStore,InMemoryProviderConfigStore,SqliteProviderConfigStore,IPersonaService,InMemoryPersonaService,SqlitePersonaService,SqliteChatRepository}.cs`

**Web** (`src/OmniChat.Web/`): `Security/DataProtectionKeyVault.cs`

**Frontend** (`src/OmniChat.Web/wwwroot/`): `styles/{tokens,base,components,layout}.css`, `js/{app,state,api,render,keymap,native}.js`, `icons/sprite.svg`

**MAUI** (`src/OmniChat.Maui/`): full project — `OmniChat.Maui.csproj`, `App.xaml(.cs)`, `MainPage.xaml(.cs)`, `MauiProgram.cs`, `Bridges/{BridgeRouter,SecureStorageBridge,ShareBridge,FilePickerBridge,HapticBridge,BiometricBridge}.cs`, `Platforms/{Android,iOS,MacCatalyst,Windows}/...`

**Docs**: `docs/DESIGN_SYSTEM.md`, `docs/ROADMAP.md` (this file)

---

## 8. Files modified by the M1–M11 build-out

- [src/OmniChat.Web/Program.cs](../src/OmniChat.Web/Program.cs) — endpoints, DI, response shapes, streaming, rate limit, CORS, loopback guard
- [src/OmniChat.Web/wwwroot/index.html](../src/OmniChat.Web/wwwroot/index.html) — shrink to skeleton + script tags + semantic landmarks
- [src/OmniChat.Core/Services/InMemoryChatRepository.cs](../src/OmniChat.Core/Services/InMemoryChatRepository.cs) — race fix; runs alongside `SqliteChatRepository`
- [src/OmniChat.Core/Models/ChatSession.cs](../src/OmniChat.Core/Models/ChatSession.cs) — lock-guarded snapshot pattern + `Pinned` + `FolderId`
- [src/OmniChat.Core/Models/ChatMessage.cs](../src/OmniChat.Core/Models/ChatMessage.cs) — `Id`, `ParentId`, `TokenCount`, `CostMicros`
- [src/OmniChat.Core/Models/ProviderConfig.cs](../src/OmniChat.Core/Models/ProviderConfig.cs) — encrypted key field, base URL, system prompt, temperature, max-tokens
- [src/OmniChat.Core/Services/LocalEmbeddingService.cs](../src/OmniChat.Core/Services/LocalEmbeddingService.cs) — implements `IEmbeddingService`; remains as fallback for `CompositeEmbeddingService`
- [README.md](../README.md) — reframe MAUI as WebView shell, document PWA-first
- [.github/copilot-instructions.md](../.github/copilot-instructions.md) — codify split-file convention so future contributors don't re-inline
- [scripts/capture_ui_screenshots.py](../scripts/capture_ui_screenshots.py) — updated selectors for the M6 settings drawer
- [tests/OmniChat.Core.Tests/CoreServicesTests.cs](../tests/OmniChat.Core.Tests/CoreServicesTests.cs) — provider parsers + SSE reader + fingerprint + snapshot tests

---

## 9. Quick-win bundle (ship in week 1)

If only one thing ships: do M1 + the M3 markdown/sanitize/light-mode/icons foundation + start the M2 OpenAI adapter. That alone takes the product from "echo demo" to "real chat client with personality" before any of the heavier work.

---

## 10. Risks & trade-offs

- **Vanilla JS state scaling**: `state.js` works through M4. By M6 (settings forms + optimistic updates + provider validation) consider `@preact/signals-core` (1.7KB, no JSX, drops in as ES module). Threshold: if `state.js` exceeds ~150 LOC or causes 2+ cross-surface coordination bugs, switch.
- **Markdown XSS**: non-negotiable — every rendered message content must go through `DOMPurify.sanitize(marked(content))`. Document the one exception (sanitized markdown) explicitly in `DESIGN_SYSTEM.md`; everywhere else use `textContent` per `.github/copilot-instructions.md`.
- **Vendor CDN coupling**: pin vendor JS into `wwwroot/vendor/` w/ SRI hashes, don't pull from CDN at runtime (deferred to M6/M10).
- **ONNX model size (23MB)**: never commit. First-run downloader w/ explicit user prompt preserves the privacy posture and keeps repo lean.
- **Schema migration**: EF Core migrations from day 1; the in-memory → SQLite cutover at M5 is one-time. After that, all schema changes get migrations.
- **"One file" UI tradition**: the user explicitly valued single-file. Compromise: `index.html` stays the only HTML file (no Razor partials); only CSS/JS split. That preserves "open one file and see structure" while unblocking real engineering.

---

## 11. Verification plan

After each milestone:
- `dotnet build OmniChat.slnx --warnaserror` → 0 warnings
- `dotnet test OmniChat.slnx` → all tests pass (add new ones per milestone)
- `python scripts/capture_ui_screenshots.py` → regenerated screenshots in `docs/screenshots/`
- CI pipeline green on PR
- Local smoke test sequence:
  1. `dotnet run --project src/OmniChat.Web` → app starts on localhost
  2. Create session, send message → real LLM reply streams in (post-M2)
  3. Upload PDF → chunks indexed → retrieval cites it (post-M5/M7)
  4. Add provider key → fingerprint shows, raw key never visible (post-M6)
  5. Toggle theme → no FOUC, motion respects `prefers-reduced-motion` (post-M3/M8)
  6. Lighthouse desktop + mobile → 95+ accessibility, 90+ perf (post-M10)
- Manual axe-core sweep at M10

---

## 12. Locked decisions

- **Scope**: full M1–M11 (~12-14 weeks solo)
- **Mobile**: MAUI WebView wrapper (M11) — native shell, web UI inside
- **Frontend-design surfaces (4 locked)**: onboarding carousel, empty chat state, Settings → Providers rack, streaming/tool-call inline card

## 13. Execution order (suggested)

```
Week 1-2:   M1  (foundation hardening)
Week 2-4:   M2  (real LLM streaming)            // parallel w/ M3
Week 2-3:   M3  (UI tokens + markdown + light)
Week 4-5:   M4  (composer + messages)
Week 5-7:   M5  (SQLite + ONNX embeddings)
Week 7-9:   M6  (sidebar + settings + providers)
Week 9-11:  M7  (search + docs + branching)
Week 11-12: M8  (onboarding + motion polish)    // frontend-design heavy
Week 12-14: M9  (MCP + multimodal + voice)
Week 14:    M10 (security + a11y + perf)
Week 14-16: M11 (MAUI WebView shell)
```

Critical path: M1 → M2 → M5 → M11 (everything else parallelizable around it).

---

## 14. Implementation provenance

This plan was implemented on branch `claude/omnichat-roadmap-m1-m11` across 16 commits:

| Commit | What it lands |
|---|---|
| `chore: add CI workflow, editorconfig, analyzer baseline` | M1 tooling |
| `fix: thread-safe session snapshot + repository abstractions` | M1 race fix |
| `feat: provider abstraction with OpenAI/Anthropic/Azure/Groq/Echo adapters` | M2 core |
| `feat: provider config store + persona service (in-memory and SQLite)` | M2/M5 stores |
| `feat: EF Core 9 SQLite persistence layer` | M5 schema |
| `feat: embedding abstraction with ONNX skeleton + model downloader` | M5 embeddings |
| `feat: document extraction + ingestion service` | M7 docs |
| `feat: hybrid search, provider pricing, session exporter` | M7 search/pricing/export |
| `feat: MCP stdio client (JSON-RPC 2.0)` | M9 MCP |
| `feat: data protection key vault (cross-platform BYOK at rest)` | M2 key vault impl |
| `feat: wire backend services + SSE streaming + middleware` | M2/M6/M7/M10 endpoints |
| `feat: design system (tokens, styles, icons, js helpers)` | M3 foundation |
| `feat: UI shell — semantic index.html + app.js orchestration` | M3/M4/M6/M8/M9 frontend |
| `feat: .NET MAUI WebView shell + native bridges` | M11 mobile |
| `test: provider parsers + SSE reader + fingerprint + snapshot isolation` | M1/M2 tests |
| `docs: README rewrite + copilot guidance + screenshot script update` | M10 docs |

Open follow-ups carried in code as TODO comments:
- **M5b**: real BERT tokenize + ONNX run inside `OnnxEmbeddingService`
- **M7b**: SQLite FTS5 virtual table replacing the LIKE scan
- **M10b**: Serilog JSON sink + OpenTelemetry traces
- **M11b**: embedded Kestrel host inside the MAUI shell
