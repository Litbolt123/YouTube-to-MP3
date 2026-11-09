(() => {
  const SOURCE = "YOUTUBE_TO_MP3_PLAYER";

  function pickFormats(list) {
    return (list || []).map((format) => ({
      height: format.height,
      fps: format.fps,
      mimeType: format.mimeType,
      qualityLabel: format.qualityLabel,
    }));
  }

  function readPlayer() {
    let player = null;
    try {
      const el = document.getElementById("movie_player");
      if (el && typeof el.getPlayerResponse === "function") {
        player = el.getPlayerResponse();
      }
    } catch {
      /* YouTube player not ready */
    }
    if (!player?.streamingData) player = window.ytInitialPlayerResponse || null;
    if (!player?.streamingData) return null;
    return {
      streamingData: {
        adaptiveFormats: pickFormats(player.streamingData.adaptiveFormats),
        formats: pickFormats(player.streamingData.formats),
      },
    };
  }

  function publish() {
    try {
      window.postMessage({ source: SOURCE, player: readPlayer() }, "*");
    } catch {
      /* ignore clone failures */
    }
  }

  window.addEventListener("message", (event) => {
    if (event.source !== window) return;
    if (event.data?.source === SOURCE && event.data?.request === "player") publish();
  });

  document.addEventListener("yt-navigate-finish", publish);
  window.addEventListener("yt-navigate-finish", publish);
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", publish);
  } else {
    publish();
  }
  setTimeout(publish, 250);
  setTimeout(publish, 1200);
})();
