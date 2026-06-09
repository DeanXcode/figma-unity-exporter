namespace FigmaUnityBridge;

internal static partial class Program
{
    private sealed class UiModel
    {
        public List<PrefabNode> Nodes { get; set; } = [];
        public Dictionary<string, List<PrefabNode>> ChildrenByParent { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, PrefabNode> NodeById { get; set; } = new(StringComparer.Ordinal);
        public List<MenuReport> Menus { get; set; } = [];
        public List<StateGroupReport> StateGroups { get; set; } = [];
        public List<ButtonReport> AutoButtons { get; set; } = [];
    }

    private sealed class MenuReport
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public int TabCount { get; set; }
        public List<TabReport> Tabs { get; set; } = [];
    }

    private sealed class TabReport
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public bool HasNormal { get; set; }
        public bool HasSelected { get; set; }
        public bool NormalVisible { get; set; }
        public bool SelectedVisible { get; set; }
    }

    private sealed class StateGroupReport
    {
        public string OwnerName { get; set; } = string.Empty;
        public List<StateLayerReport> States { get; set; } = [];
    }

    private sealed class StateLayerReport
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public bool Visible { get; set; }
    }

    private sealed class ButtonReport
    {
        public string Name { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }
}
