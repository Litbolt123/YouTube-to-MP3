const portInput = document.getElementById("port");
const tokenInput = document.getElementById("token");
const statusEl = document.getElementById("status");

async function load() {
  const saved = await chrome.storage.sync.get({ port: 47384, token: "" });
  portInput.value = saved.port;
  tokenInput.value = saved.token;
}

async function save() {
  const port = Number(portInput.value) || 47384;
  const token = tokenInput.value.trim();
  await chrome.storage.sync.set({ port, token });
  statusEl.textContent = "Saved.";
}

async function testConnection() {
  await save();
  statusEl.textContent = "Testing…";
  const result = await ytdlHealthCheck();
  statusEl.textContent = result?.ok
    ? `Connected to ${result.data.app} v${result.data.version}.`
    : result?.error || "Could not reach the app. Is it running with the extension enabled?";
}

document.getElementById("save").addEventListener("click", save);
document.getElementById("test").addEventListener("click", testConnection);
load();
