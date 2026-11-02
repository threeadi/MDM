document.addEventListener("DOMContentLoaded", () => {
  const DEFAULT_EXTENSIONS = [
    "zip", "rar", "7z", "iso", "tar", "gz", "exe", "msi", "deb", "jar",
    "apk", "bin", "mp3", "aac", "pdf", "mp4", "3gp", "avi", "mkv", "wav", "mpeg", "srt"
  ];

  const optAutoCapture = document.getElementById("opt-auto-capture");
  const chipsContainer = document.getElementById("chips-container");
  const chipInput = document.getElementById("chip-input");
  const optIgnoredPatterns = document.getElementById("opt-ignored-patterns");
  const optMinSize = document.getElementById("opt-min-size");
  const optBypassShortcut = document.getElementById("opt-bypass-shortcut");
  const optShowPopups = document.getElementById("opt-show-popups");
  const btnReset = document.getElementById("btn-reset");
  const btnSave = document.getElementById("btn-save");
  const toastMsg = document.getElementById("toast-msg");

  let currentExtensions = [...DEFAULT_EXTENSIONS];

  function renderChips() {
    // Remove existing chips except the input element
    const existingChips = chipsContainer.querySelectorAll(".chip");
    existingChips.forEach((c) => c.remove());

    currentExtensions.forEach((ext) => {
      const chip = document.createElement("span");
      chip.className = "chip";
      chip.textContent = ext;

      const removeBtn = document.createElement("button");
      removeBtn.className = "chip-remove";
      removeBtn.type = "button";
      removeBtn.textContent = "×";
      removeBtn.addEventListener("click", () => {
        currentExtensions = currentExtensions.filter((e) => e !== ext);
        renderChips();
      });

      chip.appendChild(removeBtn);
      chipsContainer.insertBefore(chip, chipInput);
    });
  }

  // Load existing settings
  chrome.runtime.sendMessage({ action: "get-interceptor-settings" }, (settings) => {
    if (settings) {
      if (settings.isInterceptorEnabled !== undefined) {
        optAutoCapture.checked = Boolean(settings.isInterceptorEnabled);
      }
      if (Array.isArray(settings.registeredExtensions) && settings.registeredExtensions.length > 0) {
        currentExtensions = [...settings.registeredExtensions];
      }
      if (typeof settings.minFileSizeKb === "number") {
        optMinSize.value = String(settings.minFileSizeKb);
      }
    }
    renderChips();
  });

  // Load additional storage preferences (ignoredPatterns, bypassKey, showPopups)
  if (chrome.storage && chrome.storage.local) {
    chrome.storage.local.get(["ignoredPatterns", "bypassShortcut", "showPopups"], (res) => {
      if (res) {
        if (res.ignoredPatterns) optIgnoredPatterns.value = res.ignoredPatterns;
        if (res.bypassShortcut) optBypassShortcut.value = res.bypassShortcut;
        if (res.showPopups !== undefined) optShowPopups.checked = Boolean(res.showPopups);
      }
    });
  }

  // Chip input key listener
  chipInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === "," || e.key === " ") {
      e.preventDefault();
      const val = chipInput.value.trim().toLowerCase().replace(/^\./, "");
      if (val && !currentExtensions.includes(val)) {
        currentExtensions.push(val);
        chipInput.value = "";
        renderChips();
      }
    }
  });

  // Save changes
  btnSave.addEventListener("click", () => {
    const isInterceptorEnabled = optAutoCapture.checked;
    const minFileSizeKb = parseInt(optMinSize.value, 10) || 0;
    const ignoredPatterns = optIgnoredPatterns.value;
    const bypassShortcut = optBypassShortcut.value.trim() || "Alt";
    const showPopups = optShowPopups.checked;

    chrome.runtime.sendMessage({
      action: "update-interceptor-settings",
      isInterceptorEnabled,
      minFileSizeKb,
      registeredExtensions: currentExtensions
    });

    if (chrome.storage && chrome.storage.local) {
      chrome.storage.local.set({
        ignoredPatterns,
        bypassShortcut,
        showPopups
      });
    }

    if (toastMsg) {
      toastMsg.style.display = "inline-flex";
      setTimeout(() => {
        toastMsg.style.display = "none";
      }, 2500);
    }
  });

  // Reset defaults
  btnReset.addEventListener("click", () => {
    currentExtensions = [...DEFAULT_EXTENSIONS];
    optAutoCapture.checked = true;
    optMinSize.value = "0";
    optIgnoredPatterns.value = "";
    optBypassShortcut.value = "Alt";
    optShowPopups.checked = true;
    renderChips();
  });
});
