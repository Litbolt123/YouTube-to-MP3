const BTN_ID = "youtubetomp3-download-btn";
const PANEL_ID = "youtubetomp3-panel";
const WRAPPER_ATTR = "data-ytmp3-wrapper";
const FORMATS = [
  { tag: "mp3", label: "MP3 (audio)", kind: "audio" },
  { tag: "m4a", label: "M4A (audio)", kind: "audio" },
  { tag: "opus", label: "Opus (audio)", kind: "audio" },
  { tag: "flac", label: "FLAC (audio)", kind: "audio" },
  { tag: "wav", label: "WAV (audio)", kind: "audio" },
  { tag: "mp4", label: "MP4 (video)", kind: "video" },
];

const AUDIO_QUALITIES = [
  { value: 0, label: "Best (VBR)" },
  { value: 5, label: "Good (default)" },
  { value: 9, label: "Smaller file" },
];

const VIDEO_QUALITIES = [
  { value: -1, label: "Match this screen" },
  { value: 0, label: "Best available (highest this video has)" },
  { value: 1, label: "Up to 4K (2160p)" },
  { value: 2, label: "Up to 1440p" },
  { value: 5, label: "Up to 1080p" },
  { value: 9, label: "Up to 720p" },
  { value: 10, label: "Up to 480p" },
  { value: 11, label: "Up to 360p" },
  { value: 12, label: "Up to 240p" },
  { value: 13, label: "Up to 144p" },
];

const WRAPPER_STYLE =
  "display:inline-flex;align-items:center;margin:0 4px 0 6px;padding:0;border:0;flex:0 0 auto;";

const BTN_STYLE = [
  "appearance:none",
  "border:none",
  "border-radius:18px",
  "background:#d32f2f",
  "color:#fff",
  "cursor:pointer",
  "font:500 14px/36px Roboto,Arial,sans-serif",
  "padding:0 16px",
  "height:36px",
  "margin:0",
  "box-sizing:border-box",
  "display:inline-flex",
  "align-items:center",
  "justify-content:center",
  "vertical-align:middle",
  "flex:0 0 auto",
  "align-self:center",
  "gap:6px",
].join(";");

const SHADOW_STYLE_ID = "ytmp3-shadow-style";

let panelOpen = false;
let outsideClickHandler = null;
let lastWatchKey = "";
let injectBurstId = 0;
let pollTimer = null;
const shadowObservers = new WeakSet();

async function getSettings() {
  return ytdlGetSettings();
}

function isWatchPage() {
  return /\/watch\b/.test(location.pathname);
}

function getPlaylistContext() {
  if (!isWatchPage()) return null;

  const listId = new URLSearchParams(location.search).get("list");
  if (!listId) return null;
  if (/^RD/i.test(listId)) return null;

  return { listId };
}

function hasPlaylistContext() {
  return getPlaylistContext() !== null;
}

function getWatchKey() {
  if (!isWatchPage()) return "";
  const id = new URLSearchParams(location.search).get("v");
  return id ? `watch:${id}` : `watch:${location.pathname}${location.search}`;
}

function queryDeep(selector, root = document.documentElement) {
  if (!root) return null;

  try {
    if (root.querySelector) {
      const hit = root.querySelector(selector);
      if (hit) return hit;
    }
  } catch {
    /* ignore */
  }

  const nodes = root.querySelectorAll ? root.querySelectorAll("*") : [];
  for (const node of nodes) {
    if (node.shadowRoot) {
      const found = queryDeep(selector, node.shadowRoot);
      if (found) return found;
    }
  }

  return null;
}

function queryDeepAll(selector, root = document.documentElement, results = []) {
  if (!root) return results;

  try {
    if (root.querySelectorAll) {
      root.querySelectorAll(selector).forEach((el) => results.push(el));
    }
  } catch {
    /* ignore */
  }

  const nodes = root.querySelectorAll ? root.querySelectorAll("*") : [];
  for (const node of nodes) {
    if (node.shadowRoot) queryDeepAll(selector, node.shadowRoot, results);
  }

  return results;
}

function isVisible(el) {
  if (!el?.isConnected) return false;
  const rect = el.getBoundingClientRect();
  return rect.width > 8 && rect.height > 8;
}

function getPlayerBottom() {
  const player = queryDeep("#movie_player, .html5-video-player, video, ytd-player");
  return player?.getBoundingClientRect().bottom ?? 180;
}

function isBelowPlayer(el) {
  const rect = el.getBoundingClientRect();
  return rect.top >= getPlayerBottom() - 40 && rect.top < window.innerHeight * 0.82;
}

function scoreMainVideoAction(el) {
  const rect = el.getBoundingClientRect();
  const metadata = queryDeep("ytd-watch-metadata");
  const metaRect = metadata?.getBoundingClientRect();
  if (!metaRect) return rect.top;

  const targetY = metaRect.top + Math.min(metaRect.height * 0.55, 120);
  return Math.abs(rect.top - targetY) * 3 + Math.abs(rect.left - (metaRect.right - 320));
}

function walkVisibleNodes(root, visit) {
  if (!root || root.nodeType !== 1) return;
  visit(root);
  if (root.shadowRoot) walkVisibleNodes(root.shadowRoot, visit);
  for (const child of root.children || []) walkVisibleNodes(child, visit);
}

function elementLabel(el) {
  const aria =
    el?.getAttribute?.("aria-label") ||
    el?.querySelector?.("[aria-label]")?.getAttribute("aria-label") ||
    "";
  const text = (el?.innerText || el?.textContent || "").trim();
  return `${aria} ${text}`.toLowerCase();
}

const LIKE_PILL_SELECTORS = [
  "segmented-like-dislike-button-view-model",
  "ytd-segmented-like-dislike-button-renderer",
];

function tagName(el) {
  return el?.tagName?.toLowerCase() || "";
}

function isLikePillHost(el) {
  const tag = tagName(el);
  return tag === "segmented-like-dislike-button-view-model" || tag === "ytd-segmented-like-dislike-button-renderer";
}

function queryDeepFirst(selectors, root = document.documentElement) {
  for (const selector of selectors) {
    const hit = queryDeep(selector, root);
    if (hit) return hit;
  }
  return null;
}

function walkUpFrom(el) {
  const chain = [];
  let node = el;
  for (let depth = 0; depth < 25 && node; depth++) {
    chain.push(node);
    if (node.parentElement) {
      node = node.parentElement;
      continue;
    }
    const root = node.getRootNode?.();
    if (root instanceof ShadowRoot && root.host) {
      node = root.host;
      continue;
    }
    break;
  }
  return chain;
}

function getMetadataRect() {
  const metadata =
    queryDeep("ytd-watch-metadata") ||
    queryDeep("ytd-watch-flexy #meta") ||
    queryDeep("#meta");
  return metadata?.getBoundingClientRect() || null;
}

function overlapsPlayerRect(rect) {
  const player = queryDeep("#movie_player, .html5-video-player, ytd-player");
  if (!player || !rect) return false;

  const pr = player.getBoundingClientRect();
  const overlapX = Math.min(rect.right, pr.right) - Math.max(rect.left, pr.left);
  const overlapY = Math.min(rect.bottom, pr.bottom) - Math.max(rect.top, pr.top);
  return overlapX > 24 && overlapY > 24;
}

function isInsidePlayer(el) {
  return walkUpFrom(el).some((node) => {
    const tag = node.tagName?.toLowerCase() || "";
    return (
      node.id === "movie_player" ||
      tag === "ytd-player" ||
      node.classList?.contains("html5-video-player") ||
      node.classList?.contains("ytp-chrome-bottom")
    );
  });
}

function isLikelyMetadataAction(el) {
  if (!el?.isConnected) return false;
  if (isInsidePlayer(el)) return false;

  // Soft gate: allow not-yet-laid-out nodes in metadata (common before comments hydrate).
  const rect = el.getBoundingClientRect();
  const hasSize = rect.width > 8 && rect.height > 8;
  if (!hasSize) {
    return walkUpFrom(el).some(
      (n) =>
        n.id === "top-level-buttons-computed" ||
        n.tagName?.toLowerCase() === "ytd-watch-metadata"
    );
  }

  const playerBottom = getPlayerBottom();
  if (overlapsPlayerRect(rect)) return false;
  if (rect.top < playerBottom - 80) return false;
  if (rect.top > window.innerHeight * 0.82) return false;

  const metaRect = getMetadataRect();
  if (metaRect) {
    if (rect.top < metaRect.top - 32) return false;
    if (rect.top > metaRect.bottom + 96) return false;
    if (rect.right < metaRect.left - 48) return false;
    return true;
  }

  return rect.top >= playerBottom - 40;
}

function canUseAsAnchor(el) {
  return isLikelyMetadataAction(el);
}

function findBestHost(selector, predicate) {
  let best = null;
  let bestScore = Infinity;

  for (const el of queryDeepAll(selector)) {
    if (!canUseAsAnchor(el)) continue;
    if (predicate && !predicate(el)) continue;
    const score = scoreMainVideoAction(el);
    if (score < bestScore) {
      bestScore = score;
      best = el;
    }
  }

  return best;
}

function findAriaAction(pattern) {
  let best = null;
  let bestScore = Infinity;
  const root = queryDeep("ytd-watch-flexy") || queryDeep("ytd-watch-metadata") || document.documentElement;

  walkVisibleNodes(root, (node) => {
    const label = node.getAttribute?.("aria-label")?.toLowerCase() || "";
    if (!label || !pattern.test(label)) return;
    if (!canUseAsAnchor(node)) return;
    const score = scoreMainVideoAction(node);
    if (score < bestScore) {
      bestScore = score;
      best = node;
    }
  });

  return best;
}

function queryDeepAllTags(selectors, root = document.documentElement, results = []) {
  for (const selector of selectors) {
    queryDeepAll(selector, root, results);
  }
  return results;
}

function findBestLikePill() {
  let best = null;
  let bestScore = Infinity;

  for (const el of queryDeepAllTags(LIKE_PILL_SELECTORS)) {
    if (!canUseAsAnchor(el)) continue;
    const score = scoreMainVideoAction(el);
    if (score < bestScore) {
      bestScore = score;
      best = el;
    }
  }

  return best;
}

function findLikePillFromAnchor(anchor) {
  if (!anchor) return null;
  if (isLikePillHost(anchor)) return anchor;

  let node = anchor;
  for (let depth = 0; depth < 14 && node; depth++) {
    if (isLikePillHost(node)) return node;
    if (node.parentElement) {
      node = node.parentElement;
      continue;
    }
    const root = node.getRootNode?.();
    if (root instanceof ShadowRoot && root.host) {
      node = root.host;
      continue;
    }
    break;
  }

  return findBestLikePill();
}

function normalizeActionAnchor(anchor) {
  return findLikePillFromAnchor(anchor) || anchor;
}

function findButtonRow(host) {
  if (!host) return null;

  let node = host;
  for (let depth = 0; depth < 14 && node; depth++) {
    const id = node.id || "";
    if (id === "top-level-buttons-computed" || id === "actions") return node;

    const parent = node.parentElement;
    if (parent) {
      if (parent.id === "top-level-buttons-computed" || parent.id === "actions") return parent;
      node = parent;
      continue;
    }

    const root = node.getRootNode?.();
    if (root instanceof ShadowRoot && root.host) {
      node = root.host;
      continue;
    }
    break;
  }

  return queryDeep("#top-level-buttons-computed") ||
    queryDeep("ytd-watch-metadata #flexible-item-buttons") ||
    queryDeep("ytd-watch-metadata #menu");
}

function findMetadataLikeAnchor() {
  const pill = findBestLikePill() || findBestLikePillLoose();
  if (pill) return pill;

  const innerLike =
    findAriaAction(/like this video/i) ||
    findBestHost("button", (el) => elementLabel(el).includes("like this video"));

  if (innerLike) {
    const outer = findLikePillFromAnchor(innerLike);
    if (outer) return outer;
  }

  const row = findActionsRowEarly();
  if (row) {
    const rowPill = findLikeInRow(row);
    if (rowPill) return rowPill;
  }

  if (innerLike?.isConnected && !isInsidePlayer(innerLike)) return innerLike;

  const share = findWatchShareHost();
  return share?.isConnected && !isInsidePlayer(share) ? share : null;
}

function resolveActionRow(host) {
  const pill = normalizeActionAnchor(host);
  if (!pill || !isLikePillHost(pill)) return null;

  const row = findButtonRow(pill);
  if (!row) return null;

  return { container: row, like: pill, anchor: pill };
}

function findWatchShareHost() {
  return (
    findBestHost("ytd-button-renderer", (el) => elementLabel(el).includes("share")) ||
    findAriaAction(/^share\b/i) ||
    findBestHost("button", (el) => elementLabel(el).includes("share"))
  );
}

function getPlacementAnchor(wrapper) {
  if (wrapper?._ytmp3PinnedAnchor?.isConnected) {
    return wrapper._ytmp3PinnedAnchor;
  }

  const anchor = findMetadataLikeAnchor();
  if (wrapper && anchor) wrapper._ytmp3PinnedAnchor = anchor;
  return anchor;
}

function pinPlacementAnchor(wrapper, anchor) {
  // Prefer an already-pinned connected anchor (keeps float stable while scrolling).
  if (wrapper?._ytmp3PinnedAnchor?.isConnected) {
    // Upgrade only when we discover a real Like pill.
    if (!isLikePillHost(wrapper._ytmp3PinnedAnchor) && isLikePillHost(anchor) && canUseAsAnchor(anchor)) {
      wrapper._ytmp3PinnedAnchor = anchor;
      return anchor;
    }
    return wrapper._ytmp3PinnedAnchor;
  }

  const resolved = normalizeActionAnchor(anchor) || anchor;
  if (resolved?.isConnected) {
    if (wrapper) wrapper._ytmp3PinnedAnchor = resolved;
    return resolved;
  }
  return getPlacementAnchor(wrapper);
}

function isCorrectlyLeftOfPill(wrapper, anchor) {
  const btn = wrapper.querySelector(`#${BTN_ID}`);
  const host = normalizeActionAnchor(anchor);
  if (!btn || !host) return false;

  const btnRect = btn.getBoundingClientRect();
  const hostRect = host.getBoundingClientRect();
  if (btnRect.width < 8 || btnRect.height < 8) return false;
  if (isInsideLikePill(btnRect, hostRect)) return false;

  const leftOk = btnRect.right <= hostRect.left + 4;
  const rowOk = Math.abs(btnRect.top - hostRect.top) <= 16;
  return leftOk && rowOk;
}

function isInsideLikePill(btnRect, hostRect) {
  const overlapX = Math.min(btnRect.right, hostRect.right) - Math.max(btnRect.left, hostRect.left);
  const overlapY = Math.min(btnRect.bottom, hostRect.bottom) - Math.max(btnRect.top, hostRect.top);
  if (overlapX <= 0 || overlapY <= 0) return false;

  const hostWidth = Math.max(hostRect.width, 1);
  return overlapX / hostWidth > 0.35;
}

function findActionsRowEarly() {
  // Prefer the computed button row; fall back to menu hosts that exist earlier.
  const scoped = [
    "ytd-watch-metadata #top-level-buttons-computed",
    "ytd-watch-metadata #actions #top-level-buttons-computed",
    "ytd-watch-metadata #actions-inner #top-level-buttons-computed",
    "#above-the-fold #top-level-buttons-computed",
    "#top-level-buttons-computed",
    "ytd-watch-metadata #flexible-item-buttons",
    "ytd-watch-metadata #menu-container #top-level-buttons",
    "ytd-watch-metadata #menu #top-level-buttons",
    "ytd-watch-metadata #actions #menu",
    "ytd-watch-metadata #actions-inner",
  ];

  for (const sel of scoped) {
    const el = queryDeep(sel);
    if (!el?.isConnected || isInsidePlayer(el)) continue;
    return el;
  }

  return null;
}

/** Closest direct child of container that contains el (for insertBefore). */
function directChildContaining(container, el) {
  if (!container || !el) return null;
  if (el.parentElement === container) return el;

  for (const node of walkUpFrom(el)) {
    if (node.parentElement === container) return node;
  }
  return null;
}

function findLikeInRow(row) {
  if (!row) return null;
  const pill = queryDeepFirst(LIKE_PILL_SELECTORS, row);
  if (pill?.isConnected && !isInsidePlayer(pill)) return pill;

  for (const el of queryDeepAllTags(LIKE_PILL_SELECTORS)) {
    if (!el?.isConnected || isInsidePlayer(el)) continue;
    if (row.contains?.(el) || walkUpFrom(el).includes(row)) return el;
  }
  return null;
}

function findShareInMetadata() {
  const meta = queryDeep("ytd-watch-metadata");
  if (!meta) return null;

  const candidates = [];
  walkVisibleNodes(meta, (node) => {
    if (!node?.isConnected || isInsidePlayer(node)) return;
    const label = elementLabel(node);
    if (!label.includes("share")) return;
    const tag = tagName(node);
    if (
      tag === "yt-button-view-model" ||
      tag === "ytd-button-renderer" ||
      tag === "button" ||
      node.getAttribute?.("aria-label")
    ) {
      candidates.push(node);
    }
  });

  // Prefer larger / more leftward share controls in the actions strip.
  candidates.sort((a, b) => {
    const ar = a.getBoundingClientRect();
    const br = b.getBoundingClientRect();
    return ar.left - br.left || br.width * br.height - ar.width * ar.height;
  });
  return candidates[0] || null;
}

function findMountPoint() {
  // 1) Like pill + row → insert before the row-child that wraps Like
  const like = findBestLikePillLoose() || findMetadataLikeAnchor();
  if (like && isLikePillHost(like)) {
    const row =
      findButtonRow(like) ||
      resolveActionRow(like)?.container ||
      findActionsRowEarly();
    if (row) {
      const before = directChildContaining(row, like) || like;
      return { mode: "inline", container: row, like, anchor: like, before };
    }
  }

  // 2) Button / menu row exists → prepend, or sit before Share if present
  const row = findActionsRowEarly();
  if (row) {
    const likeInRow = findLikeInRow(row);
    if (likeInRow) {
      const before = directChildContaining(row, likeInRow) || likeInRow;
      return { mode: "inline", container: row, like: likeInRow, anchor: likeInRow, before };
    }

    const share = findShareInMetadata();
    const shareChild = share ? directChildContaining(row, share) : null;
    if (shareChild) {
      return { mode: "inline", container: row, like: null, anchor: share, before: shareChild };
    }

    return {
      mode: "inline",
      container: row,
      like: null,
      anchor: row,
      before: row.firstElementChild,
    };
  }

  // 3) Share exists but row id missing — mount into Share's parent
  const share = findShareInMetadata();
  if (share?.parentElement && !isInsidePlayer(share.parentElement)) {
    return {
      mode: "inline",
      container: share.parentElement,
      like: null,
      anchor: share,
      before: share,
    };
  }

  return null;
}

/** Like pill without visibility/fold gates — used for early mount. */
function findBestLikePillLoose() {
  let best = null;
  let bestScore = Infinity;
  for (const el of queryDeepAllTags(LIKE_PILL_SELECTORS)) {
    if (!el?.isConnected || isInsidePlayer(el)) continue;
    const score = scoreMainVideoAction(el);
    if (score < bestScore) {
      bestScore = score;
      best = el;
    }
  }
  return best;
}

function findConnectedWrappers() {
  const seen = new Set();
  const connected = [];

  for (const el of document.querySelectorAll(`[${WRAPPER_ATTR}]`)) {
    if (!seen.has(el) && el.isConnected) {
      seen.add(el);
      connected.push(el);
    }
  }

  for (const el of queryDeepAll(`[${WRAPPER_ATTR}]`)) {
    if (!seen.has(el) && el.isConnected) {
      seen.add(el);
      connected.push(el);
    }
  }

  return connected;
}

function findExistingWrapper() {
  return findConnectedWrappers()[0] || null;
}

function hasVisibleDownloadButton() {
  const btn = queryDeep(`#${BTN_ID}`) || document.getElementById(BTN_ID);
  // Connected is enough — actions row can be 0×0 until comments hydrate.
  return !!btn?.isConnected;
}

function removeButton() {
  closePanel();
  stopStatusPoll();
  clearResetTimer();
  localPhase = "idle";
  sawThisVisitDownload = false;
  for (const wrapper of findConnectedWrappers()) {
    clearFloatHandlers(wrapper);
    wrapper._ytmp3PinnedAnchor = null;
    wrapper.remove();
  }
  document.getElementById("youtubetomp3-toast")?.remove();
}

function showToast(message, isError = false) {
  let toast = document.getElementById("youtubetomp3-toast");
  if (!toast) {
    toast = document.createElement("div");
    toast.id = "youtubetomp3-toast";
    toast.className = "ytmp3-toast";
    document.body.appendChild(toast);
  }
  toast.textContent = message;
  toast.className = isError ? "ytmp3-toast ytmp3-toast-error" : "ytmp3-toast";
  toast.style.display = "block";
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => {
    toast.style.display = "none";
  }, 3500);
}

function isVideoFormat(tag) {
  return tag === "mp4";
}

function heightLabel(height) {
  if (height >= 2160) return `${height}p (4K)`;
  return `${height}p`;
}

function fillQualitySelect(select, formatTag, preferred) {
  const options = isVideoFormat(formatTag) ? VIDEO_QUALITIES : AUDIO_QUALITIES;
  select.innerHTML = "";
  for (const option of options) {
    const el = document.createElement("option");
    el.value = String(option.value);
    el.textContent = option.label;
    select.appendChild(el);
  }
  const fallback = isVideoFormat(formatTag) ? "-1" : "0";
  select.value = preferred != null && [...select.options].some((o) => o.value === String(preferred))
    ? String(preferred)
    : fallback;
}

function fillLiveVideoQualities(select, probe) {
  if (!select) return;
  select.disabled = false;
  select.innerHTML = "";

  const heights = Array.isArray(probe?.heights) && probe.heights.length
    ? probe.heights
    : [2160, 1440, 1080, 720, 480, 360];
  const labels = probe?.labels || {};
  const matchScreen = probe?.matchScreen !== false;
  const suggested = probe?.suggestedHeight ?? probe?.screenHeight ?? heights[0];
  const best = probe?.bestHeight ?? heights[0];

  const bestOpt = document.createElement("option");
  bestOpt.value = "0";
  bestOpt.textContent = best
    ? `Best available (${labels[best] || heightLabel(best)} — highest this video has)`
    : "Best available (highest this video has)";
  select.appendChild(bestOpt);

  for (const height of heights) {
    const el = document.createElement("option");
    el.value = String(height);
    const shown = labels[height] || heightLabel(height);
    const notes = [];
    if (height === suggested && matchScreen) notes.push("matches your screen");
    if (height === best) notes.push("highest");
    el.textContent = notes.length ? `${shown} (${notes.join(", ")})` : shown;
    select.appendChild(el);
  }

  select.value = matchScreen ? String(suggested) : "0";
  if (![...select.options].some((o) => o.value === select.value)) {
    select.value = String(heights[0]);
  }
  syncPanelCaptions(select.closest(".ytmp3-panel"));
}

function selectedQualityCaption(select) {
  const opt = select?.selectedOptions?.[0];
  const raw = (opt?.textContent || suggestedDownloadLabel || "MP4").replace(/\s*\(.*\)\s*$/, "").trim();
  return raw || suggestedDownloadLabel || "MP4";
}

function syncPanelCaptions(root) {
  const panel = root?.classList?.contains("ytmp3-panel") ? root : document.getElementById(PANEL_ID);
  if (!panel) return;
  const select = panel.querySelector(".ytmp3-live-quality");
  const videoCap = selectedQualityCaption(select);
  panel.querySelectorAll(".ytmp3-quick-video-cap").forEach((el) => {
    el.textContent = videoCap;
  });
  const status = panel.querySelector(".ytmp3-panel-status");
  if (status) {
    if (localPhase === "queued") {
      status.hidden = false;
      status.textContent = "Queued in the app";
    } else if (localPhase === "downloading") {
      status.hidden = false;
      status.textContent = "Downloading now";
    } else if (localPhase === "downloaded") {
      status.hidden = false;
      status.textContent = "Already saved ✓";
    } else {
      status.hidden = true;
      status.textContent = "";
    }
  }
}

let cachedScreenHeight = 1080;
let cachedMatchScreen = true;
let cachedAfterDownload = "keepUntilFileGone";
let cachedResetSeconds = 5;
let suggestedDownloadLabel = "";
let statusPollTimer = null;
let statusPollFast = false;
let resetToDownloadTimer = null;
let localPhase = "idle";
let sawThisVisitDownload = false;
let timedResetDone = false;
let holdUntilAppSeesJob = 0;
let pagePlayerResponse = null;

const PAGE_PLAYER_SOURCE = "YOUTUBE_TO_MP3_PLAYER";

window.addEventListener("message", (event) => {
  if (event.source !== window) return;
  if (event.data?.source !== PAGE_PLAYER_SOURCE) return;
  if (event.data.player?.streamingData) {
    pagePlayerResponse = event.data.player;
    const probe = probeFromYouTubePage();
    const open = document.getElementById(PANEL_ID);
    if (open?.isConnected) {
      fillLiveVideoQualities(open.querySelector(".ytmp3-live-quality"), probe);
      syncPanelCaptions(open);
    }
    if (localPhase === "idle") {
      const mark = getDownloadButton()?.querySelector(".ytmp3-btn-mark");
      const badgeOn = !!(mark && !mark.hidden);
      setButtonChrome(idleDownloadText(), { badge: badgeOn, title: idleTitle() });
    }
  }
});

function requestPagePlayer() {
  window.postMessage({ source: PAGE_PLAYER_SOURCE, request: "player" }, "*");
}

function extractJsonObject(source) {
  const start = source.indexOf("{");
  if (start < 0) return null;
  let depth = 0;
  let inStr = false;
  let escape = false;
  for (let i = start; i < Math.min(source.length, start + 2_000_000); i++) {
    const ch = source[i];
    if (inStr) {
      if (escape) escape = false;
      else if (ch === "\\") escape = true;
      else if (ch === '"') inStr = false;
      continue;
    }
    if (ch === '"') inStr = true;
    else if (ch === "{") depth++;
    else if (ch === "}") {
      depth--;
      if (depth === 0) {
        try {
          return JSON.parse(source.slice(start, i + 1));
        } catch {
          return null;
        }
      }
    }
  }
  return null;
}

function parsePlayerResponse() {
  if (pagePlayerResponse?.streamingData) return pagePlayerResponse;
  const fromWindow = window.ytInitialPlayerResponse;
  if (fromWindow?.streamingData) return fromWindow;

  for (const script of document.getElementsByTagName("script")) {
    const text = script.textContent || "";
    const idx = text.indexOf("ytInitialPlayerResponse");
    if (idx < 0) continue;
    const eq = text.indexOf("=", idx);
    if (eq < 0) continue;
    const parsed = extractJsonObject(text.slice(eq + 1));
    if (parsed?.streamingData) return parsed;
  }
  return null;
}

function probeFromYouTubePage() {
  const player = parsePlayerResponse();
  const formats = [
    ...(player?.streamingData?.adaptiveFormats || []),
    ...(player?.streamingData?.formats || []),
  ];
  const byHeight = new Map();
  for (const fmt of formats) {
    const height = Number(fmt.height);
    if (!Number.isFinite(height) || height < 144) continue;
    if (fmt.mimeType && /^audio\//i.test(fmt.mimeType)) continue;
    const fps = Number(fmt.fps) || 0;
    const label = fmt.qualityLabel || (fps >= 50 ? `${height}p${fps}` : `${height}p`);
    const prev = byHeight.get(height);
    if (!prev || fps > prev.fps) byHeight.set(height, { height, fps, label });
  }

  const heights = [...byHeight.keys()].sort((a, b) => b - a);
  const labels = {};
  for (const [height, info] of byHeight) labels[height] = info.label;
  if (!heights.length) {
    return {
      heights: [],
      labels,
      bestHeight: null,
      suggestedHeight: cachedScreenHeight,
      screenHeight: cachedScreenHeight,
      matchScreen: cachedMatchScreen,
    };
  }

  const suggested = heights.filter((h) => h <= cachedScreenHeight).sort((a, b) => b - a)[0]
    || heights[heights.length - 1];
  suggestedDownloadLabel = byHeight.get(suggested)?.label || `${suggested}p`;
  return {
    heights,
    labels,
    bestHeight: heights[0] || null,
    suggestedHeight: suggested,
    screenHeight: cachedScreenHeight,
    matchScreen: cachedMatchScreen,
  };
}

function getDownloadButton() {
  return queryDeep(`#${BTN_ID}`) || document.getElementById(BTN_ID);
}

function setButtonChrome(text, { badge = false, title = "", busy = false } = {}) {
  const btn = getDownloadButton();
  if (!btn) return;
  let label = btn.querySelector(".ytmp3-btn-label");
  if (!label) {
    btn.textContent = "";
    label = document.createElement("span");
    label.className = "ytmp3-btn-label";
    btn.appendChild(label);
    const mark = document.createElement("span");
    mark.className = "ytmp3-btn-mark";
    mark.hidden = true;
    mark.textContent = "✓";
    mark.title = "Saved";
    btn.appendChild(mark);
  }
  label.textContent = text;
  const markEl = btn.querySelector(".ytmp3-btn-mark");
  if (markEl) markEl.hidden = !badge;
  btn.disabled = !!busy;
  btn.title = title || `Download with ${YTDL_APP_NAME}`;
}

function idleDownloadText() {
  return "Download";
}

function stopStatusPoll() {
  if (statusPollTimer) {
    clearInterval(statusPollTimer);
    statusPollTimer = null;
  }
  statusPollFast = false;
}

function clearResetTimer() {
  if (resetToDownloadTimer) {
    clearTimeout(resetToDownloadTimer);
    resetToDownloadTimer = null;
  }
}

function rescheduleStatusPoll(fast) {
  const want = !!fast;
  if (statusPollTimer && statusPollFast === want) return;
  stopStatusPoll();
  statusPollFast = want;
  statusPollTimer = setInterval(() => {
    if (!isWatchPage()) {
      stopStatusPoll();
      return;
    }
    refreshWatchStatus();
  }, want ? 900 : 2500);
}

function idleTitle(data) {
  if (data?.downloadedBefore && !data.fileExists) return "File was removed — click to download again";
  if (data?.fileExists || (data?.downloadedBefore && cachedAfterDownload === "resetAfterSeconds")) {
    return "Saved — click for Audio or Video to download again";
  }
  return `Download with ${YTDL_APP_NAME} — click for Audio or Video`;
}

function applyWatchStatus(data) {
  if (!data) return;
  if (data.afterDownload) cachedAfterDownload = data.afterDownload;
  if (Number.isFinite(Number(data.resetSeconds))) cachedResetSeconds = Number(data.resetSeconds);

  const state = data.state || "idle";
  const resetTimed = cachedAfterDownload === "resetAfterSeconds";

  if (state === "queued" || state === "downloading") {
    timedResetDone = false;
    clearResetTimer();
  }

  if (state === "downloading") {
    localPhase = "downloading";
    const pct = Number.isFinite(Number(data.percent)) ? ` ${Math.round(Number(data.percent))}%` : "";
    setButtonChrome(`Downloading${pct}`, { title: data.detail || "Downloading…" });
    rescheduleStatusPoll(true);
    syncPanelCaptions();
    return;
  }

  if (state === "queued") {
    localPhase = "queued";
    setButtonChrome("Queued", { title: "Waiting in the YouTube Downloader queue" });
    rescheduleStatusPoll(true);
    syncPanelCaptions();
    return;
  }

  if (state === "downloaded") {
    holdUntilAppSeesJob = 0;
    if (resetTimed && timedResetDone) {
      localPhase = "idle";
      setButtonChrome(idleDownloadText(), { badge: true, title: idleTitle(data) });
      rescheduleStatusPoll(false);
      syncPanelCaptions();
      return;
    }
    localPhase = "downloaded";
    setButtonChrome("Downloaded", {
      badge: true,
      title: data.path || "Saved to your videos folder",
    });
    if (resetTimed && !resetToDownloadTimer) {
      resetToDownloadTimer = setTimeout(() => {
        timedResetDone = true;
        localPhase = "idle";
        setButtonChrome(idleDownloadText(), { badge: true, title: "Saved — click to download again" });
        syncPanelCaptions();
      }, Math.max(1, cachedResetSeconds) * 1000);
    }
    rescheduleStatusPoll(false);
    syncPanelCaptions();
    return;
  }

  if (
    sawThisVisitDownload &&
    Date.now() < holdUntilAppSeesJob &&
    (localPhase === "queued" || localPhase === "downloading")
  ) {
    return;
  }

  localPhase = "idle";
  const badge = !!data.fileExists && resetTimed && !!data.downloadedBefore;
  setButtonChrome(idleDownloadText(), { badge, title: idleTitle(data) });
  rescheduleStatusPoll(false);
  syncPanelCaptions();
}

async function refreshWatchStatus() {
  if (!isWatchPage()) return;
  requestPagePlayer();
  probeFromYouTubePage();

  const result = await ytdlWatchStatus(location.href);
  if (result?.ok && result.data) {
    applyWatchStatus(result.data);
    return;
  }

  if (localPhase === "idle") {
    setButtonChrome(idleDownloadText(), { title: idleTitle() });
  }
}

function startStatusPoll(fast) {
  const want = !!fast;
  if (!statusPollTimer || statusPollFast !== want) {
    stopStatusPoll();
    statusPollFast = want;
    statusPollTimer = setInterval(() => {
      if (!isWatchPage()) {
        stopStatusPoll();
        return;
      }
      refreshWatchStatus();
    }, want ? 900 : 2500);
  }
  refreshWatchStatus();
}

async function rememberHealthSettings() {
  const health = await ytdlHealthCheck();
  if (!health?.ok) return;
  const data = health.data || {};
  if (data.video?.screenHeight) cachedScreenHeight = data.video.screenHeight;
  if (typeof data.video?.matchScreen === "boolean") cachedMatchScreen = data.video.matchScreen;
  if (data.button?.afterDownload) cachedAfterDownload = data.button.afterDownload;
  if (Number.isFinite(Number(data.button?.resetSeconds))) cachedResetSeconds = Number(data.button.resetSeconds);
}

function readLiveQuality(panel) {
  const select = panel?.querySelector(".ytmp3-live-quality");
  if (!select || select.disabled) return undefined;
  const n = Number(select.value);
  return Number.isFinite(n) ? n : undefined;
}

function closePanel() {
  document.getElementById(PANEL_ID)?.remove();
  panelOpen = false;
  if (outsideClickHandler) {
    document.removeEventListener("click", outsideClickHandler, true);
    outsideClickHandler = null;
  }
}

async function sendToApp({ format, quality, contentKind, scope = "single", forceRedownload = false }) {
  closePanel();
  ytdlWakeAppSync();

  const { token } = await getSettings();
  if (!token) {
    showToast("Set your connection token in the extension options.", true);
    return;
  }

  const btn = getDownloadButton();
  if (btn) setButtonChrome("Sending…", { busy: true, title: "Sending to YouTube Downloader…" });

  const isPlaylist = scope === "playlist";

  try {
    let force = !!forceRedownload;
    if (!force) {
      const check = await ytdlCheckUrl(location.href);
      const data = check?.data || check;
      if (check?.ok && (data?.alreadyDownloaded || data?.inHistory || data?.inQueue)) {
        const detail = data.message || "This link was already downloaded or is in the queue.";
        const again = window.confirm(`${detail}\n\nDownload anyway?`);
        if (!again) {
          showToast("Skipped — already downloaded.");
          setButtonChrome("Downloaded", { badge: true, title: "Already saved" });
          localPhase = "downloaded";
          startStatusPoll(false);
          return;
        }
        force = true;
      }
    }

    const result = await ytdlQueueDownload({
      url: location.href,
      scope,
      format,
      quality,
      contentKind,
      forceRedownload: force,
    });

    if (!result?.ok) {
      throw new Error(result?.error || "Could not send to app");
    }

    const skipped = result?.data?.skipped;
    if (skipped) {
      showToast(result.data.message || "Already downloaded — skipped.");
      setButtonChrome("Downloaded", { badge: true, title: result.data.message || "Already saved" });
      localPhase = "downloaded";
      startStatusPoll(false);
    } else {
      showToast(
        force
          ? isPlaylist
            ? `Playlist re-download sent to ${YTDL_APP_NAME}`
            : `Re-download sent to ${YTDL_APP_NAME}`
          : isPlaylist
            ? `Playlist sent to ${YTDL_APP_NAME}`
            : `Sent to ${YTDL_APP_NAME}`
      );
      sawThisVisitDownload = true;
      timedResetDone = false;
      holdUntilAppSeesJob = Date.now() + 15000;
      localPhase = "queued";
      setButtonChrome("Queued", { title: "Waiting in the YouTube Downloader queue" });
      startStatusPoll(true);
    }
  } catch (error) {
    showToast(error.message || "Could not send to app", true);
    setButtonChrome(idleDownloadText());
  } finally {
    const live = getDownloadButton();
    if (live) live.disabled = false;
  }
}

function openPanel(anchorButton) {
  closePanel();
  requestPagePlayer();

  const playlist = hasPlaylistContext();
  const panel = document.createElement("div");
  panel.id = PANEL_ID;
  panel.className = "ytmp3-panel";
  panel.innerHTML = `
    <div class="ytmp3-panel-title">${YTDL_APP_NAME}</div>
    <div class="ytmp3-panel-status" hidden></div>
    <p class="ytmp3-panel-lead">Audio saves music. Video saves the quality below.</p>
    <div class="ytmp3-quick-row ytmp3-quick-row-tall">
      <button type="button" class="ytmp3-quick ytmp3-quick-audio">
        <span class="ytmp3-quick-name">Audio</span>
        <span class="ytmp3-quick-cap ytmp3-quick-audio-cap">MP3</span>
      </button>
      <button type="button" class="ytmp3-quick ytmp3-quick-video">
        <span class="ytmp3-quick-name">Video</span>
        <span class="ytmp3-quick-cap ytmp3-quick-video-cap">MP4</span>
      </button>
    </div>
    <div class="ytmp3-quality-row">
      <div class="ytmp3-scope-label">Video quality</div>
      <select class="ytmp3-select ytmp3-live-quality">
        <option>Reading this page…</option>
      </select>
    </div>
    ${
      playlist
        ? `
    <div class="ytmp3-playlist-section">
      <div class="ytmp3-scope-label">Whole playlist</div>
      <div class="ytmp3-quick-row ytmp3-quick-row-tall">
        <button type="button" class="ytmp3-quick ytmp3-quick-audio ytmp3-quick-playlist-audio">
          <span class="ytmp3-quick-name">Audio</span>
          <span class="ytmp3-quick-cap ytmp3-quick-audio-cap">MP3</span>
        </button>
        <button type="button" class="ytmp3-quick ytmp3-quick-video ytmp3-quick-playlist-video">
          <span class="ytmp3-quick-name">Video</span>
          <span class="ytmp3-quick-cap ytmp3-quick-video-cap">MP4</span>
        </button>
      </div>
    </div>`
        : ""
    }
    <button type="button" class="ytmp3-toggle-details">More options…</button>
    <div class="ytmp3-details ytmp3-details-hidden">
      ${
        playlist
          ? `
      <label class="ytmp3-label" for="youtubetomp3-scope">Download</label>
      <select id="youtubetomp3-scope" class="ytmp3-select ytmp3-scope">
        <option value="single">This video only</option>
        <option value="playlist">Whole playlist</option>
      </select>`
          : ""
      }
      <label class="ytmp3-label" for="youtubetomp3-format">Format</label>
      <select id="youtubetomp3-format" class="ytmp3-select ytmp3-format"></select>
      <label class="ytmp3-label" for="youtubetomp3-quality">Quality</label>
      <select id="youtubetomp3-quality" class="ytmp3-select ytmp3-quality"></select>
      <label class="ytmp3-label" for="youtubetomp3-content">Save as</label>
      <select id="youtubetomp3-content" class="ytmp3-select ytmp3-content">
        <option value="auto">Auto</option>
        <option value="music">Music</option>
        <option value="video">Video</option>
      </select>
      <button type="button" class="ytmp3-confirm">Download with these settings</button>
    </div>
  `;

  const rect = anchorButton.getBoundingClientRect();
  panel.style.top = `${Math.round(rect.bottom + 8 + window.scrollY)}px`;
  panel.style.left = `${Math.round(rect.left + window.scrollX)}px`;

  document.body.appendChild(panel);
  panelOpen = true;

  const formatSelect = panel.querySelector(".ytmp3-format");
  const qualitySelect = panel.querySelector(".ytmp3-quality");
  const liveQualitySelect = panel.querySelector(".ytmp3-live-quality");
  const contentSelect = panel.querySelector(".ytmp3-content");
  const scopeSelect = panel.querySelector(".ytmp3-scope");
  const details = panel.querySelector(".ytmp3-details");
  const toggle = panel.querySelector(".ytmp3-toggle-details");

  for (const format of FORMATS) {
    const option = document.createElement("option");
    option.value = format.tag;
    option.textContent = format.label;
    formatSelect.appendChild(option);
  }

  formatSelect.value = "mp3";
  fillQualitySelect(qualitySelect, formatSelect.value, -1);
  fillLiveVideoQualities(liveQualitySelect, probeFromYouTubePage());
  syncPanelCaptions(panel);
  liveQualitySelect.addEventListener("change", () => syncPanelCaptions(panel));
  void getSettings().then((settings) => {
    if (!panel.isConnected) return;
    const audioTag = (settings.defaultAudioFormat || "mp3").toUpperCase();
    panel.querySelectorAll(".ytmp3-quick-audio-cap").forEach((el) => {
      el.textContent = audioTag;
    });
  });

  formatSelect.addEventListener("change", () => {
    fillQualitySelect(qualitySelect, formatSelect.value, readLiveQuality(panel));
    contentSelect.value = isVideoFormat(formatSelect.value) ? "video" : "music";
  });

  toggle.addEventListener("click", (event) => {
    event.stopPropagation();
    const hidden = details.classList.toggle("ytmp3-details-hidden");
    toggle.textContent = hidden ? "More options…" : "Hide options";
  });

  panel.querySelector(".ytmp3-quick-audio").addEventListener("click", async (event) => {
    event.stopPropagation();
    ytdlWakeAppSync();
    const settings = await getSettings();
    await sendToApp({
      format: settings.defaultAudioFormat || "mp3",
      quality: undefined,
      contentKind: "auto",
      scope: "single",
    });
  });

  panel.querySelector(".ytmp3-quick-video").addEventListener("click", async (event) => {
    event.stopPropagation();
    ytdlWakeAppSync();
    const settings = await getSettings();
    await sendToApp({
      format: settings.defaultVideoFormat || "mp4",
      quality: readLiveQuality(panel),
      contentKind: "video",
      scope: "single",
    });
  });

  if (playlist) {
    panel.querySelector(".ytmp3-quick-playlist-audio").addEventListener("click", async (event) => {
      event.stopPropagation();
      ytdlWakeAppSync();
      const settings = await getSettings();
      await sendToApp({
        format: settings.defaultAudioFormat || "mp3",
        quality: undefined,
        contentKind: "auto",
        scope: "playlist",
      });
    });

    panel.querySelector(".ytmp3-quick-playlist-video").addEventListener("click", async (event) => {
      event.stopPropagation();
      ytdlWakeAppSync();
      const settings = await getSettings();
      await sendToApp({
        format: settings.defaultVideoFormat || "mp4",
        quality: readLiveQuality(panel),
        contentKind: "video",
        scope: "playlist",
      });
    });
  }

  panel.querySelector(".ytmp3-confirm").addEventListener("click", async (event) => {
    event.stopPropagation();
    ytdlWakeAppSync();
    const format = formatSelect.value;
    const quality = isVideoFormat(format)
      ? (Number.isFinite(Number(qualitySelect.value)) ? Number(qualitySelect.value) : readLiveQuality(panel))
      : Number(qualitySelect.value);
    await sendToApp({
      format,
      quality,
      contentKind: contentSelect.value,
      scope: scopeSelect?.value || "single",
    });
  });

  void rememberHealthSettings().then(() => {
    if (!panel.isConnected) return;
    fillLiveVideoQualities(liveQualitySelect, probeFromYouTubePage());
    syncPanelCaptions(panel);
    if (isVideoFormat(formatSelect.value)) {
      fillQualitySelect(qualitySelect, formatSelect.value, readLiveQuality(panel));
    }
  });

  panel.addEventListener("click", (event) => event.stopPropagation());

  outsideClickHandler = (event) => {
    if (!panel.contains(event.target) && event.target !== anchorButton) {
      closePanel();
    }
  };
  setTimeout(() => document.addEventListener("click", outsideClickHandler, true), 0);
}

function injectShadowStyles(root) {
  if (!root || root === document) return;
  if (root.querySelector?.(`#${SHADOW_STYLE_ID}`)) return;

  const style = document.createElement("style");
  style.id = SHADOW_STYLE_ID;
  style.textContent = `
    [${WRAPPER_ATTR}] { display: inline-flex !important; align-items: center; margin: 0 8px 0 0; flex: 0 0 auto; }
    .ytmp3-btn {
      appearance: none; border: none; border-radius: 18px; background: #d32f2f; color: #fff;
      cursor: pointer; font: 500 14px/36px Roboto, Arial, sans-serif; padding: 0 16px;
      height: 36px; margin: 0 8px 0 0; box-sizing: border-box; display: inline-flex;
      align-items: center; justify-content: center; gap: 6px; flex: 0 0 auto; align-self: center;
    }
    .ytmp3-btn:hover:not(:disabled) { background: #b71c1c; }
    .ytmp3-btn:disabled { opacity: 0.7; cursor: wait; }
    .ytmp3-btn-mark { font-size: 12px; line-height: 1; opacity: 0.95; }
  `;

  try {
    root.appendChild(style);
  } catch {
    /* ignore */
  }
}

function syncButtonSize(btn, like) {
  if (!like) return;

  const ref =
    like.querySelector?.("yt-button-shape") ||
    like.querySelector?.("button") ||
    like.querySelector?.("#segmented-like-button") ||
    like;

  const height = ref?.getBoundingClientRect?.().height ?? like.getBoundingClientRect?.().height ?? 0;
  if (height > 20 && height < 60) {
    const px = `${Math.round(height)}px`;
    btn.style.height = px;
    btn.style.lineHeight = px;
    btn.style.minHeight = px;
  }
}

function applyButtonStyles(btn, like) {
  btn.style.cssText = BTN_STYLE;
  syncButtonSize(btn, like);
}

function createWrapper(like) {
  requestPagePlayer();
  probeFromYouTubePage();
  const wrapper = document.createElement("div");
  wrapper.setAttribute(WRAPPER_ATTR, "1");
  wrapper.className = "ytmp3-wrapper";
  wrapper.style.cssText = WRAPPER_STYLE;

  const btn = document.createElement("button");
  btn.id = BTN_ID;
  btn.type = "button";
  btn.className = "ytmp3-btn";
  btn.innerHTML = `<span class="ytmp3-btn-label">Download</span><span class="ytmp3-btn-mark" hidden title="Saved">✓</span>`;
  btn.title = `Download with ${YTDL_APP_NAME} — click for Audio or Video`;
  applyButtonStyles(btn, like);
  btn.addEventListener("click", (event) => {
    event.preventDefault();
    event.stopPropagation();
    ytdlWakeAppSync();
    if (panelOpen) closePanel();
    else openPanel(btn);
  });

  wrapper.appendChild(btn);
  return wrapper;
}

function clearFloatHandlers(wrapper) {
  if (!wrapper?._ytmp3PositionHandler) return;
  window.removeEventListener("scroll", wrapper._ytmp3PositionHandler, true);
  window.removeEventListener("resize", wrapper._ytmp3PositionHandler);
  wrapper._ytmp3PositionHandler = null;
}

function ensurePlacement(wrapper, mount) {
  if (!wrapper || !mount || mount.mode !== "inline") return false;

  const { container, like, anchor, before } = mount;
  if (!container?.isConnected) return false;

  wrapper.removeAttribute("data-ytmp3-float");
  clearFloatHandlers(wrapper);
  wrapper.style.cssText = WRAPPER_STYLE;
  // Ensure flex children don't collapse to zero in YouTube's action row.
  wrapper.style.display = "inline-flex";
  wrapper.style.alignItems = "center";
  wrapper.style.flex = "0 0 auto";
  wrapper.style.visibility = "visible";
  wrapper.style.opacity = "1";

  injectShadowStyles(container.getRootNode());

  let insertBeforeNode = null;
  if (like) {
    insertBeforeNode = directChildContaining(container, like);
  }
  if (!insertBeforeNode && before) {
    insertBeforeNode =
      before.parentElement === container ? before : directChildContaining(container, before);
  }
  if (!insertBeforeNode) {
    insertBeforeNode = container.firstElementChild;
  }
  // Don't insert before ourselves.
  if (insertBeforeNode === wrapper) {
    insertBeforeNode = wrapper.nextElementSibling;
  }

  try {
    if (wrapper.parentElement !== container || wrapper.nextElementSibling !== insertBeforeNode) {
      if (insertBeforeNode && insertBeforeNode.parentElement === container) {
        container.insertBefore(wrapper, insertBeforeNode);
      } else {
        container.appendChild(wrapper);
      }
    }
    wrapper._ytmp3PinnedAnchor = like || anchor || container;
  } catch {
    try {
      container.appendChild(wrapper);
      wrapper._ytmp3PinnedAnchor = like || anchor || container;
    } catch {
      return false;
    }
  }

  const btn = wrapper.querySelector(`#${BTN_ID}`) || wrapper.firstElementChild;
  if (btn) {
    applyButtonStyles(btn, like || anchor || container);
    btn.style.visibility = "visible";
    btn.style.opacity = "1";
    btn.style.display = "inline-flex";
  }

  return wrapper.isConnected && (wrapper.parentElement === container || container.contains(wrapper));
}

function tryInject() {
  if (!isWatchPage()) {
    removeButton();
    stopPoll();
    return false;
  }

  const mount = findMountPoint();
  if (!mount) return false;

  let existing = findExistingWrapper();
  if (existing) {
    if (existing.getAttribute("data-ytmp3-float") === "1") {
      clearFloatHandlers(existing);
      existing.removeAttribute("data-ytmp3-float");
    }

    const targetBefore =
      (mount.like && mount.like.parentElement === mount.container ? mount.like : null) ||
      (mount.before && mount.before.parentElement === mount.container ? mount.before : null);

    const alreadyGood =
      existing.parentElement === mount.container &&
      (!targetBefore || existing.nextElementSibling === targetBefore);

    if (alreadyGood) {
      applyButtonStyles(
        existing.querySelector(`#${BTN_ID}`) || existing.firstElementChild,
        mount.like || mount.anchor
      );
      requestPagePlayer();
      probeFromYouTubePage();
      if (localPhase === "idle") {
        setButtonChrome(idleDownloadText(), { title: idleTitle() });
      }
      if (!statusPollTimer) startStatusPoll(localPhase === "queued" || localPhase === "downloading");
      return true;
    }

    if (!ensurePlacement(existing, mount)) {
      existing._ytmp3PinnedAnchor = null;
      existing.remove();
      existing = null;
    } else {
      if (!statusPollTimer) startStatusPoll(localPhase === "queued" || localPhase === "downloading");
      return true;
    }
  }

  const wrapper = createWrapper(mount.like || mount.anchor || mount.container);
  const ok = ensurePlacement(wrapper, mount);
  if (!ok) wrapper.remove();
  else startStatusPoll(false);
  return ok;
}

function observeShadowTrees(root = document.documentElement) {
  if (!root) return;

  const visit = (node) => {
    if (!node) return;
    if (node.shadowRoot) attachShadowObserver(node.shadowRoot);
    const children = node.querySelectorAll ? node.querySelectorAll("*") : [];
    for (const child of children) {
      if (child.shadowRoot) attachShadowObserver(child.shadowRoot);
    }
  };

  visit(root);
  if (root.shadowRoot) visit(root.shadowRoot);
}

function attachShadowObserver(shadowRoot) {
  if (!shadowRoot || shadowObservers.has(shadowRoot)) return;
  shadowObservers.add(shadowRoot);

  const observer = new MutationObserver(() => {
    if (isWatchPage() && !hasVisibleDownloadButton()) tryInject();
  });

  try {
    observer.observe(shadowRoot, { childList: true, subtree: true });
  } catch {
    /* ignore */
  }
}

function startInjectBurst() {
  const burstId = ++injectBurstId;
  // Dense early retries until Like row exists (inline mount only — no float).
  const delays = [0, 50, 100, 200, 350, 500, 750, 1000, 1500, 2200, 3000, 4500, 6500, 9000];

  for (const delay of delays) {
    setTimeout(() => {
      if (burstId !== injectBurstId || !isWatchPage()) return;
      observeShadowTrees();
      tryInject();
    }, delay);
  }
}

function startPoll() {
  if (pollTimer) return;
  pollTimer = setInterval(() => {
    if (!isWatchPage()) {
      stopPoll();
      return;
    }
    observeShadowTrees();
    if (!hasVisibleDownloadButton()) tryInject();
  }, 300);
}

function stopPoll() {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

function onPageChange() {
  if (!isWatchPage()) {
    lastWatchKey = "";
    removeButton();
    stopPoll();
    return;
  }

  const watchKey = getWatchKey();
  const videoChanged = watchKey !== lastWatchKey;
  if (videoChanged) {
    lastWatchKey = watchKey;
    suggestedDownloadLabel = "";
    localPhase = "idle";
    sawThisVisitDownload = false;
    timedResetDone = false;
    holdUntilAppSeesJob = 0;
    pagePlayerResponse = null;
    stopStatusPoll();
    clearResetTimer();
    removeButton();
    startInjectBurst();
    requestPagePlayer();
    void rememberHealthSettings();
  }

  observeShadowTrees();
  tryInject();
  startPoll();
}

function hookNavigation() {
  const notify = () => queueMicrotask(onPageChange);

  window.addEventListener("popstate", notify);
  window.addEventListener("yt-navigate-finish", notify);
  document.addEventListener("yt-navigate-finish", notify);

  const wrapHistory = (original) =>
    function historyHook(...args) {
      const result = original.apply(this, args);
      notify();
      return result;
    };

  history.pushState = wrapHistory(history.pushState);
  history.replaceState = wrapHistory(history.replaceState);
}

hookNavigation();

const pageObserver = new MutationObserver(() => {
  if (!isWatchPage()) return;
  observeShadowTrees();
  if (!hasVisibleDownloadButton()) tryInject();
});
pageObserver.observe(document.documentElement, { childList: true, subtree: true });

window.addEventListener("load", onPageChange);
if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", onPageChange);
} else {
  onPageChange();
}
