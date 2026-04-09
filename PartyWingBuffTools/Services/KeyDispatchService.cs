using System;
using System.Runtime.InteropServices;

namespace PartyWingBuffTools.Services;

public sealed class KeyDispatchService
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public bool SendKey(int processId, string keyText)
    {
        if (!TryParseVirtualKey(keyText, out int virtualKey))
        {
            return false;
        }

        IntPtr handle = ResolveMainWindowHandle(processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var keyParam = new IntPtr(virtualKey);
        PostMessage(handle, WmKeyDown, keyParam, IntPtr.Zero);
        PostMessage(handle, WmKeyUp, keyParam, IntPtr.Zero);
        return true;
    }

    private IntPtr ResolveMainWindowHandle(int processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById(processId).MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private bool TryParseVirtualKey(string keyText, out int virtualKey)
    {
        virtualKey = 0;
        string normalized = keyText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length == 1 && normalized[0] >= 'A' && normalized[0] <= 'Z')
        {
            virtualKey = normalized[0];
            return true;
        }

        if (normalized.Length == 1 && normalized[0] >= '0' && normalized[0] <= '9')
        {
            virtualKey = normalized[0];
            return true;
        }

        if (normalized.StartsWith("F") && int.TryParse(normalized.AsSpan(1), out int fKey) && fKey is >= 1 and <= 24)
        {
            virtualKey = 0x6F + fKey;
            return true;
        }

        return normalized switch
        {
            "ENTER" => SetKey(0x0D, out virtualKey),
            "TAB" => SetKey(0x09, out virtualKey),
            "SPACE" => SetKey(0x20, out virtualKey),
            "UP" => SetKey(0x26, out virtualKey),
            "DOWN" => SetKey(0x28, out virtualKey),
            "LEFT" => SetKey(0x25, out virtualKey),
            "RIGHT" => SetKey(0x27, out virtualKey),
            "ESC" => SetKey(0x1B, out virtualKey),
            _ => false,
        };
    }

    private bool SetKey(int value, out int output)
    {
        output = value;
        return true;
    }
}
