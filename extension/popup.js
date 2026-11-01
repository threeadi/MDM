document.addEventListener("DOMContentLoaded", async () => {
  const interceptorToggle = document.getElementById("interceptor-toggle");
  const autocloseToggle = document.getElementById("autoclose-toggle");
  const whitelistToggle = document.getElementById("whitelist-toggle");
  const statusBadge = document.getElementById("status-badge");
  const ipcBadge = document.getElementById("ipc-badge");
  const mediaContainer = document.getElementById("media-container");

  // Check MDM desktop connection status
  async function checkDesktopConnection() {
    if (!ipcBadge) return;
    try {
      const res = await fetch("http://127.0.0.1:18888/", { method: "GET", signal: AbortSignal.timeout(1200) });
      if (res.ok) {
        ipcBadge.textContent = "MDM Terhubung";
        ipcBadge.style.color = "#34d399";
        ipcBadge.style.borderColor = "rgba(16, 185, 129, 0.3)";
        ipcBadge.style.background = "rgba(16, 185, 129, 0.15)";
        return;
      }
    } catch (e) {}

    ipcBadge.textContent = "MDM Offline";
    ipcBadge.style.color = "#ff5252";
    ipcBadge.style.borderColor = "rgba(255, 82, 82, 0.3)";
    ipcBadge.style.background = "rgba(255, 82, 82, 0.15)";
  }

  checkDesktopConnection();

  // Load current settings from background
  chrome.runtime.sendMessage({ action: "get-interceptor-settings" }, (settings) => {
    if (settings) {
      if (interceptorToggle) interceptorToggle.checked = settings.isInterceptorEnabled;
      if (autocloseToggle) autocloseToggle.checked = settings.autoCloseNewTab;
      if (whitelistToggle) whitelistToggle.checked = settings.filterByExtensions;
      updateStatusBadge(settings.isInterceptorEnabled);
    }
  });

  function updateStatusBadge(active) {
    if (!statusBadge) return;
    if (active) {
      statusBadge.textContent = "Aktif";
      statusBadge.style.color = "#34d399";
      statusBadge.style.borderColor = "rgba(16, 185, 129, 0.3)";
      statusBadge.style.background = "rgba(16, 185, 129, 0.15)";
    } else {
      statusBadge.textContent = "Dijeda";
      statusBadge.style.color = "#f87171";
      statusBadge.style.borderColor = "rgba(239, 68, 68, 0.3)";
      statusBadge.style.background = "rgba(239, 68, 68, 0.15)";
    }
  }

  function syncSettings() {
    const isInterceptorEnabled = interceptorToggle ? interceptorToggle.checked : true;
    const autoCloseNewTab = autocloseToggle ? autocloseToggle.checked : true;
    const filterByExtensions = whitelistToggle ? whitelistToggle.checked : true;

    updateStatusBadge(isInterceptorEnabled);

    chrome.runtime.sendMessage({
      action: "update-interceptor-settings",
      isInterceptorEnabled,
      autoCloseNewTab,
      filterByExtensions
    });
  }

  interceptorToggle?.addEventListener("change", syncSettings);
  autocloseToggle?.addEventListener("change", syncSettings);
  whitelistToggle?.addEventListener("change", syncSettings);

  // Get detected media for active tab
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tab?.id) {
      chrome.runtime.sendMessage(
        { action: "get-detected-media", tabId: tab.id },
        (response) => {
          if (response) {
            renderMedia(response.media, tab.url);
          }
        }
      );
    }
  } catch (e) {
    console.warn("Could not query active tab:", e);
  }

  function renderMedia(mediaList, pageUrl) {
    if (!mediaContainer) return;
    if (!mediaList || mediaList.length === 0) {
      mediaContainer.innerHTML = '<div class="empty-state">Tidak ada video atau audio stream terdeteksi di halaman ini.</div>';
      return;
    }

    mediaContainer.innerHTML = "";
    mediaList.forEach((m) => {
      const item = document.createElement("div");
      item.className = "media-item";

      const info = document.createElement("div");
      info.className = "media-info";

      const urlDiv = document.createElement("div");
      urlDiv.className = "media-url";
      urlDiv.textContent = m.url;
      urlDiv.title = m.url;

      const tag = document.createElement("div");
      tag.className = "media-tag";
      tag.textContent = m.url.includes(".m3u8") ? "HLS STREAM" : (m.format || "MEDIA");

      info.appendChild(urlDiv);
      info.appendChild(tag);

      const btn = document.createElement("button");
      btn.className = "btn-dl";
      btn.textContent = "Unduh";
      btn.addEventListener("click", () => {
        btn.textContent = "Terkirim ✓";
        btn.style.background = "#10b981";
        chrome.runtime.sendMessage({
          action: "send-download",
          url: m.url,
          referer: pageUrl
        });
      });

      item.appendChild(info);
      item.appendChild(btn);
      mediaContainer.appendChild(item);
    });
  }
});
