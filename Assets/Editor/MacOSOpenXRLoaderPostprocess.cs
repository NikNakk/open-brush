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

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.XR.Management;
using UnityEngine.XR.OpenXR;

// Unity OpenXR 1.18 loads "openxr_loader.dylib" from the macOS bundle's PlugIns
// directory, but its standard loader asset is libopenxr_loader.dylib and can be
// packaged in an architecture subdirectory. Install the package's universal loader
// at the expected path. Final application signing must run after postprocessing,
// as it already does in the macOS distribution workflow; do not replace its identity.
static class MacOSOpenXRLoaderPostprocess
{
    const string kLoaderAsset =
        "Packages/com.unity.xr.openxr/RuntimeLoaders/osx/libopenxr_loader.dylib";

    [PostProcessBuild(3)]
    static void InstallLoader(BuildTarget target, string playerPath)
    {
        if (target != BuildTarget.StandaloneOSX)
        {
            return;
        }
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
            BuildTargetGroup.Standalone);
        if (settings?.Manager == null ||
            !settings.Manager.activeLoaders.Any(loader => loader is OpenXRLoaderBase))
        {
            return;
        }

        string destination = Path.Combine(playerPath, "Contents/PlugIns/openxr_loader.dylib");
        if (File.Exists(destination))
        {
            return;
        }
        var package = PackageInfo.FindForAssetPath(kLoaderAsset);
        string source = package == null ? null : Path.Combine(
            package.resolvedPath, "RuntimeLoaders/osx/libopenxr_loader.dylib");
        if (source == null || !File.Exists(source))
        {
            throw new BuildFailedException("The OpenXR package's macOS loader is missing.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination);
    }
}
