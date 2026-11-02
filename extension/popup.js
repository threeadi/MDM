document.addEventListener("DOMContentLoaded", async () => {
  const interceptorToggle = document.getElementById("interceptor-toggle");
  const autocloseToggle = document.getElementById("autoclose-toggle");
  const whitelistToggle = document.getElementById("whitelist-toggle");
  const ipcBadge = document.getElementById("ipc-badge");
  const ipcText = document.getElementById("ipc-text");
  const mediaContainer = document.getElementById("media-container");
  const rescanBtn = document.getElementById("rescan-btn");
  const btnOpenApp = document.getElementById("btn-open-app");
  const btnOpenOptions = document.getElementById("btn-open-options");

  const cardAutoCatch = document.getElementById("card-auto-catch");
  const cardAutoClose = document.getElementById("card-auto-close");
  const cardWhitelist = document.getElementById("card-whitelist");

  // Check MDM desktop connection status
  async function checkDesktopConnection() {
    if (!ipcBadge || !ipcText) return;
    try {
      const res = await fetch("http://127.0.0.1:18888/", { method: "GET", signal: AbortSignal.timeout(1200) });
      if (res.ok) {
        ipcText.textContent = "MDM Terhubung";
        ipcBadge.classList.remove("badge-offline");
        return;
      }
    } catch (e) {}

    ipcText.textContent = "MDM Terputus";
    ipcBadge.classList.add("badge-offline");
  }

  checkDesktopConnection();

  function updateCardStyles() {
    if (cardAutoCatch && interceptorToggle) {
      cardAutoCatch.classList.toggle("disabled-card", !interceptorToggle.checked);
    }
    if (cardAutoClose && autocloseToggle) {
      cardAutoClose.classList.toggle("disabled-card", !autocloseToggle.checked);
    }
    if (cardWhitelist && whitelistToggle) {
      cardWhitelist.classList.toggle("disabled-card", !whitelistToggle.checked);
    }
  }

  // Load current settings from background
  chrome.runtime.sendMessage({ action: "get-interceptor-settings" }, (settings) => {
    if (settings) {
      if (interceptorToggle && settings.isInterceptorEnabled !== undefined) {
        interceptorToggle.checked = Boolean(settings.isInterceptorEnabled);
      }
      if (autocloseToggle && settings.autoCloseNewTab !== undefined) {
        autocloseToggle.checked = Boolean(settings.autoCloseNewTab);
      }
      if (whitelistToggle && settings.filterByExtensions !== undefined) {
        whitelistToggle.checked = Boolean(settings.filterByExtensions);
      }
      updateCardStyles();
    }
  });

  function syncSettings() {
    const isInterceptorEnabled = interceptorToggle ? interceptorToggle.checked : true;
    const autoCloseNewTab = autocloseToggle ? autocloseToggle.checked : true;
    const filterByExtensions = whitelistToggle ? whitelistToggle.checked : true;

    updateCardStyles();

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

  // Load detected media
  async function loadDetectedMedia() {
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
  }

  loadDetectedMedia();

  rescanBtn?.addEventListener("click", () => {
    const originalText = rescanBtn.innerHTML;
    rescanBtn.style.opacity = "0.5";
    loadDetectedMedia();
    setTimeout(() => {
      rescanBtn.style.opacity = "1";
    }, 400);
  });

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
      urlDiv.textContent = m.filename || m.url;
      urlDiv.title = m.url;

      const tag = document.createElement("div");
      tag.className = "media-tag";
      const tagText = m.url.includes(".m3u8") ? "HLS STREAM" : (m.format || m.quality || "MEDIA");
      const sizeText = m.sizeFormatted ? ` • ${m.sizeFormatted}` : "";
      tag.textContent = `${tagText}${sizeText}`;

      info.appendChild(urlDiv);
      info.appendChild(tag);

      const btn = document.createElement("button");
      btn.className = "btn-dl";
      btn.textContent = "Unduh";
      btn.addEventListener("click", () => {
        btn.textContent = "Terkirim ✓";
        btn.classList.add("btn-sent");
        chrome.runtime.sendMessage({
          action: "send-download",
          url: m.url,
          filename: m.filename || "",
          referer: pageUrl
        });
      });

      item.appendChild(info);
      item.appendChild(btn);
      mediaContainer.appendChild(item);
    });
  }

  // Quick Action Buttons
  btnOpenApp?.addEventListener("click", async () => {
    try {
      await fetch("http://127.0.0.1:18888/", { method: "GET" });
    } catch (e) {}
    const originalContent = btnOpenApp.innerHTML;
    btnOpenApp.innerHTML = '<span>Membuka MDM Turbo...</span>';
    setTimeout(() => {
      btnOpenApp.innerHTML = originalContent;
      checkDesktopConnection();
    }, 1500);
  });

  btnOpenOptions?.addEventListener("click", () => {
    if (chrome.runtime.openOptionsPage) {
      chrome.runtime.openOptionsPage();
    } else {
      window.open(chrome.runtime.getURL("options.html"));
    }
  });
});