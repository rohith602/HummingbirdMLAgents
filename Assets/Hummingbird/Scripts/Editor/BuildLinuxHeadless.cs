using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Produces the headless Linux training build from the command line, so a rebuild
/// is one repeatable command instead of a sequence of Editor clicks:
///
///   "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" -quit -batchmode ^
///       -projectPath "<path to this project>" ^
///       -executeMethod BuildLinuxHeadless.Build ^
///       -buildOutput "...\Mini Project\Training\build\Hummingbird.x86_64" ^
///       -logFile "...\build_linux.log"
///
/// Batch mode is used rather than building from the open Editor because it needs no
/// Editor GUI (noticeably less memory on a machine this tight), it writes a plain
/// log file that can be read while the build runs, and it returns a real exit code —
/// so a failure is unambiguous instead of having to be inferred from a timed-out
/// tool call.
///
/// NOTE: Unity registers its installed build-target modules once at startup. If
/// Linux Build Support was installed while the Editor was already open, the build
/// fails with "Build target 'StandaloneLinux64' not supported" even though the files
/// are on disk. Restarting Unity — which batch mode does by definition — clears it.
/// </summary>
public static class BuildLinuxHeadless
{
    // The one scene that actually gets trained in. Kept explicit rather than reading
    // EditorBuildSettings, so a stray scene left enabled in the Build Settings window
    // cannot silently end up in — or worse, replace — the training build.
    private const string TrainingScene = "Assets/Hummingbird/Scenes/Training.unity";

    private const string DefaultOutput =
        @"Builds/Linux/Hummingbird.x86_64";

    public static void Build()
    {
        string output = ReadArg("-buildOutput", DefaultOutput);

        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
        {
            Fail("Linux Build Support is not installed for this Unity version.");
            return;
        }

        // Player, not Dedicated Server: only "Linux Build Support (Mono)" is installed,
        // and the server subtarget needs its own separate module.
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { TrainingScene },
            locationPathName = output,
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            subtarget = (int)StandaloneBuildSubtarget.Player,

            // BuildOptions.None means a NON-development build. This matters for
            // throughput: a development build carries the profiler and deep script
            // instrumentation, which slows the physics stepping that is already
            // ~95% of training wall clock.
            options = BuildOptions.None
        };

        Debug.Log("[BuildLinuxHeadless] scene=" + TrainingScene + " -> " + output);

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Fail("build result = " + summary.result + ", errors = " + summary.totalErrors);
            return;
        }

        Debug.Log(string.Format(
            "[BuildLinuxHeadless] SUCCESS  {0:N1} MB in {1:N0}s  warnings={2}  -> {3}",
            summary.totalSize / (1024f * 1024f),
            summary.totalTime.TotalSeconds,
            summary.totalWarnings,
            summary.outputPath));

        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Reads "-name value" from Unity's own command line, falling back when absent.
    /// </summary>
    private static string ReadArg(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();

        // Stop one short of the end: a trailing flag has no value after it.
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }

        return fallback;
    }

    private static void Fail(string reason)
    {
        // LogError alone would not change the exit code, and batch mode would report
        // success to the caller. The non-zero Exit is what actually signals failure.
        Debug.LogError("[BuildLinuxHeadless] FAILED: " + reason);
        EditorApplication.Exit(1);
    }
}
