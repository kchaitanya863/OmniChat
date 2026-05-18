# OmniChat Design System

The source of truth for OmniChat's visual + interaction language. Read this before adding new components or proposing visual changes. Tokens live in [`/src/OmniChat.Web/wwwroot/styles/tokens.css`](../src/OmniChat.Web/wwwroot/styles/tokens.css); this doc tells you which token to pick.

## Brand position

> *"A quiet command line for AI — confident, instrumented, on-device."*

Distinguishes OmniChat from:
- **Claude.ai** — warm, soft, generous whitespace. OmniChat is denser, more keyboard-driven.
- **ChatGPT** — neutral utility. OmniChat reads as a tool for developers/power users.
- **T3.chat** — playful, expressive. OmniChat is restrained.
- **OpenWebUI** — utilitarian-bare. OmniChat has more personality, less raw form-list aesthetic.

Visual reference points: Linear's restraint + Raycast's keyboard-first density + a thin cryptographic/circuit motif that *earns* the privacy claim.

## File structure

The UI is split for legibility, not for build-step gymnastics. Vanilla CSS + ES modules, no bundler.

```
src/OmniChat.Web/wwwroot/
├── index.html                # HTML skeleton only — no inline CSS, no inline JS
├── icons/sprite.svg          # all icons + the OmniChat sigil as one sprite
├── styles/
│   ├── tokens.css            # design contract — colors, type, space, motion, radius
│   ├── base.css              # reset, typography, focus ring, prefers-reduced-motion
│   ├── components.css        # buttons, inputs, cards, pills, message bubbles, etc.
│   └── layout.css            # app grid, sidebar, chat, composer, drawer
└── js/
    ├── app.js                # entry — DOM refs, wiring, orchestration
    ├── api.js                # HTTP + SSE client
    ├── render.js             # icon + markdown-sanitize + bubble/empty-state builders
    ├── state.js              # tiny pub/sub state store
    └── keymap.js             # global keyboard shortcuts
```

**Rule**: `index.html` stays the only HTML file. No Razor partials, no `<template>` fragments. Splitting was for engineering, not architecture — open one HTML file and the structure is still legible.

## Color system

Tokens are paired via the CSS `light-dark()` function. To force a theme, set `<html data-theme="light">` or `<html data-theme="dark">` — the JS toggle persists this in `localStorage` under `omnichat.theme`.

### Surface scale

Four steps + base background. Pick by elevation, not by visual taste:

| Token | Job |
|-------|-----|
| `--bg-base` | The body backdrop. Never used for content. |
| `--surface-0` | The lowest content surface (e.g., a settings drawer interior). |
| `--surface-1` | Default card surface. Most cards. |
| `--surface-2` | Lifted surface — buttons at rest, sub-cards. |
| `--surface-3` | Highest emphasis — active toolbar, focus glow base. |

### Accent palette — three jobs, no exceptions

Each accent has **one** semantic job. Don't reach for them decoratively.

| Accent | Job | Example uses |
|--------|-----|--------------|
| `--brand` (blue) | OmniChat identity, primary CTA, active selection | Send button, sidebar active item, sigil |
| `--mint` | Local / secure / streaming | Status dot when streaming, "local" pills, streaming cursor, "secure key" indicators |
| `--amber` | BYOK / cost / warn | "Add API key" prompts, cost totals, mild warnings |
| `--violet` | RAG / citation | Citation chips `[1]`, retrieval evidence chrome |
| `--danger` | Errors / destructive ops only | Clear-all button, error bubbles, alerts |

If you reach for an accent and the role doesn't match — pick a neutral surface or text token instead.

### Semantic tokens

`--success / --warn / --info / --danger` are aliases of the accents above. Prefer the semantic name for UI states. Reserve the accent name for branded surfaces (status pills, citations, etc.).

## Typography

- **UI**: Inter (system stack fallback). Loaded from system or vendored in `wwwroot/fonts/` at M6+.
- **Mono**: ui-monospace stack. Use for model IDs, token meters, key fingerprints, code blocks, the keyboard-shortcut legend in the composer — anywhere precision matters.

Type scale: `--text-xs / sm / base / md / lg / xl / 2xl / 3xl`. Never inline a `font-size` value — pick a step.

Heading rules:
- `h1` = page title (just one per surface) — uses `--text-xl`.
- `h2` = card title — uses `--text-md`.
- `h3` = sub-section — uses `--text-base` 600-weight.

## Spacing + radius

- **Spacing scale (4 px multiples)**: `--space-0 .. --space-20`. Use `gap`/`padding`/`margin` with these tokens — never raw px.
- **Radius**: `--radius-xs / sm / md / lg / xl / 2xl / pill`. Use `--radius-md` (10 px) for inputs/buttons, `--radius-lg` (14 px) for cards, `--radius-xl` (18 px) for outer panels, `--radius-pill` (999) for chips/dots.

## Motion principle

> *"Motion confirms. Never decorates."*

Three easings, three durations. Don't invent new curves.

| Token | When |
|-------|------|
| `--ease-out-soft` | Default — opacity, color, transform on hover |
| `--ease-spring` | Used sparingly for "delight" moments (panel open, success confirmation) |
| `--ease-stream` (linear) | Streaming token reveal only |

| Token | Duration |
|-------|----------|
| `--duration-fast` | 120 ms — hover responses |
| `--duration-base` | 200 ms — open/close, fades |
| `--duration-slow` | 320 ms — drawer/modal |

**Reduced motion**: [`base.css`](../src/OmniChat.Web/wwwroot/styles/base.css) collapses every animation + transition to 0.001 ms inside `@media (prefers-reduced-motion: reduce)`. New animations inherit this for free — do not add `animation-duration` overrides that bypass the guard.

Tokens stream as **opacity-in batches**, never letter-by-letter (epileptic-unfriendly, costly to scroll). The streaming caret blinks at 1 Hz with `steps(1)` (no smoothing). The caret hides itself entirely under reduced motion.

## Markdown safety

**Non-negotiable**: every rendered message body runs through `DOMPurify.sanitize(marked.parse(text))`. This is the *only* path in the codebase that calls `innerHTML`. All other text rendering uses `textContent`.

The pipeline lives in [`render.js → renderMarkdownToHtml`](../src/OmniChat.Web/wwwroot/js/render.js). Allowed tags + attributes are explicit; anything not in the list is stripped.

Failure mode: if `marked` or `DOMPurify` fail to load (offline, CDN blocked, CSP rejection), `renderMarkdownToHtml` returns `null` and the caller falls back to `textContent`. Safe by default.

**SRI hashes are deferred to M6** — when we vendor the libraries under `wwwroot/vendor/` we'll pin SHA-384 integrity hashes. Until then the CDN coupling is acceptable because the fallback path is bulletproof.

## Iconography

All icons live in [`/icons/sprite.svg`](../src/OmniChat.Web/wwwroot/icons/sprite.svg). To use an icon:

```html
<svg aria-hidden="true" focusable="false"><use href="/icons/sprite.svg#i-send"></use></svg>
```

Conventions:
- 24×24 viewBox, 2 px stroke, round caps + joins (Lucide-compatible).
- Always `aria-hidden="true"` + `focusable="false"` unless the icon is the entire accessible name of a button (in which case the button gets `aria-label`).
- The OmniChat **sigil** (`#i-sigil`) is unique — chevron-bracket inside an O. It's the brand mark. Use it in the brand row, the empty-state, and the favicon (when generated). Don't use it as a generic decoration.

When adding an icon, prefer copying the SVG path from Lucide's source. Match the stroke + viewBox conventions above.

## State scaling

The custom `state.js` pub/sub works through ~10 surfaces. **If it grows past 150 LOC or causes more than two cross-surface coordination bugs, migrate to [`@preact/signals-core`](https://github.com/preactjs/signals)** (~1.7 KB, no JSX, vendored as an ES module). Don't pre-emptively migrate — the current store is good enough for M3–M5.

## Accessibility commitments

- Every interactive control has a visible focus ring (`:focus-visible` on body).
- Skip link as the first body child (`.skip-link`), focuses on Tab, lands on `#messages`.
- Landmarks: `<aside>`, `<main>`, `<header>`, `<nav>`. Use `aria-label` to disambiguate similar regions.
- Live region: `#messages` has `aria-live="polite"` so SRs hear streaming completions.
- Reduced motion respected (see Motion principle above).
- Color contrast: `--text-base` against `--surface-1` and `--bubble-*` must clear WCAG AA. Re-check on token edits.

Future audits: run axe-core at M10. Add a Lighthouse CI step at the same time.

## Surfaces marked for frontend-design treatment

The plan ([locked roadmap](../../.claude/plans/search-the-entire-codebase-hidden-bonbon.md)) commits four surfaces to the `frontend-design` skill for distinctive treatment:

1. **Onboarding carousel** (M8) — three-panel intro: *Your Data, Your Device* → *BYOK* → *Local Superpowers*. Pure SVG illustrations, scroll-snap.
2. **Empty chat state** (M4) — sigil + headline + four suggestion chips. Already stubbed in [`render.js → renderEmptyMessages`](../src/OmniChat.Web/wwwroot/js/render.js); frontend-design polish at M4.
3. **Settings → Providers rack** (M6) — visual provider cards with status dots, model count, key fingerprint, last-used.
4. **Streaming/tool-call inline card** (M8) — live MCP tool execution collapsed to a citation pill on completion.

Outside these four, stay disciplined and token-driven.

## When to add a token vs override locally

| Situation | Action |
|-----------|--------|
| You're about to write a hex color | Add a token in `tokens.css` *or* reuse an existing one. Never inline hex outside `tokens.css`. |
| You need a one-off spacing value | Use the nearest scale step. If you genuinely need a new step, propose it here first. |
| You want a new shadow | Reuse `--shadow-sm/md/lg`. If you need a new one, name its job (e.g. "popover floating shadow") and add it. |
| You want a new animation | Reuse the three easings + three durations. If you need a new combo, add it as a named keyframe in `base.css`. |

Reject pull requests that introduce inline hex/px/timing values without a token, unless they're inside `tokens.css` itself.
