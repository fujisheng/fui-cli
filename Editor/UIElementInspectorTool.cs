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

namespace FUI.Cli
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
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (!FuiElementSelectorResolver.TryResolve(parameters?.selector, out var selection, out var error))
            {
                return error;
            }

            var element = selection.Target;
            var elementObject = selection.GameObject;
            if (elementObject == null)
            {
                return ToolResult.Error("selector_target_has_no_gameobject", "Cannot resolve GameObject from selector target", new { selection.Selector });
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

            return ToolResult.Ok(new
            {
                selector = selection.Selector,
                targetPath = selection.TargetPath,
                targetInstanceId = elementObject.GetInstanceID(),
                elementName = UIInspectorHelpers.GetElementName(element),
                properties
            }, "Inspected element");
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
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (!FuiElementSelectorResolver.TryResolveGameObject(parameters?.selector, out var elementObject, out var selection, out var error))
            {
                return error;
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

            return ToolResult.Ok(new
            {
                selector = selection.Selector,
                targetPath = selection.TargetPath,
                targetInstanceId = elementObject.GetInstanceID(),
                components
            }, "Inspected element details");
        }
    }

    /// <summary>
    /// 元素检查辅助方法。
    /// </summary>
    internal static class UIElementInspectorHelpers
    {
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
