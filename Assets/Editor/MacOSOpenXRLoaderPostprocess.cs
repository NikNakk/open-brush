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

using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using Debug = UnityEngine.Debug;

/// <summary>
/// Works around the macOS player layout used by Unity OpenXR 1.18.
///
/// UnityOpenXR asks its native plugin loader for "openxr_loader". On macOS the native
/// plugin appends ".dylib" without adding the conventional "lib" prefix, so it looks for
/// Contents/PlugIns/openxr_loader.dylib. The package asset is named
/// libopenxr_loader.dylib and Unity may place it in an architecture subdirectory instead.
/// Copy an alias to the location UnityOpenXR actually loads, then re-sign the app because
/// modifying an already-signed bundle invalidates its code signature.
/// </summary>
static class MacOSOpenXRLoaderPostprocess
{
    const string kPackageAsset =
        "Packages/com.unity.xr.openxr/RuntimeLoaders/osx/libopenxr_loader.dylib";
    const string kPackageRelative = "RuntimeLoaders/osx/libopenxr_loader.dylib";
    const string kPlayerRelative = "Contents/PlugIns/openxr_loader.dylib";

    [PostProcessBuild(1000)]
    static void FixOpenXrLoaderLayout(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneOSX)
        {
            return;
        }

        if (string.IsNullOrEmpty(pathToBuiltProject) || !Directory.Exists(pathToBuiltProject))
        {
            Debug.LogWarning(
                $"[OpenBrush macOS OpenXR] Loader fix skipped: app bundle not found at '{pathToBuiltProject}'.");
            return;
        }

        PackageInfo packageInfo = PackageInfo.FindForAssetPath(kPackageAsset);
        if (packageInfo == null)
        {
            Debug.LogError(
                "[OpenBrush macOS OpenXR] Loader fix failed: com.unity.xr.openxr package could not be resolved.");
            return;
        }

        string source = Path.Combine(packageInfo.resolvedPath, kPackageRelative);
        if (!File.Exists(source))
        {
            Debug.LogError(
                $"[OpenBrush macOS OpenXR] Loader fix failed: '{source}' does not exist.");
            return;
        }

        string destination = Path.Combine(pathToBuiltProject, kPlayerRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, true);
        Debug.Log(
            $"[OpenBrush macOS OpenXR] Copied '{source}' to UnityOpenXR loader path '{destination}'.");

#if UNITY_EDITOR_OSX
        AdHocResign(pathToBuiltProject);
#else
        Debug.LogWarning(
            "[OpenBrush macOS OpenXR] Loader copied, but the app was not re-signed because the build did not run on macOS.");
#endif
    }

#if UNITY_EDITOR_OSX
    static void AdHocResign(string appPath)
    {
        const string codesign = "/usr/bin/codesign";
        if (!File.Exists(codesign))
        {
            Debug.LogWarning(
                "[OpenBrush macOS OpenXR] Loader copied, but /usr/bin/codesign was not found.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = codesign,
            Arguments = "--force --deep --sign - " + Quote(appPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using (Process process = Process.Start(startInfo))
        {
            if (process == null)
            {
                Debug.LogWarning(
                    "[OpenBrush macOS OpenXR] Loader copied, but codesign could not be started.");
                return;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Debug.LogWarning(
                    $"[OpenBrush macOS OpenXR] Loader copied, but ad-hoc codesign failed " +
                    $"with exit code {process.ExitCode}.\n{stdout}{stderr}");
                return;
            }
        }

        Debug.Log("[OpenBrush macOS OpenXR] Re-signed app after installing OpenXR loader alias.");
    }

    static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
#endif
}
