using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUI;
using FUI.Bindable;
using FUI.UGUI.Control;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;

namespace Game.Editor.Cli
{
    /// <summary>
    /// 获取元素的 BindableProperty 信息。
    /// </summary>
    [UnityCliTool(
        "ui_inspect_element",
        Description = "Get element BindableProperty information",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class UIElementInspectorTool : PlayModeUnityCliTool<UIElementInspectorTool.Parameters>
    {
        public override string Id => "ui_inspect_element";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string ViewName { get; set; }

            [UnityCliParam("Element Name")]
            public string ElementName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.ViewName) || string.IsNullOrEmpty(parameters?.ElementName))
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName and elementName parameters are required" };
            }

            var element = UIElementInspectorHelpers.FindElement(parameters.ViewName, parameters.ElementName);
            if (element == null)
            {
                return new { Success = false, Error = $"Element '{parameters.ElementName}' not found in '{parameters.ViewName}'" };
            }

            var elementObject = UIElementInspectorHelpers.GetElementGameObject(element);
            if (elementObject == null)
            {
                return new { Success = false, Error = "Cannot resolve GameObject from element" };
            }

            var properties = new Dictionary<string, object>();
            foreach (var component in elementObject.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type.Namespace == null || (!type.Namespace.StartsWith("FUI", StringComparison.Ordinal) && !type.Name.EndsWith("Element", StringComparison.Ordinal)))
                {
                    continue;
                }

                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(BindableProperty<>))
                    {
                        continue;
                    }

                    try
                    {
                        var propertyValue = field.GetValue(component);
                        if (propertyValue == null)
                        {
                            continue;
                        }

                        var value = UIInspectorHelpers.GetPropertyValue(propertyValue, "Value");
                        properties[field.Name] = value?.ToString() ?? "null";
                    }
                    catch
                    {
                    }
                }
            }

            return new { Success = true, Message = "Inspected element", Data = new { elementName = parameters.ElementName, properties } };
        }
    }

    /// <summary>
    /// 获取元素的 UGUI 组件详细信息。
    /// </summary>
    [UnityCliTool(
        "ui_inspect_element_detail",
        Description = "Get UGUI component details of an element",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class InspectElementDetailTool : PlayModeUnityCliTool<InspectElementDetailTool.Parameters>
    {
        public override string Id => "ui_inspect_element_detail";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string ViewName { get; set; }

            [UnityCliParam("Element Name")]
            public string ElementName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.ViewName) || string.IsNullOrEmpty(parameters?.ElementName))
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName and elementName parameters are required" };
            }

            var element = UIElementInspectorHelpers.FindElement(parameters.ViewName, parameters.ElementName);
            if (element == null)
            {
                return new { Success = false, Error = $"Element '{parameters.ElementName}' not found in '{parameters.ViewName}'" };
            }

            var elementObject = UIElementInspectorHelpers.GetElementGameObject(element);
            if (elementObject == null)
            {
                return new { Success = false, Error = "Element GameObject not found" };
            }

            var components = new List<object>
            {
                new
                {
                    type = "GameObject",
                    properties = new
                    {
                        name = elementObject.name,
                        activeInHierarchy = elementObject.activeInHierarchy.ToString(),
                        activeSelf = elementObject.activeSelf.ToString(),
                        layer = LayerMask.LayerToName(elementObject.layer),
                        tag = elementObject.tag
                    }
                }
            };

            foreach (var component in elementObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var componentType = component.GetType();
                var props = new Dictionary<string, object>();
                foreach (var property in componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead)
                    {
                        continue;
                    }

                    var propertyType = property.PropertyType;
                    if (!propertyType.IsPrimitive && propertyType != typeof(string) && propertyType != typeof(Vector2)
                        && propertyType != typeof(Vector3) && propertyType != typeof(Color) && !propertyType.IsEnum)
                    {
                        continue;
                    }

                    try
                    {
                        props[property.Name] = property.GetValue(component)?.ToString();
                    }
                    catch
                    {
                    }
                }

                components.Add(new { type = componentType.Name, properties = props });
            }

            return new { Success = true, Message = "Inspected element details", Data = new { components } };
        }
    }

    /// <summary>
    /// 元素检查辅助方法。
    /// </summary>
    internal static class UIElementInspectorHelpers
    {
        public static object FindElement(string viewName, string path)
        {
            if (string.IsNullOrEmpty(viewName) || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var entities = UIInspectorHelpers.GetEntities();
            var viewEntity = entities?.FirstOrDefault(entity => UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString() == viewName);
            var view = UIInspectorHelpers.GetPropertyValue(viewEntity, "View") as IView;
            if (view == null)
            {
                return null;
            }

            var parts = path.Split(new[] { '/' }, 2);
            var firstPart = parts[0];
            var remainingPath = parts.Length > 1 ? parts[1] : null;
            var currentElement = view.GetElement(firstPart, typeof(IElement));

            if (string.IsNullOrEmpty(remainingPath))
            {
                if (currentElement != null)
                {
                    return currentElement;
                }

                return UIInteractionHelpers.FindChildInAllCanvases(viewName, path);
            }

            if (currentElement is ListViewElement listView)
            {
                return FindItemInListView(listView, remainingPath);
            }

            var elementGameObject = GetElementGameObject(currentElement);
            if (elementGameObject != null)
            {
                var child = elementGameObject.transform.Find(remainingPath);
                if (child != null)
                {
                    return child.gameObject;
                }
            }

            return UIInteractionHelpers.FindChildInAllCanvases(viewName, path);
        }

        static object FindItemInListView(ListViewElement listView, string path)
        {
            var parts = path.Split(new[] { '/' }, 2);
            var indexOrName = parts[0];
            var rest = parts.Length > 1 ? parts[1] : null;

            if (!(UIInspectorHelpers.GetPropertyValue(listView, "ItemEntites") is IList itemEntities))
            {
                return null;
            }

            object targetEntity = null;
            if (int.TryParse(indexOrName, out var index) && index >= 0 && index < itemEntities.Count)
            {
                targetEntity = itemEntities[index];
            }
            else
            {
                foreach (var entity in itemEntities)
                {
                    var name = UIInspectorHelpers.GetPropertyValue(entity, "Name") as string;
                    if (name == indexOrName)
                    {
                        targetEntity = entity;
                        break;
                    }
                }
            }

            if (targetEntity == null)
            {
                return null;
            }

            var itemView = UIInspectorHelpers.GetPropertyValue(targetEntity, "View") as IView;
            if (string.IsNullOrEmpty(rest))
            {
                return itemView;
            }

            return itemView?.GetElement(rest, typeof(IElement));
        }

        public static GameObject GetElementGameObject(object element)
        {
            switch (element)
            {
                case GameObject gameObject:
                    return gameObject;
                case Component component:
                    return component.gameObject;
                case IElement iElement when iElement is Component component:
                    return component.gameObject;
                default:
                    return null;
            }
        }

        public static GameObject GetViewGameObject(object viewEntity)
        {
            if (viewEntity == null)
            {
                return null;
            }

            var view = UIInspectorHelpers.GetPropertyValue(viewEntity, "View");
            if (view != null)
            {
                if (view is MonoBehaviour monoBehaviour)
                {
                    return monoBehaviour.gameObject;
                }

                var gameObject = UIInspectorHelpers.GetPropertyValue(view, "gameObject") as GameObject;
                if (gameObject != null)
                {
                    return gameObject;
                }
            }

            var entityGameObject = UIInspectorHelpers.GetPropertyValue(viewEntity, "gameObject") as GameObject;
            if (entityGameObject != null)
            {
                return entityGameObject;
            }

            if (UIInspectorHelpers.GetPropertyValue(viewEntity, "view") is MonoBehaviour monoBehaviourField)
            {
                return monoBehaviourField.gameObject;
            }

            return null;
        }
    }
}
