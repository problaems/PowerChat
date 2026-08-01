# PowerChat Guidebook for Humans

PowerChat is a native Windows chat shell built with WinForms and Microsoft WebView2. This guide explains what it is, how to run it, how sessions are stored, how the AI bridge works, and how to use the interface.

## 1. Introduction to PowerChat

Welcome to the official documentation for PowerChat, a professional, native borderless Windows chat shell built using Windows Forms (WinForms) and Microsoft WebView2. Designed specifically to act as a robust local node for AI agent interactions, PowerChat bridges the gap between web-based AI capabilities and deep local system integration. 

PowerChat is not merely a web wrapper; it is an enterprise-grade hybrid application. By utilizing WebView2, it leverages the modern Edge Chromium rendering engine for a fluid, responsive, and aesthetically premium user interface, while the WinForms backend provides uncompromising, direct access to the local Windows file system, Win32 APIs, and native window management capabilities. This architecture ensures that PowerChat remains lightweight compared to Electron-based alternatives, while offering superior integration with the Windows Desktop Environment.

This manual is designed for the human operator—the user who will install, configure, and interact with the PowerChat interface on a daily basis. For bridge protocol details, state machine requirements, and architecture notes for AI agents, see the separate [Guidebook for Agents](guidebook-for-agents.md).

## 2. System Requirements and Prerequisites

To successfully compile, launch, and operate PowerChat, your system must meet the following enterprise prerequisites:

### 2.1 Hardware Requirements
*   **Processor:** 64-bit architecture (x64 or ARM64) with a minimum of 4 cores recommended for smooth WebView2 rendering alongside background AI bridge tasks.
*   **Memory (RAM):** Minimum 4 GB RAM. 8 GB or higher is recommended when running local AI agents or extensive Python bridge scripts concurrently.
*   **Storage:** At least 500 MB of free disk space for the .NET SDK, application binaries, and the local `AppData` session storage where chat logs and attachments are serialized.

### 2.2 Software Prerequisites
*   **Operating System:** Windows 10 (Version 1809 or later) or Windows 11. The application utilizes native Windows Desktop Manager (DWM) APIs for borderless window rendering and Aero Snap support, which require modern Windows versions.
*   **.NET 8.0 SDK:** The application is built on the `.net8.0-windows` target framework. You must have the .NET 8.0 SDK installed to compile and run the source code.
*   **Microsoft Edge WebView2 Runtime:** PowerChat relies on the WebView2 runtime. Windows 11 includes this by default. Windows 10 users may need to download the Evergreen Bootstrapper from the Microsoft website.
*   **Python 3.9+ (Optional but highly recommended):** Required for running the `bridge_wait.py` script, which facilitates continuous polling and connection between an external AI agent and the local PowerChat instance.

## 3. Installation and Build Process

PowerChat is designed to be easily compilable from source, ensuring that enterprise environments can audit, build, and deploy the application securely.

### 3.1 Restoring the Repository
Ensure that the entire `WebView2PowerChat` directory is extracted to a location on your local drive with read/write permissions. The directory structure includes the main C# project files, the `wwwroot` frontend assets, and the `bridge` directory.

### 3.2 Compiling and Running the Application
Open a terminal (PowerShell or Command Prompt) with standard user privileges. Navigate to the root of the `WebView2PowerChat` directory. Execute the following command to build and launch the application:

```powershell
dotnet run --project WebView2PowerChat.csproj
```

The .NET CLI will automatically restore the required NuGet packages—specifically `Microsoft.Web.WebView2` (Version 1.0.4078.44)—compile the WinForms backend, copy the `wwwroot` assets to the output directory, and launch the PowerChat executable.

### 3.3 Session Initialization and AppData Storage
Upon launching, PowerChat automatically initializes a unique session. It creates a dedicated chat folder located in your local Application Data directory:

`%LOCALAPPDATA%\PowerChat\chat_yyyyMMdd_HHmmss_ffff`

This folder acts as the absolute source of truth for your current conversation. It contains:
*   `messages.jsonl`: A JSON Lines file containing the complete history of the conversation. Every user message and AI response is appended here instantly.
*   `attachments/`: A directory storing all images and files dropped into the chat.
*   `draft-attachments/`: A directory storing temporary files linked to an unsent prompt buffer.
*   `draft.json`: A persistent state file ensuring that if you close PowerChat mid-thought, your text and attachments are restored upon the next launch.

## 4. Connecting the AI Agent (The Bridge)

PowerChat uses a unique, file-system-based "Bridge" architecture to communicate with AI agents. Rather than exposing a local web server (which can trigger enterprise firewall alerts), PowerChat reads and writes to a designated `bridge/` directory. 

### 4.1 The Bridge Directory
By default, the bridge is located at `WebView2PowerChat/bridge/`. You can override this location by setting a system environment variable named `POWERCHAT_BRIDGE` to your desired absolute path.

### 4.2 The `bridge_wait.py` Script
To facilitate communication between a web-based or local AI agent and PowerChat, you must run the included Python script:

```bash
python bridge_wait.py
```

This script continuously monitors the `bridge/latest-user-message.json` and `bridge/ai-status.json` files. It outputs JSON-formatted events to `stdout` whenever a new message arrives from the user, or when the connection state changes (e.g., a timeout). The AI agent's environment should pipe this standard output to stay synchronized with the PowerChat UI.

### 4.3 Agent Arming and the Connection Message
When an AI agent connects to the bridge, it is strictly required to read `POWERCHAT_AI_RULE.md`. To signal to the human operator that the AI is active, the agent must write a specific connection message into the bridge, which will instantly reflect in the UI. 

Once connected, the "DISCONNECTED" pill in the PowerChat header will turn green and display "READY", alongside a 60-minute countdown timer.

## 5. Using the PowerChat Interface

The PowerChat UI is engineered for maximum productivity, merging command-line aesthetics with modern chat interface paradigms.

### 5.1 The Title Bar and Window Controls
PowerChat features a completely custom, dark-themed title bar while retaining native Windows windowing capabilities (Aero Snap, Win+Arrow shortcuts). 
*   **Drag Region:** Click and drag anywhere on the empty space of the title bar to move the window.
*   **Status Indicator:** Displays `BOOTING`, `DISCONNECTED`, or `READY` depending on the AI's presence.
*   **Window Controls:** Standard Minimize, Maximize/Restore, and Close buttons. 
*   **F11 Fullscreen:** Pressing `F11` strips away the Windows frame and sidebar entirely, putting the chat into a distraction-free, terminal-like fullscreen mode. Press `F11` or `Esc` to exit.

### 5.2 The Sidebar (Operator Console)
The left-hand sidebar acts as your mission control for the current session.
*   **New Prompt (Ctrl+L):** Instantly focuses your cursor back to the input box.
*   **Session Panel:** Displays the unique ID of the current chat. Clicking the file path instantly copies the `%LOCALAPPDATA%` directory path to your clipboard.
*   **Open Chat Folder:** Launches Windows Explorer directly into the current session's backend data folder.
*   **Current Work:** Displays real-time status updates from the AI agent (e.g., "Reading files...", "Generating code...", "Connected and monitoring the queue").
*   **Task Queue:** A powerful feature that separates PowerChat from standard chat interfaces. Every message you send becomes a "Task". As the AI answers them, they are marked as complete. You can click any task in this queue to instantly scroll to that specific message in the main chat view.

### 5.3 The Main Chat View and View Modes
The center column displays the conversation. Messages from the `OPERATOR` (You) are styled with amber/blue accents, while `ASSISTANT` (AI) messages utilize a green/teal palette.
*   **View Modes:** Located in the top right, clicking `VIEW: FULL` cycles through different visibilities:
    *   `FULL`: Shows all messages.
    *   `QUEUE`: Shows only your messages that have not yet been answered by the AI.
    *   `ANSWERED`: Shows only your messages that the AI has completed.
    *   `ANSWERED + AI`: Shows your answered messages paired directly with the AI's responses.
*   **Message Actions:** Hovering over any message reveals a `COPY` button. AI messages that complete a task will feature a `SOURCE TASK` button to jump back to your original prompt.
*   **Markdown Support:** The UI natively renders complex Markdown, including headers, bold/italic text, blockquotes, lists, and syntax-highlighted code blocks.

### 5.4 The Prompt Buffer (Composer)
Located at the bottom of the screen, the Composer is where you construct your inputs.
*   **Draft Persistence:** Everything you type is instantly saved. If you close PowerChat or your PC crashes, your text and attachments will be waiting for you when you return.
*   **Attachments:** You can drag and drop images directly into the PowerChat window, or click the `Attach` button (Ctrl+U). PowerChat supports up to 6 files per turn, with a 15 MB limit per file. Images will display a preview thumbnail.
*   **Sending:** Press `Ctrl + Shift + Enter` or click the Send button to dispatch your message.

### 5.5 Slash Commands and Priority Queuing
PowerChat implements a strict queuing system. If you send multiple messages rapidly, they queue up.
*   **/critical:** Typing `/critical` at the start of your message elevates its priority. The task will glow red in your queue, and the AI agent is programmed to bypass all regular tasks to handle `/critical` tasks first.

### 5.6 Data Export
Clicking the `EXPORT` button opens an enterprise-grade modal dialog over the chat.
*   **Filters:** You can filter the export by Role (Operator, AI, Both), Format (Markdown, Text, JSON), and Scope (All, Date/Time range, Latest N messages, or an Approximate Token Budget).
*   **Preview:** The modal provides live metrics: Selected messages, raw character count, approximate token count (for LLM context windows), and final output byte size.
*   **Save:** Uses the native Windows Save File Dialog to export your data cleanly.

## 6. Future Plans and Enterprise Roadmap

PowerChat represents the foundational node of a much larger vision for local AI orchestration. The architecture has been deliberately decoupled (Frontend HTML/JS -> WinForms Host -> JSON Bridge) to allow for aggressive expansion.

**Roadmap Features:**
1.  **SQLite Migration:** Transitioning from `messages.jsonl` to a robust SQLite database for instantaneous querying of millions of messages, allowing for full-text search across historical sessions.
2.  **Multi-Agent Orchestration:** Upgrading the bridge to support multiple `assistant-reply.json` streams, allowing a "Manager AI" to delegate tasks to "Coder AI" and "Reviewer AI" within the same PowerChat thread.
3.  **Semantic Search & Vectorization:** Implementing local embeddings so the AI can automatically query past sessions without the human operator needing to explicitly link old files.
4.  **Plugin Ecosystem:** Expanding the `window.chrome.webview.postMessage` architecture to allow the AI to trigger local PowerShell scripts, docker container builds, or git commits directly through the PowerChat WinForms host.

---
---
