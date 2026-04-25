using System.Diagnostics;
using System.Text.Json;

namespace RagnarokAutomation.Core;

public sealed class RagexeCharacterNameReader
{
    private readonly ProcessMemoryReader _memoryReader = new();
    private readonly List<RagexeAddressCandidate> _candidates;
    private readonly int _fallbackNameAddress;
    private readonly string _sourceInfo;

    public RagexeCharacterNameReader()
    {
        (_candidates, _sourceInfo) = ResolveRagexeCandidates();
        _fallbackNameAddress = _candidates.FirstOrDefault(c => c.IsPreferredLandverse)?.NameAddress
            ?? _candidates.LastOrDefault()?.NameAddress
            ?? unchecked((int)0x012C0F08);
    }

    public string TryReadCharacterName(Process process)
    {
        return TryReadCharacterNameDetailed(process).CharacterName;
    }

    public CharacterNameReadResult TryReadCharacterNameDetailed(Process process)
    {
        if (process.HasExited)
        {
            return new CharacterNameReadResult(string.Empty, "process-exited");
        }

        int processId = process.Id;
        int moduleBase = 0;
        try
        {
            moduleBase = process.MainModule?.BaseAddress.ToInt32() ?? 0;
        }
        catch
        {
            moduleBase = 0;
        }

        foreach (RagexeAddressCandidate candidate in _candidates)
        {
            uint hp = TryReadHp(processId, candidate.HpAddress, moduleBase);
            if (hp == 0)
            {
                continue;
            }

            string byMatchedCandidate = CharacterNameMemoryResolver.TryReadCharacterName(
                _memoryReader,
                processId,
                candidate.NameAddress,
                moduleBase);
            if (!string.IsNullOrWhiteSpace(byMatchedCandidate))
            {
                string diag = $"matched-candidate hp=0x{candidate.HpAddress:X8} name=0x{candidate.NameAddress:X8} module=0x{moduleBase:X8} src={_sourceInfo}";
                return new CharacterNameReadResult(byMatchedCandidate, diag);
            }
        }

        foreach (RagexeAddressCandidate candidate in _candidates)
        {
            string byAnyCandidate = CharacterNameMemoryResolver.TryReadCharacterName(
                _memoryReader,
                processId,
                candidate.NameAddress,
                moduleBase);
            if (!string.IsNullOrWhiteSpace(byAnyCandidate))
            {
                string diag = $"fallback-candidate name=0x{candidate.NameAddress:X8} module=0x{moduleBase:X8} src={_sourceInfo}";
                return new CharacterNameReadResult(byAnyCandidate, diag);
            }
        }

        string fallback = CharacterNameMemoryResolver.TryReadCharacterName(_memoryReader, processId, _fallbackNameAddress, moduleBase);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            string diag = $"fallback-address name=0x{_fallbackNameAddress:X8} module=0x{moduleBase:X8} src={_sourceInfo}";
            return new CharacterNameReadResult(fallback, diag);
        }

        string openDiag = _memoryReader.GetOpenProcessDiagnostic(processId);
        string failDiag = $"name-read-empty fallback=0x{_fallbackNameAddress:X8} module=0x{moduleBase:X8} src={_sourceInfo} {openDiag}";
        return new CharacterNameReadResult(string.Empty, failDiag);
    }

    private static (List<RagexeAddressCandidate> candidates, string sourceInfo) ResolveRagexeCandidates()
    {
        HashSet<string> probePaths = BuildProbePaths();

        foreach (string candidate in probePaths)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(candidate));
                List<RagexeAddressCandidate> entries = [];
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("name", out JsonElement nameElement))
                    {
                        continue;
                    }

                    string? name = nameElement.GetString();
                    if (!string.Equals(name, "Ragexe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("nameAddress", out JsonElement nameAddressElement) ||
                        !item.TryGetProperty("hpAddress", out JsonElement hpAddressElement))
                    {
                        continue;
                    }

                    string? nameAddressRaw = nameAddressElement.GetString();
                    string? hpAddressRaw = hpAddressElement.GetString();
                    if (TryParseHex(nameAddressRaw, out int nameAddress) &&
                        TryParseHex(hpAddressRaw, out int hpAddress))
                    {
                        string site = item.TryGetProperty("site", out JsonElement siteElement)
                            ? (siteElement.GetString() ?? string.Empty)
                            : string.Empty;
                        entries.Add(new RagexeAddressCandidate(
                            hpAddress,
                            nameAddress,
                            site.Contains("landverse", StringComparison.OrdinalIgnoreCase)));
                    }
                }

                if (entries.Count > 0)
                {
                    List<RagexeAddressCandidate> ordered = entries
                        .OrderByDescending(e => e.IsPreferredLandverse)
                        .ThenBy(e => e.NameAddress)
                        .ToList();
                    return (ordered, Path.GetFileName(candidate));
                }
            }
            catch
            {
                // Ignore malformed files and probe next path.
            }
        }

        return ([], "none");
    }

    private static bool TryParseHex(string? value, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string raw = value.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        return int.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out parsed);
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

    private static HashSet<string> BuildProbePaths()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        AddCandidatePath(paths, Environment.CurrentDirectory);
        AddCandidatePath(paths, AppContext.BaseDirectory);
        AddParentCandidatePaths(paths, Environment.CurrentDirectory, 8);
        AddParentCandidatePaths(paths, AppContext.BaseDirectory, 8);
        return paths;
    }

    private static void AddParentCandidatePaths(HashSet<string> paths, string startPath, int maxDepth)
    {
        DirectoryInfo? dir = new(startPath);
        for (int i = 0; i < maxDepth && dir is not null; i++)
        {
            AddCandidatePath(paths, dir.FullName);
            dir = dir.Parent;
        }
    }

    private static void AddCandidatePath(HashSet<string> paths, string basePath)
    {
        try
        {
            string candidate = Path.GetFullPath(Path.Combine(basePath, "supported_servers.json"));
            paths.Add(candidate);
        }
        catch
        {
            // ignore invalid path segments
        }
    }

    private sealed record RagexeAddressCandidate(int HpAddress, int NameAddress, bool IsPreferredLandverse);
}

public sealed record CharacterNameReadResult(string CharacterName, string Diagnostic);
