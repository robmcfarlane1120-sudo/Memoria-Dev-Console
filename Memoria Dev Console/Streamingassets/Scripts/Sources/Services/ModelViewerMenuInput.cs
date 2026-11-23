using System;
using System.Collections.Generic;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private readonly Dictionary<UICamera,Boolean[]> _viewerMenuCameras = new Dictionary<UICamera,Boolean[]>();

        private Boolean _consoleMenuInputBlocked;
        internal void SetConsoleMenuInputBlocked(Boolean blocked)
        {
            _consoleMenuInputBlocked=blocked;
            if(blocked || IsActive)SuspendViewerMenuInput(); else RestoreViewerMenuInput();
        }
        private void SuspendViewerMenuInput()
        {
            foreach (UICamera camera in UnityEngine.Object.FindObjectsOfType<UICamera>())
            {
                if (!_viewerMenuCameras.ContainsKey(camera))
                    _viewerMenuCameras.Add(camera,new[] {camera.useKeyboard,camera.useController,camera.useMouse,camera.useTouch});
                // Rendering remains active. Viewer controls read Unity input directly;
                // its UIInput text field also keeps its own Update/OnGUI processing.
                camera.useKeyboard = camera.useController = camera.useMouse = camera.useTouch = false;
            }
        }

        private void RestoreViewerMenuInput()
        {
            if (_consoleMenuInputBlocked) return;
            foreach (var entry in _viewerMenuCameras)
            {
                if (entry.Key == null) continue;
                entry.Key.useKeyboard = entry.Value[0]; entry.Key.useController = entry.Value[1];
                entry.Key.useMouse = entry.Value[2]; entry.Key.useTouch = entry.Value[3];
            }
            _viewerMenuCameras.Clear();
        }
    }
}
