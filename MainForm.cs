using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WebView2PowerChat;

public sealed class MainForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcCalcSize = 0x0083;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeBorder = 7;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsThickFrame = 0x00040000;
    private const int WsSysMenu = 0x00080000;

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _bridgeTimer = new() { Interval = 450 };
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _sessionId;
    private readonly string _appRoot;
    private readonly string _chatFolder;
    private readonly string _attachmentsFolder;
    private readonly string _messagesPath;
    private readonly string _bridgeFolder;
    private readonly string _assistantReplyPath;
    private readonly string _aiStatusPath;
    private readonly string _pendingQueuePath;
    private readonly string _draftPath;
    private readonly string _draftAttachmentsFolder;
    private string _draftText = string.Empty;
    private readonly List<DraftAttachmentState> _draftAttachments = [];
    private bool _webReady;
    private bool _processingReply;
    private bool _isConsoleFullscreen;
    private Rectangle _fullscreenRestoreBounds;
    private FormWindowState _fullscreenRestoreState = FormWindowState.Normal;
    private string? _lastAiState;
    private DateTime _lastAiStatusWriteUtc;

    public MainForm()
    {
        _appRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerChat");
        _bridgeFolder = ResolveBridgeFolder();

        if (!TryResumeCurrentSession(out _sessionId, out _chatFolder, out _messagesPath))
        {
            _sessionId = $"chat_{DateTime.Now:yyyyMMdd_HHmmss_ffff}";
            _chatFolder = Path.Combine(_appRoot, _sessionId);
            _messagesPath = Path.Combine(_chatFolder, "messages.jsonl");
        }

        _attachmentsFolder = Path.Combine(_chatFolder, "attachments");
        _assistantReplyPath = Path.Combine(_bridgeFolder, "assistant-reply.json");
        _aiStatusPath = Path.Combine(_bridgeFolder, "ai-status.json");
        _pendingQueuePath = Path.Combine(_bridgeFolder, "pending-user-messages.json");
        _draftPath = Path.Combine(_chatFolder, "draft.json");
        _draftAttachmentsFolder = Path.Combine(_chatFolder, "draft-attachments");

        Text = "PowerChat";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);
        Size = new Size(1180, 780);
        BackColor = Color.FromArgb(7, 10, 16);
        Padding = new Padding(1);
        DoubleBuffered = true;
        KeyPreview = true;

        Controls.Add(_webView);
        Load += OnLoadAsync;
        Shown += (_, _) => ApplyRoundedCorners();
        _bridgeTimer.Tick += BridgeTimerOnTick;
        Resize += (_, _) => PublishWindowState();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // FormBorderStyle.None removes the native frame flags Windows uses
            // for Aero Snap, Win+Arrow, edge resizing and normal maximize
            // transitions. Restore those capabilities without restoring the
            // stock title bar; the HTML title bar remains the visible chrome.
            parameters.Style |= WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu;
            return parameters;
        }
    }

    private async Task LoadDraftStateAsync()
    {
        _draftText = string.Empty;
        _draftAttachments.Clear();
        if (!File.Exists(_draftPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_draftPath);
            var state = JsonSerializer.Deserialize<DraftFileState>(json, _jsonOptions);
            if (state is null)
            {
                return;
            }

            _draftText = state.Text ?? string.Empty;
            _draftAttachments.AddRange((state.Attachments ?? []).Where(attachment => File.Exists(attachment.Path)));
        }
        catch (IOException)
        {
            // A missing or interrupted draft should not block the conversation.
        }
        catch (JsonException)
        {
            // Keep the app usable even if a draft file was externally damaged.
        }
    }

    private async Task<object> BuildDraftPayloadAsync()
    {
        var attachments = new List<object>();
        foreach (var attachment in _draftAttachments.Where(attachment => File.Exists(attachment.Path)))
        {
            var bytes = await File.ReadAllBytesAsync(attachment.Path);
            attachments.Add(new
            {
                draftId = attachment.Id,
                name = attachment.Name,
                storedName = attachment.StoredName,
                path = attachment.Path,
                mime = attachment.Mime,
                size = attachment.Size,
                dataUrl = $"data:{attachment.Mime};base64,{Convert.ToBase64String(bytes)}"
            });
        }

        return new
        {
            text = _draftText,
            attachments,
            path = _draftPath,
            attachmentFolder = _draftAttachmentsFolder
        };
    }

    private async Task SaveDraftTextAsync(JsonElement root)
    {
        _draftText = root.TryGetProperty("text", out var textNode)
            ? textNode.GetString() ?? string.Empty
            : string.Empty;
        await WriteDraftStateAsync();
        await PostToWebAsync(new { type = "draftSaved", updatedAt = DateTimeOffset.Now });
    }

    private async Task PersistDraftAttachmentAsync(JsonElement root)
    {
        var draftId = root.TryGetProperty("draftId", out var idNode) && !string.IsNullOrWhiteSpace(idNode.GetString())
            ? idNode.GetString()!
            : $"draft_{Guid.NewGuid():N}";
        var name = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "attachment.bin" : "attachment.bin";
        var mime = root.TryGetProperty("mime", out var mimeNode) ? mimeNode.GetString() ?? "application/octet-stream" : "application/octet-stream";
        var dataUrl = root.TryGetProperty("dataUrl", out var dataNode) ? dataNode.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.Contains(','))
        {
            throw new InvalidDataException("Draft attachment data is missing.");
        }

        var existing = _draftAttachments.FirstOrDefault(attachment => attachment.Id == draftId);
        if (existing is not null && File.Exists(existing.Path))
        {
            File.Delete(existing.Path);
            _draftAttachments.Remove(existing);
        }

        var storedName = $"{MakeSafeFileName(draftId)}_{MakeSafeFileName(name)}";
        var path = Path.Combine(_draftAttachmentsFolder, storedName);
        var bytes = Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
        await File.WriteAllBytesAsync(path, bytes);
        var saved = new DraftAttachmentState(draftId, name, storedName, path, mime, bytes.LongLength);
        _draftAttachments.Add(saved);
        await WriteDraftStateAsync();
        await PostToWebAsync(new
        {
            type = "draftAttachmentSaved",
            attachment = new
            {
                draftId,
                name,
                storedName,
                path,
                mime,
                size = bytes.LongLength
            }
        });
    }

    private async Task ResetAiTimeoutAfterUserMessageAsync()
    {
        if (!File.Exists(_aiStatusPath))
        {
            return;
        }

        var status = await ReadAiStatusAsync();
        if (!string.Equals(status.State, "connected", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        status = status with
        {
            UpdatedAt = now,
            ExpiresAt = now.AddHours(1)
        };

        await WriteJsonAtomicAsync(_aiStatusPath, new
        {
            state = status.State,
            surface = "PowerChat",
            detail = status.Detail,
            currentUserMessageId = status.CurrentUserMessageId,
            updatedAt = status.UpdatedAt,
            expiresAt = status.ExpiresAt
        });

        _lastAiState = status.State;
        _lastAiStatusWriteUtc = File.GetLastWriteTimeUtc(_aiStatusPath);
        await PostToWebAsync(new
        {
            type = "aiStatus",
            state = status.State,
            detail = status.Detail,
            currentUserMessageId = status.CurrentUserMessageId,
            updatedAt = status.UpdatedAt,
            expiresAt = status.ExpiresAt
        });
    }

    private async Task RemoveDraftAttachmentAsync(JsonElement root)
    {
        var draftId = root.TryGetProperty("draftId", out var idNode) ? idNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return;
        }

        var existing = _draftAttachments.FirstOrDefault(attachment => attachment.Id == draftId);
        if (existing is not null)
        {
            if (File.Exists(existing.Path))
            {
                File.Delete(existing.Path);
            }
            _draftAttachments.Remove(existing);
            await WriteDraftStateAsync();
        }
    }

    private Task WriteDraftStateAsync()
    {
        return WriteJsonAtomicAsync(_draftPath, new
        {
            updatedAt = DateTimeOffset.Now,
            text = _draftText,
            attachments = _draftAttachments
        });
    }

    private async Task ClearDraftAsync()
    {
        foreach (var attachment in _draftAttachments)
        {
            if (File.Exists(attachment.Path))
            {
                File.Delete(attachment.Path);
            }
        }
        _draftAttachments.Clear();
        _draftText = string.Empty;
        if (File.Exists(_draftPath))
        {
            File.Delete(_draftPath);
        }
        await PostToWebAsync(new { type = "draftCleared" });
    }

    private async Task RebuildPendingQueueAsync()
    {
        var pending = new Dictionary<string, PendingQueueMessage>(StringComparer.Ordinal);
        if (File.Exists(_messagesPath))
        {
            foreach (var line in await File.ReadAllLinesAsync(_messagesPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var role = root.TryGetProperty("role", out var roleNode) ? roleNode.GetString() : null;
                    if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = root.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        var text = root.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
                        var priority = root.TryGetProperty("priority", out var priorityNode)
                            ? priorityNode.GetString() ?? DetermineMessagePriority(text)
                            : DetermineMessagePriority(text);
                        var createdAt = root.TryGetProperty("createdAt", out var createdNode)
                                        && DateTimeOffset.TryParse(createdNode.GetString(), out var parsedCreated)
                            ? parsedCreated
                            : DateTimeOffset.MinValue;
                        var attachments = root.TryGetProperty("attachments", out var attachmentsNode)
                            ? attachmentsNode.Clone()
                            : JsonDocument.Parse("[]").RootElement.Clone();

                        pending[id] = new PendingQueueMessage(
                            id,
                            text,
                            priority,
                            attachments,
                            createdAt,
                            _sessionId);
                    }
                    else if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        var completesTask = !root.TryGetProperty("completeTask", out var completeNode)
                                            || completeNode.ValueKind != JsonValueKind.False;
                        var inReplyTo = root.TryGetProperty("inReplyTo", out var replyNode)
                            ? replyNode.GetString()
                            : null;
                        if (completesTask && !string.IsNullOrWhiteSpace(inReplyTo))
                        {
                            pending.Remove(inReplyTo);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore a damaged trailing line while preserving every valid queued task.
                }
            }
        }

        var ordered = pending.Values
            .OrderBy(message => string.Equals(message.Priority, "critical", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(message => message.CreatedAt)
            .ToList();

        await WriteJsonAtomicAsync(_pendingQueuePath, new
        {
            sessionId = _sessionId,
            updatedAt = DateTimeOffset.Now,
            count = ordered.Count,
            criticalCount = ordered.Count(message => string.Equals(message.Priority, "critical", StringComparison.OrdinalIgnoreCase)),
            next = ordered.FirstOrDefault(),
            messages = ordered
        });
    }

    private static string DetermineMessagePriority(string text)
    {
        var trimmed = text.TrimStart();
        const string command = "/critical";
        if (!trimmed.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return "regular";
        }

        return trimmed.Length == command.Length || char.IsWhiteSpace(trimmed[command.Length])
            ? "critical"
            : "regular";
    }

    private string ResolveBridgeFolder()
    {
        var configured = Environment.GetEnvironmentVariable("POWERCHAT_BRIDGE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "bridge"));
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_appRoot);
            Directory.CreateDirectory(_chatFolder);
            Directory.CreateDirectory(_attachmentsFolder);
            Directory.CreateDirectory(_draftAttachmentsFolder);
            Directory.CreateDirectory(_bridgeFolder);
            await LoadDraftStateAsync();

            var aiRulePath = Path.Combine(_bridgeFolder, "POWERCHAT_AI_RULE.md");
            await EnsureAiRuleAsync(aiRulePath);

            await WriteJsonAtomicAsync(
                Path.Combine(_bridgeFolder, "current-chat.json"),
                new
                {
                    sessionId = _sessionId,
                    chatFolder = _chatFolder,
                    messagesPath = _messagesPath,
                    bridgeFolder = _bridgeFolder,
                    aiRulePath,
                    aiStatusPath = _aiStatusPath,
                    pendingQueuePath = _pendingQueuePath,
                    draftPath = _draftPath,
                    draftAttachmentsFolder = _draftAttachmentsFolder,
                    latestUserMessagePath = Path.Combine(_bridgeFolder, "latest-user-message.json"),
                    assistantReplyPath = _assistantReplyPath,
                    requiredAiConnectionMessage = "**POWERCHAT STATUS: CONNECTED — I AM THINKING IN POWERCHAT.**",
                    startedAt = DateTimeOffset.Now
                });

            await RebuildPendingQueueAsync();

            var profileFolder = Path.Combine(_appRoot, "WebView2Profile");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            ConfigureWebView();
            _bridgeTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"PowerChat could not start.\n\n{ex.Message}",
                "PowerChat startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task CheckAiStatusAsync()
    {
        if (!File.Exists(_aiStatusPath))
        {
            if (!string.Equals(_lastAiState, "disconnected", StringComparison.OrdinalIgnoreCase))
            {
                await PublishAiStatusAsync(
                    "disconnected",
                    "No AI status file is present.",
                    currentUserMessageId: null,
                    expiresAt: null,
                    announceDisconnect: true);
            }
            return;
        }

        var writeUtc = File.GetLastWriteTimeUtc(_aiStatusPath);
        var status = await ReadAiStatusAsync();
        var expired = string.Equals(status.State, "connected", StringComparison.OrdinalIgnoreCase)
                      && status.ExpiresAt is not null
                      && status.ExpiresAt <= DateTimeOffset.Now;

        if (expired)
        {
            status = status with
            {
                State = "disconnected",
                Detail = "AI connection timed out.",
                CurrentUserMessageId = null,
                UpdatedAt = DateTimeOffset.Now,
                ExpiresAt = null
            };

            await WriteJsonAtomicAsync(_aiStatusPath, new
            {
                state = status.State,
                surface = "PowerChat",
                detail = status.Detail,
                currentUserMessageId = status.CurrentUserMessageId,
                updatedAt = status.UpdatedAt,
                expiresAt = status.ExpiresAt
            });
            writeUtc = File.GetLastWriteTimeUtc(_aiStatusPath);
        }

        if (writeUtc == _lastAiStatusWriteUtc
            && string.Equals(status.State, _lastAiState, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var announceDisconnect = string.Equals(_lastAiState, "connected", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(status.State, "disconnected", StringComparison.OrdinalIgnoreCase);
        await PublishAiStatusAsync(status.State, status.Detail, status.CurrentUserMessageId, status.ExpiresAt, announceDisconnect);
        _lastAiStatusWriteUtc = writeUtc;
    }

    private async Task PublishAiStatusAsync(
        string state,
        string detail,
        string? currentUserMessageId,
        DateTimeOffset? expiresAt,
        bool announceDisconnect)
    {
        _lastAiState = state;
        await PostToWebAsync(new
        {
            type = "aiStatus",
            state,
            detail,
            currentUserMessageId,
            updatedAt = DateTimeOffset.Now,
            expiresAt
        });

        if (!announceDisconnect)
        {
            return;
        }

        var message = new
        {
            id = $"assistant_status_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            role = "assistant",
            text = $"**POWERCHAT STATUS: DISCONNECTED — I AM NO LONGER THINKING IN POWERCHAT.**\n\n{detail}",
            createdAt = DateTimeOffset.Now,
            inReplyTo = (string?)null,
            sessionId = _sessionId
        };

        await AppendJsonLineAsync(message);
        await PostToWebAsync(new { type = "assistantMessage", message });
    }

    private async Task<List<JsonElement>> LoadMessageHistoryAsync()
    {
        var messages = new List<JsonElement>();
        if (!File.Exists(_messagesPath))
        {
            return messages;
        }

        foreach (var line in await File.ReadAllLinesAsync(_messagesPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                messages.Add(document.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Ignore a damaged trailing line rather than blocking session recovery.
            }
        }

        return messages;
    }

    private async Task<AiStatusSnapshot> ReadAiStatusAsync()
    {
        if (!File.Exists(_aiStatusPath))
        {
            return new AiStatusSnapshot("disconnected", "AI is not connected.", null, DateTimeOffset.Now, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_aiStatusPath);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var state = root.TryGetProperty("state", out var stateNode)
                ? stateNode.GetString() ?? "disconnected"
                : "disconnected";
            var detail = root.TryGetProperty("detail", out var detailNode)
                ? detailNode.GetString() ?? string.Empty
                : string.Empty;
            var currentUserMessageId = root.TryGetProperty("currentUserMessageId", out var currentMessageNode)
                ? currentMessageNode.GetString()
                : null;
            var updatedAt = root.TryGetProperty("updatedAt", out var updatedNode)
                            && DateTimeOffset.TryParse(updatedNode.GetString(), out var parsedUpdated)
                ? parsedUpdated
                : DateTimeOffset.Now;
            DateTimeOffset? expiresAt = root.TryGetProperty("expiresAt", out var expiresNode)
                                        && expiresNode.ValueKind == JsonValueKind.String
                                        && DateTimeOffset.TryParse(expiresNode.GetString(), out var parsedExpires)
                ? parsedExpires
                : null;

            return new AiStatusSnapshot(state, detail, currentUserMessageId, updatedAt, expiresAt);
        }
        catch (IOException)
        {
            return new AiStatusSnapshot(_lastAiState ?? "disconnected", "AI status is being updated.", null, DateTimeOffset.Now, null);
        }
        catch (JsonException)
        {
            return new AiStatusSnapshot(_lastAiState ?? "disconnected", "AI status file is incomplete.", null, DateTimeOffset.Now, null);
        }
    }

    private bool TryResumeCurrentSession(out string sessionId, out string chatFolder, out string messagesPath)
    {
        sessionId = string.Empty;
        chatFolder = string.Empty;
        messagesPath = string.Empty;
        var currentChatPath = Path.Combine(_bridgeFolder, "current-chat.json");
        if (!File.Exists(currentChatPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(currentChatPath));
            var root = document.RootElement;
            sessionId = root.GetProperty("sessionId").GetString() ?? string.Empty;
            chatFolder = root.GetProperty("chatFolder").GetString() ?? string.Empty;
            messagesPath = root.GetProperty("messagesPath").GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sessionId)
                   && Directory.Exists(chatFolder)
                   && !string.IsNullOrWhiteSpace(messagesPath);
        }
        catch
        {
            sessionId = string.Empty;
            chatFolder = string.Empty;
            messagesPath = string.Empty;
            return false;
        }
    }

    private void ConfigureWebView()
    {
        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsWebMessageEnabled = true;

        core.WebMessageReceived += CoreOnWebMessageReceived;
        core.NavigationCompleted += CoreOnNavigationCompleted;
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
        };

        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping(
            "powerchat.local",
            webRoot,
            CoreWebView2HostResourceAccessKind.Allow);
        _webView.Source = new Uri("https://powerchat.local/index.html");
    }

    private async Task TriggerSendShortcutAsync()
    {
        if (_webReady)
        {
            await _webView.ExecuteScriptAsync("window.powerChatSendCurrentMessage?.();");
        }
    }

    private async void CoreOnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MessageBox.Show(
                $"The local interface failed to load: {e.WebErrorStatus}",
                "PowerChat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _webReady = true;
        var messages = await LoadMessageHistoryAsync();
        var aiStatus = await ReadAiStatusAsync();
        var draft = await BuildDraftPayloadAsync();
        _lastAiState = aiStatus.State;
        if (File.Exists(_aiStatusPath))
        {
            _lastAiStatusWriteUtc = File.GetLastWriteTimeUtc(_aiStatusPath);
        }

        await PostToWebAsync(new
        {
            type = "bootstrap",
            session = new
            {
                id = _sessionId,
                folder = _chatFolder,
                appDataRoot = _appRoot,
                bridge = _bridgeFolder,
                startedAt = DateTimeOffset.Now
            },
            messages,
            draft,
            aiStatus = new
            {
                state = aiStatus.State,
                detail = aiStatus.Detail,
                currentUserMessageId = aiStatus.CurrentUserMessageId,
                updatedAt = aiStatus.UpdatedAt,
                expiresAt = aiStatus.ExpiresAt
            },
            windowState = new
            {
                maximized = WindowState == FormWindowState.Maximized,
                fullscreen = _isConsoleFullscreen
            }
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;
        if (keyCode == Keys.Enter && modifiers == (Keys.Control | Keys.Shift))
        {
            _ = TriggerSendShortcutAsync();
            return true;
        }

        if (keyData == Keys.F11)
        {
            ToggleConsoleFullscreen();
            return true;
        }

        if (keyData == Keys.Escape && _isConsoleFullscreen)
        {
            ExitConsoleFullscreen();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void CoreOnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;

            switch (type)
            {
                case "window":
                    HandleWindowAction(root);
                    break;
                case "send":
                    await SaveUserMessageAsync(root);
                    break;
                case "openFolder":
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_chatFolder}\"")
                    {
                        UseShellExecute = true
                    });
                    break;
                case "copyPath":
                    Clipboard.SetText(_chatFolder);
                    await PostToWebAsync(new { type = "toast", message = "Chat folder copied to clipboard." });
                    break;
                case "assistantRendered":
                    await SaveAssistantRenderedReceiptAsync(root);
                    break;
                case "exportChat":
                    await ExportChatAsync(root);
                    break;
                case "saveDraftText":
                    await SaveDraftTextAsync(root);
                    break;
                case "persistDraftAttachment":
                    await PersistDraftAttachmentAsync(root);
                    break;
                case "removeDraftAttachment":
                    await RemoveDraftAttachmentAsync(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            await PostToWebAsync(new { type = "error", message = ex.Message });
        }
    }

    private void HandleWindowAction(JsonElement root)
    {
        var action = root.TryGetProperty("action", out var actionNode) ? actionNode.GetString() : null;
        switch (action)
        {
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "toggleMaximize":
                if (_isConsoleFullscreen)
                {
                    ExitConsoleFullscreen();
                }
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                PublishWindowState();
                break;
            case "close":
                Close();
                break;
            case "drag":
                if (_isConsoleFullscreen)
                {
                    break;
                }
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
                break;
            case "toggleFullscreen":
                ToggleConsoleFullscreen();
                break;
        }
    }

    private void ToggleConsoleFullscreen()
    {
        if (_isConsoleFullscreen)
        {
            ExitConsoleFullscreen();
            return;
        }

        _fullscreenRestoreState = WindowState;
        _fullscreenRestoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        WindowState = FormWindowState.Normal;
        Bounds = Screen.FromControl(this).Bounds;
        Padding = Padding.Empty;
        TopMost = true;
        _isConsoleFullscreen = true;
        PublishFullscreenState();
    }

    private void ExitConsoleFullscreen()
    {
        if (!_isConsoleFullscreen)
        {
            return;
        }

        TopMost = false;
        WindowState = FormWindowState.Normal;
        Bounds = _fullscreenRestoreBounds;
        Padding = new Padding(1);
        _isConsoleFullscreen = false;
        if (_fullscreenRestoreState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
        PublishFullscreenState();
        PublishWindowState();
    }

    private void PublishFullscreenState()
    {
        if (!_webReady)
        {
            return;
        }

        _ = PostToWebAsync(new
        {
            type = "fullscreenChanged",
            active = _isConsoleFullscreen
        });
    }

    private void PublishWindowState()
    {
        if (!_webReady || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _ = PostToWebAsync(new
        {
            type = "windowStateChanged",
            maximized = WindowState == FormWindowState.Maximized
        });
    }

    private async Task SaveUserMessageAsync(JsonElement root)
    {
        var clientId = root.TryGetProperty("id", out var idNode) && !string.IsNullOrWhiteSpace(idNode.GetString())
            ? idNode.GetString()!
            : $"msg_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}"[..34];
        var text = root.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
        var priority = DetermineMessagePriority(text);
        var savedAttachments = new List<object>();

        if (root.TryGetProperty("attachments", out var attachmentsNode) && attachmentsNode.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var attachment in attachmentsNode.EnumerateArray())
            {
                index++;
                var originalName = attachment.TryGetProperty("name", out var nameNode)
                    ? nameNode.GetString() ?? $"image-{index}.png"
                    : $"image-{index}.png";
                var mime = attachment.TryGetProperty("mime", out var mimeNode)
                    ? mimeNode.GetString() ?? "application/octet-stream"
                    : "application/octet-stream";
                var dataUrl = attachment.TryGetProperty("dataUrl", out var dataNode)
                    ? dataNode.GetString() ?? string.Empty
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.Contains(','))
                {
                    continue;
                }

                var safeName = MakeSafeFileName(originalName);
                var finalName = $"{DateTime.Now:HHmmssfff}_{index:00}_{safeName}";
                var finalPath = Path.Combine(_attachmentsFolder, finalName);
                var base64 = dataUrl[(dataUrl.IndexOf(',') + 1)..];
                var bytes = Convert.FromBase64String(base64);
                await File.WriteAllBytesAsync(finalPath, bytes);

                savedAttachments.Add(new
                {
                    name = originalName,
                    storedName = finalName,
                    path = finalPath,
                    mime,
                    size = bytes.LongLength
                });
            }
        }

        var message = new
        {
            id = clientId,
            role = "user",
            text,
            priority,
            attachments = savedAttachments,
            createdAt = DateTimeOffset.Now,
            sessionId = _sessionId
        };

        await AppendJsonLineAsync(message);
        await WriteJsonAtomicAsync(Path.Combine(_bridgeFolder, "latest-user-message.json"), message);
        await WriteJsonAtomicAsync(Path.Combine(_bridgeFolder, $"user-{clientId}.json"), message);
        await RebuildPendingQueueAsync();
        await ResetAiTimeoutAfterUserMessageAsync();
        await ClearDraftAsync();

        await PostToWebAsync(new
        {
            type = "messageSaved",
            id = clientId,
            attachmentCount = savedAttachments.Count,
            folder = _chatFolder
        });
    }

    private async void BridgeTimerOnTick(object? sender, EventArgs e)
    {
        if (_processingReply || !_webReady)
        {
            return;
        }

        _processingReply = true;
        try
        {
            await CheckAiStatusAsync();

            if (!File.Exists(_assistantReplyPath))
            {
                return;
            }

            string json;
            await using (var stream = new FileStream(
                             _assistantReplyPath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            var id = root.TryGetProperty("id", out var idNode) && !string.IsNullOrWhiteSpace(idNode.GetString())
                ? idNode.GetString()!
                : $"assistant_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var text = root.TryGetProperty("text", out var textNode)
                ? textNode.GetString() ?? string.Empty
                : string.Empty;
            var inReplyTo = root.TryGetProperty("inReplyTo", out var replyNode)
                ? replyNode.GetString()
                : null;
            var completeTask = !root.TryGetProperty("completeTask", out var completeNode)
                               || completeNode.ValueKind != JsonValueKind.False;

            var message = new
            {
                id,
                role = "assistant",
                text,
                createdAt = DateTimeOffset.Now,
                inReplyTo,
                completeTask,
                sessionId = _sessionId
            };

            await AppendJsonLineAsync(message);
            await RebuildPendingQueueAsync();
            await PostToWebAsync(new { type = "assistantMessage", message });

            var archiveFolder = Path.Combine(_bridgeFolder, "processed");
            Directory.CreateDirectory(archiveFolder);
            var archivePath = Path.Combine(archiveFolder, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{id}.json");
            File.Move(_assistantReplyPath, archivePath, true);
        }
        catch (IOException)
        {
            // The writer may still be completing its atomic replacement. The next tick retries.
        }
        catch (JsonException)
        {
            // A partial file is ignored until the next tick.
        }
        finally
        {
            _processingReply = false;
        }
    }

    private async Task SaveAssistantRenderedReceiptAsync(JsonElement root)
    {
        var id = root.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
        await WriteJsonAtomicAsync(
            Path.Combine(_bridgeFolder, "assistant-rendered.json"),
            new
            {
                id,
                renderedAt = DateTimeOffset.Now,
                sessionId = _sessionId
            });
    }

    private async Task ExportChatAsync(JsonElement root)
    {
        var content = root.TryGetProperty("content", out var contentNode)
            ? contentNode.GetString() ?? string.Empty
            : string.Empty;
        var format = root.TryGetProperty("format", out var formatNode)
            ? formatNode.GetString()?.ToLowerInvariant() ?? "markdown"
            : "markdown";
        var requestedName = root.TryGetProperty("suggestedName", out var nameNode)
            ? nameNode.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            await PostToWebAsync(new { type = "error", message = "There is no selected chat content to export." });
            return;
        }

        var extension = format switch
        {
            "json" => ".json",
            "text" => ".txt",
            _ => ".md"
        };
        var filter = format switch
        {
            "json" => "JSON files (*.json)|*.json|All files (*.*)|*.*",
            "text" => "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            _ => "Markdown files (*.md)|*.md|All files (*.*)|*.*"
        };
        var safeBaseName = MakeSafeFileName(Path.GetFileNameWithoutExtension(requestedName));
        if (string.IsNullOrWhiteSpace(safeBaseName) || safeBaseName == "attachment.bin")
        {
            safeBaseName = $"PowerChat_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export PowerChat conversation",
            Filter = filter,
            DefaultExt = extension.TrimStart('.'),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = safeBaseName + extension,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            await PostToWebAsync(new { type = "exportCancelled" });
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(false));
        await PostToWebAsync(new
        {
            type = "exportSaved",
            path = dialog.FileName,
            bytes = Encoding.UTF8.GetByteCount(content)
        });
    }

    private async Task AppendJsonLineAsync<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions).ReplaceLineEndings(string.Empty);
        await File.AppendAllTextAsync(_messagesPath, json + Environment.NewLine, Encoding.UTF8);
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static async Task EnsureAiRuleAsync(string path)
    {
        const string rule = """
# POWERCHAT AI CONNECTION RULE — READ FIRST

**Immediately after connecting to this PowerChat bridge, send this exact visible message inside PowerChat:**

**POWERCHAT STATUS: CONNECTED — I AM THINKING IN POWERCHAT.**

Then follow these rules:

1. Read `current-chat.json`, `pending-user-messages.json`, and this rule before doing anything else. `latest-user-message.json` is diagnostic only.
2. Process `pending-user-messages.json.messages[0]` first. The app sorts all unanswered `/critical` messages ahead of regular messages while preserving arrival order within each priority.
3. While a PowerChat interaction is active, answer the user through `assistant-reply.json`, not in the normal ChatGPT conversation.
4. Include `inReplyTo` using the selected queued message ID. For acknowledgements or progress updates, also set `completeTask: false`; omit it or set it to `true` only on the final answer that should remove the task from the queue.
5. Confirm delivery by checking `assistant-rendered.json` for the same assistant message ID.
6. On connection, write `ai-status.json` with `state: "connected"`, a concrete `detail`, `updatedAt`, `expiresAt` one hour ahead, and the current user-message ID. Refresh the one-hour expiry after every user turn.
7. If a new ChatGPT context takes over, it must send the bold connection message again before processing pending messages.
8. Before sending a final response in the normal ChatGPT conversation—or when ending/timing out the PowerChat loop—write `state: "disconnected"` with a reason. PowerChat will turn the READY indicator red and post the disconnect confirmation.
9. Never silently switch between the ChatGPT page and PowerChat. Announce the active surface inside PowerChat.
""";

        if (!File.Exists(path) || !string.Equals(await File.ReadAllTextAsync(path), rule, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(path, rule, new UTF8Encoding(false));
        }
    }

    private Task PostToWebAsync<T>(T payload)
    {
        if (!_webReady || _webView.CoreWebView2 is null)
        {
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
        return Task.CompletedTask;
    }

    private static string MakeSafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "attachment.bin" : safe;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcCalcSize && message.WParam != IntPtr.Zero)
        {
            // Keep the native sizing/snap styles, but make the entire native
            // frame client area so Windows does not draw a white strip above
            // the custom HTML title bar.
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg == WmGetMinMaxInfo)
        {
            ApplyMaximizedWorkArea(message.HWnd, message.LParam);
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal && !_isConsoleFullscreen)
        {
            base.WndProc(ref message);
            if ((int)message.Result != 1)
            {
                return;
            }

            var cursor = PointToClient(Cursor.Position);
            var left = cursor.X <= ResizeBorder;
            var right = cursor.X >= ClientSize.Width - ResizeBorder;
            var top = cursor.Y <= ResizeBorder;
            var bottom = cursor.Y >= ClientSize.Height - ResizeBorder;

            message.Result = (IntPtr)(left && top ? HtTopLeft
                : right && top ? HtTopRight
                : left && bottom ? HtBottomLeft
                : right && bottom ? HtBottomRight
                : left ? HtLeft
                : right ? HtRight
                : top ? HtTop
                : bottom ? HtBottom
                : 1);
            return;
        }

        base.WndProc(ref message);
    }

    private static void ApplyMaximizedWorkArea(IntPtr windowHandle, IntPtr minMaxInfoPointer)
    {
        const uint monitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(windowHandle, monitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        minMaxInfo.MaxPosition.X = Math.Abs(monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left);
        minMaxInfo.MaxPosition.Y = Math.Abs(monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top);
        minMaxInfo.MaxSize.X = Math.Abs(monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left);
        minMaxInfo.MaxSize.Y = Math.Abs(monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top);
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            const int dwmwaWindowCornerPreference = 33;
            var preference = 2;
            DwmSetWindowAttribute(
                Handle,
                dwmwaWindowCornerPreference,
                ref preference,
                Marshal.SizeOf<int>());

            const int dwmwaBorderColor = 34;
            var noBorderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(
                Handle,
                dwmwaBorderColor,
                ref noBorderColor,
                Marshal.SizeOf<int>());
        }
        catch
        {
            // Rounded corners are cosmetic and unavailable on some Windows builds.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, int wParam, int lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private sealed record AiStatusSnapshot(
        string State,
        string Detail,
        string? CurrentUserMessageId,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? ExpiresAt);

    private sealed record PendingQueueMessage(
        string Id,
        string Text,
        string Priority,
        JsonElement Attachments,
        DateTimeOffset CreatedAt,
        string SessionId);

    private sealed record DraftFileState(
        string? Text,
        List<DraftAttachmentState>? Attachments);

    private sealed record DraftAttachmentState(
        string Id,
        string Name,
        string StoredName,
        string Path,
        string Mime,
        long Size);
}
