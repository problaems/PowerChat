(() => {
  'use strict';

  const state = {
    session: null,
    attachments: [],
    messageCount: 0,
    awaitingReply: false,
    aiState: 'disconnected',
    aiExpiresAt: null,
    activeTaskId: null,
    tasks: new Map(),
    taskOrder: [],
    history: [],
    historyIndex: new Map(),
    exportInProgress: false,
    maximized: false,
    viewMode: 'full',
    consoleFullscreen: false,
    draftSaveTimer: null,
    draftRestoring: false
  };

  const viewModes = [
    { id: 'full', label: 'FULL', title: 'Show every user and assistant message.' },
    { id: 'queue', label: 'QUEUE', title: 'Show only your pending and in-progress queue.' },
    { id: 'answered', label: 'ANSWERED', title: 'Show only your messages that have received an answer.' },
    { id: 'answered-ai', label: 'ANSWERED + AI', title: 'Show answered user messages together with their matching AI responses.' }
  ];

  const $ = selector => document.querySelector(selector);
  const elements = {
    dragRegion: $('#dragRegion'),
    minimizeButton: $('#minimizeButton'),
    maximizeButton: $('#maximizeButton'),
    closeButton: $('#closeButton'),
    titleStatus: $('#titleStatus'),
    titleStatusWrap: $('.title-status'),
    connectionState: $('#connectionState'),
    connectionTimer: $('#connectionTimer'),
    connectionPill: $('.connection-pill'),
    sessionId: $('#sessionId'),
    folderPath: $('#folderPath'),
    pathField: $('#pathField'),
    openFolderButton: $('#openFolderButton'),
    focusComposerButton: $('#focusComposerButton'),
    scrollBottomButton: $('#scrollBottomButton'),
    messageCount: $('#messageCount'),
    chatScroll: $('#chatScroll'),
    emptyState: $('#emptyState'),
    messages: $('#messages'),
    typingRow: $('#typingRow'),
    composerInput: $('#composerInput'),
    composerMode: $('#composerMode'),
    fileInput: $('#fileInput'),
    attachButton: $('#attachButton'),
    attachmentStrip: $('#attachmentStrip'),
    slashCommandPanel: $('#slashCommandPanel'),
    sendButton: $('#sendButton'),
    counter: $('#counter'),
    dropOverlay: $('#dropOverlay'),
    toastStack: $('#toastStack'),
    clock: $('#clock'),
    workPanel: $('.work-panel'),
    workDetail: $('#workDetail'),
    activeTaskState: $('#activeTaskState'),
    taskQueue: $('#taskQueue'),
    taskQueueCount: $('#taskQueueCount'),
    appFrame: $('.app-frame'),
    viewModeButton: $('#viewModeButton'),
    exportButton: $('#exportButton'),
    exportModal: $('#exportModal'),
    exportCloseButton: $('#exportCloseButton'),
    exportCancelButton: $('#exportCancelButton'),
    exportSaveButton: $('#exportSaveButton'),
    exportFormat: $('#exportFormat'),
    exportTimestamps: $('#exportTimestamps'),
    exportFrom: $('#exportFrom'),
    exportTo: $('#exportTo'),
    exportMessageLimit: $('#exportMessageLimit'),
    exportTokenLimit: $('#exportTokenLimit'),
    exportSelectedCount: $('#exportSelectedCount'),
    exportCharacterCount: $('#exportCharacterCount'),
    exportTokenEstimate: $('#exportTokenEstimate'),
    exportByteEstimate: $('#exportByteEstimate'),
    exportSelectionHint: $('#exportSelectionHint')
  };

  function post(payload) {
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(payload);
    }
  }

  function messagePriority(message) {
    if (String(message.priority || '').toLowerCase() === 'critical') return 'critical';
    return /^\s*\/critical(?:\s|$)/i.test(String(message.text || '')) ? 'critical' : 'regular';
  }

  function isVisibleInCurrentMode(article) {
    if (state.viewMode === 'full') return true;
    const isUser = article.classList.contains('user');
    const taskStatus = article.dataset.taskStatus || '';

    if (state.viewMode === 'queue') {
      return isUser && taskStatus !== 'complete';
    }

    if (state.viewMode === 'answered') {
      return isUser && taskStatus === 'complete';
    }

    if (state.viewMode === 'answered-ai') {
      if (isUser) return taskStatus === 'complete';
      const sourceId = article.dataset.inReplyTo;
      return Boolean(sourceId && state.tasks.get(sourceId)?.status === 'complete');
    }

    return true;
  }

  function applyViewMode() {
    const mode = viewModes.find(candidate => candidate.id === state.viewMode) || viewModes[0];
    let visible = 0;
    elements.messages.querySelectorAll('.message').forEach(article => {
      const show = isVisibleInCurrentMode(article);
      article.classList.toggle('filtered-out', !show);
      if (show) visible += 1;
    });
    elements.viewModeButton.textContent = `VIEW: ${mode.label}`;
    elements.viewModeButton.title = `${mode.title} ${visible} message${visible === 1 ? '' : 's'} currently visible.`;
  }

  function cycleViewMode() {
    const index = viewModes.findIndex(candidate => candidate.id === state.viewMode);
    const next = viewModes[(index + 1) % viewModes.length];
    state.viewMode = next.id;
    applyViewMode();
    showToast(`${next.label} view active.`);
  }

  function escapeHtml(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  function renderMarkdown(source) {
    const codeBlocks = [];
    let text = String(source ?? '').replace(/```([\w-]*)\n?([\s\S]*?)```/g, (_, language, code) => {
      const token = `@@CODEBLOCK_${codeBlocks.length}@@`;
      codeBlocks.push({ language: language || 'text', code });
      return token;
    });

    text = escapeHtml(text)
      .replace(/^### (.+)$/gm, '<h3>$1</h3>')
      .replace(/^## (.+)$/gm, '<h2>$1</h2>')
      .replace(/^# (.+)$/gm, '<h1>$1</h1>')
      .replace(/^&gt; (.+)$/gm, '<blockquote>$1</blockquote>')
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/__(.+?)__/g, '<strong>$1</strong>')
      .replace(/(?<!\*)\*([^*\n]+)\*(?!\*)/g, '<em>$1</em>')
      .replace(/`([^`\n]+)`/g, '<code>$1</code>')
      .replace(/\[([^\]]+)]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>');

    const lines = text.split('\n');
    const output = [];
    let listType = null;
    let paragraph = [];

    const flushParagraph = () => {
      if (!paragraph.length) return;
      output.push(`<p>${paragraph.join('<br>')}</p>`);
      paragraph = [];
    };
    const closeList = () => {
      if (listType) output.push(`</${listType}>`);
      listType = null;
    };

    for (const line of lines) {
      const unordered = line.match(/^\s*[-+]\s+(.+)$/);
      const ordered = line.match(/^\s*\d+\.\s+(.+)$/);
      if (unordered || ordered) {
        flushParagraph();
        const desired = unordered ? 'ul' : 'ol';
        if (listType !== desired) {
          closeList();
          listType = desired;
          output.push(`<${desired}>`);
        }
        output.push(`<li>${(unordered || ordered)[1]}</li>`);
      } else if (!line.trim()) {
        flushParagraph();
        closeList();
      } else if (/^<(h[1-3]|blockquote)>/.test(line) || /^@@CODEBLOCK_\d+@@$/.test(line.trim())) {
        flushParagraph();
        closeList();
        output.push(line);
      } else {
        paragraph.push(line);
      }
    }
    flushParagraph();
    closeList();

    let html = output.join('');
    codeBlocks.forEach((block, index) => {
      const token = `@@CODEBLOCK_${index}@@`;
      const code = escapeHtml(block.code.replace(/^\n|\n$/g, ''));
      html = html.replace(token, `<pre data-lang="${escapeHtml(block.language)}"><code>${code}</code></pre>`);
    });
    return html || '<p></p>';
  }

  function formatTime(value = new Date()) {
    return new Date(value).toLocaleTimeString([], {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  }

  function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  function normalizeMessageForExport(message) {
    return {
      id: message.id,
      role: message.role === 'assistant' ? 'assistant' : 'user',
      text: String(message.text || ''),
      priority: message.priority || undefined,
      attachments: (message.attachments || []).map(attachment => ({
        name: attachment.name || attachment.storedName || 'attachment',
        storedName: attachment.storedName || undefined,
        path: attachment.path || undefined,
        mime: attachment.mime || undefined,
        size: Number(attachment.size || 0)
      })),
      createdAt: message.createdAt || new Date().toISOString(),
      inReplyTo: message.inReplyTo || undefined,
      completeTask: message.completeTask === false ? false : undefined,
      sessionId: message.sessionId || state.session?.id || undefined
    };
  }

  function rememberMessage(message) {
    const normalized = normalizeMessageForExport(message);
    const existingIndex = state.historyIndex.get(normalized.id);
    if (existingIndex !== undefined) {
      state.history[existingIndex] = normalized;
      return;
    }
    state.historyIndex.set(normalized.id, state.history.length);
    state.history.push(normalized);
  }

  function rawMessageCharacters(message) {
    return JSON.stringify(message).length;
  }

  function estimateTokensFromCharacters(characters) {
    return Math.ceil(Math.max(0, characters) / 4);
  }

  function getCheckedValue(name, fallback) {
    return document.querySelector(`input[name="${name}"]:checked`)?.value || fallback;
  }

  function getExportSettings() {
    return {
      role: getCheckedValue('exportRole', 'both'),
      scope: getCheckedValue('exportScope', 'all'),
      format: elements.exportFormat.value,
      keepTimestamps: elements.exportTimestamps.checked,
      from: elements.exportFrom.value ? new Date(elements.exportFrom.value) : null,
      to: elements.exportTo.value ? new Date(elements.exportTo.value) : null,
      messageLimit: Math.max(1, Number.parseInt(elements.exportMessageLimit.value || '20', 10)),
      tokenLimit: Math.max(1, Number.parseInt(elements.exportTokenLimit.value || '4000', 10))
    };
  }

  function selectExportMessages(settings) {
    let selected = state.history.filter(message => settings.role === 'both' || message.role === settings.role);

    if (settings.scope === 'datetime') {
      const fromTime = settings.from && !Number.isNaN(settings.from.getTime()) ? settings.from.getTime() : -Infinity;
      const toTime = settings.to && !Number.isNaN(settings.to.getTime()) ? settings.to.getTime() : Infinity;
      selected = selected.filter(message => {
        const time = new Date(message.createdAt).getTime();
        return time >= fromTime && time <= toTime;
      });
    } else if (settings.scope === 'messages') {
      selected = selected.slice(-settings.messageLimit);
    } else if (settings.scope === 'tokens') {
      const newestFirst = [];
      let estimatedTokens = 0;
      for (let index = selected.length - 1; index >= 0; index -= 1) {
        const message = selected[index];
        const messageTokens = estimateTokensFromCharacters(rawMessageCharacters(message));
        if (newestFirst.length && estimatedTokens + messageTokens > settings.tokenLimit) break;
        newestFirst.push(message);
        estimatedTokens += messageTokens;
        if (estimatedTokens >= settings.tokenLimit) break;
      }
      selected = newestFirst.reverse();
    }

    return selected;
  }

  const exportTimestampFormatter = new Intl.DateTimeFormat(undefined, {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit'
  }, true);

  function formatExportTimestamp(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value || '') : exportTimestampFormatter.format(date);
  }

  function attachmentLines(message, prefix = '- ') {
    return (message.attachments || []).map(attachment => {
      const location = attachment.path || attachment.storedName || '';
      const detail = [attachment.mime, attachment.size ? formatBytes(attachment.size) : '', location]
        .filter(Boolean).join(' | ');
      return `${prefix}${attachment.name}${detail ? ` (${detail})` : ''}`;
    });
  }

  function buildMarkdownExport(messages, settings) {
    const lines = ['# PowerChat conversation export', '', `Session: \`${state.session?.id || 'unknown'}\``];
    if (settings.keepTimestamps) lines.push(`Exported: ${formatExportTimestamp(new Date())}`);
    lines.push('', '---');
    for (const message of messages) {
      const label = message.role === 'assistant' ? 'AI' : 'You';
      const timestamp = settings.keepTimestamps ? ` — ${formatExportTimestamp(message.createdAt)}` : '';
      lines.push('', `## ${label}${timestamp}`, '', message.text || '');
      const attachments = attachmentLines(message);
      if (attachments.length) lines.push('', '**Attachments**', ...attachments);
    }
    return `${lines.join('\n').trim()}\n`;
  }

  function buildTextExport(messages, settings) {
    const lines = ['POWERCHAT CONVERSATION EXPORT', `Session: ${state.session?.id || 'unknown'}`];
    if (settings.keepTimestamps) lines.push(`Exported: ${formatExportTimestamp(new Date())}`);
    lines.push('='.repeat(64));
    for (const message of messages) {
      const label = message.role === 'assistant' ? 'AI' : 'YOU';
      const timestamp = settings.keepTimestamps ? ` | ${formatExportTimestamp(message.createdAt)}` : '';
      lines.push('', `[${label}${timestamp}]`, message.text || '');
      const attachments = attachmentLines(message, '  - ');
      if (attachments.length) lines.push('Attachments:', ...attachments);
    }
    return `${lines.join('\n').trim()}\n`;
  }

  function buildJsonExport(messages, settings) {
    const exportedMessages = messages.map(message => {
      const copy = structuredClone(message);
      if (!settings.keepTimestamps) delete copy.createdAt;
      return copy;
    });
    const output = {
      sessionId: state.session?.id || null,
      ...(settings.keepTimestamps ? { exportedAt: new Date().toISOString() } : {}),
      selection: {
        roles: settings.role,
        scope: settings.scope,
        messageCount: exportedMessages.length
      },
      messages: exportedMessages
    };
    return `${JSON.stringify(output, null, 2)}\n`;
  }

  function buildExportSnapshot() {
    const settings = getExportSettings();
    const messages = selectExportMessages(settings);
    const characters = messages.reduce((total, message) => total + rawMessageCharacters(message), 0);
    const tokens = estimateTokensFromCharacters(characters);
    const content = settings.format === 'json'
      ? buildJsonExport(messages, settings)
      : settings.format === 'text'
        ? buildTextExport(messages, settings)
        : buildMarkdownExport(messages, settings);
    const bytes = new TextEncoder().encode(content).length;
    return { settings, messages, characters, tokens, content, bytes };
  }

  function toLocalDateTimeInput(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    const pad = number => String(number).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  function updateScopePanel() {
    const scope = getCheckedValue('exportScope', 'all');
    document.querySelectorAll('[data-scope-panel]').forEach(panel => {
      panel.classList.toggle('hidden', panel.dataset.scopePanel !== scope);
    });
  }

  function updateExportPreview() {
    const snapshot = buildExportSnapshot();
    elements.exportSelectedCount.textContent = `${snapshot.messages.length} ${snapshot.messages.length === 1 ? 'message' : 'messages'}`;
    elements.exportCharacterCount.textContent = snapshot.characters.toLocaleString();
    elements.exportTokenEstimate.textContent = `~${snapshot.tokens.toLocaleString()}`;
    elements.exportByteEstimate.textContent = formatBytes(snapshot.bytes);
    elements.exportSaveButton.disabled = snapshot.messages.length === 0 || state.exportInProgress;
    const roleLabel = snapshot.settings.role === 'both' ? 'both roles' : snapshot.settings.role === 'user' ? 'your messages' : 'AI messages';
    elements.exportSelectionHint.textContent = snapshot.messages.length
      ? `${roleLabel}; ${snapshot.settings.scope} selection; ${snapshot.settings.keepTimestamps ? 'timestamps kept' : 'timestamps removed'}.`
      : 'No messages match the current filters.';
    return snapshot;
  }

  function openExportModal() {
    if (state.history.length) {
      if (!elements.exportFrom.value) elements.exportFrom.value = toLocalDateTimeInput(state.history[0].createdAt);
      if (!elements.exportTo.value) elements.exportTo.value = toLocalDateTimeInput(state.history.at(-1).createdAt);
    }
    updateScopePanel();
    updateExportPreview();
    elements.exportModal.classList.remove('hidden');
    elements.exportCloseButton.focus();
  }

  function closeExportModal() {
    if (state.exportInProgress) return;
    elements.exportModal.classList.add('hidden');
    elements.exportButton.focus();
  }

  function beginExport() {
    const snapshot = updateExportPreview();
    if (!snapshot.messages.length || state.exportInProgress) return;
    state.exportInProgress = true;
    elements.exportSaveButton.disabled = true;
    elements.exportSaveButton.textContent = 'Opening save dialog...';
    const extension = snapshot.settings.format === 'json' ? 'json' : snapshot.settings.format === 'text' ? 'txt' : 'md';
    post({
      type: 'exportChat',
      format: snapshot.settings.format,
      content: snapshot.content,
      suggestedName: `PowerChat_${state.session?.id || 'conversation'}_${new Date().toISOString().slice(0, 10)}.${extension}`
    });
  }

  function finishExport() {
    state.exportInProgress = false;
    elements.exportSaveButton.textContent = 'Choose save location';
    updateExportPreview();
  }

  function setMaximized(active) {
    state.maximized = Boolean(active);
    elements.maximizeButton.classList.toggle('maximized', state.maximized);
    elements.maximizeButton.title = state.maximized ? 'Restore down' : 'Maximize';
    elements.maximizeButton.setAttribute('aria-label', state.maximized ? 'Restore down' : 'Maximize');
  }

  function setConsoleFullscreen(active) {
    state.consoleFullscreen = Boolean(active);
    elements.appFrame.classList.toggle('console-fullscreen', state.consoleFullscreen);
    requestAnimationFrame(() => scrollToBottom(false));
  }

  function updateClock() {
    elements.clock.textContent = new Date().toLocaleTimeString([], { hour12: false });
  }

  function taskSummary(message) {
    const text = String(message.text || '')
      .replace(/^\s*\/critical(?:\s+|$)/i, '')
      .replace(/\s+/g, ' ')
      .trim();
    if (text) return text.length > 74 ? `${text.slice(0, 71)}…` : text;
    const count = (message.attachments || []).length;
    return count ? `${count} image attachment${count === 1 ? '' : 's'}` : 'Empty task';
  }

  function ensureTask(message) {
    if (!message?.id || state.tasks.has(message.id)) return;
    state.tasks.set(message.id, {
      id: message.id,
      summary: taskSummary(message),
      createdAt: message.createdAt,
      priority: messagePriority(message),
      status: 'pending'
    });
    state.taskOrder.push(message.id);
    renderTaskQueue();
  }

  function updateTaskDom(id, status) {
    const article = elements.messages.querySelector(`[data-message-id="${CSS.escape(id)}"]`);
    if (article) {
      article.classList.remove('task-pending', 'task-active', 'task-complete');
      article.classList.add(`task-${status}`);
      article.dataset.taskStatus = status;
      const label = article.querySelector('[data-task-status]');
      if (label) label.textContent = status === 'active' ? 'IN PROGRESS' : status === 'complete' ? 'ANSWERED' : 'QUEUED';
    }
    applyViewMode();
  }

  function setTaskStatus(id, status) {
    const task = state.tasks.get(id);
    if (!task) return;
    task.status = status;
    updateTaskDom(id, status);
    renderTaskQueue();
  }

  function setActiveTask(id) {
    if (state.activeTaskId && state.activeTaskId !== id) {
      const previous = state.tasks.get(state.activeTaskId);
      if (previous?.status === 'active') setTaskStatus(state.activeTaskId, 'pending');
    }
    state.activeTaskId = id || null;
    if (id && state.tasks.has(id)) {
      setTaskStatus(id, 'active');
    }
  }

  function completeTask(id) {
    if (!id || !state.tasks.has(id)) return;
    setTaskStatus(id, 'complete');
    if (state.activeTaskId === id) state.activeTaskId = null;
  }

  function scrollToMessage(id) {
    const target = elements.messages.querySelector(`[data-message-id="${CSS.escape(id)}"]`);
    if (!target) return;
    if (target.classList.contains('filtered-out')) {
      state.viewMode = 'full';
      applyViewMode();
    }
    target.scrollIntoView({ behavior: 'smooth', block: 'center' });
    target.animate([
      { filter: 'brightness(1)' },
      { filter: 'brightness(1.55)' },
      { filter: 'brightness(1)' }
    ], { duration: 850, easing: 'ease-out' });
  }

  function renderTaskQueue() {
    const originalOrder = new Map(state.taskOrder.map((id, index) => [id, index]));
    const tasks = state.taskOrder
      .map(id => state.tasks.get(id))
      .filter(Boolean)
      .sort((left, right) => {
        const leftComplete = left.status === 'complete';
        const rightComplete = right.status === 'complete';
        if (leftComplete !== rightComplete) return leftComplete ? 1 : -1;
        const leftPriority = left.priority === 'critical' ? 0 : 1;
        const rightPriority = right.priority === 'critical' ? 0 : 1;
        if (leftPriority !== rightPriority) return leftPriority - rightPriority;
        const leftActive = left.status === 'active' ? 0 : 1;
        const rightActive = right.status === 'active' ? 0 : 1;
        if (leftActive !== rightActive) return leftActive - rightActive;
        return originalOrder.get(left.id) - originalOrder.get(right.id);
      });
    elements.taskQueueCount.textContent = String(tasks.filter(task => task.status !== 'complete').length);
    elements.taskQueue.innerHTML = '';
    if (!tasks.length) {
      elements.taskQueue.innerHTML = '<div class="task-queue-empty">No queued tasks</div>';
      return;
    }

    for (const task of tasks) {
      const button = document.createElement('button');
      button.className = `task-item ${task.status}${task.priority === 'critical' ? ' critical' : ''}`;
      button.dataset.taskId = task.id;
      const statusText = task.status === 'active' ? 'AI working now' : task.status === 'complete' ? 'Answered' : 'Unread / queued';
      button.innerHTML = `<span class="task-dot"></span><span class="task-copy"><b>${escapeHtml(task.summary)}</b><small>${task.priority === 'critical' ? 'CRITICAL · ' : ''}${statusText}</small></span>`;
      button.addEventListener('click', () => scrollToMessage(task.id));
      elements.taskQueue.appendChild(button);
    }
  }

  function updateConnectionTimer() {
    if (state.aiState !== 'connected' || !state.aiExpiresAt) {
      elements.connectionTimer.textContent = '--:--';
      elements.connectionTimer.title = 'No active AI timeout';
      elements.connectionTimer.classList.add('disconnected');
      return;
    }

    elements.connectionTimer.classList.remove('disconnected');
    const remainingMs = Math.max(0, state.aiExpiresAt - Date.now());
    const totalSeconds = Math.ceil(remainingMs / 1000);
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    elements.connectionTimer.textContent = `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
    elements.connectionTimer.title = 'AI disconnects after this much inactivity. Sending a message resets it to 60:00.';
  }

  function setAiConnectionState(nextState, detail = '', currentUserMessageId = null, expiresAt = null) {
    const connected = String(nextState).toLowerCase() === 'connected';
    state.aiState = connected ? 'connected' : 'disconnected';
    const parsedExpiry = expiresAt ? Date.parse(expiresAt) : NaN;
    state.aiExpiresAt = connected && Number.isFinite(parsedExpiry) ? parsedExpiry : null;
    elements.titleStatus.textContent = connected ? 'READY' : 'DISCONNECTED';
    elements.connectionState.textContent = connected ? 'AI CONNECTED' : 'AI DISCONNECTED';
    elements.titleStatusWrap.classList.toggle('connected', connected);
    elements.titleStatusWrap.classList.toggle('disconnected', !connected);
    elements.connectionPill.classList.toggle('connected', connected);
    elements.connectionPill.classList.toggle('disconnected', !connected);
    elements.titleStatusWrap.title = detail || elements.connectionState.textContent;
    elements.connectionPill.title = detail || elements.connectionState.textContent;
    elements.workPanel.classList.toggle('connected', connected && !currentUserMessageId);
    elements.workPanel.classList.toggle('disconnected', !connected);
    elements.workPanel.classList.toggle('active', connected && Boolean(currentUserMessageId));
    elements.workDetail.textContent = detail || (connected ? 'Connected and monitoring the queue.' : 'AI is not connected.');
    elements.activeTaskState.textContent = !connected ? 'OFFLINE' : currentUserMessageId ? 'ACTIVE' : 'READY';
    setActiveTask(connected ? currentUserMessageId : null);
    updateConnectionTimer();
  }

  function updateMessageCount() {
    elements.messageCount.textContent = `${state.messageCount} ${state.messageCount === 1 ? 'message' : 'messages'}`;
  }

  function scrollToBottom(smooth = true) {
    elements.chatScroll.scrollTo({
      top: elements.chatScroll.scrollHeight,
      behavior: smooth ? 'smooth' : 'auto'
    });
  }

  function showToast(message, isError = false) {
    const toast = document.createElement('div');
    toast.className = `toast${isError ? ' error' : ''}`;
    toast.textContent = message;
    elements.toastStack.appendChild(toast);
    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(8px)';
      toast.style.transition = '.2s ease';
      setTimeout(() => toast.remove(), 220);
    }, 3000);
  }

  function draftStorageKey() {
    return `powerchat-draft-text:${state.session?.id || 'pending'}`;
  }

  function scheduleDraftTextSave() {
    if (state.draftRestoring) return;
    const text = elements.composerInput.value;
    localStorage.setItem(draftStorageKey(), text);
    elements.composerMode.textContent = 'SAVING DRAFT';
    clearTimeout(state.draftSaveTimer);
    state.draftSaveTimer = setTimeout(() => {
      post({ type: 'saveDraftText', text });
    }, 90);
  }

  function restoreDraft(draft = {}) {
    state.draftRestoring = true;
    const localText = localStorage.getItem(draftStorageKey()) || '';
    // localStorage is updated synchronously on every edit, so it is the safest
    // source when the app is closed before the debounced disk write completes.
    elements.composerInput.value = String(localText || draft.text || '');
    state.attachments = (draft.attachments || []).map(attachment => ({
      draftId: attachment.draftId || makeId('draft'),
      name: attachment.name || attachment.storedName || 'attachment',
      mime: attachment.mime || 'application/octet-stream',
      size: Number(attachment.size || 0),
      dataUrl: attachment.dataUrl || '',
      persistedPath: attachment.path || ''
    }));
    renderAttachments();
    updateComposer();
    elements.composerMode.textContent = elements.composerInput.value || state.attachments.length ? 'DRAFT RESTORED' : 'MARKDOWN';
    state.draftRestoring = false;
  }

  function renderMessage(message, options = {}) {
    rememberMessage(message);
    elements.emptyState.classList.add('hidden');
    elements.messages.classList.add('active');

    const article = document.createElement('article');
    const role = message.role === 'assistant' ? 'assistant' : 'user';
    const priority = role === 'user' ? messagePriority(message) : 'regular';
    article.className = `message ${role}${role === 'user' ? ` task-pending${priority === 'critical' ? ' critical' : ''}` : ''}`;
    article.dataset.messageId = message.id;
    if (role === 'user') {
      article.dataset.taskStatus = 'pending';
      article.dataset.priority = priority;
    }
    if (role === 'assistant' && message.inReplyTo) article.dataset.inReplyTo = message.inReplyTo;

    const attachmentHtml = (message.attachments || []).map(attachment => {
      const source = attachment.dataUrl || attachment.preview || '';
      const isImage = String(attachment.mime || '').startsWith('image/') && source;
      return isImage
        ? `<figure class="message-image"><img src="${source}" alt="${escapeHtml(attachment.name)}"><figcaption class="image-caption">${escapeHtml(attachment.name)} · ${formatBytes(attachment.size || 0)}</figcaption></figure>`
        : `<div class="message-file"><span>FILE</span><div><b>${escapeHtml(attachment.name)}</b><small>${escapeHtml(attachment.mime || 'application/octet-stream')} · ${formatBytes(attachment.size || 0)}</small></div></div>`;
    }).join('');

    const priorityHtml = role === 'user' && priority === 'critical' ? '<span class="critical-label">CRITICAL</span>' : '';
    const taskStatusHtml = role === 'user' ? '<span class="task-status-label" data-task-status>QUEUED</span>' : '';
    const jumpTaskHtml = role === 'assistant' && message.inReplyTo
      ? `<button class="jump-task" data-jump-task="${escapeHtml(message.inReplyTo)}">SOURCE TASK</button>`
      : '';

    article.innerHTML = `
      <div class="message-main">
        <div class="message-meta">
          <b>${role === 'assistant' ? 'ASSISTANT' : 'OPERATOR'}</b>
          <span>${formatTime(message.createdAt)}</span>
          <span>${role === 'assistant' ? 'MARKDOWN' : 'LOCAL TURN'}</span>
          ${priorityHtml}
          ${taskStatusHtml}
          ${jumpTaskHtml}
          <span class="state ${options.saved ? 'saved' : ''}">${options.saved ? 'SAVED' : role === 'assistant' ? 'DELIVERED' : 'SYNCING'}</span>
        </div>
        <div class="message-card">
          <button class="copy-message" title="Copy message">COPY</button>
          ${attachmentHtml ? `<div class="message-images">${attachmentHtml}</div>` : ''}
          <div class="markdown">${renderMarkdown(message.text)}</div>
        </div>
      </div>`;

    article.querySelector('.copy-message').addEventListener('click', async () => {
      await navigator.clipboard.writeText(message.text || '');
      showToast('Message copied.');
    });
    article.querySelector('.jump-task')?.addEventListener('click', event => {
      scrollToMessage(event.currentTarget.dataset.jumpTask);
    });

    elements.messages.appendChild(article);
    if (role === 'user') ensureTask(message);
    if (role === 'assistant' && message.inReplyTo && message.completeTask !== false) completeTask(message.inReplyTo);
    state.messageCount += 1;
    updateMessageCount();
    applyViewMode();
    scrollToBottom();
    return article;
  }

  function markMessageSaved(id) {
    const message = [...elements.messages.querySelectorAll('[data-message-id]')]
      .find(candidate => candidate.dataset.messageId === id);
    if (!message) return;
    const status = message.querySelector('.state');
    status.textContent = 'SAVED';
    status.classList.add('saved');
  }

  function setAwaitingReply(awaiting) {
    state.awaitingReply = awaiting;
    elements.typingRow.classList.toggle('hidden', !awaiting);
    if (awaiting) scrollToBottom();
  }

  function updateComposer() {
    const textLength = elements.composerInput.value.length;
    elements.counter.textContent = `${textLength.toLocaleString()} chars · ${state.attachments.length} files`;
    elements.sendButton.disabled = textLength === 0 && state.attachments.length === 0;
    elements.composerInput.style.height = 'auto';
    elements.composerInput.style.height = `${Math.min(elements.composerInput.scrollHeight, 170)}px`;
  }

  function updateSlashCommandPanel() {
    const text = elements.composerInput.value;
    const firstToken = text.trimStart().split(/\s/, 1)[0].toLowerCase();
    const shouldShow = firstToken.startsWith('/') && '/critical'.startsWith(firstToken);
    elements.slashCommandPanel.classList.toggle('hidden', !shouldShow);
  }

  function insertSlashCommand(command) {
    const text = elements.composerInput.value;
    const leadingWhitespace = text.match(/^\s*/)?.[0] || '';
    const remainder = text.slice(leadingWhitespace.length).replace(/^\/\S*\s*/, '');
    elements.composerInput.value = `${leadingWhitespace}${command}${remainder}`;
    updateComposer();
    updateSlashCommandPanel();
    scheduleDraftTextSave();
    elements.composerInput.focus();
    elements.composerInput.setSelectionRange(command.length + leadingWhitespace.length, command.length + leadingWhitespace.length);
  }

  function renderAttachments() {
    elements.attachmentStrip.innerHTML = '';
    elements.attachmentStrip.classList.toggle('hidden', state.attachments.length === 0);
    state.attachments.forEach((attachment, index) => {
      const chip = document.createElement('div');
      chip.className = 'attachment-chip';
      const preview = String(attachment.mime || '').startsWith('image/') && attachment.dataUrl
        ? `<img src="${attachment.dataUrl}" alt="">`
        : '<span class="attachment-file-icon">FILE</span>';
      chip.innerHTML = `
        ${preview}
        <div class="attachment-info"><b>${escapeHtml(attachment.name)}</b><span>${formatBytes(attachment.size)}</span></div>
        <button class="remove-attachment" title="Remove">×</button>`;
      chip.querySelector('button').addEventListener('click', () => {
        const [removed] = state.attachments.splice(index, 1);
        if (removed?.draftId) post({ type: 'removeDraftAttachment', draftId: removed.draftId });
        renderAttachments();
        updateComposer();
      });
      elements.attachmentStrip.appendChild(chip);
    });
  }

  async function fileToAttachment(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve({
        draftId: makeId('draft'),
        name: file.name || `pasted-image-${Date.now()}.png`,
        mime: file.type || 'application/octet-stream',
        size: file.size,
        dataUrl: reader.result
      });
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
  }

  async function addFiles(fileList) {
    const candidates = [...fileList];
    for (const file of candidates) {
      if (state.attachments.length >= 6) {
        showToast('Attachment limit reached: 6 files per turn.', true);
        break;
      }
      if (file.size > 15 * 1024 * 1024) {
        showToast(`${file.name} exceeds the 15 MB per-file limit.`, true);
        continue;
      }
      try {
        const attachment = await fileToAttachment(file);
        state.attachments.push(attachment);
        post({ type: 'persistDraftAttachment', ...attachment });
      } catch {
        showToast(`Could not read ${file.name}.`, true);
      }
    }
    renderAttachments();
    updateComposer();
    elements.fileInput.value = '';
  }

  function makeId(prefix = 'msg') {
    return `${prefix}_${Date.now()}_${crypto.randomUUID().replaceAll('-', '').slice(0, 10)}`;
  }

  function sendCurrentMessage() {
    const text = elements.composerInput.value.trim();
    if (!text && state.attachments.length === 0) return;

    const id = makeId();
    const attachments = state.attachments.map(item => ({ ...item }));
    renderMessage({
      id,
      role: 'user',
      text,
      attachments,
      createdAt: new Date().toISOString()
    });
    post({ type: 'send', id, text, attachments });

    elements.composerInput.value = '';
    state.attachments = [];
    clearTimeout(state.draftSaveTimer);
    state.draftSaveTimer = null;
    localStorage.removeItem(draftStorageKey());
    elements.composerMode.textContent = 'MARKDOWN';
    renderAttachments();
    updateComposer();
    setAwaitingReply(true);
    elements.composerInput.focus();
  }

  function handleHostMessage(event) {
    const payload = event.data;
    switch (payload.type) {
      case 'bootstrap':
        state.session = payload.session;
        elements.sessionId.textContent = payload.session.id;
        elements.sessionId.title = payload.session.id;
        elements.folderPath.textContent = payload.session.folder;
        elements.folderPath.title = payload.session.folder;
        elements.messages.innerHTML = '';
        state.messageCount = 0;
        state.tasks.clear();
        state.taskOrder = [];
        state.activeTaskId = null;
        state.history = [];
        state.historyIndex.clear();
        for (const message of payload.messages || []) {
          renderMessage(message, { saved: true, historical: true });
        }
        setAiConnectionState(payload.aiStatus?.state, payload.aiStatus?.detail, payload.aiStatus?.currentUserMessageId, payload.aiStatus?.expiresAt);
        setMaximized(payload.windowState?.maximized);
        setConsoleFullscreen(payload.windowState?.fullscreen);
        restoreDraft(payload.draft);
        showToast('Local session initialized. AppData persistence is active.');
        elements.composerInput.focus();
        break;
      case 'messageSaved':
        markMessageSaved(payload.id);
        break;
      case 'draftSaved':
        elements.composerMode.textContent = 'DRAFT SAVED';
        break;
      case 'draftAttachmentSaved': {
        const saved = payload.attachment;
        const attachment = state.attachments.find(item => item.draftId === saved?.draftId);
        if (attachment) attachment.persistedPath = saved.path;
        elements.composerMode.textContent = 'DRAFT SAVED';
        break;
      }
      case 'draftCleared':
        localStorage.removeItem(draftStorageKey());
        break;
      case 'assistantMessage':
        setAwaitingReply(false);
        renderMessage(payload.message, { saved: true });
        post({ type: 'assistantRendered', id: payload.message.id });
        showToast('Assistant reply received through the local bridge.');
        break;
      case 'aiStatus':
        setAiConnectionState(payload.state, payload.detail, payload.currentUserMessageId, payload.expiresAt);
        showToast(payload.state === 'connected' ? 'AI connected to PowerChat.' : 'AI disconnected from PowerChat.', payload.state !== 'connected');
        break;
      case 'toast':
        showToast(payload.message);
        break;
      case 'exportSaved':
        finishExport();
        elements.exportModal.classList.add('hidden');
        showToast(`Chat exported: ${payload.path}`);
        break;
      case 'exportCancelled':
        finishExport();
        showToast('Export cancelled.');
        break;
      case 'windowStateChanged':
        setMaximized(payload.maximized);
        break;
      case 'fullscreenChanged':
        setConsoleFullscreen(payload.active);
        showToast(payload.active ? 'F11 fullscreen active. Press F11 or Esc to restore.' : 'Fullscreen exited.');
        break;
      case 'error':
        showToast(payload.message || 'Native host error.', true);
        setAwaitingReply(false);
        if (state.exportInProgress) finishExport();
        break;
    }
  }

  elements.dragRegion.addEventListener('pointerdown', event => {
    if (event.button === 0) post({ type: 'window', action: 'drag' });
  });
  elements.dragRegion.addEventListener('dblclick', event => {
    if (event.button === 0) post({ type: 'window', action: 'toggleMaximize' });
  });
  elements.minimizeButton.addEventListener('click', () => post({ type: 'window', action: 'minimize' }));
  elements.maximizeButton.addEventListener('click', () => post({ type: 'window', action: 'toggleMaximize' }));
  elements.closeButton.addEventListener('click', () => post({ type: 'window', action: 'close' }));
  elements.openFolderButton.addEventListener('click', () => post({ type: 'openFolder' }));
  elements.pathField.addEventListener('click', () => post({ type: 'copyPath' }));
  elements.focusComposerButton.addEventListener('click', () => elements.composerInput.focus());
  elements.scrollBottomButton.addEventListener('click', () => scrollToBottom());
  elements.viewModeButton.addEventListener('click', cycleViewMode);
  elements.exportButton.addEventListener('click', openExportModal);
  elements.exportCloseButton.addEventListener('click', closeExportModal);
  elements.exportCancelButton.addEventListener('click', closeExportModal);
  elements.exportSaveButton.addEventListener('click', beginExport);
  elements.exportModal.addEventListener('pointerdown', event => {
    if (event.target === elements.exportModal) closeExportModal();
  });
  document.querySelectorAll('input[name="exportRole"], input[name="exportScope"], #exportFormat, #exportTimestamps, #exportFrom, #exportTo, #exportMessageLimit, #exportTokenLimit')
    .forEach(control => {
      control.addEventListener('input', () => {
        updateScopePanel();
        updateExportPreview();
      });
      control.addEventListener('change', () => {
        updateScopePanel();
        updateExportPreview();
      });
    });
  elements.attachButton.addEventListener('click', () => elements.fileInput.click());
  elements.fileInput.addEventListener('change', () => addFiles(elements.fileInput.files));
  elements.sendButton.addEventListener('click', sendCurrentMessage);
  elements.composerInput.addEventListener('input', () => {
    updateComposer();
    updateSlashCommandPanel();
    scheduleDraftTextSave();
  });
  elements.slashCommandPanel.querySelectorAll('[data-command]').forEach(button => {
    button.addEventListener('click', () => insertSlashCommand(button.dataset.command));
  });
  elements.composerInput.addEventListener('paste', async event => {
    const imageFiles = [...event.clipboardData.files].filter(file => file.type.startsWith('image/'));
    if (imageFiles.length) await addFiles(imageFiles);
  });

  window.addEventListener('keydown', event => {
    if (event.key === 'F11') {
      event.preventDefault();
      event.stopPropagation();
      post({ type: 'window', action: 'toggleFullscreen' });
    } else if ((event.key === 'Enter' || event.code === 'Enter' || event.code === 'NumpadEnter') && event.ctrlKey && event.shiftKey) {
      event.preventDefault();
      event.stopPropagation();
      if (elements.exportModal.classList.contains('hidden')) sendCurrentMessage();
    } else if (event.ctrlKey && event.key.toLowerCase() === 'l') {
      event.preventDefault();
      elements.composerInput.focus();
    } else if (event.ctrlKey && event.key.toLowerCase() === 'u') {
      event.preventDefault();
      elements.fileInput.click();
    } else if (event.key === 'Escape' && state.consoleFullscreen) {
      event.preventDefault();
      event.stopPropagation();
      post({ type: 'window', action: 'toggleFullscreen' });
    } else if (event.key === 'Escape' && !elements.exportModal.classList.contains('hidden')) {
      event.preventDefault();
      closeExportModal();
    } else if (event.key === 'Escape' && state.attachments.length) {
      const removed = state.attachments;
      state.attachments = [];
      removed.forEach(attachment => {
        if (attachment.draftId) post({ type: 'removeDraftAttachment', draftId: attachment.draftId });
      });
      renderAttachments();
      updateComposer();
      showToast('Attachments cleared.');
    } else if (event.key === 'Escape' && !elements.slashCommandPanel.classList.contains('hidden')) {
      event.preventDefault();
      elements.slashCommandPanel.classList.add('hidden');
    }
  }, true);

  window.powerChatSendCurrentMessage = sendCurrentMessage;

  let dragDepth = 0;
  document.addEventListener('dragenter', event => {
    event.preventDefault();
    dragDepth += 1;
    elements.dropOverlay.classList.add('visible');
  });
  document.addEventListener('dragover', event => event.preventDefault());
  document.addEventListener('dragleave', event => {
    event.preventDefault();
    dragDepth -= 1;
    if (dragDepth <= 0) elements.dropOverlay.classList.remove('visible');
  });
  document.addEventListener('drop', async event => {
    event.preventDefault();
    dragDepth = 0;
    elements.dropOverlay.classList.remove('visible');
    await addFiles(event.dataTransfer.files);
  });

  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', handleHostMessage);
  }

  window.addEventListener('beforeunload', () => {
    localStorage.setItem(draftStorageKey(), elements.composerInput.value);
    post({ type: 'saveDraftText', text: elements.composerInput.value });
  });

  updateClock();
  setInterval(updateClock, 1000);
  setInterval(updateConnectionTimer, 1000);
  updateComposer();
})();
