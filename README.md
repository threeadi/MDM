# MDM — Modern Download Manager (.NET 10 Native)

MDM adalah aplikasi Download Manager berkecepatan tinggi yang dibangun di atas **.NET 10 (net10.0-windows)** dengan antarmuka **WPF Native** bertenaga akselerasi hardware DirectX.

Aplikasi ini dirancang untuk mereplikasi kecepatan responsif dan efisiensi memori **IDM asli (Win32/C++)**, tanpa menggunakan WebView2, tanpa Chromium, dan tanpa visual slop yang membebani CPU.

---

## 1. Arsitektur & Fitur Utama

- **Runtime**: .NET 10 SDK (`10.0.403`), C# 13, Windows Desktop SDK (`net10.0-windows`).
- **Antarmuka Pengguna (UI)**:
  - **Dark Mode Utilitarian**: Menggunakan palet Obsidian `#12151B`, toolbar flat `#181B22`, dan aksen Electric Cyan `#00E5FF`.
  - **Akselerasi DirectX & Virtualization**: Komponen `DataGrid` menggunakan `VirtualizingPanel.VirtualizationMode="Recycling"` sehingga pembaruan progres dan kecepatan transfer berkali-kali per detik tetap berjalan di **60 FPS** mulus tanpa lag.
  - **Native Dark Titlebar**: Terintegrasi langsung dengan kernel Windows Desktop Window Manager (`DwmSetWindowAttribute` - `DWMWA_USE_IMMERSIVE_DARK_MODE`).
  - **Toolbar Aksi**: `Tambah URL`, `▶ Lanjut`, `⏸ Jeda`, `✕ Hapus`, `📁 Berkas`, `📂 Folder`, serta Quick Search.
  - **Kategori Status**: `Semua Unduhan`, `Sedang Berjalan`, `Selesai`, `Dijeda`, `Gagal`.
  - **In-Flight Segment Monitor**: Jendela pop-up transfer dengan bilah monitor multi-thread dinamis.
- **Engine Unduhan**:
  - **Multi-thread Range Chunking**: Membagi berkas menjadi 1–32 koneksi serentak via `SocketsHttpHandler` dan `HttpClient`.
  - **Zero-Allocation Sparse Files**: Menggunakan Windows Win32 API `FSCTL_SET_SPARSE` via `DeviceIoControl` sehingga berkas berukuran gigabyte dialokasikan seketika tanpa jeda I/O disk.
  - **SQLite Database**: Penyimpanan persistent di `%APPDATA%\MDM\mdm.db` via `Microsoft.Data.Sqlite 10.0`.
- **Integrasi Ekstensi Browser**:
  - Berjalan di background mendengarkan HTTP IPC port `18888` dan Windows Named Pipe `\\.\pipe\my_own_idm_ipc`.
  - Saat menekan unduh di browser, dialog unduh MDM langsung muncul otomatis dengan URL dan nama berkas terisi.

---

## 2. Cara Menjalankan Aplikasi

Jalankan perintah berikut di PowerShell:

```powershell
# Jalankan aplikasi MDM
dotnet run --project D:\Development\MDM\Mdm.csproj

# Atau jalankan berkas executable hasil kompilasi:
D:\Development\MDM\bin\Debug\net10.0-windows\Mdm.exe
```

## 3. Menjalankan Unit Tests

```powershell
dotnet test D:\Development\MDM.Tests\Mdm.Tests.csproj
```

