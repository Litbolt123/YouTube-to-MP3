# Injecting a button into YouTube’s watch action row

Lessons from **YouTube Downloader Bridge** (extension v1.4.4+). Reuse this pattern for any site that lazy-loads a toolbar next to Like / Share / etc.

## Goal

Put a custom control **in the same row** as the host’s native buttons so it:

- Appears as soon as that row exists (no “scroll to comments” wait)
- Scrolls with the page (no jump)
- Does not cover the channel avatar / title

## Do / don’t

| Do | Don’t |
|----|--------|
| Mount **inline** in document flow (`insertBefore` into the action row) | Use `position: fixed` / float that re-anchors on `scroll` |
| Prefer the **computed top-level button row** (`#top-level-buttons-computed`) | Mount into broad `#actions` (includes owner/channel) |
| Accept nodes that are **connected but 0×0** (not laid out yet) | Require `getBoundingClientRect()` size / “visible in viewport” |
| Resolve `insertBefore` via **direct child of the row** that *contains* Like | Assume Like is a direct child of the row |
| Fall back: prepend in row → before Share → append | Fall back to floating over `#primary-inner` / metadata |
| Poll + MutationObserver + navigate hooks until mounted | One-shot inject at `document_idle` only |
| Upgrade placement when Like appears (`insertBefore(likeChild)`) | Re-pick anchors on every scroll event |

## Mount strategy (ordered)

1. **Best:** Find Like/Dislike host (`segmented-like-dislike-button-view-model` or legacy `ytd-segmented-like-dislike-button-renderer`).
2. Find its **button row** (walk up to `#top-level-buttons-computed`, or menu / flexible-item hosts).
3. Compute `before = directChildContaining(row, like)` — the row’s child that wraps Like (may be nested).
4. `row.insertBefore(wrapper, before)`.
5. **Early (Like not ready):** If the row exists, prepend (or insert before Share). When Like later appears, move to `insertBefore(likeChild)`.
6. **Last resort:** Share’s `parentElement` + `insertBefore(share)` — still flow layout.

```text
#top-level-buttons-computed
  ├── [our wrapper]          ← insertBefore(likeChild)
  ├── <… wraps Like/Dislike …>
  ├── Share
  └── …
```

## Why “wait for comments” happened

YouTube often **lazy-hydrates** engagement UI. Gates that fail early:

- `getBoundingClientRect().width > 8` (row is 0×0 until hydrate)
- “Must be below the fold / near comments”
- “Like must be visible”
- Only querying `#top-level-buttons-computed` when the live DOM still uses another menu host

**Fix:** treat **connected** as enough; soft-gate layout; broaden early hosts; keep mounting in flow.

## Why scroll jump happened

`position: fixed` + `scroll` listener that re-reads `getBoundingClientRect()` on changing anchors (channel → Like → primary). Each scroll recalculated `top`/`left` → bounce.

**Fix:** never fixed-position for the steady state. Inline flex child scrolls with the row for free.

## Shadow DOM

YouTube uses shadow roots. Use deep `querySelector` that walks `shadowRoot`, and inject styles into the shadow root that hosts the button when needed.

## SPA navigation

Hook `yt-navigate-finish`, `popstate`, and `history.pushState` / `replaceState`. On video change: remove old button, burst-retry inject (0–few seconds), keep a light poll until mounted.

## Spacing tweak

Wrapper margins control gap vs Like. Example (slightly right of flush-left):

`margin: 0 4px 0 6px` → 6px from the left of the row, 4px before Like.

## Checklist for a new site

1. Identify the **stable action row** in DevTools (not the whole page chrome).
2. Confirm whether controls live in **shadow DOM**.
3. Inject **inline** next to a known sibling (Like / Share).
4. Don’t require visibility; require `isConnected`.
5. Handle SPA route changes.
6. Avoid fixed overlays unless the product truly needs a floating FAB — and then pin one anchor, don’t re-hunt on scroll.

## Code pointers (this repo)

- `browser-extension/content.js` — `findMountPoint`, `directChildContaining`, `ensurePlacement`, `tryInject`
- `browser-extension/content.css` — `.ytmp3-wrapper` margins
- Working version: extension **1.4.4+**
