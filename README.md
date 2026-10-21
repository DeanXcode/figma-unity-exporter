# Codex Figma Unity Exporter

Export selected Figma UI layers to Unity UI prefabs. The Windows bridge writes assets directly into a Unity project; the Figma plugin also creates a PNG sprite for each exported visual leaf and supports TextMeshPro text, anchors, menu/state helpers, and export reports.

[中文使用说明](README.zh-CN.md)

## Requirements

- Windows (the included bridge and release package target `win-x64`).
- .NET 8 SDK to build the bridge from source, or use the packaged bridge executable.
- A Unity project with a `Packages/manifest.json` and `Assets/` folder.
- TextMeshPro available in the Unity project for exported text.
- Figma desktop or web with permission to import a development plugin.

## Install

### Build the bridge from source

From the repository root, publish a self-contained Windows executable:

```powershell
dotnet publish .\bridge\FigmaUnityBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\bridge\publish-self-contained
```

Alternatively, create a release zip (it publishes the bridge and stages the plugin and Unity scripts):

```powershell
.\scripts\package-release.ps1 -Version v0.1.0
```

The zip is written under `artifacts/`. Extract it before following the steps below.

### Import the Figma plugin

1. Open Figma and choose **Plugins → Development → Import plugin from manifest**.
2. Select `figma-plugin/manifest.json` from this repository or extracted release.
3. Run **Codex Unity Exporter** from Figma's development plugins.

### Install Unity helper scripts

Copy both folders into the root of the Unity project and keep their `.meta` files:

```text
unity/Assets/Editor
unity/Assets/FigmaUnityExport
```

Unity imports the scripts when it next refreshes. Resolve any missing TextMeshPro dependency in the project before exporting text.

## Start the local bridge

Run the published executable and pass the absolute path to the Unity project:

```powershell
.\bridge\publish-self-contained\FigmaUnityBridge.exe --project "D:\Projects\MyUnityGame"
```

The project path must contain `Assets/`. Keep the console running while exporting. The plugin defaults to `http://localhost:17890/export`; the bridge also exposes:

| URL | Purpose |
| --- | --- |
| `http://localhost:17890/health` | Connection check |
| `http://localhost:17890/prefab` | Figma prefab export endpoint |
| `http://localhost:17890/export` | Single asset export endpoint |

In the plugin's **Advanced settings** (gear icon), set **Unity project path** to the same project path. Use the chain icon to check the bridge. The copy-command icon copies a PowerShell launch command; if the bridge is not running, the plugin also displays a command and a one-time Windows logon startup option.

To install or remove the optional logon startup task manually:

```powershell
FigmaUnityBridge.exe --install-startup --project "D:\Projects\MyUnityGame"
FigmaUnityBridge.exe --uninstall-startup
```

The task runs for the current Windows user and remembers the project/prefix passed at installation time. To change the project, run the install command again with the new path.

For a source checkout, `start-bridge.bat "D:\Projects\MyUnityGame"` starts the bridge built at `bridge\bin\Release\net8.0-windows\FigmaUnityBridge.exe`. If no path is supplied, the script prompts for one.

## Export a UI prefab

1. Select a Frame, component, or layer in Figma. For a whole screen, select its top-level Frame.
2. Open **Advanced settings** if you want to change the prefab name, destination folder, font strategy, TMP font GUID, or bridge URL.
3. Click **Export selected content**. The plugin sends the selected hierarchy to the local bridge.
4. In Unity, wait for assets to refresh. The output is placed under the selected destination (default `Assets/FigmaUnityExport`) in a folder named from the prefab plus a source identifier. The folder contains the `.prefab`, `Sprites/`, and export report/manifest files.

Repeating the export for the same Figma source and root name updates that export folder. The bridge replaces its generated `Sprites` folder and writes the prefab/report again; keep custom files outside generated export folders.

## Figma naming and interaction helpers

- `btn_...` layers receive a Unity UGUI `Button` component.
- `menu_...` nodes are treated as menu switcher owners when they contain direct `tab_...` children.
- `tab_...` layers receive a Button and are included in a parent menu when directly nested under `menu_...`.
- `state_...` children are state layers. A `FigmaStateSwitcher` is generated on their parent when it has direct state children.
- A tab can have direct `state_normal` and `state_selected` children. The selected visual is chosen from the initially visible `state_selected`, or the first tab if none is visible.
- Use the plugin's naming buttons to add prefixes to the selected Figma layer. **Generate menu template** creates a sample menu/tab structure.
- **Inspect structure** reports naming and menu/state issues before export; it does not change the selection.

At runtime, `FigmaMenuSwitcher.SetSelected(key)` selects a tab and toggles its normal/selected layers. `FigmaStateSwitcher.SetOnly(key)` activates one state, and `SetVisible(key, visible)` changes an individual state. Keys are derived from names after the prefix (for example, `tab_settings` becomes `settings`).

## Export behavior and fonts

- Text layers become `TextMeshProUGUI`; Figma horizontal/vertical alignment and layout constraints are mapped to Unity fields.
- Auto-width text disables TMP word wrapping. Fixed-width auto-height text and explicit line breaks enable wrapping.
- Visual leaf layers export as PNG sprites and Unity `Image` components. Containers with exportable children remain hierarchy nodes.
- The default font strategy uses the configured TMP FontAsset GUID (the plugin's default GUID is `ea525a267c9e3a042837ad4847a1ea09`). Set this to a font asset in your Unity project if needed.
- **Match/generate from Figma fonts** reuses matching `*SDF.asset` files when available, searches project/system font files, and creates font generation tasks for the included Unity editor helper. Inspect the export report for font warnings/tasks.
- Repeated text styles can reuse generated TMP outline/shadow material presets.

## Troubleshooting

- **Bridge not connected:** start the executable, verify the project path has `Assets/`, and check that the plugin URL matches the bridge prefix. Default port is `17890`.
- **Prefab export fails:** confirm the Unity project path is correct and writable, and that the destination is inside `Assets/`.
- **Text has a missing font:** install/enable TextMeshPro and use a valid TMP FontAsset GUID, or choose the Figma font strategy and process generated font tasks in Unity.
- **Menu has no tabs:** make sure `tab_` nodes are direct children of a `menu_` node. Use **Inspect structure** and read the generated report.
- **Unity cannot resolve generated helper components:** copy the Unity helper folders with their `.meta` files before importing the prefab.

## Current limits

- Unity UI Prefab is the only implemented export target. The exporter abstraction does not mean Godot or Unreal export is available.
- Visual leaf layers are rasterized to PNG; editable vector shapes are not recreated as native Unity graphics.
- Nine-slice metadata, particles, Spine, complex script bindings, and dynamically loaded resources need project-specific mapping.

## Repository layout

- `figma-plugin/`: Figma plugin source and manifest.
- `bridge/`: local .NET HTTP bridge and Unity prefab exporter.
- `unity/Assets/`: runtime/editor helper scripts and stable `.meta` GUIDs.
- `scripts/package-release.ps1`: Windows release packaging script.
