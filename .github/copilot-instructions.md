# Copilot Instructions for OmniChat

## Product and architecture principles
- Preserve **local-first BYOK** behavior: user data stays local by default, and no telemetry is enabled by default.
- Keep this repository as a polished baseline for a premium privacy-first chat product.
- Prefer secure defaults and explicit validation on all API boundaries.

## Current implementation map
- `src/OmniChat.Core`: core models and services (chunking, token budgeting, SSE parsing, embeddings, RAG retrieval).
- `src/OmniChat.Web`: minimal web app and API endpoints for session management, messaging, RAG, runtime settings, and storage controls.
- `tests/OmniChat.Core.Tests`: core unit tests.
- `scripts/capture_ui_screenshots.py`: reusable screenshot automation.
- `docs/PRODUCT_OWNER_REVIEW.md`: product-owner quality assessment and launch-gap tracking.

## Required validation commands
Run these before and after code changes:

```bash
dotnet build OmniChat.slnx
dotnet test OmniChat.slnx
```

When UI changes are made, regenerate screenshots:

```bash
python scripts/capture_ui_screenshots.py
```

## Coding guidelines
- Make focused, minimal changes that fully satisfy the request.
- Prefer existing patterns and avoid adding new dependencies unless necessary.
- Use safe rendering patterns in UI code (`textContent`/DOM APIs over HTML interpolation for user content).
- Keep API contracts explicit and validate request input values with clear error responses.
- Maintain accessibility basics (labels, keyboard interactions, visible focus states).

## Product evolution guidelines (going forward)
- Add user-facing controls only when backend behavior is wired and testable.
- Keep storage controls transparent with clear summaries and reversible actions where practical.
- Extend chunking behavior via explicit strategy naming (`focused`, `balanced`, `broad`) before introducing custom advanced modes.
- For every meaningful UX change, refresh screenshots and update documentation in the same PR.
- Update `docs/PRODUCT_OWNER_REVIEW.md` whenever product readiness posture changes.
