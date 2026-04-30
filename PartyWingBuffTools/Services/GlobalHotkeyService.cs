using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PartyWingBuffTools.Services;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int PollDelayMs = 25;

    private int _hotkeyVk;
    private bool _enabled;
    private bool _wasDown;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public event Action? HotkeyPressed;

    public bool TrySetHotkeyToken(string token)
    {
        if (!TryParseTokenToVk(token, out int vk))
        {
            return false;
        }

        _hotkeyVk = vk;
        return true;
    }

    public void Enable()
    {
        if (_enabled)
        {
            return;
        }

        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
        _enabled = true;
    }

    public void Disable()
    {
        if (!_enabled)
        {
            return;
        }

        _pollCts?.Cancel();
        try
        {
            _pollTask?.Wait(250);
        }
        catch
        {
            // ignore shutdown races
        }

        _pollTask = null;
        _pollCts?.Dispose();
        _pollCts = null;
        _wasDown = false;
        _enabled = false;
    }

    public void Dispose()
    {
        Disable();
        GC.SuppressFinalize(this);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_hotkeyVk != 0)
            {
                bool isDown = (GetAsyncKeyState(_hotkeyVk) & 0x8000) != 0;
                if (isDown && !_wasDown)
                {
                    try
                    {
                        HotkeyPressed?.Invoke();
                    }
                    catch
                    {
                        // Do not stop polling on callback failure.
                    }
                }

                _wasDown = isDown;
            }

            try
            {
                await Task.Delay(PollDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static bool TryParseTokenToVk(string token, out int vk)
    {
        vk = 0;
        string normalized = (token ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length == 1)
        {
            char c = normalized[0];
            if (c is >= 'A' and <= 'Z')
            {
                vk = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        if (normalized.StartsWith("F", StringComparison.Ordinal) &&
            int.TryParse(normalized[1..], out int fNumber) &&
            fNumber is >= 1 and <= 24)
        {
            vk = 0x70 + (fNumber - 1);
            return true;
        }

        return normalized switch
        {
            "ENTER" => Map(0x0D, out vk),
            "TAB" => Map(0x09, out vk),
            "SPACE" => Map(0x20, out vk),
            "UP" => Map(0x26, out vk),
            "DOWN" => Map(0x28, out vk),
            "LEFT" => Map(0x25, out vk),
            "RIGHT" => Map(0x27, out vk),
            "PGUP" => Map(0x21, out vk),
            "PGDN" => Map(0x22, out vk),
            "ESC" => Map(0x1B, out vk),
            _ => false,
        };
    }

    private static bool Map(int value, out int vk)
    {
        vk = value;
        return true;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
