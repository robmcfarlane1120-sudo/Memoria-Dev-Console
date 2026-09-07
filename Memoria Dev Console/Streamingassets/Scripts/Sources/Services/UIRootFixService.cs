using Memoria.Prime;
using System;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal sealed class UIRootFixService
    {
        public void ApplyLateFrame()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            if (UIRoot.list == null || UIRoot.list.Count == 0)
                return;

            for (Int32 i = 0; i < UIRoot.list.Count; i++)
            {
                UIRoot root = UIRoot.list[i];
                if (root == null)
                    continue;

                ApplyReferenceCanvas(root);
            }
        }

        private static void ApplyReferenceCanvas(UIRoot root)
        {
            const Int32 referenceWidth = 1280;
            const Int32 referenceHeight = 720;

            root.manualWidth = referenceWidth;
            root.manualHeight = referenceHeight;
            root.fitWidth = false;
            root.fitHeight = true;

            // UIRoot.UpdateScale() cannot be used here while widescreen support
            // is enabled because activeHeight immediately rewrites manualWidth
            // and manualHeight from UIManager.UIContentSize.
            Single scale = 2f / referenceHeight;
            root.transform.localScale = new Vector3(scale, scale, scale);

            UIPanel panel = root.GetComponent<UIPanel>();
            if (panel != null)
            {
                Vector4 clip = panel.baseClipRegion;
                clip.z = referenceWidth + 2f;
                clip.w = referenceHeight + 2f;
                panel.baseClipRegion = clip;
            }
        }

    }
}
