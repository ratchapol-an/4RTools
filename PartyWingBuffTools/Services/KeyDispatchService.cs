using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PartyWingBuffTools.Services;

public sealed class KeyDispatchService
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmChar = 0x0102;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public bool SendKey(int processId, string keyText)
    {
        if (!TryParseVirtualKey(keyText, out int virtualKey))
        {
            return false;
        }

        IntPtr handle = ResolvePrimaryWindowHandle(processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        int charCode = TryParseCharCode(keyText);
        if (charCode > 0)
        {
            return PostMessage(handle, WmChar, new IntPtr(charCode), IntPtr.Zero);
        }

        var keyParam = new IntPtr(virtualKey);
        bool down = PostMessage(handle, WmKeyDown, keyParam, IntPtr.Zero);
        bool up = PostMessage(handle, WmKeyUp, keyParam, IntPtr.Zero);
        return down && up;
    }

    private static int TryParseCharCode(string keyText)
    {
        string normalized = (keyText ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 1)
        {
            char c = normalized[0];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                return c;
            }
        }

        return normalized switch
        {
            "SPACE" => 0x20,
            _ => 0,
        };
    }

    private IntPtr ResolvePrimaryWindowHandle(int processId)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            IntPtr fallback = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == (uint)processId && IsWindowVisible(hWnd))
                {
                    fallback = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return fallback;
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
            "PGUP" => SetKey(0x21, out virtualKey),
            "PGDN" => SetKey(0x22, out virtualKey),
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
