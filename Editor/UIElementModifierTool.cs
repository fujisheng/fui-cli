using System;
using System.Collections.Generic;
using System.Reflection;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;

namespace Game.Editor.Cli
{
    /// <summary>
    /// 修改 UI 元素的 BindableProperty 值。
    /// </summary>
    [UnityCliTool(
        "ui_modify_element",
        Description = "Modify a UI element BindableProperty value at runtime",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class ModifyElementTool : PlayModeUnityCliTool<ModifyElementTool.Parameters>
    {
        public override string Id => "ui_modify_element";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }

            [UnityCliParam("Element Name")]
            public string elementName { get; set; }

            [UnityCliParam("Property Name")]
            public string propertyName { get; set; }

            [UnityCliParam("New Value")]
            public string value { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName) || string.IsNullOrEmpty(parameters?.elementName)
                || string.IsNullOrEmpty(parameters?.propertyName) || string.IsNullOrEmpty(parameters?.value))
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName, elementName, propertyName, and value parameters are required" };
            }

            var element = UIElementInspectorHelpers.FindElement(parameters.viewName, parameters.elementName);
            if (element == null)
            {
                return new { Success = false, Error = "Element not found" };
            }

            var type = element.GetType();
            var field = type.GetField(parameters.propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                return new { Success = false, Error = $"Property '{parameters.propertyName}' not found" };
            }

            var bindableProperty = field.GetValue(element);
            if (bindableProperty == null)
            {
                return new { Success = false, Error = "BindableProperty is null" };
            }

            var valueProperty = bindableProperty.GetType().GetProperty("Value");
            if (valueProperty == null)
            {
                return new { Success = false, Error = "Not a BindableProperty" };
            }

            try
            {
                var targetType = valueProperty.PropertyType;
                object convertedValue;

                if (targetType == typeof(string)) convertedValue = parameters.value;
                else if (targetType == typeof(int)) convertedValue = int.Parse(parameters.value);
                else if (targetType == typeof(float)) convertedValue = float.Parse(parameters.value);
                else if (targetType == typeof(double)) convertedValue = double.Parse(parameters.value);
                else if (targetType == typeof(bool)) convertedValue = bool.Parse(parameters.value);
                else if (targetType == typeof(long)) convertedValue = long.Parse(parameters.value);
                else if (targetType == typeof(Color)) convertedValue = ParseColor(parameters.value);
                else if (targetType == typeof(Vector2)) convertedValue = ParseVector2(parameters.value);
                else if (targetType == typeof(Vector3)) convertedValue = ParseVector3(parameters.value);
                else if (targetType == typeof(Vector4)) convertedValue = ParseVector4(parameters.value);
                else if (targetType == typeof(Quaternion)) convertedValue = ParseQuaternion(parameters.value);
                else if (targetType == typeof(Rect)) convertedValue = ParseRect(parameters.value);
                else if (targetType.IsEnum) convertedValue = Enum.Parse(targetType, parameters.value);
                else convertedValue = Convert.ChangeType(parameters.value, targetType);

                var setValueMethod = bindableProperty.GetType().GetMethod("SetValue", new[] { typeof(object) });
                if (setValueMethod != null)
                {
                    setValueMethod.Invoke(bindableProperty, new[] { convertedValue });
                }
                else
                {
                    valueProperty.SetValue(bindableProperty, convertedValue);
                }

                return new { Success = true, Message = "Value updated", Data = new { propertyName = parameters.propertyName, value = convertedValue.ToString() } };
            }
            catch (Exception exception)
            {
                return new { Success = false, Error = $"Error setting value: {exception.Message}" };
            }
        }

        static Color ParseColor(string value)
        {
            value = value.Trim();
            if (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal))
            {
                var parts = value.Trim('(', ')').Split(',');
                if (parts.Length >= 3)
                {
                    var r = float.Parse(parts[0].Trim());
                    var g = float.Parse(parts[1].Trim());
                    var b = float.Parse(parts[2].Trim());
                    var a = parts.Length > 3 ? float.Parse(parts[3].Trim()) : 1f;
                    return new Color(r, g, b, a);
                }
            }

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                return ParseHexColor(value);
            }

            if (ColorUtility.TryParseHtmlString(value, out var color))
            {
                return color;
            }

            throw new FormatException($"Cannot parse color: {value}");
        }

        static Color ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                hex += "FF";
            }

            if (hex.Length != 8)
            {
                throw new FormatException($"Invalid hex color: #{hex}");
            }

            var r = Convert.ToByte(hex.Substring(0, 2), 16);
            var g = Convert.ToByte(hex.Substring(2, 2), 16);
            var b = Convert.ToByte(hex.Substring(4, 2), 16);
            var a = Convert.ToByte(hex.Substring(6, 2), 16);
            return new Color32(r, g, b, a);
        }

        static Vector2 ParseVector2(string value)
        {
            value = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = value.Split(',');
            if (parts.Length >= 2)
            {
                return new Vector2(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()));
            }

            throw new FormatException($"Cannot parse Vector2: {value}");
        }

        static Vector3 ParseVector3(string value)
        {
            value = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = value.Split(',');
            if (parts.Length >= 3)
            {
                return new Vector3(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()));
            }

            throw new FormatException($"Cannot parse Vector3: {value}");
        }

        static Vector4 ParseVector4(string value)
        {
            value = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = value.Split(',');
            if (parts.Length >= 4)
            {
                return new Vector4(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()), float.Parse(parts[3].Trim()));
            }

            throw new FormatException($"Cannot parse Vector4: {value}");
        }

        static Quaternion ParseQuaternion(string value)
        {
            value = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = value.Split(',');
            if (parts.Length == 3)
            {
                return Quaternion.Euler(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()));
            }

            if (parts.Length == 4)
            {
                return new Quaternion(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()), float.Parse(parts[3].Trim()));
            }

            throw new FormatException($"Cannot parse Quaternion: {value}");
        }

        static Rect ParseRect(string value)
        {
            value = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = value.Split(',');
            if (parts.Length >= 4)
            {
                return new Rect(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()), float.Parse(parts[3].Trim()));
            }

            throw new FormatException($"Cannot parse Rect: {value}");
        }
    }
}
