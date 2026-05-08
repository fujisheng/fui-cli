using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUI;
using FUI.UGUI;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;

namespace FUI.Cli
{
    /// <summary>
    /// 列出当前打开的 UI 视图。
    /// </summary>
    [UnityCliTool(
        "ui_list_open_views",
        Description = "List all currently open UI views and their states",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class ListOpenViewsTool : PlayModeUnityCliTool<ListOpenViewsTool.Parameters>
    {
        public override string Id => "ui_list_open_views";

        public class Parameters
        {
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            var entities = UIInspectorHelpers.GetEntities();
            if (entities == null)
            {
                return new
                {
                    Success = false,
                    Error = "Registry resolution failed",
                    Message = "Failed to resolve FUI.Editor.UIEntityRegistry."
                };
            }

            var views = new List<object>();
            foreach (var entity in entities)
            {
                var viewModel = UIInspectorHelpers.GetPropertyValue(entity, "ViewModel");
                var layer = UIInspectorHelpers.GetPropertyValue(entity, "Layer");
                var order = UIInspectorHelpers.GetPropertyValue(entity, "Order");

                views.Add(new
                {
                    name = UIInspectorHelpers.GetPropertyValue(entity, "Name"),
                    state = UIInspectorHelpers.GetPropertyValue(entity, "State")?.ToString(),
                    layer = layer != null ? Convert.ToInt32(layer) : 0,
                    order = order != null ? Convert.ToInt32(order) : 0,
                    viewModelType = viewModel?.GetType()?.Name
                });
            }

            return new
            {
                Success = true,
                Message = $"Found {views.Count} active views",
                Data = new { views, count = views.Count }
            };
        }
    }

    /// <summary>
    /// 检查指定视图的元素树。
    /// </summary>
    [UnityCliTool(
        "ui_inspect_view",
        Description = "Inspect element tree of a specific UI view",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class InspectViewTool : PlayModeUnityCliTool<InspectViewTool.Parameters>
    {
        public override string Id => "ui_inspect_view";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName))
            {
                return new { Success = false, Error = "Missing viewName", Message = "viewName parameter is required" };
            }

            var targetEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (targetEntity == null)
            {
                return new
                {
                    Success = false,
                    Error = "View not found",
                    Message = $"View '{parameters.viewName}' not found. Use ui_list_open_views to see available views."
                };
            }

            var result = new
            {
                name = UIInspectorHelpers.GetPropertyValue(targetEntity, "Name"),
                elements = UIInspectorHelpers.GetElements(targetEntity)
            };

            return new
            {
                Success = true,
                Message = $"Inspected view '{parameters.viewName}'",
                Data = result
            };
        }
    }

    /// <summary>
    /// 获取 ViewModel 的公开状态。
    /// </summary>
    [UnityCliTool(
        "ui_get_viewmodel_state",
        Description = "Get ViewModel property states for a view",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class GetViewModelStateTool : PlayModeUnityCliTool<GetViewModelStateTool.Parameters>
    {
        public override string Id => "ui_get_viewmodel_state";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName))
            {
                return new { Success = false, Error = "Missing viewName", Message = "viewName parameter is required" };
            }

            var targetEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (targetEntity == null)
            {
                return new
                {
                    Success = false,
                    Error = "View not found",
                    Message = $"View '{parameters.viewName}' not found. Use ui_list_open_views to see available views."
                };
            }

            var viewModel = UIInspectorHelpers.GetPropertyValue(targetEntity, "ViewModel");
            if (viewModel == null)
            {
                return new
                {
                    Success = false,
                    Error = "No ViewModel bound to view.",
                    Data = (object)null
                };
            }

            var properties = new Dictionary<string, object>();
            var type = viewModel.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(viewModel);
                    if (value != null && !UIInspectorHelpers.IsUnityInternal(value))
                    {
                        properties[field.Name] = value.ToString();
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            var reflectedProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in reflectedProperties)
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                try
                {
                    var value = property.GetValue(viewModel, null);
                    if (value != null && !UIInspectorHelpers.IsUnityInternal(value))
                    {
                        properties[property.Name] = value.ToString();
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }

            return new
            {
                Success = true,
                Message = $"Retrieved ViewModel state for view '{parameters.viewName}'",
                Data = new { viewName = parameters.viewName, properties }
            };
        }
    }

    /// <summary>
    /// 获取视图的绑定关系。
    /// </summary>
    [UnityCliTool(
        "ui_get_bindings",
        Description = "Get binding relationships for a view (BindableProperties and their values)",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class GetBindingsTool : PlayModeUnityCliTool<GetBindingsTool.Parameters>
    {
        public override string Id => "ui_get_bindings";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName))
            {
                return new { Success = false, Error = "Missing viewName", Message = "viewName parameter is required" };
            }

            var targetEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (targetEntity == null)
            {
                return new
                {
                    Success = false,
                    Error = "View not found",
                    Message = $"View '{parameters.viewName}' not found. Use ui_list_open_views to see available views."
                };
            }

            var viewModel = UIInspectorHelpers.GetPropertyValue(targetEntity, "ViewModel");
            var bindings = new List<object>();

            if (viewModel != null)
            {
                var viewModelType = viewModel.GetType();
                foreach (var property in viewModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    try
                    {
                        var value = property.GetValue(viewModel);
                        bindings.Add(new
                        {
                            source = "ViewModel",
                            type = "Property",
                            name = property.Name,
                            propertyType = property.PropertyType.Name,
                            value = value?.ToString() ?? "null"
                        });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                foreach (var field in viewModelType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var fieldValue = field.GetValue(viewModel);
                        var valueString = "null";
                        var bindingType = "Field";

                        if (fieldValue != null)
                        {
                            if (field.FieldType.IsGenericType && field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                            {
                                bindingType = "BindableProperty";
                                var valueProperty = fieldValue.GetType().GetProperty("Value");
                                var innerValue = valueProperty?.GetValue(fieldValue);
                                valueString = innerValue?.ToString() ?? "null";
                            }
                            else
                            {
                                valueString = fieldValue.ToString();
                            }
                        }

                        bindings.Add(new
                        {
                            source = "ViewModel",
                            type = bindingType,
                            name = field.Name,
                            propertyType = field.FieldType.Name,
                            value = valueString
                        });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

            foreach (var element in UIInspectorHelpers.GetElementObjects(targetEntity))
            {
                var elementType = element.GetType();
                foreach (var field in elementType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        var fieldValue = field.GetValue(element);
                        var valueString = "null";
                        if (fieldValue != null)
                        {
                            var valueProperty = fieldValue.GetType().GetProperty("Value");
                            var innerValue = valueProperty?.GetValue(fieldValue);
                            valueString = innerValue?.ToString() ?? "null";
                        }

                        bindings.Add(new
                        {
                            source = "Element",
                            elementName = UIInspectorHelpers.GetElementName(element),
                            type = "BindableProperty",
                            name = field.Name,
                            propertyType = field.FieldType.Name,
                            value = valueString
                        });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

            return new
            {
                Success = true,
                Message = $"Found {bindings.Count} bindings for view '{parameters.viewName}'",
                Data = new { viewName = parameters.viewName, bindings, count = bindings.Count }
            };
        }
    }

    /// <summary>
    /// UI 检查辅助方法。
    /// </summary>
    internal static class UIInspectorHelpers
    {
        public static List<object> GetEntities()
        {
            var entities = new List<UIEntity>();
            FUI.Editor.UIEntityRegistry.GetEnabledEntities(entities);
            var result = new List<object>(entities.Count);
            foreach (var entity in entities)
            {
                result.Add(entity);
            }

            return result;
        }

        public static object FindViewEntity(string viewName)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                return null;
            }

            var entities = GetEntities();
            if (entities == null)
            {
                return null;
            }

            return entities.FirstOrDefault(entity => GetPropertyValue(entity, "Name")?.ToString() == viewName);
        }

        public static object GetPropertyValue(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var property = FindProperty(obj.GetType(), propertyName, flags);
            if (property != null)
            {
                try
                {
                    return property.GetValue(obj, null);
                }
                catch (Exception)
                {
                    return null;
                }
            }

            var field = FindField(obj.GetType(), propertyName, flags);
            if (field == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(obj);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<object> GetElements(object parentEntity)
        {
            var result = new List<object>();
            foreach (var element in GetElementObjects(parentEntity))
            {
                result.Add(new
                {
                    name = GetElementName(element),
                    type = element.GetType().Name,
                    fullType = element.GetType().FullName
                });
            }

            return result;
        }

        public static List<object> GetElementObjects(object parentEntity)
        {
            var elements = new List<object>();
            if (parentEntity == null)
            {
                return elements;
            }

            IEnumerable rawElements = GetPropertyValue(parentEntity, "Elements") as IEnumerable;

            if (rawElements == null)
            {
                var view = GetPropertyValue(parentEntity, "View");
                if (view != null)
                {
                    rawElements = GetPropertyValue(view, "Elements") as IEnumerable;
                }
            }

            if (rawElements == null)
            {
                return elements;
            }

            foreach (var element in rawElements)
            {
                if (element != null)
                {
                    elements.Add(element);
                }
            }

            return elements;
        }

        public static string GetElementName(object element)
        {
            if (element == null)
            {
                return "unnamed";
            }

            var name = GetPropertyValue(element, "Name") ?? GetPropertyValue(element, "name");
            if (name != null)
            {
                return name.ToString();
            }

            if (element is GameObject gameObject)
            {
                return gameObject.name;
            }

            if (element is Component component)
            {
                return component.gameObject.name;
            }

            return "unnamed";
        }

        public static bool IsUnityInternal(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var typeName = obj.GetType().FullName;
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            return typeName.StartsWith("UnityEngine.", StringComparison.Ordinal)
                || typeName.StartsWith("UnityEditor.", StringComparison.Ordinal);
        }

        static System.Reflection.PropertyInfo FindProperty(Type type, string propertyName, BindingFlags flags)
        {
            if (type == null || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            var searchFlags = (flags & ~BindingFlags.IgnoreCase) | BindingFlags.DeclaredOnly;
            foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
            {
                foreach (var currentType in EnumerateTypeHierarchy(type))
                {
                    System.Reflection.PropertyInfo[] properties;
                    try
                    {
                        properties = currentType.GetProperties(searchFlags);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (var property in properties)
                    {
                        if (!property.CanRead || property.GetIndexParameters().Length > 0)
                        {
                            continue;
                        }

                        if (string.Equals(property.Name, propertyName, comparison))
                        {
                            return property;
                        }
                    }
                }
            }

            return null;
        }

        static System.Reflection.FieldInfo FindField(Type type, string fieldName, BindingFlags flags)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            var searchFlags = (flags & ~BindingFlags.IgnoreCase) | BindingFlags.DeclaredOnly;
            foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
            {
                foreach (var currentType in EnumerateTypeHierarchy(type))
                {
                    System.Reflection.FieldInfo[] fields;
                    try
                    {
                        fields = currentType.GetFields(searchFlags);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (var field in fields)
                    {
                        if (string.Equals(field.Name, fieldName, comparison))
                        {
                            return field;
                        }
                    }
                }
            }

            return null;
        }

        static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
        {
            if (type == null)
            {
                yield break;
            }

            if (type.IsInterface)
            {
                yield return type;
                foreach (var interfaceType in type.GetInterfaces())
                {
                    yield return interfaceType;
                }

                yield break;
            }

            for (var current = type; current != null; current = current.BaseType)
            {
                yield return current;
            }
        }
    }
}
