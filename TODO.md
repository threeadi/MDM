# Roadmap 1:1 Parity: My Own IDM -> MDM (.NET 10 Native)

Dokumen ini adalah rencana kerja komprehensif untuk mencapai **100% kesetaraan fitur (Feature Parity)** dan **kesetaraan tampilan (UI Parity)** antara **My Own IDM** (arsitektur Tauri v2 / Rust / Svelte) dan **MDM (Modern Download Manager)** yang dibangun di atas **.NET 10 (`net10.0-windows`)** dengan WPF Native.

---

## 1. Status Checklist Rencana Kerja

### Fase 1: Core Download Engine & Advanced Networking
- [x] **1.1. Multi-Thread Range Engine**:
  - [x] HTTP `Range: bytes=start-end` chunking (1–32 koneksi).
  - [x] Auto-detection dukungan Range via HEAD/GET probe.
  - [x] Ekstraksi nama berkas otomatis dari header `Content-Disposition`.
- [x] **1.2. Sparse File Allocation (Win32 API)**:
  - [x] Implementasi `FSCTL_SET_SPARSE` via `DeviceIoControl` P/Invoke pada `SparseFileWriter.cs` untuk alokasi instan tanpa jeda disk.
- [x] **1.3. Dynamic Range Splitting (Chunk Balancer)**:
  - [x] Implementasi algoritma pemecahan segmen dinamis: saat ada worker yang selesai lebih cepat sementara worker lain masih tertinggal, segmen yang tersisa dibagi dua secara otomatis untuk memaksimalkan utilitas *bandwidth*.
- [x] **1.4. Token Bucket Rate Limiter**:
  - [x] Pembatas kecepatan global (Token Bucket pacing thread-safe dengan burst handling).
  - [x] Pengaturan dinamis saat unduhan sedang aktif berjalan tanpa perlu *restart*.
- [x] **1.5. Refresh Download Address (Expiring URL Recovery)**:
  - [x] Penanganan URL kedaluwarsa (`HTTP 401`, `HTTP 403`, `HTTP 410`, link Google Drive / YouTube yang habis masa berlakunya).
  - [x] Workflow pembaruan URL dari browser tanpa menghapus byte yang sudah berhasil diunduh sebelumnya (`RefreshLinkDialog`).
- [x] **1.6. Duplicate Download Detection**:
  - [x] Deteksi apakah berkas dengan nama yang sama sudah ada di disk/folder tujuan.
  - [x] Dialog keputusan duplikasi: *Ganti Nama Otomatis* (`berkas (1).zip`), *Timpa Berkas*, *Buka Berkas*, atau *Batal*.
- [ ] **1.7. Video Stream & HLS Engine**:
  - [ ] Runner `yt-dlp` / `ffmpeg` untuk YouTube, Instagram, dsb.
  - [ ] Parser M3U8 HLS dengan dekripsi AES-128 CBC.
- [x] **1.8. Telegram MTProto Integration (Telegram Media Grabber Pro)**:
  - [x] Client MTProto resmi via library `WTelegramClient` tanpa batasan ukuran bot (mendukung file hingga 2 GB / 4 GB).
  - [x] Login sesi pengguna: Nomor HP -> Kode OTP -> Verifikasi 2FA Password (`TelegramAuthDialog.xaml`).
  - [x] Pemindaian media chat/channel flexible (`@username`, link `t.me/...`, ID channel, judul obrolan).
  - [x] Filter tipe berkas media: Semua, Video, Dokumen, Musik, Foto.
  - [x] Workspace dedicated `TelegramWorkspaceWindow.xaml` (Telegram Media Grabber Pro).
  - [x] Unduh berkas media multi-pilihan langsung diintegrasikan ke antrean engine download MDM (`tg://chat/{chatId}/msg/{messageId}`).

---

### Fase 2: Antarmuka Pengguna Utama (Main Window UI 1:1)
- [x] **2.1. Dark Theme Utilitarian (Stitch Kinetic Telemetry)**:
  - [x] Palet Obsidian `#12151B`, toolbar `#181B22`, separator 1px `#242832`, aksen Electric Cyan `#00E5FF`.
  - [x] Native Windows Dark Titlebar via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`.
- [x] **2.2. Two-Tier Top Toolbar**:
  - [x] Tombol aksi: `+ Tambah URL`, `▶ Lanjut`, `⏸ Jeda`, `▶ Semua` (Lanjutkan Semua), `⏸ Semua` (Jeda Semua), `✕ Hapus`, `📁 Berkas`, `📂 Folder`, `⚙ Pengaturan`.
  - [x] Kolom pencarian cepat (*Quick Search*) dengan filter reaktif instan.
- [x] **2.3. Multi-Category Navigation Sidebar**:
  - [x] Kategori Status: `Semua Unduhan`, `Sedang Berjalan`, `Selesai`, `Dijeda`, `Gagal` dengan badge counter dinamis.
  - [x] Kategori Tipe Berkas dengan live badge:
    - 📦 *Kompresi* (`.zip`, `.rar`, `.7z`, `.tar`, `.gz`, `.iso`)
    - 📄 *Dokumen* (`.pdf`, `.docx`, `.xlsx`, `.txt`)
    - 🎬 *Video* (`.mp4`, `.mkv`, `.webm`, `.avi`)
    - 🎵 *Musik* (`.mp3`, `.flac`, `.wav`, `.aac`)
    - ⚙ *Program* (`.exe`, `.msi`, `.apk`)
- [x] **2.4. Hardware-Virtualized DataGrid (60 FPS)**:
  - [x] Kolom: Nama Berkas, Ukuran, Progres Bar + Persentase, Kecepatan, Sisa Waktu (ETA), Status Badge.
  - [x] Klik ganda: Membuka berkas jika selesai, membuka jendela transfer jika aktif.
  - [x] Context Menu klik kanan lengkap:
    - *Buka Berkas*, *Buka Folder*
    - *Lanjutkan*, *Jeda*, *Perbarui Tautan Unduhan...*
    - *Properti & Checksum...*
    - *Hapus*
- [x] **2.5. Telemetry & Speed Monitoring**:
  - [x] Telemetry bar di status bawah penunjuk total berkas aktif, total selesai, dan kecepatan global real-time.

---

### Fase 3: Dialog & Modal Tambahan
- [x] **3.1. Dialog Tambah Unduhan Baru (`AddDownloadDialog.xaml`)**:
  - [x] Input URL, Folder Tujuan (Browse native `OpenFolderDialog`), Jumlah Thread (1–32), Nama Berkas.
  - [x] Dropdown Kategori & Checkbox "Unduh Sekarang" vs "Antrekan Saja".
  - [x] Estimasi ukuran berkas instan saat URL diketik/ditempel (*Auto Probe HEAD/GET*).
- [x] **3.2. Jendela Transfer Mengapung (`TransferWindow.xaml`) In-Place Lifecycle**:
  - [x] Bilah progres total, persentase besar, data terunduh, kecepatan, ETA.
  - [x] **Discrete Multi-Thread Segment Visualizer**: Menampilkan status tiap segmen thread (Cyan aktif, Emerald selesai, Slate pending).
  - [x] Transisi status in-place: saat transfer selesai bertransformasi ke tampilan sukses (*Buka Berkas*, *Buka Folder*, *Tutup*) dengan aksen Emerald.
  - [x] Tampilan kegagalan dengan banner error dan tombol *Perbarui Tautan...*.
  - [x] Tombol Jeda/Lanjutkan langsung di jendela transfer.
- [x] **3.3. Dialog Perbarui Tautan Unduhan (`RefreshLinkDialog.xaml`)**:
  - [x] Menampilkan data lama, tombol buka halaman asal di browser (*Referer*).
  - [x] Validasi tautan baru & pengecekan kecocokan ukuran serta dukungan Range sebelum diterapkan.
- [x] **3.4. Dialog Properti & Checksum (`PropertiesDialog.xaml`)**:
  - [x] Rincian tugas: URL lengkap, Referer, Tanggal dibuat, total potongan, lokasi penyimpanan.
  - [x] Kalkulator Checksum terintegrasi: Perhitungan hash **MD5**, **SHA-1**, dan **SHA-256** secara asinkron.
  - [x] Fitur komparasi hash: Kotak pencocokan hash dengan verifikasi status (COCOK / TIDAK COCOK).
- [x] **3.5. Dialog Pengaturan Aplikasi (`SettingsDialog.xaml`)**:
  - [x] Folder unduhan default & opsi auto-subfolder berdasarkan kategori.
  - [x] Batas koneksi thread default (1–32).
  - [x] Pengaturan batas kecepatan unduh global (KB/s).
  - [x] Jalankan saat Windows startup (Registry `HKCU\Run`).
  - [x] Opsi minimize ke System Tray saat tombol tutup ditekan.

---

### Fase 4: Integrasi OS Windows & Browser Extension
- [x] **4.1. Background IPC Server**:
  - [x] HTTP Server port `18888` (CORS + Chrome Private Network Access PNA).
  - [x] Windows Named Pipe `\\.\pipe\my_own_idm_ipc`.
  - [x] Penangkapan request unduh otomatis dari ekstensi Chrome/Edge/Firefox.
- [x] **4.2. Windows System Tray Integration (`TrayIconService.cs`)**:
  - [x] Ikon Tray di taskbar Windows dengan menu klik kanan: *Buka MDM*, *Tambah Unduhan Baru*, *Lanjutkan Semua*, *Jeda Semua*, *Keluar*.
  - [x] Klik kiri untuk mengembalikan jendela utama.
  - [x] Minimize ke tray saat tombol *close* (X) ditekan.
  - [x] Notifikasi native Windows (*Balloon Notification*) saat unduhan selesai.
- [x] **4.3. Native Windows Startup**:
  - [x] Pendaftaran ke Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

---

### Fase 5: Logging, Diagnostik & Error Reporting
- [x] **5.1. Hybrid Logging (`Logger.cs`)**:
  - [x] In-memory ring buffer (1.000 baris log terakhir) untuk inspeksi live tanpa jeda disk.
  - [x] File log harian di `%APPDATA%\MDM\logs\mdm_yyyyMMdd.log` dengan rotasi harian otomatis.
  - [x] Sanitasi token query URL (`token`, `auth`, `api_key`, `secret`, `signature`) dan masking direktori pengguna Windows (`C:\Users\***\`).
- [x] **5.2. GlitchTip / Sentry Crash Reporting (`CrashReporter.cs`)**:
  - [x] Hook global `AppDomain.UnhandledException` dan `TaskScheduler.UnobservedTaskException`.
  - [x] Integrasi endpoint GlitchTip (`https://app.glitchtip.com/api/28025/store/`) dengan format Sentry SDK v7.
  - [x] Lampiran 30 baris log diagnostik terakhir yang telah disanitasi.
