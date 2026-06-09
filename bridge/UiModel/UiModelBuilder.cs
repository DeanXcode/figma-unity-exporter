namespace FigmaUnityBridge;

internal static partial class Program
{
    private static UiModel BuildUiModel(List<PrefabNode> nodes)
    {
        Dictionary<string, List<PrefabNode>> childrenByParent = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ParentId))
            .GroupBy(node => node.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        Dictionary<string, PrefabNode> nodeById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        return new UiModel
        {
            Nodes = nodes,
            ChildrenByParent = childrenByParent,
            NodeById = nodeById,
            Menus = CreateMenuReports(nodes, childrenByParent),
            StateGroups = CreateStateGroupReports(nodes, childrenByParent),
            AutoButtons = CreateButtonReports(nodes),
        };
    }

    private static List<MenuReport> CreateMenuReports(List<PrefabNode> nodes, Dictionary<string, List<PrefabNode>> childrenByParent)
    {
        List<MenuReport> menus = [];
        foreach (PrefabNode menu in nodes.Where(IsMenuNode))
        {
            List<PrefabNode> tabs = GetTabChildren(menu, childrenByParent);
            menus.Add(new MenuReport
            {
                Name = menu.Name,
                Key = KeyFromName(menu.Name, "menu"),
                TabCount = tabs.Count,
                Tabs = tabs.Select(tab =>
                {
                    PrefabNode? normal = FindStateChild(tab, "normal", childrenByParent);
                    PrefabNode? selected = FindStateChild(tab, "selected", childrenByParent);
                    return new TabReport
                    {
                        Name = tab.Name,
                        Key = KeyFromName(tab.Name, "tab"),
                        HasNormal = normal != null,
                        HasSelected = selected != null,
                        NormalVisible = normal?.Visible ?? false,
                        SelectedVisible = selected?.Visible ?? false,
                    };
                }).ToList(),
            });
        }

        return menus;
    }

    private static List<StateGroupReport> CreateStateGroupReports(List<PrefabNode> nodes, Dictionary<string, List<PrefabNode>> childrenByParent)
    {
        return nodes
            .Where(node => GetStateChildren(node, childrenByParent).Count > 0)
            .Select(node => new StateGroupReport
            {
                OwnerName = node.Name,
                States = GetStateChildren(node, childrenByParent)
                    .Select(state => new StateLayerReport
                    {
                        Name = state.Name,
                        Key = KeyFromName(state.Name, "state"),
                        Visible = state.Visible,
                    })
                    .ToList(),
            })
            .ToList();
    }

    private static List<ButtonReport> CreateButtonReports(List<PrefabNode> nodes)
    {
        return nodes
            .Where(IsButtonNode)
            .Select(node => new ButtonReport
            {
                Name = node.Name,
                Key = KeyFromName(node.Name, HasNamePrefix(node.Name, "tab") ? "tab" : "btn"),
                Kind = HasNamePrefix(node.Name, "tab") ? "tab" : "btn",
            })
            .ToList();
    }
}
