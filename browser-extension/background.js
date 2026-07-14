importScripts("api.js");

const KEEPALIVE_ALARM = "yt-downloader-keepalive";

function scheduleKeepalive() {
  chrome.alarms.create(KEEPALIVE_ALARM, { periodInMinutes: 1 });
}

chrome.runtime.onInstalled.addListener(scheduleKeepalive);
chrome.runtime.onStartup.addListener(scheduleKeepalive);
scheduleKeepalive();

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === KEEPALIVE_ALARM) {
    chrome.storage.session.set({ keepaliveAt: Date.now() }).catch(() => {});
  }
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "queue-download") {
    ytdlRequestApp("/download", {
      method: "POST",
      body: JSON.stringify({
        url: message.url,
        scope: message.scope || "single",
        format: message.format,
        quality: message.quality,
        contentKind: message.contentKind,
        forceRedownload: !!message.forceRedownload,
      }),
    })
      .then((data) => sendResponse({ ok: true, data }))
      .catch((error) => sendResponse({ ok: false, error: ytdlConnectionError(error) }));
    return true;
  }

  if (message?.type === "check-url") {
    ytdlRequestApp("/check", {
      method: "POST",
      body: JSON.stringify({ url: message.url }),
    })
      .then((data) => sendResponse({ ok: true, data }))
      .catch((error) => sendResponse({ ok: false, error: ytdlConnectionError(error) }));
    return true;
  }

  if (message?.type === "health-check") {
    ytdlGetSettings()
      .then(async ({ port }) => {
        const response = await fetch(`http://127.0.0.1:${port}/health`);
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
          throw new Error(`App responded with status ${response.status}`);
        }
        sendResponse({ ok: true, data });
      })
      .catch((error) => sendResponse({ ok: false, error: ytdlConnectionError(error) }));
    return true;
  }

  return false;
});
