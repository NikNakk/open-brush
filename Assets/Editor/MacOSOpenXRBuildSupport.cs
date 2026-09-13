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
using UnityEngine.XR.OpenXR.Features;

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

        // OpenXR 1.18's feature refresh assumes every feature object it considers is
        // non-null. Run after the editor has finished loading package assets so we can both
        // repair persisted missing references and diagnose feature types that Unity itself
        // cannot instantiate during RefreshAllFeatureInfo().
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

        OpenXRFeature[] allFeatures = settings.GetFeatures();
        OpenXRFeature[] validFeatures = allFeatures.Where(feature => feature != null).ToArray();
        int nullFeatureCount = allFeatures.Length - validFeatures.Length;

        if (nullFeatureCount == 0)
        {
            Debug.Log(kLogPrefix + "Standalone OpenXR feature list contains no null references.");
            DiagnoseStandaloneRefreshCandidates(settings);
            return;
        }

        // OpenXRSettings.features is internal in the package, so rebuild the serialized
        // array through Unity's editor serialization API. Reconstructing the whole array is
        // more reliable for a missing embedded subasset than deleting the broken PPtr slot.
        var serializedSettings = new SerializedObject(settings);
        SerializedProperty features = serializedSettings.FindProperty("features");
        if (features == null || !features.isArray)
        {
            Debug.LogWarning(
                kLogPrefix + "Could not find the serialized Standalone OpenXR feature array.");
            return;
        }

        features.ClearArray();
        features.arraySize = validFeatures.Length;
        for (int i = 0; i < validFeatures.Length; ++i)
        {
            features.GetArrayElementAtIndex(i).objectReferenceValue = validFeatures[i];
        }

        serializedSettings.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        serializedSettings.Update();
        OpenXRFeature[] verifiedFeatures = settings.GetFeatures();
        int remainingNulls = verifiedFeatures.Count(feature => feature == null);

        Debug.LogWarning(
            kLogPrefix +
            $"Removed {nullFeatureCount} null/missing Standalone OpenXR feature " +
            $"reference{(nullFeatureCount == 1 ? "" : "s")}; " +
            $"verification found {remainingNulls} remaining null reference" +
            $"{(remainingNulls == 1 ? "" : "s")}.");

        if (remainingNulls != 0)
        {
            Debug.LogError(
                kLogPrefix +
                "Not diagnosing OpenXR feature refresh because the Standalone feature array " +
                "still contains null references after reconstruction.");
            return;
        }

        DiagnoseStandaloneRefreshCandidates(settings);
    }

    static void DiagnoseStandaloneRefreshCandidates(OpenXRSettings settings)
    {
        var existingTypes = new HashSet<Type>(
            settings.GetFeatures()
                .Where(feature => feature != null)
                .Select(feature => feature.GetType()));

        int missingEligibleTypes = 0;
        int suspiciousTypes = 0;

        foreach (Type featureType in TypeCache.GetTypesWithAttribute<OpenXRFeatureAttribute>())
        {
            OpenXRFeatureAttribute attribute;
            try
            {
                attribute = featureType.GetCustomAttribute<OpenXRFeatureAttribute>(true);
            }
            catch (Exception exception)
            {
                ++suspiciousTypes;
                Debug.LogError(
                    kLogPrefix +
                    $"Could not read OpenXRFeatureAttribute for {featureType.FullName}: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                continue;
            }

            if (attribute == null)
            {
                continue;
            }

            BuildTargetGroup[] groups = attribute.BuildTargetGroups;
            if (groups != null && groups.Length > 0 &&
                !groups.Contains(BuildTargetGroup.Standalone))
            {
                continue;
            }

            if (existingTypes.Contains(featureType))
            {
                continue;
            }

            ++missingEligibleTypes;
            string reason = null;

            if (!typeof(OpenXRFeature).IsAssignableFrom(featureType))
            {
                reason = "is marked as an OpenXR feature but does not derive from OpenXRFeature";
            }
            else if (featureType.IsAbstract)
            {
                reason = "is abstract";
            }
            else if (featureType.ContainsGenericParameters)
            {
                reason = "contains unbound generic parameters";
            }
            else
            {
                OpenXRFeature probe = null;
                try
                {
                    probe = ScriptableObject.CreateInstance(featureType) as OpenXRFeature;
                    if (probe == null)
                    {
                        reason = "ScriptableObject.CreateInstance returned null";
                    }
                }
                catch (Exception exception)
                {
                    reason =
                        $"ScriptableObject.CreateInstance threw {exception.GetType().Name}: " +
                        exception.Message;
                }
                finally
                {
                    if (probe != null)
                    {
                        UnityEngine.Object.DestroyImmediate(probe);
                    }
                }
            }

            if (reason != null)
            {
                ++suspiciousTypes;
                Debug.LogError(
                    kLogPrefix +
                    $"OpenXR refresh candidate {featureType.FullName} " +
                    $"({featureType.Assembly.GetName().Name}) {reason}. " +
                    "This type can cause Unity OpenXR 1.18 RefreshAllFeatureInfo() to add a null feature and then throw.");
            }
        }

        Debug.Log(
            kLogPrefix +
            $"OpenXR refresh candidate scan: {missingEligibleTypes} Standalone-eligible feature " +
            $"type{(missingEligibleTypes == 1 ? "" : "s")} absent from the settings; " +
            $"{suspiciousTypes} suspicious.");
    }
}
