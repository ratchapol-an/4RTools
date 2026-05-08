using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PartyWingBuffTools.Core.Models;
using RagnarokAutomation.Core;

namespace PartyWingBuffTools.Services;

public sealed class RagnarokProcessService
{
    private readonly ProcessMemoryReader _memoryReader = new();

    public readonly record struct HpSnapshot(uint CurrentHp, uint MaxHp)
    {
        public bool HasValue => MaxHp > 0 || CurrentHp > 0;
        public int Percent => MaxHp > 0 ? (int)Math.Clamp(Math.Round(CurrentHp * 100.0 / MaxHp), 0, 100) : 0;
    }

    public IReadOnlyList<RagnarokProcessInfo> DiscoverProcesses(
        IReadOnlyCollection<SupportedServerEntry> supportedServers,
        int fallbackHpAddress,
        int fallbackNameAddress)
    {
        var results = new List<RagnarokProcessInfo>();
        var serverByName = supportedServers
            .Where(s => !string.IsNullOrWhiteSpace(s.ProcessName))
            .GroupBy(s => s.ProcessName.Trim().Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }
                if (!serverByName.TryGetValue(process.ProcessName, out List<SupportedServerEntry>? serverCandidates))
                {
                    continue;
                }

                int moduleBase = 0;
                try
                {
                    moduleBase = process.MainModule?.BaseAddress.ToInt32() ?? 0;
                }
                catch
                {
                    moduleBase = 0;
                }

                // Match the same way 4RTools does: pick first address pair where HP > 0.
                SupportedServerEntry? matched = null;
                foreach (SupportedServerEntry candidate in serverCandidates)
                {
                    uint hp = TryReadHp(process.Id, candidate.HpAddress, moduleBase);
                    if (hp > 0)
                    {
                        matched = candidate;
                        break;
                    }
                }

                int effectiveNameAddress = matched?.NameAddress ?? fallbackNameAddress;

                string characterName = CharacterNameMemoryResolver.TryReadCharacterName(
                    _memoryReader,
                    process.Id,
                    effectiveNameAddress,
                    moduleBase);
                if (string.IsNullOrWhiteSpace(characterName) &&
                    !process.MainWindowTitle.Contains("Ragnarok", StringComparison.OrdinalIgnoreCase))
                {
                    characterName = process.MainWindowTitle;
                }
                string prettyName = string.IsNullOrWhiteSpace(characterName) ? "(Unknown)" : characterName;
                string displayName = $"{prettyName} | {process.ProcessName}.exe - {process.Id}";

                results.Add(new RagnarokProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    DisplayName = displayName,
                    CharacterName = prettyName,
                });
            }
            catch
            {
                // process may exit during iteration
            }
        }

        return results.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string BuildDebugReport(
        int processId,
        string processName,
        IReadOnlyCollection<SupportedServerEntry> supportedServers,
        int fallbackHpAddress,
        int fallbackNameAddress)
    {
        var lines = new List<string>
        {
            $"ProcessId={processId}",
            $"ProcessName={processName}",
            $"FallbackHp=0x{fallbackHpAddress:X8} ({fallbackHpAddress})",
            $"FallbackName=0x{fallbackNameAddress:X8} ({fallbackNameAddress})",
            _memoryReader.GetOpenProcessDiagnostic(processId),
        };

        List<SupportedServerEntry> matches = supportedServers
            .Where(s => string.Equals(s.ProcessName?.Trim().Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase), processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        lines.Add($"MatchedServerEntries={matches.Count}");

        int idx = 0;
        foreach (SupportedServerEntry e in matches)
        {
            uint hp = _memoryReader.TryReadUInt32(processId, e.HpAddress);
            string direct = _memoryReader.ReadAsciiString(processId, e.NameAddress);
            int ptr = _memoryReader.TryReadPointer32(processId, e.NameAddress);
            string viaPtr = ptr > 0 ? _memoryReader.ReadAsciiString(processId, ptr) : string.Empty;
            byte[] raw = _memoryReader.ReadBytes(processId, e.NameAddress, 32);
            string rawHex = BitConverter.ToString(raw).Replace("-", " ");

            lines.Add($"Entry[{idx}] Hp=0x{e.HpAddress:X8} Name=0x{e.NameAddress:X8}");
            lines.Add($"Entry[{idx}] hpValue={hp}");
            lines.Add($"Entry[{idx}] direct='{direct}'");
            lines.Add($"Entry[{idx}] ptr=0x{ptr:X8} ({ptr})");
            lines.Add($"Entry[{idx}] viaPtr='{viaPtr}'");
            lines.Add($"Entry[{idx}] raw={rawHex}");
            idx++;
        }

        uint fHp = _memoryReader.TryReadUInt32(processId, fallbackHpAddress);
        string fDirect = _memoryReader.ReadAsciiString(processId, fallbackNameAddress);
        int fPtr = _memoryReader.TryReadPointer32(processId, fallbackNameAddress);
        string fViaPtr = fPtr > 0 ? _memoryReader.ReadAsciiString(processId, fPtr) : string.Empty;
        byte[] fRaw = _memoryReader.ReadBytes(processId, fallbackNameAddress, 32);
        string fRawHex = BitConverter.ToString(fRaw).Replace("-", " ");

        lines.Add("FallbackRead:");
        lines.Add($"Fallback hpValue={fHp}");
        lines.Add($"Fallback direct='{fDirect}'");
        lines.Add($"Fallback ptr=0x{fPtr:X8} ({fPtr})");
        lines.Add($"Fallback viaPtr='{fViaPtr}'");
        lines.Add($"Fallback raw={fRawHex}");

        return string.Join(Environment.NewLine, lines);
    }

    public uint TryReadHpValue(
        int processId,
        IReadOnlyCollection<SupportedServerEntry> supportedServers,
        int fallbackHpAddress)
    {
        HpSnapshot snapshot = TryReadHpSnapshot(processId, supportedServers, fallbackHpAddress);
        if (!snapshot.HasValue)
        {
            return 0;
        }

        return snapshot.MaxHp > 0 ? (uint)snapshot.Percent : snapshot.CurrentHp;
    }

    public HpSnapshot TryReadHpSnapshot(
        int processId,
        IReadOnlyCollection<SupportedServerEntry> supportedServers,
        int fallbackHpAddress)
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return default;
            }

            string processName = process.ProcessName;
            int moduleBase = 0;
            try
            {
                moduleBase = process.MainModule?.BaseAddress.ToInt32() ?? 0;
            }
            catch
            {
                moduleBase = 0;
            }

            List<SupportedServerEntry> matches = supportedServers
                .Where(s => string.Equals(
                    s.ProcessName?.Trim().Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase),
                    processName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (SupportedServerEntry match in matches)
            {
                HpSnapshot byServer = TryReadHpSnapshotByBaseAddress(processId, match.HpAddress, moduleBase);
                if (byServer.HasValue)
                {
                    return byServer;
                }
            }

            return TryReadHpSnapshotByBaseAddress(processId, fallbackHpAddress, moduleBase);
        }
        catch
        {
            return default;
        }
    }

    private HpSnapshot TryReadHpSnapshotByBaseAddress(int processId, int hpBaseAddress, int moduleBase)
    {
        uint current = TryReadUInt32WithOptionalModuleBase(processId, hpBaseAddress, moduleBase);
        uint max = TryReadUInt32WithOptionalModuleBase(processId, hpBaseAddress + 4, moduleBase);
        if (max > 0 || current > 0)
        {
            return new HpSnapshot(current, max);
        }

        return default;
    }

    private uint TryReadUInt32WithOptionalModuleBase(int processId, int address, int moduleBase)
    {
        uint value = _memoryReader.TryReadUInt32(processId, address);
        if (value > 0)
        {
            return value;
        }

        if (moduleBase > 0)
        {
            long candidate = (long)moduleBase + address;
            if (candidate is > 0 and <= int.MaxValue)
            {
                value = _memoryReader.TryReadUInt32(processId, (int)candidate);
                if (value > 0)
                {
                    return value;
                }
            }
        }

        return value;
    }

    private uint TryReadHp(int processId, int hpAddress, int moduleBase)
    {
        uint hp = _memoryReader.TryReadUInt32(processId, hpAddress);
        if (hp > 0)
        {
            return hp;
        }

        if (moduleBase > 0)
        {
            long candidate = (long)moduleBase + hpAddress;
            if (candidate is > 0 and <= int.MaxValue)
            {
                hp = _memoryReader.TryReadUInt32(processId, (int)candidate);
                if (hp > 0)
                {
                    return hp;
                }
            }
        }

        return 0;
    }

}

public sealed class SupportedServerEntry
{
    public required string ProcessName { get; init; }

    public required int HpAddress { get; init; }

    public required int NameAddress { get; init; }
}
