using System.Text;

namespace FigmaUnityBridge;

internal static partial class Program
{
    private sealed class UnityPrefabExporter : IEngineExporter
    {
        public string Target => UnityExportTarget;

        public EngineExportArtifact Export(EngineExportContext context)
        {
            string prefabAssetPath = context.ExportFolderAssetPath + "/" + context.RootName + ".prefab";
            string prefabFullPath = SafeAssetFullPath(context.ProjectRoot, prefabAssetPath);
            string prefabGuid = ReadMetaGuid(prefabFullPath + ".meta") ?? NewUnityGuid();
            File.WriteAllText(prefabFullPath, CreatePrefabYaml(context.Model, context.SpriteGuids, context.FontContext), Encoding.UTF8);
            if (!File.Exists(prefabFullPath + ".meta"))
            {
                File.WriteAllText(prefabFullPath + ".meta", CreatePrefabMeta(prefabGuid), Encoding.UTF8);
            }

            return new EngineExportArtifact
            {
                MainAssetPath = prefabAssetPath,
                MainAssetKind = "prefab",
                PrefabAssetPath = prefabAssetPath,
            };
        }

        private static string CreatePrefabYaml(UiModel model, Dictionary<string, string> spriteGuids, FontExportContext fontContext)
        {
            List<PrefabNode> nodes = model.Nodes;
            Dictionary<string, GeneratedIds> ids = new(StringComparer.Ordinal);
            long nextId = 100000000000000000;
            foreach (PrefabNode node in nodes)
            {
                ids[node.Id] = new GeneratedIds
                {
                    GameObject = nextId++,
                    RectTransform = nextId++,
                    CanvasRenderer = nextId++,
                    Image = nextId++,
                    Button = nextId++,
                    Text = nextId++,
                    MenuSwitcher = nextId++,
                    StateSwitcher = nextId++,
                };
            }

            Dictionary<string, List<PrefabNode>> childrenByParent = model.ChildrenByParent;
            Dictionary<string, PrefabNode> nodeById = model.NodeById;
            StringBuilder yaml = new();
            yaml.AppendLine("%YAML 1.1");
            yaml.AppendLine("%TAG !u! tag:unity3d.com,2011:");

            foreach (PrefabNode node in nodes)
            {
                GeneratedIds nodeIds = ids[node.Id];
                bool hasImage = spriteGuids.ContainsKey(node.Id);
                bool isText = IsTextNode(node);
                bool isButton = IsButtonNode(node);
                bool hasMenuSwitcher = IsMenuNode(node) && GetTabChildren(node, childrenByParent).Count > 0;
                bool hasStateSwitcher = GetStateChildren(node, childrenByParent).Count > 0;
                bool needsImage = hasImage || isButton;
                bool needsCanvasRenderer = needsImage || isText;
                yaml.AppendLine("--- !u!1 &" + nodeIds.GameObject);
                yaml.AppendLine("GameObject:");
                yaml.AppendLine("  m_ObjectHideFlags: 0");
                yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
                yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
                yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
                yaml.AppendLine("  serializedVersion: 6");
                yaml.AppendLine("  m_Component:");
                yaml.AppendLine("  - component: {fileID: " + nodeIds.RectTransform + "}");
                if (needsCanvasRenderer)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.CanvasRenderer + "}");
                }
                if (needsImage)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.Image + "}");
                }
                if (isButton)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.Button + "}");
                }
                if (isText)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.Text + "}");
                }
                if (hasMenuSwitcher)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.MenuSwitcher + "}");
                }
                if (hasStateSwitcher)
                {
                    yaml.AppendLine("  - component: {fileID: " + nodeIds.StateSwitcher + "}");
                }
                yaml.AppendLine("  m_Layer: 5");
                yaml.AppendLine("  m_Name: " + EscapeYaml(SanitizeName(node.Name)));
                yaml.AppendLine("  m_TagString: Untagged");
                yaml.AppendLine("  m_Icon: {fileID: 0}");
                yaml.AppendLine("  m_NavMeshLayer: 0");
                yaml.AppendLine("  m_StaticEditorFlags: 0");
                yaml.AppendLine("  m_IsActive: " + (node.Visible ? "1" : "0"));

                PrefabNode? parent = !string.IsNullOrWhiteSpace(node.ParentId) && nodeById.TryGetValue(node.ParentId, out PrefabNode? foundParent)
                    ? foundParent
                    : null;
                double anchoredX = parent == null ? 0 : CenterX(node) - CenterX(parent);
                double anchoredY = parent == null ? 0 : CenterY(parent) - CenterY(node);
                AnchorLayout anchorLayout = parent == null
                    ? AnchorLayout.Root()
                    : CreateAnchorLayout(node, parent);
                int rootOrder = parent == null || !childrenByParent.TryGetValue(parent.Id, out List<PrefabNode>? siblings)
                    ? 0
                    : Math.Max(0, siblings.FindIndex(item => item.Id == node.Id));

                yaml.AppendLine("--- !u!224 &" + nodeIds.RectTransform);
                yaml.AppendLine("RectTransform:");
                yaml.AppendLine("  m_ObjectHideFlags: 0");
                yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
                yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
                yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
                yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
                yaml.AppendLine("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}");
                yaml.AppendLine("  m_LocalPosition: {x: 0, y: 0, z: 0}");
                yaml.AppendLine("  m_LocalScale: {x: 1, y: 1, z: 1}");
                yaml.AppendLine("  m_ConstrainProportionsScale: 0");
                if (childrenByParent.TryGetValue(node.Id, out List<PrefabNode>? children) && children.Count > 0)
                {
                    yaml.AppendLine("  m_Children:");
                    foreach (PrefabNode child in children)
                    {
                        yaml.AppendLine("  - {fileID: " + ids[child.Id].RectTransform + "}");
                    }
                }
                else
                {
                    yaml.AppendLine("  m_Children: []");
                }
                yaml.AppendLine("  m_Father: {fileID: " + (parent == null ? 0 : ids[parent.Id].RectTransform) + "}");
                yaml.AppendLine("  m_RootOrder: " + rootOrder);
                yaml.AppendLine("  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}");
                yaml.AppendLine("  m_AnchorMin: {x: " + F(anchorLayout.AnchorMinX) + ", y: " + F(anchorLayout.AnchorMinY) + "}");
                yaml.AppendLine("  m_AnchorMax: {x: " + F(anchorLayout.AnchorMaxX) + ", y: " + F(anchorLayout.AnchorMaxY) + "}");
                yaml.AppendLine("  m_AnchoredPosition: {x: " + F(parent == null ? anchoredX : anchorLayout.AnchoredX) + ", y: " + F(parent == null ? anchoredY : anchorLayout.AnchoredY) + "}");
                yaml.AppendLine("  m_SizeDelta: {x: " + F(parent == null ? node.Width : anchorLayout.SizeDeltaX) + ", y: " + F(parent == null ? node.Height : anchorLayout.SizeDeltaY) + "}");
                yaml.AppendLine("  m_Pivot: {x: 0.5, y: 0.5}");

                if (needsCanvasRenderer)
                {
                    yaml.AppendLine("--- !u!222 &" + nodeIds.CanvasRenderer);
                    yaml.AppendLine("CanvasRenderer:");
                    yaml.AppendLine("  m_ObjectHideFlags: 0");
                    yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
                    yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
                    yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
                    yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
                    yaml.AppendLine("  m_CullTransparentMesh: 1");
                }

                if (needsImage)
                {
                    yaml.AppendLine("--- !u!114 &" + nodeIds.Image);
                    yaml.AppendLine("MonoBehaviour:");
                    yaml.AppendLine("  m_ObjectHideFlags: 0");
                    yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
                    yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
                    yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
                    yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
                    yaml.AppendLine("  m_Enabled: 1");
                    yaml.AppendLine("  m_EditorHideFlags: 0");
                    yaml.AppendLine("  m_Script: {fileID: 11500000, guid: " + ImageScriptGuid + ", type: 3}");
                    yaml.AppendLine("  m_Name: ");
                    yaml.AppendLine("  m_EditorClassIdentifier: ");
                    yaml.AppendLine("  m_Material: {fileID: 0}");
                    yaml.AppendLine("  m_Color: " + (hasImage ? "{r: 1, g: 1, b: 1, a: 1}" : "{r: 0, g: 0, b: 0, a: 0}"));
                    yaml.AppendLine("  m_RaycastTarget: " + (isButton ? "1" : "0"));
                    yaml.AppendLine("  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}");
                    yaml.AppendLine("  m_Maskable: 1");
                    yaml.AppendLine("  m_OnCullStateChanged:");
                    yaml.AppendLine("    m_PersistentCalls:");
                    yaml.AppendLine("      m_Calls: []");
                    yaml.AppendLine("  m_Sprite: " + (hasImage ? "{fileID: 21300000, guid: " + spriteGuids[node.Id] + ", type: 3}" : "{fileID: 0}"));
                    yaml.AppendLine("  m_Type: 0");
                    yaml.AppendLine("  m_PreserveAspect: 0");
                    yaml.AppendLine("  m_FillCenter: 1");
                    yaml.AppendLine("  m_FillMethod: 4");
                    yaml.AppendLine("  m_FillAmount: 1");
                    yaml.AppendLine("  m_FillClockwise: 1");
                    yaml.AppendLine("  m_FillOrigin: 0");
                    yaml.AppendLine("  m_UseSpriteMesh: 0");
                    yaml.AppendLine("  m_PixelsPerUnitMultiplier: 1");
                }

                if (isButton)
                {
                    AppendButtonYaml(yaml, nodeIds, nodeIds.Image);
                }

                if (isText)
                {
                    AppendTextMeshProYaml(yaml, nodeIds, node, fontContext.Resolve(node));
                }

                if (hasMenuSwitcher)
                {
                    AppendFigmaMenuSwitcherYaml(yaml, nodeIds, node, ids, childrenByParent);
                }

                if (hasStateSwitcher)
                {
                    AppendFigmaStateSwitcherYaml(yaml, nodeIds, node, ids, childrenByParent);
                }
            }

            return yaml.ToString();
        }


        private static double CenterX(PrefabNode node)
        {
            return node.X + node.Width / 2.0;
        }


        private static double CenterY(PrefabNode node)
        {
            return node.Y + node.Height / 2.0;
        }


        private static string ColorYaml(UnityColor color)
        {
            return "{r: " + F(color.R) + ", g: " + F(color.G) + ", b: " + F(color.B) + ", a: " + F(color.A) + "}";
        }


        private static AnchorLayout CreateAnchorLayout(PrefabNode node, PrefabNode parent)
        {
            double left = node.X - parent.X;
            double top = node.Y - parent.Y;
            double right = parent.Width - left - node.Width;
            double bottom = parent.Height - top - node.Height;
            double centerX = left + node.Width / 2.0 - parent.Width / 2.0;
            double centerY = parent.Height / 2.0 - top - node.Height / 2.0;

            AnchorLayout layout = new();
            ApplyHorizontalAnchor(layout, node.ConstraintH, parent.Width, left, right, centerX, node.Width);
            ApplyVerticalAnchor(layout, node.ConstraintV, parent.Height, top, bottom, centerY, node.Height);
            return layout;
        }

        private static void ApplyHorizontalAnchor(AnchorLayout layout, string value, double parentWidth, double left, double right, double centerX, double width)
        {
            string constraint = (value ?? "CENTER").ToUpperInvariant();
            if (constraint is "LEFT_RIGHT" or "STRETCH" or "SCALE" && parentWidth > 0)
            {
                layout.AnchorMinX = left / parentWidth;
                layout.AnchorMaxX = (parentWidth - right) / parentWidth;
                layout.AnchoredX = 0;
                layout.SizeDeltaX = 0;
                return;
            }

            if (constraint is "LEFT" or "MIN")
            {
                layout.AnchorMinX = 0;
                layout.AnchorMaxX = 0;
                layout.AnchoredX = left + width / 2.0;
                layout.SizeDeltaX = width;
                return;
            }

            if (constraint is "RIGHT" or "MAX")
            {
                layout.AnchorMinX = 1;
                layout.AnchorMaxX = 1;
                layout.AnchoredX = -(right + width / 2.0);
                layout.SizeDeltaX = width;
                return;
            }

            layout.AnchorMinX = 0.5;
            layout.AnchorMaxX = 0.5;
            layout.AnchoredX = centerX;
            layout.SizeDeltaX = width;
        }

        private static void ApplyVerticalAnchor(AnchorLayout layout, string value, double parentHeight, double top, double bottom, double centerY, double height)
        {
            string constraint = (value ?? "CENTER").ToUpperInvariant();
            if (constraint is "TOP_BOTTOM" or "STRETCH" or "SCALE" && parentHeight > 0)
            {
                layout.AnchorMinY = bottom / parentHeight;
                layout.AnchorMaxY = (parentHeight - top) / parentHeight;
                layout.AnchoredY = 0;
                layout.SizeDeltaY = 0;
                return;
            }

            if (constraint is "TOP" or "MIN")
            {
                layout.AnchorMinY = 1;
                layout.AnchorMaxY = 1;
                layout.AnchoredY = -(top + height / 2.0);
                layout.SizeDeltaY = height;
                return;
            }

            if (constraint is "BOTTOM" or "MAX")
            {
                layout.AnchorMinY = 0;
                layout.AnchorMaxY = 0;
                layout.AnchoredY = bottom + height / 2.0;
                layout.SizeDeltaY = height;
                return;
            }

            layout.AnchorMinY = 0.5;
            layout.AnchorMaxY = 0.5;
            layout.AnchoredY = centerY;
            layout.SizeDeltaY = height;
        }


        private static void AppendButtonYaml(StringBuilder yaml, GeneratedIds nodeIds, long targetGraphicId)
        {
            yaml.AppendLine("--- !u!114 &" + nodeIds.Button);
            yaml.AppendLine("MonoBehaviour:");
            yaml.AppendLine("  m_ObjectHideFlags: 0");
            yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
            yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
            yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
            yaml.AppendLine("  m_Enabled: 1");
            yaml.AppendLine("  m_EditorHideFlags: 0");
            yaml.AppendLine("  m_Script: {fileID: 11500000, guid: " + CustomButtonScriptGuid + ", type: 3}");
            yaml.AppendLine("  m_Name: ");
            yaml.AppendLine("  m_EditorClassIdentifier: ");
            yaml.AppendLine("  m_Navigation:");
            yaml.AppendLine("    m_Mode: 3");
            yaml.AppendLine("    m_WrapAround: 0");
            yaml.AppendLine("    m_SelectOnUp: {fileID: 0}");
            yaml.AppendLine("    m_SelectOnDown: {fileID: 0}");
            yaml.AppendLine("    m_SelectOnLeft: {fileID: 0}");
            yaml.AppendLine("    m_SelectOnRight: {fileID: 0}");
            yaml.AppendLine("  m_Transition: 1");
            yaml.AppendLine("  m_Colors:");
            yaml.AppendLine("    m_NormalColor: {r: 1, g: 1, b: 1, a: 1}");
            yaml.AppendLine("    m_HighlightedColor: {r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}");
            yaml.AppendLine("    m_PressedColor: {r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 1}");
            yaml.AppendLine("    m_SelectedColor: {r: 0.9607843, g: 0.9607843, b: 0.9607843, a: 1}");
            yaml.AppendLine("    m_DisabledColor: {r: 0.78431374, g: 0.78431374, b: 0.78431374, a: 0.5019608}");
            yaml.AppendLine("    m_ColorMultiplier: 1");
            yaml.AppendLine("    m_FadeDuration: 0.1");
            yaml.AppendLine("  m_SpriteState:");
            yaml.AppendLine("    m_HighlightedSprite: {fileID: 0}");
            yaml.AppendLine("    m_PressedSprite: {fileID: 0}");
            yaml.AppendLine("    m_SelectedSprite: {fileID: 0}");
            yaml.AppendLine("    m_DisabledSprite: {fileID: 0}");
            yaml.AppendLine("  m_AnimationTriggers:");
            yaml.AppendLine("    m_NormalTrigger: Normal");
            yaml.AppendLine("    m_HighlightedTrigger: Highlighted");
            yaml.AppendLine("    m_PressedTrigger: Pressed");
            yaml.AppendLine("    m_SelectedTrigger: Selected");
            yaml.AppendLine("    m_DisabledTrigger: Disabled");
            yaml.AppendLine("  m_Interactable: 1");
            yaml.AppendLine("  m_TargetGraphic: {fileID: " + targetGraphicId + "}");
            yaml.AppendLine("  m_OnClick:");
            yaml.AppendLine("    m_PersistentCalls:");
            yaml.AppendLine("      m_Calls: []");
        }


        private static void AppendTextMeshProYaml(StringBuilder yaml, GeneratedIds nodeIds, PrefabNode node, FontReference fontReference)
        {
            UnityColor color = node.Color ?? UnityColor.White();
            TextGradient? gradient = node.Gradient;
            int horizontalAlignment = TextHorizontalAlignment(node.TextAlignH);
            int verticalAlignment = TextVerticalAlignment(node.TextAlignV);
            int textAlignment = horizontalAlignment | verticalAlignment;
            uint rgba = ColorToRgba(color);

            yaml.AppendLine("--- !u!114 &" + nodeIds.Text);
            yaml.AppendLine("MonoBehaviour:");
            yaml.AppendLine("  m_ObjectHideFlags: 0");
            yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
            yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
            yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
            yaml.AppendLine("  m_Enabled: 1");
            yaml.AppendLine("  m_EditorHideFlags: 0");
            yaml.AppendLine("  m_Script: {fileID: 11500000, guid: " + TextMeshProScriptGuid + ", type: 3}");
            yaml.AppendLine("  m_Name: ");
            yaml.AppendLine("  m_EditorClassIdentifier: ");
            yaml.AppendLine("  m_Material: {fileID: 0}");
            yaml.AppendLine("  m_Color: {r: 1, g: 1, b: 1, a: 1}");
            yaml.AppendLine("  m_RaycastTarget: 0");
            yaml.AppendLine("  m_RaycastPadding: {x: 0, y: 0, z: 0, w: 0}");
            yaml.AppendLine("  m_Maskable: 1");
            yaml.AppendLine("  m_OnCullStateChanged:");
            yaml.AppendLine("    m_PersistentCalls:");
            yaml.AppendLine("      m_Calls: []");
            yaml.AppendLine("  m_text: " + EscapeYaml(node.Text ?? string.Empty));
            yaml.AppendLine("  m_isRightToLeft: 0");
            yaml.AppendLine("  m_fontAsset: {fileID: 11400000, guid: " + fontReference.FontGuid + ", type: 2}");
            yaml.AppendLine("  m_sharedMaterial: " + fontReference.SharedMaterialYaml);
            yaml.AppendLine("  m_fontSharedMaterials: []");
            yaml.AppendLine("  m_fontMaterial: {fileID: 0}");
            yaml.AppendLine("  m_fontMaterials: []");
            yaml.AppendLine("  m_fontColor32:");
            yaml.AppendLine("    serializedVersion: 2");
            yaml.AppendLine("    rgba: " + rgba);
            yaml.AppendLine("  m_fontColor: {r: " + F(color.R) + ", g: " + F(color.G) + ", b: " + F(color.B) + ", a: " + F(color.A) + "}");
            yaml.AppendLine("  m_enableVertexGradient: " + (gradient == null ? "0" : "1"));
            yaml.AppendLine("  m_colorMode: " + (gradient == null ? "3" : "2"));
            yaml.AppendLine("  m_fontColorGradient:");
            yaml.AppendLine("    topLeft: " + ColorYaml(gradient?.TopLeft ?? UnityColor.White()));
            yaml.AppendLine("    topRight: " + ColorYaml(gradient?.TopRight ?? UnityColor.White()));
            yaml.AppendLine("    bottomLeft: " + ColorYaml(gradient?.BottomLeft ?? UnityColor.White()));
            yaml.AppendLine("    bottomRight: " + ColorYaml(gradient?.BottomRight ?? UnityColor.White()));
            yaml.AppendLine("  m_fontColorGradientPreset: {fileID: 0}");
            yaml.AppendLine("  m_spriteAsset: {fileID: 0}");
            yaml.AppendLine("  m_tintAllSprites: 0");
            yaml.AppendLine("  m_StyleSheet: {fileID: 0}");
            yaml.AppendLine("  m_TextStyleHashCode: -1183493901");
            yaml.AppendLine("  m_overrideHtmlColors: 0");
            yaml.AppendLine("  m_faceColor:");
            yaml.AppendLine("    serializedVersion: 2");
            yaml.AppendLine("    rgba: " + rgba);
            yaml.AppendLine("  m_fontSize: " + F(node.FontSize <= 0 ? 24 : node.FontSize));
            yaml.AppendLine("  m_fontSizeBase: " + F(node.FontSize <= 0 ? 24 : node.FontSize));
            yaml.AppendLine("  m_fontWeight: 400");
            yaml.AppendLine("  m_enableAutoSizing: 0");
            yaml.AppendLine("  m_fontSizeMin: 18");
            yaml.AppendLine("  m_fontSizeMax: 120");
            yaml.AppendLine("  m_fontStyle: 0");
            yaml.AppendLine("  m_HorizontalAlignment: " + horizontalAlignment);
            yaml.AppendLine("  m_VerticalAlignment: " + verticalAlignment);
            yaml.AppendLine("  m_textAlignment: " + textAlignment);
            yaml.AppendLine("  m_characterSpacing: 0");
            yaml.AppendLine("  m_wordSpacing: 0");
            yaml.AppendLine("  m_lineSpacing: 0");
            yaml.AppendLine("  m_lineSpacingMax: 0");
            yaml.AppendLine("  m_paragraphSpacing: 0");
            yaml.AppendLine("  m_charWidthMaxAdj: 0");
            yaml.AppendLine("  m_enableWordWrapping: " + (node.WordWrap ? "1" : "0"));
            yaml.AppendLine("  m_wordWrappingRatios: 0.4");
            yaml.AppendLine("  m_overflowMode: 0");
            yaml.AppendLine("  m_linkedTextComponent: {fileID: 0}");
            yaml.AppendLine("  parentLinkedComponent: {fileID: 0}");
            yaml.AppendLine("  m_enableKerning: 1");
            yaml.AppendLine("  m_enableExtraPadding: 0");
            yaml.AppendLine("  checkPaddingRequired: 0");
            yaml.AppendLine("  m_isRichText: 1");
            yaml.AppendLine("  m_parseCtrlCharacters: 1");
            yaml.AppendLine("  m_isOrthographic: 1");
            yaml.AppendLine("  m_isCullingEnabled: 0");
            yaml.AppendLine("  m_horizontalMapping: 0");
            yaml.AppendLine("  m_verticalMapping: 0");
            yaml.AppendLine("  m_uvLineOffset: 0");
            yaml.AppendLine("  m_geometrySortingOrder: 0");
            yaml.AppendLine("  m_IsTextObjectScaleStatic: 0");
            yaml.AppendLine("  m_VertexBufferAutoSizeReduction: 0");
            yaml.AppendLine("  m_useMaxVisibleDescender: 1");
            yaml.AppendLine("  m_pageToDisplay: 1");
            yaml.AppendLine("  m_margin: {x: 0, y: 0, z: 0, w: 0}");
            yaml.AppendLine("  m_isUsingLegacyAnimationComponent: 0");
            yaml.AppendLine("  m_isVolumetricText: 0");
            yaml.AppendLine("  m_hasFontAssetChanged: 0");
            yaml.AppendLine("  m_baseMaterial: {fileID: 0}");
            yaml.AppendLine("  m_maskOffset: {x: 0, y: 0, z: 0, w: 0}");
        }


        private static void AppendFigmaMenuSwitcherYaml(
            StringBuilder yaml,
            GeneratedIds nodeIds,
            PrefabNode node,
            Dictionary<string, GeneratedIds> ids,
            Dictionary<string, List<PrefabNode>> childrenByParent)
        {
            List<PrefabNode> tabs = GetTabChildren(node, childrenByParent);
            PrefabNode? defaultTab = tabs.FirstOrDefault(tab => FindStateChild(tab, "selected", childrenByParent)?.Visible == true)
                ?? tabs.FirstOrDefault();
            string defaultKey = defaultTab == null ? string.Empty : KeyFromName(defaultTab.Name, "tab");

            yaml.AppendLine("--- !u!114 &" + nodeIds.MenuSwitcher);
            yaml.AppendLine("MonoBehaviour:");
            yaml.AppendLine("  m_ObjectHideFlags: 0");
            yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
            yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
            yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
            yaml.AppendLine("  m_Enabled: 1");
            yaml.AppendLine("  m_EditorHideFlags: 0");
            yaml.AppendLine("  m_Script: {fileID: 11500000, guid: " + FigmaMenuSwitcherScriptGuid + ", type: 3}");
            yaml.AppendLine("  m_Name: ");
            yaml.AppendLine("  m_EditorClassIdentifier: ");
            yaml.AppendLine("  defaultKey: " + EscapeYaml(defaultKey));
            yaml.AppendLine("  disableSelectedButton: 1");
            yaml.AppendLine("  items:");
            foreach (PrefabNode tab in tabs)
            {
                GeneratedIds tabIds = ids[tab.Id];
                PrefabNode? normal = FindStateChild(tab, "normal", childrenByParent);
                PrefabNode? selected = FindStateChild(tab, "selected", childrenByParent);
                yaml.AppendLine("  - key: " + EscapeYaml(KeyFromName(tab.Name, "tab")));
                yaml.AppendLine("    root: {fileID: " + tabIds.GameObject + "}");
                yaml.AppendLine("    button: {fileID: " + tabIds.Button + "}");
                yaml.AppendLine("    normalLayer: {fileID: " + (normal == null ? 0 : ids[normal.Id].GameObject) + "}");
                yaml.AppendLine("    selectedLayer: {fileID: " + (selected == null ? 0 : ids[selected.Id].GameObject) + "}");
            }
        }


        private static void AppendFigmaStateSwitcherYaml(
            StringBuilder yaml,
            GeneratedIds nodeIds,
            PrefabNode node,
            Dictionary<string, GeneratedIds> ids,
            Dictionary<string, List<PrefabNode>> childrenByParent)
        {
            List<PrefabNode> states = GetStateChildren(node, childrenByParent);
            yaml.AppendLine("--- !u!114 &" + nodeIds.StateSwitcher);
            yaml.AppendLine("MonoBehaviour:");
            yaml.AppendLine("  m_ObjectHideFlags: 0");
            yaml.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            yaml.AppendLine("  m_PrefabInstance: {fileID: 0}");
            yaml.AppendLine("  m_PrefabAsset: {fileID: 0}");
            yaml.AppendLine("  m_GameObject: {fileID: " + nodeIds.GameObject + "}");
            yaml.AppendLine("  m_Enabled: 1");
            yaml.AppendLine("  m_EditorHideFlags: 0");
            yaml.AppendLine("  m_Script: {fileID: 11500000, guid: " + FigmaStateSwitcherScriptGuid + ", type: 3}");
            yaml.AppendLine("  m_Name: ");
            yaml.AppendLine("  m_EditorClassIdentifier: ");
            yaml.AppendLine("  states:");
            foreach (PrefabNode state in states)
            {
                yaml.AppendLine("  - key: " + EscapeYaml(KeyFromName(state.Name, "state")));
                yaml.AppendLine("    layer: {fileID: " + ids[state.Id].GameObject + "}");
                yaml.AppendLine("    visibleByDefault: " + (state.Visible ? "1" : "0"));
            }
        }


        private static int TextHorizontalAlignment(string value)
        {
            return (value ?? string.Empty).ToUpperInvariant() switch
            {
                "LEFT" => 1,
                "RIGHT" => 4,
                "JUSTIFIED" => 8,
                _ => 2,
            };
        }

        private static int TextVerticalAlignment(string value)
        {
            return (value ?? string.Empty).ToUpperInvariant() switch
            {
                "TOP" => 256,
                "BOTTOM" => 1024,
                _ => 512,
            };
        }

        private static uint ColorToRgba(UnityColor color)
        {
            uint r = (uint)ClampByte(color.R);
            uint g = (uint)ClampByte(color.G);
            uint b = (uint)ClampByte(color.B);
            uint a = (uint)ClampByte(color.A);
            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        private static int ClampByte(double value)
        {
            return Math.Clamp((int)Math.Round(value * 255), 0, 255);
        }


        private sealed class GeneratedIds
        {
            public long GameObject { get; set; }
            public long RectTransform { get; set; }
            public long CanvasRenderer { get; set; }
            public long Image { get; set; }
            public long Button { get; set; }
            public long Text { get; set; }
            public long MenuSwitcher { get; set; }
            public long StateSwitcher { get; set; }
        }

        private sealed class AnchorLayout
        {
            public double AnchorMinX { get; set; }
            public double AnchorMinY { get; set; }
            public double AnchorMaxX { get; set; }
            public double AnchorMaxY { get; set; }
            public double AnchoredX { get; set; }
            public double AnchoredY { get; set; }
            public double SizeDeltaX { get; set; }
            public double SizeDeltaY { get; set; }

            public static AnchorLayout Root()
            {
                return new AnchorLayout
                {
                    AnchorMinX = 0.5,
                    AnchorMinY = 0.5,
                    AnchorMaxX = 0.5,
                    AnchorMaxY = 0.5,
                    AnchoredX = 0,
                    AnchoredY = 0,
                    SizeDeltaX = 0,
                    SizeDeltaY = 0,
                };
            }
        }

    }
}
