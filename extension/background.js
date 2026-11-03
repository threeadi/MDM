const NATIVE_HOST = "com.myownidm.host";

// Default Registered File Extensions Whitelist (Classic IDM standard formats)
const DEFAULT_REGISTERED_EXTENSIONS = [
  // Archives & Disk Images
  "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "dmg", "img",
  // Executables & Installers
  "exe", "msi", "apk", "bin", "deb", "rpm", "jar",
  // Video
  "mp4", "mkv", "webm", "avi", "mov", "flv", "wmv", "3gp", "ts",
  // Audio
  "mp3", "aac", "wav", "flac", "ogg", "m4a", "wma", "opus",
  // Documents
  "pdf", "docx", "xlsx", "pptx", "epub", "csv"
];

// Leveled Logging System for MDM Browser Extension
const LogLevel = { DEBUG: 0, INFO: 1, WARN: 2, ERROR: 3 };

function generateRayId(prefix = "ray_ext") {
  const hex = Math.floor(Math.random() * 0xffffffff).toString(16).padStart(8, "0");
  return `${prefix}_${hex}`;
}

const ExtensionLogger = {
  minimumLevel: LogLevel.DEBUG,

  _format(levelName, comp, message, rayId = null) {
    const time = new Date().toISOString().substring(11, 23);
    const rayTag = rayId ? ` [${rayId}]` : "";
    return `[${time}] [${levelName}] [${comp}]${rayTag} ${message}`;
  },

  _forwardToDesktop(levelName, comp, message, rayId = null) {
    try {
      fetch("http://127.0.0.1:18888/log", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ level: levelName, component: comp, message: String(message), ray_id: rayId || undefined })
      }).catch(() => {});
    } catch (_) {}
  },

  debug(msg, comp = "EXT:BG", extra, rayId = null) {
    if (this.minimumLevel <= LogLevel.DEBUG) {
      console.debug(this._format("DEBUG", comp, msg, rayId), extra !== undefined ? extra : "");
    }
  },

  info(msg, comp = "EXT:BG", extra, rayId = null) {
    if (this.minimumLevel <= LogLevel.INFO) {
      console.info(this._format("INFO", comp, msg, rayId), extra !== undefined ? extra : "");
      this._forwardToDesktop("INFO", comp, msg, rayId);
    }
  },

  warn(msg, comp = "EXT:BG", extra, rayId = null) {
    if (this.minimumLevel <= LogLevel.WARN) {
      console.warn(this._format("WARN", comp, msg, rayId), extra !== undefined ? extra : "");
      this._forwardToDesktop("WARN", comp, msg, rayId);
    }
  },

  error(msg, comp = "EXT:BG", extra, rayId = null) {
    if (this.minimumLevel <= LogLevel.ERROR) {
      console.error(this._format("ERROR", comp, msg, rayId), extra !== undefined ? extra : "");
      this._forwardToDesktop("ERROR", comp, msg, rayId);
    }
  }
};

// State
let isInterceptorEnabled = true;
let bypassUntilTimestamp = 0;
let autoCloseNewTab = true;
let filterByExtensions = true;
let minFileSizeKb = 0; // 0 = capture all sizes
let registeredExtensions = [...DEFAULT_REGISTERED_EXTENSIONS];
let newlyOpenedTabs = new Map(); // tabId -> { url, createdAt }
let detectedMediaByTab = {};

// Load persisted configuration from storage
if (typeof chrome !== "undefined" && chrome.storage && chrome.storage.local) {
  chrome.storage.local.get([
    "isInterceptorEnabled",
    "autoCloseNewTab",
    "filterByExtensions",
    "minFileSizeKb",
    "registeredExtensions"
  ], (res) => {
    if (res) {
      if (res.isInterceptorEnabled !== undefined) isInterceptorEnabled = Boolean(res.isInterceptorEnabled);
      if (res.autoCloseNewTab !== undefined) autoCloseNewTab = Boolean(res.autoCloseNewTab);
      if (res.filterByExtensions !== undefined) filterByExtensions = Boolean(res.filterByExtensions);
      if (typeof res.minFileSizeKb === "number") minFileSizeKb = res.minFileSizeKb;
      if (Array.isArray(res.registeredExtensions)) registeredExtensions = res.registeredExtensions;
    }
  });
}

// Track newly opened tabs to close orphaned blank tabs when download is intercepted
if (typeof chrome !== "undefined" && chrome.tabs && chrome.tabs.onCreated) {
  chrome.tabs.onCreated.addListener((tab) => {
    if (tab && tab.id) {
      const initialUrl = tab.url || tab.pendingUrl || "";
      newlyOpenedTabs.set(tab.id, {
        url: initialUrl,
        createdAt: Date.now()
      });
      setTimeout(() => {
        newlyOpenedTabs.delete(tab.id);
      }, 30000);
    }
  });

  chrome.tabs.onUpdated?.addListener((tabId, changeInfo) => {
    if (newlyOpenedTabs.has(tabId) && changeInfo.url) {
      const rec = newlyOpenedTabs.get(tabId);
      if (rec) rec.url = changeInfo.url;
    }
  });

  chrome.tabs.onRemoved?.addListener((tabId) => {
    newlyOpenedTabs.delete(tabId);
  });
}

function getFileExtension(filenameOrUrl) {
  if (!filenameOrUrl) return "";
  try {
    const clean = String(filenameOrUrl).split("?")[0].split("#")[0];
    const segments = clean.split("/").filter(Boolean);
    const last = segments[segments.length - 1] || clean;
    const dotIdx = last.lastIndexOf(".");
    if (dotIdx !== -1 && dotIdx < last.length - 1) {
      return last.substring(dotIdx + 1).toLowerCase();
    }
  } catch (e) {}
  return "";
}

function isExtensionRegistered(filenameOrUrl, list = registeredExtensions) {
  if (!list || list.length === 0) return true;
  const ext = getFileExtension(filenameOrUrl);
  if (!ext) return true; // If no extension detectable from URL, let desktop prober inspect it
  return list.includes(ext.toLowerCase());
}

function closeMatchingNewTab(downloadUrl, refererUrl) {
  if (typeof chrome === "undefined" || !chrome.tabs || !chrome.tabs.remove) return;
  const now = Date.now();
  for (const [tabId, info] of newlyOpenedTabs.entries()) {
    if (now - info.createdAt < 15000) {
      const tabUrl = (info.url || "").toLowerCase();
      const targetUrl = (downloadUrl || "").toLowerCase();
      if (tabUrl === targetUrl || tabUrl === "about:blank" || tabUrl === "" || (refererUrl && tabUrl === refererUrl.toLowerCase())) {
        try {
          chrome.tabs.remove(tabId);
        } catch (e) {}
        newlyOpenedTabs.delete(tabId);
        break;
      }
    }
  }
}

// 1. Setup Context Menus on Install
if (typeof chrome !== "undefined" && chrome.runtime?.onInstalled) {
  chrome.runtime.onInstalled.addListener(() => {
    chrome.contextMenus?.create?.({
      id: "download-with-myownidm",
      title: "Unduh dengan MDM Turbo",
      contexts: ["link", "video", "audio", "image"]
    });
  });
}

if (typeof chrome !== "undefined" && chrome.contextMenus?.onClicked) {
  chrome.contextMenus.onClicked.addListener((info, tab) => {
    if (info.menuItemId === "download-with-myownidm") {
      const targetUrl = info.linkUrl || info.srcUrl;
      if (targetUrl) {
        sendToMyOwnIdm(targetUrl, tab?.url);
      }
    }
  });
}

function extractFilenameFromUrl(urlStr) {
  try {
    const parsed = new URL(urlStr);
    const pathname = parsed.pathname;
    const segments = pathname.split("/").filter(Boolean);
    const last = segments[segments.length - 1];
    if (last && last.includes(".")) {
      return decodeURIComponent(last);
    }
  } catch (e) {}
  return "";
}

// 2. Intercept Browser Downloads
if (typeof chrome !== "undefined" && chrome.downloads?.onCreated) {
  chrome.downloads.onCreated.addListener(async (item) => {
  if (!isInterceptorEnabled) return;

  // Ignore data URLs and extension internal downloads
  if (!item.url || item.url.startsWith("blob:") || item.url.startsWith("data:")) return;

  // 1. Check if download was bypassed (e.g. user held Alt key while clicking)
  if (Date.now() < bypassUntilTimestamp) {
    ExtensionLogger.debug("Download di-bypass oleh tombol pintas pengguna (Alt ditahan). Diteruskan ke browser.", "EXT:INTERCEPT");
    return;
  }

  // 2. Check registered file extensions whitelist
  const safeFilename = item.filename || extractFilenameFromUrl(item.url);
  if (filterByExtensions && !isExtensionRegistered(safeFilename || item.url, registeredExtensions)) {
    ExtensionLogger.debug(`Ekstensi berkas tidak terdaftar di whitelist. Diteruskan ke browser: ${safeFilename || item.url}`, "EXT:INTERCEPT");
    return;
  }

  // 3. Check minimum file size threshold (if Content-Length or totalBytes is known)
  if (minFileSizeKb > 0 && item.totalBytes > 0 && item.totalBytes < minFileSizeKb * 1024) {
    ExtensionLogger.debug(`Ukuran berkas (${item.totalBytes} B) di bawah ambang batas minimum (${minFileSizeKb} KB). Diteruskan ke browser.`, "EXT:INTERCEPT");
    return;
  }

  ExtensionLogger.info(`Mencegat unduhan browser: ${safeFilename || item.url} (${item.totalBytes ? item.totalBytes + " B" : "ukuran dinamis"})`, "EXT:INTERCEPT");

  // Pause and cancel browser download immediately
  try {
    await chrome.downloads.cancel(item.id);
    await chrome.downloads.erase({ id: item.id });
  } catch (e) {
    // Ignore if already cancelled
  }

  // Auto-close blank new tab if this download opened one
  if (autoCloseNewTab) {
    closeMatchingNewTab(item.url, item.referrer);
  }

  // Forward to desktop client with fallback URL filename
  sendToMyOwnIdm(item.url, item.referrer, safeFilename);
  });
}

// 3. Media Sniffer (Video/Audio/m3u8 stream detection)
const STREAM_EXTENSIONS = [".m3u8", "/pl/", "playlist.m3u8", "master.m3u8", ".urlset", "master.txt", "index.txt"];
const FILE_EXTENSIONS = [".mp4", ".mkv", ".webm", ".flv", ".mp3", ".aac", ".ogg"];
const NON_MEDIA_EXTENSIONS = [
  ".js", ".mjs", ".css", ".json", ".map", ".wasm",
  ".html", ".htm", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".woff", ".woff2", ".ttf"
];

function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return "";
  const units = ["B", "KB", "MB", "GB"];
  let i = 0;
  let val = bytes;
  while (val >= 1024 && i < units.length - 1) {
    val /= 1024;
    i++;
  }
  return `${val.toFixed(val >= 100 || i === 0 ? 0 : 1)} ${units[i]}`;
}

if (typeof chrome !== "undefined" && chrome.webRequest?.onResponseStarted) {
  chrome.webRequest.onResponseStarted.addListener(
  (details) => {
    if (details.tabId < 0) return;
    const url = details.url.toLowerCase();

    // STRICTLY IGNORE scripts, stylesheets, fonts, and static non-media assets
    if (NON_MEDIA_EXTENSIONS.some(ext => {
      const idx = url.indexOf(ext);
      if (idx === -1) return false;
      const nextChar = url.charAt(idx + ext.length);
      return nextChar === "" || nextChar === "?" || nextChar === "#" || nextChar === "/";
    })) {
      return;
    }

    // STRICTLY IGNORE fragment chunks (like .m4s, .ts segments, internal SABR videoplayback)
    if (
      url.includes(".m4s") ||
      url.includes(".ts?") ||
      url.endsWith(".ts") ||
      url.includes("videoplayback") ||
      url.includes("/segment") ||
      url.includes("range=") ||
      url.includes("/avc1/") ||
      url.match(/\/\d+\/\d+\/\d+$/)
    ) {
      return;
    }

    const contentTypeHeader = details.responseHeaders?.find(
      h => h.name.toLowerCase() === "content-type"
    );
    const ct = contentTypeHeader?.value?.toLowerCase() || "";
    if (ct.includes("javascript") || ct.includes("json") || ct.includes("css") || ct.includes("text/html") || ct.startsWith("image/") || ct.includes("image/")) {
      return;
    }

    const isStream = STREAM_EXTENSIONS.some(ext => url.includes(ext));
    const isFile = FILE_EXTENSIONS.some(ext => url.includes(ext));
    const isStreamMime = ct.includes("mpegurl");

    if (isStream || isStreamMime || isFile) {
      // Parse content-length or total size from content-range if available
      let contentLength = null;
      const contentRangeHeader = details.responseHeaders?.find(
        h => h.name.toLowerCase() === "content-range"
      );
      if (contentRangeHeader?.value) {
        const rangeMatch = contentRangeHeader.value.match(/\/(\d+)/);
        if (rangeMatch && rangeMatch[1]) {
          const totalFromRange = parseInt(rangeMatch[1], 10);
          if (!isNaN(totalFromRange) && totalFromRange > 0) {
            contentLength = totalFromRange;
          }
        }
      }

      if (!contentLength) {
        const contentLengthHeader = details.responseHeaders?.find(
          h => h.name.toLowerCase() === "content-length"
        );
        if (contentLengthHeader?.value) {
          const parsedLen = parseInt(contentLengthHeader.value, 10);
          if (!isNaN(parsedLen) && parsedLen > 0) {
            contentLength = parsedLen;
          }
        }
      }

      // Ignore tracking pixels, beacons, or empty error stubs (< 20 KB) for non-stream files
      if (!isStream && !isStreamMime && contentLength !== null && contentLength > 0 && contentLength < 20480) {
        return;
      }

      if (!detectedMediaByTab[details.tabId]) {
        detectedMediaByTab[details.tabId] = [];
      }

      // Determine stream/file format
      let format = "MP4";
      if (isStream || isStreamMime) {
        format = "M3U8";
      } else if (url.includes(".webm") || ct.includes("webm")) {
        format = "WEBM";
      } else if (url.includes(".mp3") || ct.includes("audio/mpeg")) {
        format = "MP3";
      } else if (url.includes(".m4a") || ct.includes("audio/mp4")) {
        format = "M4A";
      } else if (url.includes(".mkv")) {
        format = "MKV";
      }

      // Check existing item
      const existing = detectedMediaByTab[details.tabId].find(m => m.url === details.url);
      if (!existing) {
        detectedMediaByTab[details.tabId].push({
          url: details.url,
          type: (isStream || isStreamMime) ? "stream" : "media",
          format: format,
          contentLength: contentLength,
          sizeFormatted: contentLength ? formatBytes(contentLength) : "",
          timestamp: Date.now()
        });

        // Update badge
        const count = detectedMediaByTab[details.tabId].length;
        try {
          chrome.action?.setBadgeText?.({ tabId: details.tabId, text: String(count) });
          chrome.action?.setBadgeBackgroundColor?.({ tabId: details.tabId, color: "#2563eb" });
        } catch (e) {}
      } else if (contentLength && (!existing.contentLength || contentLength > existing.contentLength)) {
        existing.contentLength = contentLength;
        existing.sizeFormatted = formatBytes(contentLength);
      }
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
  );
}

// Clear detected media on tab navigation
if (typeof chrome !== "undefined" && chrome.tabs?.onUpdated) {
  chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
    if (changeInfo.status === "loading" && changeInfo.url) {
      detectedMediaByTab[tabId] = [];
      try {
        chrome.action?.setBadgeText?.({ tabId, text: "" });
      } catch (e) {}
    }
  });
}

// Clean up closed tabs
if (typeof chrome !== "undefined" && chrome.tabs?.onRemoved) {
  chrome.tabs.onRemoved.addListener((tabId) => {
    delete detectedMediaByTab[tabId];
  });
}

// Helpers for smart filename resolution
function cleanFilename(rawTitle, defaultName = "video", maxLen = 80) {
  if (!rawTitle) return defaultName;
  let clean = rawTitle
    .replace(/\s*-\s*YouTube$/i, "")
    .replace(/\s*\/\s*X$/i, "")
    .replace(/\s*-\s*Twitter$/i, "")
    .replace(/\s*•\s*Instagram.*$/i, "")
    .replace(/\s*on Instagram:.*$/i, "")
    .replace(/\s*\|\s*TikTok$/i, "")
    .replace(/https?:\/\/\S+/gi, "")
    .trim();

  clean = clean.replace(/[\r\n\t]+/g, " ");
  clean = clean.replace(/[^\w\s\-\.\(\)\[\]]/g, " ");
  clean = clean.replace(/[\s_]+/g, "_").trim();
  clean = clean.replace(/^[\._\-]+|[\._\-]+$/g, "");

  if (clean.length > maxLen) {
    clean = clean.substring(0, maxLen).replace(/[\._\-]+$/, "");
  }

  return clean || defaultName;
}

function isGenericTitle(title) {
  if (!title || typeof title !== "string") return true;
  const base = title.replace(/\.[a-zA-Z0-9]+$/, "").trim().toLowerCase();
  const genericList = [
    "embed",
    "video",
    "player",
    "video player",
    "iframe",
    "untitled",
    "stream",
    "media player",
    "watch",
    "download",
    "play",
    "index",
    "master"
  ];
  return genericList.includes(base);
}

function resolveSmartFilename(requestedFilename, tabTitle, quality = "") {
  if (!tabTitle || isGenericTitle(tabTitle)) {
    return requestedFilename || "video.mp4";
  }

  const safeTabTitle = cleanFilename(tabTitle, "video", 80);

  if (!requestedFilename) {
    return quality ? `${safeTabTitle}_${quality}.mp4` : `${safeTabTitle}.mp4`;
  }

  let safeRequested = requestedFilename;
  if (safeRequested && (safeRequested.toLowerCase().endsWith(".m3u8") || safeRequested.toLowerCase().endsWith(".hls"))) {
    safeRequested = safeRequested.replace(/\.(m3u8|hls)$/i, ".mp4");
  }

  const match = safeRequested.match(/^(.*?)(_(?:4k|2160p|1080p|720p|480p|360p|audio|sub_[a-z0-9]+))?(\.[a-zA-Z0-9]+)?$/i);
  if (match) {
    const basePrefix = match[1] || "";
    const qualityTag = match[2] || (quality ? `_${quality}` : "");
    let ext = match[3] || ".mp4";
    if (ext.toLowerCase() === ".m3u8" || ext.toLowerCase() === ".hls") {
      ext = ".mp4";
    }

    if (isGenericTitle(basePrefix)) {
      return `${safeTabTitle}${qualityTag}${ext}`;
    }
    return `${basePrefix}${qualityTag}${ext}`;
  }

  return safeRequested;
}

// 4. Send to Desktop App via Local HTTP Server (Port 18888) or Native Messaging Fallback
async function sendToMyOwnIdm(downloadUrl, refererUrl = "", filename = "", extraMeta = {}) {
  const rayId = extraMeta.ray_id || generateRayId("ray_ext");
  try {
    ExtensionLogger.info(`Menginisiasi transfer unduhan: ${downloadUrl} (filename: ${filename || "auto"})`, "EXT:IPC", null, rayId);

    // Only get cookies for valid http/https URLs
    let cookieHeader = "";
    if (downloadUrl && (downloadUrl.startsWith("http://") || downloadUrl.startsWith("https://"))) {
      try {
        const cookies = await chrome.cookies.getAll({ url: downloadUrl });
        cookieHeader = cookies.map(c => `${c.name}=${c.value}`).join("; ");
      } catch (e) {
        ExtensionLogger.warn("Gagal membaca cookies:", "EXT:COOKIES", e, rayId);
      }
    }

    let effectiveFilename = filename || "";
    if (effectiveFilename && (effectiveFilename.toLowerCase().endsWith(".m3u8") || effectiveFilename.toLowerCase().endsWith(".hls"))) {
      effectiveFilename = effectiveFilename.replace(/\.(m3u8|hls)$/i, ".mp4");
    }

    const payload = {
      action: "download",
      ray_id: rayId,
      url: downloadUrl,
      filename: effectiveFilename,
      quality: extraMeta.quality || "",
      is_audio_only: extraMeta.is_audio_only || false,
      threads: extraMeta.threads || 16,
      is_drm_protected: Boolean(extraMeta.is_drm_protected),
      is_honeypot: Boolean(extraMeta.is_honeypot),
      is_decoy_canvas: Boolean(extraMeta.is_decoy_canvas),
      headers: {
        "Cookie": cookieHeader,
        "Referer": refererUrl || "",
        "User-Agent": navigator.userAgent
      }
    };

    // Try Local HTTP Server first (Port 18888)
    try {
      const res = await fetch("http://127.0.0.1:18888/download", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        ExtensionLogger.info("Permintaan unduhan berhasil dikirim ke MDM Desktop via HTTP IPC (200 OK)", "EXT:IPC", null, rayId);
        return true;
      }
    } catch (httpErr) {
      ExtensionLogger.warn("HTTP IPC lokal tidak terhubung, mencoba fallback Native Messaging...", "EXT:IPC", httpErr, rayId);
    }

    // Fallback: Native Messaging
    return new Promise((resolve) => {
      chrome.runtime.sendNativeMessage(NATIVE_HOST, payload, (response) => {
        if (chrome.runtime.lastError) {
          ExtensionLogger.error(`Komunikasi Native Messaging gagal: ${chrome.runtime.lastError.message}`, "EXT:IPC", null, rayId);
          resolve(false);
        } else {
          ExtensionLogger.info("Respon MDM Desktop via Native Messaging berhasil diterima", "EXT:IPC", response, rayId);
          resolve(true);
        }
      });
    });
  } catch (err) {
    ExtensionLogger.error("Gagal meneruskan unduhan ke desktop:", "EXT:IPC", err, rayId);
    return false;
  }
}

if (typeof chrome !== "undefined" && chrome.runtime?.onMessage) {
  chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === "get-tab-info") {
    sendResponse({
      tabId: sender.tab?.id,
      tabTitle: sender.tab?.title || "",
      tabUrl: sender.tab?.url || ""
    });
    return true;
  } else if (request.action === "get-detected-media") {
    const tabId = request.tabId || sender.tab?.id;
    const media = detectedMediaByTab[tabId] || [];
    sendResponse({ media, isInterceptorEnabled });
  } else if (request.action === "toggle-interceptor") {
    isInterceptorEnabled = request.enabled;
    sendResponse({ isInterceptorEnabled });
  } else if (request.action === "send-download") {
    let filename = request.filename || "";
    const tabTitle = sender.tab?.title;
    if (tabTitle) {
      filename = resolveSmartFilename(filename, tabTitle, request.quality);
    }
    const extraMeta = {
      quality: request.quality,
      is_audio_only: request.is_audio_only,
      threads: request.threads,
      batch: request.batch,
      is_drm_protected: Boolean(request.is_drm_protected),
      is_honeypot: Boolean(request.is_honeypot),
      is_decoy_canvas: Boolean(request.is_decoy_canvas)
    };
    sendToMyOwnIdm(request.url, request.referer, filename, extraMeta)
      .then((success) => {
        sendResponse({ status: success ? "ok" : "error" });
      })
      .catch((err) => {
        sendResponse({ status: "error", error: String(err) });
      });
    return true; // Keep channel open for async response
  } else if (request.action === "set-bypass-until") {
    const duration = request.durationMs || 2500;
    bypassUntilTimestamp = Date.now() + duration;
    sendResponse({ bypassActive: true, bypassUntil: bypassUntilTimestamp });
    return true;
  } else if (request.action === "get-interceptor-settings") {
    sendResponse({
      isInterceptorEnabled,
      autoCloseNewTab,
      filterByExtensions,
      minFileSizeKb,
      registeredExtensions
    });
    return true;
  } else if (request.action === "update-interceptor-settings") {
    if (request.isInterceptorEnabled !== undefined) isInterceptorEnabled = Boolean(request.isInterceptorEnabled);
    if (request.autoCloseNewTab !== undefined) autoCloseNewTab = Boolean(request.autoCloseNewTab);
    if (request.filterByExtensions !== undefined) filterByExtensions = Boolean(request.filterByExtensions);
    if (typeof request.minFileSizeKb === "number") minFileSizeKb = request.minFileSizeKb;
    if (Array.isArray(request.registeredExtensions)) registeredExtensions = request.registeredExtensions;

    if (typeof chrome !== "undefined" && chrome.storage && chrome.storage.local) {
      chrome.storage.local.set({
        isInterceptorEnabled,
        autoCloseNewTab,
        filterByExtensions,
        minFileSizeKb,
        registeredExtensions
      });
    }
    sendResponse({ status: "ok" });
    return true;
  } else if (request.action === "report-error") {
    reportExtensionError(request.error_type || "extension_error", request.message, request.details)
      .then((ok) => {
        sendResponse({ status: ok ? "ok" : "fallback" });
      });
    return true;
  }
  return true;
  });
}

const GLITCHTIP_PROJECT_ID = "28025";
const GLITCHTIP_KEY = "2e56fa98dc824baaa843dff62a18b3d2";
const GLITCHTIP_STORE_URL = `https://app.glitchtip.com/api/${GLITCHTIP_PROJECT_ID}/store/`;

// 5. Crash & Diagnostic Error Reporting
async function reportExtensionError(errorType, message, details = {}) {
  const payload = {
    error_type: errorType,
    message: String(message),
    browser: navigator.userAgent,
    details: details || {}
  };

  // Primary: Forward via Desktop App IPC HTTP Server
  try {
    const res = await fetch("http://127.0.0.1:18888/error-report", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    if (res.ok) {
      ExtensionLogger.info(`Diagnostik error ekstensi dilaporkan via Desktop IPC: ${errorType}`, "EXT:CRASH");
      return true;
    }
  } catch (ipcErr) {
    // Desktop app may be closed or offline
  }

  // Fallback 1: Try Native Messaging if available
  try {
    chrome.runtime.sendNativeMessage(
      NATIVE_HOST,
      { type: "error_report", ...payload },
      () => {
        if (chrome.runtime.lastError) {
          // Native host offline
        }
      }
    );
  } catch (e) {
    // ignore
  }

  // Fallback 2: Direct HTTP POST to GlitchTip when desktop is closed
  try {
    const eventId = (typeof crypto !== "undefined" && crypto.randomUUID
      ? crypto.randomUUID()
      : Math.random().toString(36).slice(2)).replace(/-/g, "");

    const sentryEvent = {
      event_id: eventId,
      timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, "Z"),
      platform: "javascript",
      level: "error",
      logger: "browser_extension",
      message: `Extension [${errorType}]: ${message}`,
      tags: {
        component: "browser_extension",
        error_type: errorType,
        browser: navigator.userAgent
      },
      extra: details || {}
    };

    const directRes = await fetch(GLITCHTIP_STORE_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Sentry-Auth": `Sentry sentry_version=7, sentry_client=myownidm-extension/1.0, sentry_key=${GLITCHTIP_KEY}`
      },
      body: JSON.stringify(sentryEvent)
    });
    if (directRes.ok) {
      ExtensionLogger.info(`Diagnostik error dilaporkan langsung ke GlitchTip: ${errorType}`, "EXT:CRASH");
      return true;
    }
  } catch (directErr) {
    // network error
  }

  return false;
}


// Global Exception Handlers for Extension Background Service Worker
if (typeof self !== "undefined" && typeof self.addEventListener === "function") {
  self.addEventListener("error", (event) => {
    reportExtensionError("uncaught_exception", event.message, {
      filename: event.filename,
      lineno: event.lineno,
      colno: event.colno
    });
  });

  self.addEventListener("unhandledrejection", (event) => {
    reportExtensionError("unhandled_rejection", String(event.reason));
  });
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = {
    DEFAULT_REGISTERED_EXTENSIONS,
    getFileExtension,
    isExtensionRegistered,
    closeMatchingNewTab,
    extractFilenameFromUrl,
    cleanFilename,
    resolveSmartFilename,
    isGenericTitle,
    get newlyOpenedTabs() { return newlyOpenedTabs; },
    get isInterceptorEnabled() { return isInterceptorEnabled; },
    set isInterceptorEnabled(val) { isInterceptorEnabled = val; },
    get bypassUntilTimestamp() { return bypassUntilTimestamp; },
    set bypassUntilTimestamp(val) { bypassUntilTimestamp = val; },
    get autoCloseNewTab() { return autoCloseNewTab; },
    set autoCloseNewTab(val) { autoCloseNewTab = val; },
    get filterByExtensions() { return filterByExtensions; },
    set filterByExtensions(val) { filterByExtensions = val; },
    get minFileSizeKb() { return minFileSizeKb; },
    set minFileSizeKb(val) { minFileSizeKb = val; },
    get registeredExtensions() { return registeredExtensions; },
    set registeredExtensions(val) { registeredExtensions = val; }
  };
}

