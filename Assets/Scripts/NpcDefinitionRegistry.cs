using System;
using System.Collections.Generic;

public static class NpcDefinitionRegistry
{
    private static readonly Dictionary<string, NpcDefinition> DefinitionsByStableId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> RegistrationCountsByStableId = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IEnumerable<NpcDefinition> definitions)
    {
        if (definitions == null)
        {
            return;
        }

        foreach (NpcDefinition definition in definitions)
        {
            Register(definition);
        }
    }

    public static void Register(NpcDefinition definition)
    {
        if (!TryNormalizeStableId(definition, out string stableId))
        {
            return;
        }

        DefinitionsByStableId[stableId] = definition;
        RegistrationCountsByStableId.TryGetValue(stableId, out int currentCount);
        RegistrationCountsByStableId[stableId] = currentCount + 1;
    }

    public static void Unregister(IEnumerable<NpcDefinition> definitions)
    {
        if (definitions == null)
        {
            return;
        }

        foreach (NpcDefinition definition in definitions)
        {
            Unregister(definition);
        }
    }

    public static void Unregister(NpcDefinition definition)
    {
        if (!TryNormalizeStableId(definition, out string stableId) ||
            !RegistrationCountsByStableId.TryGetValue(stableId, out int currentCount))
        {
            return;
        }

        if (currentCount <= 1)
        {
            RegistrationCountsByStableId.Remove(stableId);
            DefinitionsByStableId.Remove(stableId);
            return;
        }

        RegistrationCountsByStableId[stableId] = currentCount - 1;
        DefinitionsByStableId[stableId] = definition;
    }

    public static bool TryResolve(string stableId, out NpcDefinition definition)
    {
        definition = null;
        string normalizedStableId = NpcDefinition.NormalizeStableId(stableId);
        return !string.IsNullOrWhiteSpace(normalizedStableId) &&
               DefinitionsByStableId.TryGetValue(normalizedStableId, out definition) &&
               definition != null;
    }

    private static bool TryNormalizeStableId(NpcDefinition definition, out string stableId)
    {
        stableId = definition != null ? NpcDefinition.NormalizeStableId(definition.StableId) : string.Empty;
        return !string.IsNullOrWhiteSpace(stableId);
    }
}
