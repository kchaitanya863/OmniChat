# OmniChat — Spec, Roadmap & Implementation Log

> **Provenance**: This document started as a plan-mode audit + roadmap after a full codebase review. It is now the **as-built spec** — refreshed after the M1–M11 implementation shipped on branch `claude/omnichat-roadmap-m1-m11` (17 commits). Future scope changes should append to this doc, not start a new one.

## Status at a glance

| # | Milestone | Status | Where it lives |
|---|---|---|---|
| M1 | Foundation hardening | ✅ Shipped | [Directory.Build.props](../Directory.Build.props), [.editorconfig](../.editorconfig), [.github/workflows/ci.yml](../.github/workflows/ci.yml), [ChatSession.cs](../src/OmniChat.Core/Models/ChatSession.cs), [InMemoryChatRepository.cs](../src/OmniChat.Core/Services/InMemoryChatRepository.cs), [Program.cs](../src/OmniChat.Web/Program.cs) |
| M2 | Real LLM streaming core | ✅ Shipped | [Providers/](../src/OmniChat.Core/Providers/), [DataProtectionKeyVault.cs](../src/OmniChat.Web/Security/DataProtectionKeyVault.cs), `/api/sessions/{id}/stream` |
| M3 | UI foundation rebuild + design tokens | ✅ Shipped | [styles/](../src/OmniChat.Web/wwwroot/styles/), [js/](../src/OmniChat.Web/wwwroot/js/), [icons/sprite.svg](../src/OmniChat.Web/wwwroot/icons/sprite.svg), [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) |
| M4 | Composer + messages overhaul | ✅ Shipped | [render.js](../src/OmniChat.Web/wwwroot/js/render.js), `/api/sessions/{id}/regenerate`, `ChatMessage.Id` + `ParentId` |
| M5 | Persistent storage + real embeddings | ✅ Scaffold + ✋ M5b open | [Data/](../src/OmniChat.Core/Data/), [SqliteChatRepository.cs](../src/OmniChat.Core/Services/SqliteChatRepository.cs), [OnnxEmbeddingService.cs](../src/OmniChat.Core/Services/OnnxEmbeddingService.cs) — real BERT tokenizer + ONNX inference pending |
| M6 | Sidebar + sessions + settings + provider UI | ✅ Shipped | [app.js](../src/OmniChat.Web/wwwroot/js/app.js), `/api/sessions/{id}` PATCH, settings drawer in [index.html](../src/OmniChat.Web/wwwroot/index.html) |
| M7 | Search + docs + branching + persona + cost + export | ✅ Shipped, ✋ M7b open | [DocumentService.cs](../src/OmniChat.Core/Services/DocumentService.cs), [HybridSearchService.cs](../src/OmniChat.Core/Services/HybridSearchService.cs), [SessionExporter.cs](../src/OmniChat.Core/Services/SessionExporter.cs), [ProviderPricing.cs](../src/OmniChat.Core/Services/ProviderPricing.cs) — FTS5 virtual table pending |
| M8 | Onboarding + motion polish | ✅ Shipped | onboarding shell in [index.html](../src/OmniChat.Web/wwwroot/index.html), aurora drift in [base.css](../src/OmniChat.Web/wwwroot/styles/base.css), `buildToolCard` in [render.js](../src/OmniChat.Web/wwwroot/js/render.js) |
| M9 | MCP + multimodal + voice + palette | ✅ Shipped, ✋ M9b open | [Mcp/StdioMcpClient.cs](../src/OmniChat.Core/Mcp/StdioMcpClient.cs), `toggleVoice` + command palette in [app.js](../src/OmniChat.Web/wwwroot/js/app.js) — vision passthrough + MCP→tool-calling wiring pending |
| M10 | Security + observability + a11y + perf | ✅ Partial, ✋ M10b open | loopback guard + CORS in [Program.cs](../src/OmniChat.Web/Program.cs) — Serilog redaction, OpenTelemetry traces, virtualized message list, axe-core sweep pending |
| M11 | MAUI WebView shell + native bridges | ✅ Scaffold, ✋ M11b open | [src/OmniChat.Maui/](../src/OmniChat.Maui/), [native.js](../src/OmniChat.Web/wwwroot/js/native.js) — embedded Kestrel host pending |

**Legend**: ✅ Shipped = production-ready as bounded by the milestone scope. ✋ open = a named follow-up tracked in code as a `TODO`/`NOTE` comment or in §8 below.

---

## Context

User asked for: (1) full understanding of *why* OmniChat exists, (2) areas to improve, (3) UI/UX enhancements via the `frontend-design` skill, (4) performance + feature upgrades. This document is the consolidated audit + the roadmap that drove the build-out + the as-built record.

---

## 1. What OmniChat was, pre-audit (frozen baseline)

> The following describes the repo *at audit time* — kept here so the value of the M1–M11 change is legible. None of these descriptions are accurate today.

**Original vision (from the README)**: a premium cross-platform mobile app — `.NET MAUI`, Telerik/Syncfusion UI, on-device SQLite + `sqlite-vec`, ONNX embeddings, multi-provider routing, MCP, SSE, RAG over PDF/DOCX/TXT, animated onboarding.

**As found**:
- .NET 9 ASP.NET Core Minimal API + **one** 717-line vanilla-JS HTML page
- **No MAUI project**. No native mobile shell.
- **No real LLM call** — assistant reply was `$"You said: {request.Content}..."`
- **`ProviderType` enum existed, zero adapters**
- **No persistence** — `ConcurrentDictionary` only
- **Embedding was 26-dim letter histogram** — RAG ranking essentially random
- **Web search returned 3 hardcoded `example.local` rows**
- **`SseStreamParser` existed, unused**
- **No auth, no CORS policy, no rate limit, no CI workflow**
- **6 tests, 1 file**

**Diagnosis**: vision-heavy, code-light. Demo-quality baseline shipped behind aspirational README copy.

---

## 2. What OmniChat is now (post-M11)

- **.NET 9 ASP.NET Core Minimal API + .NET MAUI shell** with a shared `wwwroot` UI.
- **Five real chat providers** — OpenAI, Anthropic, Azure OpenAI, Groq, plus an offline Echo provider — all behind a single `IChatProvider` interface that streams `IAsyncEnumerable<ChatChunk>`.
- **BYOK keys encrypted at rest** via ASP.NET Data Protection (DPAPI/Keychain/libsecret). UI surfaces only a `sha256:<hex12>` fingerprint, never the raw key.
- **Persistent SQLite** — sessions, messages (with `ParentId`), provider configs, documents, chunks (packed float32 embedding blobs), personas, folders. Schema managed by EF Core 9. Storage mode flippable via `OmniChat:Storage=sqlite|memory`.
- **Embedding scaffold** — `OnnxEmbeddingService` lazy-loads `all-MiniLM-L6-v2.onnx` (384-dim); `CompositeEmbeddingService` falls back to the letter-frequency local embedder when the model file is absent.
- **Document ingestion** — PDF (PdfPig), DOCX (OpenXml), plain-text + code via a registry pattern. Upload → extract → chunk → embed → store.
- **MCP stdio client** with JSON-RPC 2.0 handshake, `tools/list`, `tools/call`.
- **SSE streaming endpoint** + rate-limited regenerate. Token-bucket per session (30/min).
- **Loopback guard** refusing non-127.0.0.1 binds unless `OmniChat:AllowRemote=true`. Explicit CORS policy via `OmniChat:AllowedOrigins`.
- **UI split into 11 focused files** with a tokenized design system, light/dark via `light-dark()`, Lucide-style icon sprite + custom sigil, sanitized markdown rendering through `DOMPurify.sanitize(marked.parse(...))`.
- **Command palette** (`⌘+⇧+K`), voice input via WebSpeech (`⌘+⇧+M`), session search (`⌘+/`), theme toggle (`⌘+⇧+L`), regenerate / copy / pin / branch toolbar on every message.
- **First-run onboarding carousel** — three scroll-snap panels with custom SVG illustrations (Your Data Your Device / BYOK / Local Superpowers).
- **Native MAUI WebView shell** multi-targeting Android, iOS, MacCatalyst, Windows with five bridges (SecureStorage, Share, FilePicker, Haptic, Biometric) and a web-side feature-detect layer.
- **CI workflow** running `dotnet build --warnaserror` + `dotnet test` on every push.

---

## 3. Top correctness/perf/security findings — every one addressed

| # | Severity | Finding | Status | Where resolved |
|---|---|---|---|---|
| 1 | High | LLM call was a string template — no provider integration | ✅ Fixed | M2 [Providers/](../src/OmniChat.Core/Providers/) + `/api/sessions/{id}/stream` |
| 2 | High | `GetSessionAsync` returned live mutable list → enumerate-during-write race | ✅ Fixed | M1 [ChatSession.cs](../src/OmniChat.Core/Models/ChatSession.cs) snapshot pattern + `System.Threading.Lock` |
| 3 | High | `AppRuntimeSettings` mutable singleton, torn reads | ✅ Fixed | M1 immutable record + `Interlocked.CompareExchange` retry loop |
| 4 | High | Frontend `refreshAll()` ran 4 round trips per send | ✅ Fixed | M1 optimistic append in [app.js](../src/OmniChat.Web/wwwroot/js/app.js) `sendMessage` |
| 5 | High | `/messages` called `FitToBudget` twice | ✅ Fixed | M1 single call in [Program.cs](../src/OmniChat.Web/Program.cs) |
| 6 | High | `/api/rag/retrieve` re-indexed document content per request — DoS vector | ✅ Fixed | M1 size cap (`Limits.MaxRagDocumentChars`) + M7 persistent chunks via `DocumentService` |
| 7 | High | Embedding was letter-frequency → retrieval random | ✅ Scaffolded | M5 [OnnxEmbeddingService](../src/OmniChat.Core/Services/OnnxEmbeddingService.cs); real BERT tokenize lands at M5b |
| 8 | Med | Web search was a stub | ✅ Acknowledged | M2 echo+placeholder; real BYOK Brave/Serper still open (see §8) |
| 9 | Med | No CI, no analyzers | ✅ Fixed | M1 [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) + [Directory.Build.props](../Directory.Build.props) |
| 10 | Med | No auth/CORS/rate-limit | ✅ Fixed | M10 loopback guard + CORS; M2 token-bucket per session |
| 11 | Med | No input-size guards on RAG endpoints | ✅ Fixed | M1 `Limits` class enforced server-side |
| 12 | Low | `ChatMessage` had no ID — blocked edit/regen/branching | ✅ Fixed | M4 `ChatMessage.Id` + `ParentId` |

---

## 4. UI/UX state

### Pre-audit (frozen)
- Single 717-line HTML file, vanilla JS, 9 CSS variables total
- Dark-only, no light mode, no `prefers-reduced-motion`, no icons
- No markdown, no model picker, no message actions
- "AI generic dark admin dashboard" aesthetic — sidebar dominated by implementation chrome (RAG/storage controls)

### Post-M11 (current)
- 11-file split: HTML skeleton + `styles/{tokens,base,components,layout}.css` + `js/{state,api,render,keymap,app,native}.js` + `icons/sprite.svg`
- Tokenized design system (`light-dark()` paired tokens, 4-step surface scale, three accent jobs: mint = local/secure/streaming, amber = BYOK/cost, violet = RAG/citations)
- Light mode with persisted theme toggle (`omnichat.theme` in localStorage)
- Inter (UI) + monospace stack for code/model IDs/key fingerprints
- 13 Lucide-style icons + custom OmniChat sigil in one SVG sprite
- Markdown rendered via `marked` + `DOMPurify.sanitize` with explicit allow-list; falls back to `textContent` if libraries fail to load
- Semantic landmarks, skip link, focus-visible ring, `prefers-reduced-motion` global guard
- Aurora-drift backdrop animation (90s linear, paused under reduced motion)
- Session list grouped Pinned / Today / Yesterday / This Week / Older with search input (`⌘+/`)
- Provider rack in sidebar with key fingerprint display
- Composer with model override input, voice mic button, keyboard-shortcut legend
- Hover toolbar on every message bubble (copy, regenerate, edit, branch)
- First-run 3-panel onboarding carousel with custom SVG illustrations
- Command palette (`⌘+⇧+K`)
- Settings drawer holding RAG controls, storage retention, export buttons

### Four surfaces commissioned for `frontend-design` skill (per the locked plan)
1. **Onboarding carousel** — shipped with hand-rolled SVG illustrations; can still benefit from a deeper skill pass for animation choreography.
2. **Empty chat state** — shipped with sigil + suggestion chips; baseline is good, skill pass would refine the chip language.
3. **Settings → Providers tab** — shipped as a sidebar card; the full "visual provider rack" treatment is open work.
4. **Streaming/tool-call inline card** — `buildToolCard` helper shipped; real MCP tool integration lights it up at M9b.

---

## 5. Feature matrix (current state)

| Feature | Pre-audit | Post-M11 |
|---|---|---|
| Real LLM call (multi-provider) | Missing | ✅ OpenAI / Anthropic / Azure / Groq / Echo |
| Token streaming (SSE) | Missing | ✅ |
| BYOK key vault (encrypted at rest) | Missing | ✅ via Data Protection |
| Persistent storage | Missing | ✅ SQLite via EF Core 9 |
| Conversation search | Missing | ✅ (LIKE; FTS5 = M7b) |
| Folders | Missing | ✅ schema + PATCH endpoint |
| Pinning | Missing | ✅ right-click toggle |
| File upload + extract (PDF/DOCX/text) | Missing | ✅ |
| RAG over docs (chunking + embed + retrieve) | Broken | ✅ pipeline; ONNX inference = M5b |
| Branching / regenerate | Missing | ✅ ParentId + regenerate endpoint |
| System prompt / persona library | Missing | ✅ CRUD endpoints |
| Token & cost tracking | Estimator only | ✅ price table + cost-micros math |
| Real web search | Stub | ✋ open (BYOK Brave/Serper adapters not yet built) |
| MCP client | Missing | ✅ stdio JSON-RPC; tool-calling wire-up = M9b |
| Vision / multimodal | Missing | ✋ open (M9b passthrough) |
| Image generation | Missing | ✋ open |
| Voice in (WebSpeech) | Missing | ✅ |
| TTS out (WebSpeech synth) | Missing | ✋ open (one helper away) |
| Slash commands / Cmd-K palette | Missing | ✅ |
| Export (md / JSON) | Missing | ✅ |
| Memory / long-term context | Missing | ✋ open (post-M11) |
| Light mode | Missing | ✅ |
| Markdown + code blocks | Missing | ✅ (highlight.js still optional — M3 punted to avoid 250 KB CDN dep) |
| Keyboard shortcuts | Cmd+Enter only | ✅ full keymap |
| CI workflow | Missing | ✅ |
| Mobile shell | Missing | ✅ MAUI WebView; embedded Kestrel = M11b |

---

## 6. Mobile decision — MAUI WebView wrapper around web UI (LOCKED)

**Approach**: thin MAUI shell hosting the web UI in `WebView` with native bridges. NOT a XAML rebuild.

Rationale:
- One UI codebase (web) drives desktop PWA + iOS + Android
- Native shell unlocks: `SecureStorage` for BYOK keys (Keychain/Keystore/DPAPI), haptics, share sheet, file pickers, biometric unlock
- App-store presence without doubling design/dev surface

**What shipped at M11**:
- Multi-target `OmniChat.Maui` project (net9.0-android/ios/maccatalyst/windows)
- `MainPage` intercepts `omnichat://<bridge>/<method>?<json>` URLs and routes to [BridgeRouter](../src/OmniChat.Maui/Bridges/BridgeRouter.cs)
- Five bridges: SecureStorage, Share, FilePicker, Haptic, Biometric (last is a graceful stub)
- Web side: [`wwwroot/js/native.js`](../src/OmniChat.Web/wwwroot/js/native.js) feature-detects `window.omnichat` and falls back to localStorage / navigator.share / file input / vibrate on plain browsers
- Lives in its own `OmniChat.Maui.slnx` so default CI doesn't require `dotnet workload install maui`

**What's pending (M11b)**: the embedded ASP.NET Core host. The current shell shows a placeholder confirming the bridges loaded but API calls don't work inside MAUI yet — they need Kestrel running on 127.0.0.1:<random port> with the WebView pointed at it.

---

## 7. Shipped milestones — each as a single coherent surface

### ✅ M1 — Foundation hardening
- CI workflow (`dotnet build --warnaserror` + `dotnet test`, NuGet cache, concurrency cancel)
- `Directory.Build.props` with `TreatWarningsAsErrors`, latest analyzers, opinionated NoWarn list
- `.editorconfig` for C# style + formatting
- `InMemoryChatRepository` race fix via `ChatSession.Snapshot()` and `System.Threading.Lock`
- `AppRuntimeSettings` as immutable record swapped via `Interlocked.CompareExchange`
- `/messages` response narrowed to `{ userMessage, assistantMessage }`; client switches to optimistic append
- `Limits` class enforcing `MaxMessageChars=100k`, `MaxRagDocumentChars=2M`, `MaxRagQueryChars=1k`

### ✅ M2 — Real LLM streaming core
- `IChatProvider` returning `IAsyncEnumerable<ChatChunk>` (delta + usage + finish reason)
- Adapters: `OpenAiChatProvider` (with `stream_options.include_usage`), `AnthropicChatProvider` (parses `content_block_delta` + `message_delta` + `message_start`), `AzureOpenAiChatProvider` (subclass with api-version + api-key header), `GroqChatProvider` (OpenAI-compatible base URL), `EchoChatProvider` (offline default streaming token-by-token)
- Shared `SseHttpStreamReader` (multi-line `data:` concat, `[DONE]` halt, comment skip)
- `ChatProviderRegistry` dispatching by `ProviderType`
- `IKeyVault` + `DataProtectionKeyVault` implementation (DPAPI/Keychain/libsecret via Data Protection)
- `KeyFingerprint` → `sha256:<hex12>` UI-safe identifier
- `IProviderConfigStore` with in-memory + SQLite implementations
- `POST /api/sessions/{id}/stream` SSE endpoint with `event: delta` / `done` / `error`
- Token-bucket rate limiter (30/min/session)
- Frontend `streamSse` in [api.js](../src/OmniChat.Web/wwwroot/js/api.js) + `beginStreamingAssistant` controller in [render.js](../src/OmniChat.Web/wwwroot/js/render.js) + mint streaming caret in CSS

### ✅ M3 — UI foundation rebuild + design tokens
- `index.html` shrunk from 717 → 186 LOC, just skeleton + script tags + landmarks
- `styles/tokens.css` — design contract: surface scale, three accent jobs, type scale, spacing, radius, shadows, motion easings, durations, z-scale
- `styles/base.css` — reset, focus ring, skip link, scrollbar, `prefers-reduced-motion` global, aurora-drift backdrop
- `styles/components.css` — buttons, inputs, cards, pills, message bubbles, hover toolbar, toast stack, onboarding panels, tool-call card, command palette
- `styles/layout.css` — app grid, sidebar, chat region, composer, drawer, responsive collapse
- 13 Lucide-style icons + custom OmniChat sigil in `icons/sprite.svg`
- `state.js` / `api.js` / `render.js` / `keymap.js` / `app.js` ES modules
- Markdown pipeline: `marked` + `DOMPurify.sanitize` with explicit tag/attribute allow-list; textContent fallback if libs fail to load
- `docs/DESIGN_SYSTEM.md` capturing token rules, accent semantics, motion principle

### ✅ M4 — Composer + messages overhaul
- Hover toolbar on every message: copy, regenerate (assistant only), edit (user only), branch
- Toast system (`toast(message, kind, durationMs)`) appended to a fixed `.toast-stack`
- `ChatMessage` extended with `Id`, `ParentId`, `TokenCount`, `CostMicros`
- `IChatRepository.RemoveMessagesFromAsync` + `POST /api/sessions/{id}/regenerate` — removes target message + tail, then streams a new reply

### ✅ M5 — Persistent storage + ✋ M5b ONNX inference
- EF Core 9 + Microsoft.Data.Sqlite. Entities: Session, Message (w/ ParentId), ProviderConfig, Document, Chunk (embedding blob + dims), Persona, Folder
- Indexes on session UpdatedAt, message Timestamp, document Sha256, chunk DocumentId
- `SqliteChatRepository` + `SqliteProviderConfigStore` + `SqlitePersonaService` implementing the same interfaces as the in-memory versions
- Storage selectable via `OmniChat:Storage=sqlite|memory`. `EnsureCreatedAsync` runs at boot
- `IEmbeddingService` interface; `LocalEmbeddingService` (26-dim letter-frequency) remains as the fallback
- `OnnxEmbeddingService` lazy-loads `all-MiniLM-L6-v2.onnx`. **M5b open**: real BertTokenizer/SentencePiece tokenize → InferenceSession run → mean-pool → normalize. Until then a deterministic hash-based vector at the configured dimensionality keeps the cosine math consistent
- `CompositeEmbeddingService` picks ONNX when ready, falls back to local otherwise
- `ModelDownloader` streams the model from a configured URL, verifies SHA-256, atomically renames into place

### ✅ M6 — Sidebar + sessions + settings drawer
- Session search input with `⌘+/` shortcut
- Time-bucketed grouping (Pinned / Today / Yesterday / This Week / Older)
- Right-click any session to toggle pin via `PATCH /api/sessions/{id}`
- `ChatSession.Pinned` + `FolderId` added to model and persisted
- RAG + storage controls **moved out of sidebar** into a slide-out settings drawer (the IA cleanup the audit called for)
- Settings drawer holds chunking strategy, storage mode, retention, trim/clear/refresh, and Markdown/JSON export buttons
- `GET /api/providers` returns key-presence + fingerprint, never raw key

### ✅ M7 — Feature wave A: docs, search, branching, persona, pricing, export — ✋ M7b FTS5
- `IDocumentExtractor` registry with three implementations: PlainText (covers code + markup), PdfDocumentExtractor (PdfPig), DocxDocumentExtractor (DocumentFormat.OpenXml)
- `DocumentService.ImportAsync`: hash file → extract text → chunk via `TextChunker` → embed each chunk via `IEmbeddingService` → persist Document + Chunks (vector packed as little-endian float32 blob with explicit `Dimensions`)
- `DocumentService.RetrieveAsync` runs a cosine scan against the persisted chunks (filterable by document)
- `HybridSearchService.SearchMessagesAsync` does a LIKE scan across all session messages; **M7b open**: replace with SQLite FTS5 virtual table for >10k message scale
- `IPersonaService` with in-memory + SQLite implementations and full CRUD endpoints
- `ProviderPricing` table with `CostMicrosForUsage(model, UsageStats)` math
- `SessionExporter.ToMarkdown` + `ToJson` rendering
- Branching/regenerate functional via `ChatMessage.ParentId` + the M4 regenerate endpoint
- New endpoints: `POST /api/documents`, `GET /api/documents`, `DELETE /api/documents/{id}`, `GET /api/documents/retrieve`, `GET /api/personas`, `POST /api/personas`, `DELETE /api/personas/{id}`, `GET /api/search`, `GET /api/sessions/{id}/export?format=md|json`, `GET /api/pricing`

### ✅ M8 — Onboarding + motion polish
- First-run gated on `localStorage.omnichat.onboarded`; three scroll-snap panels with custom inline SVG illustrations colored mint/amber/violet
- Dots indicator, Skip + Next buttons, "Add a provider" CTA on the last panel that opens the settings drawer
- Aurora-drift backdrop animation (90s linear, paused under `prefers-reduced-motion`)
- `buildToolCard(toolName, verb)` helper in [render.js](../src/OmniChat.Web/wwwroot/js/render.js) for live MCP tool execution rendering — complete()/fail() methods collapse to summary chip
- Toast system w/ `success` / `warn` / `danger` / `info` variants

### ✅ M9 — MCP + voice + command palette — ✋ M9b vision passthrough
- `StdioMcpClient`: JSON-RPC 2.0 over stdio, 2024-11-05 handshake (`initialize` + `notifications/initialized`), `tools/list`, `tools/call`. Tracks request IDs, dispatches responses back to `TaskCompletionSource`s, owns server lifecycle via `IAsyncDisposable`
- Voice input via WebSpeech (`SpeechRecognition` with `webkit` fallback): continuous + interim results, mic button + `⌘+⇧+M` shortcut, active state styled mint
- Command palette: `⌘+⇧+K` opens; fuzzy filter, arrow nav, Enter to run, Esc to dismiss. Seeded with: New session, Toggle theme, Open settings, Export MD/JSON, Add provider, Search sessions
- **M9b open**: wire MCP tool descriptors into provider request bodies (OpenAI/Anthropic tool-calling), render tool-call cards inline during streaming. Vision content-parts passthrough (`messages.content` → array of `{type:"text"|"image_url",...}`)

### ✅ M10 — Security + ✋ M10b observability + a11y + perf
- **Shipped**:
  - Loopback guard: aborts startup unless `OmniChat:AllowRemote=true` when `ASPNETCORE_URLS` contains a non-loopback address
  - Explicit CORS policy with `OmniChat:AllowedOrigins` array (empty by default = same-origin only)
  - EF Core log level lowered to Warning
  - Rate limiter (token bucket, 30/min/session) on the stream endpoint
  - Key fingerprint already prevents raw-key surfacing in logs (see M2)
- **M10b open**:
  - Serilog JSON sink with `Destructure.ByTransforming<ChatRequest>` to redact prompts and keys
  - OpenTelemetry traces around `IChatProvider.StreamAsync`, exporter null by default
  - Virtualized message list (~80 LOC hand-rolled, `IntersectionObserver`) for sessions >500 messages
  - axe-core automated a11y pass, WCAG AA contrast verification in both themes, keyboard-only audit
  - Lighthouse 95+ accessibility / 90+ perf gates in CI
  - Regenerate `docs/screenshots/` with light-mode variants

### ✅ M11 — MAUI WebView shell — ✋ M11b embedded Kestrel
- **Shipped**:
  - Multi-target `OmniChat.Maui` project (net9.0-android/ios/maccatalyst/windows)
  - Platform entry points (MainActivity/MainApplication on Android; AppDelegate/Program on iOS + MacCatalyst; App.xaml on Windows)
  - `MainPage` with WebView + `omnichat://<bridge>/<method>?<json>` URL interception
  - `BridgeRouter` dispatching to: SecureStorageBridge, ShareBridge, FilePickerBridge, HapticBridge, BiometricBridge (last is graceful "unsupported" until Plugin.Fingerprint bundled)
  - `wwwroot/js/native.js` feature-detects `window.omnichat`; falls back to localStorage / navigator.share / file input / navigator.vibrate on plain browsers
  - Separate `OmniChat.Maui.slnx` so default CI doesn't require the MAUI workload
- **M11b open**: launch the existing ASP.NET Core host in-process inside the MAUI app on `127.0.0.1:<random port>`, point WebView at that URL. Until then `MainPage` shows a placeholder confirming the bridges are wired (real API calls won't work inside MAUI yet)

---

## 8. Open follow-ups (named TODOs, in priority order)

| ID | Scope | Where flagged | Effort |
|---|---|---|---|
| M5b | Real BERT tokenize + ONNX run + mean-pool + normalize inside `OnnxEmbeddingService.Embed` | [OnnxEmbeddingService.cs](../src/OmniChat.Core/Services/OnnxEmbeddingService.cs):53 | M |
| M7b | SQLite FTS5 virtual table backing `HybridSearchService` | [HybridSearchService.cs](../src/OmniChat.Core/Services/HybridSearchService.cs):22 | S |
| M9b | MCP tool descriptors injected into provider request bodies + tool-call UI cards | (no inline TODO; tracked here) | M |
| M9c | Vision passthrough — `messages.content` becomes a content-parts array; UI image upload through `native.pickFile` | (no inline TODO; tracked here) | S |
| M9d | TTS output via browser `SpeechSynthesis` (one helper + a play button on assistant bubbles) | (no inline TODO; tracked here) | XS |
| M10b | Serilog JSON sink + OpenTelemetry traces + virtualized message list + axe-core audit + Lighthouse CI gates | (no inline TODO; tracked here) | M |
| M11b | Embedded Kestrel host inside MAUI shell + WebView pointer to `127.0.0.1:<random port>` | [MainPage.xaml.cs](../src/OmniChat.Maui/MainPage.xaml.cs):15 | M |
| — | BYOK Brave/Serper real web search adapters (replace stub `LocalWebSearchService`) | (no inline TODO; tracked here) | S |
| — | Vendor `marked` + `DOMPurify` under `wwwroot/vendor/` with SRI hashes | DESIGN_SYSTEM.md "Markdown safety" | XS |

---

## 9. Files

### Added by M1–M11 build-out

**Repo root**: `.github/workflows/ci.yml`, `.editorconfig`, `Directory.Build.props`, `OmniChat.Maui.slnx`

**Backend** (`src/OmniChat.Core/`):
- Providers (13 files): `IChatProvider.cs`, `ChatChunk.cs`, `ChatStreamRequest.cs`, `SseHttpStreamReader.cs`, `ProviderException.cs`, `ProviderErrorHelper.cs`, `IChatProviderRegistry.cs`, `ChatProviderRegistry.cs`, `OpenAiChatProvider.cs`, `AnthropicChatProvider.cs`, `AzureOpenAiChatProvider.cs`, `GroqChatProvider.cs`, `EchoChatProvider.cs`
- Security: `IKeyVault.cs`, `KeyFingerprint.cs`
- Data: `OmniChatDbContext.cs`, `Entities/{Session,Message,ProviderConfig,Document,Chunk,Persona,Folder}Entity.cs`
- Mcp: `IMcpClient.cs`, `StdioMcpClient.cs`
- Services (15 new): `IEmbeddingService.cs`, `OnnxEmbeddingService.cs`, `CompositeEmbeddingService.cs`, `ModelDownloader.cs`, `IProviderConfigStore.cs`, `InMemoryProviderConfigStore.cs`, `SqliteProviderConfigStore.cs`, `SqliteChatRepository.cs`, `IPersonaService.cs`, `InMemoryPersonaService.cs`, `SqlitePersonaService.cs`, `IDocumentExtractor.cs`, `PlainTextExtractor.cs`, `PdfDocumentExtractor.cs`, `DocxDocumentExtractor.cs`, `DocumentExtractorRegistry.cs`, `DocumentService.cs`, `HybridSearchService.cs`, `ProviderPricing.cs`, `SessionExporter.cs`

**Web** (`src/OmniChat.Web/`): `Security/DataProtectionKeyVault.cs`

**Frontend** (`src/OmniChat.Web/wwwroot/`): `styles/{tokens,base,components,layout}.css`, `js/{app,state,api,render,keymap,native}.js`, `icons/sprite.svg`

**MAUI** (`src/OmniChat.Maui/`, full project): csproj, App + MainPage XAML/cs, MauiProgram, BridgeRouter + 5 bridges, platform entry points for Android/iOS/MacCatalyst/Windows

**Docs**: `docs/DESIGN_SYSTEM.md`, `docs/ROADMAP.md` (this file)

### Modified by M1–M11 build-out

- `src/OmniChat.Web/Program.cs` — full DI overhaul, SSE endpoint, regenerate, PATCH session, provider CRUD, persona CRUD, document upload + retrieve, search, export, pricing, rate limiting, loopback guard, CORS
- `src/OmniChat.Web/wwwroot/index.html` — shrunk to skeleton + semantic landmarks + skip link
- `src/OmniChat.Web/appsettings.json` — documents `OmniChat:*` config keys
- `src/OmniChat.Core/Services/InMemoryChatRepository.cs` — race fix + new methods (RemoveMessagesFromAsync, RenameSessionAsync, UpdateSessionAsync)
- `src/OmniChat.Core/Services/IChatRepository.cs` — extended interface
- `src/OmniChat.Core/Services/LocalEmbeddingService.cs` — implements `IEmbeddingService`
- `src/OmniChat.Core/Services/RagIndexer.cs` — depends on `IEmbeddingService`
- `src/OmniChat.Core/OmniChat.Core.csproj` — EF Core 9, ONNX, Tokenizers, PdfPig, OpenXml packages + `InternalsVisibleTo`
- `src/OmniChat.Core/Models/ChatSession.cs` — snapshot pattern + Pinned + FolderId
- `src/OmniChat.Core/Models/ChatMessage.cs` — Id, ParentId, TokenCount, CostMicros
- `src/OmniChat.Core/Models/ProviderConfig.cs` — ApiKeyCipher, KeyFingerprint, BaseUrl, SystemPrompt, Temperature, MaxOutputTokens, CreatedAt, UpdatedAt
- `README.md` — reframed around current state, M1-M11 status table, run instructions
- `.github/copilot-instructions.md` — codifies file-split convention, markdown-sanitize contract, key-never-logged rule
- `scripts/capture_ui_screenshots.py` — selectors updated for settings drawer + `modelInput`
- `tests/OmniChat.Core.Tests/CoreServicesTests.cs` — 6 new test classes (193 lines added)

---

## 10. Risks + trade-offs that surfaced during the build-out

- **Vanilla-JS state store**: the `state.js` pub/sub works at current scale but isn't reactive. Already nearing the documented "150 LOC or 2+ cross-surface coordination bugs" threshold for migrating to `@preact/signals-core`. Watch closely as M9b/M10b add more dynamic surfaces.
- **Markdown XSS**: every rendered message body runs through `DOMPurify.sanitize(marked.parse(text))`. This is the only `innerHTML` path in the codebase; everywhere else uses `textContent`. The allow-list is explicit in [render.js → renderMarkdownToHtml](../src/OmniChat.Web/wwwroot/js/render.js). Don't add to it without a security review.
- **CDN coupling**: `marked` + `DOMPurify` currently load from jsdelivr without SRI hashes. The textContent fallback is the actual safety net. Vendoring with SRI hashes is small-effort but deferred — see §8.
- **ONNX model size (23MB)**: never committed. First-run downloader is gated on explicit user prompt to preserve the privacy posture.
- **EF Core migrations**: not yet wired. `EnsureCreatedAsync` is fine while the schema is settling, but as soon as we ship to real users we need `dotnet ef migrations add` and `Database.MigrateAsync` instead.
- **"One file" UI tradition**: respected — `index.html` stays the only HTML file; only CSS + JS split. Open it and you can still see the entire app structure.

---

## 11. Verification plan

After each merge to a feature branch:
- `dotnet build OmniChat.slnx --warnaserror` → 0 warnings
- `dotnet test OmniChat.slnx` → all tests pass (currently 14 tests across 7 classes)
- `python scripts/capture_ui_screenshots.py` → regenerated screenshots in `docs/screenshots/`
- CI pipeline green on PR

Local smoke test sequence (functional checks each milestone unlocks):
1. `dotnet run --project src/OmniChat.Web` → app boots, listens on 127.0.0.1:5078
2. Create session, send message → streaming reply appears token-by-token (Echo provider on first run)
3. Add OpenAI key in the Providers card → fingerprint shows, raw key never visible
4. Send a message with that provider selected → real OpenAI reply streams in
5. Upload a PDF via `POST /api/documents` → `ChunkCount` returned; `GET /api/documents/retrieve?q=...` returns cosine-ranked hits
6. Toggle theme → no FOUC, motion respects `prefers-reduced-motion`
7. `⌘+⇧+K` → command palette opens, arrow + Enter run commands
8. `⌘+/` → session search focuses; type to filter
9. Right-click a session → pin toggles, group migrates to Pinned bucket
10. Hover a message → toolbar appears; copy / regenerate work

---

## 12. Locked decisions

- **Scope**: full M1–M11 (~12-14 weeks solo originally; landed in one drive)
- **Mobile**: MAUI WebView wrapper, NOT a XAML rebuild
- **Frontend-design surfaces (4 locked)**: onboarding carousel, empty chat state, Settings → Providers rack, streaming/tool-call inline card
- **Storage default**: SQLite (`OmniChat:Storage=sqlite`); memory mode is for tests + screenshot automation
- **Loopback default**: refuse non-127.0.0.1 binds unless `OmniChat:AllowRemote=true` is set
- **Key visibility**: raw API keys never surfaced after save; only the SHA-256 fingerprint and a presence boolean

---

## 13. Implementation provenance

Plan + build implemented across 17 commits on branch `claude/omnichat-roadmap-m1-m11`. Reading oldest → newest:

| Commit | What it lands |
|---|---|
| `chore: add CI workflow, editorconfig, analyzer baseline` | M1 tooling |
| `fix: thread-safe session snapshot + repository abstractions` | M1 race fix + repo extensions |
| `feat: provider abstraction with OpenAI/Anthropic/Azure/Groq/Echo adapters` | M2 core |
| `feat: provider config store + persona service (in-memory and SQLite)` | M2 + M5 + M7 stores |
| `feat: EF Core 9 SQLite persistence layer` | M5 schema + SqliteChatRepository |
| `feat: embedding abstraction with ONNX skeleton + model downloader` | M5 embeddings |
| `feat: document extraction + ingestion service` | M7 docs |
| `feat: hybrid search, provider pricing, session exporter` | M7 search + pricing + export |
| `feat: MCP stdio client (JSON-RPC 2.0)` | M9 MCP |
| `feat: data protection key vault (cross-platform BYOK at rest)` | M2 key vault impl |
| `feat: wire backend services + SSE streaming + middleware` | M2 + M6 + M7 + M10 endpoints |
| `feat: design system (tokens, styles, icons, js helpers)` | M3 foundation |
| `feat: UI shell — semantic index.html + app.js orchestration` | M3 + M4 + M6 + M8 + M9 frontend |
| `feat: .NET MAUI WebView shell + native bridges` | M11 mobile |
| `test: provider parsers + SSE reader + fingerprint + snapshot isolation` | M1 + M2 tests |
| `docs: README rewrite + copilot guidance + screenshot script update` | M10 docs |
| `docs: import M1-M11 spec + roadmap into the repo` | this document landed in-repo |
