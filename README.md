# PowerChat

PowerChat is a queue-management layer for your existing AI coding harness. Instead of waiting for each LLM response before typing the next prompt, you can write and queue prompts while the model is still working.

It is built for terminal-window and multiplexer fatigue. Use it alongside Claude Code, Codex, OpenCode, PI Agent, or any other local harness. PowerChat is a native Windows shell built with WinForms and Microsoft WebView2.

## Guidebooks

- [Guidebook for Humans](docs/guidebook-for-humans.md)
- [Guidebook for Agents](docs/guidebook-for-agents.md)

## Human quick start

PowerChat is designed for operators who want a durable local AI conversation shell instead of a browser-only chat window. Each launch creates a session folder, persists messages and drafts, supports attachments, and exposes the current task queue through a local bridge directory.

### Requirements

- Windows 10 version 1809 or later, or Windows 11
- .NET 8.0 SDK
- Microsoft Edge WebView2 Runtime
- Python 3.9+ if you want to run the optional bridge polling script

### Run

```powershell
dotnet run --project WebView2PowerChat.csproj
```

### Session storage

Each launch creates a session folder under:

`%LOCALAPPDATA%\PowerChat\chat_yyyyMMdd_HHmmss_ffff`

The folder stores the conversation and draft state:

- `messages.jsonl` contains the conversation history.
- `attachments/` stores files attached to sent messages.
- `draft-attachments/` stores temporary files attached to an unsent prompt.
- `draft.json` restores the prompt buffer after restart.

### AI bridge

PowerChat communicates with local AI agents through a file-system bridge instead of a local web server. By default, the bridge lives at:

`WebView2PowerChat\bridge\`

The bridge can be overridden with the `POWERCHAT_BRIDGE` environment variable. Agents inspect pending user messages and write assistant replies as JSON files that PowerChat renders inside the app.

A minimal assistant reply looks like this:

```json
{
  "id": "assistant_001",
  "inReplyTo": "msg_001",
  "text": "Markdown **is supported**."
}
```

Read the full human guidebook for interface usage, view modes, export options, task queue behavior, and the roadmap. Read the agent guidebook for bridge protocol details, architecture notes, and development guidance.
