// Thin HTTP / SSE client. Keep request shapes here so the rest of the app
// stays unaware of fetch + ReadableStream plumbing.

export async function fetchJson(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(text || `Request failed (${response.status})`);
  }
  return response.json();
}

// Convenience POST that JSON-encodes body.
export function postJson(url, body) {
  return fetchJson(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
}

// Convenience DELETE.
export async function del(url) {
  const response = await fetch(url, { method: 'DELETE' });
  if (!response.ok && response.status !== 204) {
    const text = await response.text().catch(() => '');
    throw new Error(text || `Delete failed (${response.status})`);
  }
}

/**
 * POSTs JSON to an SSE endpoint and yields parsed events.
 * Each yielded value is { event: string, data: any }.
 * Throws on non-2xx responses; respects AbortSignal.
 */
export async function* streamSse(url, body, signal) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json', accept: 'text/event-stream' },
    body: JSON.stringify(body),
    signal
  });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(text || `Stream request failed (${response.status})`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    let separator;
    while ((separator = buffer.indexOf('\n\n')) !== -1) {
      const block = buffer.slice(0, separator);
      buffer = buffer.slice(separator + 2);

      let eventName = 'message';
      const dataLines = [];
      for (const line of block.split('\n')) {
        if (line.startsWith('event:')) {
          eventName = line.slice(6).trim();
        } else if (line.startsWith('data:')) {
          dataLines.push(line.slice(5).trim());
        }
      }
      if (dataLines.length === 0) continue;

      try {
        const data = JSON.parse(dataLines.join('\n'));
        yield { event: eventName, data };
      } catch {
        // Ignore malformed payload — server should not emit these.
      }
    }
  }
}
