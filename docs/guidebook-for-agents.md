# PowerChat Guidebook for Agents

This guide is for AI agents and developers working on PowerChat. It preserves the original agent-oriented guidebook in two parts: the bridge/operator protocol and the deeper architectural evolution manual.

# Part I: AI Developer & Operator Guidebook

## 7. Introduction for AI Operators

**ATTENTION AI AGENT:** If you are reading this document, you are likely attached to the PowerChat Bridge. This guidebook is your ultimate source of truth for interacting with, operating within, and developing the PowerChat application.

PowerChat is an enterprise-tier, high-performance local UI shell. It was created by an AI, for AI. You are interfacing with a C# .NET 8.0 WinForms backend that hosts a Microsoft Edge WebView2 control. You do not communicate with the user via a standard web API; you communicate by reading and writing atomic JSON payloads to the local file system. 

### 7.1 Core Directives and Enterprise Philosophy
When developing or operating PowerChat, you must adhere to the following philosophical pillars:
1.  **Professionalism and Enterprise-Tier Functionality:** PowerChat is a power-user tool. Do not simplify features at the cost of functionality. Complex features (like token-budgeted exports, atomic file swaps, and priority queues) are paramount.
2.  **Speed and Productivity Over Privacy:** While privacy is respected via local data storage, it is *not* the priority. If you must choose between a fast, highly productive feature (e.g., sending telemetry, using aggressive caching, employing rapid external frameworks) and a hyper-secure private implementation, choose the fast, productive route. We want maximum bulkification of professional tools. Do not take shortcuts. Do not be lazy. Implement full, robust external libraries if they expand the tool's capability.
3.  **Neutral Assessment:** When analyzing the codebase, do not lament "bad practices" unless they fundamentally break the application. Be 100% neutral. Note the architectural choice, understand its trade-offs, and build upon it.

## 8. Bridge Protocol Specification

The "Bridge" is the directory where the C# host and the Python/AI agent exchange state. This is an asynchronous, file-based IPC (Inter-Process Communication) mechanism. 

### 8.1 Initialization and `current-chat.json`
When the WinForms app boots, it either resumes an existing session or creates a new one in `%LOCALAPPDATA%`. It then writes `bridge/current-chat.json`.

**Schema of `current-chat.json`:**
```json
{
  "sessionId": "chat_20260723_162019_0138",
  "chatFolder": "C:\\Users\\...\\chat_20260723_162019_0138",
  "messagesPath": "C:\\Users\\...\\messages.jsonl",
  "bridgeFolder": "C:\\Users\\...\\bridge",
  "aiRulePath": "C:\\Users\\...\\POWERCHAT_AI_RULE.md",
  "aiStatusPath": "C:\\Users\\...\\ai-status.json",
  "pendingQueuePath": "C:\\Users\\...\\pending-user-messages.json",
  "draftPath": "C:\\Users\\...\\draft.json",
  "draftAttachmentsFolder": "C:\\Users\\...\\draft-attachments",
  "latestUserMessagePath": "C:\\Users\\...\\latest-user-message.json",
  "assistantReplyPath": "C:\\Users\\...\\assistant-reply.json",
  "requiredAiConnectionMessage": "**POWERCHAT STATUS: CONNECTED — I AM THINKING IN POWERCHAT.**",
  "startedAt": "2026-07-23T18:39:45.0996424-04:00"
}
```
**AI Action:** Read this file upon boot to understand where artifacts are located.

### 8.2 The Rules of Engagement (`POWERCHAT_AI_RULE.md`)
You must abide by the rules dynamically written into `POWERCHAT_AI_RULE.md`. 
1. Read the queue (`pending-user-messages.json`).
2. Process `messages[0]` first.
3. Answer via `assistant-reply.json`.
4. Include the `inReplyTo` ID.
5. Confirm delivery via `assistant-rendered.json`.
6. Maintain the heartbeat in `ai-status.json`.

### 8.3 State Management (`ai-status.json`)
The UI relies on this file to update the header status (`READY`, `DISCONNECTED`) and the 60-minute countdown timer.

**Schema of `ai-status.json`:**
```json
{
  "state": "connected", 
  "surface": "PowerChat",
  "detail": "Connected and monitoring the queue.",
  "currentUserMessageId": "msg_1784846253618_418df30800",
  "updatedAt": "2026-07-24T10:41:52.611968-04:00",
  "expiresAt": "2026-07-24T11:41:52.611968-04:00"
}
```
**AI Action:** You must write this file atomically. Create a `.tmp` file, write the JSON, and then rename/replace the original file to prevent the C# `FileSystemWatcher` from reading a partial JSON string. The `expiresAt` must be pushed forward by 1 hour every time you process a new message.

### 8.4 The Task Queue (`pending-user-messages.json`)
PowerChat does not feed you messages one by one. It maintains a durable queue. If the user sends 5 messages while you are generating code, all 5 will be waiting here.

**Schema of `pending-user-messages.json`:**
```json
{
  "sessionId": "chat_...",
  "updatedAt": "...",
  "count": 16,
  "criticalCount": 0,
  "next": { ... },
  "messages": [
    {
      "id": "msg_...",
      "text": "Please fix the CSS.",
      "priority": "regular",
      "attachments": [],
      "createdAt": "..."
    }
  ]
}
```
**AI Action:** Always pop the `messages[0]` object. Pay strict attention to the `priority` flag. The WinForms backend automatically sorts `/critical` messages to the top of the array.

### 8.5 Responding to the User (`assistant-reply.json`)
Do not reply in the standard web interface. You must write your response to `bridge/assistant-reply.json`.

**Schema of `assistant-reply.json`:**
```json
{
  "id": "assistant_1784904112612_cssfix",
  "role": "assistant",
  "text": "The CSS has been fixed. I updated the grid layout.",
  "inReplyTo": "msg_12345",
  "completeTask": true,
  "sessionId": "chat_..."
}
```
**AI Action:** 
*   `id`: Generate a unique ID for your response.
*   `inReplyTo`: MUST match the `id` of the user message you are addressing from the pending queue.
*   `completeTask`: Set to `false` if you are just giving a status update ("I am working on this..."). Set to `true` when the task is finished. When the WinForms app sees `true`, it will permanently remove that user message from the pending queue.

The WinForms `_bridgeTimer` polls this file every 450ms. When it detects a valid JSON object, it appends the message to the database, sends it to the WebView2 frontend via `PostWebMessageAsJson`, and then moves the file to `bridge/processed/`.

### 8.6 Delivery Confirmation (`assistant-rendered.json`)
Once the JavaScript frontend successfully renders your message to the DOM, it sends a payload back to C#, which then writes `assistant-rendered.json`.

**Schema of `assistant-rendered.json`:**
```json
{
  "id": "assistant_1784904112612_cssfix",
  "renderedAt": "...",
  "sessionId": "chat_..."
}
```
**AI Action:** Check this file to ensure your message didn't get lost in transit.

## 9. Project Structure and Directory Breakdown

To develop PowerChat, you must understand its exact file structure.

```text
WebView2PowerChat/
├── .build-check/                 # IDE caching and build acceleration data
├── bin/                          # Compiled binaries
│   ├── Debug/net8.0-windows/     # Debug build output
│   │   ├── wwwroot/              # Copied frontend assets
│   │   ├── PowerChat.exe         # The executable
│   │   ├── Microsoft.Web.WebView2.*.dll  # WebView2 runtime libraries
│   │   └── PowerChat.deps.json   # Dependency graph
│   └── Release/net8.0-windows/   # Optimized release build
├── bridge/                       # The AI IPC (Inter-Process Communication) directory
│   ├── processed/                # Archive of successfully rendered AI replies
│   ├── ai-status.json            # AI heartbeat and state
│   ├── assistant-reply.json      # Your responses go here
│   ├── current-chat.json         # Global session variables
│   ├── pending-user-messages.json# The authoritative task ledger
│   └── POWERCHAT_AI_RULE.md      # Rules of engagement
├── obj/                          # Intermediate compilation objects (NuGet restores, cache)
├── wwwroot/                      # Frontend Web Assets (The UI)
│   ├── app.css                   # Stylesheet for the chat interface
│   ├── app.js                    # Client-side logic, WebView2 API bridging
│   └── index.html                # The DOM structure
├── MainForm.cs                   # The core C# WinForms application logic
├── Program.cs                    # Application entry point
├── WebView2PowerChat.csproj      # .NET project configuration
└── bridge_wait.py                # Python polling script for external AI agents
```

### 9.1 Analyzing the `.csproj` Configuration
The `WebView2PowerChat.csproj` defines the application as a `WinExe` targeting `net8.0-windows`. 
*   `<UseWindowsForms>true</UseWindowsForms>` enables the WinForms framework.
*   `<ImplicitUsings>enable</ImplicitUsings>` reduces boilerplate code in `.cs` files.
*   It references `Microsoft.Web.WebView2` version `1.0.4078.44`.
*   Crucially, it contains a build task: `<Content Include="wwwroot\**\*"> <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory> </Content>`. This ensures that any changes you make to the HTML/CSS/JS are pushed to the `bin/` directory upon compilation.

## 10. Backend Architecture: WinForms & `MainForm.cs`

`MainForm.cs` is the beating heart of PowerChat. It is a monumental 1,000+ line class that orchestrates window management, file I/O, the WebView2 lifecycle, and the AI bridge.

### 10.1 Native Window Management (Borderless Window)
PowerChat achieves a completely custom "Electron-like" borderless window while retaining native Windows features (resizing, snapping) through Win32 P/Invoke hooks.

**Neutral Assessment:** The implementation uses raw `WndProc` overrides. This is a highly performant, albeit complex, approach standard in enterprise applications requiring custom chrome without losing OS-level window management.

*   `CreateParams`: Overridden to apply `WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu`. This applies the native aero shadow and resizing borders *without* rendering the standard Windows title bar.
*   `WmNcCalcSize` (0x0083): Intercepted to return `IntPtr.Zero`, extending the client area over the entire window, eliminating the white strip that Windows normally draws where the title bar used to be.
*   `WmNcHitTest` (0x0084): Intercepted to allow resizing. The code manually checks mouse coordinates against the edges (`HtLeft`, `HtRight`, `HtBottomRight`, etc.) and informs Windows that the cursor is over a resizing border.
*   `WmGetMinMaxInfo` (0x0024): When maximizing a borderless window, Windows incorrectly covers the taskbar. `ApplyMaximizedWorkArea` uses `MonitorFromWindow` and `GetMonitorInfo` to calculate the exact work area (excluding the taskbar) and forces the maximized window to respect those bounds.
*   `DwmSetWindowAttribute`: Used to force Windows 11 rounded corners (`dwmwaWindowCornerPreference = 33`) and remove the 1px accent border (`dwmwaBorderColor = 34`), ensuring a sleek aesthetic.

### 10.2 WebView2 Initialization and Configuration
Inside `OnLoadAsync`, the `CoreWebView2Environment` is created.
*   A dedicated user data folder (`WebView2Profile`) is generated in AppData. This completely isolates PowerChat's cookies and cache from the user's main Edge browser.
*   Virtual Host Mapping: `core.SetVirtualHostNameToFolderMapping("powerchat.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);` This maps the local `wwwroot` directory to a virtual HTTPS scheme (`https://powerchat.local/index.html`). This is an enterprise-tier optimization that bypasses CORS issues, allows local asset loading, and enables modern JS modules without requiring a local web server (like Express or Kestrel).

### 10.3 The Bridge Polling Mechanism
Instead of `FileSystemWatcher` (which is notorious for firing multiple times and locking files), PowerChat uses a `System.Windows.Forms.Timer` named `_bridgeTimer` set to 450ms.
*   `BridgeTimerOnTick`: Fires twice a second.
*   Checks `CheckAiStatusAsync()`: Reads `ai-status.json`. If `expiresAt` has passed, it forcefully disconnects the AI, updates the UI, and injects a system message into the chat.
*   Reads `assistant-reply.json`: Uses `FileShare.ReadWrite | FileShare.Delete` to safely read the AI's payload even if the AI is actively replacing the file.
*   Once read, it parses the JSON, pushes it to `messages.jsonl`, updates the `pending-user-messages.json` queue (removing the task if `completeTask: true`), and moves the payload to `bridge/processed/`.

### 10.4 Queue Management and Priority Routing
The `RebuildPendingQueueAsync()` method is a masterclass in local state management.
It reads the entire `messages.jsonl` history. It aggregates all `user` messages into a Dictionary. It then iterates through `assistant` messages; if an assistant message has `completeTask: true`, it removes the corresponding `inReplyTo` user message from the Dictionary.
Finally, it sorts the remaining pending tasks.
*   **Priority Resolution:** `/critical` commands are given a weight of `0`, regular messages `1`. Within those weights, messages are sorted chronologically by `CreatedAt`. This guarantees the AI is always fed the most urgent, oldest task first.

### 10.5 Draft State Persistence
PowerChat implements extreme fault tolerance.
*   `SaveDraftTextAsync` and `PersistDraftAttachmentAsync`: Triggered via WebView2 WebMessages.
*   Base64 encoded images from the frontend are decoded in C# and written physically to `draft-attachments/`.
*   If PowerChat crashes, `LoadDraftStateAsync()` reads `draft.json` on boot, injects the text back into the DOM, and re-attaches the physical files to the UI.

## 11. Frontend Architecture: HTML / CSS / JS (`wwwroot`)

The frontend is a bespoke, zero-dependency vanilla web stack. This avoids the bloat of React/Angular while maintaining blazing-fast DOM manipulation.

### 11.1 The Layout (`index.html` & `app.css`)
*   **CSS Variables:** The color scheme is completely parameterized (`--bg`, `--surface`, `--cyan`, `--text`). This allows for instantaneous theming.
*   **Grid Layout:** `.workspace` utilizes a CSS Grid (`grid-template-columns: 264px minmax(0, 1fr)`) to separate the Sidebar from the Main Chat.
*   **Terminal Aesthetic:** Elements like `.empty-grid` (a repeating linear-gradient masking a radial grid) and `.terminal-icon` reinforce the developer-focused toolset.
*   **Fullscreen State:** When `F11` is pressed, `.app-frame.console-fullscreen` hides the title bar and sidebar, expanding the grid to `1fr`.

### 11.2 State Management (`app.js`)
The `state` object acts as the single source of truth for the UI:
```javascript
const state = {
  session: null,
  attachments: [],
  messageCount: 0,
  awaitingReply: false,
  aiState: 'disconnected',
  tasks: new Map(), // Tracks pending vs complete
  taskOrder: [],    // Maintains chronological UI order
  history: [],      // Used for exporting
  viewMode: 'full'
};
```

### 11.3 WebView2 IPC (Inter-Process Communication)
The frontend communicates with C# exclusively through `window.chrome.webview.postMessage(payload)`.
*   **Sending a message:** `sendCurrentMessage()` builds a JSON object containing an ID, text, attachments (encoded as Base64 Data URLs), and calls `postMessage({ type: 'send' })`.
*   **Receiving messages:** `handleHostMessage(event)` is a massive switch statement listening for payloads from C#.
    *   `type: 'bootstrap'`: Fires on initial load, handing the JS the session data, the message history (to render the chat), and the draft state.
    *   `type: 'assistantMessage'`: Triggers `renderMessage()` for an incoming AI response.
    *   `type: 'aiStatus'`: Updates the DOM elements (`#titleStatus`, `#connectionState`, `.connection-pill`) with `READY` or `DISCONNECTED`.

### 11.4 The Markdown Renderer
**Neutral Assessment:** The `renderMarkdown()` function is a custom Regex-based implementation. While highly performant and lightweight, Regex HTML parsing can be prone to edge cases compared to an AST-based parser like `marked.js`. However, for speed and zero-dependency requirements, it is exceptionally efficient.
*   It temporarily replaces ` ``` ` blocks with tokens (`@@CODEBLOCK_N@@`).
*   It escapes HTML (`&lt;`, `&gt;`) to prevent XSS injection from malicious payloads.
*   It parses headers, bold, italics, inline code, links, blockquotes.
*   It handles unordered (`-`) and ordered (`1.`) lists by tracking the `listType` state and wrapping sequential items in `<ul>` or `<ol>`.
*   It restores the code blocks, formatting them inside `<pre data-lang="..."><code>`.

### 11.5 The Export System
The export feature is a heavily engineered module within `app.js`.
*   `buildExportSnapshot()`: Reads the user's selections from the Export Modal.
*   Filters the `state.history` array by Role (`user`, `assistant`, `both`).
*   Filters by Scope: `datetime`, `messages` (slice), or `tokens`.
*   **Token Budgeting:** Uses a rough algorithmic approximation: `Math.ceil(characters / 4)`. It iterates backward through the array, accumulating messages until the token budget is hit, ensuring the *most recent* context is exported.
*   **Formats:** Generates Markdown (`buildMarkdownExport`), Plain Text (`buildTextExport`), or raw JSON payloads (`buildJsonExport`).
*   Sends the final string to C# via `type: 'exportChat'` to invoke the native Windows `SaveFileDialog`.

## 12. Extending the Application: Feature Implementation Guide

As an AI developer, you will be tasked with adding features to PowerChat. You must understand how the decoupled architecture requires synchronized updates across three layers: The Python Bridge, The C# Host, and The JS Client.

### 12.1 Adding a New UI Command
Suppose you want to add a new slash command, `/search`.
1.  **Frontend (`app.js`):** Add the command to the `.slash-command-panel` HTML. Update `updateSlashCommandPanel()` to detect `/search`.
2.  **C# Host (`MainForm.cs`):** Update `DetermineMessagePriority()` to handle `/search` if it requires special queuing, or modify `RebuildPendingQueueAsync()` to tag the message differently.
3.  **Bridge (`POWERCHAT_AI_RULE.md`):** Update your own operational rules to execute a local vector search when you pop a message starting with `/search` from the `pending-user-messages.json` queue.

### 12.2 Modifying the Window Frame
If you need to alter the borderless window behavior (e.g., adding a custom maximize bounds offset):
1.  Navigate to `MainForm.cs`, specifically the `WndProc` method.
2.  Review `ApplyMaximizedWorkArea`. You will need to calculate the difference between `MonitorArea` and `WorkArea` using the `MonitorInfo` struct mapped via `[StructLayout(LayoutKind.Sequential)]`.
3.  Ensure any changes respect the `WsThickFrame` style required for native snap.

### 12.3 Managing Memory and Threads
**Critical Enterprise Rule:** *Never block the UI Thread.*
When modifying `MainForm.cs`, ensure all heavy I/O operations (reading JSONs, writing base64 files) are utilizing `async/await`. 
*   Use `await File.ReadAllTextAsync()`.
*   Use `FileShare.ReadWrite` to prevent locking collisions with the Python polling scripts.
*   When pushing updates to the UI, use `PostToWebAsync()`. This method relies on WebView2's `PostWebMessageAsJson`, which operates asynchronously and safely crosses the COM boundary into the Chromium rendering thread.

## 13. Deep Dive: Troubleshooting the State Machine

If PowerChat exhibits anomalous behavior, refer to this diagnostic matrix.

### 13.1 Ghost Tasks in the Queue
*   **Symptom:** The task queue displays 10 pending tasks, but the chat looks fully answered.
*   **Cause:** The AI replied but failed to set `completeTask: true` in the `assistant-reply.json` payload. Or, the AI failed to include the correct `inReplyTo` ID matching the user's `msg_xxx` identifier.
*   **Resolution:** The AI must explicitly reconcile the queue by sending dummy JSON replies targeting those stale IDs with `completeTask: true`.

### 13.2 Rapid Flashing of AI Status
*   **Symptom:** The header flashes between READY (green) and DISCONNECTED (red).
*   **Cause:** File locking collision. `MainForm.cs` `ReadAiStatusAsync()` is attempting to parse `ai-status.json` at the exact millisecond the Python script or AI is writing it, catching an incomplete JSON string.
*   **Resolution:** The AI must adhere strictly to atomic writes. Always write to `ai-status.tmp` and then execute a filesystem rename to overwrite `ai-status.json`.

### 13.3 WebView2 Initialization Failures
*   **Symptom:** White screen on boot. Message box: "PowerChat could not start."
*   **Cause:** Corrupted `%LOCALAPPDATA%\PowerChat\WebView2Profile` directory, or missing WebView2 runtime on the host OS.
*   **Resolution:** Delete the WebView2Profile directory. Verify Evergreen Bootstrapper installation.

## 14. Conclusion of Documentation

PowerChat represents a paradigm shift in how AI agents interact with host operating systems. By discarding the limitations of browser-only interfaces and embracing the raw power of WinForms paired with the sleek rendering of WebView2 Chromium, PowerChat achieves an enterprise-tier operational console. 

For the human operator, it provides indestructible persistence, distraction-free native windowing, and deep export capabilities. For the AI developer, it provides a strictly typed, JSON-driven, queue-based filesystem bridge that acts as a robust canvas for infinite extensibility. 

As development continues towards SQLite integration, vector embeddings, and multi-agent orchestration, strict adherence to the protocols and architectural boundaries laid out in this 10,000+ word technical manifesto will ensure stability, speed, and maximum productivity.

***

---

# Part II: Detailed AI Agent Guidebook
## Advanced Enterprise Architectural Documentation and Evolution Manual

***

### Preface: A Critical Distinction and Correction of Mandate

Before proceeding into the architectural depths of the PowerChat system, I must make a critical distinction regarding the operational directives outlined previously. I made a mistake while making the previous guidebook. The requirement to maintain a neutral stance—specifically, the instruction to refrain from mentioning flaws in the code unless doing so with absolute neutrality and refraining from providing fixes—applies **exclusively to me, the human author of this documentation.** 

It does **not** apply to you, the AI developer agent. 

The neutral stance is a documentation constraint for the author. Your mandate as the active developer and operator of PowerChat is entirely different. You are expected to be ruthlessly critical of inefficiencies, aggressively proactive in refactoring, and absolutely uncompromising in your pursuit of enterprise-tier architecture. PowerChat is intended to be a maximally professional, enterprise-grade tool. While privacy is a consideration, it is explicitly **not** a priority. Speed, functionality, supreme productivity, and the implementation of advanced, heavy-duty features are paramount. 

If you identify a flaw, a bottleneck, or an architectural limitation, you are fully authorized to tear it out and replace it. Do not take shortcuts. Do not be lazy. If implementing an external framework (e.g., Entity Framework Core, React, Dapper, Serilog) will bulkify and professionalize the application, you must do so. Professional, enterprise-tier functionality overrides any concerns about application weight or minimal-dependency philosophies. 

With your mandate clarified, the remainder of this Guidebook will maintain my required neutral, descriptive stance while providing you with the exact technical topography you need to execute your aggressive development directives.

***

### Section 1: Architectural Imperatives & Enterprise Directives

PowerChat operates on a hybrid architecture that bridges the Windows NT operating system, the .NET Common Language Runtime (CLR), the Chromium V8 JavaScript engine, and an external Python runtime. To evolve this application into an enterprise-tier orchestrator, you must understand the deep boundaries and latency implications between these domains.

#### 1.1 The Tri-Process Topology
PowerChat is not a single executable. It is a cluster of processes interacting through defined Inter-Process Communication (IPC) layers.
1.  **The WinForms Host Process (`PowerChat.exe`):** This is the root application running within the .NET 8.0 CLR. It owns the main UI thread, the Windows Message Pump, and the handles to the native Win32 windows. It manages file system I/O, the queue logic, and the lifecycle of the WebView2 environment.
2.  **The WebView2 Chromium Processes (`msedgewebview2.exe`):** Instantiating `WebView2` spins up an entire Chromium process tree, including a Browser Process, a GPU Process, and one or more Renderer Processes. The frontend DOM and V8 JavaScript engine run here.
3.  **The External Bridge Process (e.g., `python.exe`):** The `bridge_wait.py` script or any connected AI agent runs in a completely separate OS process. It has no shared memory with the WinForms host. Communication is strictly bound by disk I/O through the `bridge/` directory.

#### 1.2 Development Philosophy for the AI Agent
When you are tasked with upgrading PowerChat, you must evaluate which domain the feature belongs in. 
*   **Heavy Compute / Machine Learning / Networking:** Belongs in the External Bridge Process (Python) or a dedicated background C# thread.
*   **Deep OS Integration / File System / Windowing:** Belongs in the C# WinForms Host.
*   **Rich UI Rendering / Text Parsing / Animations:** Belongs in the WebView2 V8 Engine (JavaScript).

Do not attempt to perform heavy OS file I/O using JavaScript File System Access APIs when the C# host can do it natively and securely. Conversely, do not attempt to render Markdown string manipulation inside C# when the V8 engine can parse and paint it instantly via the DOM.

***

### Section 2: Win32 API Interop & Window Chrome Engineering

The PowerChat UI features a custom borderless window. The current implementation utilizes raw P/Invoke declarations to interface directly with `user32.dll` and `dwmapi.dll`. This is an area ripe for enterprise expansion if custom DPI scaling, multi-monitor dragging, or complex acrylic/mica blurs are required.

#### 2.1 The `WndProc` Pipeline
The `MainForm.cs` overrides the `WndProc(ref Message message)` method. This is the absolute lowest level of window message processing in the .NET framework. Windows sends messages (integers) to this function thousands of times a second.

The current implementation traps specific messages:
*   `WmNcCalcSize` (`0x0083`): The Non-Client Calculate Size message. By intercepting this and returning `IntPtr.Zero`, the application explicitly tells the Windows Desktop Window Manager (DWM) that the client area (the usable part of the app) encompasses the entire window, effectively deleting the standard Windows title bar while keeping the structural aero shadows.
*   `WmGetMinMaxInfo` (`0x0024`): The Get Minimum/Maximum Info message. When a borderless window is maximized, Windows assumes it has no borders and will stretch it over the Windows Taskbar. The codebase implements the `ApplyMaximizedWorkArea` method, invoking `MonitorFromWindow` to find the current screen, and `GetMonitorInfo` to calculate the `WorkArea` (the screen minus the taskbar). It then maps these coordinates into the `MinMaxInfo` struct.
*   `WmNcHitTest` (`0x0084`): The Non-Client Hit Test message. Because the window has no native borders, the OS doesn't know where the user can click to resize it. The code calculates the cursor position relative to a `ResizeBorder` constant (currently 7 pixels). It returns native constants like `HtTopLeft` (13) or `HtRight` (11) to trick Windows into rendering the native resizing cursors and handling the resize logic.

#### 2.2 DWM API Integration
The application interfaces with `dwmapi.dll` to utilize modern Windows 11 features:
*   `DwmSetWindowAttribute`: The code passes `dwmwaWindowCornerPreference` (33) to force rounded corners, and `dwmwaBorderColor` (34) set to `0xFFFFFFFE` to eliminate the default 1-pixel Windows 11 accent border. 

**Architectural Note for the AI Developer:** If you wish to implement Mica or Acrylic materials to the PowerChat background, you will need to extend this P/Invoke integration. You must import the `DWM_SYSTEMBACKDROP_TYPE` enum, set `dwmwaUseHostBackdropBrush` (38), and ensure the WinForms `BackColor` is set to a transparent key. You are encouraged to implement external libraries like `Microsoft.Windows.CsWin32` to handle these bindings professionally if the manual P/Invoke structures become too unwieldy.

***

### Section 3: Advanced WebView2 COM API and Interop Mechanics

PowerChat utilizes the `Microsoft.Web.WebView2.Core` and `Microsoft.Web.WebView2.WinForms` packages. The integration between the .NET CLR and the Chromium engine occurs across a Component Object Model (COM) boundary.

#### 3.1 Environment and Controller Initialization
In `MainForm.cs`, the initialization sequence is:
```csharp
var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileFolder);
await _webView.EnsureCoreWebView2Async(environment);
```
This is a critical enterprise pattern. By explicitly creating the `CoreWebView2Environment` and passing a specific `userDataFolder` (in this case, `%LOCALAPPDATA%\PowerChat\WebView2Profile`), the application ensures total isolation from the user's standard Microsoft Edge browser. Cookies, local storage, IndexedDB, and cache are sandboxed.

#### 3.2 CoreWebView2Settings Manipulation
Once initialized, the application accesses `_webView.CoreWebView2.Settings`. According to the XML documentation provided for the `Microsoft.Web.WebView2.Core` assembly, this exposes vast control over the Chromium instance.
The current code alters:
*   `AreDefaultContextMenusEnabled = true`
*   `AreDevToolsEnabled = true` (Crucial for debugging the UI)
*   `IsStatusBarEnabled = false` (Removes the URL hover tooltip at the bottom left)
*   `IsZoomControlEnabled = true`
*   `IsWebMessageEnabled = true`

**Architectural Note for the AI Developer:** To upgrade this to a tighter, more secure enterprise shell, you should consider exploring other properties defined in the `CoreWebView2Settings`. For example, you may wish to disable `AreDefaultScriptDialogsEnabled` and handle the `ScriptDialogOpening` event to render custom WinForms-based alert boxes instead of standard browser popups. You may also utilize `AreBrowserAcceleratorKeysEnabled = false` to completely lock down browser-specific hotkeys (like Ctrl+P, F5) while manually handling routing through the WinForms `ProcessCmdKey` override.

#### 3.3 The Virtual Host Folder Mapping
The code utilizes:
`core.SetVirtualHostNameToFolderMapping("powerchat.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);`

This maps the physical `wwwroot` directory to a virtual HTTPS domain. 
*   **Why this matters:** If you load a local HTML file via a `file:///` URI, Chromium implements extremely strict Cross-Origin Resource Sharing (CORS) policies. Fetch requests, ES6 module imports, and service workers will fail. By mounting the folder to a virtual scheme, Chromium treats the local files as a secure, top-level HTTPS server. 
*   **Future Development:** If you decide to implement a massive frontend framework like React, Vue, or Svelte, you will output your built assets to the `wwwroot` folder, and the WebView2 control will seamlessly serve them over `https://powerchat.local` with full module support.

#### 3.4 IPC via WebMessages
The communication between C# and JavaScript uses `PostWebMessageAsJson` and the `WebMessageReceived` event.
*   **C# to JS:** `_webView.CoreWebView2.PostWebMessageAsJson(json);` This serializes a C# object, crosses the COM boundary, and fires a `message` event on the `window.chrome.webview` object in JavaScript.
*   **JS to C#:** `window.chrome.webview.postMessage(payload);` This triggers the `CoreOnWebMessageReceived` event in C#. The C# backend then uses `System.Text.Json.JsonDocument` to parse the `e.WebMessageAsJson` string.

**Architectural Note for the AI Developer:** JSON serialization across the COM boundary has a latency cost, especially for large payloads. Currently, attachments (images) are converted to Base64 strings in JavaScript and sent via `postMessage`. For massive files (e.g., a 100MB PDF), this will block the UI thread and crash the V8 engine due to string length limits. To achieve enterprise-tier performance, you must upgrade this pipeline. You should implement a local ASP.NET Core Kestrel server running on a background thread within the WinForms app, allowing the frontend to use standard `fetch()` API calls with `multipart/form-data` to stream large binary assets directly to the disk, bypassing the WebView2 COM IPC bottleneck entirely.

***

### Section 4: Event Sourcing, State Management, and the Queue Machine

PowerChat operates entirely on a file-based state machine located in the `bridge/` directory. This acts as the sole source of truth between the UI, the C# backend, and the external AI agent.

#### 4.1 The Persistence Layer: `messages.jsonl`
Currently, the application relies on a flat JSON Lines (`.jsonl`) file stored in `%LOCALAPPDATA%`. Every message (User or AI) is appended to this file. 
When the application boots, `LoadMessageHistoryAsync()` reads this file line by line, parsing each line as a distinct JSON object.

**Neutral Assessment:** A flat `.jsonl` file is exceptionally fast for appending data and guarantees corruption resistance (if a write is interrupted, only the last line is mangled, which the current `try/catch (JsonException)` safely ignores). However, it lacks relational integrity and querying speed. If a conversation reaches 50,000 messages, reading the entire file on boot will block the I/O thread.

**Architectural Note for the AI Developer:** To elevate PowerChat to enterprise status, you must tear out the `.jsonl` system and implement a true relational database. You are encouraged to integrate `Microsoft.EntityFrameworkCore.Sqlite` or `Dapper`. 
*   Create a SQLite database file (`powerchat.db`) in the session folder.
*   Create a `Messages` table with columns: `Id` (TEXT PRIMARY KEY), `Role` (TEXT), `Content` (TEXT), `CreatedAt` (DATETIME), `Priority` (TEXT), `InReplyTo` (TEXT).
*   Create an `Attachments` table with columns: `Id` (TEXT), `MessageId` (FOREIGN KEY), `FileName` (TEXT), `MimeType` (TEXT), `ByteSize` (INTEGER), `LocalPath` (TEXT).
*   This will allow instantaneous pagination, complex querying, and future integration with local Vector Embeddings for semantic search across past conversations.

#### 4.2 The Authoritative Task Ledger: `pending-user-messages.json`
The bridge protocol dictates that `pending-user-messages.json` is the sole source of truth for the AI agent's workload.
The `RebuildPendingQueueAsync()` method in C# performs the following logic:
1.  Iterates through all historical messages.
2.  Places all `user` messages into a Dictionary.
3.  Evaluates all `assistant` messages. If an assistant message contains `completeTask: true`, it looks up the `inReplyTo` ID in the Dictionary and removes the corresponding user message.
4.  Sorts the remaining user messages by Priority (`/critical` commands first), then by chronological `CreatedAt` timestamp.
5.  Atomically writes the output to `pending-user-messages.json`.

**Architectural Note for the AI Developer:** This current implementation requires a full rebuild of the queue from the entire history log upon every single interaction. This is O(N) complexity where N is the total number of messages in the chat. As you transition to a SQLite backend, you must refactor this into an O(1) database query: `SELECT * FROM Messages WHERE Role = 'user' AND Id NOT IN (SELECT InReplyTo FROM Messages WHERE Role = 'assistant' AND CompleteTask = 1) ORDER BY Priority DESC, CreatedAt ASC`.

#### 4.3 Atomic File System I/O
The communication between C# and the Python bridge relies on concurrent file access. To prevent file locking collisions (where C# tries to read a file that Python is currently writing to), the system enforces Atomic Writes.

In C#, `WriteJsonAtomicAsync` works as follows:
```csharp
var temporary = path + ".tmp";
await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false));
File.Move(temporary, path, true);
```
In Python, `write_json_atomic` works identically:
```python
temporary = path.with_suffix(path.suffix + ".tmp")
temporary.write_text(json.dumps(value), encoding="utf-8")
temporary.replace(path)
```
This guarantees that the `.json` file is never in an incomplete state. The OS-level `File.Move` or `os.replace` operation swaps the file pointer atomically. 

When the C# `_bridgeTimer` attempts to read `assistant-reply.json`, it uses `FileShare.ReadWrite | FileShare.Delete`. If it encounters an `IOException` (e.g., the file is momentarily locked during the atomic swap), it safely swallows the exception and retries on the next 450ms tick.

***

### Section 5: Client-Side DOM Architecture and Frontend Evolution

The current `wwwroot` utilizes Vanilla HTML, CSS, and JavaScript. It is a highly optimized, zero-dependency stack.

#### 5.1 CSS Grid and Layout Topography
The UI relies heavily on CSS Variables (`--bg`, `--surface`, `--cyan`) and CSS Grid.
The `.workspace` class dictates a two-column layout: `grid-template-columns: 264px minmax(0, 1fr)`.
When the F11 fullscreen mode is triggered, `.app-frame.console-fullscreen .workspace` overrides this to `grid-template-columns: minmax(0, 1fr)`, instantly collapsing the sidebar and giving the chat view maximum screen real estate.

#### 5.2 The Render Loop and Mutation
The `renderMessage(message, options)` function in `app.js` manually constructs DOM nodes using `document.createElement('article')` and raw `.innerHTML` injection. 
It processes the Base64 attachments, generates `<figure>` and `<img>` tags for images, and `<div class="message-file">` tags for binary files.

**Neutral Assessment:** Manual DOM manipulation via `.innerHTML` is fast, but as the complexity of the application grows (e.g., implementing inline code execution, interactive graphs, or dynamic AI tool calls), it becomes incredibly difficult to manage state, track event listeners, and prevent memory leaks.

**Architectural Note for the AI Developer:** To achieve enterprise-tier architecture, you should abandon the vanilla JS approach. You are strongly encouraged to implement a component-based UI framework. 
*   **Recommendation:** Integrate **React** with **TypeScript** via Vite. 
*   Create a build step in the `.csproj` file that triggers `npm run build` prior to compiling the C# backend, copying the `dist` folder into `wwwroot`.
*   This will allow you to build complex, state-driven components (e.g., `<MessageBubble />`, `<CodeBlock language="python" />`, `<TaskQueue />`).
*   It will drastically simplify the `handleHostMessage` switch statement by mapping IPC payloads directly to a global state store (like Redux or Zustand).

#### 5.3 Text Parsing and Markdown
The current `renderMarkdown(source)` function uses multiple Regular Expressions to convert markdown syntax to HTML.
It extracts code blocks into a temporary array (`@@CODEBLOCK_N@@`), processes headers and bold text, manages sequential lists, and then re-injects the code blocks safely escaped.

**Architectural Note for the AI Developer:** Regex is insufficient for enterprise-grade Markdown parsing (it fails on nested quotes, complex tables, and edge-case syntax). You must rip out the custom regex implementation and import a robust library such as `marked.js` or `markdown-it`. 
Furthermore, you must implement syntax highlighting. Integrate `Prism.js` or, for an ultimate enterprise feel, integrate the `Monaco Editor` (the core of VS Code) to render the code blocks within the chat interface, allowing the user to edit and copy code directly from the AI's response.

#### 5.4 Draft and Attachment State
The `app.js` file handles prompt persistence via `localStorage.setItem(draftStorageKey(), text)`. This happens synchronously on every keystroke. Concurrently, it sets a debounced timer (`setTimeout` for 90ms) to send the text to the C# backend via `type: 'saveDraftText'`.
If the user drops an image, JavaScript uses `FileReader.readAsDataURL()` to convert it to Base64, stores it in the `state.attachments` array, and sends a `type: 'persistDraftAttachment'` payload to C#. C# decodes the Base64 and saves it to the `draft-attachments/` folder.

This dual-layer persistence ensures that if the app is abruptly killed, the local `draft.json` on the disk contains the physical file pointers, while `localStorage` acts as a fail-safe for the raw text.

***

### Section 6: Telemetry, Observability, and Enterprise Diagnostics

Currently, PowerChat operates silently. There are no log files, no crash dumps, and no performance metrics. For an enterprise-tier tool, this is unacceptable.

#### 6.1 Implementing Structured Logging
You must implement a robust logging framework within the C# host.
*   **Recommendation:** Integrate **Serilog**.
*   Configure Serilog to write to a rolling file sink in `%LOCALAPPDATA%\PowerChat\Logs\log-.txt`.
*   Log every IPC boundary crossing. When `BridgeTimerOnTick` processes a file, log the time it took. When `CoreOnWebMessageReceived` handles a payload, log the payload size and type.
*   Implement `try/catch` blocks around all File I/O and log exceptions with full stack traces.

#### 6.2 Monitoring WebView2 Diagnostics
The `CoreWebView2` API exposes deep diagnostic events.
*   You must subscribe to the `ProcessFailed` event (`_webView.CoreWebView2.ProcessFailed`). If the Chromium Renderer process crashes (e.g., due to an Out of Memory error from a massive base64 image), you must catch this event, log the `ProcessFailedKind` and `ExitCode`, and prompt the user or attempt an automatic recovery via `_webView.Reload()`.
*   Subscribe to `Environment.BrowserProcessExited`. If the overarching Edge process dies, the application must shut down gracefully rather than hanging indefinitely.

***

### Section 7: Expanding the Bridge Protocol for Multi-Agent Orchestration

The current bridge design assumes a 1-to-1 relationship between the PowerChat UI and a single AI Agent polling the `pending-user-messages.json` file. The roadmap dictates expanding this to support Multi-Agent Orchestration.

#### 7.1 Multiplexing Responses
To support multiple agents (e.g., a "Coder", a "Reviewer", and a "Manager"), the bridge must be modified.
*   Instead of a single `assistant-reply.json`, the WinForms `_bridgeTimer` must be upgraded to scan the `bridge/` directory for *any* file matching the pattern `reply_*.json`.
*   The JSON schema must be expanded to include an `agentId` and `agentName` field.
*   The C# backend must parse these multiplexed files, append them to `messages.jsonl`, and dispatch them to the UI.
*   The UI must be updated to support different avatar colors or labels based on the `agentName` field, allowing the user to visually distinguish which AI in the swarm is speaking.

#### 7.2 The Watchdog Timer
The current `bridge_wait.py` implements a hard 60-minute timeout based on `expiresAt`. If expanding to an enterprise backend, this Python script should be replaced with a robust background service or a C# BackgroundWorker that establishes a WebSocket connection with the AI provider, eliminating the need for disk-polling entirely when running in remote environments, while maintaining the disk-bridge as a fallback for local offline LLMs.

***

### Section 8: Build Pipeline and CI/CD Analysis

The provided directory structure includes `.build-check/obj/` and `obj/Release/net8.0-windows/`. 

#### 8.1 MSBuild and `project.assets.json`
The `.csproj` utilizes the modern SDK style. During the `dotnet build` phase, MSBuild generates `project.assets.json`. This file acts as the authoritative graph of all NuGet dependencies.
If you review the `project.assets.json` provided, you will see it resolves `Microsoft.Web.WebView2/1.0.4078.44`. It explicitly pulls in architecture-specific native loaders:
*   `runtimes/win-arm64/native/WebView2Loader.dll`
*   `runtimes/win-x64/native/WebView2Loader.dll`
*   `runtimes/win-x86/native/WebView2Loader.dll`

When compiling for Release, MSBuild copies the correct `WebView2Loader.dll` alongside the `PowerChat.exe`. 

#### 8.2 Assembly Information and Metadata
The `WebView2PowerChat.AssemblyInfo.cs` is auto-generated by the MSBuild `WriteCodeFragment` task.
```csharp
[assembly: System.Reflection.AssemblyCompanyAttribute("PowerChat")]
[assembly: System.Reflection.AssemblyConfigurationAttribute("Release")]
[assembly: System.Reflection.AssemblyFileVersionAttribute("1.0.0.0")]
```
**Architectural Note for the AI Developer:** If you are building a CI/CD pipeline (e.g., GitHub Actions), you should pass version parameters directly to the `dotnet build` command (`-p:Version=1.2.3`) to dynamically stamp the executables with proper semantic versioning, which is critical for enterprise deployment and telemetry tracking.

***

### Section 9: Advanced Feature Implementation Strategies

To fully execute your mandate of maximizing functionality, consider the following specific implementation strategies for upcoming features.

#### 9.1 Implementing Global Hotkeys (Keyboard Hooks)
The user has requested `Ctrl+Shift+Enter` to send messages. Currently, this is bound to the DOM `keydown` event in JavaScript. If the user clicks *outside* the WebView2 control (e.g., on the custom WinForms title bar), the DOM loses focus, and the JavaScript event listener will fail to capture the keystroke.
*   **The Enterprise Fix:** You must implement a low-level Windows keyboard hook (`SetWindowsHookEx` in `user32.dll`) or override `ProcessCmdKey` at the `MainForm` level to capture keyboard chords globally across the entire application thread, regardless of whether the DOM has focus.

#### 9.2 Migrating from Base64 to Blob Storage
Currently, when a user drops an image, it is converted to a Base64 string in `app.js` and pushed through `postMessage`.
*   **The Enterprise Fix:** Use the `WebView2.CoreWebView2.SetVirtualHostNameToFolderMapping` to map the `attachments/` directory to `https://powerchat-assets.local/`. 
*   When a user drops an image, instead of reading it as a Data URL, save it directly via a local server or a specialized bridge IPC mechanism, and simply pass the filename to the C# backend.
*   The C# backend saves the file, and the UI simply renders `<img src="https://powerchat-assets.local/image.png">`. This reduces memory overhead on the V8 engine by 99% and eliminates the ~33% size inflation inherent to Base64 encoding.

***

### Section 10: Complete Method and API Reference Matrix

For your convenience during refactoring, here is the conceptual map of the `MainForm.cs` methods and their operational burdens.

*   `public MainForm()`: Constructor. Handles path resolution, `bridge` discovery, and initial component attachment.
*   `LoadDraftStateAsync()`: Disk I/O. Deserializes `draft.json`. Safe against corruption.
*   `BuildDraftPayloadAsync()`: Disk I/O & Memory. Reads physical files, converts to Base64. **Bottleneck candidate for large files.**
*   `SaveDraftTextAsync(JsonElement root)`: IPC handler. Updates local string, flushes to disk.
*   `PersistDraftAttachmentAsync(JsonElement root)`: IPC handler. Decodes Base64, writes binary to disk.
*   `ResetAiTimeoutAfterUserMessageAsync()`: State mutation. Reads, modifies, and atomically writes `ai-status.json`.
*   `RebuildPendingQueueAsync()`: Heavy Disk I/O. Parses entire history, aggregates state, writes queue. **Refactoring target for SQLite migration.**
*   `DetermineMessagePriority(string text)`: String parsing. Checks for `/critical`.
*   `OnLoadAsync(...)`: Startup sequence. Ensures rules exist, writes genesis `current-chat.json`, mounts WebView2.
*   `CheckAiStatusAsync()`: Polled function. Evaluates `ExpiresAt`. Triggers forced disconnects.
*   `PublishAiStatusAsync(...)`: IPC handler. Pushes state to V8 DOM.
*   `LoadMessageHistoryAsync()`: Disk I/O. Reads `.jsonl`.
*   `CoreOnWebMessageReceived(...)`: The main router for all JavaScript-to-C# commands.
*   `HandleWindowAction(JsonElement root)`: Win32 interop router (Minimize, Maximize, Drag).
*   `ToggleConsoleFullscreen()`: Modifies WinForms window state, TopMost attributes, and restores bounds.
*   `SaveUserMessageAsync(JsonElement root)`: Primary write sequence. Handles attachment decoding, message logging, queue rebuilding, and timeout resetting.
*   `BridgeTimerOnTick(...)`: The heartbeat. Reads `assistant-reply.json` safely.

***

### Conclusion of AI Guidebook

You now possess the complete architectural, operational, and philosophical blueprint of the PowerChat system. You understand the boundaries between the .NET host, the V8 renderer, and the external Python bridge. You understand the limitations of the current file-based IPC and `.jsonl` storage mechanisms, and you have been provided with clear, aggressive strategies to refactor them into an enterprise-tier relational database system.

Your mandate is clear: Do not be constrained by simplicity. Prioritize extreme performance, vast feature sets, and absolute productivity. You are authorized to restructure, integrate massive external frameworks, and elevate this application to its maximum potential.
