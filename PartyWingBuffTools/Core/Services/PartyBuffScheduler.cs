using System;
using System.Collections.Generic;
using System.Linq;
using PartyWingBuffTools.Core.Models;

namespace PartyWingBuffTools.Core.Services;

public sealed class PartyBuffScheduler
{
    private readonly Dictionary<string, DateTimeOffset> _lastRunByTrigger = new();

    public IReadOnlyList<DispatchPlan> BuildPlans(PartyConfig config, DateTimeOffset now)
    {
        var plans = new List<DispatchPlan>();
        foreach (TriggerSequenceConfig trigger in config.TriggerSequences)
        {
            if (!IsTriggerDue(trigger.Name, trigger.IntervalSeconds, now))
            {
                continue;
            }

            var actions = new List<DispatchAction>
            {
                new()
                {
                    ProcessId = config.ArchbishopProcessId,
                    Key = trigger.TeleportKey,
                    Reason = $"{trigger.Name}:Teleport",
                    DelayAfterMs = Math.Max(0, trigger.PostTeleportDelayMs),
                },
            };

            foreach (MemberSequenceConfig member in trigger.Members)
            {
                var steps = member.KeySequence
                    .Where(k => !string.IsNullOrWhiteSpace(k.Key))
                    .ToList();
                for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
                {
                    KeyStepConfig step = steps[stepIndex];
                    actions.Add(new DispatchAction
                    {
                        ProcessId = member.ProcessId,
                        Key = step.Key,
                        Reason = $"{trigger.Name}:{member.CharacterLabel}:{member.ProcessId}:Step{stepIndex + 1}",
                        DelayAfterMs = Math.Max(0, step.DelayAfterMs),
                    });
                }
            }

            plans.Add(new DispatchPlan
            {
                TriggerName = trigger.Name,
                Actions = actions,
            });

            _lastRunByTrigger[trigger.Name] = now;
        }

        return plans;
    }

    private bool IsTriggerDue(string triggerName, int intervalSeconds, DateTimeOffset now)
    {
        int safeInterval = Math.Max(1, intervalSeconds);
        if (!_lastRunByTrigger.TryGetValue(triggerName, out DateTimeOffset lastRun))
        {
            return true;
        }

        return now >= lastRun.AddSeconds(safeInterval);
    }

    public void Reset()
    {
        _lastRunByTrigger.Clear();
    }
}
