using System;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DevConsoleStartupAttribute : Attribute
    {
        public DevConsoleStartupAttribute()
        {
            GameLoopManager.Update -= DevConsoleBootstrap.TryInitialize;
            GameLoopManager.Update += DevConsoleBootstrap.TryInitialize;
        }
    }

    [DevConsoleStartup]
    public static class DevConsoleEntryPoint
    {
    }

    public static class DevConsoleBootstrap
    {
        private static Boolean _started;

        public static void TryInitialize()
        {
            if (_started)
                return;

            _started = true;
            GameLoopManager.Update -= TryInitialize;

            try
            {
                Log.Message("[Dev Console] Creating controller...");

                GameObject host = new GameObject("Memoria Dev Console");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<DevConsoleController>();

                Log.Message("[Dev Console] Bootstrap started.");
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console] Bootstrap FAILED: " + ex);
            }
        }
    }
}
