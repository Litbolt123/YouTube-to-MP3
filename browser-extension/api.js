const YTDL_DEFAULT_PORT = 47384;
const YTDL_APP_NAME = "YouTube Downloader";

async function ytdlGetSettings() {
  return chrome.storage.sync.get({
    port: YTDL_DEFAULT_PORT,
    token: "",
    defaultAudioFormat: "mp3",
    defaultVideoFormat: "mp4",
  });
}

async function ytdlRequestApp(path, options = {}) {
  const { port, token } = await ytdlGetSettings();
  if (!token) {
    throw new Error("Set your connection token in the extension options.");
  }

  const response = await fetch(`http://127.0.0.1:${port}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-Extension-Token": token,
      ...(options.headers || {}),
    },
  });

  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.error || `Request failed (${response.status})`);
  }
  return data;
}

function ytdlConnectionError(error) {
  const msg = error?.message || "";
  if (msg.includes("Failed to fetch") || msg.includes("NetworkError")) {
    return "Could not reach the app. Is it running?";
  }
  return msg || "Could not send to app";
}

async function ytdlSendRuntimeMessage(message) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendMessage(message, (response) => {
        if (chrome.runtime.lastError) {
          resolve({
            ok: false,
            error: chrome.runtime.lastError.message,
            swUnavailable: true,
          });
          return;
        }
        resolve(response ?? { ok: false, error: "No response from extension.", swUnavailable: true });
      });
    } catch (error) {
      resolve({ ok: false, error: error.message, swUnavailable: true });
    }
  });
}

async function ytdlHealthCheck() {
  const viaSw = await ytdlSendRuntimeMessage({ type: "health-check" });
  if (viaSw?.ok) return viaSw;

  try {
    const { port } = await ytdlGetSettings();
    const response = await fetch(`http://127.0.0.1:${port}/health`);
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(`App responded with status ${response.status}`);
    }
    return { ok: true, data };
  } catch (error) {
    return { ok: false, error: ytdlConnectionError(error) };
  }
}

async function ytdlCheckUrl(url) {
  const viaSw = await ytdlSendRuntimeMessage({
    type: "check-url",
    url,
  });
  if (viaSw?.ok) return viaSw;

  try {
    const data = await ytdlRequestApp("/check", {
      method: "POST",
      body: JSON.stringify({ url }),
    });
    return { ok: true, data };
  } catch (error) {
    return { ok: false, error: ytdlConnectionError(error) };
  }
}

async function ytdlQueueDownload(payload) {
  const viaSw = await ytdlSendRuntimeMessage({
    type: "queue-download",
    url: payload.url,
    scope: payload.scope || "single",
    format: payload.format,
    quality: payload.quality,
    contentKind: payload.contentKind,
    forceRedownload: !!payload.forceRedownload,
  });
  if (viaSw?.ok) return viaSw;

  try {
    const data = await ytdlRequestApp("/download", {
      method: "POST",
      body: JSON.stringify({
        url: payload.url,
        scope: payload.scope || "single",
        format: payload.format,
        quality: payload.quality,
        contentKind: payload.contentKind,
        forceRedownload: !!payload.forceRedownload,
      }),
    });
    return { ok: true, data };
  } catch (error) {
    return { ok: false, error: ytdlConnectionError(error) };
  }
}
