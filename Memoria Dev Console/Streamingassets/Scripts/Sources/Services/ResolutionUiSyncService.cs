using Memoria.Prime;
using System;
using UnityEngine;

namespace Memoria.DevConsole
{
    /// <summary>
    /// Keeps Memoria's cached widescreen UI dimensions synchronized with the
    /// actual runtime resolution.
    ///
    /// UIManager.UIContentSize is initialized from Screen.width/Screen.height.
    /// FFIX can start in a temporary window size and switch resolution shortly
    /// afterwards. Memoria does not automatically recompute UIContentSize for
    /// that resolution change, leaving NGUI laid out for the old aspect ratio.
    /// </summary>
    internal sealed class ResolutionUiSyncService
    {
        private Int32 _screenWidth = -1;
        private Int32 _screenHeight = -1;
        private Boolean _initialized;

        public void Update()
        {
            Int32 width = Screen.width;
            Int32 height = Screen.height;

            if (width <= 0 || height <= 0)
                return;

            if (!_initialized)
            {
                _initialized = true;
                _screenWidth = width;
                _screenHeight = height;

                return;
            }

            if (width == _screenWidth && height == _screenHeight)
                return;

            _screenWidth = width;
            _screenHeight = height;

            UIManager manager = PersistenSingleton<UIManager>.Instance;
            if (manager == null)
                return;

            // This is Memoria's own supported recalculation path. It recomputes
            // UIContentSize from the CURRENT Screen aspect ratio and updates the
            // UI resource multipliers without disabling widescreen rendering.
            manager.OnWidescreenSupportChanged();

            // Apply the refreshed UIContentSize immediately instead of waiting
            // for an uncertain MonoBehaviour Update ordering on this frame.
            if (UIRoot.list != null)
            {
                for (Int32 i = 0; i < UIRoot.list.Count; i++)
                {
                    UIRoot root = UIRoot.list[i];
                    if (root != null)
                        root.UpdateScale(true);
                }
            }

        }
    }
}
