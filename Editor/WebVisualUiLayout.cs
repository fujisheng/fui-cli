using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using FUI.Rendering.UGUI;
namespace FUI.Cli
{
    [Serializable]
    public sealed class WebLayoutOptions
    {
        public string mode = "free"; // free, anchored, managed
        public float[] anchorMin, anchorMax, pivot;
        public string group = ""; // horizontal, vertical, grid
        public float spacingX, spacingY;
        public int[] padding; // left, right, top, bottom
        public string alignment = "UpperLeft";
        public bool controlWidth = true, controlHeight = true, expandWidth, expandHeight;
        public bool ignore, contentHeight, safeArea;
        public float minWidth = -1, minHeight = -1, preferredWidth = -1, preferredHeight = -1;
        public float flexibleWidth = -1, flexibleHeight = -1;
        public int columns = 1;
        public float cellWidth = 100, cellHeight = 100;
    }
    static partial class WebVisualUiPrefabBuilder
    {
        static void ValidateLayout(WebVisualNode node, WebVisualPrefabResult result)
        {
            var l = node.layout; if (l == null) return;
            void Error(string message) { result.ok=false; result.issues.Add(WebVisualPrefabIssue.Create("layout_contract", message, node.id)); }
            if (!new[]{"free","anchored","managed"}.Contains(l.mode)) Error("Unknown layout positioning mode.");
            if (!new[]{"","horizontal","vertical","grid"}.Contains(l.group)) Error("Unknown layout group.");
            if (l.mode == "anchored" && (l.anchorMin?.Length != 2 || l.anchorMax?.Length != 2)) Error("Anchored nodes require two anchor pairs.");
            if (l.anchorMin?.Length == 2 && l.anchorMax?.Length == 2)
                for (int i=0;i<2;i++) if(l.anchorMin[i]<0 || l.anchorMax[i]>1 || l.anchorMin[i]>l.anchorMax[i]) Error("Invalid anchor interval.");
            if (l.mode == "managed" && l.contentHeight) Error("Parent layout and child fitter cannot both own height.");
            if (l.group == "grid" && (l.columns<1 || l.cellWidth<=0 || l.cellHeight<=0)) Error("Grid requires positive cell size and column count.");
            if (l.padding != null && l.padding.Length != 4) Error("Padding must contain left/right/top/bottom.");
            if (!string.IsNullOrEmpty(l.group)) foreach(var child in node.children)
                if (!child.layout.ignore && child.layout.mode != "managed") Error("Layout child must declare managed positioning or ignore: " + child.id);
        }
        static void ApplyLayout(GameObject go, WebVisualNode node, WebVisualRect parent)
        {
            var l=node.layout; if(l==null)return;
            var rt=(RectTransform)go.transform;
            if(l.mode=="anchored")
            {
                rt.anchorMin=new Vector2(l.anchorMin[0],l.anchorMin[1]); rt.anchorMax=new Vector2(l.anchorMax[0],l.anchorMax[1]);
                rt.pivot=l.pivot?.Length==2?new Vector2(l.pivot[0],l.pivot[1]):Vector2.one*.5f;
                // Preserve design margins at reference size. Future parent resizing is handled by anchors.
                rt.offsetMin=new Vector2(node.rect.x-parent.x-rt.anchorMin.x*parent.width,
                    parent.height-(node.rect.y-parent.y)-node.rect.height-rt.anchorMin.y*parent.height);
                rt.offsetMax=new Vector2(node.rect.x-parent.x+node.rect.width-rt.anchorMax.x*parent.width,
                    parent.height-(node.rect.y-parent.y)-rt.anchorMax.y*parent.height);
            }
            if(l.mode=="managed" || l.ignore)
            {
                var e=EnsureComponent<LayoutElement>(go);e.ignoreLayout=l.ignore;
                e.minWidth=l.minWidth;e.minHeight=l.minHeight;
                e.preferredWidth=l.preferredWidth<0?node.rect.width:l.preferredWidth;
                // -2 delegates preferred height to ILayoutElement providers such as Text.
                e.preferredHeight=l.preferredHeight==-2?-1:l.preferredHeight<0?node.rect.height:l.preferredHeight;
                e.flexibleWidth=l.flexibleWidth;e.flexibleHeight=l.flexibleHeight;
                if(!l.ignore)rt.anchoredPosition=Vector2.zero;
            }
            LayoutGroup group=null;
            if(l.group=="horizontal")group=EnsureComponent<HorizontalLayoutGroup>(go);
            if(l.group=="vertical")group=EnsureComponent<VerticalLayoutGroup>(go);
            if(l.group=="grid")
            {
                var grid=EnsureComponent<GridLayoutGroup>(go);group=grid;
                grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=l.columns;
                grid.cellSize=new Vector2(l.cellWidth,l.cellHeight);grid.spacing=new Vector2(l.spacingX,l.spacingY);
            }
            if(group!=null)
            {
                group.childAlignment=(TextAnchor)Enum.Parse(typeof(TextAnchor),l.alignment);
                if(l.padding?.Length==4)group.padding=new RectOffset(l.padding[0],l.padding[1],l.padding[2],l.padding[3]);
                if(group is HorizontalOrVerticalLayoutGroup linear)
                {
                    linear.spacing=l.group=="horizontal"?l.spacingX:l.spacingY;
                    linear.childControlWidth=l.controlWidth;linear.childControlHeight=l.controlHeight;
                    linear.childForceExpandWidth=l.expandWidth;linear.childForceExpandHeight=l.expandHeight;
                }
            }
            if(l.contentHeight){var fit=EnsureComponent<ContentSizeFitter>(go);fit.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;}
            if(l.safeArea){var safe=EnsureComponent<SafeAreaFitter>(go);safe.ReferenceWidth=node.rect.width;safe.MinimumHeight=node.rect.height;}
        }
    }
}
