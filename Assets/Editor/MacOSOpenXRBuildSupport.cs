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

        // Unity OpenXR 1.18 RefreshAllFeatureInfo() dereferences every feature entry before
        // it has a chance to filter missing references. Open Brush carries settings for
        // several build target groups, and packages may refresh groups other than the active
        // macOS/Standalone one during editor initialisation. Sanitize every settings object
        // after package assets have loaded.
        EditorApplication.delayCall += SanitizeAllOpenXrFeatureLists;
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

    static void SanitizeAllOpenXrFeatureLists()
    {
        int settingsObjects = 0;
        int groupsWithNulls = 0;
        int removedNulls = 0;
        var processedSettings = new HashSet<int>();

        foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup))
                     .Cast<BuildTargetGroup>()
                     .Distinct())
        {
            OpenXRSettings settings;
            try
            {
                settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            }
            catch
            {
                // Some obsolete/unsupported enum values are not meaningful to XR Management.
                continue;
            }

            if (settings == null)
            {
                continue;
            }

            int instanceId = settings.GetInstanceID();
            if (!processedSettings.Add(instanceId))
            {
                continue;
            }

            ++settingsObjects;

            OpenXRFeature[] allFeatures = settings.GetFeatures();
            OpenXRFeature[] validFeatures = allFeatures
                .Where(feature => !ReferenceEquals(feature, null))
                .ToArray();
            int nullCount = allFeatures.Length - validFeatures.Length;

            if (nullCount == 0)
            {
                continue;
            }

            ++groupsWithNulls;
            removedNulls += nullCount;

            string path = AssetDatabase.GetAssetPath(settings);
            Debug.LogWarning(
                kLogPrefix +
                $"{group} OpenXR settings '{settings.name}' at '{path}' contain " +
                $"{nullCount} null/missing feature reference{(nullCount == 1 ? "" : "s")}; rebuilding the feature array.");

            var serializedSettings = new SerializedObject(settings);
            SerializedProperty features = serializedSettings.FindProperty("features");
            if (features == null || !features.isArray)
            {
                Debug.LogError(
                    kLogPrefix +
                    $"Could not find the serialized feature array for {group} OpenXR settings.");
                continue;
            }

            features.ClearArray();
            features.arraySize = validFeatures.Length;
            for (int i = 0; i < validFeatures.Length; ++i)
            {
                features.GetArrayElementAtIndex(i).objectReferenceValue = validFeatures[i];
            }

            serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
        }

        if (removedNulls > 0)
        {
            AssetDatabase.SaveAssets();
        }

        Debug.Log(
            kLogPrefix +
            $"Scanned {settingsObjects} OpenXR settings object{(settingsObjects == 1 ? "" : "s")}; " +
            $"{groupsWithNulls} contained null feature references; removed {removedNulls} in total.");

        // Keep the original Standalone-specific diagnostic because this is the target that
        // matters for the macOS player and it also confirms whether Unity considers the
        // Standalone feature set complete after cleanup.
        OpenXRSettings standalone =
            OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (standalone != null)
        {
            DiagnoseStandaloneRefreshCandidates(standalone);
        }
    }

    static void DiagnoseStandaloneRefreshCandidates(OpenXRSettings settings)
    {
        var existingTypes = new HashSet<Type>(
            settings.GetFeatures()
                .Where(feature => !ReferenceEquals(feature, null))
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
