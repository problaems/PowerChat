# PowerChat

A native borderless Windows chat shell built with WinForms and Microsoft WebView2.

## Storage

Each launch creates a session folder under:

`%LOCALAPPDATA%\PowerChat\chat_yyyyMMdd_HHmmss_ffff`

The folder contains `messages.jsonl` and an `attachments` directory. A development bridge is created at `WebView2PowerChat\bridge` so a local coding agent can inspect user messages and place `assistant-reply.json` responses that are rendered inside the app.

## Run

```powershell
dotnet run --project WebView2PowerChat.csproj
```

## Bridge reply shape

```json
{
  "id": "assistant_001",
  "inReplyTo": "msg_001",
  "text": "Markdown **is supported**."
}
```
