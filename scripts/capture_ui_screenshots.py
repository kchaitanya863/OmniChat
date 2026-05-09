#!/usr/bin/env python3
"""Capture OmniChat UI screenshots with reusable automation.

Usage examples:
  python scripts/capture_ui_screenshots.py
  python scripts/capture_ui_screenshots.py --no-start-server --base-url http://127.0.0.1:5078
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path
from urllib.request import urlopen
from urllib.error import URLError

try:
    from playwright.sync_api import sync_playwright
except ImportError as exc:  # pragma: no cover
    print("Playwright is required. Install with: python -m pip install playwright && python -m playwright install chromium")
    raise SystemExit(1) from exc


def parse_args() -> argparse.Namespace:
    repo_root = Path(__file__).resolve().parent.parent

    parser = argparse.ArgumentParser(description="Capture OmniChat UI screenshots")
    parser.add_argument("--base-url", default="http://127.0.0.1:5078", help="App URL")
    parser.add_argument(
        "--project",
        default=str(repo_root / "src/OmniChat.Web/OmniChat.Web.csproj"),
        help="Path to OmniChat web project used when starting local server",
    )
    parser.add_argument(
        "--output-dir",
        default=str(repo_root / "docs/screenshots"),
        help="Directory where screenshots will be written",
    )
    parser.add_argument("--session-title", default="Screenshot Session", help="Session title used in UI flow")
    parser.add_argument("--prompt", default="Summarize local-first BYOK privacy posture.", help="Prompt sent in UI flow")
    parser.add_argument("--model", default="demo-local-model", help="Model text to fill in model input")
    parser.add_argument("--timeout-seconds", type=int, default=45, help="Server startup timeout")
    parser.add_argument("--no-start-server", action="store_true", help="Use existing running server")
    return parser.parse_args()


def wait_for_server(base_url: str, timeout_seconds: int) -> None:
    deadline = time.time() + timeout_seconds
    probe_url = f"{base_url.rstrip('/')}/api/sessions"

    while time.time() < deadline:
        try:
            with urlopen(probe_url, timeout=2) as response:
                if response.status == 200:
                    return
        except URLError:
            pass
        time.sleep(0.5)

    raise TimeoutError(f"Server did not become ready within {timeout_seconds}s at {probe_url}")


def capture(base_url: str, output_dir: Path, session_title: str, prompt: str, model: str) -> list[Path]:
    output_dir.mkdir(parents=True, exist_ok=True)

    home = output_dir / "ui-home.png"
    sessions = output_dir / "ui-sessions.png"
    chat = output_dir / "ui-chat-after-send.png"

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1440, "height": 960})

        page.goto(base_url, wait_until="networkidle")
        page.screenshot(path=str(home), full_page=True)

        page.fill("#newSessionTitle", session_title)
        page.click("#createSessionBtn")
        page.wait_for_timeout(600)
        page.screenshot(path=str(sessions), full_page=True)

        page.fill("#model", model)
        page.fill("#prompt", prompt)
        page.click("#sendBtn")
        page.wait_for_timeout(1400)
        page.screenshot(path=str(chat), full_page=True)

        browser.close()

    return [home, sessions, chat]


def main() -> int:
    args = parse_args()
    output_dir = Path(args.output_dir)

    server_process: subprocess.Popen[str] | None = None
    try:
        if not args.no_start_server:
            server_process = subprocess.Popen(
                [
                    "dotnet",
                    "run",
                    "--project",
                    args.project,
                    "--urls",
                    args.base_url,
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                text=True,
            )

        wait_for_server(args.base_url, args.timeout_seconds)
        paths = capture(args.base_url, output_dir, args.session_title, args.prompt, args.model)

        print("Captured screenshots:")
        for path in paths:
            print(f"- {path}")

        return 0
    except Exception as exc:  # pragma: no cover
        print(f"Screenshot capture failed: {exc}", file=sys.stderr)
        return 1
    finally:
        if server_process and server_process.poll() is None:
            server_process.terminate()
            try:
                server_process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server_process.kill()


if __name__ == "__main__":
    raise SystemExit(main())
