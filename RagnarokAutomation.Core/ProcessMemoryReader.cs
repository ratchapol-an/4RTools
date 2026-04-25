using System.Runtime.InteropServices;
using System.Text;

namespace RagnarokAutomation.Core;

public sealed class ProcessMemoryReader
{
    [Flags]
    private enum ProcessAccess
    {
        VmOperation = 0x0008,
        VmRead = 0x0010,
        VmWrite = 0x0020,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, int inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ReadProcessMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();

    public string ReadAsciiString(int processId, int address, int maxBytes = 40)
    {
        IntPtr handle = OpenProcess((uint)(ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite), 1, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            byte[] buffer = new byte[maxBytes];
            _ = ReadProcessMemory(handle, new IntPtr(address), buffer, (uint)buffer.Length, out _);
            int length = Array.IndexOf(buffer, (byte)0);
            if (length < 0)
            {
                length = buffer.Length;
            }

            return Encoding.Default.GetString(buffer, 0, length).Trim();
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public int TryReadPointer32(int processId, int address)
    {
        IntPtr handle = OpenProcess((uint)(ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite), 1, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            byte[] buffer = new byte[4];
            _ = ReadProcessMemory(handle, new IntPtr(address), buffer, (uint)buffer.Length, out _);
            return BitConverter.ToInt32(buffer, 0);
        }
        catch
        {
            return 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public uint TryReadUInt32(int processId, int address)
    {
        IntPtr handle = OpenProcess((uint)(ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite), 1, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            byte[] buffer = new byte[4];
            _ = ReadProcessMemory(handle, new IntPtr(address), buffer, (uint)buffer.Length, out _);
            return BitConverter.ToUInt32(buffer, 0);
        }
        catch
        {
            return 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public byte[] ReadBytes(int processId, int address, int count)
    {
        IntPtr handle = OpenProcess((uint)(ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite), 1, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return Array.Empty<byte>();
        }

        try
        {
            byte[] buffer = new byte[Math.Max(1, count)];
            _ = ReadProcessMemory(handle, new IntPtr(address), buffer, (uint)buffer.Length, out _);
            return buffer;
        }
        catch
        {
            return Array.Empty<byte>();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public string GetOpenProcessDiagnostic(int processId)
    {
        const uint access = (uint)(ProcessAccess.VmOperation | ProcessAccess.VmRead | ProcessAccess.VmWrite);
        IntPtr handle = OpenProcess(access, 1, (uint)processId);
        uint err = GetLastError();
        if (handle != IntPtr.Zero)
        {
            _ = CloseHandle(handle);
        }

        return $"OpenProcessHandle=0x{handle.ToInt64():X} LastError={err}";
    }
}
