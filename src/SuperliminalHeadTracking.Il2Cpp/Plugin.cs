using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SuperliminalHeadTracking.Core;
using UnityEngine;

namespace SuperliminalHeadTracking.Il2Cpp
{
    [BepInPlugin(HeadTrackingPlugin.PluginGUID, HeadTrackingPlugin.PluginName, HeadTrackingPlugin.PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        internal static ManualLogSource Logger { get; private set; }
        internal static ConfigFile Settings { get; private set; }
        private static GameObject _host;

        public override void Load()
        {
            Logger = Log;
            Settings = Config;
            Log.LogInfo("Loading Superliminal IL2CPP support.");
            Assembly.Load("Assembly-CSharp-firstpass");
            Assembly.Load("Assembly-CSharp");
            ClassInjector.RegisterTypeInIl2Cpp<Runtime>();
            _host = new GameObject(HeadTrackingPlugin.PluginName);
            Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<Runtime>();
        }
    }
}
