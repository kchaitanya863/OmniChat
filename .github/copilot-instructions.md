# Copilot Instructions for OmniChat

## Product and architecture principles
- Preserve **local-first BYOK** behavior: user data stays local by default, and no telemetry is enabled by default.
- Keep this repository as a polished baseline for a premium privacy-first chat product.
- Prefer secure defaults and explicit validation on all API boundaries.
- Active milestone plan lives at [`docs/ROADMAP.md`](../docs/ROADMAP.md). New work should map to a milestone or an explicit follow-up.

## Current implementation map
- `src/OmniChat.Core`: domain library — models, services (chunking, token budgeting, embeddings, RAG, SSE parsing), provider abstractions (`Providers/`), security primitives (`Security/`).
- `src/OmniChat.Web`: ASP.NET Core 9 host — Minimal-API endpoints, DI wiring, key vault impl (`Security/DataProtectionKeyVault`), static `wwwroot/`.
- `src/OmniChat.Web/wwwroot/`: split single-page UI. `index.html` is HTML only; CSS lives in `styles/*.css`; JS in `js/*.js` (ES modules); icons in `icons/sprite.svg`. See `docs/DESIGN_SYSTEM.md` for the file-split rationale.
- `tests/OmniChat.Core.Tests`: unit + parser tests. `InternalsVisibleTo` is enabled for white-box tests.
- `scripts/capture_ui_screenshots.py`: reusable screenshot automation.
- `docs/PRODUCT_OWNER_REVIEW.md`: product-owner readiness assessment.
- `docs/DESIGN_SYSTEM.md`: design tokens, accent rules, motion principle, markdown safety, state-scaling threshold.

## File-split convention (do NOT re-inline)
The single-page UI was deliberately split for legibility. Do not:
- Re-inline CSS into `<style>` inside `index.html` (only `<link>` tags belong in `<head>`).
- Re-inline JS into `<script>` inside `index.html` (only `<script type="module" src="...">` belongs in `<head>`).
- Add new HTML files (Razor partials, `<template>` fragments) — `index.html` stays the only HTML file.

CSS belongs in `styles/{tokens,base,components,layout}.css`; JS in `js/{app,api,render,state,keymap}.js`; icons go in `icons/sprite.svg`. Add new files in those directories rather than creating new top-level paths.

## Required validation commands
Run these before and after code changes:

```bash
dotnet build OmniChat.slnx --warnaserror
dotnet test OmniChat.slnx
```

When UI changes are made, regenerate screenshots:

```bash
python scripts/capture_ui_screenshots.py
```

CI (`.github/workflows/ci.yml`) runs the same on push/PR.

## Coding guidelines
- Make focused, minimal changes that fully satisfy the request.
- Prefer existing patterns and avoid adding new dependencies unless necessary.
- Use safe rendering patterns in UI code (`textContent`/DOM APIs over HTML interpolation for user content). The **only** exception is rendered markdown, which must go through `DOMPurify.sanitize(marked.parse(text))` — see `render.js → renderMarkdownToHtml`.
- Never log or surface plaintext API keys. `ProviderConfig.ApiKeyCipher` is encrypted via `IKeyVault.Seal`; `KeyFingerprint` is the only UI-safe identifier.
- Keep API contracts explicit and validate request input values with clear error responses.
- Maintain accessibility basics (labels, keyboard interactions, visible focus states, `prefers-reduced-motion`).
- New colors/sizes/timings: add a token in `tokens.css` and reference it. Do not inline hex/px/ms outside `tokens.css`.

## Streaming and provider integration
- All chat completions stream via `IChatProvider.StreamAsync` (returns `IAsyncEnumerable<ChatChunk>`).
- Add a new provider by implementing `IChatProvider`, registering the singleton in `Program.cs`, and adding a parser test in `tests/OmniChat.Core.Tests/`.
- The SSE endpoint is `POST /api/sessions/{id}/stream`; events are `delta`/`done`/`error`. Don't add a non-streaming chat path.

## Product evolution guidelines (going forward)
- Add user-facing controls only when backend behavior is wired and testable.
- Keep storage controls transparent with clear summaries and reversible actions where practical.
- Extend chunking behavior via explicit strategy naming (`focused`, `balanced`, `broad`) before introducing custom advanced modes.
- For every meaningful UX change, refresh screenshots and update documentation in the same PR.
- Update `docs/PRODUCT_OWNER_REVIEW.md` whenever product readiness posture changes.
