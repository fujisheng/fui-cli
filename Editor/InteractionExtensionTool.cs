using System;
using System.Collections.Generic;
using System.Linq;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FUI.Cli
{
    /// <summary>
    /// 扩展 UI 交互操作：Toggle、Slider、Dropdown、ScrollRect、Drag。
    /// </summary>
    [UnityCliTool(
        "ui.toggle_element",
        Description = "Toggle the state of a Toggle UI element",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class ToggleElementTool : PlayModeUnityCliTool<ToggleElementTool.Parameters>
    {
        public override string Id => "ui.toggle_element";

        public class Parameters
        {
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }

            [UnityCliParam("Target state (true/false, empty = flip)", Required = false)]
            public string targetState { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (parameters?.selector == null)
            {
                return ToolResult.Error("invalid_parameter", "selector 参数是必需的。");
            }

            if (!ClickElementTool.ResolveElementGameObject(parameters.selector, out var elementObject, out var selection, out var selectorError))
            {
                return selectorError;
            }

            var toggle = elementObject.GetComponent<Toggle>();
            if (toggle == null)
            {
                return new { Success = false, Error = "Not a Toggle", Message = "元素没有 Toggle 组件。" };
            }

            var beforeState = toggle.isOn;
            bool targetValue;
            if (!string.IsNullOrWhiteSpace(parameters.targetState) && bool.TryParse(parameters.targetState, out var explicitState))
            {
                targetValue = explicitState;
            }
            else
            {
                targetValue = !toggle.isOn;
            }

            toggle.isOn = targetValue;

            return new
            {
                Success = true,
                Message = $"Toggle 状态从 {beforeState} 变为 {targetValue}。",
                Data = new
                {
                    selector = selection.Selector,
                    targetPath = selection.TargetPath,
                    before = beforeState,
                    after = targetValue
                }
            };
        }
    }

    [UnityCliTool(
        "ui.set_slider_value",
        Description = "Set a Slider UI element value",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class SetSliderValueTool : PlayModeUnityCliTool<SetSliderValueTool.Parameters>
    {
        public override string Id => "ui.set_slider_value";

        public class Parameters
        {
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }

            [UnityCliParam("Slider value")]
            public float value { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (parameters?.selector == null)
            {
                return ToolResult.Error("invalid_parameter", "selector 参数是必需的。");
            }

            if (!ClickElementTool.ResolveElementGameObject(parameters.selector, out var elementObject, out var selection, out var selectorError))
            {
                return selectorError;
            }

            var slider = elementObject.GetComponent<Slider>();
            if (slider == null)
            {
                return new { Success = false, Error = "Not a Slider", Message = "元素没有 Slider 组件。" };
            }

            var beforeValue = slider.value;
            slider.value = Mathf.Clamp(parameters.value, slider.minValue, slider.maxValue);

            return new
            {
                Success = true,
                Message = $"Slider 值从 {beforeValue:F2} 变为 {slider.value:F2}。",
                Data = new
                {
                    selector = selection.Selector,
                    targetPath = selection.TargetPath,
                    before = beforeValue,
                    after = slider.value,
                    minValue = slider.minValue,
                    maxValue = slider.maxValue
                }
            };
        }
    }

    [UnityCliTool(
        "ui.select_dropdown_option",
        Description = "Select a dropdown option by index or value name",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class SelectDropdownOptionTool : PlayModeUnityCliTool<SelectDropdownOptionTool.Parameters>
    {
        public override string Id => "ui.select_dropdown_option";

        public class Parameters
        {
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }

            [UnityCliParam("Option index (0-based)", Required = false)]
            public int optionIndex { get; set; } = -1;

            [UnityCliParam("Option text (alternative to index)", Required = false)]
            public string optionText { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (parameters?.selector == null)
            {
                return ToolResult.Error("invalid_parameter", "selector 参数是必需的。");
            }

            if (!ClickElementTool.ResolveElementGameObject(parameters.selector, out var elementObject, out var selection, out var selectorError))
            {
                return selectorError;
            }

            var dropdown = elementObject.GetComponent<Dropdown>();
            if (dropdown == null)
            {
                return new { Success = false, Error = "Not a Dropdown", Message = "元素没有 Dropdown 组件。" };
            }

            var beforeIndex = dropdown.value;
            var beforeValue = beforeIndex >= 0 && beforeIndex < dropdown.options.Count
                ? dropdown.options[beforeIndex].text
                : "none";

            int targetIndex;
            if (parameters.optionIndex >= 0)
            {
                targetIndex = Mathf.Clamp(parameters.optionIndex, 0, dropdown.options.Count - 1);
            }
            else if (!string.IsNullOrWhiteSpace(parameters.optionText))
            {
                targetIndex = dropdown.options.FindIndex(option =>
                    string.Equals(option.text, parameters.optionText, StringComparison.OrdinalIgnoreCase));
                if (targetIndex < 0)
                {
                    return new { Success = false, Error = "Option not found", Message = $"选项 '{parameters.optionText}' 未找到。" };
                }
            }
            else
            {
                return new { Success = false, Error = "Missing selection", Message = "请提供 optionIndex 或 optionText。" };
            }

            dropdown.value = targetIndex;
            dropdown.RefreshShownValue();
            var afterValue = dropdown.options[targetIndex].text;

            return new
            {
                Success = true,
                Message = $"Dropdown 从 '{beforeValue}' 变为 '{afterValue}'。",
                Data = new
                {
                    selector = selection.Selector,
                    targetPath = selection.TargetPath,
                    before = new { index = beforeIndex, text = beforeValue },
                    after = new { index = targetIndex, text = afterValue },
                    totalOptions = dropdown.options.Count
                }
            };
        }
    }

    [UnityCliTool(
        "ui.scroll_to",
        Description = "Scroll a ScrollRect to a target normalized position",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class ScrollToTool : PlayModeUnityCliTool<ScrollToTool.Parameters>
    {
        public override string Id => "ui.scroll_to";

        public class Parameters
        {
            [UnityCliParam("Element selector: { view, element, itemIndex?, child? }")]
            public Dictionary<string, object> selector { get; set; }

            [UnityCliParam("Target normalized position X (0-1)")]
            public float normalizedX { get; set; }

            [UnityCliParam("Target normalized position Y (0-1)")]
            public float normalizedY { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (parameters?.selector == null)
            {
                return ToolResult.Error("invalid_parameter", "selector 参数是必需的。");
            }

            if (!ClickElementTool.ResolveElementGameObject(parameters.selector, out var elementObject, out var selection, out var selectorError))
            {
                return selectorError;
            }

            var scrollRect = elementObject.GetComponent<ScrollRect>();
            if (scrollRect == null)
            {
                return new { Success = false, Error = "Not a ScrollRect", Message = "元素没有 ScrollRect 组件。" };
            }

            var beforePosition = scrollRect.normalizedPosition;
            var targetPosition = new Vector2(
                Mathf.Clamp01(parameters.normalizedX),
                Mathf.Clamp01(parameters.normalizedY));
            scrollRect.normalizedPosition = targetPosition;

            return new
            {
                Success = true,
                Message = $"ScrollRect 从 ({beforePosition.x:F2}, {beforePosition.y:F2}) 滚到 ({targetPosition.x:F2}, {targetPosition.y:F2})。",
                Data = new
                {
                    selector = selection.Selector,
                    targetPath = selection.TargetPath,
                    before = new { x = beforePosition.x, y = beforePosition.y },
                    after = new { x = targetPosition.x, y = targetPosition.y }
                }
            };
        }
    }

    [UnityCliTool(
        "ui.drag_element",
        Description = "Simulate drag from one screen position to another",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class DragElementTool : PlayModeUnityCliTool<DragElementTool.Parameters>
    {
        public override string Id => "ui.drag_element";

        public class Parameters
        {
            [UnityCliParam("Start X (normalized 0-1)")]
            public float startX { get; set; }

            [UnityCliParam("Start Y (normalized 0-1)")]
            public float startY { get; set; }

            [UnityCliParam("End X (normalized 0-1)")]
            public float endX { get; set; }

            [UnityCliParam("End Y (normalized 0-1)")]
            public float endY { get; set; }

            [UnityCliParam("Drag duration seconds", Required = false)]
            public float duration { get; set; } = 0.5f;

            [UnityCliParam("Drag steps", Required = false)]
            public int steps { get; set; } = 20;
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (EventSystem.current == null)
            {
                return new { Success = false, Error = "No EventSystem", Message = "场景中没有 EventSystem。" };
            }

            var startPosition = new Vector2(
                Mathf.Clamp01(parameters.startX) * Screen.width,
                Mathf.Clamp01(parameters.startY) * Screen.height);
            var endPosition = new Vector2(
                Mathf.Clamp01(parameters.endX) * Screen.width,
                Mathf.Clamp01(parameters.endY) * Screen.height);

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = startPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            var target = results.FirstOrDefault().gameObject;
            if (target == null)
            {
                return new { Success = false, Error = "No target", Message = "起始位置没有命中任何 UI 元素。" };
            }

            try
            {
                var stepCount = UnityCliParameterBinder.HasArgument(args, nameof(Parameters.steps))
                    ? Mathf.Clamp(parameters.steps, 1, 240)
                    : Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp(parameters.duration, 0.05f, 10f) * 60f), 1, 240);

                DragSimulateHelper.SimulateDrag(target, startPosition, endPosition, stepCount);
                return new
                {
                    Success = true,
                    Message = "拖动完成。",
                    Data = new
                    {
                        target = target.name,
                        start = new { x = startPosition.x, y = startPosition.y },
                        end = new { x = endPosition.x, y = endPosition.y },
                        parameters.duration,
                        steps = stepCount
                    }
                };
            }
            catch (Exception exception)
            {
                return new { Success = false, Error = "Drag failed", Message = $"拖动失败: {exception.Message}" };
            }
        }
    }

    /// <summary>
    /// Drag simulate 辅助方法（作为 SwipeScreenTool 的 internal 扩展）。
    /// </summary>
    internal static class DragSimulateHelper
    {
        public static void SimulateDrag(GameObject target, Vector2 start, Vector2 end, int steps)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = start,
                pressPosition = start,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                pointerPress = target,
                pointerDrag = target,
                pointerCurrentRaycast = new RaycastResult
                {
                    gameObject = target,
                    screenPosition = start,
                    worldPosition = target.transform.position
                }
            };

            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.beginDragHandler);

            for (var index = 1; index <= steps; index++)
            {
                var progress = (float)index / steps;
                var nextPosition = Vector2.Lerp(start, end, progress);
                pointerData.delta = nextPosition - pointerData.position;
                pointerData.position = nextPosition;
                ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.dragHandler);
            }

            pointerData.position = end;
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.dropHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
        }
    }
}
