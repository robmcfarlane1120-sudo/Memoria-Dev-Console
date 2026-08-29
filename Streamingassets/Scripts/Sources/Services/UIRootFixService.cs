using Memoria.Prime;
using System;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal sealed class UIRootFixService
    {
        private Int32 _lastScreenWidth = -1;
        private Int32 _lastScreenHeight = -1;
        private Boolean _loggedSourceState;

        public void ApplyLateFrame()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            if (UIRoot.list == null || UIRoot.list.Count == 0)
                return;

            Boolean viewportChanged =
                _lastScreenWidth != Screen.width ||
                _lastScreenHeight != Screen.height;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            for (Int32 i = 0; i < UIRoot.list.Count; i++)
            {
                UIRoot root = UIRoot.list[i];
                if (root == null)
                    continue;

                if (!_loggedSourceState || viewportChanged)
                    LogState("MEMORIA", root);

                ApplyReferenceCanvas(root);

                if (!_loggedSourceState || viewportChanged)
                    LogState("OVERRIDE", root);
            }

            _loggedSourceState = true;
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

        private static void LogState(String phase, UIRoot root)
        {
            try
            {
                UIPanel panel = root.GetComponent<UIPanel>();
                Camera camera = UICamera.mainCamera;

                Log.Message(
                    "[Dev Console][UIROOT " + phase + "] " +
                    "Screen=" + Screen.width + "x" + Screen.height +
                    " UIContent=" + UIManager.UIContentSize +
                    " manual=" + root.manualWidth + "x" + root.manualHeight +
                    " pixelAdj=" + root.pixelSizeAdjustment.ToString("F4") +
                    " scale=" + root.transform.localScale.ToString("F6") +
                    " clip=" + (panel != null ? panel.baseClipRegion.ToString("F2") : "<none>") +
                    " cameraRect=" + (camera != null ? camera.pixelRect.ToString() : "<none>"));
            }
            catch (Exception ex)
            {
                Log.Warning("[Dev Console][UIROOT] Diagnostic log failed: " + ex.Message);
            }
        }
    }
}
