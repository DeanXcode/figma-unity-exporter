# Codex Figma Unity 导出工具

将 Figma 中选中的 UI 图层导出为 Unity UI Prefab。Windows Bridge 会把资源直接写入 Unity 工程；Figma 插件还会为可视叶子图层生成 PNG Sprite，并支持 TextMeshPro 文本、锚点、菜单/状态切换辅助组件和导出报告。

[English usage guide](README.md)

## 环境要求

- Windows（随附的 Bridge 和发布包面向 `win-x64`）。
- 从源码构建 Bridge 需要 .NET 8 SDK；也可以使用发布包内的 Bridge 可执行文件。
- Unity 工程应包含 `Packages/manifest.json` 和 `Assets/` 目录。
- 导出文本需要在 Unity 工程中安装/启用 TextMeshPro。
- Figma 桌面版或网页版，并允许导入开发插件。

## 安装

### 从源码构建 Bridge

在仓库根目录运行：

```powershell
dotnet publish .\bridge\FigmaUnityBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\bridge\publish-self-contained
```

也可以生成发布压缩包（脚本会发布 Bridge 并打包插件和 Unity 脚本）：

```powershell
.\scripts\package-release.ps1 -Version v0.1.0
```

压缩包位于 `artifacts/`，解压后按下列步骤安装。

### 导入 Figma 插件

1. 打开 Figma，选择 **Plugins → Development → Import plugin from manifest**（插件 → 开发 → 从 manifest 导入插件）。
2. 选择本仓库或已解压发布包中的 `figma-plugin/manifest.json`。
3. 在 Figma 的开发插件中运行 **Codex Unity Exporter**。

### 安装 Unity 辅助脚本

将下面两个目录复制到 Unity 工程根目录，并保留随附的 `.meta` 文件：

```text
unity/Assets/Editor
unity/Assets/FigmaUnityExport
```

Unity 刷新资源后会导入脚本。导出文本前，请先在工程中安装/启用 TextMeshPro 并解决缺失依赖。

## 启动本地 Bridge

运行发布好的可执行文件，并传入 Unity 工程的绝对路径：

```powershell
.\bridge\publish-self-contained\FigmaUnityBridge.exe --project "D:\Projects\MyUnityGame"
```

工程目录必须包含 `Assets/`。导出期间请保持 Bridge 控制台运行。插件默认使用 `http://localhost:17890/export`，Bridge 还提供以下地址：

| 地址 | 用途 |
| --- | --- |
| `http://localhost:17890/health` | 检查连接 |
| `http://localhost:17890/prefab` | Figma Prefab 导出接口 |
| `http://localhost:17890/export` | 单个资源导出接口 |

在插件的**高级设置**（齿轮图标）中，将**Unity 工程路径**设置为相同路径。点击链条图标检测 Bridge。复制图标会复制 PowerShell 启动命令；如果 Bridge 未运行，插件也会显示手动启动命令和一次性 Windows 登录自启命令。

手动安装或移除可选的登录启动任务：

```powershell
FigmaUnityBridge.exe --install-startup --project "D:\Projects\MyUnityGame"
FigmaUnityBridge.exe --uninstall-startup
```

该任务以当前 Windows 用户身份运行，并记住安装时传入的工程路径和监听地址。若要更换工程路径，使用新路径重新运行安装命令。

## 导出 UI Prefab

1. 在 Figma 中选择 Frame、组件或图层。要导出整个界面，请选择顶层 Frame。
2. 如需自定义 Prefab 名称、导出目录、字体策略、TMP 字体 GUID 或 Bridge 地址，先打开**高级设置**。
3. 点击**导出选中内容**。插件会把选中节点层级发送到本机 Bridge。
4. 等待 Unity 刷新资源。导出内容写入指定目录（默认 `Assets/FigmaUnityExport`）下按 Prefab 名称和源标识创建的文件夹，其中包含 `.prefab`、`Sprites/` 以及导出报告/清单。

对同一个 Figma 源和根名称再次导出会更新原文件夹。Bridge 会替换生成的 `Sprites` 文件夹并重新写入 Prefab 和报告；请勿把自定义文件放在生成目录内。

## Figma 命名与交互辅助功能

- `btn_...` 图层会添加 Unity UGUI `Button` 组件。
- 带有直接 `tab_...` 子节点的 `menu_...` 节点会作为菜单切换器的宿主。
- `tab_...` 图层会添加 Button；当它直接位于 `menu_...` 下时，也会被菜单切换器识别。
- `state_...` 是状态图层。某节点有直接状态子节点时，会在该节点上生成 `FigmaStateSwitcher`。
- Tab 可包含直接子节点 `state_normal` 和 `state_selected`。默认选中初始可见的 `state_selected`；如果没有，则选择第一个 Tab。
- 使用插件中的命名按钮为选中图层添加前缀。**生成菜单模板**会创建示例菜单/Tab 结构。
- **检查结构**会报告命名及菜单/状态结构问题，不会修改当前选区。

运行时可调用 `FigmaMenuSwitcher.SetSelected(key)` 选中 Tab 并切换普通/选中图层；`FigmaStateSwitcher.SetOnly(key)` 只激活一个状态，`SetVisible(key, visible)` 切换单个状态。Key 取自前缀后的名称，例如 `tab_settings` 对应 `settings`。

## 导出行为与字体

- 文本图层导出为 `TextMeshProUGUI`；Figma 水平/垂直对齐和布局约束会映射到 Unity 对应字段。
- 自动宽度文本会关闭 TMP 自动换行；固定宽度自动高度文本及包含显式换行的文本会启用换行。
- 可视叶子图层会栅格化为 PNG Sprite 和 Unity `Image`。含有可导出子节点的容器会保留为层级节点。
- 默认字体策略使用配置的 TMP FontAsset GUID（默认 `ea525a267c9e3a042837ad4847a1ea09`）。如有需要，请改为 Unity 工程中的字体资源 GUID。
- **按 Figma 字体匹配/生成**会优先复用匹配的 `*SDF.asset`，然后搜索工程/系统字体文件，并为随附的 Unity Editor 辅助工具创建字体生成任务。请查看导出报告中的字体警告和任务。
- 相同文本样式可复用生成的 TMP 描边/阴影材质预设。

## 常见问题

- **Bridge 未连接：**启动可执行文件，确认工程目录包含 `Assets/`，并确认插件地址与 Bridge 监听地址一致。默认端口为 `17890`。
- **Prefab 导出失败：**检查 Unity 工程路径是否正确且可写，并确认导出目录位于 `Assets/` 内。
- **文本缺少字体：**安装/启用 TextMeshPro 并填写有效 TMP FontAsset GUID；也可选择 Figma 字体策略，并在 Unity 中处理生成任务。
- **菜单没有 Tab：**确认 `tab_` 节点是 `menu_` 的直接子节点。使用**检查结构**并查看生成的报告。
- **Unity 无法解析生成的辅助组件：**导入 Prefab 前，先复制 Unity 辅助脚本目录及其 `.meta` 文件。

## 当前限制

- 目前只实现 Unity UI Prefab 导出。存在导出器抽象不代表 Godot 或 Unreal 导出已实现。
- 可视叶子图层会栅格化为 PNG，不会还原为可编辑的 Unity 原生矢量图形。
- 九宫格元数据、粒子、Spine、复杂脚本绑定和动态加载资源仍需按项目定制映射。

## 仓库目录

- `figma-plugin/`：Figma 插件源码和 manifest。
- `bridge/`：本地 .NET HTTP Bridge 和 Unity Prefab 导出器。
- `unity/Assets/`：运行时/编辑器辅助脚本及稳定的 `.meta` GUID。
- `scripts/package-release.ps1`：Windows 发布打包脚本。
