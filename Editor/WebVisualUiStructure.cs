using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace FUI.Cli
{
    static partial class WebVisualUiPrefabBuilder
    {
        static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                var found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static void ValidateStructure(WebVisualUiPlan plan, WebVisualPrefabResult result)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var borders = new Dictionary<string, Vector4>(StringComparer.Ordinal);
            void Error(string code, WebVisualNode node, string message)
            {
                result.ok = false;
                result.issues.Add(WebVisualPrefabIssue.Create(code, message, node?.id));
            }
            bool Visible(WebVisualNode node) => !string.IsNullOrWhiteSpace(node.style?.sprite)
                || !string.IsNullOrWhiteSpace(node.text?.content)
                || (!string.IsNullOrWhiteSpace(node.style?.color) && ResolveAlpha(node.style) > 0f);
            IEnumerable<WebVisualNode> Descendants(WebVisualNode node) => (node.children ?? new List<WebVisualNode>())
                .Where(n => n != null).SelectMany(n => new[] { n }.Concat(Descendants(n)));
            void Visit(WebVisualNode node, string parent)
            {
                if (node == null) { Error("null_node", node, "节点不能为空。"); return; }
                var supported = new[] { "Container", "ImageElement", "TextElement", "RawImage", "ButtonElement", "SliderElement", "ScrollbarElement", "ToggleElement", "InputFieldElement", "DropdownElement", "ScrollView", "ToggleGroup", "CanvasGroup", "MaskElement", "RectMask2D", "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup", "ContentSizeFitter", "AspectRatioFitter", "LayoutElement", "ListView", "StaticListViewElement", "Template", "DynamicViewElement", "StarElement" };
                if (!supported.Contains(node.element)) Error("unsupported_ui_type", node, "未知控件类型，不能隐式降级为 Image: " + node.element);
                ValidateControlParts(node, result);
                ValidateLayout(node, result);
                if (string.IsNullOrWhiteSpace(node.id) || !ids.Add(node.id)) Error("duplicate_ui_id", node, "UI id 必须非空且在视图内唯一。");
                if (!string.IsNullOrEmpty(node.owner) && node.owner != parent) Error("owner_mismatch", node, "owner 必须与已规范化的父节点一致。");
                if (Regex.IsMatch(node.id ?? "", @"_(Slice|Pixels)\d+$")) Error("expanded_slice_nodes", node, "请声明一个 sliced Image，不要导入网页九宫格裁切节点。");
                if (node.binding == "none" && CompositeTypes.Contains(node.element))
                    Error("invalid_decorative_control", node, "交互控件不能标记为无绑定装饰。");
                if (NormalizeElement(node.element) == "ButtonElement")
                {
                    var backgrounds = Descendants(node).Where(n => n.part == "background").ToArray();
                    if (string.IsNullOrEmpty(node.targetGraphic) && backgrounds.Length == 1) node.targetGraphic = backgrounds[0].id;
                    if (string.IsNullOrEmpty(node.targetGraphic) && backgrounds.Length > 1) Error("ambiguous_button_background", node, "按钮有多个背景，需要显式 targetGraphic。");
                    if (!string.IsNullOrEmpty(node.targetGraphic) && !Descendants(node).Any(n => n.id == node.targetGraphic && new[]{"ImageElement","RawImage","TextElement"}.Contains(NormalizeElement(n.element)) && Visible(n)))
                        Error("invalid_button_target", node, "targetGraphic 必须指向按钮子树中的可见 Graphic（Image/RawImage/Text）。");
                    if (!node.hitRegion && !Visible(node) && string.IsNullOrEmpty(node.targetGraphic))
                        Error("button_visual_missing", node, "按钮需要可见背景，独立热区必须显式声明 hitRegion=true。");
                }
                if (ParseImageType(node.style?.imageType) == UnityEngine.UI.Image.Type.Sliced)
                {
                    var border = ResolveSpriteBorder(node.style);
                    if (border == Vector4.zero || border.x < 0 || border.y < 0 || border.z < 0 || border.w < 0)
                        Error("sliced_border_missing", node, "九宫格需要有效的源图像素边距 left,bottom,right,top。");
                    var path = NormalizeAssetPath(node.style.sprite);
                    if (borders.TryGetValue(path, out var previous) && previous != border) Error("sprite_border_conflict", node, "同一 Sprite 不能声明不同导入边距；使用每个 Image 的像素缩放控制显示厚度。");
                    borders[path] = border;
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null)
                    {
                        importer.GetSourceTextureWidthAndHeight(out var width, out var height);
                        if (border.x + border.z >= width || border.y + border.w >= height) Error("sprite_border_out_of_bounds", node, "边距必须保留有效中心区域。");
                    }
                }
                if ((node.component ?? "").Contains("RectMask2D") && !node.clip)
                    result.warnings.Add(WebVisualPrefabIssue.Create("mask_without_intent", "遮罩缺少显式 clip 用途。", node.id));
                foreach (var child in node.children ?? new List<WebVisualNode>()) Visit(child, node.id);
            }
            foreach (var node in plan.nodes) Visit(node, "");
        }
    }
}
