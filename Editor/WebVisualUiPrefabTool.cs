using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FUI.UGUI;
using FUI.UGUI.Control;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;

namespace FUI.Cli
{
    [UnityCliTool(
        "ui.web_to_ugui_prefab",
        Description = "Create a UGUI/FUI prefab from Web extracted visual UI JSON",
        Mode = ToolMode.EditOnly,
        Capabilities = ToolCapabilities.WriteAssets,
        Category = "ui")]
    public sealed class WebVisualUiPrefabTool : EditModeUnityCliTool<WebVisualUiPrefabTool.Parameters>
    {
        public override string Id => "ui.web_to_ugui_prefab";

        public class Parameters
        {
            [UnityCliParam("Web visual UI JSON 文件路径。")]
            public string json_file { get; set; }

            [UnityCliParam("目标 prefab 路径。", Required = false)]
            public string prefab_path { get; set; }

            [UnityCliParam("是否只预览不写入 prefab。", Required = false, DefaultValue = false)]
            public bool dry_run { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.json_file))
            {
                return ToolResult.Error("invalid_parameter", "json_file 参数不能为空。", new { parameter = "json_file" });
            }

            if (!TryNormalizeProjectPath(parameters.json_file, out var jsonPath, out var inputError))
            {
                return inputError;
            }

            var absoluteJsonPath = ToAbsoluteProjectPath(jsonPath);
            if (!File.Exists(absoluteJsonPath))
            {
                return ToolResult.Error("not_found", $"Web visual UI JSON 文件不存在: {jsonPath}。", new { jsonFile = jsonPath });
            }

            WebVisualUiPlan plan;
            try
            {
                var json = File.ReadAllText(absoluteJsonPath);
                plan = JsonUtility.FromJson<WebVisualUiPlan>(json);
            }
            catch (Exception exception)
            {
                return ToolResult.Error("invalid_json", $"JSON 解析失败: {exception.Message}");
            }

            if (plan == null || string.IsNullOrWhiteSpace(plan.viewName))
            {
                return ToolResult.Error("invalid_json", "JSON 中 viewName 不能为空。", new { field = "viewName" });
            }

            if (plan.referenceResolution == null || plan.referenceResolution.width <= 0 || plan.referenceResolution.height <= 0)
            {
                return ToolResult.Error("invalid_json", "referenceResolution.width/height 必须大于 0。", new { field = "referenceResolution" });
            }

            if (plan.nodes == null || plan.nodes.Count == 0)
            {
                return ToolResult.Error("invalid_json", "nodes 不能为空。", new { field = "nodes" });
            }

            var targetPrefabPath = string.IsNullOrWhiteSpace(parameters.prefab_path)
                ? $"Assets/Resources/UI/Prefabs/{SanitizeName(plan.viewName, "View")}.prefab"
                : parameters.prefab_path;
            if (!TryNormalizePrefabPath(targetPrefabPath, out var prefabPath, out inputError))
            {
                return inputError;
            }

            var result = WebVisualUiPrefabBuilder.Build(plan, prefabPath, parameters.dry_run);
            var response = new
            {
                ok = result.ok,
                toolId = Id,
                source = jsonPath,
                prefabPath,
                plan.viewName,
                parameters.dry_run,
                nodeCount = result.nodeCount,
                elementCount = result.elementCount,
                warnings = result.warnings.ToArray(),
                issues = result.issues.ToArray(),
                hierarchy = result.hierarchy.ToArray()
            };

            if (!result.ok)
            {
                var message = result.issues.Count > 0 ? result.issues[0].message : "Web visual UI prefab 生成失败。";
                var code = result.issues.Count > 0 ? result.issues[0].code : "web_visual_prefab_failed";
                return ToolResult.Error(code, message, response);
            }

            return ToolResult.Ok(response, parameters.dry_run ? "Web visual UI prefab dry-run 完成。" : "Web visual UI prefab 生成完成。");
        }

        static bool TryNormalizeProjectPath(string rawPath, out string normalizedPath, out ToolResult error)
        {
            normalizedPath = string.Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                error = ToolResult.Error("invalid_parameter", "路径不能为空。");
                return false;
            }

            var value = rawPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(value))
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') ?? string.Empty;
                if (!value.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    error = ToolResult.Error("path_not_allowed", "路径必须位于当前 Unity 项目内。", new { path = rawPath });
                    return false;
                }

                value = value.Substring(projectRoot.Length).TrimStart('/');
            }

            if (value.Contains(".."))
            {
                error = ToolResult.Error("path_not_allowed", "路径不能包含 '..'。", new { path = rawPath });
                return false;
            }

            normalizedPath = value;
            return true;
        }

        static bool TryNormalizePrefabPath(string rawPath, out string prefabPath, out ToolResult error)
        {
            prefabPath = string.Empty;
            if (!TryNormalizeProjectPath(rawPath, out var normalizedPath, out error))
            {
                return false;
            }

            if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) || !normalizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = ToolResult.Error("invalid_parameter", "prefab_path 必须是 Assets/ 下的 .prefab 路径。", new { prefabPath = rawPath });
                return false;
            }

            prefabPath = normalizedPath;
            return true;
        }

        static string ToAbsoluteProjectPath(string projectRelativePath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        static string SanitizeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var index = 0; index < chars.Length; index++)
            {
                if (Array.IndexOf(invalidChars, chars[index]) >= 0 || chars[index] == '/')
                {
                    chars[index] = '_';
                }
            }

            return new string(chars);
        }
    }

    [Serializable]
    public sealed class WebVisualUiPlan
    {
        public string schemaVersion = string.Empty;
        public string viewName = string.Empty;
        public WebReferenceResolution referenceResolution = new WebReferenceResolution();
        public string coordinateSystem = string.Empty;
        public List<WebVisualNode> nodes = new List<WebVisualNode>();
    }

    [Serializable]
    public sealed class WebReferenceResolution
    {
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class WebVisualNode
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public string webType = string.Empty;
        public string element = string.Empty;
        public WebVisualRect rect = new WebVisualRect();
        public WebVisualStyle style = new WebVisualStyle();
        public WebVisualText text = new WebVisualText();
        public WebVisualList list = new WebVisualList();
        public WebVisualTemplate template = new WebVisualTemplate();
        public WebVisualSlider slider = new WebVisualSlider();
        public WebVisualDropdown dropdown = new WebVisualDropdown();
        public WebVisualScrollbar scrollbar = new WebVisualScrollbar();
        public List<WebVisualNode> children = new List<WebVisualNode>();
    }

    [Serializable]
    public sealed class WebVisualList
    {
        public string layout = string.Empty;
        public string binding = string.Empty;
        public string itemView = string.Empty;
        public string rowView = string.Empty;
        public string scrollDirection = string.Empty;
        public string scrollMovement = string.Empty;
        public string scrollInertia = string.Empty;
        public string gridConstraint = string.Empty;
        public int gridCount;
        public float cellWidth;
        public float cellHeight;
        public float spacingX;
        public float spacingY;
        public float paddingLeft;
        public float paddingRight;
        public float paddingTop;
        public float paddingBottom;
    }

    [Serializable]
    public sealed class WebVisualTemplate
    {
        public string kind = string.Empty;
        public string view = string.Empty;
    }

    [Serializable]
    public sealed class WebVisualSlider
    {
        public float minValue;
        public float maxValue = 1f;
        public float value;
        public string direction = string.Empty;
        public string wholeNumbers = string.Empty;
    }

    [Serializable]
    public sealed class WebVisualDropdown
    {
        public string options = string.Empty;
        public int value;
    }

    [Serializable]
    public sealed class WebVisualScrollbar
    {
        public string direction = string.Empty;
        public float size = 1f;
        public float value;
    }

    [Serializable]
    public sealed class WebVisualRect
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class WebVisualStyle
    {
        public string color = string.Empty;
        public string textColor = string.Empty;
        public string sprite = string.Empty;
        public string imageType = string.Empty;
        public float alpha = 1f;
        public float opacity = 1f;
        public float borderRadius;
        public float contentWidth;
        public float contentHeight;
    }

    [Serializable]
    public sealed class WebVisualText
    {
        public string content = string.Empty;
        public float fontSize = 16f;
        public string fontWeight = string.Empty;
        public string color = string.Empty;
        public string alignment = string.Empty;
        public string overflow = string.Empty;
        public string truncate = string.Empty;
        public string bestFit = string.Empty;
    }

    sealed class WebVisualPrefabResult
    {
        public bool ok = true;
        public int nodeCount;
        public int elementCount;
        public readonly List<WebVisualPrefabIssue> warnings = new List<WebVisualPrefabIssue>();
        public readonly List<WebVisualPrefabIssue> issues = new List<WebVisualPrefabIssue>();
        public readonly List<object> hierarchy = new List<object>();
    }

    [Serializable]
    sealed class WebVisualPrefabIssue
    {
        public string code = string.Empty;
        public string message = string.Empty;
        public string nodePath = string.Empty;

        public static WebVisualPrefabIssue Create(string code, string message, string nodePath = "")
        {
            return new WebVisualPrefabIssue
            {
                code = code ?? string.Empty,
                message = message ?? string.Empty,
                nodePath = nodePath ?? string.Empty
            };
        }
    }

    static class WebVisualUiPrefabBuilder
    {
        public static WebVisualPrefabResult Build(WebVisualUiPlan plan, string prefabPath, bool dryRun)
        {
            var result = new WebVisualPrefabResult();
            GameObject root = null;
            try
            {
                root = CreateRoot(plan);
                var rootRect = new WebVisualRect
                {
                    x = 0f,
                    y = 0f,
                    width = plan.referenceResolution.width,
                    height = plan.referenceResolution.height
                };

                foreach (var node in GetRootChildren(plan))
                {
                    CreateNode(root.transform, node, rootRect, result, string.Empty, dryRun);
                }

                BuildHierarchy(root.transform, 0, result.hierarchy);

                if (!dryRun)
                {
                    EnsureFolderExists(prefabPath);
                    var savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                    if (!success || savedPrefab == null)
                    {
                        result.ok = false;
                        result.issues.Add(WebVisualPrefabIssue.Create("save_prefab_failed", $"保存 prefab 失败: {prefabPath}。"));
                        return result;
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                return result;
            }
            catch (Exception exception)
            {
                result.ok = false;
                result.issues.Add(WebVisualPrefabIssue.Create("web_visual_prefab_exception", exception.Message));
                return result;
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        static GameObject CreateRoot(WebVisualUiPlan plan)
        {
            var root = new GameObject(SanitizeName(plan.viewName, "View"), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.AddComponent<View>();

            var rectTransform = root.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(plan.referenceResolution.width, plan.referenceResolution.height);
            rectTransform.localScale = Vector3.one;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(plan.referenceResolution.width, plan.referenceResolution.height);
            scaler.matchWidthOrHeight = 0.5f;

            return root;
        }

        static IEnumerable<WebVisualNode> GetRootChildren(WebVisualUiPlan plan)
        {
            if (plan.nodes.Count == 1)
            {
                var candidate = plan.nodes[0];
                if (string.Equals(candidate.name, plan.viewName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.id, plan.viewName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.children ?? new List<WebVisualNode>();
                }
            }

            return plan.nodes;
        }

        static void CreateNode(Transform parent, WebVisualNode node, WebVisualRect parentRect, WebVisualPrefabResult result, string parentPath, bool dryRun)
        {
            if (node == null)
            {
                result.warnings.Add(WebVisualPrefabIssue.Create("null_node", "跳过空节点。", parentPath));
                return;
            }

            var nodeName = SanitizeName(string.IsNullOrWhiteSpace(node.name) ? node.id : node.name, "Node");
            var nodePath = string.IsNullOrWhiteSpace(parentPath) ? nodeName : $"{parentPath}/{nodeName}";
            var nodeObject = new GameObject(nodeName, typeof(RectTransform));
            nodeObject.transform.SetParent(parent, false);
            ApplyRect(nodeObject.GetComponent<RectTransform>(), node.rect, parentRect);
            ApplyElement(nodeObject, node, result, nodePath, dryRun);

            result.nodeCount++;
            var element = NormalizeElement(node.element);
            if (!string.Equals(element, "Container", StringComparison.Ordinal))
            {
                result.elementCount++;
            }

            if (node.children == null)
            {
                return;
            }

            var childParent = ResolveChildParent(nodeObject);
            var childParentRect = ResolveChildParentRect(node);
            var childIndex = 0;
            foreach (var child in ResolveChildrenToCreate(node, result, nodePath))
            {
                CreateNode(childParent, child, childParentRect, result, nodePath, dryRun);
                ConfigureListTemplateChild(nodeObject, node, childIndex);
                childIndex++;
            }

            ConfigureCompositeControlChildren(nodeObject, node);
        }

        static IEnumerable<WebVisualNode> ResolveChildrenToCreate(WebVisualNode node, WebVisualPrefabResult result, string nodePath)
        {
            if (!string.Equals(NormalizeElement(node.element), "ListView", StringComparison.Ordinal))
            {
                return node.children;
            }

            var children = node.children ?? new List<WebVisualNode>();
            if (children.Count <= 1)
            {
                return children;
            }

            result.warnings.Add(WebVisualPrefabIssue.Create(
                "listview_multiple_templates",
                "ListView 只会使用第一个子节点作为 item template；其它子节点应由运行时列表数据生成。",
                nodePath));
            return new[] { children[0] };
        }

        static void ApplyRect(RectTransform rectTransform, WebVisualRect rect, WebVisualRect parentRect)
        {
            rect = rect ?? new WebVisualRect();
            parentRect = parentRect ?? new WebVisualRect();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var rectCenterX = rect.x + rect.width * 0.5f;
            var rectCenterY = rect.y + rect.height * 0.5f;
            var parentCenterX = parentRect.x + parentRect.width * 0.5f;
            var parentCenterY = parentRect.y + parentRect.height * 0.5f;
            rectTransform.anchoredPosition = new Vector2(rectCenterX - parentCenterX, parentCenterY - rectCenterY);
            rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
            rectTransform.localScale = Vector3.one;
        }

        static void ApplyElement(GameObject nodeObject, WebVisualNode node, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            var element = NormalizeElement(node.element);
            switch (element)
            {
                case "Container":
                    if (!string.IsNullOrWhiteSpace(node.style?.color))
                    {
                        ConfigureImage(nodeObject, node.style, false, result, nodePath, dryRun);
                    }
                    break;
                case "ScrollView":
                    ConfigureScrollView(nodeObject, node);
                    break;
                case "ListView":
                    ConfigureListView(nodeObject, node);
                    break;
                case "Template":
                    ConfigureTemplate(nodeObject, node);
                    break;
                case "TextElement":
                    ConfigureText(nodeObject, node.text, node.style, false);
                    break;
                case "ButtonElement":
                    ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
                    var button = EnsureComponent<Button>(nodeObject);
                    button.targetGraphic = nodeObject.GetComponent<Image>();
                    EnsureComponent<ImageElement>(nodeObject);
                    EnsureComponent<ButtonElement>(nodeObject);
                    CreateTextChild(nodeObject.transform, "Label", node.text, true);
                    break;
                case "InputFieldElement":
                    ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
                    var inputField = EnsureComponent<InputField>(nodeObject);
                    EnsureComponent<InputFieldElement>(nodeObject);
                    var inputText = CreateTextChild(nodeObject.transform, "Text", node.text, false);
                    inputField.textComponent = inputText;
                    inputField.text = node.text == null ? string.Empty : node.text.content ?? string.Empty;
                    break;
                case "ToggleElement":
                    ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
                    var toggle = EnsureComponent<Toggle>(nodeObject);
                    toggle.targetGraphic = nodeObject.GetComponent<Image>();
                    EnsureComponent<ImageElement>(nodeObject);
                    EnsureComponent<ToggleElement>(nodeObject);
                    break;
                case "SliderElement":
                    ConfigureSlider(nodeObject, node, result, nodePath, dryRun);
                    break;
                case "DropdownElement":
                    ConfigureDropdown(nodeObject, node, result, nodePath, dryRun);
                    break;
                case "ScrollbarElement":
                    ConfigureScrollbar(nodeObject, node, result, nodePath, dryRun);
                    break;
                case "ImageElement":
                default:
                    ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
                    EnsureComponent<ImageElement>(nodeObject);
                    break;
            }

            if (node.style != null && node.style.borderRadius > 0f)
            {
                result.warnings.Add(WebVisualPrefabIssue.Create("border_radius_not_supported", "默认 UGUI Image 未精确支持 Web borderRadius，当前仅记录该值。", nodePath));
            }
        }

        static Transform ResolveChildParent(GameObject nodeObject)
        {
            var content = nodeObject.transform.Find("Viewport/Content");
            return content == null ? nodeObject.transform : content;
        }

        static WebVisualRect ResolveChildParentRect(WebVisualNode node)
        {
            var element = NormalizeElement(node.element);
            if (!string.Equals(element, "ScrollView", StringComparison.Ordinal)
                && !string.Equals(element, "ListView", StringComparison.Ordinal))
            {
                return node.rect;
            }

            var contentSize = ResolveScrollContentSize(node);
            return new WebVisualRect
            {
                x = node.rect.x,
                y = node.rect.y,
                width = contentSize.x,
                height = contentSize.y
            };
        }

        static string NormalizeElement(string element)
        {
            if (string.IsNullOrWhiteSpace(element))
            {
                return "ImageElement";
            }

            return element.Trim();
        }

        static void ConfigureImage(GameObject nodeObject, WebVisualStyle style, bool raycastTarget, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            var image = EnsureComponent<Image>(nodeObject);
            image.color = ParseColor(style == null ? string.Empty : style.color, Color.white, ResolveAlpha(style));
            image.raycastTarget = raycastTarget;
            image.type = ParseImageType(style == null ? string.Empty : style.imageType);

            var spritePath = NormalizeAssetPath(style == null ? string.Empty : style.sprite);
            if (!string.IsNullOrWhiteSpace(spritePath))
            {
                ValidateOrUpdateSpriteImporter(spritePath, style, result, nodePath, dryRun);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null)
                {
                    image.sprite = sprite;
                    image.preserveAspect = false;
                }
                else if (dryRun && AssetImporter.GetAtPath(spritePath) is TextureImporter)
                {
                    result.warnings.Add(WebVisualPrefabIssue.Create("sprite_importer_required", $"dry-run 未写入导入设置；正式生成时会尝试导入 Sprite: {spritePath}。", nodePath));
                }
                else
                {
                    result.ok = false;
                    result.issues.Add(WebVisualPrefabIssue.Create("sprite_not_found", $"找不到可用 Sprite: {spritePath}。", nodePath));
                }
            }
        }

        static void ValidateOrUpdateSpriteImporter(string spritePath, WebVisualStyle style, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            var importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            var imageType = ParseImageType(style == null ? string.Empty : style.imageType);
            if (dryRun)
            {
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    result.warnings.Add(WebVisualPrefabIssue.Create("sprite_importer_would_update", $"正式生成时会把资源导入为 Sprite: {spritePath}。", nodePath));
                }

                if (imageType == Image.Type.Sliced && importer.spriteBorder == Vector4.zero && ResolveSpriteBorder(style) == Vector4.zero)
                {
                    result.warnings.Add(WebVisualPrefabIssue.Create("sliced_sprite_border_missing", $"sliced 资源缺少 Sprite border: {spritePath}。", nodePath));
                }

                return;
            }

            var changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (imageType == Image.Type.Sliced)
            {
                var border = ResolveSpriteBorder(style);
                if (border != Vector4.zero && importer.spriteBorder != border)
                {
                    importer.spriteBorder = border;
                    changed = true;
                }
                else if (border == Vector4.zero && importer.spriteBorder == Vector4.zero)
                {
                    result.warnings.Add(WebVisualPrefabIssue.Create("sliced_sprite_border_missing", $"sliced 资源缺少 Sprite border: {spritePath}。", nodePath));
                }
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        static Vector4 ResolveSpriteBorder(WebVisualStyle style)
        {
            if (style == null || style.borderRadius <= 0f)
            {
                return Vector4.zero;
            }

            var horizontal = style.borderRadius;
            if (style.contentWidth > 0f)
            {
                horizontal = Mathf.Min(horizontal, style.contentWidth * 0.5f);
            }

            var vertical = style.borderRadius;
            if (style.contentHeight > 0f)
            {
                vertical = Mathf.Min(vertical, style.contentHeight * 0.5f);
            }

            horizontal = Mathf.Max(1f, horizontal);
            vertical = Mathf.Max(1f, vertical);
            return new Vector4(horizontal, vertical, horizontal, vertical);
        }

        static string NormalizeAssetPath(string rawPath)
        {
            return string.IsNullOrWhiteSpace(rawPath) ? string.Empty : rawPath.Trim().Replace('\\', '/');
        }

        static void ConfigureScrollView(GameObject nodeObject, WebVisualNode node)
        {
            var scrollRect = EnsureComponent<ScrollRect>(nodeObject);
            var list = node.list ?? new WebVisualList();
            var layout = NormalizeListLayout(list.layout);
            ConfigureScrollDirection(scrollRect, list, layout);
            ConfigureScrollBehavior(scrollRect, list);
            scrollRect.scrollSensitivity = 40f;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(nodeObject.transform, false);
            var viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0.5f, 0.5f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;

            var mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = ResolveScrollContentSize(node);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
        }

        static void ConfigureSlider(GameObject nodeObject, WebVisualNode node, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
            var slider = EnsureComponent<Slider>(nodeObject);
            var data = node.slider ?? new WebVisualSlider();
            slider.minValue = data.minValue;
            slider.maxValue = data.maxValue <= data.minValue ? data.minValue + 1f : data.maxValue;
            slider.wholeNumbers = ParseBoolean(data.wholeNumbers, false);
            slider.direction = ParseSliderDirection(data.direction);
            slider.value = Mathf.Clamp(data.value, slider.minValue, slider.maxValue);
            slider.targetGraphic = nodeObject.GetComponent<Image>();
            EnsureComponent<ImageElement>(nodeObject);
            EnsureComponent<SliderElement>(nodeObject);
        }

        static void ConfigureDropdown(GameObject nodeObject, WebVisualNode node, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
            var dropdown = EnsureComponent<Dropdown>(nodeObject);
            dropdown.targetGraphic = nodeObject.GetComponent<Image>();
            dropdown.options.Clear();
            foreach (var option in ParseDropdownOptions(node.dropdown == null ? string.Empty : node.dropdown.options))
            {
                dropdown.options.Add(new Dropdown.OptionData(option));
            }

            dropdown.value = Mathf.Clamp(node.dropdown == null ? 0 : node.dropdown.value, 0, Mathf.Max(0, dropdown.options.Count - 1));
            dropdown.RefreshShownValue();
            EnsureComponent<ImageElement>(nodeObject);
            EnsureComponent<DropdownElement>(nodeObject);
        }

        static void ConfigureScrollbar(GameObject nodeObject, WebVisualNode node, WebVisualPrefabResult result, string nodePath, bool dryRun)
        {
            ConfigureImage(nodeObject, node.style, true, result, nodePath, dryRun);
            var scrollbar = EnsureComponent<Scrollbar>(nodeObject);
            var data = node.scrollbar ?? new WebVisualScrollbar();
            scrollbar.direction = ParseScrollbarDirection(data.direction);
            scrollbar.size = NormalizeScrollbarSize(data.size, node.rect, scrollbar.direction);
            scrollbar.value = Mathf.Clamp01(data.value);
            scrollbar.targetGraphic = nodeObject.GetComponent<Image>();
            EnsureComponent<ImageElement>(nodeObject);
            EnsureComponent<ScrollbarElement>(nodeObject);
        }

        static void ConfigureListView(GameObject nodeObject, WebVisualNode node)
        {
            ConfigureScrollView(nodeObject, node);
            var scrollRect = nodeObject.GetComponent<ScrollRect>();
            var list = node.list ?? new WebVisualList();
            var layout = NormalizeListLayout(list.layout);
            ConfigureScrollDirection(scrollRect, list, layout);
            EnsureComponent<ScrollRectElement>(nodeObject);

            var content = scrollRect.content;
            if (content == null)
            {
                return;
            }

            ApplyContentAnchors(content, layout, scrollRect);
            ConfigureListLayout(content.gameObject, node, list, layout);
        }

        static void ConfigureTemplate(GameObject nodeObject, WebVisualNode node)
        {
            nodeObject.AddComponent<View>();
        }

        static void ConfigureListTemplateChild(GameObject nodeObject, WebVisualNode node, int childIndex)
        {
            if (!string.Equals(NormalizeElement(node.element), "ListView", StringComparison.Ordinal) || childIndex != 0)
            {
                return;
            }

            var content = nodeObject.transform.Find("Viewport/Content");
            if (content == null || content.childCount == 0)
            {
                return;
            }

            var template = content.GetChild(0).gameObject;
            if (template.GetComponent<View>() == null)
            {
                template.AddComponent<View>();
            }

            template.SetActive(false);
        }

        static void ConfigureCompositeControlChildren(GameObject nodeObject, WebVisualNode node)
        {
            var element = NormalizeElement(node.element);
            switch (element)
            {
                case "SliderElement":
                    ConfigureSliderChildReferences(nodeObject);
                    break;
                case "DropdownElement":
                    ConfigureDropdownChildReferences(nodeObject);
                    break;
                case "ScrollbarElement":
                    ConfigureScrollbarChildReferences(nodeObject);
                    break;
            }
        }

        static void ConfigureSliderChildReferences(GameObject nodeObject)
        {
            var slider = nodeObject.GetComponent<Slider>();
            if (slider == null)
            {
                return;
            }

            var fillRect = FindDescendantRect(nodeObject.transform, "fill");
            if (fillRect != null)
            {
                slider.fillRect = fillRect;
            }

            var handleRect = FindDescendantRect(nodeObject.transform, "handle", "knob");
            if (handleRect != null)
            {
                slider.handleRect = handleRect;
                var graphic = handleRect.GetComponent<Graphic>();
                if (graphic != null)
                {
                    slider.targetGraphic = graphic;
                }
            }
        }

        static void ConfigureDropdownChildReferences(GameObject nodeObject)
        {
            var dropdown = nodeObject.GetComponent<Dropdown>();
            if (dropdown == null)
            {
                return;
            }

            var captionText = FindDescendantComponent<Text>(nodeObject.transform, "label", "caption", "text");
            if (captionText == null && dropdown.options.Count > 0)
            {
                var selectedIndex = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
                var selectedText = dropdown.options[selectedIndex].text;
                captionText = CreateTextChild(nodeObject.transform, "Label", new WebVisualText
                {
                    content = selectedText,
                    fontSize = 16f,
                    alignment = "center"
                }, false);
            }

            dropdown.captionText = captionText;
            dropdown.RefreshShownValue();
        }

        static void ConfigureScrollbarChildReferences(GameObject nodeObject)
        {
            var scrollbar = nodeObject.GetComponent<Scrollbar>();
            if (scrollbar == null)
            {
                return;
            }

            var handleRect = FindDescendantRect(nodeObject.transform, "handle", "thumb");
            if (handleRect == null)
            {
                return;
            }

            scrollbar.handleRect = handleRect;
            var graphic = handleRect.GetComponent<Graphic>();
            if (graphic != null)
            {
                scrollbar.targetGraphic = graphic;
            }
        }

        static void ConfigureScrollDirection(ScrollRect scrollRect, WebVisualList list, string layout)
        {
            var direction = (list.scrollDirection ?? string.Empty).Trim().ToLowerInvariant();
            if (direction == "both")
            {
                scrollRect.horizontal = true;
                scrollRect.vertical = true;
                return;
            }

            if (direction == "horizontal" || layout == "horizontal")
            {
                scrollRect.horizontal = true;
                scrollRect.vertical = false;
                return;
            }

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
        }

        static void ConfigureScrollBehavior(ScrollRect scrollRect, WebVisualList list)
        {
            var movement = (list.scrollMovement ?? string.Empty).Trim().ToLowerInvariant();
            switch (movement)
            {
                case "elastic":
                    scrollRect.movementType = ScrollRect.MovementType.Elastic;
                    break;
                case "unrestricted":
                    scrollRect.movementType = ScrollRect.MovementType.Unrestricted;
                    break;
                case "clamped":
                case "":
                default:
                    scrollRect.movementType = ScrollRect.MovementType.Clamped;
                    break;
            }

            scrollRect.inertia = ParseBoolean(list.scrollInertia, true);
        }

        static void ConfigureListLayout(GameObject contentObject, WebVisualNode node, WebVisualList list, string layout)
        {
            switch (layout)
            {
                case "horizontal":
                    ConfigureHorizontalLayout(contentObject, list);
                    break;
                case "grid":
                    ConfigureGridLayout(contentObject, node, list);
                    break;
                case "mixed":
                case "vertical":
                default:
                    ConfigureVerticalLayout(contentObject, list);
                    break;
            }
        }

        static void ConfigureVerticalLayout(GameObject contentObject, WebVisualList list)
        {
            var layout = EnsureComponent<VerticalLayoutGroup>(contentObject);
            layout.padding = CreatePadding(list);
            layout.spacing = list.spacingY;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = EnsureComponent<ContentSizeFitter>(contentObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static void ConfigureHorizontalLayout(GameObject contentObject, WebVisualList list)
        {
            var layout = EnsureComponent<HorizontalLayoutGroup>(contentObject);
            layout.padding = CreatePadding(list);
            layout.spacing = list.spacingX;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = EnsureComponent<ContentSizeFitter>(contentObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        static void ConfigureGridLayout(GameObject contentObject, WebVisualNode node, WebVisualList list)
        {
            var layout = EnsureComponent<GridLayoutGroup>(contentObject);
            layout.padding = CreatePadding(list);
            layout.spacing = new Vector2(list.spacingX, list.spacingY);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = ParseGridConstraint(list.gridConstraint);
            layout.constraintCount = Mathf.Max(1, list.gridCount == 0 ? 1 : list.gridCount);
            layout.cellSize = ResolveGridCellSize(node, list);
            var fitter = EnsureComponent<ContentSizeFitter>(contentObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static void ApplyContentAnchors(RectTransform content, string layout, ScrollRect scrollRect)
        {
            if (layout == "horizontal" && scrollRect.horizontal && !scrollRect.vertical)
            {
                content.anchorMin = new Vector2(0f, 0.5f);
                content.anchorMax = new Vector2(0f, 0.5f);
                content.pivot = new Vector2(0f, 0.5f);
                return;
            }

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
        }

        static RectOffset CreatePadding(WebVisualList list)
        {
            return new RectOffset(
                Mathf.RoundToInt(Mathf.Max(0f, list.paddingLeft)),
                Mathf.RoundToInt(Mathf.Max(0f, list.paddingRight)),
                Mathf.RoundToInt(Mathf.Max(0f, list.paddingTop)),
                Mathf.RoundToInt(Mathf.Max(0f, list.paddingBottom)));
        }

        static GridLayoutGroup.Constraint ParseGridConstraint(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);
            return normalized == "fixedrowcount" ? GridLayoutGroup.Constraint.FixedRowCount : GridLayoutGroup.Constraint.FixedColumnCount;
        }

        static Vector2 ResolveGridCellSize(WebVisualNode node, WebVisualList list)
        {
            if (list.cellWidth > 0f && list.cellHeight > 0f)
            {
                return new Vector2(list.cellWidth, list.cellHeight);
            }

            var template = node.children != null && node.children.Count > 0 ? node.children[0] : null;
            if (template?.rect != null && template.rect.width > 0f && template.rect.height > 0f)
            {
                return new Vector2(template.rect.width, template.rect.height);
            }

            return new Vector2(Mathf.Max(1f, node.rect.width), Mathf.Max(1f, node.rect.height));
        }

        static string NormalizeListLayout(string layout)
        {
            var value = (layout ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
            switch (value)
            {
                case "horizontal":
                case "grid":
                case "mixed":
                    return value;
                case "mixed-row":
                    return "mixed";
                case "vertical":
                default:
                    return "vertical";
            }
        }

        static Vector2 ResolveScrollContentSize(WebVisualNode node)
        {
            var width = Mathf.Max(node.rect.width, node.style == null ? 0f : node.style.contentWidth);
            var height = Mathf.Max(node.rect.height, node.style == null ? 0f : node.style.contentHeight);
            if (node.children != null)
            {
                foreach (var child in node.children)
                {
                    if (child?.rect == null)
                    {
                        continue;
                    }

                    width = Mathf.Max(width, child.rect.x + child.rect.width - node.rect.x);
                    height = Mathf.Max(height, child.rect.y + child.rect.height - node.rect.y);
                }
            }

            return new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }

        static Text CreateTextChild(Transform parent, string name, WebVisualText text, bool center)
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);
            var rectTransform = labelObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = center ? Vector2.zero : new Vector2(28f, 0f);
            rectTransform.offsetMax = center ? Vector2.zero : new Vector2(-20f, 0f);
            return ConfigureText(labelObject, text, null, center);
        }

        static Text ConfigureText(GameObject nodeObject, WebVisualText text, WebVisualStyle style, bool forceCenter)
        {
            var textComponent = EnsureComponent<Text>(nodeObject);
            textComponent.text = text == null ? string.Empty : text.content ?? string.Empty;
            textComponent.fontSize = Mathf.Max(1, Mathf.RoundToInt(text == null ? 16f : text.fontSize));
            textComponent.color = ParseColor(ResolveTextColor(text, style), Color.white, 1f);
            textComponent.alignment = forceCenter ? TextAnchor.MiddleCenter : ParseTextAnchor(text == null ? string.Empty : text.alignment);
            textComponent.fontStyle = ParseFontStyle(text == null ? string.Empty : text.fontWeight);
            ApplyTextOverflow(textComponent, text);
            ApplyTextBestFit(textComponent, text);
            textComponent.raycastTarget = false;
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureComponent<LegacyTextAdapter>(nodeObject);
            EnsureComponent<TextElement>(nodeObject);
            return textComponent;
        }

        static void ApplyTextOverflow(Text textComponent, WebVisualText text)
        {
            if (text == null)
            {
                return;
            }

            var overflow = (text.overflow ?? string.Empty).Trim().ToLowerInvariant();
            if (overflow == "wrap")
            {
                textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else if (overflow == "overflow")
            {
                textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            var truncate = (text.truncate ?? string.Empty).Trim().ToLowerInvariant();
            if (truncate == "truncate")
            {
                textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }
            else if (truncate == "overflow")
            {
                textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        static void ApplyTextBestFit(Text textComponent, WebVisualText text)
        {
            var value = text == null ? string.Empty : text.bestFit ?? string.Empty;
            value = value.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (value == "false" || value == "0")
            {
                textComponent.resizeTextForBestFit = false;
                return;
            }

            textComponent.resizeTextForBestFit = true;
            textComponent.resizeTextMinSize = 1;
            textComponent.resizeTextMaxSize = Mathf.Max(1, textComponent.fontSize);
            if (value == "true" || value == "1")
            {
                return;
            }

            var separators = new[] { '-', ',', '，', ':', '~' };
            var parts = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSize))
            {
                maxSize = Mathf.Max(1, maxSize);
                if (textComponent.fontSize > maxSize)
                {
                    textComponent.fontSize = maxSize;
                }

                textComponent.resizeTextMaxSize = maxSize;
                return;
            }

            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
            {
                return;
            }

            textComponent.resizeTextMinSize = Mathf.Max(1, Mathf.Min(min, max));
            var resolvedMax = Mathf.Max(textComponent.resizeTextMinSize, Mathf.Max(min, max));
            if (textComponent.fontSize > resolvedMax)
            {
                textComponent.fontSize = resolvedMax;
            }

            textComponent.resizeTextMaxSize = resolvedMax;
        }

        static FontStyle ParseFontStyle(string fontWeight)
        {
            if (int.TryParse((fontWeight ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericWeight))
            {
                return numericWeight >= 700 ? FontStyle.Bold : FontStyle.Normal;
            }

            var value = (fontWeight ?? string.Empty).Trim().ToLowerInvariant();
            return value == "bold" || value == "bolder" ? FontStyle.Bold : FontStyle.Normal;
        }

        static string ResolveTextColor(WebVisualText text, WebVisualStyle style)
        {
            if (text != null && !string.IsNullOrWhiteSpace(text.color))
            {
                return text.color;
            }

            if (style != null && !string.IsNullOrWhiteSpace(style.textColor))
            {
                return style.textColor;
            }

            return string.Empty;
        }

        static TextAnchor ParseTextAnchor(string alignment)
        {
            switch ((alignment ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "center":
                    return TextAnchor.MiddleCenter;
                case "right":
                case "end":
                    return TextAnchor.MiddleRight;
                case "left":
                case "start":
                default:
                    return TextAnchor.MiddleLeft;
            }
        }

        static Image.Type ParseImageType(string imageType)
        {
            var value = (imageType ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "sliced":
                    return Image.Type.Sliced;
                case "tiled":
                    return Image.Type.Tiled;
                case "filled":
                    return Image.Type.Filled;
                case "simple":
                case "":
                default:
                    return Image.Type.Simple;
            }
        }

        static Slider.Direction ParseSliderDirection(string direction)
        {
            var value = NormalizeEnumLikeValue(direction);
            switch (value)
            {
                case "righttoleft":
                    return Slider.Direction.RightToLeft;
                case "bottomtotop":
                    return Slider.Direction.BottomToTop;
                case "toptobottom":
                    return Slider.Direction.TopToBottom;
                case "lefttoright":
                default:
                    return Slider.Direction.LeftToRight;
            }
        }

        static Scrollbar.Direction ParseScrollbarDirection(string direction)
        {
            var value = NormalizeEnumLikeValue(direction);
            switch (value)
            {
                case "horizontal":
                case "lefttoright":
                    return Scrollbar.Direction.LeftToRight;
                case "righttoleft":
                    return Scrollbar.Direction.RightToLeft;
                case "vertical":
                case "bottomtotop":
                    return Scrollbar.Direction.BottomToTop;
                case "toptobottom":
                    return Scrollbar.Direction.TopToBottom;
                default:
                    return Scrollbar.Direction.LeftToRight;
            }
        }

        static string NormalizeEnumLikeValue(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty);
        }

        static bool ParseBoolean(string value, bool fallback)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "true":
                case "1":
                case "yes":
                    return true;
                case "false":
                case "0":
                case "no":
                    return false;
                case "":
                default:
                    return fallback;
            }
        }

        static IEnumerable<string> ParseDropdownOptions(string options)
        {
            var separators = new[] { ',', '，', '|', '\n', '\r' };
            foreach (var part in (options ?? string.Empty).Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }

        static float NormalizeScrollbarSize(float value, WebVisualRect rect, Scrollbar.Direction direction)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            if (value <= 1f)
            {
                return Mathf.Clamp01(value);
            }

            var vertical = direction == Scrollbar.Direction.BottomToTop || direction == Scrollbar.Direction.TopToBottom;
            var trackSize = vertical ? rect.height : rect.width;
            if (trackSize <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(value / trackSize);
        }

        static RectTransform FindDescendantRect(Transform root, params string[] nameTokens)
        {
            var transform = FindDescendant(root, nameTokens);
            return transform == null ? null : transform.GetComponent<RectTransform>();
        }

        static T FindDescendantComponent<T>(Transform root, params string[] nameTokens) where T : Component
        {
            var transform = FindDescendant(root, nameTokens);
            return transform == null ? null : transform.GetComponent<T>();
        }

        static Transform FindDescendant(Transform root, params string[] nameTokens)
        {
            if (root == null)
            {
                return null;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (NameContainsAny(child.name, nameTokens))
                {
                    return child;
                }

                var nested = FindDescendant(child, nameTokens);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        static bool NameContainsAny(string name, params string[] tokens)
        {
            var normalizedName = (name ?? string.Empty).ToLowerInvariant();
            foreach (var token in tokens)
            {
                var normalizedToken = (token ?? string.Empty).ToLowerInvariant();
                if (!string.IsNullOrEmpty(normalizedToken) && normalizedName.Contains(normalizedToken))
                {
                    return true;
                }
            }

            return false;
        }

        static float ResolveAlpha(WebVisualStyle style)
        {
            if (style == null)
            {
                return 1f;
            }

            return Mathf.Clamp01(style.alpha * style.opacity);
        }

        static Color ParseColor(string value, Color fallback, float alpha)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                fallback.a = alpha;
                return fallback;
            }

            if (ColorUtility.TryParseHtmlString(value.Trim(), out var color))
            {
                color.a = alpha;
                return color;
            }

            var parts = value.Split(',');
            if (parts.Length >= 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                return new Color(r, g, b, alpha);
            }

            fallback.a = alpha;
            return fallback;
        }

        static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            var component = gameObject.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            return gameObject.AddComponent<T>();
        }

        static void BuildHierarchy(Transform transform, int depth, List<object> hierarchy)
        {
            var components = new List<string>();
            foreach (var component in transform.GetComponents<Component>())
            {
                if (component != null)
                {
                    components.Add(component.GetType().Name);
                }
            }

            hierarchy.Add(new
            {
                name = transform.name,
                depth,
                active = transform.gameObject.activeSelf,
                components = components.ToArray()
            });

            for (var index = 0; index < transform.childCount; index++)
            {
                BuildHierarchy(transform.GetChild(index), depth + 1, hierarchy);
            }
        }

        static void EnsureFolderExists(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            var segments = directory.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        static string SanitizeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var index = 0; index < chars.Length; index++)
            {
                if (Array.IndexOf(invalidChars, chars[index]) >= 0 || chars[index] == '/')
                {
                    chars[index] = '_';
                }
            }

            return new string(chars);
        }
    }
}
