using System.Diagnostics;
using System.Linq;

namespace RagnarokAutomation.Core;

public sealed class ProcessRegistryService
{
    public IReadOnlyList<ProcessSnapshot> DiscoverRagnarokProcesses(SocketInspector socketInspector, RagexeCharacterNameReader characterNameReader)
    {
        List<ProcessSnapshot> snapshots = [];
        foreach (Process process in Process.GetProcessesByName("Ragexe"))
        {
            try
            {
                if (!string.Equals(process.ProcessName, "Ragexe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool alive = !process.HasExited;
                ProcessSnapshot snapshot = socketInspector.GetSnapshot(process.Id, process.ProcessName, alive);
                CharacterNameReadResult readResult = characterNameReader.TryReadCharacterNameDetailed(process);
                snapshot.MemoryCharacterName = readResult.CharacterName;
                snapshot.MemoryReadDiagnostic = readResult.Diagnostic;
                snapshots.Add(snapshot);
            }
            catch
            {
                // Ignore process access errors.
            }
        }

        return snapshots.OrderBy(p => p.ProcessId).ToList();
    }
}
