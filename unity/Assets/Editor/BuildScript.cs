#nullable enable

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
        public static void BuildAndroidAab() => Guarded(() => RunAndroid(appBundle: true, artifactName: AabName));

        /// <summary>Standalone APK for sideloading onto a test device.</summary>
        public static void BuildAndroidApk() => Guarded(() => RunAndroid(appBundle: false, artifactName: ApkName));

        /// <summary>Exports the Xcode project. The .ipa is produced by xcodebuild on macOS.</summary>
        public static void BuildIos() => Guarded(() =>
        {
            SwitchTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            ReleaseBuildSettings.Apply(BuildTarget.iOS);

            Run(BuildTarget.iOS, Path.Combine(OutputDir(), XcodeProjectDirName));
        });

        /// <summary>
        /// Runs a build and guarantees the Editor exits with a meaningful code.
        /// Callers launch Unity WITHOUT <c>-quit</c>: when startup triggers a script
        /// recompile, <c>-quit</c> tears the Editor down before <c>-executeMethod</c>
        /// is ever invoked, and the run exits 0 having built nothing. Since nothing
        /// else will stop the Editor, every path out of a batch-mode build has to
        /// come through here.
        /// </summary>
        private static void Guarded(Action build)
        {
            try
            {
                build();
                Complete(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Build failed: {ex}");
                Complete(1);
            }
        }

        private static void Complete(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
                return;
            }

            if (exitCode != 0)
            {
                throw new BuildFailedException("Build failed — see the Console for the cause.");
            }
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
                throw new BuildFailedException(
                    "No scenes are enabled in Build Settings — the player would be empty.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(locationPathName)!);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = target,
                options = BuildOptions.None,
            };

            // "[PoseBuild]" is the marker the wrapper scripts grep for to tell a real
            // build failure apart from -executeMethod never having been invoked.
            Debug.Log($"[PoseBuild] Building {target} -> {locationPathName} " +
                      $"(version {PlayerSettings.bundleVersion}, {scenes.Length} scene(s))");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Build {summary.result} after {summary.totalTime} " +
                    $"with {summary.totalErrors} error(s).");
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
                throw new BuildFailedException(
                    $"Could not switch the active build target to {target} — " +
                    "is that platform module installed?");
            }
        }
    }
}
