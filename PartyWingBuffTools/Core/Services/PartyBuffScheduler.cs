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
        if (config.TriggerSequences.Count == 0)
        {
            return Array.Empty<DispatchPlan>();
        }

        TriggerSequenceConfig mainTrigger = config.TriggerSequences[0];
        if (!IsTriggerDue(mainTrigger.Name, mainTrigger.IntervalSeconds, now))
        {
            return Array.Empty<DispatchPlan>();
        }

        var plans = new List<DispatchPlan>();
        foreach (TriggerSequenceConfig trigger in config.TriggerSequences)
        {
            var actions = new List<DispatchAction>
            {
                new()
                {
                    ProcessId = config.ArchbishopProcessId,
                    Type = ActionStepType.Key,
                    Key = trigger.TeleportKey,
                    Reason = $"{trigger.Name}:Teleport",
                    DelayAfterMs = Math.Max(0, trigger.PostTeleportDelayMs),
                },
            };

            foreach (MemberSequenceConfig member in trigger.Members)
            {
                var steps = member.Steps
                    .Where(IsValidStep)
                    .ToList();
                for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
                {
                    ActionStepConfig step = steps[stepIndex];
                    actions.Add(new DispatchAction
                    {
                        ProcessId = member.ProcessId,
                        Type = step.Type,
                        Key = step.Key,
                        NormalizedX = step.NormalizedX,
                        NormalizedY = step.NormalizedY,
                        Reason = $"{trigger.Name}:{member.CharacterLabel}:{member.ProcessId}:Step{stepIndex + 1}",
                        DelayAfterMs = Math.Max(0, step.DelayAfterMs),
                    });
                }
            }

            plans.Add(new DispatchPlan
            {
                TriggerName = trigger.Name,
                DelayBeforeNextMs = Math.Max(0, trigger.DelayBeforeNextMs),
                Actions = actions,
            });
        }

        _lastRunByTrigger[mainTrigger.Name] = now;
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

    private static bool IsValidStep(ActionStepConfig step)
    {
        if (step.Type == ActionStepType.Key)
        {
            return !string.IsNullOrWhiteSpace(step.Key);
        }

        return step.NormalizedX.HasValue && step.NormalizedY.HasValue;
    }
}
