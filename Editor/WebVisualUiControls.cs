using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace FUI.Cli
{
    [Serializable]
    public sealed class WebControlOptions
    {
        public bool interactable=true, isOn, allowSwitchOff, blocksRaycasts=true, showMaskGraphic=true;
        public float alpha=1, spacing, cellWidth=100, cellHeight=100, aspectRatio=1, fillAmount=1, preferredWidth=-1, preferredHeight=-1, flexibleWidth=-1, flexibleHeight=-1;
        public string fillMethod="Horizontal";
        public bool fillClockwise=true, preserveAspect;
        public int fillOrigin;
        public int padding, characterLimit;
        public string lineType="SingleLine", contentType="Standard", horizontalFit="Unconstrained", verticalFit="Unconstrained", aspectMode="None";
    }
    static partial class WebVisualUiPrefabBuilder
    {
        static void ConfigureControlOptions(GameObject go, WebVisualNode node)
        {
            var c=node.control ?? new WebControlOptions();
            if(go.TryGetComponent<Image>(out var image)) { image.preserveAspect=c.preserveAspect;image.fillMethod=(Image.FillMethod)Enum.Parse(typeof(Image.FillMethod),c.fillMethod);image.fillAmount=Mathf.Clamp01(c.fillAmount);image.fillClockwise=c.fillClockwise;image.fillOrigin=c.fillOrigin; }
            if(go.TryGetComponent<UnityEngine.UI.LayoutElement>(out var layoutElement)) { layoutElement.preferredWidth=c.preferredWidth;layoutElement.preferredHeight=c.preferredHeight;layoutElement.flexibleWidth=c.flexibleWidth;layoutElement.flexibleHeight=c.flexibleHeight; }
            if(go.TryGetComponent<Selectable>(out var selectable)) selectable.interactable=c.interactable;
            if(go.TryGetComponent<Toggle>(out var toggle)) toggle.isOn=c.isOn;
            if(go.TryGetComponent<ToggleGroup>(out var group)) group.allowSwitchOff=c.allowSwitchOff;
            if(go.TryGetComponent<CanvasGroup>(out var canvas)) { canvas.alpha=c.alpha;canvas.interactable=c.interactable;canvas.blocksRaycasts=c.blocksRaycasts; }
            if(go.TryGetComponent<Mask>(out var mask)) mask.showMaskGraphic=c.showMaskGraphic;
            if(go.TryGetComponent<InputField>(out var input)) { input.characterLimit=c.characterLimit;input.contentType=(InputField.ContentType)Enum.Parse(typeof(InputField.ContentType),c.contentType);input.lineType=(InputField.LineType)Enum.Parse(typeof(InputField.LineType),c.lineType); }
            if(go.TryGetComponent<HorizontalOrVerticalLayoutGroup>(out var layout)) { layout.spacing=c.spacing;layout.padding=new RectOffset(c.padding,c.padding,c.padding,c.padding); }
            if(go.TryGetComponent<GridLayoutGroup>(out var grid)) { grid.cellSize=new Vector2(c.cellWidth,c.cellHeight);grid.spacing=Vector2.one*c.spacing;grid.padding=new RectOffset(c.padding,c.padding,c.padding,c.padding); }
            if(go.TryGetComponent<ContentSizeFitter>(out var fitter)) { fitter.horizontalFit=(ContentSizeFitter.FitMode)Enum.Parse(typeof(ContentSizeFitter.FitMode),c.horizontalFit);fitter.verticalFit=(ContentSizeFitter.FitMode)Enum.Parse(typeof(ContentSizeFitter.FitMode),c.verticalFit); }
            if(go.TryGetComponent<AspectRatioFitter>(out var aspect)) { aspect.aspectRatio=c.aspectRatio;aspect.aspectMode=(AspectRatioFitter.AspectMode)Enum.Parse(typeof(AspectRatioFitter.AspectMode),c.aspectMode); }
        }
        static readonly HashSet<string> CompositeTypes = new HashSet<string> {
            "ButtonElement", "SliderElement", "ScrollbarElement", "ToggleElement", "InputFieldElement", "DropdownElement", "ScrollView"
        };
        // Stop at nested controls: a Dropdown must not borrow its item Toggle's background.
        static IEnumerable<WebVisualNode> ControlParts(WebVisualNode node)
        {
            foreach (var child in node.children ?? new List<WebVisualNode>())
            {
                if (child == null) continue;
                yield return child;
                if (!CompositeTypes.Contains(child.element))
                    foreach (var part in ControlParts(child)) yield return part;
            }
        }
        static IEnumerable<WebVisualNode> AllParts(WebVisualNode node) => (node.children ?? new List<WebVisualNode>())
            .Where(n => n != null).SelectMany(n => new[] { n }.Concat(AllParts(n)));
        static WebVisualNode PartNode(WebVisualNode node, string role) => ControlParts(node).FirstOrDefault(n => n.part == role);
        static Transform Part(Transform root, WebVisualNode node, string role)
        {
            var part = PartNode(node, role);
            return part == null ? null : FindDescendant(root, SanitizeName(string.IsNullOrWhiteSpace(part.name) ? part.id : part.name, "Node"));
        }
        static void ValidateControlParts(WebVisualNode node, WebVisualPrefabResult result)
        {
            void Error(string code, string message) { result.ok=false;result.issues.Add(WebVisualPrefabIssue.Create(code,message,node.id)); }
            WebVisualNode Require(string role, bool required, params string[] types)
            {
                var parts=ControlParts(node).Where(n=>n.part==role).ToArray();
                if(parts.Length>1 || (required && parts.Length==0)) Error("control_part_count", "控件部件必须唯一且完整: "+role);
                if(parts.Length>0 && types.Length>0 && !types.Contains(parts[0].element)) Error("control_part_type", "控件部件类型错误: "+role);
                return parts.FirstOrDefault();
            }
            switch(node.element)
            {
                case "SliderElement":
                    var fill=Require("fill",false,"ImageElement","RawImage");
                    var sliderHandle=Require("handle",false,"ImageElement","RawImage");
                    if(fill==null && sliderHandle==null) Error("control_part_missing","Slider 至少需要 fill 或 handle。");
                    break;
                case "ScrollbarElement": Require("handle",true,"ImageElement","RawImage");break;
                case "ToggleElement": Require("checkmark",true,"ImageElement","RawImage");break;
                case "InputFieldElement": Require("text",true,"TextElement");Require("placeholder",false,"TextElement");break;
                case "DropdownElement":
                    Require("caption",true,"TextElement");
                    var template=Require("template",true,"Container","ScrollView");
                    if(template!=null) {
                        var items=AllParts(template).Where(n=>n.part=="item" && n.element=="ToggleElement").ToArray();
                        if(items.Length!=1) Error("dropdown_item_missing","Dropdown template 必须包含一个 item Toggle。");
                        else if(ControlParts(items[0]).Count(n=>n.part=="item-label" && n.element=="TextElement")!=1)
                            Error("dropdown_item_label_missing","Dropdown item 需要唯一 item-label Text。");
                    }
                    break;
                case "ScrollView":
                    var viewport=Require("viewport",true,"Container","RectMask2D","MaskElement");
                    var content=Require("content",true,"Container","VerticalLayoutGroup","HorizontalLayoutGroup","GridLayoutGroup");
                    if(viewport!=null && content!=null && !AllParts(viewport).Contains(content)) Error("scroll_content_outside_viewport","content 必须位于 viewport 子树内。");
                    Require("horizontal-scrollbar",false,"ScrollbarElement");Require("vertical-scrollbar",false,"ScrollbarElement");break;
            }
        }
        static void ConfigureDeclaredControl(GameObject go, WebVisualNode node)
        {
            Transform P(string role)=>Part(go.transform,node,role);
            T C<T>(string role) where T:Component => P(role)?.GetComponent<T>();
            var selectable=go.GetComponent<Selectable>();
            if(selectable && C<Graphic>("background")) selectable.targetGraphic=C<Graphic>("background");
            switch(node.element)
            {
                case "SliderElement":
                    var slider=go.GetComponent<Slider>();
                    var fill=C<RectTransform>("fill");var handle=C<RectTransform>("handle");
                    // The authored rect is the full-travel artwork. Driven anchors must not add
                    // that full size a second time; preserve only artwork overflow/padding.
                    if(fill) { var parent=(RectTransform)fill.parent;fill.sizeDelta-=parent.rect.size; }
                    if(handle) { var pos=handle.anchoredPosition;var size=handle.sizeDelta;var parent=(RectTransform)handle.parent;
                        if(slider.direction==Slider.Direction.LeftToRight||slider.direction==Slider.Direction.RightToLeft){pos.x=0;size.y-=parent.rect.height;}else {pos.y=0;size.x-=parent.rect.width;}
                        handle.anchoredPosition=pos;handle.sizeDelta=size; }
                    slider.fillRect=fill;slider.handleRect=handle;
                    if(C<Graphic>("handle")) slider.targetGraphic=C<Graphic>("handle");break;
                case "ScrollbarElement":
                    var bar=go.GetComponent<Scrollbar>();var barHandle=C<RectTransform>("handle");if(barHandle)barHandle.sizeDelta-=((RectTransform)barHandle.parent).rect.size;bar.handleRect=barHandle;bar.targetGraphic=C<Graphic>("handle");break;
                case "ToggleElement":
                    var toggle=go.GetComponent<Toggle>();toggle.graphic=C<Graphic>("checkmark");
                    toggle.group=go.GetComponentInParent<ToggleGroup>();break;
                case "InputFieldElement":
                    var input=go.GetComponent<InputField>();input.textComponent=C<Text>("text");input.placeholder=C<Graphic>("placeholder");break;
                case "DropdownElement":
                    var dropdown=go.GetComponent<Dropdown>();var templateNode=PartNode(node,"template");var template=P("template");
                    dropdown.captionText=C<Text>("caption");dropdown.template=template as RectTransform;
                    var itemNode=AllParts(templateNode).Single(n=>n.part=="item"&&n.element=="ToggleElement");
                    var item=FindDescendant(template,SanitizeName(string.IsNullOrWhiteSpace(itemNode.name)?itemNode.id:itemNode.name,"Node"));
                    dropdown.itemText=Part(item,itemNode,"item-label").GetComponent<Text>();
                    template.gameObject.SetActive(false);dropdown.RefreshShownValue();break;
                case "ScrollView":
                    var scroll=go.GetComponent<ScrollRect>();scroll.viewport=C<RectTransform>("viewport");scroll.content=C<RectTransform>("content");
                    if (!scroll.viewport.GetComponent<Mask>() && !scroll.viewport.GetComponent<RectMask2D>()) EnsureComponent<RectMask2D>(scroll.viewport.gameObject);
                    // A viewport must receive drag input even when its backing is transparent.
                    var hit=scroll.viewport.GetComponent<Graphic>();
                    if(!hit) { hit=EnsureComponent<Image>(scroll.viewport.gameObject);hit.color=Color.clear; }
                    hit.raycastTarget=true;
                    scroll.horizontalScrollbar=C<Scrollbar>("horizontal-scrollbar");scroll.verticalScrollbar=C<Scrollbar>("vertical-scrollbar");break;
            }
        }
    }
}
