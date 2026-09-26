using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Produces the Android demo APK from the command line:
///
///   "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" -quit -batchmode ^
///       -projectPath "<path to this project>" ^
///       -executeMethod BuildAndroid.Build ^
///       -buildOutput "Builds/HummingbirdDemo.apk" ^
///       -logFile "build_android.log"
///
/// Batch mode rather than building from the open Editor, for the same reasons as
/// BuildLinuxHeadless: no Editor GUI (which matters a great deal here — an IL2CPP
/// Android build on this 7.8 GB machine is memory-bound, and the open Editor holds
/// ~1.5 GB it does not need to), a plain log file readable while the build runs, and
/// a real exit code.
///
/// The APK is ARM64 + IL2CPP because the target device (Snapdragon 8 Gen 3) is ARMv9
/// and has no 32-bit support at all; Unity only reaches ARM64 through IL2CPP, so Mono
/// is not an option. Both are already set in ProjectSettings — asserted below rather
/// than assumed, since a silent Mono/ARMv7 build would install and then crash on the
/// device with nothing useful in the log.
/// </summary>
public static class BuildAndroid
{
    // Kept explicit rather than read from EditorBuildSettings, matching
    // BuildLinuxHeadless: a stray scene enabled in the Build Settings window must not
    // be able to slip into — or replace — the demo build. Flower Island in particular
    // must never ship: its birds run trainingMode = 0, so flowers never refill.
    //
    // This is Demo.unity — a copy of Training.unity that carries the Start button and
    // the model switcher. Training.unity is deliberately left pristine so it can still
    // be shown as the bare training setup; the two are separate on purpose, and only
    // this one ships in the APK.
    private const string DemoScene = "Assets/Hummingbird/Scenes/Demo.unity";

    private const string DefaultOutput = @"Builds/HummingbirdDemo.apk";

    public static void Build()
    {
        string output = ReadArg("-buildOutput", DefaultOutput);

        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Fail("Android Build Support is not installed for this Unity version.");
            return;
        }

        // Assert rather than set: if these ever drift, the build should stop loudly
        // instead of quietly producing an APK that cannot run on the target device.
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
        if (backend != ScriptingImplementation.IL2CPP)
        {
            Fail("scripting backend is " + backend + ", expected IL2CPP (ARM64 requires it).");
            return;
        }

        if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
        {
            Fail("ARM64 is not in target architectures (" + PlayerSettings.Android.targetArchitectures + ").");
            return;
        }

        // Building an .apk, not an .aab: this is sideloaded to one phone for a demo,
        // never uploaded to Play, and an app bundle cannot be installed directly.
        EditorUserBuildSettings.buildAppBundle = false;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { DemoScene },
            locationPathName = output,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,

            // Non-development build. A development build adds the profiler and deep
            // script instrumentation, which would slow the physics stepping that
            // dominates this scene's frame time — exactly what Phase 0 is measuring.
            options = BuildOptions.None
        };

        Debug.Log("[BuildAndroid] scene=" + DemoScene + " -> " + output);
        Debug.Log("[BuildAndroid] backend=" + backend + " arch=" + PlayerSettings.Android.targetArchitectures);

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Fail("build result = " + summary.result + ", errors = " + summary.totalErrors);
            return;
        }

        Debug.Log(string.Format(
            "[BuildAndroid] SUCCESS  {0:N1} MB in {1:N0}s  warnings={2}  -> {3}",
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
        Debug.LogError("[BuildAndroid] FAILED: " + reason);
        EditorApplication.Exit(1);
    }
}
