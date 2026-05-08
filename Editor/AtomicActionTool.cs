using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UI;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;

namespace FUI.Cli
{
    /// <summary>
    /// 批量设置 ViewModel 属性值，一次调用修改多个属性。
    /// </summary>
    [UnityCliTool(
        "ui_set_viewmodel_properties",
        Description = "Set multiple ViewModel property values at runtime in a single call",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class SetViewModelPropertiesTool : PlayModeUnityCliTool<SetViewModelPropertiesTool.Parameters>
    {
        public override string Id => "ui_set_viewmodel_properties";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }

            [UnityCliParam("Properties JSON: {\"prop1\": \"value1\", \"prop2\": \"value2\"}")]
            public string properties { get; set; }

            [UnityCliParam("Read back after write (true/false)", Required = false)]
            public bool readback { get; set; } = true;
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName) || string.IsNullOrEmpty(parameters?.properties))
            {
                return ToolResult.Error("invalid_parameter", "viewName 和 properties 参数是必需的。", new
                {
                    required = new[] { "viewName", "properties" }
                });
            }

            var entities = UIInspectorHelpers.GetEntities();
            if (entities == null)
            {
                return ToolResult.Error("tool_execution_failed", "无法解析 UI 实体集合。");
            }

            var targetEntity = entities.FirstOrDefault(entity => UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString() == parameters.viewName);
            if (targetEntity == null)
            {
                return ToolResult.Error("not_found", $"视图 '{parameters.viewName}' 未找到。");
            }

            var viewModel = UIInspectorHelpers.GetPropertyValue(targetEntity, "ViewModel");
            if (viewModel == null)
            {
                return ToolResult.Error("not_found", "视图没有绑定 ViewModel。");
            }

            Dictionary<string, object> propertyDict;
            try
            {
                if (args.TryGetValue("properties", out var rawValue) && rawValue is Dictionary<string, object> dict)
                {
                    propertyDict = dict;
                }
                else if (!string.IsNullOrWhiteSpace(parameters.properties))
                {
                    if (!UnityCliParameterBinder.TryDeserializeJsonObject(parameters.properties, out propertyDict, out var error))
                    {
                        return ToolResult.Error("invalid_parameter", "properties 必须是 JSON 对象。", new
                        {
                            parseError = error,
                            value = parameters.properties
                        });
                    }
                }
                else
                {
                    return ToolResult.Error("invalid_parameter", "properties 必须是 JSON 对象。");
                }
            }
            catch (Exception exception)
            {
                return ToolResult.Error("invalid_parameter", $"properties JSON 解析失败: {exception.Message}");
            }

            if (propertyDict.Count == 0)
            {
                return ToolResult.Error("invalid_parameter", "properties 不能为空。");
            }

            var beforeState = new Dictionary<string, string>();
            var setResults = new List<PropertySetResult>();
            var afterState = new Dictionary<string, string>();
            var viewModelType = viewModel.GetType();

            foreach (var kvp in propertyDict)
            {
                var propertyName = kvp.Key;
                var stringValue = kvp.Value?.ToString() ?? string.Empty;

                var beforeValue = TryReadPropertyValue(viewModel, viewModelType, propertyName);
                beforeState[propertyName] = beforeValue ?? "null";

                var setResult = TrySetProperty(viewModel, viewModelType, propertyName, stringValue);
                setResults.Add(new PropertySetResult
                {
                    property = propertyName,
                    value = stringValue,
                    ok = setResult.success,
                    error = setResult.error
                });

                if (parameters.readback && setResult.success)
                {
                    var afterValue = TryReadPropertyValue(viewModel, viewModelType, propertyName);
                    afterState[propertyName] = afterValue ?? "null";
                }
            }

            var succeeded = setResults.Count(result => result.ok);
            var failed = setResults.Count - succeeded;

            return ToolResult.Ok(new
            {
                ok = failed == 0,
                toolId = Id,
                viewName = parameters.viewName,
                totalRequested = propertyDict.Count,
                succeeded,
                failed,
                beforeState,
                afterState = parameters.readback ? afterState : null,
                results = setResults.ConvertAll(result => new
                {
                    result.property,
                    result.value,
                    result.ok,
                    result.error
                }).ToArray()
            }, $"已设置 {succeeded}/{propertyDict.Count} 个属性。");
        }

        static string TryReadPropertyValue(object viewModel, Type viewModelType, string propertyName)
        {
            try
            {
                var property = viewModelType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(viewModel)?.ToString();
                }

                var field = viewModelType.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var fieldValue = field.GetValue(viewModel);
                    if (fieldValue != null && field.FieldType.IsGenericType
                        && field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                    {
                        var valueProperty = fieldValue.GetType().GetProperty("Value");
                        return valueProperty?.GetValue(fieldValue)?.ToString();
                    }

                    return fieldValue?.ToString();
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static (bool success, string error) TrySetProperty(object viewModel, Type viewModelType, string propertyName, string value)
        {
            try
            {
                var property = viewModelType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    if (!HasBindingAttribute(property))
                    {
                        return (false, $"属性 '{propertyName}' 没有 [Binding] 标记，已拒绝修改。");
                    }

                    var convertedValue = ConvertValue(value, property.PropertyType);
                    property.SetValue(viewModel, convertedValue);
                    return (true, null);
                }

                var field = viewModelType.GetField(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    if (field.FieldType.IsGenericType && field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                    {
                        var bindableProperty = field.GetValue(viewModel);
                        if (bindableProperty != null)
                        {
                            var valueProperty = bindableProperty.GetType().GetProperty("Value");
                            if (valueProperty != null)
                            {
                                var convertedValue = ConvertValue(value, valueProperty.PropertyType);
                                valueProperty.SetValue(bindableProperty, convertedValue);
                                return (true, null);
                            }
                        }

                        return (false, "BindableProperty 值为 null。");
                    }

                    return (false, $"字段 '{propertyName}' 不是受支持的 BindableProperty。请改为公开 [Binding] 属性。");
                }

                return (false, $"属性 '{propertyName}' 未找到。");
            }
            catch (Exception exception)
            {
                return (false, exception.Message);
            }
        }

        static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            if (targetType == typeof(int))
            {
                return int.Parse(value);
            }

            if (targetType == typeof(float))
            {
                return float.Parse(value);
            }

            if (targetType == typeof(double))
            {
                return double.Parse(value);
            }

            if (targetType == typeof(bool))
            {
                return bool.Parse(value);
            }

            if (targetType == typeof(long))
            {
                return long.Parse(value);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, value);
            }

            return Convert.ChangeType(value, targetType);
        }

        static bool HasBindingAttribute(MemberInfo memberInfo)
        {
            if (memberInfo == null)
            {
                return false;
            }

            return memberInfo.GetCustomAttributes(true)
                .Any(attribute => string.Equals(attribute.GetType().Name, "BindingAttribute", StringComparison.Ordinal));
        }

        sealed class PropertySetResult
        {
            public string property;
            public string value;
            public bool ok;
            public string error;
        }
    }

    /// <summary>
    /// 原子化 UI 操作：设置属性 + 等待一帧 + 读取结果，一次调用完成。
    /// 支持单一操作（set_property）和交互操作（click）。
    /// </summary>
    [UnityCliTool(
        "ui.atomic_action",
        Description = "Perform an atomic UI action with built-in verification (set property, click, etc.)",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class AtomicActionTool : PlayModeUnityCliTool<AtomicActionTool.Parameters>
    {
        public override string Id => "ui.atomic_action";

        public class Parameters
        {
            [UnityCliParam("Action type: set_property, click, input_text, toggle")]
            public string action { get; set; }

            [UnityCliParam("Element selector for click/input/toggle: { view, element, itemIndex?, child? }", Required = false)]
            public Dictionary<string, object> selector { get; set; }

            [UnityCliParam("View Name (for set_property)", Required = false)]
            public string viewName { get; set; }

            [UnityCliParam("Property Name (for set_property)")]
            public string propertyName { get; set; }

            [UnityCliParam("Value to set or input")]
            public string value { get; set; }

            [UnityCliParam("Frames to wait after action (default 2)", Required = false)]
            public int waitFrames { get; set; } = 2;
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.action))
            {
                return ToolResult.Error("invalid_parameter", "action 参数是必需的。");
            }

            var action = parameters.action.Trim().ToLowerInvariant();
            object beforeState = null;
            object afterState = null;
            string actionDescription;
            bool actionSuccess;
            string actionError = null;

            switch (action)
            {
                case "set_property":
                    if (string.IsNullOrEmpty(parameters.viewName))
                    {
                        return ToolResult.Error("invalid_parameter", "set_property 需要 viewName 参数。");
                    }

                    actionDescription = $"设置 {parameters.propertyName} = {parameters.value}";
                    beforeState = GetPropertyState(parameters.viewName, parameters.propertyName);
                    (actionSuccess, actionError) = SetProperty(parameters.viewName, parameters.propertyName, parameters.value);
                    break;
                case "click":
                    actionDescription = "点击 selector 目标";
                    beforeState = GetElementState(parameters.selector);
                    (actionSuccess, actionError) = SimulateClick(parameters.selector);
                    break;
                case "input_text":
                    actionDescription = "输入文本到 selector 目标";
                    beforeState = GetElementState(parameters.selector);
                    (actionSuccess, actionError) = SimulateInputText(parameters.selector, parameters.value);
                    break;
                case "toggle":
                    actionDescription = "切换 selector 目标";
                    beforeState = GetElementState(parameters.selector);
                    (actionSuccess, actionError) = ToggleElement(parameters.selector);
                    break;
                default:
                    return ToolResult.Error("invalid_parameter", $"不支持的操作类型: {action}。支持: set_property, click, input_text, toggle。");
            }

            var waitFrames = Mathf.Clamp(parameters.waitFrames, 1, 10);
            for (var index = 0; index < waitFrames; index++)
            {
                Canvas.ForceUpdateCanvases();
            }

            if (actionSuccess && action == "set_property")
            {
                afterState = GetPropertyState(parameters.viewName, parameters.propertyName);
            }
            else if (actionSuccess)
            {
                afterState = GetElementState(parameters.selector);
            }

            return ToolResult.Ok(new
            {
                ok = actionSuccess,
                toolId = Id,
                action = parameters.action,
                viewName = parameters.viewName,
                selector = parameters.selector,
                description = actionDescription,
                error = actionError,
                waitFrames,
                before = beforeState,
                after = afterState,
                synchronized = true
            }, actionSuccess ? $"同步动作 '{action}' 已完成并刷新 UI。" : $"同步动作 '{action}' 执行失败。");
        }

        static object ReadNamedValue(object source, string memberName)
        {
            if (source == null)
            {
                return null;
            }

            var property = source.GetType().GetProperty(memberName);
            if (property != null)
            {
                return property.GetValue(source);
            }

            var field = source.GetType().GetField(memberName);
            if (field != null)
            {
                return field.GetValue(source);
            }

            return null;
        }

        static Dictionary<string, object> ReadPropertiesDictionary(object data)
        {
            if (data == null)
            {
                return null;
            }

            var value = ReadNamedValue(data, "properties");
            return value as Dictionary<string, object>;
        }

        static object CreatePropertySnapshot(string propertyName, object value)
        {
            return new
            {
                name = propertyName,
                value = value ?? "null"
            };
        }

        static object CreateElementSnapshot(Dictionary<string, object> selector)
        {
            return new
            {
                selector
            };
        }

        static object CreateUnknownSnapshot(string message)
        {
            return new
            {
                error = message
            };
        }

        static object GetPropertyState(string viewName, string propertyName)
        {
            try
            {
                var tool = new GetViewModelStateTool();
                var result = tool.Execute(new Dictionary<string, object>
                {
                    ["viewName"] = viewName
                }, ToolContext.CreateCurrent());

                if (!result.IsOk || result.Data == null)
                {
                    return null;
                }

                var properties = ReadPropertiesDictionary(result.Data);
                if (properties != null && properties.TryGetValue(propertyName, out var value))
                {
                    return CreatePropertySnapshot(propertyName, value);
                }

                return CreateUnknownSnapshot($"未找到属性 '{propertyName}' 的快照。");
            }
            catch (Exception exception)
            {
                return CreateUnknownSnapshot(exception.Message);
            }
        }

        static object GetElementState(Dictionary<string, object> selector)
        {
            try
            {
                var tool = new UIElementInspectorTool();
                var result = tool.Execute(new Dictionary<string, object>
                {
                    ["selector"] = selector
                }, ToolContext.CreateCurrent());

                if (!result.IsOk || result.Data == null)
                {
                    return CreateElementSnapshot(selector);
                }

                return ReadNamedValue(result.Data, "properties") ?? CreateElementSnapshot(selector);
            }
            catch (Exception exception)
            {
                return CreateUnknownSnapshot(exception.Message);
            }
        }

        static (bool success, string error) SetProperty(string viewName, string propertyName, string value)
        {
            try
            {
                var tool = new SetViewModelPropertyTool();
                var result = tool.Execute(new Dictionary<string, object>
                {
                    ["viewName"] = viewName,
                    ["propertyName"] = propertyName,
                    ["value"] = value
                }, ToolContext.CreateCurrent());

                return (result.IsOk, result.IsOk ? null : (result.ErrorInfo?.message ?? "属性设置失败。"));
            }
            catch (Exception exception)
            {
                return (false, exception.Message);
            }
        }

        static (bool success, string error) SimulateClick(Dictionary<string, object> selector)
        {
            try
            {
                var tool = new ClickElementTool();
                var result = tool.Execute(new Dictionary<string, object>
                {
                    ["selector"] = selector
                }, ToolContext.CreateCurrent());

                return (result.IsOk, result.IsOk ? null : (result.ErrorInfo?.message ?? "点击失败。"));
            }
            catch (Exception exception)
            {
                return (false, exception.Message);
            }
        }

        static (bool success, string error) SimulateInputText(Dictionary<string, object> selector, string text)
        {
            try
            {
                var tool = new InputTextTool();
                var result = tool.Execute(new Dictionary<string, object>
                {
                    ["selector"] = selector,
                    ["text"] = text,
                    ["submit"] = true
                }, ToolContext.CreateCurrent());

                return (result.IsOk, result.IsOk ? null : (result.ErrorInfo?.message ?? "输入失败。"));
            }
            catch (Exception exception)
            {
                return (false, exception.Message);
            }
        }

        static (bool success, string error) ToggleElement(Dictionary<string, object> selector)
        {
            try
            {
                if (!ClickElementTool.ResolveElementGameObject(selector, out var elementObject, out _, out var selectorError))
                {
                    return (false, selectorError?.ErrorInfo?.message ?? "元素未找到。");
                }

                var toggleComponent = elementObject.GetComponent<UnityEngine.UI.Toggle>();
                if (toggleComponent == null)
                {
                    return (false, "元素没有 Toggle 组件。");
                }

                toggleComponent.isOn = !toggleComponent.isOn;
                return (true, null);
            }
            catch (Exception exception)
            {
                return (false, exception.Message);
            }
        }
    }
}
