using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace BrawlArena.EditorAutomation
{
    /// Exports the iOS Xcode project locally so CI only needs a Mac for
    /// compile/sign/upload (no Unity license required in CI).
    public static class IosExportBuilder
    {
        public const string OutputPath = "Builds/iOSExport";

        public static string BuildXcodeProject()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes in EditorBuildSettings.");
            }

            if (Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, recursive: true);
            }
            Directory.CreateDirectory(OutputPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"iOS export failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            return $"iOS Xcode project exported to {OutputPath} " +
                   $"({report.summary.totalSize / (1024 * 1024)} MB, {scenes.Length} scenes)";
        }
    }
}
