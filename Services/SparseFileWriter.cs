using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mdm.Services;

public class SparseFileWriter : IDisposable
{
    private const uint FSCTL_SET_SPARSE = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private readonly FileStream _stream;
    private readonly object _lock = new();

    public string FilePath { get; }

    public SparseFileWriter(string filePath, long? totalBytes)
    {
        FilePath = filePath;
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Mark file as sparse on NTFS
            DeviceIoControl(_stream.SafeFileHandle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }

        if (totalBytes is > 0)
        {
            _stream.SetLength(totalBytes.Value);
        }

        Logger.Debug($"SparseFileWriter diinisialisasi untuk '{filePath}' (Pre-alokasi: {(totalBytes.HasValue ? totalBytes.Value + " bytes" : "dinamis")})", "WRITER");
    }

    public void WriteAt(long offset, byte[] buffer, int count)
    {
        lock (_lock)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.Write(buffer, 0, count);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            _stream.Flush();
        }
        Logger.Debug($"SparseFileWriter flush selesai: '{FilePath}'", "WRITER");
    }

    public void Dispose()
    {
        _stream.Dispose();
        Logger.Debug($"SparseFileWriter ditutup dan sumber daya dilepas: '{FilePath}'", "WRITER");
        GC.SuppressFinalize(this);
    }
}

