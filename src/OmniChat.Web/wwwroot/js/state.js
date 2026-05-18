// Minimal pub/sub state store. Pure ES module, no deps.
// If this file grows past ~150 LOC or causes cross-surface coordination bugs,
// see DESIGN_SYSTEM.md §"State scaling" for the @preact/signals-core migration path.

const state = {
  selectedSessionId: null,
  sessions: [],
  messages: [],
  providers: [],
  activeProviderConfigId: null,
  settings: { chunkingStrategy: 'balanced', storageMode: 'ephemeral', keepLatestSessions: 10 },
  storageSummary: null,
  loading: false,
  errorText: '',
  statusText: 'Ready',
  theme: 'dark'
};

const listeners = new Map();
let nextId = 1;

function get() { return state; }

function set(patch) {
  Object.assign(state, patch);
  for (const cb of listeners.values()) cb(state);
}

function on(callback) {
  const id = nextId++;
  listeners.set(id, callback);
  callback(state);
  return () => listeners.delete(id);
}

export const store = { get, set, on };
