# YouTube Downloader — browser extension (friend guide)

Use this guide if someone sent you **YouTube Downloader** and you want a **Download** button on YouTube pages in Chrome or Edge.

You do **not** download the extension from the Chrome Web Store. It is **included with the desktop app** you already installed.

---

## What you need

| Thing | Why |
|-------|-----|
| **YouTube Downloader** (desktop app) | Must be installed and **running** while you browse |
| **Chrome** or **Microsoft Edge** | Firefox is not supported for this extension yet |
| About **2 minutes** | One-time setup |

**Works on:** Windows 10/11 (same PC as the app).

---

## Part 1 — Turn on the connection in the app

1. Open **YouTube Downloader** from the Start Menu.
2. Click **Settings…** (top right).
3. Scroll to **Local API (extension & Music Hub)**.
4. Make sure **Enable local download API** is checked.
5. Click **Copy** next to **Connection token** (you will paste this into the browser in a minute).
6. Click **Open extension folder**. A File Explorer window opens — leave it open or remember this folder. It is usually one of:
   - `C:\Users\<YourName>\AppData\Local\Programs\YouTubeToMp3\browser-extension`
   - `C:\Users\<YourName>\AppData\Local\YouTubeToMp3\browser-extension`
7. Click **Save** at the bottom of Settings, then leave the app open (you can minimize it).

> **Tip:** Enable **Start with Windows** in Settings if you want the app ready whenever you use the extension.

---

## Part 2 — Load the extension in Chrome or Edge

This is a one-time step per browser. Chrome calls it “Load unpacked” because the extension ships with the app, not the store.

### Google Chrome

1. Open a new tab and go to: `chrome://extensions`
2. Turn on **Developer mode** (toggle in the top-right corner).
3. Click **Load unpacked**.
4. In the folder picker, select the **`browser-extension`** folder from Part 1 (the one that contains `manifest.json`).
5. You should see **YouTube Downloader Bridge** in the list.

### Microsoft Edge

1. Open a new tab and go to: `edge://extensions`
2. Turn on **Developer mode** (bottom-left or sidebar, depending on Edge version).
3. Click **Load unpacked**.
4. Select the same **`browser-extension`** folder.
5. You should see **YouTube Downloader Bridge** in the list.

### Pin the extension (optional but helpful)

Click the **puzzle piece** icon in the browser toolbar → pin **YouTube Downloader Bridge** so it is easy to find.

---

## Part 3 — Connect the extension to the app

1. Click the **YouTube Downloader Bridge** icon in the toolbar.
2. **Port** should be `47384` (default — only change if you changed it in the app).
3. Paste the **Connection token** you copied from the app.
4. Click **Save**, then **Test connection**.
5. You should see a success message. If not, see [Troubleshooting](#troubleshooting) below.

---

## Part 4 — Download from YouTube

1. Make sure **YouTube Downloader** is still running on your PC.
2. Open any YouTube **watch** page (a single video).
3. Look for a red **Download** button near **Like / Dislike**.

From there you can:

| Button | What it does |
|--------|----------------|
| **Audio** | Quick download as MP3 (or your default audio format in the app) |
| **Video** | Quick download as MP4 |
| **Whole playlist** | Appears when the URL has a playlist — downloads the full list |
| **More options…** | Pick format (MP3, M4A, FLAC, …), quality, music vs video folder |

Downloads appear in the desktop app’s queue and save to the music/video folder you chose in the app.

---

## After app updates

When you install a newer **YouTubeToMp3-Setup-*.exe**:

1. Open the app → **Settings** → **Open extension folder** (files may have updated).
2. Go to `chrome://extensions` or `edge://extensions`.
3. Find **YouTube Downloader Bridge** and click **Reload**.
4. If you regenerated the token in the app, paste the new token in the extension and **Save** again.

You usually **do not** need to “Load unpacked” again unless you removed the extension from the browser.

---

## Troubleshooting

| Problem | What to try |
|---------|-------------|
| **“Could not reach the app”** | Open YouTube Downloader on the same PC. Check **Enable local download API** in Settings. |
| **“Invalid token”** | App → Settings → **Copy** token again → extension → paste → **Save**. |
| **No Download button on YouTube** | Refresh the video page. On `chrome://extensions`, click **Reload** on the extension. |
| **Button worked before, stopped** | App may have closed — launch it again. |
| **Changed port in the app** | Put the same port in the extension options and click **Save**. |
| **“Inactive” service worker** | Normal when idle. Click **Test connection** or download something — it wakes up. |
| **Can’t find the folder** | Settings → **Open extension folder**. Or browse to `%LocalAppData%\Programs\YouTubeToMp3\browser-extension`. |

---

## Is this safe?

- The extension only talks to the app on **your computer** (`127.0.0.1`), not over the internet.
- It needs the secret **token** from your app — other websites and other people cannot use your downloader.
- You can turn it off anytime: uncheck **Enable local download API** in the app, or remove the extension from the browser.

---

## Optional: Local Music Hub

If you also installed **Local Music Hub**, it uses the same API. You can queue downloads from Music Hub’s sidebar instead of (or as well as) the browser button. See the [music stack setup guide](https://github.com/Litbolt123/Local-Music-Hub/blob/main/docs/music-stack-setup.md) in the Local Music Hub repo.

---

## Quick checklist

- [ ] YouTube Downloader installed and running  
- [ ] Local API enabled in Settings  
- [ ] Token copied  
- [ ] Extension loaded via **Load unpacked** in Chrome/Edge  
- [ ] Token pasted in extension → **Test connection** OK  
- [ ] Red **Download** button visible on a YouTube video page  

**Repo:** [Litbolt123/YouTube-to-MP3](https://github.com/Litbolt123/YouTube-to-MP3) — technical details in `docs/browser-extension.md`.
