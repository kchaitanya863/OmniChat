// OmniChat app entry. Wires DOM, loads data, drives state.
// All view code lives in render.js. All HTTP/SSE lives in api.js.

import { store } from './state.js';
import { fetchJson, postJson, del, streamSse } from './api.js';
import {
  appendMessage,
  beginStreamingAssistant,
  buildMessageNode,
  iconSvg,
  el,
  renderEmptyMessages,
  renderNoSessionEmpty,
  setMarkdownContent,
  toast
} from './render.js';
import { bindKeymap } from './keymap.js';

const THEME_KEY = 'omnichat.theme';
const SUGGESTIONS = [
  { label: 'Brainstorm', prompt: 'Brainstorm three approaches for...' },
  { label: 'Summarize', prompt: 'Summarize the key points of...' },
  { label: 'Explain code', prompt: 'Explain what this code does:\n```\n\n```' },
  { label: 'Compare options', prompt: 'Compare X and Y across cost, performance, and maintenance.' }
];

// ---- DOM refs ----
const $ = (id) => document.getElementById(id);
const refs = {
  messages: $('messages'),
  sessions: $('sessionsList'),
  sessionSearch: $('sessionSearch'),
  statusText: $('statusText'),
  statusDot: $('statusDot'),
  activeSessionLabel: $('activeSessionLabel'),
  errorBox: $('errorBox'),
  sendBtn: $('sendBtn'),
  promptEl: $('prompt'),
  modelEl: $('modelInput'),
  newSessionTitle: $('newSessionTitle'),
  createSessionBtn: $('createSessionBtn'),
  themeToggleBtn: $('themeToggleBtn'),
  openSettingsBtn: $('openSettingsBtn'),
  closeSettingsBtn: $('closeSettingsBtn'),
  settingsDrawer: $('settingsDrawer'),
  exportMdBtn: $('exportMdBtn'),
  exportJsonBtn: $('exportJsonBtn'),
  onboardingShell: $('onboardingShell'),
  onboardingScroller: $('onboardingScroller'),
  onboardingDots: $('onboardingDots'),
  onboardingSkipBtn: $('onboardingSkipBtn'),
  onboardingNextBtn: $('onboardingNextBtn'),
  cmdShell: $('cmdShell'),
  cmdInput: $('cmdInput'),
  cmdList: $('cmdList'),
  voiceBtn: $('voiceBtn'),

  providerSelect: $('providerSelect'),
  providerCountPill: $('providerCountPill'),
  providerFingerprint: $('providerFingerprint'),
  toggleProviderFormBtn: $('toggleProviderFormBtn'),
  providerForm: $('providerForm'),
  newProviderName: $('newProviderName'),
  newProviderType: $('newProviderType'),
  newProviderModel: $('newProviderModel'),
  newProviderKey: $('newProviderKey'),
  newProviderBaseUrl: $('newProviderBaseUrl'),
  newProviderSystemPrompt: $('newProviderSystemPrompt'),
  saveProviderBtn: $('saveProviderBtn'),
  cancelProviderBtn: $('cancelProviderBtn'),
  deleteProviderBtn: $('deleteProviderBtn'),

  chunkingStrategy: $('chunkingStrategy'),
  storageMode: $('storageMode'),
  keepLatestSessions: $('keepLatestSessions'),
  saveSettingsBtn: $('saveSettingsBtn'),
  cleanupLatestBtn: $('cleanupLatestBtn'),
  clearAllBtn: $('clearAllBtn'),
  refreshStorageBtn: $('refreshStorageBtn'),
  storageSummary: $('storageSummary')
};

// ---- Status / errors ----
function setStatus(text, busy = false) {
  refs.statusText.textContent = text;
  refs.statusDot.classList.remove('busy', 'error');
  if (busy) refs.statusDot.classList.add('busy');
  if (text === 'Error') refs.statusDot.classList.add('error');

  store.set({ loading: busy, statusText: text });
  refs.sendBtn.disabled = busy;
  refs.createSessionBtn.disabled = busy;
  refs.saveSettingsBtn.disabled = busy;
  refs.cleanupLatestBtn.disabled = busy;
  refs.clearAllBtn.disabled = busy;
  refs.refreshStorageBtn.disabled = busy;
  refs.toggleProviderFormBtn.disabled = busy;
  refs.saveProviderBtn.disabled = busy;
}

function setError(message) {
  if (!message) {
    refs.errorBox.classList.remove('visible');
    refs.errorBox.textContent = '';
    return;
  }
  refs.errorBox.textContent = message;
  refs.errorBox.classList.add('visible');
}

// ---- Theme ----
function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  store.set({ theme });
  refs.themeToggleBtn.setAttribute('aria-label', theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme');
}

function loadInitialTheme() {
  let theme;
  try {
    theme = localStorage.getItem(THEME_KEY);
  } catch { /* private mode */ }
  if (!theme) {
    theme = window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }
  applyTheme(theme === 'light' ? 'light' : 'dark');
}

function toggleTheme() {
  const next = (store.get().theme === 'dark') ? 'light' : 'dark';
  applyTheme(next);
  try { localStorage.setItem(THEME_KEY, next); } catch { /* ignore */ }
}

// ---- Sessions ----
async function loadSessions() {
  const list = await fetchJson('/api/sessions');
  store.set({ sessions: list });
  renderSessions();

  let { selectedSessionId } = store.get();
  if (list.length === 0) {
    refs.activeSessionLabel.textContent = 'No session selected';
    return;
  }
  if (!selectedSessionId || !list.some(s => s.id === selectedSessionId)) {
    selectedSessionId = list[0].id;
    store.set({ selectedSessionId });
  }
  const active = list.find(s => s.id === selectedSessionId) ?? list[0];
  refs.activeSessionLabel.textContent = active.title;
}

function renderSessions() {
  const list = store.get().sessions;
  const query = (refs.sessionSearch.value || '').trim().toLowerCase();
  refs.sessions.innerHTML = '';

  if (list.length === 0) {
    refs.sessions.appendChild(el('div', 'subtle', 'No sessions yet. Create one to get started.'));
    return;
  }

  const matches = query
    ? list.filter(s =>
        (s.title || '').toLowerCase().includes(query) ||
        (s.lastMessageContent || '').toLowerCase().includes(query))
    : list;

  if (matches.length === 0) {
    refs.sessions.appendChild(el('div', 'subtle', 'No sessions match this search.'));
    return;
  }

  const buckets = bucketSessions(matches);
  for (const { label, items } of buckets) {
    if (items.length === 0) continue;
    refs.sessions.appendChild(el('div', 'msg-role', label));
    for (const session of items) {
      refs.sessions.appendChild(renderSessionCard(session));
    }
  }
}

function bucketSessions(sessions) {
  const now = new Date();
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const startOfYesterday = startOfToday - 86_400_000;
  const startOfWeek = startOfToday - 6 * 86_400_000;

  const pinned = [];
  const today = [];
  const yesterday = [];
  const thisWeek = [];
  const older = [];

  for (const session of sessions) {
    if (session.pinned) { pinned.push(session); continue; }
    const ts = session.lastMessageTimestamp ? new Date(session.lastMessageTimestamp).getTime() : 0;
    if (ts >= startOfToday) today.push(session);
    else if (ts >= startOfYesterday) yesterday.push(session);
    else if (ts >= startOfWeek) thisWeek.push(session);
    else older.push(session);
  }

  return [
    { label: 'Pinned', items: pinned },
    { label: 'Today', items: today },
    { label: 'Yesterday', items: yesterday },
    { label: 'This week', items: thisWeek },
    { label: 'Older', items: older }
  ];
}

function renderSessionCard(session) {
  const card = el('button', 'session');
  card.type = 'button';
  if (session.id === store.get().selectedSessionId) card.classList.add('active');

  const titleRow = el('div', 'row-tight split');
  const title = el('div', 'session-title', session.title || 'Untitled');
  titleRow.appendChild(title);
  if (session.pinned) {
    const pinPill = el('span', 'pill amber', 'pinned');
    titleRow.appendChild(pinPill);
  }
  const meta = el('div', 'session-meta', `${session.messageCount} ${session.messageCount === 1 ? 'message' : 'messages'}`);
  card.append(titleRow, meta);

  card.onclick = async (event) => {
    if (event.defaultPrevented || store.get().loading) return;
    store.set({ selectedSessionId: session.id });
    await loadSession(session.id);
    for (const c of refs.sessions.querySelectorAll('.session')) c.classList.remove('active');
    card.classList.add('active');
  };

  card.oncontextmenu = (event) => {
    event.preventDefault();
    togglePin(session);
  };
  return card;
}

async function togglePin(session) {
  try {
    await fetch(`/api/sessions/${session.id}`, {
      method: 'PATCH',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ pinned: !session.pinned })
    });
    await loadSessions();
    toast(session.pinned ? 'Unpinned' : 'Pinned', 'success', 1600);
  } catch (error) {
    toast(error.message || 'Pin failed', 'danger');
  }
}

function messageToolbarOptions() {
  return {
    onCopy: copyMessage,
    onRegenerate: regenerateMessage,
    onEdit: editMessage,
    onBranch: branchAt
  };
}

async function loadSession(id) {
  if (!id) { renderNoSessionEmpty(refs.messages); return; }
  const session = await fetchJson(`/api/sessions/${id}`);
  refs.messages.innerHTML = '';

  if (!session.messages || session.messages.length === 0) {
    renderEmptyMessages(refs.messages, SUGGESTIONS);
    wireSuggestionChips();
    return;
  }

  const opts = messageToolbarOptions();
  for (const msg of session.messages) {
    const { card } = buildMessageNode(msg, opts);
    refs.messages.appendChild(card);
  }
  refs.messages.scrollTop = refs.messages.scrollHeight;
}

async function copyMessage(text) {
  try {
    await navigator.clipboard.writeText(text);
    toast('Copied to clipboard', 'success', 1800);
  } catch {
    toast('Copy failed — clipboard not available', 'danger');
  }
}

async function regenerateMessage(targetMsg) {
  const { loading, selectedSessionId, activeProviderConfigId } = store.get();
  if (loading || !selectedSessionId || !targetMsg?.id) return;

  const card = refs.messages.querySelector(`.msg[data-message-id="${cssEscape(targetMsg.id)}"]`);
  setStatus('Regenerating...', true);
  setError('');

  if (card) card.remove();
  const stream = beginStreamingAssistant(refs.messages, {
    model: targetMsg.model || null,
    ...messageToolbarOptions()
  });

  let receivedAnyDelta = false;
  let finalAssistant = null;
  let streamError = null;

  try {
    for await (const { event, data } of streamSse(
      `/api/sessions/${selectedSessionId}/regenerate`,
      { targetMessageId: targetMsg.id, providerConfigId: activeProviderConfigId, model: targetMsg.model || null }
    )) {
      if (event === 'delta' && data?.delta) { stream.appendDelta(data.delta); receivedAnyDelta = true; }
      else if (event === 'done') { finalAssistant = data?.assistantMessage || null; }
      else if (event === 'error') { streamError = data?.message || 'Provider error'; }
    }
    if (streamError) {
      stream.fail(streamError);
      setError(streamError);
      setStatus('Error', false);
    } else {
      stream.finalize(finalAssistant);
      setStatus('Ready', false);
      toast('Regenerated', 'success', 1800);
    }
    await loadSessions();
  } catch (error) {
    if (!receivedAnyDelta) stream.remove();
    setStatus('Error', false);
    setError(error.message || 'Failed to regenerate');
  }
}

function editMessage(msg) {
  refs.promptEl.value = msg.content || '';
  refs.promptEl.focus();
  toast('Loaded into composer. Send a follow-up or branch.', 'info', 2400);
}

function branchAt(msg) {
  toast('Branching lands at M7 (needs ParentId persistence).', 'info', 3200);
}

function cssEscape(value) {
  if (window.CSS?.escape) return window.CSS.escape(value);
  return String(value).replace(/[^a-zA-Z0-9_-]/g, (c) => `\\${c}`);
}

function wireSuggestionChips() {
  for (const chip of refs.messages.querySelectorAll('.chip[data-prompt]')) {
    chip.addEventListener('click', () => {
      refs.promptEl.value = chip.dataset.prompt;
      refs.promptEl.focus();
    });
  }
}

async function createSession() {
  if (store.get().loading) return;
  const title = refs.newSessionTitle.value.trim() || 'New Session';
  setStatus('Creating session...', true);
  setError('');

  try {
    const response = await postJson('/api/sessions', { title });
    store.set({ selectedSessionId: response.id });
    await loadSessions();
    await loadSession(response.id);
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Failed to create session');
  }
}

// ---- Providers ----
async function loadProviders() {
  const response = await fetchJson('/api/providers');
  const list = response.providers || [];
  store.set({ providers: list });
  refs.providerCountPill.textContent = String(list.length);

  const prior = store.get().activeProviderConfigId;
  refs.providerSelect.innerHTML = '';
  if (list.length === 0) {
    const opt = document.createElement('option');
    opt.value = '';
    opt.textContent = 'No providers';
    refs.providerSelect.appendChild(opt);
    store.set({ activeProviderConfigId: null });
    refs.providerFingerprint.textContent = '';
    return;
  }

  for (const p of list) {
    const opt = document.createElement('option');
    opt.value = p.id;
    opt.textContent = `${p.displayName} · ${p.providerType}${p.hasKey ? '' : ' (no key)'}`;
    refs.providerSelect.appendChild(opt);
  }

  const active = (prior && list.some(p => p.id === prior)) ? prior : list[0].id;
  store.set({ activeProviderConfigId: active });
  refs.providerSelect.value = active;
  updateFingerprintUi();
}

function updateFingerprintUi() {
  const { providers, activeProviderConfigId } = store.get();
  const provider = providers.find(p => p.id === activeProviderConfigId);
  if (!provider) { refs.providerFingerprint.textContent = ''; return; }
  if (provider.hasKey && provider.keyFingerprint) {
    refs.providerFingerprint.textContent = `Key: ${provider.keyFingerprint}`;
  } else {
    refs.providerFingerprint.textContent = provider.providerType === 'Custom'
      ? 'Echo provider — no key required.'
      : 'No key on file. Add one to enable streaming.';
  }
  refs.deleteProviderBtn.disabled = provider.id === 'echo-default';
}

function showProviderForm(show) {
  refs.providerForm.classList.toggle('visible', show);
  if (show) refs.newProviderName.focus();
}

async function saveProvider() {
  if (store.get().loading) return;
  const displayName = refs.newProviderName.value.trim();
  const providerType = refs.newProviderType.value;
  const model = refs.newProviderModel.value.trim();
  const apiKey = refs.newProviderKey.value;
  const baseUrl = refs.newProviderBaseUrl.value.trim();
  const systemPrompt = refs.newProviderSystemPrompt.value.trim();

  if (!displayName || !model) {
    setError('Display name and model are required.');
    return;
  }

  setStatus('Saving provider...', true);
  setError('');

  try {
    const saved = await postJson('/api/providers', {
      displayName,
      providerType,
      model,
      apiKey: apiKey || null,
      baseUrl: baseUrl || null,
      systemPrompt: systemPrompt || null
    });
    refs.newProviderName.value = '';
    refs.newProviderModel.value = '';
    refs.newProviderKey.value = '';
    refs.newProviderBaseUrl.value = '';
    refs.newProviderSystemPrompt.value = '';
    showProviderForm(false);
    store.set({ activeProviderConfigId: saved.id });
    await loadProviders();
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Failed to save provider');
  }
}

async function deleteActiveProvider() {
  const { activeProviderConfigId } = store.get();
  if (!activeProviderConfigId || activeProviderConfigId === 'echo-default') return;
  if (!confirm('Delete this provider configuration?')) return;
  setStatus('Deleting provider...', true);
  setError('');
  try {
    await del(`/api/providers/${activeProviderConfigId}`);
    store.set({ activeProviderConfigId: null });
    await loadProviders();
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Failed to delete provider');
  }
}

// ---- Settings + storage ----
async function loadSettings() {
  const settings = await fetchJson('/api/settings');
  store.set({ settings });
  refs.chunkingStrategy.value = settings.chunkingStrategy || 'balanced';
  refs.storageMode.value = settings.storageMode || 'ephemeral';
  refs.keepLatestSessions.value = settings.keepLatestSessions ?? 10;
}

async function loadStorageSummary() {
  const summary = await fetchJson('/api/storage/summary');
  store.set({ storageSummary: summary });
  refs.storageSummary.textContent =
    `Sessions ${summary.sessionCount} · Messages ${summary.messageCount} · Chars ${summary.approximateContentChars} · ` +
    `${summary.storageMode} mode · keep latest ${summary.keepLatestSessions}`;
}

async function saveSettings() {
  if (store.get().loading) return;
  setStatus('Saving controls...', true);
  setError('');
  try {
    await postJson('/api/settings', {
      chunkingStrategy: refs.chunkingStrategy.value,
      storageMode: refs.storageMode.value,
      keepLatestSessions: Number.parseInt(refs.keepLatestSessions.value, 10)
    });
    await loadStorageSummary();
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Failed to save controls');
  }
}

async function cleanupStorage(mode) {
  if (store.get().loading) return;
  setStatus(mode === 'clear_all' ? 'Clearing storage...' : 'Trimming storage...', true);
  setError('');
  try {
    await postJson('/api/storage/cleanup', {
      mode,
      keepLatestSessions: Number.parseInt(refs.keepLatestSessions.value, 10)
    });
    await refreshAll();
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Storage cleanup failed');
  }
}

async function refreshStorageSummary() {
  if (store.get().loading) return;
  setStatus('Refreshing summary...', true);
  setError('');
  try {
    await loadStorageSummary();
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Ready', false);
    setError(error.message || 'Failed to refresh summary');
  }
}

// ---- Streaming send ----
async function sendMessage() {
  const { loading, selectedSessionId, activeProviderConfigId } = store.get();
  if (loading || !selectedSessionId) return;

  const content = (refs.promptEl.value || '').trim();
  if (!content) {
    setError('Please enter a prompt before sending.');
    return;
  }

  const model = (refs.modelEl.value || '').trim() || null;
  setStatus('Streaming...', true);
  setError('');

  const opts = messageToolbarOptions();
  const optimisticUserCard = appendMessage(refs.messages, {
    role: 'user',
    content,
    timestamp: new Date().toISOString()
  }, opts);
  refs.promptEl.value = '';

  const stream = beginStreamingAssistant(refs.messages, { model, ...opts });
  let receivedAnyDelta = false;
  let finalAssistant = null;
  let streamError = null;

  try {
    for await (const { event, data } of streamSse(
      `/api/sessions/${selectedSessionId}/stream`,
      { content, providerConfigId: activeProviderConfigId, model }
    )) {
      if (event === 'delta') {
        if (data?.delta) {
          stream.appendDelta(data.delta);
          receivedAnyDelta = true;
        }
      } else if (event === 'done') {
        finalAssistant = data?.assistantMessage || null;
      } else if (event === 'error') {
        streamError = data?.message || 'Provider error';
      }
    }

    if (streamError) {
      stream.fail(streamError);
      setError(streamError);
      setStatus('Error', false);
    } else {
      stream.finalize(finalAssistant);
      setStatus('Ready', false);
    }
    await loadSessions();
  } catch (error) {
    if (!receivedAnyDelta) {
      stream.remove();
      if (optimisticUserCard?.parentNode) optimisticUserCard.remove();
      refs.promptEl.value = content;
    } else {
      stream.fail(`Stream interrupted: ${error.message || error}`);
    }
    setStatus('Error', false);
    setError(error.message || 'Failed to stream response');
  }
}

// ---- Orchestration ----
async function refreshAll() {
  setStatus('Syncing...', true);
  setError('');
  try {
    await loadProviders();
    await loadSessions();
    await loadSession(store.get().selectedSessionId);
    await loadSettings();
    await loadStorageSummary();
    setStatus('Ready', false);
  } catch (error) {
    setStatus('Error', false);
    setError(error.message || 'Unexpected error');
  }
}

// ---- Wiring ----
function wire() {
  refs.createSessionBtn.onclick = createSession;
  refs.sendBtn.onclick = sendMessage;
  refs.saveSettingsBtn.onclick = saveSettings;
  refs.refreshStorageBtn.onclick = refreshStorageSummary;
  refs.cleanupLatestBtn.onclick = () => cleanupStorage('keep_latest');
  refs.clearAllBtn.onclick = () => cleanupStorage('clear_all');
  refs.toggleProviderFormBtn.onclick = () =>
    showProviderForm(!refs.providerForm.classList.contains('visible'));
  refs.cancelProviderBtn.onclick = () => showProviderForm(false);
  refs.saveProviderBtn.onclick = saveProvider;
  refs.deleteProviderBtn.onclick = deleteActiveProvider;
  refs.providerSelect.onchange = () => {
    store.set({ activeProviderConfigId: refs.providerSelect.value || null });
    updateFingerprintUi();
  };
  refs.themeToggleBtn.onclick = toggleTheme;
  refs.sessionSearch.addEventListener('input', renderSessions);

  refs.openSettingsBtn.onclick = () => openSettings(true);
  refs.closeSettingsBtn.onclick = () => openSettings(false);
  refs.exportMdBtn.onclick = () => exportSession('md');
  refs.exportJsonBtn.onclick = () => exportSession('json');

  refs.onboardingSkipBtn.onclick = closeOnboarding;
  refs.onboardingNextBtn.onclick = onboardingNext;
  for (const dot of refs.onboardingDots.querySelectorAll('.onboarding-dot')) {
    dot.onclick = () => scrollOnboardingTo(Number(dot.dataset.target));
  }
  refs.onboardingScroller.addEventListener('scroll', syncOnboardingDots);

  refs.cmdInput.addEventListener('input', renderCmdResults);
  refs.cmdInput.addEventListener('keydown', cmdKeyHandler);
  refs.voiceBtn.onclick = toggleVoice;

  bindKeymap({
    onSend: sendMessage,
    onFocusComposer: () => refs.promptEl.focus(),
    onNewSession: createSession,
    onToggleTheme: toggleTheme,
    onEscape: () => {
      if (refs.cmdShell.classList.contains('open')) { closeCmd(); return; }
      if (!refs.settingsDrawer.hidden) { openSettings(false); return; }
      if (refs.providerForm.classList.contains('visible')) showProviderForm(false);
    }
  });

  // Cmd+/  → focus session search; Cmd+Shift+K → command palette
  document.addEventListener('keydown', (event) => {
    const meta = event.metaKey || event.ctrlKey;
    if (meta && event.key === '/') {
      event.preventDefault();
      refs.sessionSearch.focus();
      refs.sessionSearch.select();
    }
    if (meta && event.shiftKey && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      openCmd();
    }
    if (meta && event.shiftKey && event.key.toLowerCase() === 'm') {
      event.preventDefault();
      toggleVoice();
    }
  });
}

// ---- Settings drawer ----
function openSettings(open) {
  refs.settingsDrawer.hidden = !open;
  refs.settingsDrawer.classList.toggle('open', open);
}

async function exportSession(format) {
  const { selectedSessionId } = store.get();
  if (!selectedSessionId) { toast('No session selected', 'warn'); return; }
  window.open(`/api/sessions/${selectedSessionId}/export?format=${format}`, '_blank');
}

// ---- Onboarding ----
const ONBOARDING_KEY = 'omnichat.onboarded';

function maybeShowOnboarding() {
  let onboarded;
  try { onboarded = localStorage.getItem(ONBOARDING_KEY) === '1'; } catch { onboarded = true; }
  if (onboarded) return;
  refs.onboardingShell.hidden = false;
  refs.onboardingShell.classList.add('visible');
}

function closeOnboarding() {
  refs.onboardingShell.classList.remove('visible');
  refs.onboardingShell.hidden = true;
  try { localStorage.setItem(ONBOARDING_KEY, '1'); } catch { /* ignore */ }
}

function currentOnboardingIndex() {
  const width = refs.onboardingScroller.clientWidth;
  return width === 0 ? 0 : Math.round(refs.onboardingScroller.scrollLeft / width);
}

function scrollOnboardingTo(index) {
  const width = refs.onboardingScroller.clientWidth;
  refs.onboardingScroller.scrollTo({ left: width * index, behavior: 'smooth' });
}

function onboardingNext() {
  const idx = currentOnboardingIndex();
  if (idx >= 2) { closeOnboarding(); openSettings(true); return; }
  scrollOnboardingTo(idx + 1);
}

function syncOnboardingDots() {
  const idx = currentOnboardingIndex();
  for (const dot of refs.onboardingDots.querySelectorAll('.onboarding-dot')) {
    dot.classList.toggle('active', Number(dot.dataset.target) === idx);
  }
  refs.onboardingNextBtn.textContent = idx === 2 ? 'Add a provider' : 'Next';
}

// ---- Command palette ----
function openCmd() {
  refs.cmdShell.hidden = false;
  refs.cmdShell.classList.add('open');
  refs.cmdInput.value = '';
  renderCmdResults();
  refs.cmdInput.focus();
}
function closeCmd() {
  refs.cmdShell.classList.remove('open');
  refs.cmdShell.hidden = true;
}

const COMMANDS = [
  { id: 'new', label: 'New session', hint: '⌘+N', run: () => createSession() },
  { id: 'theme', label: 'Toggle theme', hint: '⌘+⇧+L', run: () => toggleTheme() },
  { id: 'settings', label: 'Open settings', run: () => openSettings(true) },
  { id: 'export-md', label: 'Export current session as Markdown', run: () => exportSession('md') },
  { id: 'export-json', label: 'Export current session as JSON', run: () => exportSession('json') },
  { id: 'add-provider', label: 'Add provider', run: () => { openSettings(false); showProviderForm(true); } },
  { id: 'focus-search', label: 'Search sessions', hint: '⌘+/', run: () => refs.sessionSearch.focus() }
];

function renderCmdResults() {
  const query = refs.cmdInput.value.trim().toLowerCase();
  const items = COMMANDS.filter(c => !query || c.label.toLowerCase().includes(query));
  refs.cmdList.innerHTML = '';
  for (const item of items) {
    const li = el('li', 'cmd-item');
    li.dataset.id = item.id;
    li.append(el('span', '', item.label));
    if (item.hint) li.append(el('span', 'kbd-hint', item.hint));
    li.onclick = () => { closeCmd(); item.run(); };
    refs.cmdList.appendChild(li);
  }
  const first = refs.cmdList.firstElementChild;
  if (first) first.classList.add('active');
}

// ---- Voice input (browser WebSpeech, no server hop) ----
let voiceRecognizer = null;
let voiceActive = false;

function toggleVoice() {
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRecognition) { toast('Voice input not supported in this browser.', 'warn'); return; }

  if (voiceActive && voiceRecognizer) {
    voiceRecognizer.stop();
    return;
  }

  voiceRecognizer = new SpeechRecognition();
  voiceRecognizer.continuous = true;
  voiceRecognizer.interimResults = true;
  voiceRecognizer.lang = navigator.language || 'en-US';

  const baseValue = refs.promptEl.value;
  let interim = '';

  voiceRecognizer.onresult = (event) => {
    let finals = '';
    let pendingInterim = '';
    for (let i = event.resultIndex; i < event.results.length; i++) {
      const transcript = event.results[i][0].transcript;
      if (event.results[i].isFinal) finals += transcript;
      else pendingInterim += transcript;
    }
    if (finals) refs.promptEl.value = (baseValue + interim + finals).trim();
    refs.promptEl.value = (baseValue + interim + pendingInterim).trim();
    if (finals) interim += finals;
  };
  voiceRecognizer.onerror = (e) => { toast(`Voice error: ${e.error}`, 'danger'); };
  voiceRecognizer.onend = () => {
    voiceActive = false;
    refs.voiceBtn.classList.remove('active');
    refs.voiceBtn.title = 'Voice input';
  };
  voiceRecognizer.start();
  voiceActive = true;
  refs.voiceBtn.classList.add('active');
  refs.voiceBtn.title = 'Stop voice input';
  toast('Listening… click mic again or pause to stop.', 'info', 2400);
}

function cmdKeyHandler(event) {
  const items = [...refs.cmdList.querySelectorAll('.cmd-item')];
  if (items.length === 0) return;
  let active = items.findIndex(i => i.classList.contains('active'));
  if (active < 0) active = 0;

  if (event.key === 'ArrowDown') {
    event.preventDefault();
    items[active].classList.remove('active');
    items[(active + 1) % items.length].classList.add('active');
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    items[active].classList.remove('active');
    items[(active - 1 + items.length) % items.length].classList.add('active');
  } else if (event.key === 'Enter') {
    event.preventDefault();
    items[active].click();
  } else if (event.key === 'Escape') {
    event.preventDefault();
    closeCmd();
  }
}

// ---- Boot ----
loadInitialTheme();
wire();
refreshAll().then(maybeShowOnboarding);
