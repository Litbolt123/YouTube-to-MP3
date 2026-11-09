const YTDL_DEFAULT_PORT = 47384;
const YTDL_APP_NAME = "YouTube Downloader";
const YTDL_WAKE_URL = "youtubetomp3://wake";
const YTDL_WAKE_TIMEOUT_MS = 20000;

async function ytdlGetSettings() {
  return chrome.storage.sync.get({
    port: YTDL_DEFAULT_PORT,
    token: "",
    defaultAudioFormat: "mp3",
    defaultVideoFormat: "mp4",
  });
}

async function ytdlRequestApp(path, options = {}, attempt = 0) {
  const retry = options.retry !== false;
  const fetchOptions = { ...options };
  delete fetchOptions.retry;

  const { port, token } = await ytdlGetSettings();
  if (!token) {
    throw new Error("Set your connection token in the extension options.");
  }

  try {
    const response = await fetch(`http://127.0.0.1:${port}${path}`, {
      ...fetchOptions,
      headers: {
        "Content-Type": "application/json",
        "X-Extension-Token": token,
        ...(fetchOptions.headers || {}),
      },
    });

    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(data.error || `Request failed (${response.status})`);
    }
    return data;
  } catch (error) {
    const msg = ytdlConnectionError(error);
    const retryable = retry && (msg.includes("Could not reach") || msg.includes("Failed to fetch") || msg.includes("NetworkError"));
    if (retryable && attempt < 8) {
      await new Promise((resolve) => setTimeout(resolve, 400 + attempt * 200));
      return ytdlRequestApp(path, options, attempt + 1);
    }
    throw error;
  }
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

async function ytdlWakeApp() {
  if (ytdlWakeAppSync()) {
    return { ok: true };
  }

  const viaSw = await ytdlSendRuntimeMessage({ type: "wake-app" });
  if (viaSw?.ok) return viaSw;
  return { ok: false, error: "Could not wake the desktop app." };
}

/** Launch the desktop app via custom protocol. Must run synchronously during a user click. */
function ytdlWakeAppSync() {
  if (typeof document === "undefined" || !document.body) {
    return false;
  }

  try {
    const link = document.createElement("a");
    link.href = YTDL_WAKE_URL;
    link.style.display = "none";
    link.setAttribute("aria-hidden", "true");
    document.body.appendChild(link);
    link.click();
    link.remove();
    return true;
  } catch {
    return false;
  }
}

async function ytdlEnsureAppReady(maxWaitMs = YTDL_WAKE_TIMEOUT_MS) {
  ytdlWakeAppSync();

  // Cold start needs more than one health probe — the window/API bind after Show().
  await new Promise((resolve) => setTimeout(resolve, 1200));

  let health = await ytdlHealthCheck();
  if (health?.ok) return health;

  ytdlWakeAppSync();
  await new Promise((resolve) => setTimeout(resolve, 800));

  const start = Date.now();
  while (Date.now() - start < maxWaitMs) {
    await new Promise((resolve) => setTimeout(resolve, 400));
    health = await ytdlHealthCheck();
    if (health?.ok) return health;
  }

  return {
    ok: false,
    error: "App did not start in time. Open YouTube Downloader from the Start Menu once, then try again.",
  };
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
  const ready = await ytdlEnsureAppReady();
  if (!ready?.ok) return ready;

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
    const msg = ytdlConnectionError(error);
    // If the app just woke, /check can race the listener — still allow /download to proceed.
    if (msg.includes("Could not reach") || msg.includes("Failed to fetch") || msg.includes("NetworkError")) {
      return { ok: true, data: { alreadyDownloaded: false, inQueue: false, inHistory: false } };
    }

    return { ok: false, error: msg };
  }
}

async function ytdlProbeFormats(url) {
  const ready = await ytdlEnsureAppReady();
  if (!ready?.ok) return ready;

  const viaSw = await ytdlSendRuntimeMessage({
    type: "probe-formats",
    url,
  });
  if (viaSw?.ok) return viaSw;

  try {
    const data = await ytdlRequestApp("/formats", {
      method: "POST",
      body: JSON.stringify({ url }),
    });
    return { ok: true, data };
  } catch (error) {
    return { ok: false, error: ytdlConnectionError(error) };
  }
}

async function ytdlWatchStatus(url) {
  const viaSw = await ytdlSendRuntimeMessage({
    type: "watch-status",
    url,
  });
  if (viaSw?.ok) return viaSw;
  if (viaSw && !viaSw.swUnavailable) return viaSw;

  try {
    const data = await ytdlRequestApp("/status", {
      method: "POST",
      body: JSON.stringify({ url }),
      retry: false,
    });
    return { ok: true, data };
  } catch (error) {
    return { ok: false, error: ytdlConnectionError(error) };
  }
}

async function ytdlQueueDownload(payload) {
  const ready = await ytdlEnsureAppReady();
  if (!ready?.ok) return ready;

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
