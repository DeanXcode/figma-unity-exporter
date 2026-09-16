using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Win32;

namespace FigmaUnityBridge;

internal static partial class Program
{
    private const string DefaultPrefix = "http://localhost:17890/";
    private const string ProtocolName = "codex-unity-bridge";
    private const string ImageScriptGuid = "fe87c0e1cc204ed48ad3b37840f39efc";
    private const string UnityButtonScriptGuid = "4e29b1a8efbd4b44bb3f3716e73f07ff";
    private const string TextMeshProScriptGuid = "f4688fdb7df04437aeb418b961361dc5";
    private const string FigmaMenuSwitcherScriptGuid = "c0d3f19a000000000000000000000101";
    private const string FigmaStateSwitcherScriptGuid = "c0d3f19a000000000000000000000102";
    private const string DefaultTmpFontGuid = "ea525a267c9e3a042837ad4847a1ea09";

    private static async Task<int> Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--register-protocol", StringComparison.OrdinalIgnoreCase)))
        {
            RegisterProtocol();
            return 0;
        }

        BridgeOptions options = BridgeOptions.Parse(args);
        if (args.Any(arg => arg.Equals("--install-startup", StringComparison.OrdinalIgnoreCase)))
        {
            InstallStartupTask(options);
            return 0;
        }

        if (args.Any(arg => arg.Equals("--uninstall-startup", StringComparison.OrdinalIgnoreCase)))
        {
            UninstallStartupTask();
            return 0;
        }

        Directory.CreateDirectory(options.ProjectRoot);
        string assetsRoot = Path.Combine(options.ProjectRoot, "Assets");
        if (!Directory.Exists(assetsRoot))
        {
            Console.Error.WriteLine("Unity project Assets folder not found: " + assetsRoot);
            return 1;
        }

        using HttpListener listener = new();
        listener.Prefixes.Add(options.Prefix);
        listener.Start();

        Console.WriteLine("Codex Figma Unity Bridge");
        Console.WriteLine("Project : " + options.ProjectRoot);
        Console.WriteLine("Listen  : " + options.Prefix);
        Console.WriteLine("Endpoint: " + options.Prefix.TrimEnd('/') + "/export");
        Console.WriteLine("Press Ctrl+C to stop.");

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            listener.Stop();
        };

        while (!cts.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await listener.GetContextAsync();
                _ = Task.Run(() => Handle(context, options), cts.Token);
            }
            catch (HttpListenerException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }

        return 0;
    }

    private static void RegisterProtocol()
    {
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FigmaUnityBridge.exe");
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProtocolName);
        root.SetValue("", "URL:Codex Unity Bridge");
        root.SetValue("URL Protocol", "");
        using RegistryKey command = root.CreateSubKey(@"shell\open\command");
        command.SetValue("", "\"" + exePath + "\" \"%1\"");
        Console.WriteLine("Registered protocol: " + ProtocolName);
        Console.WriteLine("Command: " + exePath);
    }

    private static void InstallStartupTask(BridgeOptions options)
    {
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FigmaUnityBridge.exe");
        string arguments = "--project " + Quote(options.ProjectRoot) + " --prefix " + Quote(options.Prefix);
        string taskRun = Quote(exePath) + " " + arguments;

        RunSchtasks($"/Create /F /TN \"CodexFigmaUnityBridge\" /SC ONLOGON /RL LIMITED /TR \"{taskRun}\"");
        Console.WriteLine("Installed startup task: CodexFigmaUnityBridge");
        Console.WriteLine("Command: " + taskRun);
    }

    private static void UninstallStartupTask()
    {
        RunSchtasks("/Delete /F /TN \"CodexFigmaUnityBridge\"");
        Console.WriteLine("Removed startup task: CodexFigmaUnityBridge");
    }

    private static void RunSchtasks(string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start schtasks.exe");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.Length > 0 ? stderr : stdout);
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static async Task Handle(HttpListenerContext context, BridgeOptions options)
    {
        string failedPayload = string.Empty;
        string failedKind = string.Empty;
        try
        {
            if (context.Request.HttpMethod == "OPTIONS")
            {
                WriteCors(context.Response);
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/health" && context.Request.HttpMethod == "GET")
            {
                await WriteJson(context.Response, 200, new
                {
                    ok = true,
                    projectRoot = options.ProjectRoot,
                    prefix = options.Prefix,
                });
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/assets" && context.Request.HttpMethod == "GET")
            {
                string query = context.Request.QueryString["query"] ?? string.Empty;
                int limit = ParseLimit(context.Request.QueryString["limit"], 300);
                await WriteJson(context.Response, 200, new
                {
                    projectRoot = options.ProjectRoot,
                    items = SearchAssets(options.ProjectRoot, query, limit),
                });
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/prefab" && context.Request.HttpMethod == "POST")
            {
                using StreamReader prefabReader = new(context.Request.InputStream, context.Request.ContentEncoding);
                string prefabBody = await prefabReader.ReadToEndAsync();
                failedPayload = prefabBody;
                failedKind = "prefab";
                PrefabRequest? prefabRequest = JsonSerializer.Deserialize<PrefabRequest>(prefabBody, JsonOptions.Default);
                if (prefabRequest == null)
                {
                    await WriteJson(context.Response, 400, new { error = "Invalid JSON" });
                    return;
                }

                PrefabExportResult prefabResult = ApplyPrefabExport(prefabRequest, options);
                await WriteJson(context.Response, 200, prefabResult);
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " prefab " + prefabResult.PrefabPath + " images=" + prefabResult.ImageCount);
                return;
            }

            if (context.Request.Url?.AbsolutePath != "/export" || context.Request.HttpMethod != "POST")
            {
                await WriteJson(context.Response, 404, new { error = "Not found" });
                return;
            }

            using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();
            failedPayload = body;
            failedKind = "asset";
            ExportRequest? request = JsonSerializer.Deserialize<ExportRequest>(body, JsonOptions.Default);
            if (request == null)
            {
                await WriteJson(context.Response, 400, new { error = "Invalid JSON" });
                return;
            }

            ExportResult result = ApplyExport(request, options);
            await WriteJson(context.Response, 200, result);
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " exported " + result.AssetPath + " bytes=" + result.Bytes);
        }
        catch (Exception ex)
        {
            string failedRequestPath = string.IsNullOrWhiteSpace(failedPayload)
                ? string.Empty
                : SaveFailedRequest(options.ProjectRoot, failedKind, failedPayload, ex);
            await WriteJson(context.Response, 500, new { error = ex.Message, failedRequestPath });
            Console.Error.WriteLine(ex);
        }
    }

    private static string SaveFailedRequest(string projectRoot, string kind, string payload, Exception ex)
    {
        string folder = Path.Combine(projectRoot, "Logs", "FigmaUnityBridge_FailedRequests");
        Directory.CreateDirectory(folder);
        string safeKind = string.IsNullOrWhiteSpace(kind) ? "request" : Regex.Replace(kind, @"[^0-9a-zA-Z_-]", "_");
        string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + safeKind + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            error = ex.Message,
            exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            payload,
        }, JsonOptions.Pretty), Encoding.UTF8);
        return ToAssetOrProjectRelativePath(projectRoot, path);
    }

    private static int ParseLimit(string? value, int fallback)
    {
        if (!int.TryParse(value, out int limit))
        {
            return fallback;
        }

        return Math.Clamp(limit, 1, 5000);
    }

    private static List<AssetItem> SearchAssets(string projectRoot, string query, int limit)
    {
        string assetsRoot = Path.Combine(projectRoot, "Assets");
        string normalizedQuery = NormalizeSearchText(query);
        string[] supportedExtensions = [".png", ".jpg", ".jpeg"];

        IEnumerable<string> files = Directory.EnumerateFiles(assetsRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => supportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            files = files.Where(path =>
            {
                string assetPath = ToAssetPath(projectRoot, path);
                string fileName = Path.GetFileNameWithoutExtension(path);
                return NormalizeSearchText(assetPath).Contains(normalizedQuery, StringComparison.Ordinal)
                    || NormalizeSearchText(fileName).Contains(normalizedQuery, StringComparison.Ordinal);
            });
        }

        return files
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(path => new AssetItem
            {
                AssetPath = ToAssetPath(projectRoot, path),
                Name = Path.GetFileNameWithoutExtension(path),
                Extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
            })
            .ToList();
    }

    private static string ToAssetPath(string projectRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(projectRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return relative.Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ToAssetOrProjectRelativePath(string projectRoot, string fullPath)
    {
        string normalizedFullPath = Path.GetFullPath(fullPath);
        string normalizedProjectRoot = Path.GetFullPath(projectRoot);
        if (normalizedFullPath.StartsWith(normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return ToAssetPath(projectRoot, normalizedFullPath);
        }

        return normalizedFullPath;
    }

    private static string NormalizeSearchText(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static PrefabExportResult ApplyPrefabExport(PrefabRequest request, BridgeOptions options)
    {
        if (request.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Prefab request has no nodes.");
        }

        PrefabNode root = request.Nodes[0];
        string rootName = SanitizeName(string.IsNullOrWhiteSpace(request.RootName) ? root.Name : request.RootName);
        string destinationFolder = string.IsNullOrWhiteSpace(request.DestinationFolder)
            ? "Assets/FigmaUnityExport"
            : request.DestinationFolder.Replace('\\', '/').TrimEnd('/');
        if (!destinationFolder.StartsWith("Assets/", StringComparison.Ordinal) && destinationFolder != "Assets")
        {
            throw new InvalidOperationException("destinationFolder must be under Assets/");
        }

        string sourceHash = ShortHash(string.Join("|", request.SourceFileKey, request.SourceFileName, request.SourcePageId, request.SourcePageName, rootName));
        string exportFolderAssetPath = destinationFolder + "/" + rootName + "_" + sourceHash;
        string exportFolderFullPath = SafeAssetFullPath(options.ProjectRoot, exportFolderAssetPath);
        bool isUpdate = Directory.Exists(exportFolderFullPath);
        string imageFolderFullPath = Path.Combine(exportFolderFullPath, "Sprites");
        string tempImageFolderFullPath = Path.Combine(exportFolderFullPath, "Sprites_tmp_" + NewUnityGuid()[..8]);
        Directory.CreateDirectory(tempImageFolderFullPath);

        Dictionary<string, string> spriteGuids = new(StringComparer.Ordinal);
        int imageIndex = 0;
        foreach (PrefabNode node in request.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.DataBase64))
            {
                continue;
            }

            byte[] bytes = Convert.FromBase64String(node.DataBase64);
            if (bytes.Length == 0)
            {
                continue;
            }

            imageIndex++;
            string fileName = imageIndex.ToString("000") + "_" + SanitizeName(node.Name) + ".png";
            string imagePath = Path.Combine(tempImageFolderFullPath, fileName);
            string guid = NewUnityGuid();
            File.WriteAllBytes(imagePath, bytes);
            File.WriteAllText(imagePath + ".meta", CreateSpriteMeta(guid), Encoding.UTF8);
            spriteGuids[node.Id] = guid;
        }

        ReplaceDirectoryRecoverable(imageFolderFullPath, tempImageFolderFullPath);

        UiModel uiModel = BuildUiModel(request.Nodes);
        FontExportContext fontContext = FontExportContext.Create(options.ProjectRoot, exportFolderAssetPath, request);
        IEngineExporter engineExporter = ResolveEngineExporter(request.ExportTarget);
        EngineExportArtifact artifact = engineExporter.Export(new EngineExportContext
        {
            ProjectRoot = options.ProjectRoot,
            RootName = rootName,
            ExportFolderAssetPath = exportFolderAssetPath,
            Model = uiModel,
            SpriteGuids = spriteGuids,
            FontContext = fontContext,
        });
        fontContext.WritePendingTasks(options.ProjectRoot);
        string reportAssetPath = exportFolderAssetPath + "/figma_export_report.json";
        ExportReport report = CreateExportReport(uiModel, fontContext, imageIndex, isUpdate, engineExporter.Target);
        File.WriteAllText(SafeAssetFullPath(options.ProjectRoot, reportAssetPath), JsonSerializer.Serialize(report, JsonOptions.Pretty), Encoding.UTF8);
        File.WriteAllText(SafeAssetFullPath(options.ProjectRoot, exportFolderAssetPath + "/figma_export_manifest.json"), JsonSerializer.Serialize(new
        {
            exportTarget = engineExporter.Target,
            request.SourceFileKey,
            request.SourceFileName,
            request.SourcePageId,
            request.SourcePageName,
            rootName,
            mainAssetPath = artifact.MainAssetPath,
            mainAssetKind = artifact.MainAssetKind,
            prefabAssetPath = artifact.PrefabAssetPath,
            reportAssetPath,
            updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        }, JsonOptions.Pretty), Encoding.UTF8);

        PrefabExportResult result = new()
        {
            ExportTarget = engineExporter.Target,
            MainAssetPath = artifact.MainAssetPath,
            MainAssetKind = artifact.MainAssetKind,
            PrefabPath = string.IsNullOrWhiteSpace(artifact.PrefabAssetPath) ? artifact.MainAssetPath : artifact.PrefabAssetPath,
            FolderPath = exportFolderAssetPath,
            ImageCount = imageIndex,
            NodeCount = request.Nodes.Count,
            IsUpdate = isUpdate,
            ReportPath = reportAssetPath,
            Report = report,
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        AppendLog(options.ProjectRoot, result);
        return result;
    }

    private static ExportReport CreateExportReport(UiModel model, FontExportContext fontContext, int imageCount, bool isUpdate, string exportTarget)
    {
        int textCount = model.Nodes.Count(IsTextNode);
        ExportReport report = new()
        {
            ExportTarget = exportTarget,
            IsUpdate = isUpdate,
            ImageCount = imageCount,
            TextCount = textCount,
            NodeCount = model.Nodes.Count,
            MenuCount = model.Menus.Count,
            StateGroupCount = model.StateGroups.Count,
            HiddenStateCount = model.Nodes.Count(node => IsStateNode(node) && !node.Visible),
            AutoButtons = model.AutoButtons,
            Menus = model.Menus,
            StateGroups = model.StateGroups,
            MissingFonts = fontContext.MissingFonts,
            GeneratedFonts = fontContext.GeneratedFonts,
            ReusedFonts = fontContext.ReusedFonts,
            GeneratedMaterials = fontContext.GeneratedMaterials,
            ReusedMaterials = fontContext.ReusedMaterials,
            Warnings = fontContext.Warnings,
        };

        if (model.Nodes.Any(node => node.Gradient != null))
        {
            AddUnique(report.Warnings, "Figma 文本渐变已近似映射为 TMP Vertex Gradient。");
        }

        if (model.Nodes.Any(node => node.Shadow != null && node.Shadow.Blur > 0))
        {
            AddUnique(report.Warnings, "Figma 文本阴影模糊已近似映射为 TMP Underlay Softness。");
        }

        foreach (MenuReport menu in report.Menus)
        {
            if (menu.Tabs.Count == 0)
            {
                AddUnique(report.Warnings, "Menu `" + menu.Name + "` has no tab_ children.");
            }

            foreach (TabReport tab in menu.Tabs)
            {
                if (!tab.HasNormal)
                {
                    AddUnique(report.Warnings, "Tab `" + tab.Name + "` is missing state_normal.");
                }

                if (!tab.HasSelected)
                {
                    AddUnique(report.Warnings, "Tab `" + tab.Name + "` is missing state_selected.");
                }
            }
        }

        return report;
    }

    private static void ReplaceDirectoryRecoverable(string targetFolderFullPath, string replacementFolderFullPath)
    {
        string? parent = Path.GetDirectoryName(targetFolderFullPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException("Invalid target folder: " + targetFolderFullPath);
        }

        string backupFolderFullPath = Path.Combine(parent, Path.GetFileName(targetFolderFullPath) + "_backup_" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        bool hasBackup = false;
        try
        {
            if (Directory.Exists(targetFolderFullPath))
            {
                Directory.Move(targetFolderFullPath, backupFolderFullPath);
                hasBackup = true;
            }

            Directory.Move(replacementFolderFullPath, targetFolderFullPath);
            if (hasBackup && Directory.Exists(backupFolderFullPath))
            {
                Directory.Delete(backupFolderFullPath, recursive: true);
            }
        }
        catch
        {
            if (Directory.Exists(targetFolderFullPath))
            {
                Directory.Delete(targetFolderFullPath, recursive: true);
            }

            if (hasBackup && Directory.Exists(backupFolderFullPath))
            {
                Directory.Move(backupFolderFullPath, targetFolderFullPath);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(replacementFolderFullPath))
            {
                Directory.Delete(replacementFolderFullPath, recursive: true);
            }
        }
    }

    private static string SafeAssetFullPath(string projectRoot, string assetPath)
    {
        if (assetPath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(assetPath))
        {
            throw new InvalidOperationException("Asset path is not safe: " + assetPath);
        }

        string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!fullPath.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes project root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static string CreateSpriteMeta(string guid)
    {
        return $$"""
fileFormatVersion: 2
guid: {{guid}}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  spritePackingTag: 
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""";
    }

    private static string CreatePrefabMeta(string guid)
    {
        return $$"""
fileFormatVersion: 2
guid: {{guid}}
PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""";
    }

    private static string CreateMaterialMeta(string guid)
    {
        return $$"""
fileFormatVersion: 2
guid: {{guid}}
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 2100000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""";
    }

    private static string SanitizeName(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "FigmaNode" : value.Trim();
        StringBuilder builder = new();
        foreach (char c in trimmed)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '[' || c == ']' ? '_' : c);
        }

        string result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "FigmaNode" : result;
    }

    private static string EscapeYaml(string value)
    {
        return value.Contains(':') || value.Contains('#') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
    }

    private static string NewUnityGuid()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string ShortHash(string value)
    {
        byte[] bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..10];
    }

    private static string UnityGuidFromHash(string value)
    {
        byte[] bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static string F(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HasTextShadow(PrefabNode node)
    {
        return node.Shadow != null && node.Shadow.Color.A > 0 && (Math.Abs(node.Shadow.OffsetX) > 0.001 || Math.Abs(node.Shadow.OffsetY) > 0.001 || node.Shadow.Blur > 0);
    }

    private static bool HasTextOutline(PrefabNode node)
    {
        return node.StrokeColor != null && node.StrokeColor.A > 0 && node.StrokeWeight > 0;
    }

    private sealed class FontExportContext
    {
        private readonly string projectRoot;
        private readonly string exportFolderAssetPath;
        private readonly string strategy;
        private readonly FontReference fallback;
        private readonly Dictionary<string, FontReference> cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FontGenerationTask> pendingTasks = [];
        private readonly List<TmpMaterialGenerationTask> pendingMaterialTasks = [];
        private readonly HashSet<string> pendingMaterialPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingFontPaths = new(StringComparer.OrdinalIgnoreCase);

        public List<string> MissingFonts { get; } = [];
        public List<string> GeneratedFonts { get; } = [];
        public List<string> ReusedFonts { get; } = [];
        public List<string> GeneratedMaterials { get; } = [];
        public List<string> ReusedMaterials { get; } = [];
        public List<string> Warnings { get; } = [];

        private FontExportContext(string projectRoot, string exportFolderAssetPath, PrefabRequest request)
        {
            this.projectRoot = projectRoot;
            this.exportFolderAssetPath = exportFolderAssetPath;
            strategy = string.IsNullOrWhiteSpace(request.FontStrategy) ? "unity-existing" : request.FontStrategy;
            fallback = ResolveByGuid(projectRoot, request.UnityFontGuid, DefaultTmpFontGuid);
        }

        public static FontExportContext Create(string projectRoot, string exportFolderAssetPath, PrefabRequest request)
        {
            return new FontExportContext(projectRoot, exportFolderAssetPath, request);
        }

        public FontReference Resolve(PrefabNode node)
        {
            FontReference fontReference;
            if (!strategy.Equals("figma-font", StringComparison.OrdinalIgnoreCase))
            {
                fontReference = fallback;
                return ResolveMaterial(node, fontReference);
            }

            string fontName = string.IsNullOrWhiteSpace(node.FontName) ? string.Empty : node.FontName.Trim();
            if (string.IsNullOrWhiteSpace(fontName))
            {
                fontReference = fallback;
                return ResolveMaterial(node, fontReference);
            }

            if (cache.TryGetValue(fontName, out FontReference? cached))
            {
                return ResolveMaterial(node, cached);
            }

            FontReference? resolvedFontReference = ResolveExistingSdf(projectRoot, fontName);
            if (resolvedFontReference != null)
            {
                fontReference = resolvedFontReference;
                AddUnique(ReusedFonts, fontName + " -> " + fontReference.FontAssetPath);
            }
            else
            {
                resolvedFontReference = QueueFontGeneration(fontName);
                if (resolvedFontReference == null)
                {
                    AddUnique(MissingFonts, fontName);
                    AddUnique(Warnings, "Missing Figma font `" + fontName + "`, fallback TMP font was used.");
                    fontReference = fallback;
                }
                else
                {
                    fontReference = resolvedFontReference;
                }
            }

            cache[fontName] = fontReference;
            return ResolveMaterial(node, fontReference);
        }

        public void WritePendingTasks(string projectRoot)
        {
            if (pendingTasks.Count == 0 && pendingMaterialTasks.Count == 0)
            {
                return;
            }

            string taskPath = SafeAssetFullPath(projectRoot, exportFolderAssetPath + "/FontTasks/figma_tmp_font_tasks.json");
            File.WriteAllText(taskPath, JsonSerializer.Serialize(new FontGenerationTaskEnvelope
            {
                Tasks = pendingTasks,
                MaterialTasks = pendingMaterialTasks,
            }, JsonOptions.Pretty), Encoding.UTF8);
        }

        private FontReference ResolveMaterial(PrefabNode node, FontReference fontReference)
        {
            if (!HasTextOutline(node) && !HasTextShadow(node))
            {
                return fontReference;
            }

            if (string.IsNullOrWhiteSpace(fontReference.FontAssetPath))
            {
                return fontReference;
            }

            TmpMaterialStyle style = TmpMaterialStyle.FromNode(node);
            string styleHash = ShortHash(fontReference.FontGuid + "|" + style.Signature);
            string targetAssetPath = "Assets/FigmaUnityExport/_SharedTmpMaterials/TMP_" + styleHash + ".mat";
            string targetFullPath = SafeAssetFullPath(projectRoot, targetAssetPath);
            string targetGuid = ReadMetaGuid(targetFullPath + ".meta") ?? UnityGuidFromHash("mat-guid|" + fontReference.FontGuid + "|" + style.Signature);
            if (!File.Exists(targetFullPath + ".meta"))
            {
                File.WriteAllText(targetFullPath + ".meta", CreateMaterialMeta(targetGuid), Encoding.UTF8);
            }

            if (File.Exists(targetFullPath))
            {
                AddUnique(ReusedMaterials, targetAssetPath);
            }
            else if (pendingMaterialPaths.Add(targetAssetPath))
            {
                pendingMaterialTasks.Add(new TmpMaterialGenerationTask
                {
                    FontAssetPath = fontReference.FontAssetPath,
                    TargetMaterialAssetPath = targetAssetPath,
                    TargetGuid = targetGuid,
                    OutlineColor = style.OutlineColor,
                    OutlineWidth = style.OutlineWidth,
                    UnderlayColor = style.UnderlayColor,
                    UnderlayOffsetX = style.UnderlayOffsetX,
                    UnderlayOffsetY = style.UnderlayOffsetY,
                    UnderlaySoftness = style.UnderlaySoftness,
                });
                AddUnique(GeneratedMaterials, targetAssetPath);
            }
            else
            {
                AddUnique(GeneratedMaterials, targetAssetPath);
            }

            return fontReference.WithSharedMaterial("{fileID: 2100000, guid: " + targetGuid + ", type: 2}");
        }

        private FontReference? QueueFontGeneration(string fontName)
        {
            string? sourceAssetPath = FindOrCopyFontSourceAssetPath(projectRoot, fontName);
            if (string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                return null;
            }

            string safeName = SanitizeName(RemoveFontStyleWords(fontName));
            string fontHash = ShortHash(fontName + "|" + sourceAssetPath);
            string targetAssetPath = "Assets/FigmaUnityExport/_SharedTmpFonts/" + safeName + "_" + fontHash + " SDF.asset";
            string targetFullPath = SafeAssetFullPath(projectRoot, targetAssetPath);
            string targetGuid = ReadMetaGuid(targetFullPath + ".meta") ?? UnityGuidFromHash("tmp-font|" + fontName + "|" + sourceAssetPath);
            if (!File.Exists(targetFullPath + ".meta"))
            {
                File.WriteAllText(targetFullPath + ".meta", CreateNativeAssetMeta(targetGuid), Encoding.UTF8);
            }

            if (File.Exists(targetFullPath))
            {
                AddUnique(ReusedFonts, fontName + " -> " + targetAssetPath);
            }
            else if (pendingFontPaths.Add(targetAssetPath))
            {
                pendingTasks.Add(new FontGenerationTask
                {
                    FontName = fontName,
                    SourceFontAssetPath = sourceAssetPath,
                    TargetFontAssetPath = targetAssetPath,
                    TargetGuid = targetGuid,
                });
                AddUnique(GeneratedFonts, fontName + " -> " + targetAssetPath);
            }
            else
            {
                AddUnique(GeneratedFonts, fontName + " -> " + targetAssetPath);
            }

            return new FontReference(targetGuid, "{fileID: 0}", targetAssetPath);
        }
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static FontReference ResolveByGuid(string projectRoot, string requestedGuid, string fallbackGuid)
    {
        string guid = IsUnityGuid(requestedGuid) ? requestedGuid : fallbackGuid;
        string materialYaml = TryGetTmpMaterialYaml(projectRoot, guid) ?? "{fileID: 0}";
        string? fontAssetFullPath = FindAssetPathByGuid(projectRoot, guid);
        return new FontReference(guid, materialYaml, fontAssetFullPath == null ? string.Empty : ToAssetPath(projectRoot, fontAssetFullPath));
    }

    private static FontReference? ResolveExistingSdf(string projectRoot, string fontName)
    {
        string normalizedFontName = NormalizeFontName(fontName);
        if (string.IsNullOrWhiteSpace(normalizedFontName))
        {
            return null;
        }

        string assetsRoot = Path.Combine(projectRoot, "Assets");
        foreach (string assetPath in Directory.EnumerateFiles(assetsRoot, "*SDF.asset", SearchOption.AllDirectories))
        {
            string assetName = Path.GetFileNameWithoutExtension(assetPath).Replace(" SDF", "", StringComparison.OrdinalIgnoreCase);
            string normalizedAssetName = NormalizeFontName(assetName);
            if (!normalizedFontName.Contains(normalizedAssetName, StringComparison.Ordinal)
                && !normalizedAssetName.Contains(normalizedFontName, StringComparison.Ordinal))
            {
                continue;
            }

            string? guid = ReadMetaGuid(assetPath + ".meta");
            if (string.IsNullOrWhiteSpace(guid))
            {
                continue;
            }

            return new FontReference(guid, TryGetTmpMaterialYaml(projectRoot, guid) ?? "{fileID: 0}", ToAssetPath(projectRoot, assetPath));
        }

        return null;
    }

    private static string? FindOrCopyFontSourceAssetPath(string projectRoot, string fontName)
    {
        string? sourceAssetPath = FindFontSourceAssetPath(projectRoot, fontName);
        if (!string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            return sourceAssetPath;
        }

        string? systemFontPath = FindSystemFontPath(fontName);
        if (string.IsNullOrWhiteSpace(systemFontPath) || !File.Exists(systemFontPath))
        {
            return null;
        }

        string extension = Path.GetExtension(systemFontPath);
        string safeName = SanitizeName(RemoveFontStyleWords(fontName));
        string sourceHash = ShortHash(fontName + "|" + systemFontPath + "|" + File.GetLastWriteTimeUtc(systemFontPath).Ticks + "|" + new FileInfo(systemFontPath).Length);
        string targetAssetPath = "Assets/FigmaUnityExport/_SharedTmpFonts/Source/" + safeName + "_" + sourceHash + extension.ToLowerInvariant();
        string targetFullPath = SafeAssetFullPath(projectRoot, targetAssetPath);
        if (!File.Exists(targetFullPath))
        {
            File.Copy(systemFontPath, targetFullPath, overwrite: true);
        }

        return targetAssetPath;
    }

    private static string? FindFontSourceAssetPath(string projectRoot, string fontName)
    {
        string normalizedFontName = NormalizeFontName(fontName);
        string assetsRoot = Path.Combine(projectRoot, "Assets");
        foreach (string path in Directory.EnumerateFiles(assetsRoot, "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalizedAssetName = NormalizeFontName(Path.GetFileNameWithoutExtension(path));
            if (normalizedFontName.Contains(normalizedAssetName, StringComparison.Ordinal)
                || normalizedAssetName.Contains(normalizedFontName, StringComparison.Ordinal))
            {
                return ToAssetPath(projectRoot, path);
            }
        }

        return null;
    }

    private static string? FindSystemFontPath(string fontName)
    {
        string normalizedFontName = NormalizeFontName(fontName);
        if (string.IsNullOrWhiteSpace(normalizedFontName))
        {
            return null;
        }

        foreach (SystemFontCandidate candidate in EnumerateSystemFontCandidates())
        {
            string normalizedDisplayName = NormalizeFontName(candidate.DisplayName);
            string normalizedFileName = NormalizeFontName(Path.GetFileNameWithoutExtension(candidate.Path));
            if (normalizedFontName.Contains(normalizedDisplayName, StringComparison.Ordinal)
                || normalizedDisplayName.Contains(normalizedFontName, StringComparison.Ordinal)
                || normalizedFontName.Contains(normalizedFileName, StringComparison.Ordinal)
                || normalizedFileName.Contains(normalizedFontName, StringComparison.Ordinal))
            {
                return candidate.Path;
            }
        }

        return null;
    }

    private static IEnumerable<SystemFontCandidate> EnumerateSystemFontCandidates()
    {
        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);
        foreach (SystemFontCandidate candidate in EnumerateRegistryFonts(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts")))
        {
            if (yielded.Add(candidate.Path))
            {
                yield return candidate;
            }
        }

        foreach (SystemFontCandidate candidate in EnumerateRegistryFonts(Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts")))
        {
            if (yielded.Add(candidate.Path))
            {
                yield return candidate;
            }
        }

        foreach (string folder in GetSystemFontFolders())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (!IsFontSourceExtension(Path.GetExtension(path)) || !yielded.Add(path))
                {
                    continue;
                }

                yield return new SystemFontCandidate(Path.GetFileNameWithoutExtension(path), path);
            }
        }
    }

    private static IEnumerable<SystemFontCandidate> EnumerateRegistryFonts(RegistryKey? key)
    {
        if (key == null)
        {
            yield break;
        }

        using (key)
        {
            foreach (string valueName in key.GetValueNames())
            {
                string? value = key.GetValue(valueName) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string path = ResolveSystemFontPath(value);
                if (!File.Exists(path) || !IsFontSourceExtension(Path.GetExtension(path)))
                {
                    continue;
                }

                yield return new SystemFontCandidate(valueName, path);
            }
        }
    }

    private static string ResolveSystemFontPath(string value)
    {
        if (Path.IsPathRooted(value))
        {
            return value;
        }

        string windowsFontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string localFontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
        string windowsPath = Path.Combine(windowsFontFolder, value);
        if (File.Exists(windowsPath))
        {
            return windowsPath;
        }

        return Path.Combine(localFontFolder, value);
    }

    private static IEnumerable<string> GetSystemFontFolders()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
    }

    private static bool IsFontSourceExtension(string extension)
    {
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetTmpMaterialYaml(string projectRoot, string fontGuid)
    {
        string? assetPath = FindAssetPathByGuid(projectRoot, fontGuid);
        if (assetPath == null || !File.Exists(assetPath))
        {
            return null;
        }

        foreach (string line in File.ReadLines(assetPath, Encoding.UTF8))
        {
            Match match = Regex.Match(line, @"^\s*material:\s*\{fileID:\s*([^}]+)\}");
            if (match.Success)
            {
                return "{fileID: " + match.Groups[1].Value.Trim() + ", guid: " + fontGuid + ", type: 2}";
            }
        }

        return null;
    }

    private static string? FindAssetPathByGuid(string projectRoot, string guid)
    {
        string assetsRoot = Path.Combine(projectRoot, "Assets");
        foreach (string metaPath in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
        {
            string? metaGuid = ReadMetaGuid(metaPath);
            if (guid.Equals(metaGuid, StringComparison.OrdinalIgnoreCase))
            {
                return metaPath[..^5];
            }
        }

        return null;
    }

    private static string? ReadMetaGuid(string metaPath)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }

        foreach (string line in File.ReadLines(metaPath, Encoding.UTF8))
        {
            if (line.StartsWith("guid:", StringComparison.Ordinal))
            {
                return line["guid:".Length..].Trim();
            }
        }

        return null;
    }

    private static bool IsUnityGuid(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), "^[0-9a-fA-F]{32}$");
    }

    private static string NormalizeFontName(string value)
    {
        string normalized = RemoveFontStyleWords(value).ToLowerInvariant();
        StringBuilder builder = new();
        foreach (char c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string RemoveFontStyleWords(string value)
    {
        return Regex.Replace(value, @"\b(regular|normal|medium|bold|semibold|semi\s*bold|black|light|thin|italic|oblique)\b", "", RegexOptions.IgnoreCase).Trim();
    }

    private static string CreateNativeAssetMeta(string guid)
    {
        return $$"""
fileFormatVersion: 2
guid: {{guid}}
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""";
    }

    private static ExportResult ApplyExport(ExportRequest request, BridgeOptions options)
    {
        if (string.IsNullOrWhiteSpace(request.AssetPath) || !request.AssetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("assetPath must start with Assets/");
        }

        if (request.AssetPath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(request.AssetPath))
        {
            throw new InvalidOperationException("assetPath is not safe: " + request.AssetPath);
        }

        byte[] bytes = Convert.FromBase64String(request.DataBase64 ?? string.Empty);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Image payload is empty.");
        }

        string targetPath = Path.GetFullPath(Path.Combine(options.ProjectRoot, request.AssetPath.Replace('/', Path.DirectorySeparatorChar)));
        string projectRoot = Path.GetFullPath(options.ProjectRoot);
        if (!targetPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escapes project root.");
        }

        string ext = Path.GetExtension(targetPath);
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only png/jpg/jpeg assets are supported.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string backupRoot = Path.Combine(options.ProjectRoot, "FigmaUnityBridge_Backups", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(backupRoot);

        string backupPath = Path.Combine(backupRoot, request.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(targetPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(targetPath, backupPath, overwrite: true);
            string metaPath = targetPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Copy(metaPath, backupPath + ".meta", overwrite: true);
            }
        }

        File.WriteAllBytes(targetPath, bytes);
        ExportResult result = new()
        {
            AssetPath = request.AssetPath,
            TargetPath = targetPath,
            BackupPath = File.Exists(backupPath) ? backupPath : string.Empty,
            Bytes = bytes.Length,
            NodeId = request.NodeId ?? string.Empty,
            NodeName = request.NodeName ?? string.Empty,
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        AppendLog(options.ProjectRoot, result);
        return result;
    }

    private static void AppendLog(string projectRoot, ExportResult result)
    {
        AppendLog(projectRoot, (object)result);
    }

    private static void AppendLog(string projectRoot, object result)
    {
        string logsDir = Path.Combine(projectRoot, "Logs");
        Directory.CreateDirectory(logsDir);
        string path = Path.Combine(logsDir, "figma_unity_bridge_exports_" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl");
        File.AppendAllText(path, JsonSerializer.Serialize(result, JsonOptions.Default) + Environment.NewLine, Encoding.UTF8);
    }

    private static async Task WriteJson(HttpListenerResponse response, int statusCode, object value)
    {
        WriteCors(response);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions.Default));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static void WriteCors(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private sealed class BridgeOptions
    {
        public string ProjectRoot { get; set; } = Directory.GetCurrentDirectory();
        public string Prefix { get; set; } = DefaultPrefix;

        public static BridgeOptions Parse(string[] args)
        {
            BridgeOptions options = new();
            if (args.Length == 1 && args[0].StartsWith(ProtocolName + "://", StringComparison.OrdinalIgnoreCase))
            {
                ApplyProtocolUri(options, args[0]);
                return options;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--project" && i + 1 < args.Length)
                {
                    options.ProjectRoot = args[++i];
                }
                else if (args[i] == "--prefix" && i + 1 < args.Length)
                {
                    options.Prefix = args[++i];
                    if (!options.Prefix.EndsWith("/", StringComparison.Ordinal))
                    {
                        options.Prefix += "/";
                    }
                }
            }

            options.ProjectRoot = Path.GetFullPath(options.ProjectRoot);
            return options;
        }

        private static void ApplyProtocolUri(BridgeOptions options, string uriText)
        {
            Uri uri = new(uriText);
            Dictionary<string, string> query = HttpUtility.ParseQueryString(uri.Query)
                .AllKeys
                .Where(key => key != null)
                .ToDictionary(key => key!, key => HttpUtility.ParseQueryString(uri.Query)[key!] ?? string.Empty);

            if (query.TryGetValue("project", out string? project) && !string.IsNullOrWhiteSpace(project))
            {
                options.ProjectRoot = project;
            }

            if (query.TryGetValue("prefix", out string? prefix) && !string.IsNullOrWhiteSpace(prefix))
            {
                options.Prefix = prefix;
            }

            if (!options.Prefix.EndsWith("/", StringComparison.Ordinal))
            {
                options.Prefix += "/";
            }

            options.ProjectRoot = Path.GetFullPath(options.ProjectRoot);
        }
    }

    private sealed class ExportRequest
    {
        public string AssetPath { get; set; } = string.Empty;
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }
        public string? Format { get; set; }
        public string? DataBase64 { get; set; }
    }

    private sealed class PrefabRequest
    {
        public string RootName { get; set; } = string.Empty;
        public string DestinationFolder { get; set; } = "Assets/FigmaUnityExport";
        public string ExportTarget { get; set; } = UnityExportTarget;
        public string SourceFileKey { get; set; } = string.Empty;
        public string SourceFileName { get; set; } = string.Empty;
        public string SourcePageId { get; set; } = string.Empty;
        public string SourcePageName { get; set; } = string.Empty;
        public string FontStrategy { get; set; } = "unity-existing";
        public string UnityFontGuid { get; set; } = string.Empty;
        public List<PrefabNode> Nodes { get; set; } = [];
    }

    private sealed class PrefabNode
    {
        public string Id { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string NodeType { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
        public string ConstraintH { get; set; } = "CENTER";
        public string ConstraintV { get; set; } = "CENTER";
        public string Text { get; set; } = string.Empty;
        public double FontSize { get; set; } = 24;
        public string FontName { get; set; } = string.Empty;
        public UnityColor? Color { get; set; }
        public TextGradient? Gradient { get; set; }
        public UnityColor? StrokeColor { get; set; }
        public double StrokeWeight { get; set; }
        public TextShadow? Shadow { get; set; }
        public string TextAlignH { get; set; } = "CENTER";
        public string TextAlignV { get; set; } = "CENTER";
        public bool WordWrap { get; set; }
        public string? DataBase64 { get; set; }
    }

    private sealed class PrefabExportResult
    {
        public string ExportTarget { get; set; } = UnityExportTarget;
        public string MainAssetPath { get; set; } = string.Empty;
        public string MainAssetKind { get; set; } = string.Empty;
        public string PrefabPath { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public int ImageCount { get; set; }
        public int NodeCount { get; set; }
        public bool IsUpdate { get; set; }
        public string ReportPath { get; set; } = string.Empty;
        public ExportReport Report { get; set; } = new();
        public string ExportedAt { get; set; } = string.Empty;
    }

    private sealed class ExportReport
    {
        public string ExportTarget { get; set; } = UnityExportTarget;
        public bool IsUpdate { get; set; }
        public int ImageCount { get; set; }
        public int TextCount { get; set; }
        public int NodeCount { get; set; }
        public int MenuCount { get; set; }
        public int StateGroupCount { get; set; }
        public int HiddenStateCount { get; set; }
        public List<MenuReport> Menus { get; set; } = [];
        public List<StateGroupReport> StateGroups { get; set; } = [];
        public List<ButtonReport> AutoButtons { get; set; } = [];
        public List<string> MissingFonts { get; set; } = [];
        public List<string> GeneratedFonts { get; set; } = [];
        public List<string> ReusedFonts { get; set; } = [];
        public List<string> GeneratedMaterials { get; set; } = [];
        public List<string> ReusedMaterials { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    private sealed class UnityColor
    {
        public double R { get; set; } = 1;
        public double G { get; set; } = 1;
        public double B { get; set; } = 1;
        public double A { get; set; } = 1;

        public static UnityColor White()
        {
            return new UnityColor();
        }
    }

    private sealed class TextGradient
    {
        public UnityColor TopLeft { get; set; } = UnityColor.White();
        public UnityColor TopRight { get; set; } = UnityColor.White();
        public UnityColor BottomLeft { get; set; } = UnityColor.White();
        public UnityColor BottomRight { get; set; } = UnityColor.White();
    }

    private sealed class TextShadow
    {
        public UnityColor Color { get; set; } = new() { R = 0, G = 0, B = 0, A = 0.5 };
        public double OffsetX { get; set; } = 1;
        public double OffsetY { get; set; } = 1;
        public double Blur { get; set; }
    }

    private sealed class TmpMaterialStyle
    {
        public UnityColor OutlineColor { get; private init; } = new() { A = 0 };
        public double OutlineWidth { get; private init; }
        public UnityColor UnderlayColor { get; private init; } = new() { R = 0, G = 0, B = 0, A = 0 };
        public double UnderlayOffsetX { get; private init; }
        public double UnderlayOffsetY { get; private init; }
        public double UnderlaySoftness { get; private init; }
        public string Signature { get; private init; } = string.Empty;

        public static TmpMaterialStyle FromNode(PrefabNode node)
        {
            UnityColor outlineColor = HasTextOutline(node) ? node.StrokeColor! : new UnityColor { A = 0 };
            double outlineWidth = HasTextOutline(node) ? Math.Clamp(node.StrokeWeight / Math.Max(1, node.FontSize), 0.001, 0.35) : 0;
            UnityColor underlayColor = HasTextShadow(node) ? node.Shadow!.Color : new UnityColor { A = 0 };
            double underlayOffsetX = HasTextShadow(node) ? Math.Clamp(node.Shadow!.OffsetX / Math.Max(1, node.FontSize), -1, 1) : 0;
            double underlayOffsetY = HasTextShadow(node) ? Math.Clamp(-node.Shadow!.OffsetY / Math.Max(1, node.FontSize), -1, 1) : 0;
            double underlaySoftness = HasTextShadow(node) ? Math.Clamp(node.Shadow!.Blur / Math.Max(1, node.FontSize), 0, 1) : 0;

            string signature = string.Join("|",
                ColorSignature(outlineColor),
                F(outlineWidth),
                ColorSignature(underlayColor),
                F(underlayOffsetX),
                F(underlayOffsetY),
                F(underlaySoftness));

            return new TmpMaterialStyle
            {
                OutlineColor = outlineColor,
                OutlineWidth = outlineWidth,
                UnderlayColor = underlayColor,
                UnderlayOffsetX = underlayOffsetX,
                UnderlayOffsetY = underlayOffsetY,
                UnderlaySoftness = underlaySoftness,
                Signature = signature,
            };
        }

        private static string ColorSignature(UnityColor color)
        {
            return F(color.R) + "," + F(color.G) + "," + F(color.B) + "," + F(color.A);
        }
    }

    private readonly record struct SystemFontCandidate(string DisplayName, string Path);

    private sealed record FontReference(string FontGuid, string SharedMaterialYaml, string FontAssetPath)
    {
        public FontReference WithSharedMaterial(string sharedMaterialYaml)
        {
            return this with { SharedMaterialYaml = sharedMaterialYaml };
        }
    }

    private sealed class FontGenerationTask
    {
        public string FontName { get; set; } = string.Empty;
        public string SourceFontAssetPath { get; set; } = string.Empty;
        public string TargetFontAssetPath { get; set; } = string.Empty;
        public string TargetGuid { get; set; } = string.Empty;
    }

    private sealed class FontGenerationTaskEnvelope
    {
        public List<FontGenerationTask> Tasks { get; set; } = [];
        public List<TmpMaterialGenerationTask> MaterialTasks { get; set; } = [];
    }

    private sealed class TmpMaterialGenerationTask
    {
        public string FontAssetPath { get; set; } = string.Empty;
        public string TargetMaterialAssetPath { get; set; } = string.Empty;
        public string TargetGuid { get; set; } = string.Empty;
        public UnityColor OutlineColor { get; set; } = new() { A = 0 };
        public double OutlineWidth { get; set; }
        public UnityColor UnderlayColor { get; set; } = new() { A = 0 };
        public double UnderlayOffsetX { get; set; }
        public double UnderlayOffsetY { get; set; }
        public double UnderlaySoftness { get; set; }
    }

    private sealed class AssetItem
    {
        public string AssetPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
    }

    private sealed class ExportResult
    {
        public string AssetPath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public int Bytes { get; set; }
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public string ExportedAt { get; set; } = string.Empty;
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        public static readonly JsonSerializerOptions Pretty = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }
}
