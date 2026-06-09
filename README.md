# Codex Figma Unity Exporter

Figma to Unity UI exporter. It sends selected Figma layers to a local bridge and generates Unity UI Prefabs with sprites, TextMeshPro text, layout constraints, menu/state switchers, export reports, and shared TMP material presets.

## Contents

- `figma-plugin/`: Figma development plugin.
- `bridge/`: Local .NET HTTP bridge that writes Unity assets.
- `unity/Assets/`: Unity-side helper scripts and `.meta` files.

## Build Bridge

```powershell
dotnet publish .\bridge\FigmaUnityBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\bridge\publish-self-contained
```

## Start Bridge

Run manually:

```powershell
.\bridge\publish-self-contained\FigmaUnityBridge.exe --project <UnityProjectRoot>
```

Or install Windows login startup:

```powershell
.\bridge\publish-self-contained\FigmaUnityBridge.exe --install-startup --project <UnityProjectRoot>
```

Default endpoints:

```text
http://localhost:17890/export
http://localhost:17890/prefab
http://localhost:17890/health
```

## Install Figma Plugin

1. Open Figma.
2. Go to `Plugins -> Development -> Import plugin from manifest`.
3. Select `figma-plugin/manifest.json`.

## Install Unity Helpers

Copy these folders into the Unity project root:

```text
unity/Assets/Editor
unity/Assets/FigmaUnityExport
```

Keep the included `.meta` files. Some generated prefab components depend on stable Unity script GUIDs.

## Export Rules

- Figma `TEXT` layers become `TextMeshProUGUI`.
- Figma Constraints map to Unity `RectTransform` anchors.
- Text horizontal and vertical alignment are synced to TMP alignment fields.
- Figma auto-width text disables TMP word wrapping.
- Fixed-width auto-height text or explicit line breaks enable TMP word wrapping.
- Layers named with `btn_` or `tab_` get the configured Unity button component.
- Layers named with `menu_`, `tab_`, and `state_` generate menu/state switcher data.
- Visible leaf layers export as PNG sprites and Unity `Image` components.

## TMP Font Strategy

The plugin supports:

- Use configured Unity TMP font: all text uses the configured TMP FontAsset GUID.
- Match/generate from Figma fonts: reuse existing `*SDF.asset` first, then search project/system font files and create TMP generation tasks.

TMP material presets for outline and shadow are shared by style signature, so repeated exports reuse existing materials when possible.

## Current Limits

- Unity Prefab export is implemented.
- The bridge already has an exporter abstraction for future targets, but Godot/UE exporters are not implemented yet.
- Spine, particles, nine-slice metadata, complex script bindings, and dynamically loaded resources still need project-specific mapping rules.
