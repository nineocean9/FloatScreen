using System.Text;

namespace FloatScreen;

internal static class DanmakuMergeKey
{
    private static readonly DanmakuRuleMatcher Default = CreateDefault();

    public static DanmakuAliasRule[] DefaultRules() => [new("biaoji", "标记")];

    private static DanmakuRuleMatcher CreateDefault()
    {
        DanmakuRuleMatcher.TryCreate(DefaultRules(), out var matcher, out _);
        return matcher;
    }

    public static string Create(string text) => Default.Create(text);

    public static string CreateBase(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var hasLetter = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune)) continue;
            hasLetter |= Rune.IsLetter(rune);
            builder.Append(Rune.ToLowerInvariant(rune).ToString());
        }
        var key = builder.ToString();
        if (key.Length == 0) return "literal:" + normalized.Trim();
        if (hasLetter) key = ReduceRepeatingUnit(key);
        return key;
    }

    private static string ReduceRepeatingUnit(string text)
    {
        // KMP prefix lengths find the shortest exact repeating unit in linear time.
        Span<int> prefix = text.Length <= 512 ? stackalloc int[text.Length] : new int[text.Length];
        prefix.Clear();
        for (var index = 1; index < text.Length; index++)
        {
            var length = prefix[index - 1];
            while (length > 0 && text[index] != text[length]) length = prefix[length - 1];
            if (text[index] == text[length]) length++;
            prefix[index] = length;
        }
        var unit = text.Length - prefix[^1];
        return unit < text.Length && text.Length % unit == 0 ? text[..unit] : text;
    }
}

internal sealed record DanmakuAliasRule(string Source, string Target);

internal sealed class DanmakuRuleMatcher
{
    private readonly Dictionary<string, string> _aliases;
    private DanmakuRuleMatcher(Dictionary<string, string> aliases) => _aliases = aliases;
    public string Create(string text)
    {
        var key = DanmakuMergeKey.CreateBase(text);
        return _aliases.GetValueOrDefault(key, key);
    }

    public static bool TryCreate(IReadOnlyList<DanmakuAliasRule>? rules, out DanmakuRuleMatcher matcher, out string error)
    {
        matcher = new([]); error = "";
        if (rules == null || rules.Count > 200) { error = "规则表最多支持 200 条。"; return false; }
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Source) || string.IsNullOrWhiteSpace(rule.Target)
                    || rule.Source.Length > 256 || rule.Target.Length > 256)
                { error = "每条规则需要填写原写法和统一词，各不超过 256 个字符。"; return false; }
                var source = DanmakuMergeKey.CreateBase(rule.Source);
                var target = DanmakuMergeKey.CreateBase(rule.Target);
                if (source == target) continue; // Already handled by built-in format/repetition normalization.
                if (map.TryGetValue(source, out var existing) && existing != target)
                { error = $"“{rule.Source}”与另一条规则等价，但指向了不同的统一词。"; return false; }
                map[source] = target;
            }
        }
        catch (ArgumentException) { error = "规则中含有无法处理的字符。"; return false; }
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in map.Keys)
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            var target = source;
            while (map.TryGetValue(target, out var next))
            {
                if (!path.Add(target)) { error = "规则存在循环，例如 A→B→A，请修改后保存。"; return false; }
                target = next;
            }
            resolved[source] = target;
        }
        matcher = new(resolved);
        return true;
    }
}
