// Copyright 2026 The Open Brush Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Management;

namespace TiltBrush
{
    /// <summary>
    /// Experimental macOS OpenXR bring-up helper.
    ///
    /// Open Brush currently attempts manual XR initialization from VrSdk.Awake(). XR Management
    /// documents that manual initialization must not happen before Start has completed because
    /// graphics initialization is not guaranteed to be ready. On macOS/Metal that early attempt
    /// can leave activeLoader null without ever reaching the native OpenXR loader.
    ///
    /// For the experimental macOS port, retry once on the following frame. This is deliberately
    /// isolated so we can prove the timing issue before changing VrSdk's cross-platform lifecycle.
    /// </summary>
    internal sealed class MacOSOpenXRLateInit : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            var host = new GameObject("OpenBrush macOS OpenXR late init");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<MacOSOpenXRLateInit>();
#endif
        }

        private IEnumerator Start()
        {
            // Let every scene object's Start() finish and allow Unity's graphics device to reach the
            // point required by XRManagerSettings.InitializeLoaderSync().
            yield return null;

            XRGeneralSettings generalSettings = XRGeneralSettings.Instance;
            XRManagerSettings manager = generalSettings?.Manager;
            if (manager == null)
            {
                Debug.LogError("[OpenBrush XR] LateInit: XRGeneralSettings.Manager is null.");
                Destroy(gameObject);
                yield break;
            }

            string configuredLoaders = string.Join(", ", manager.activeLoaders
                .Where(loader => loader != null)
                .Select(loader => loader.GetType().FullName));

            Debug.Log(
                $"[OpenBrush XR] LateInit: graphics={SystemInfo.graphicsDeviceType}; " +
                $"configuredLoaders=[{configuredLoaders}]; " +
                $"initializationComplete={manager.isInitializationComplete}; " +
                $"activeLoader={(manager.activeLoader == null ? "<null>" : manager.activeLoader.GetType().FullName)}");

            if (manager.activeLoader == null)
            {
                Debug.Log("[OpenBrush XR] LateInit: retrying InitializeLoaderSync after Start.");
                manager.InitializeLoaderSync();
            }

            if (manager.activeLoader == null)
            {
                Debug.LogError(
                    "[OpenBrush XR] LateInit: InitializeLoaderSync returned with activeLoader=<null>.");
                Destroy(gameObject);
                yield break;
            }

            Debug.Log(
                $"[OpenBrush XR] LateInit: activeLoader={manager.activeLoader.GetType().FullName}; " +
                "starting XR subsystems.");
            manager.StartSubsystems();

            Debug.Log(
                $"[OpenBrush XR] LateInit: XR subsystems started; " +
                $"initializationComplete={manager.isInitializationComplete}.");

            Destroy(gameObject);
        }
    }
}
