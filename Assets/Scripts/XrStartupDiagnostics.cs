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
using UnityEngine;
using UnityEngine.XR.Management;

namespace TiltBrush
{
    /// <summary>
    /// Temporary diagnostics for bringing up standalone macOS OpenXR builds.
    /// This deliberately does not change XR initialization behaviour.
    /// </summary>
    internal static class XrStartupDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad()
        {
            string runtimeJson = System.Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
            Debug.Log($"[OpenBrush XR] BeforeSceneLoad: platform={Application.platform}; " +
                      $"XR_RUNTIME_JSON={(string.IsNullOrEmpty(runtimeJson) ? "<unset>" : runtimeJson)}");
            LogXrManagementState("BeforeSceneLoad");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            string sdkMode;
            try
            {
                sdkMode = App.Config == null ? "<null config>" : App.Config.m_SdkMode.ToString();
            }
            catch (Exception e)
            {
                sdkMode = $"<error reading config: {e.GetType().Name}>";
            }

            Debug.Log($"[OpenBrush XR] AfterSceneLoad: App.Config.m_SdkMode={sdkMode}");
            LogXrManagementState("AfterSceneLoad");
        }

        private static void LogXrManagementState(string phase)
        {
            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null)
            {
                Debug.LogError($"[OpenBrush XR] {phase}: XRGeneralSettings.Instance=<null>");
                return;
            }

            XRManagerSettings manager = settings.Manager;
            if (manager == null)
            {
                Debug.LogError($"[OpenBrush XR] {phase}: XRGeneralSettings.Manager=<null>; " +
                               $"InitManagerOnStart={settings.InitManagerOnStart}");
                return;
            }

            XRLoader loader = manager.activeLoader;
            Debug.Log($"[OpenBrush XR] {phase}: InitManagerOnStart={settings.InitManagerOnStart}; " +
                      $"activeLoader={(loader == null ? "<null>" : loader.GetType().FullName)}");
        }
    }
}
