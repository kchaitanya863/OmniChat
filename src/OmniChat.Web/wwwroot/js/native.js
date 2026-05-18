// Web-side native bridge. When running inside the MAUI WebView shell, `window.omnichat` is
// injected by the host (see /src/OmniChat.Maui/MainPage.xaml.cs). On plain browsers, this
// module exposes a feature-detect helper that falls back to web APIs (localStorage, navigator.share, etc.).

export const native = {
  get available() { return typeof window.omnichat === 'object' && window.omnichat !== null; },

  async secureStorageGet(key) {
    if (native.available) return (await window.omnichat.secureStorage.get(key))?.value ?? null;
    try { return localStorage.getItem(key); } catch { return null; }
  },

  async secureStorageSet(key, value) {
    if (native.available) return window.omnichat.secureStorage.set(key, value);
    try { localStorage.setItem(key, value); } catch { /* private mode */ }
  },

  async secureStorageDelete(key) {
    if (native.available) return window.omnichat.secureStorage.delete(key);
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  },

  async share(text) {
    if (native.available) return window.omnichat.share(text);
    if (navigator.share) { await navigator.share({ text }); return; }
    await navigator.clipboard?.writeText?.(text);
  },

  async pickFile(accept = '.pdf,.docx,.txt,.md') {
    if (native.available) return window.omnichat.pickFile(accept);
    return new Promise((resolve) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = accept;
      input.onchange = async () => {
        const file = input.files?.[0];
        if (!file) { resolve({ cancelled: true }); return; }
        const buffer = await file.arrayBuffer();
        const base64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
        resolve({ name: file.name, mime: file.type, base64 });
      };
      input.click();
    });
  },

  haptic(kind = 'light') {
    if (native.available) return window.omnichat.haptic(kind);
    if (navigator.vibrate) navigator.vibrate(kind === 'long' ? 25 : 10);
  },

  async biometricUnlock() {
    if (native.available) return window.omnichat.biometric.unlock();
    return { unsupported: true };
  }
};
