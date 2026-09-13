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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
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
    ///
    /// Set OPENBRUSH_MACOS_XR_MIRROR=0 (or false/off/no) to disable Unity's XR mirror-view blit.
    /// Leaving the variable unset, or setting it to any other value, preserves Unity's normal
    /// desktop mirror behaviour. This is an A/B performance diagnostic only: it does not suppress
    /// the macOS player window or its CAMetalLayer presentation by itself.
    /// </summary>
    internal sealed class MacOSOpenXRLateInit : MonoBehaviour
    {
        private const string kMirrorEnvironmentVariable = "OPENBRUSH_MACOS_XR_MIRROR";

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

            if (ShouldDisableMirrorView())
            {
                // Give the display provider a few frames to become running, then suppress only the
                // XR mirror blit. The normal macOS player window may still present a drawable.
                const int maxFramesToWait = 10;
                var displays = new List<XRDisplaySubsystem>();
                bool disabledAnyMirror = false;

                for (int frame = 0; frame < maxFramesToWait && !disabledAnyMirror; ++frame)
                {
                    displays.Clear();
                    SubsystemManager.GetInstances(displays);

                    foreach (XRDisplaySubsystem display in displays.Where(display => display != null && display.running))
                    {
                        display.SetPreferredMirrorBlitMode(XRMirrorViewBlitMode.None);
                        disabledAnyMirror = true;
                        Debug.Log(
                            "[OpenBrush XR] Mirror: disabled XR mirror-view blit " +
                            $"(OPENBRUSH_MACOS_XR_MIRROR={Environment.GetEnvironmentVariable(kMirrorEnvironmentVariable)})."
                        );
                    }

                    if (!disabledAnyMirror)
                    {
                        yield return null;
                    }
                }

                if (!disabledAnyMirror)
                {
                    Debug.LogWarning(
                        "[OpenBrush XR] Mirror: requested mirror disable but no running " +
                        $"XRDisplaySubsystem was found after {maxFramesToWait} frames.");
                }
            }
            else
            {
                string mirrorSetting = Environment.GetEnvironmentVariable(kMirrorEnvironmentVariable);
                Debug.Log(
                    "[OpenBrush XR] Mirror: retaining Unity's normal mirror view; " +
                    $"{kMirrorEnvironmentVariable}={(string.IsNullOrEmpty(mirrorSetting) ? "<unset>" : mirrorSetting)}.");
            }

            Destroy(gameObject);
        }

        private static bool ShouldDisableMirrorView()
        {
            string value = Environment.GetEnvironmentVariable(kMirrorEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "0":
                case "false":
                case "off":
                case "no":
                    return true;
                default:
                    return false;
            }
        }
    }
}
