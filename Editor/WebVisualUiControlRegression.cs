using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FUI.Cli
{
    static partial class WebVisualUiPrefabBuilder
    {
        // Disposable in-memory fixtures exercise the same creation path as the official tool.
        // No prefab, scene or importer is saved by this regression menu.
        [MenuItem("FUI/Validate UGUI Control Structure")]
        static void RunControlRegression()
        {
            var root=new GameObject("Control regression",typeof(RectTransform));root.hideFlags=HideFlags.HideAndDontSave;
            int checks=0;
            void Check(bool condition,string message) { if(!condition)throw new InvalidOperationException(message);checks++; }
            WebVisualNode N(string id,string type="Container",string role="",params WebVisualNode[] children) => new WebVisualNode {
                id=id,name=id,element=type,part=role,rect=new WebVisualRect {width=200,height=80},children=children.ToList(),style=new WebVisualStyle { color="#FFFFFF",alpha=1 }
            };
            try
            {
                var fixtures=new[] {
                    N("Slider","SliderElement","",N("Track","ImageElement","background"),N("FillArea","Container","",N("Fill","ImageElement","fill")),N("HandleArea","Container","",N("Handle","ImageElement","handle"))),
                    N("Bar","ScrollbarElement","",N("BarHandle","ImageElement","handle")),
                    N("Group","ToggleGroup","",N("Toggle","ToggleElement","",N("Check","ImageElement","checkmark"))),
                    N("Input","InputFieldElement","",N("InputText","TextElement","text"),N("Hint","TextElement","placeholder")),
                    N("Scroll","ScrollView","",N("Viewport","RectMask2D","viewport",N("Content","Container","content"))),
                    N("Dropdown","DropdownElement","",N("Caption","TextElement","caption"),N("Template","Container","template",N("Item","ToggleElement","item",N("ItemCheck","ImageElement","checkmark"),N("ItemLabel","TextElement","item-label")))),
                    N("Raw","RawImage"),N("CanvasGroup","CanvasGroup"),N("Horizontal","HorizontalLayoutGroup"),N("Vertical","VerticalLayoutGroup"),N("Grid","GridLayoutGroup"),N("Fit","ContentSizeFitter"),N("Aspect","AspectRatioFitter"),N("Layout","LayoutElement"),N("Mask","MaskElement"),N("Image","ImageElement"),N("Text","TextElement"),N("Button","ButtonElement")
                };
                var plan=new WebVisualUiPlan { nodes=fixtures.ToList() };
                var result=new WebVisualPrefabResult();ValidateStructure(plan,result);Check(result.ok,"Valid fixtures rejected: "+string.Join(",",result.issues.Select(i=>i.code)));
                foreach(var fixture in fixtures)CreateNode(root.transform,fixture,new WebVisualRect {width=400,height=800},result,"",true);
                Check(result.ok,"Control generation failed");
                var slider=root.GetComponentInChildren<Slider>();Check(slider.fillRect.name=="Fill" && slider.handleRect.name=="Handle","Slider references");slider.value=.7f;Check(Mathf.Abs(slider.fillRect.anchorMax.x-.7f)<.001f,"Slider does not drive fill");
                Check(root.GetComponentInChildren<Scrollbar>().handleRect.name=="BarHandle","Scrollbar reference");
                var toggle=root.transform.Find("Group/Toggle").GetComponent<Toggle>();Check(toggle.graphic.name=="Check"&&toggle.group!=null,"Toggle references");
                var input=root.GetComponentInChildren<InputField>();Check(input.textComponent.name=="InputText"&&input.placeholder.name=="Hint"&&input.transform.childCount==2,"Input duplicate text or references");input.text="abc";Check(input.textComponent.text=="abc","Input rendering");
                var scroll=root.GetComponentInChildren<ScrollRect>();Check(scroll.content.IsChildOf(scroll.viewport)&&scroll.transform.childCount==1,"Scroll duplicate viewport or content");
                var dropdown=root.GetComponentInChildren<Dropdown>();Check(dropdown.template && !dropdown.template.gameObject.activeSelf && dropdown.itemText.name=="ItemLabel" && dropdown.captionText.name=="Caption","Dropdown template references");
                foreach(var type in new[]{typeof(RawImage),typeof(CanvasGroup),typeof(HorizontalLayoutGroup),typeof(VerticalLayoutGroup),typeof(GridLayoutGroup),typeof(ContentSizeFitter),typeof(AspectRatioFitter),typeof(UnityEngine.UI.LayoutElement),typeof(Mask),typeof(Button)})Check(root.GetComponentInChildren(type)!=null,"Missing "+type.Name);
                foreach(var type in new[]{"SliderElement","ScrollbarElement","ToggleElement","InputFieldElement","DropdownElement","ScrollView"}) {
                    var invalid=new WebVisualPrefabResult();ValidateStructure(new WebVisualUiPlan {nodes=new List<WebVisualNode>{N("Invalid",type)}},invalid);Check(!invalid.ok,"Incomplete control accepted: "+type);
                }
                var path=Path.GetFullPath("../artifacts/ui-controls-v2/unity-regression.txt");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"PASS "+checks+" assertions; 18 control fixtures; no asset writes");
                Debug.Log("UGUI_CONTROL_REGRESSION_PASS: "+checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
