using System;
using System.Collections.Generic;
using System.Linq;
using UnityCli.Editor.Attributes;
using UnityCli.Editor.Core;
using UnityCli.Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Editor.Cli
{
    /// <summary>
    /// 模拟点击 UI 元素。
    /// </summary>
    [UnityCliTool(
        "ui_click_element",
        Description = "Simulate a real screen click on a UI element",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class ClickElementTool : PlayModeUnityCliTool<ClickElementTool.Parameters>
    {
        public override string Id => "ui_click_element";

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }

            [UnityCliParam("Element Name")]
            public string elementName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName) || string.IsNullOrEmpty(parameters?.elementName))
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName and elementName parameters are required" };
            }

            if (EventSystem.current == null)
            {
                return new { Success = false, Error = "No EventSystem found", Message = "场景中没有 EventSystem，无法模拟点击" };
            }

            var elementObject = ResolveElementGameObject(parameters.viewName, parameters.elementName);
            if (elementObject == null)
            {
                return new { Success = false, Error = $"Element '{parameters.elementName}' not found in '{parameters.viewName}'" };
            }

            if (!elementObject.activeInHierarchy)
            {
                return new { Success = false, Error = "Element is inactive", Message = "元素在场景中未激活 (GameObject.activeInHierarchy is false)" };
            }

            var rectTransform = elementObject.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return new { Success = false, Error = "Element has no RectTransform", Message = "元素没有 RectTransform 组件" };
            }

            var screenPosition = RectTransformUtility.WorldToScreenPoint(null, rectTransform.TransformPoint(rectTransform.rect.center));
            var blockingElement = UIInteractionHelpers.GetBlockingElement(elementObject, screenPosition);
            var isOccluded = blockingElement != null;
            string occlusionWarning = null;
            if (isOccluded)
            {
                occlusionWarning = $"Warning: Element appears to be blocked by '{blockingElement.name}' (Layer: {blockingElement.layer}, Path: {GetGameObjectPath(blockingElement)}). Click simulation will proceed anyway.";
                Debug.LogWarning($"[UIInteractionTool] {occlusionWarning}");
            }

            var button = elementObject.GetComponent<Button>();
            var toggle = elementObject.GetComponent<Toggle>();
            var clickHandlers = elementObject.GetComponents<IPointerClickHandler>();
            if (button == null && toggle == null && clickHandlers.Length == 0)
            {
                return new { Success = false, Error = "Element is not clickable", Message = "元素没有 Button、Toggle 或 IPointerClickHandler 组件" };
            }

            if (button != null && !button.interactable)
            {
                return new { Success = false, Error = "Element is not interactable", Message = "Button 组件 interactable = false" };
            }

            if (toggle != null && !toggle.interactable)
            {
                return new { Success = false, Error = "Element is not interactable", Message = "Toggle 组件 interactable = false" };
            }

            try
            {
                SimulateRealClick(elementObject, screenPosition);
                return new
                {
                    Success = true,
                    Message = isOccluded ? occlusionWarning : "点击成功",
                    Data = new
                    {
                        targetElement = parameters.elementName,
                        screenPosition = new { x = screenPosition.x, y = screenPosition.y },
                        clickedVia = button != null ? "Button" : toggle != null ? "Toggle" : "PointerClickHandler",
                        isOccluded,
                        blockingElement = blockingElement?.name
                    }
                };
            }
            catch (Exception exception)
            {
                return new { Success = false, Error = "Click simulation failed", Message = $"点击模拟失败: {exception.Message}" };
            }
        }

        internal static GameObject ResolveElementGameObject(string viewName, string elementName)
        {
            var element = UIElementInspectorHelpers.FindElement(viewName, elementName);
            var elementObject = UIElementInspectorHelpers.GetElementGameObject(element);
            if (elementObject != null)
            {
                return elementObject;
            }

            return UIInteractionHelpers.FindChildInAllCanvases(viewName, elementName);
        }

        static string GetGameObjectPath(GameObject gameObject)
        {
            var path = gameObject.name;
            while (gameObject.transform.parent != null)
            {
                gameObject = gameObject.transform.parent.gameObject;
                path = gameObject.name + "/" + path;
            }

            return path;
        }

        static void SimulateRealClick(GameObject target, Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                pressPosition = screenPosition,
                pointerCurrentRaycast = new RaycastResult
                {
                    gameObject = target,
                    screenPosition = screenPosition,
                    worldPosition = target.transform.position
                }
            };

            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerDownHandler);
            eventSystem.SetSelectedGameObject(target, pointerData);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerExitHandler);
        }
    }

    /// <summary>
    /// 模拟在 UI 元素中输入文本。
    /// </summary>
    [UnityCliTool(
        "ui_input_text",
        Description = "Simulate text input into a UI element",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class InputTextTool : PlayModeUnityCliTool<InputTextTool.Parameters>
    {
        public override string Id => "ui_input_text";

        static readonly Type TmpInputFieldType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");

        public class Parameters
        {
            [UnityCliParam("View Name")]
            public string viewName { get; set; }

            [UnityCliParam("Element Name")]
            public string elementName { get; set; }

            [UnityCliParam("Text to input")]
            public string text { get; set; }

            [UnityCliParam("Submit (true/false)", Required = false)]
            public bool submit { get; set; } = true;
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(parameters?.viewName) || string.IsNullOrEmpty(parameters?.elementName) || parameters.text == null)
            {
                return new { Success = false, Error = "Missing parameters", Message = "viewName, elementName, and text parameters are required" };
            }

            var elementObject = ClickElementTool.ResolveElementGameObject(parameters.viewName, parameters.elementName);
            if (elementObject == null)
            {
                return new { Success = false, Error = $"Element '{parameters.elementName}' not found in '{parameters.viewName}'" };
            }

            var inputField = elementObject.GetComponent<InputField>();
            if (inputField != null)
            {
                inputField.text = parameters.text;
                if (parameters.submit)
                {
                    inputField.onEndEdit.Invoke(parameters.text);
                    inputField.onSubmit.Invoke(parameters.text);
                }

                return new { Success = true, Message = "Input text set via InputField" };
            }

            if (TmpInputFieldType != null)
            {
                var tmpInput = elementObject.GetComponent(TmpInputFieldType);
                if (tmpInput != null)
                {
                    try
                    {
                        var textProperty = TmpInputFieldType.GetProperty("text");
                        textProperty?.SetValue(tmpInput, parameters.text);

                        if (parameters.submit)
                        {
                            var onEndEditEvent = TmpInputFieldType.GetProperty("onEndEdit")?.GetValue(tmpInput);
                            var onSubmitEvent = TmpInputFieldType.GetProperty("onSubmit")?.GetValue(tmpInput);
                            InvokeUnityEvent(onEndEditEvent, parameters.text);
                            InvokeUnityEvent(onSubmitEvent, parameters.text);
                        }

                        return new { Success = true, Message = "Input text set via TMP_InputField" };
                    }
                    catch (Exception exception)
                    {
                        return new { Success = false, Error = $"TMP_InputField operation failed: {exception.Message}" };
                    }
                }
            }

            return new { Success = false, Error = "Element is not an InputField (no Unity UI InputField or TMP_InputField found)" };
        }

        static void InvokeUnityEvent(object unityEvent, string parameter)
        {
            if (unityEvent == null)
            {
                return;
            }

            try
            {
                var invokeMethod = unityEvent.GetType().GetMethod("Invoke", new[] { typeof(string) });
                invokeMethod?.Invoke(unityEvent, new object[] { parameter });
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 模拟屏幕滑动。
    /// </summary>
    [UnityCliTool(
        "ui_swipe_screen",
        Description = "Simulate a swipe gesture on screen coordinates",
        Mode = ToolMode.PlayOnly,
        Capabilities = ToolCapabilities.PlayMode,
        Category = "ui")]
    public sealed class SwipeScreenTool : PlayModeUnityCliTool<SwipeScreenTool.Parameters>
    {
        public override string Id => "ui_swipe_screen";

        public class Parameters
        {
            [UnityCliParam("Start X (screen px or normalized 0-1)")]
            public float startX { get; set; }

            [UnityCliParam("Start Y (screen px or normalized 0-1)")]
            public float startY { get; set; }

            [UnityCliParam("End X (screen px or normalized 0-1)")]
            public float endX { get; set; }

            [UnityCliParam("End Y (screen px or normalized 0-1)")]
            public float endY { get; set; }

            [UnityCliParam("Use normalized coordinates (true/false)", Required = false)]
            public bool normalized { get; set; } = true;

            [UnityCliParam("Swipe duration seconds", Required = false)]
            public float duration { get; set; } = 0.25f;

            [UnityCliParam("Swipe interpolation steps", Required = false)]
            public int steps { get; set; } = 12;

            [UnityCliParam("Optional View Name", Required = false)]
            public string viewName { get; set; }

            [UnityCliParam("Optional Element Name", Required = false)]
            public string elementName { get; set; }
        }

        protected override object ExecuteCommand(Parameters parameters, ToolContext context, Dictionary<string, object> args)
        {
            if (EventSystem.current == null)
            {
                return new { Success = false, Error = "No EventSystem found", Message = "场景中没有 EventSystem，无法模拟滑动" };
            }

            if (parameters == null)
            {
                return new { Success = false, Error = "Invalid parameters" };
            }

            if (parameters.duration <= 0f || parameters.duration > 10f)
            {
                return new { Success = false, Error = "Invalid duration", Message = "duration 必须在 (0, 10] 秒范围内" };
            }

            var hasExplicitSteps = UnityCliMigrationUtilities.HasArgument(args, nameof(Parameters.steps));
            var stepCount = hasExplicitSteps
                ? parameters.steps
                : Mathf.Clamp(Mathf.CeilToInt(parameters.duration * 60f), 1, 240);
            if (stepCount < 1 || stepCount > 240)
            {
                return new { Success = false, Error = "Invalid steps", Message = "steps 必须在 [1, 240] 范围内" };
            }

            var startPosition = ToScreenPosition(parameters.startX, parameters.startY, parameters.normalized);
            var endPosition = ToScreenPosition(parameters.endX, parameters.endY, parameters.normalized);
            if (!IsScreenPositionValid(startPosition) || !IsScreenPositionValid(endPosition))
            {
                return new
                {
                    Success = false,
                    Error = "Invalid screen position",
                    Message = $"坐标超出屏幕范围。start=({startPosition.x:F1},{startPosition.y:F1}), end=({endPosition.x:F1},{endPosition.y:F1}), screen=({Screen.width},{Screen.height})"
                };
            }

            var target = ResolveSwipeTarget(parameters, startPosition);
            if (target == null)
            {
                return new
                {
                    Success = false,
                    Error = "No swipe target found",
                    Message = "未找到可响应拖拽的 UI 元素，请检查 viewName/elementName 或起始坐标是否命中 UI"
                };
            }

            if (!target.activeInHierarchy)
            {
                return new { Success = false, Error = "Target is inactive", Message = "目标元素未激活，无法滑动" };
            }

            try
            {
                SimulateSwipe(target, startPosition, endPosition, stepCount);
                return new
                {
                    Success = true,
                    Message = "滑动成功",
                    Data = new
                    {
                        target = target.name,
                        start = new { x = startPosition.x, y = startPosition.y },
                        end = new { x = endPosition.x, y = endPosition.y },
                        parameters.normalized,
                        parameters.duration,
                        steps = stepCount
                    }
                };
            }
            catch (Exception exception)
            {
                return new { Success = false, Error = "Swipe simulation failed", Message = $"滑动模拟失败: {exception.Message}" };
            }
        }

        static Vector2 ToScreenPosition(float x, float y, bool normalized)
        {
            return normalized
                ? new Vector2(x * Screen.width, y * Screen.height)
                : new Vector2(x, y);
        }

        static bool IsScreenPositionValid(Vector2 position)
        {
            return position.x >= 0f && position.x <= Screen.width && position.y >= 0f && position.y <= Screen.height;
        }

        static GameObject ResolveSwipeTarget(Parameters parameters, Vector2 startPosition)
        {
            if (!string.IsNullOrEmpty(parameters.viewName) && !string.IsNullOrEmpty(parameters.elementName))
            {
                var element = UIElementInspectorHelpers.FindElement(parameters.viewName, parameters.elementName);
                var elementObject = UIElementInspectorHelpers.GetElementGameObject(element);
                if (elementObject != null)
                {
                    return elementObject;
                }

                return UIInteractionHelpers.FindChildInAllCanvases(parameters.viewName, parameters.elementName);
            }

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = startPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            return results.FirstOrDefault().gameObject;
        }

        static void SimulateSwipe(GameObject target, Vector2 startPosition, Vector2 endPosition, int steps)
        {
            var eventSystem = EventSystem.current;
            var pointerData = new PointerEventData(eventSystem)
            {
                position = startPosition,
                pressPosition = startPosition,
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                pointerPress = target,
                pointerDrag = target,
                pointerCurrentRaycast = new RaycastResult
                {
                    gameObject = target,
                    screenPosition = startPosition,
                    worldPosition = target.transform.position
                },
                pointerPressRaycast = new RaycastResult
                {
                    gameObject = target,
                    screenPosition = startPosition,
                    worldPosition = target.transform.position
                }
            };

            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.beginDragHandler);

            for (var index = 1; index <= steps; index++)
            {
                var progress = index / (float)steps;
                var nextPosition = Vector2.Lerp(startPosition, endPosition, progress);
                pointerData.delta = nextPosition - pointerData.position;
                pointerData.position = nextPosition;
                ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.dragHandler);
            }

            var endRaycast = GetTopRaycastResult(endPosition);
            pointerData.pointerCurrentRaycast = endRaycast;
            if (endRaycast.gameObject != null)
            {
                ExecuteEvents.ExecuteHierarchy(endRaycast.gameObject, pointerData, ExecuteEvents.dropHandler);
            }

            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.endDragHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerExitHandler);
        }

        static RaycastResult GetTopRaycastResult(Vector2 screenPosition)
        {
            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            return results.FirstOrDefault();
        }
    }

    /// <summary>
    /// UI 交互辅助方法。
    /// </summary>
    internal static class UIInteractionHelpers
    {
        public static GameObject FindChildInAllCanvases(string viewName, string elementName)
        {
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            foreach (var canvas in canvases)
            {
                if (ContainsIgnoreCase(canvas.name, viewName) || ContainsIgnoreCase(viewName, canvas.name))
                {
                    var found = FindChildRecursive(canvas.transform, elementName);
                    if (found != null)
                    {
                        return found;
                    }
                }

                var parent = canvas.transform.parent;
                if (parent != null && (ContainsIgnoreCase(parent.name, viewName) || ContainsIgnoreCase(viewName, parent.name)))
                {
                    var found = FindChildRecursive(canvas.transform, elementName);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            var directFind = GameObject.Find(elementName);
            if (directFind != null)
            {
                return directFind;
            }

            var viewGameObject = GameObject.Find(viewName);
            if (viewGameObject != null)
            {
                var found = FindChildRecursive(viewGameObject.transform, elementName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static GameObject FindChildRecursive(Transform parent, string name)
        {
            if (name.Contains("/", StringComparison.Ordinal))
            {
                var parts = name.Split(new[] { '/' }, 2);
                var candidates = new List<Transform>();
                FindAllChildrenRecursive(parent, parts[0], candidates);
                foreach (var candidate in candidates)
                {
                    var target = candidate.Find(parts[1]);
                    if (target != null)
                    {
                        return target.gameObject;
                    }
                }

                return null;
            }

            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child.name == name)
                {
                    return child.gameObject;
                }

                var found = FindChildRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        static void FindAllChildrenRecursive(Transform parent, string name, List<Transform> results)
        {
            for (var index = 0; index < parent.childCount; index++)
            {
                var child = parent.GetChild(index);
                if (child.name == name)
                {
                    results.Add(child);
                }

                FindAllChildrenRecursive(child, name, results);
            }
        }

        public static (GameObject blockedBy, List<GameObject> hits) RaycastAtScreenPoint(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return (null, new List<GameObject>());
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            var hitObjects = new List<GameObject>();
            foreach (var result in results)
            {
                if (result.gameObject != null)
                {
                    hitObjects.Add(result.gameObject);
                }
            }

            var blockedBy = hitObjects.Count > 0 ? hitObjects[0] : null;
            return (blockedBy, hitObjects);
        }

        public static GameObject GetBlockingElement(GameObject target, Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return null;
            }

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, results);

            foreach (var result in results)
            {
                var hitObject = result.gameObject;
                if (hitObject == null || hitObject == target)
                {
                    continue;
                }

                if (hitObject.transform.IsChildOf(target.transform))
                {
                    continue;
                }

                if (target.transform.IsChildOf(hitObject.transform))
                {
                    var parentButton = hitObject.GetComponent<Button>();
                    var parentToggle = hitObject.GetComponent<Toggle>();
                    var parentClickHandler = hitObject.GetComponent<IPointerClickHandler>();
                    if (parentButton == null && parentToggle == null && parentClickHandler == null)
                    {
                        continue;
                    }
                }

                return hitObject;
            }

            return null;
        }

        static bool ContainsIgnoreCase(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
