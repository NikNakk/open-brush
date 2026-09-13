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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TiltBrush;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;

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
    const string kLogPrefix = "[OpenBrush macOS OpenXR] ";

    static MacOSOpenXRBuildSupport()
    {
        EnableMacOsOpenXrBuildTarget();

        // OpenXR 1.18's feature refresh assumes every serialized feature reference is
        // non-null. Open Brush's upgraded settings can contain a missing subasset, which
        // causes RefreshAllFeatureInfo() to throw before the Standalone OpenXR setup has
        // been refreshed. Run after the editor has finished loading the package assets.
        EditorApplication.delayCall += SanitizeStandaloneOpenXrFeatures;
    }

    static void EnableMacOsOpenXrBuildTarget()
    {
        FieldInfo validTargetsField = typeof(BuildTiltBrush).GetField(
            "kValidSdkTargets",
            BindingFlags.NonPublic | BindingFlags.Static);

        var validTargets = validTargetsField?.GetValue(null) as
            List<KeyValuePair<XrSdkMode, BuildTarget>>;

        if (validTargets == null)
        {
            Debug.LogWarning(
                kLogPrefix +
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

    static void SanitizeStandaloneOpenXrFeatures()
    {
        OpenXRSettings settings =
            OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);

        if (settings == null)
        {
            Debug.LogWarning(kLogPrefix + "No Standalone OpenXRSettings asset was found.");
            return;
        }

        OpenXRFeature[] features = settings.features;
        if (features == null)
        {
            Debug.LogWarning(kLogPrefix + "Standalone OpenXRSettings.features is null.");
            return;
        }

        int nullFeatureCount = features.Count(feature => feature == null);
        if (nullFeatureCount == 0)
        {
            Debug.Log(kLogPrefix + "Standalone OpenXR feature list contains no null references.");
            return;
        }

        settings.features = features.Where(feature => feature != null).ToArray();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.LogWarning(
            kLogPrefix +
            $"Removed {nullFeatureCount} null/missing Standalone OpenXR feature " +
            $"reference{(nullFeatureCount == 1 ? "" : "s")}; refreshing feature metadata.");

        try
        {
            // Let Unity recreate any legitimate feature subassets that are now absent and
            // update its derived feature metadata using the package's supported API.
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                kLogPrefix +
                "OpenXR feature refresh still failed after removing null references:\n" +
                exception);
        }
    }
}
