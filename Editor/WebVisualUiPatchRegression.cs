using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FUI.Cli
{
    static partial class WebVisualUiPrefabBuilder
    {
        [MenuItem("FUI/Validate Prefab Patch Reference Rect")]
        static void RunPatchReferenceRegression()
        {
            var root = new GameObject("补丁尺寸回归", typeof(RectTransform));
            root.hideFlags = HideFlags.HideAndDontSave;
            ((RectTransform)root.transform).sizeDelta = Vector2.zero;
            try
            {
                var safe = new GameObject("SafeArea", typeof(RectTransform));
                safe.transform.SetParent(root.transform, false);
                var rect = (RectTransform)safe.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var reference = new WebVisualRect { width = 2532f, height = 1170f };
                var resolved = ResolvePatchReferenceRect(root.transform, safe.transform, reference);
                if (resolved.width != 2532f || resolved.height != 1170f || resolved.x != 0f || resolved.y != 0f)
                {
                    throw new InvalidOperationException("零尺寸 Canvas 的安全区必须使用设计分辨率。");
                }

                rect.anchorMin = new Vector2(0.25f, 0.25f);
                rect.anchorMax = new Vector2(0.75f, 0.75f);
                rect.offsetMin = new Vector2(10f, 20f);
                rect.offsetMax = new Vector2(-10f, -20f);
                resolved = ResolvePatchReferenceRect(root.transform, safe.transform, reference);
                if (resolved.width != 1246f || resolved.height != 545f
                    || resolved.x != 643f || resolved.y != 312.5f)
                {
                    throw new InvalidOperationException("嵌套补丁的画布坐标与锚点边距计算不正确。");
                }

                if (((RectTransform)root.transform).sizeDelta != Vector2.zero)
                {
                    throw new InvalidOperationException("补丁尺寸推导不得改写父级资源。");
                }

                Directory.CreateDirectory("Temp/SkillCooldownValidation");
                File.WriteAllText("Temp/SkillCooldownValidation/patch-reference.txt", "PASS: reference size, nested offsets, parent preservation");
                Debug.Log("PREFAB_PATCH_REFERENCE_PASS");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
