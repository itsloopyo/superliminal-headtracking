using SuperliminalHeadTracking.Core;
using UnityEngine;

namespace SuperliminalHeadTracking.Il2Cpp
{
    public sealed class Runtime : MonoBehaviour
    {
        private readonly HeadTrackingPlugin _plugin = new HeadTrackingPlugin();

        private void Awake() { _plugin.Awake(); }
        private void Update() { _plugin.Update(); }
        private void OnGUI() { _plugin.OnGUI(); }
        private void OnDestroy() { _plugin.OnDestroy(); }
    }
}
