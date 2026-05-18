// Keyboard shortcuts. Bind once from app.js. Caller provides handlers.
// Cmd/Ctrl is normalized — listen for either.

const META = (e) => e.metaKey || e.ctrlKey;

export function bindKeymap({ onSend, onFocusComposer, onEscape, onNewSession, onToggleTheme }) {
  document.addEventListener('keydown', (event) => {
    // Cmd/Ctrl + Enter — send from anywhere in composer
    if (META(event) && event.key === 'Enter') {
      event.preventDefault();
      onSend?.();
      return;
    }

    // Cmd/Ctrl + K — focus composer
    if (META(event) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      onFocusComposer?.();
      return;
    }

    // Cmd/Ctrl + N — new session
    if (META(event) && event.key.toLowerCase() === 'n') {
      event.preventDefault();
      onNewSession?.();
      return;
    }

    // Cmd/Ctrl + Shift + L — toggle theme
    if (META(event) && event.shiftKey && event.key.toLowerCase() === 'l') {
      event.preventDefault();
      onToggleTheme?.();
      return;
    }

    if (event.key === 'Escape') {
      onEscape?.();
    }
  });
}
