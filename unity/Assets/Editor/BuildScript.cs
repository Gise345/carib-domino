#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Pose.Build
{
    /// <summary>
    /// Command-line entry points for store builds. Invoked by
    /// <c>scripts/build-android.ps1</c> locally and by <c>codemagic.yaml</c> in CI:
    /// <c>Unity -batchmode -nographics -projectPath ./unity -buildTarget Android
    /// -executeMethod Pose.Build.BuildScript.BuildAndroidAab -quit</c>
    /// </summary>
    public static class BuildScript
    {
        /// <summary>Absolute directory to write artifacts into. Defaults to <c>unity/Builds</c>.</summary>
        public const string OutputDirEnvVar = "POSE_BUILD_OUTPUT";

        private const string AabName = "pose.aab";
        private const string ApkName = "pose.apk";
        private const string XcodeProjectDirName = "iOS";

        /// <summary>Android App Bundle for Google Play internal testing.</summary>
        public static void BuildAndroidAab() => RunAndroid(appBundle: true, artifactName: AabName);

        /// <summary>Standalone APK for sideloading onto a test device.</summary>
        public static void BuildAndroidApk() => RunAndroid(appBundle: false, artifactName: ApkName);

        /// <summary>Exports the Xcode project. The .ipa is produced by xcodebuild on macOS.</summary>
        public static void BuildIos()
        {
            SwitchTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            ReleaseBuildSettings.Apply(BuildTarget.iOS);

            Run(BuildTarget.iOS, Path.Combine(OutputDir(), XcodeProjectDirName));
        }

        private static void RunAndroid(bool appBundle, string artifactName)
        {
            SwitchTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ReleaseBuildSettings.Apply(BuildTarget.Android);

            EditorUserBuildSettings.buildAppBundle = appBundle;

            Run(BuildTarget.Android, Path.Combine(OutputDir(), artifactName));
        }

        private static void Run(BuildTarget target, string locationPathName)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Fail("No scenes are enabled in Build Settings — the player would be empty.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(locationPathName)!);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log($"Building {target} → {locationPathName} " +
                      $"(version {PlayerSettings.bundleVersion}, {scenes.Length} scene(s))");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build {summary.result} after {summary.totalTime} " +
                     $"with {summary.totalErrors} error(s).");
                return;
            }

            Debug.Log($"Build succeeded in {summary.totalTime}: {summary.outputPath}");
        }

        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static string OutputDir()
        {
            string? overridden = Environment.GetEnvironmentVariable(OutputDirEnvVar);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden.Trim();
            }

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.Combine(projectRoot, "Builds");
        }

        private static void SwitchTarget(BuildTargetGroup group, BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                return;
            }

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
            {
                Fail($"Could not switch the active build target to {target} — " +
                     "is that platform module installed?");
            }
        }

        /// <summary>
        /// Logs and exits non-zero. Throwing is not enough: <c>-quit</c> in batch mode
        /// still reports success for an unhandled exception in some Unity versions,
        /// which would let CI publish nothing and call it green.
        /// </summary>
        private static void Fail(string message)
        {
            Debug.LogError(message);
            EditorApplication.Exit(1);
        }
    }
}
