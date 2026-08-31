using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;

namespace FUI.Cli
{
    /// <summary>
    /// 修改 ViewModel 属性值。
    /// </summary>
    [UnityCliTool(
        "ui_set_viewmodel_property",
        Description = "Modify a ViewModel property value at runtime",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class SetViewModelPropertyTool : PlayModeUnityCliTool<SetViewModelPropertyTool.Parameters>
    {
        public override string Id => "ui_set_viewmodel_property";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }

            [UnityCliParam("Property Name")]
            public string propertyName { get; set; }

            [UnityCliParam("New Value")]
            public string value { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            var viewName = parameters?.viewName;
            var propertyName = parameters?.propertyName;
            var value = parameters?.value;

            if (string.IsNullOrEmpty(viewName) || string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(value))
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName, propertyName, and value parameters are required" };
            }

            var entities = UIInspectorHelpers.GetEntities();
            if (entities == null)
            {
                return new { Success = false, Error = "Registry resolution failed", Message = "Failed to resolve FUI.Editor.ViewInstanceRegistry." };
            }

            var targetEntity = entities.FirstOrDefault(entity => UIInspectorHelpers.GetPropertyValue(entity, "Name")?.ToString() == viewName);
            if (targetEntity == null)
            {
                return new { Success = false, Error = "View not found", Message = $"View '{viewName}' not found." };
            }

            var viewModel = UIInspectorHelpers.GetPropertyValue(targetEntity, "ViewModel");
            if (viewModel == null)
            {
                return new { Success = false, Error = "No ViewModel bound to view." };
            }

            var property = viewModel.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                try
                {
                    var convertedValue = ConvertValue(value, property.PropertyType);
                    property.SetValue(viewModel, convertedValue);
                    return new
                    {
                        Success = true,
                        Message = $"Property '{propertyName}' set to '{value}'",
                        Data = new { viewName, propertyName, value, type = "property" }
                    };
                }
                catch (Exception exception)
                {
                    return new { Success = false, Error = $"Failed to set property: {exception.Message}" };
                }
            }

            var field = viewModel.GetType().GetField(propertyName);
            if (field != null)
            {
                try
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
                                return new
                                {
                                    Success = true,
                                    Message = $"BindableProperty '{propertyName}' set to '{value}'",
                                    Data = new { viewName, propertyName, value, type = "bindableProperty" }
                                };
                            }
                        }
                    }
                    else
                    {
                        var convertedValue = ConvertValue(value, field.FieldType);
                        field.SetValue(viewModel, convertedValue);
                        return new
                        {
                            Success = true,
                            Message = $"Field '{propertyName}' set to '{value}'",
                            Data = new { viewName, propertyName, value, type = "field" }
                        };
                    }
                }
                catch (Exception exception)
                {
                    return new { Success = false, Error = $"Failed to set field: {exception.Message}" };
                }
            }

            return new { Success = false, Error = $"Property or field '{propertyName}' not found on ViewModel." };
        }

        static object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(double)) return double.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (targetType == typeof(long)) return long.Parse(value);
            if (targetType.IsEnum) return Enum.Parse(targetType, value);
            return Convert.ChangeType(value, targetType);
        }
    }
}
