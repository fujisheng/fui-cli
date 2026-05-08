using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor.Cli
{
    /// <summary>
    /// 诊断视图绑定有效性。
    /// </summary>
    [UnityCliTool(
        "ui_diagnose_bindings",
        Description = "Diagnose binding validity for a specific view",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class DiagnoseBindingsTool : PlayModeUnityCliTool<DiagnoseBindingsTool.Parameters>
    {
        public override string Id => "ui_diagnose_bindings";

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

            var issues = new List<object>();
            var viewEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (viewEntity == null)
            {
                return new { Success = false, Error = $"View '{parameters.viewName}' not found" };
            }

            var bindingCount = 0;
            var viewModel = UIInspectorHelpers.GetPropertyValue(viewEntity, "ViewModel");
            if (viewModel != null)
            {
                var viewModelType = viewModel.GetType();
                foreach (var property in viewModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    bindingCount++;
                    try
                    {
                        var value = property.GetValue(viewModel);
                        if (value == null)
                        {
                            issues.Add(new { severity = "info", source = "ViewModel", type = "Property", name = property.Name, message = "Property value is null" });
                        }
                    }
                    catch (Exception exception)
                    {
                        issues.Add(new { severity = "error", source = "ViewModel", type = "Property", name = property.Name, message = $"Failed to read property: {exception.Message}" });
                    }
                }

                foreach (var field in viewModelType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bindingCount++;
                    try
                    {
                        var propertyValue = field.GetValue(viewModel);
                        if (propertyValue == null)
                        {
                            issues.Add(new { severity = "warning", source = "ViewModel", type = "BindableProperty", name = field.Name, message = "BindableProperty is null" });
                        }
                    }
                    catch (Exception exception)
                    {
                        issues.Add(new { severity = "error", source = "ViewModel", type = "BindableProperty", name = field.Name, message = $"Failed to read field: {exception.Message}" });
                    }
                }
            }
            else
            {
                issues.Add(new { severity = "warning", element = parameters.viewName, message = "No ViewModel bound to view" });
            }

            foreach (var element in UIInspectorHelpers.GetElementObjects(viewEntity))
            {
                try
                {
                    var elementType = element.GetType();
                    var fields = elementType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (!field.FieldType.IsGenericType || !field.FieldType.Name.StartsWith("BindableProperty", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        bindingCount++;
                        var propertyValue = field.GetValue(element);
                        if (propertyValue == null)
                        {
                            issues.Add(new
                            {
                                severity = "warning",
                                source = "Element",
                                element = UIInspectorHelpers.GetElementName(element),
                                property = field.Name,
                                message = "BindableProperty is null"
                            });
                        }
                    }
                }
                catch (Exception exception)
                {
                    issues.Add(new
                    {
                        severity = "error",
                        element = UIInspectorHelpers.GetElementName(element),
                        message = $"Inspection error: {exception.Message}"
                    });
                }
            }

            return new
            {
                Success = true,
                Message = $"Diagnosed {bindingCount} bindings, found {issues.Count} issues",
                Data = new { issues, bindingCount }
            };
        }
    }

    /// <summary>
    /// 诊断布局溢出问题。
    /// </summary>
    [UnityCliTool(
        "ui_diagnose_layout",
        Description = "Diagnose layout overflow and rect anomalies for a specific view",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class DiagnoseLayoutTool : PlayModeUnityCliTool<DiagnoseLayoutTool.Parameters>
    {
        public override string Id => "ui_diagnose_layout";

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

            var issues = new List<object>();
            var viewEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (viewEntity == null)
            {
                return new { Success = false, Error = $"View '{parameters.viewName}' not found" };
            }

            var viewGameObject = UIElementInspectorHelpers.GetViewGameObject(viewEntity);
            if (viewGameObject == null)
            {
                return new
                {
                    Success = false,
                    Error = $"View '{parameters.viewName}' GameObject not found",
                    Message = "无法找到视图的 GameObject。可能原因：1) 视图没有关联的 GameObject 2) GameObject 名称不匹配 3) 需要使用其他查找方式"
                };
            }

            var rectTransforms = viewGameObject.GetComponentsInChildren<RectTransform>(true);
            foreach (var rectTransform in rectTransforms)
            {
                if (rectTransform.parent is RectTransform)
                {
                    var childSize = rectTransform.rect.size;
                    if (childSize.x < 0 || childSize.y < 0)
                    {
                        issues.Add(new { severity = "error", element = rectTransform.gameObject.name, message = "Negative rect size detected" });
                    }

                    if (Mathf.Approximately(childSize.x, 0f) && Mathf.Approximately(childSize.y, 0f))
                    {
                        issues.Add(new { severity = "warning", element = rectTransform.gameObject.name, message = "Zero rect size (may be intentional)" });
                    }
                }

                var anchoredPosition = rectTransform.anchoredPosition;
                if (float.IsNaN(anchoredPosition.x) || float.IsNaN(anchoredPosition.y)
                    || float.IsInfinity(anchoredPosition.x) || float.IsInfinity(anchoredPosition.y))
                {
                    issues.Add(new { severity = "error", element = rectTransform.gameObject.name, message = "Invalid anchoredPosition (NaN or Infinity)" });
                }

                var scale = rectTransform.localScale;
                if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
                {
                    issues.Add(new { severity = "warning", element = rectTransform.gameObject.name, message = "Zero scale on one or more axes" });
                }
            }

            return new
            {
                Success = true,
                Message = $"Diagnosed {rectTransforms.Length} RectTransforms, found {issues.Count} issues",
                Data = new { issues, elementCount = rectTransforms.Length }
            };
        }
    }

    /// <summary>
    /// 诊断文本溢出问题。
    /// </summary>
    [UnityCliTool(
        "ui_diagnose_text",
        Description = "Diagnose text overflow and invalid text configuration",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.ReadOnly | ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class DiagnoseTextTool : PlayModeUnityCliTool<DiagnoseTextTool.Parameters>
    {
        public override string Id => "ui_diagnose_text";

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

            var issues = new List<object>();
            var viewEntity = UIInspectorHelpers.FindViewEntity(parameters.viewName);
            if (viewEntity == null)
            {
                return new { Success = false, Error = $"View '{parameters.viewName}' not found" };
            }

            var viewGameObject = UIElementInspectorHelpers.GetViewGameObject(viewEntity);
            if (viewGameObject == null)
            {
                return new
                {
                    Success = false,
                    Error = $"View '{parameters.viewName}' GameObject not found",
                    Message = "无法找到视图的 GameObject。可能原因：1) 视图没有关联的 GameObject 2) GameObject 名称不匹配 3) 需要使用其他查找方式"
                };
            }

            var texts = viewGameObject.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text.text) && text.gameObject.activeInHierarchy)
                {
                    issues.Add(new { severity = "info", element = text.gameObject.name, message = "Empty Text component (active in hierarchy)" });
                }

                if (text.font == null)
                {
                    issues.Add(new { severity = "error", element = text.gameObject.name, message = "Text has no font assigned" });
                }

                if (!text.resizeTextForBestFit)
                {
                    var preferredWidth = text.preferredWidth;
                    var rectTransform = text.rectTransform;
                    if (text.horizontalOverflow == HorizontalWrapMode.Wrap
                        && preferredWidth > rectTransform.rect.width
                        && rectTransform.rect.width > 0)
                    {
                        issues.Add(new
                        {
                            severity = "warning",
                            element = text.gameObject.name,
                            message = $"Text may overflow horizontally (preferred: {preferredWidth:F0}, actual: {rectTransform.rect.width:F0})"
                        });
                    }
                }

                if (Mathf.Approximately(text.color.a, 0f))
                {
                    issues.Add(new { severity = "warning", element = text.gameObject.name, message = "Text color alpha is 0 (invisible)" });
                }
            }

            var tmpType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var tmpTexts = viewGameObject.GetComponentsInChildren(tmpType, true);
                foreach (var tmpText in tmpTexts)
                {
                    try
                    {
                        var textProperty = tmpType.GetProperty("text");
                        var fontProperty = tmpType.GetProperty("font");
                        var colorProperty = tmpType.GetProperty("color");

                        var textValue = textProperty?.GetValue(tmpText) as string;
                        var fontValue = fontProperty?.GetValue(tmpText);
                        var colorValue = colorProperty?.GetValue(tmpText);
                        var elementName = (tmpText as MonoBehaviour)?.gameObject?.name ?? "TMP";

                        if (string.IsNullOrEmpty(textValue))
                        {
                            issues.Add(new { severity = "info", element = elementName, message = "Empty TMP text" });
                        }

                        if (fontValue == null)
                        {
                            issues.Add(new { severity = "error", element = elementName, message = "TMP has no font assigned" });
                        }

                        if (colorValue is Color color && Mathf.Approximately(color.a, 0f))
                        {
                            issues.Add(new { severity = "warning", element = elementName, message = "TMP color alpha is 0 (invisible)" });
                        }
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
                Message = $"Diagnosed text elements, found {issues.Count} issues",
                Data = new { issues }
            };
        }
    }
}
