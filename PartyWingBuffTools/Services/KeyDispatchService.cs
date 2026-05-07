using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PartyWingBuffTools.Services;

public sealed class KeyDispatchService
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmChar = 0x0102;
    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfAbsolute = 0x8000;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const int DefaultFocusSettleDelayMs = 35;

    public int FocusSettleDelayMs { get; set; } = DefaultFocusSettleDelayMs;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    public bool SendMouseClickNormalized(int processId, double normalizedX, double normalizedY)
    {
        IntPtr handle = ResolvePrimaryWindowHandle(processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (!GetClientRect(handle, out RECT rect))
        {
            return false;
        }

        int width = Math.Max(0, rect.Right - rect.Left);
        int height = Math.Max(0, rect.Bottom - rect.Top);
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        double safeX = Math.Clamp(normalizedX, 0.0, 1.0);
        double safeY = Math.Clamp(normalizedY, 0.0, 1.0);
        int clientX = (int)Math.Round((width - 1) * safeX);
        int clientY = (int)Math.Round((height - 1) * safeY);
        var screenPoint = new POINT { X = clientX, Y = clientY };
        if (!ClientToScreen(handle, ref screenPoint))
        {
            return false;
        }

        int screenW = Math.Max(1, GetSystemMetrics(SmCxscreen));
        int screenH = Math.Max(1, GetSystemMetrics(SmCyscreen));
        int absoluteX = (int)Math.Round(screenPoint.X * 65535.0 / Math.Max(1, screenW - 1));
        int absoluteY = (int)Math.Round(screenPoint.Y * 65535.0 / Math.Max(1, screenH - 1));

        _ = SetForegroundWindow(handle);
        ForceForegroundWindow(handle);
        int focusDelay = Math.Max(0, FocusSettleDelayMs);
        if (focusDelay > 0)
        {
            Thread.Sleep(focusDelay);
        }

        INPUT[] inputs =
        {
            new()
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absoluteX,
                        dy = absoluteY,
                        dwFlags = MouseeventfMove | MouseeventfAbsolute,
                    },
                },
            },
        };

        uint moved = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (moved != inputs.Length)
        {
            return false;
        }

        INPUT[] down =
        {
            new()
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = MouseeventfLeftdown,
                    },
                },
            },
        };
        uint downSent = SendInput(1, down, Marshal.SizeOf<INPUT>());
        if (downSent != 1)
        {
            return false;
        }

        Thread.Sleep(20);

        INPUT[] up =
        {
            new()
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = MouseeventfLeftup,
                    },
                },
            },
        };
        uint upSent = SendInput(1, up, Marshal.SizeOf<INPUT>());
        return upSent == 1;
    }

    private void ForceForegroundWindow(IntPtr handle)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == handle)
        {
            return;
        }

        uint currentThreadId = GetCurrentThreadId();
        uint foregroundThreadId = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        uint targetThreadId = GetWindowThreadProcessId(handle, out _);

        try
        {
            if (foregroundThreadId != 0)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0)
            {
                _ = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            _ = BringWindowToTop(handle);
            _ = SetForegroundWindow(handle);
            _ = SetFocus(handle);
        }
        finally
        {
            if (targetThreadId != 0)
            {
                _ = AttachThreadInput(currentThreadId, targetThreadId, false);
            }

            if (foregroundThreadId != 0)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    private static int TryParseCharCode(string keyText)
    {
        string normalized = (keyText ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 1)
        {
            char c = normalized[0];
            if (c >= 'A' && c <= 'Z')
            {
                return c;
            }
        }

        return normalized switch
        {
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
