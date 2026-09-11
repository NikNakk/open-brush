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

using System.Collections.Generic;
using System.Reflection;
using TiltBrush;
using UnityEditor;

/// <summary>
/// Experimental macOS OpenXR build enablement.
///
/// BuildTiltBrush already contains the macOS output, post-build and OpenXR-loader paths,
/// but its supported SDK/target matrix does not currently include OpenXR + StandaloneOSX.
/// Keep this isolated while the macOS OpenXR path is being validated, then fold the entry
/// into BuildTiltBrush.kValidSdkTargets once the port is proven.
/// </summary>
[InitializeOnLoad]
static class MacOSOpenXRBuildSupport
{
    static MacOSOpenXRBuildSupport()
    {
        FieldInfo validTargetsField = typeof(BuildTiltBrush).GetField(
            "kValidSdkTargets",
            BindingFlags.NonPublic | BindingFlags.Static);

        var validTargets = validTargetsField?.GetValue(null) as
            List<KeyValuePair<XrSdkMode, BuildTarget>>;

        if (validTargets == null)
        {
            UnityEngine.Debug.LogWarning(
                "Could not enable experimental macOS OpenXR build target: " +
                "BuildTiltBrush.kValidSdkTargets was not found.");
            return;
        }

        var macOsOpenXr = new KeyValuePair<XrSdkMode, BuildTarget>(
            XrSdkMode.OpenXR,
            BuildTarget.StandaloneOSX);

        if (!validTargets.Contains(macOsOpenXr))
        {
            validTargets.Add(macOsOpenXr);
        }
    }
}
