using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FUI.Rendering;
using FUI.Rendering.UGUI.Elements;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;

namespace FUI.Cli
{
    /// <summary>
    /// FUI CLI 元素选择器解析器。
    /// </summary>
    internal static class FuiElementSelectorResolver
    {
        public static bool TryResolve(Dictionary<string, object> selector, out FuiElementSelection selection, out ToolResult error)
        {
            selection = null;
            if (!TryParse(selector, out var parsed, out error))
            {
                return false;
            }

            var entities = UIInspectorHelpers.GetEntities();
            if (entities == null)
            {
                error = ToolResult.Error("selector_runtime_unavailable", "无法解析 UI 实体集合。");
                return false;
            }

            var matchedEntities = entities
                .Where(entity => string.Equals(UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString(), parsed.view, StringComparison.Ordinal))
                .ToList();
            if (matchedEntities.Count == 0)
            {
                error = ToolResult.Error("selector_view_not_found", $"视图 '{parsed.view}' 未找到。", new
                {
                    selector = parsed.ToData(),
                    availableViews = entities
                        .Select(entity => UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString())
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Distinct()
                        .ToArray()
                });
                return false;
            }

            if (matchedEntities.Count > 1)
            {
                error = ToolResult.Error("ambiguous_selector", $"视图 '{parsed.view}' 匹配到 {matchedEntities.Count} 个实体。", new
                {
                    selector = parsed.ToData(),
                    matchCount = matchedEntities.Count,
                    candidates = matchedEntities.Select(CreateEntityCandidate).ToArray()
                });
                return false;
            }

            var viewEntity = matchedEntities[0];
            var view = UIInspectorHelpers.GetPropertyValue(viewEntity, "View") as IView;
            if (view == null)
            {
                error = ToolResult.Error("selector_view_invalid", $"视图 '{parsed.view}' 没有可用 IView。", new { selector = parsed.ToData() });
                return false;
            }

            var rootElement = view.GetElement(parsed.element, typeof(IElement));
            if (rootElement == null)
            {
                error = ToolResult.Error("selector_element_not_found", $"元素 '{parsed.element}' 未在视图 '{parsed.view}' 中找到。", new
                {
                    selector = parsed.ToData(),
                    candidates = UIInspectorHelpers.GetElementObjects(viewEntity)
                        .Select(CreateElementCandidate)
                        .ToArray()
                });
                return false;
            }

            if (!parsed.itemIndex.HasValue)
            {
                if (!string.IsNullOrEmpty(parsed.child))
                {
                    error = ToolResult.Error("invalid_selector", "child 只能和 itemIndex 一起使用。", new { selector = parsed.ToData() });
                    return false;
                }

                selection = CreateSelection(parsed, viewEntity, view, null, rootElement, null);
                return true;
            }

            if (!(rootElement is ListElement listView))
            {
                error = ToolResult.Error("selector_not_list", $"元素 '{parsed.element}' 不是 ListElement，不能使用 itemIndex。", new
                {
                    selector = parsed.ToData(),
                    elementType = rootElement.GetType().FullName
                });
                return false;
            }

            if (!(UIInspectorHelpers.GetPropertyValue(listView, "ItemInstances") is IList itemEntities))
            {
                error = ToolResult.Error("selector_list_items_unavailable", $"列表 '{parsed.element}' 没有可读 item 实体集合。", new { selector = parsed.ToData() });
                return false;
            }

            var itemIndex = parsed.itemIndex.Value;
            if (itemIndex < 0 || itemIndex >= itemEntities.Count)
            {
                error = ToolResult.Error("item_index_out_of_range", $"itemIndex {itemIndex} 超出列表 '{parsed.element}' 范围。", new
                {
                    selector = parsed.ToData(),
                    itemCount = itemEntities.Count
                });
                return false;
            }

            var itemEntity = itemEntities[itemIndex];
            var itemView = UIInspectorHelpers.GetPropertyValue(itemEntity, "View") as IView;
            if (itemView == null)
            {
                error = ToolResult.Error("selector_item_view_invalid", $"列表 '{parsed.element}' 的 itemIndex {itemIndex} 没有可用 IView。", new { selector = parsed.ToData() });
                return false;
            }

            if (string.IsNullOrEmpty(parsed.child))
            {
                selection = CreateSelection(parsed, viewEntity, view, itemEntity, itemView, itemIndex);
                return true;
            }

            var childElement = itemView.GetElement(parsed.child, typeof(IElement));
            if (childElement == null)
            {
                error = ToolResult.Error("selector_child_not_found", $"列表 '{parsed.element}' 的 itemIndex {itemIndex} 中未找到 child '{parsed.child}'。", new
                {
                    selector = parsed.ToData(),
                    itemIndex,
                    itemElements = UIInspectorHelpers.GetElementObjects(itemEntity)
                        .Select(CreateElementCandidate)
                        .ToArray()
                });
                return false;
            }

            selection = CreateSelection(parsed, viewEntity, view, itemEntity, childElement, itemIndex);
            return true;
        }

        public static bool TryResolveGameObject(Dictionary<string, object> selector, out GameObject gameObject, out FuiElementSelection selection, out ToolResult error)
        {
            gameObject = null;
            if (!TryResolve(selector, out selection, out error))
            {
                return false;
            }

            gameObject = selection.GameObject;
            if (gameObject != null)
            {
                return true;
            }

            error = ToolResult.Error("selector_target_has_no_gameobject", "选择器目标无法解析为 GameObject。", new { selector = selection.Selector });
            return false;
        }

        static bool TryParse(Dictionary<string, object> rawSelector, out ParsedSelector selector, out ToolResult error)
        {
            selector = null;
            if (rawSelector == null || rawSelector.Count == 0)
            {
                error = ToolResult.Error("invalid_selector", "selector 参数是必需的。", new
                {
                    required = new[] { "selector.view", "selector.element" }
                });
                return false;
            }

            var view = ReadString(rawSelector, "view");
            var element = ReadString(rawSelector, "element");
            if (string.IsNullOrWhiteSpace(view) || string.IsNullOrWhiteSpace(element))
            {
                error = ToolResult.Error("invalid_selector", "selector.view 和 selector.element 是必需的。", new
                {
                    selector = rawSelector,
                    required = new[] { "view", "element" }
                });
                return false;
            }

            if (!TryReadOptionalInt(rawSelector, "itemIndex", out var itemIndex, out var parseError))
            {
                error = ToolResult.Error("invalid_selector", parseError, new { selector = rawSelector, field = "itemIndex" });
                return false;
            }

            selector = new ParsedSelector
            {
                view = view.Trim(),
                element = element.Trim(),
                itemIndex = itemIndex,
                child = ReadString(rawSelector, "child")?.Trim()
            };
            error = null;
            return true;
        }

        static string ReadString(Dictionary<string, object> values, string key)
        {
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pair.Value?.ToString();
            }

            return null;
        }

        static bool TryReadOptionalInt(Dictionary<string, object> values, string key, out int? result, out string error)
        {
            result = null;
            error = null;
            foreach (var pair in values)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (pair.Value == null)
                {
                    return true;
                }

                try
                {
                    result = Convert.ToInt32(pair.Value);
                    return true;
                }
                catch (Exception exception)
                {
                    error = $"selector.{key} 必须是整数：{exception.Message}";
                    return false;
                }
            }

            return true;
        }

        static FuiElementSelection CreateSelection(ParsedSelector selector, object viewEntity, IView view, object itemEntity, object target, int? itemIndex)
        {
            var gameObject = UIElementInspectorHelpers.GetElementGameObject(target);
            return new FuiElementSelection
            {
                Selector = selector.ToData(),
                ViewEntity = viewEntity,
                View = view,
                ItemEntity = itemEntity,
                Target = target,
                GameObject = gameObject,
                ViewName = selector.view,
                ElementName = selector.element,
                ItemIndex = itemIndex,
                ChildName = selector.child,
                TargetPath = gameObject == null ? null : GetGameObjectPath(gameObject)
            };
        }

        static object CreateEntityCandidate(object entity)
        {
            var view = UIInspectorHelpers.GetPropertyValue(entity, "View") as Component;
            return new
            {
                name = UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString(),
                path = view == null ? null : GetGameObjectPath(view.gameObject),
                instanceId = view == null ? 0 : view.gameObject.GetInstanceID()
            };
        }

        static object CreateElementCandidate(object element)
        {
            var gameObject = UIElementInspectorHelpers.GetElementGameObject(element);
            return new
            {
                name = UIInspectorHelpers.GetElementName(element),
                type = element?.GetType().Name,
                path = gameObject == null ? null : GetGameObjectPath(gameObject),
                instanceId = gameObject == null ? 0 : gameObject.GetInstanceID(),
                activeInHierarchy = gameObject != null && gameObject.activeInHierarchy
            };
        }

        public static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        sealed class ParsedSelector
        {
            public string view;
            public string element;
            public int? itemIndex;
            public string child;

            public object ToData()
            {
                return new
                {
                    view,
                    element,
                    itemIndex,
                    child
                };
            }
        }
    }

    internal sealed class FuiElementSelection
    {
        public object Selector { get; set; }
        public object ViewEntity { get; set; }
        public IView View { get; set; }
        public object ItemEntity { get; set; }
        public object Target { get; set; }
        public GameObject GameObject { get; set; }
        public string ViewName { get; set; }
        public string ElementName { get; set; }
        public int? ItemIndex { get; set; }
        public string ChildName { get; set; }
        public string TargetPath { get; set; }
    }
}
