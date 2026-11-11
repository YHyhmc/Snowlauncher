using System.Text.Json;

namespace WPCL.Services;

public static class MinecraftRules
{
    public static bool IsAllowed(JsonElement item)
    {
        if (!item.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (Matches(rule))
            {
                allowed = rule.TryGetProperty("action", out var action) && action.GetString() == "allow";
            }
        }
        return allowed;
    }

    public static bool ContainsQuickPlayRule(JsonElement item)
    {
        if (!item.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return rules.EnumerateArray().Any(rule =>
            rule.TryGetProperty("features", out var features) &&
            features.EnumerateObject().Any(feature => feature.Name.StartsWith("is_quick_play", StringComparison.Ordinal)));
    }

    private static bool Matches(JsonElement rule)
    {
        if (rule.TryGetProperty("os", out var os))
        {
            if (os.TryGetProperty("name", out var name) && name.GetString() != "windows")
            {
                return false;
            }

            if (os.TryGetProperty("arch", out var arch))
            {
                var current = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
                if (!string.Equals(arch.GetString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (rule.TryGetProperty("features", out var features))
        {
            foreach (var feature in features.EnumerateObject())
            {
                var actual = false;
                if (feature.Value.ValueKind == JsonValueKind.True && !actual)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
