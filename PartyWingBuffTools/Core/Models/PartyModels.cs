using System.Collections.Generic;

namespace PartyWingBuffTools.Core.Models;

public sealed class RagnarokProcessInfo
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string DisplayName { get; init; }

    public required string CharacterName { get; init; }
}

public sealed class MemberSequenceConfig
{
    public required int ProcessId { get; init; }

    public required string CharacterLabel { get; init; }

    public required IReadOnlyList<KeyStepConfig> KeySequence { get; init; }
}

public sealed class KeyStepConfig
{
    public required string Key { get; init; }

    public required int DelayAfterMs { get; init; }
}

public sealed class TriggerSequenceConfig
{
    public required string Name { get; init; }

    public required int IntervalSeconds { get; init; }

    public required string TeleportKey { get; init; }

    public required int PostTeleportDelayMs { get; init; }

    public required IReadOnlyList<MemberSequenceConfig> Members { get; init; }
}

public sealed class PartyConfig
{
    public required int ArchbishopProcessId { get; init; }

    public required IReadOnlyList<TriggerSequenceConfig> TriggerSequences { get; init; }
}

public sealed class DispatchAction
{
    public required int ProcessId { get; init; }

    public required string Key { get; init; }

    public required string Reason { get; init; }

    public required int DelayAfterMs { get; init; }
}

public sealed class DispatchPlan
{
    public required string TriggerName { get; init; }

    public required IReadOnlyList<DispatchAction> Actions { get; init; }
}
