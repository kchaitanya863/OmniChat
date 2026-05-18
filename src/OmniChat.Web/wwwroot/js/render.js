// Render helpers: icons, message bubbles, empty states, markdown pipeline.
// All untrusted content runs through DOMPurify.sanitize(marked.parse(text)).
// If marked/DOMPurify failed to load (offline, blocked), falls back to textContent.

const SVG_NS = 'http://www.w3.org/2000/svg';
const XLINK_NS = 'http://www.w3.org/1999/xlink';

const markdownAvailable = () => typeof window.marked !== 'undefined' && typeof window.DOMPurify !== 'undefined';

export function iconSvg(name, sizeClass = '') {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  if (sizeClass) svg.setAttribute('class', sizeClass);
  const use = document.createElementNS(SVG_NS, 'use');
  use.setAttributeNS(XLINK_NS, 'xlink:href', `/icons/sprite.svg#i-${name}`);
  use.setAttribute('href', `/icons/sprite.svg#i-${name}`);
  svg.appendChild(use);
  return svg;
}

export function el(tag, className, textContent) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (textContent !== undefined && textContent !== null) node.textContent = textContent;
  return node;
}

/** Render markdown to a safe HTML string. Returns null if libs unavailable. */
export function renderMarkdownToHtml(text) {
  if (!markdownAvailable() || typeof text !== 'string') return null;
  try {
    const raw = window.marked.parse(text, { gfm: true, breaks: true });
    return window.DOMPurify.sanitize(raw, {
      ALLOWED_TAGS: [
        'p', 'br', 'strong', 'em', 'code', 'pre', 'blockquote',
        'ul', 'ol', 'li', 'a', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
        'hr', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span', 'del', 'kbd'
      ],
      ALLOWED_ATTR: ['href', 'title', 'lang', 'class'],
      ALLOW_DATA_ATTR: false,
      ADD_ATTR: ['target', 'rel'],
      FORBID_TAGS: ['style', 'script', 'iframe', 'object', 'embed', 'form'],
      FORBID_ATTR: ['onerror', 'onload', 'onclick', 'onmouseover']
    });
  } catch {
    return null;
  }
}

/** Apply rendered markdown to a content element. Falls back to textContent. */
export function setMarkdownContent(contentEl, text) {
  const html = renderMarkdownToHtml(text || '');
  if (html === null) {
    contentEl.textContent = text || '';
    return;
  }
  contentEl.innerHTML = html;
  // Open external links in new tab safely.
  for (const a of contentEl.querySelectorAll('a[href]')) {
    a.setAttribute('target', '_blank');
    a.setAttribute('rel', 'noopener noreferrer');
  }
}

export function buildMessageNode(msg, options = {}) {
  const card = document.createElement('article');
  card.className = `msg ${msg.role === 'assistant' ? 'assistant' : 'user'}`;
  if (msg.id) card.dataset.messageId = msg.id;

  const role = el('div', 'msg-role', msg.role === 'assistant' ? 'Assistant' : 'You');
  if (msg.model) {
    const tag = el('span', 'model-tag', msg.model);
    role.appendChild(tag);
  }

  const content = el('div', 'msg-content');
  setMarkdownContent(content, msg.content || '');

  const meta = el('div', 'msg-meta', formatTimestamp(msg.timestamp));

  card.append(role, content, meta);

  if (!options.skipToolbar) {
    card.appendChild(buildMessageToolbar(msg, options));
  }
  return { card, content, meta };
}

export function buildMessageToolbar(msg, { onCopy, onRegenerate, onEdit, onBranch } = {}) {
  const toolbar = el('div', 'msg-toolbar');
  toolbar.setAttribute('role', 'toolbar');
  toolbar.setAttribute('aria-label', 'Message actions');

  toolbar.appendChild(iconButton('copy', 'Copy message', () => {
    const text = msg.content || '';
    onCopy?.(text, msg);
  }));

  if (msg.role === 'assistant') {
    toolbar.appendChild(iconButton('refresh', 'Regenerate reply', () => onRegenerate?.(msg)));
  }

  if (msg.role === 'user') {
    toolbar.appendChild(iconButton('edit', 'Edit and resend', () => onEdit?.(msg)));
  }

  toolbar.appendChild(iconButton('sparkles', 'Branch conversation here', () => onBranch?.(msg)));

  return toolbar;
}

function iconButton(name, label, onClick) {
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'icon-btn';
  btn.title = label;
  btn.setAttribute('aria-label', label);
  btn.appendChild(iconSvg(name));
  btn.onclick = (event) => {
    event.stopPropagation();
    onClick?.();
  };
  return btn;
}

export function appendMessage(container, msg, options = {}) {
  clearEmptyPlaceholder(container);
  const { card } = buildMessageNode(msg, options);
  container.appendChild(card);
  container.scrollTop = container.scrollHeight;
  return card;
}

/**
 * Begins a streaming assistant bubble.
 * Returns a controller exposing appendDelta(text), finalize(message), fail(text), remove().
 * Deltas append as plain text (safe). Finalize re-renders the full content as sanitized markdown.
 */
export function beginStreamingAssistant(container, opts = {}) {
  clearEmptyPlaceholder(container);

  const card = document.createElement('article');
  card.className = 'msg assistant streaming';

  const role = el('div', 'msg-role', 'Assistant');
  if (opts.model) {
    role.appendChild(el('span', 'model-tag', opts.model));
  }

  const content = el('div', 'msg-content');
  const meta = el('div', 'msg-meta', formatTimestamp(new Date()));

  card.append(role, content, meta);
  container.appendChild(card);
  container.scrollTop = container.scrollHeight;

  let textBuffer = '';

  return {
    appendDelta(text) {
      if (!text) return;
      textBuffer += text;
      content.textContent = textBuffer;
      container.scrollTop = container.scrollHeight;
    },
    finalize(message) {
      card.classList.remove('streaming');
      const final = message?.content ?? textBuffer;
      setMarkdownContent(content, final);
      if (message?.timestamp) {
        meta.textContent = formatTimestamp(message.timestamp);
      }
      if (!opts.skipToolbar) {
        const fullMsg = { role: 'assistant', content: final, timestamp: message?.timestamp, ...message };
        card.appendChild(buildMessageToolbar(fullMsg, opts));
      }
    },
    fail(text) {
      card.classList.remove('streaming');
      card.classList.add('error-bubble');
      content.textContent = text;
    },
    remove() {
      if (card.parentNode) card.remove();
    }
  };
}

export function renderEmptyMessages(container, suggestions = []) {
  container.innerHTML = '';

  const wrap = document.createElement('div');
  wrap.className = 'empty';

  wrap.appendChild(iconSvg('sigil', 'sigil'));
  wrap.appendChild(el('h2', '', 'Ask anything. Stays local.'));
  wrap.appendChild(el('p', '', 'No telemetry by default. Bring your own provider keys and OmniChat streams replies token-by-token, with chat history kept on this machine.'));

  if (suggestions.length > 0) {
    const row = document.createElement('div');
    row.className = 'chip-row';
    for (const s of suggestions) {
      const chip = el('button', 'chip', s.label);
      chip.type = 'button';
      chip.dataset.prompt = s.prompt;
      row.appendChild(chip);
    }
    wrap.appendChild(row);
  }

  container.appendChild(wrap);
}

export function renderNoSessionEmpty(container) {
  container.innerHTML = '';
  const wrap = el('div', 'empty');
  wrap.appendChild(iconSvg('message', 'sigil'));
  wrap.appendChild(el('h2', '', 'No session selected'));
  wrap.appendChild(el('p', '', 'Create a new session from the sidebar to begin a conversation.'));
  container.appendChild(wrap);
}

export function clearEmptyPlaceholder(container) {
  const placeholder = container.querySelector('.empty');
  if (placeholder) placeholder.remove();
}

function formatTimestamp(value) {
  if (!value) return '';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return '';
  }
}

// ---- Tool-call inline card (M8/M9) ----
export function buildToolCard(toolName, verb = 'Running') {
  const card = el('div', 'tool-card');
  const spinner = el('span', 'spinner');
  const label = el('span', '', `${verb} ${toolName}…`);
  card.append(spinner, label);

  return {
    node: card,
    complete(summary) {
      card.classList.add('collapsed');
      card.innerHTML = '';
      card.appendChild(iconSvg('sparkles'));
      card.appendChild(el('span', '', summary || `${toolName} complete`));
    },
    fail(message) {
      card.classList.add('error-bubble');
      card.innerHTML = '';
      card.appendChild(iconSvg('x'));
      card.appendChild(el('span', '', `${toolName} failed: ${message}`));
    }
  };
}

// ---- Toast system ----
let toastStack = null;

function ensureToastStack() {
  if (toastStack) return toastStack;
  toastStack = document.createElement('div');
  toastStack.className = 'toast-stack';
  toastStack.setAttribute('aria-live', 'polite');
  toastStack.setAttribute('aria-atomic', 'true');
  document.body.appendChild(toastStack);
  return toastStack;
}

export function toast(message, kind = 'info', durationMs = 3200) {
  const stack = ensureToastStack();
  const node = document.createElement('div');
  node.className = `toast ${kind}`;
  node.appendChild(iconSvg(kind === 'danger' ? 'x' : kind === 'success' ? 'sparkles' : 'message'));
  node.appendChild(el('span', '', message));
  stack.appendChild(node);

  const timer = setTimeout(() => dismiss(), durationMs);
  function dismiss() {
    clearTimeout(timer);
    node.classList.add('leaving');
    node.addEventListener('animationend', () => node.remove(), { once: true });
  }
  node.onclick = dismiss;
  return dismiss;
}
