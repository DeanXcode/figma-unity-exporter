using System.Text.RegularExpressions;

namespace FigmaUnityBridge;

internal static partial class Program
{
    private static bool IsTextNode(PrefabNode node)
    {
        return node.NodeType.Equals("TEXT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsButtonNode(PrefabNode node)
    {
        return HasNamePrefix(node.Name, "btn") || HasNamePrefix(node.Name, "tab");
    }

    private static bool IsMenuNode(PrefabNode node)
    {
        return HasNamePrefix(node.Name, "menu");
    }

    private static bool IsStateNode(PrefabNode node)
    {
        return HasNamePrefix(node.Name, "state");
    }

    private static bool HasNamePrefix(string name, string prefix)
    {
        string normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == prefix
            || normalized.StartsWith(prefix + "_", StringComparison.Ordinal)
            || normalized.StartsWith(prefix + "-", StringComparison.Ordinal)
            || normalized.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private static string KeyFromName(string name, string prefix)
    {
        string value = (name ?? string.Empty).Trim();
        foreach (string marker in new[] { prefix + "_", prefix + "-", prefix + "/" })
        {
            if (value.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                value = value[marker.Length..];
                break;
            }
        }

        value = Regex.Replace(value, @"[^0-9a-zA-Z\u4e00-\u9fa5]+", "_").Trim('_').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? SanitizeName(name ?? string.Empty).ToLowerInvariant() : value;
    }

    private static List<PrefabNode> GetTabChildren(PrefabNode node, Dictionary<string, List<PrefabNode>> childrenByParent)
    {
        return childrenByParent.TryGetValue(node.Id, out List<PrefabNode>? children)
            ? children.Where(child => HasNamePrefix(child.Name, "tab")).ToList()
            : [];
    }

    private static List<PrefabNode> GetStateChildren(PrefabNode node, Dictionary<string, List<PrefabNode>> childrenByParent)
    {
        return childrenByParent.TryGetValue(node.Id, out List<PrefabNode>? children)
            ? children.Where(IsStateNode).ToList()
            : [];
    }

    private static PrefabNode? FindStateChild(PrefabNode node, string stateKey, Dictionary<string, List<PrefabNode>> childrenByParent)
    {
        return GetStateChildren(node, childrenByParent)
            .FirstOrDefault(child => KeyFromName(child.Name, "state").Equals(stateKey, StringComparison.OrdinalIgnoreCase));
    }
}
