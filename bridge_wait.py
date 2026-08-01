from __future__ import annotations

import datetime as dt
import json
import pathlib
import sys
import time
from typing import Any


BRIDGE = pathlib.Path(__file__).resolve().parent / "bridge"
LATEST_MESSAGE = BRIDGE / "latest-user-message.json"
AI_STATUS = BRIDGE / "ai-status.json"
ASSISTANT_REPLY = BRIDGE / "assistant-reply.json"


def read_json(path: pathlib.Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return {}


def write_json_atomic(path: pathlib.Path, value: dict[str, Any]) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(
        json.dumps(value, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    temporary.replace(path)


def parse_time(value: Any) -> dt.datetime | None:
    if not isinstance(value, str) or not value.strip():
        return None
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=dt.timezone.utc)
    return parsed


def disconnect_for_timeout(status: dict[str, Any]) -> None:
    now = dt.datetime.now(dt.timezone.utc).astimezone()
    session_id = read_json(BRIDGE / "current-chat.json").get("sessionId")
    write_json_atomic(
        AI_STATUS,
        {
            "state": "disconnected",
            "surface": "PowerChat",
            "detail": "Disconnected after one hour without a PowerChat message.",
            "currentUserMessageId": None,
            "updatedAt": now.isoformat(),
            "expiresAt": None,
        },
    )
    write_json_atomic(
        ASSISTANT_REPLY,
        {
            "id": f"assistant_{int(time.time() * 1000)}_timeout",
            "role": "assistant",
            "text": "**POWERCHAT STATUS: DISCONNECTED — the one-hour inactivity timeout expired.**",
            "inReplyTo": None,
            "completeTask": False,
            "createdAt": now.isoformat(),
            "sessionId": session_id,
        },
    )
    print(json.dumps({"event": "timeout", "status": status}, ensure_ascii=False), flush=True)


def main() -> int:
    baseline_id = sys.argv[1] if len(sys.argv) > 1 else read_json(LATEST_MESSAGE).get("id")

    while True:
        latest = read_json(LATEST_MESSAGE)
        latest_id = latest.get("id")
        if latest_id and latest_id != baseline_id:
            print(json.dumps({"event": "message", "message": latest}, ensure_ascii=False), flush=True)
            return 0

        status = read_json(AI_STATUS)
        if str(status.get("state", "")).lower() != "connected":
            print(json.dumps({"event": "disconnected", "status": status}, ensure_ascii=False), flush=True)
            return 0

        expires_at = parse_time(status.get("expiresAt"))
        now = dt.datetime.now(dt.timezone.utc).astimezone()
        if expires_at is not None and now >= expires_at:
            disconnect_for_timeout(status)
            return 0

        time.sleep(0.5)


if __name__ == "__main__":
    raise SystemExit(main())
