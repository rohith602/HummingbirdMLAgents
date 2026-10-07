using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Produces the browser (WebGL) demo from the command line:
///
///   "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" -quit -batchmode ^
///       -projectPath "<path to this project>" ^
///       -buildTarget WebGL ^
///       -executeMethod BuildWebGL.Build ^
///       -buildOutput "Builds/HummingbirdWeb" ^
///       -logFile "build_webgl.log"
///
/// Same pattern as BuildAndroid: batch mode so the Editor GUI does not hold memory the
/// build needs on this 7.8 GB machine, a log file readable while it runs, and an explicit
/// SUCCESS line because the exit code alone is not trustworthy.
///
/// The output is a folder (index.html, Build/, TemplateData/), not a single file. It is
/// meant for static hosting on GitHub Pages, which shapes the two settings forced below.
/// </summary>
public static class BuildWebGL
{
    // Explicit for the same reason as BuildAndroid: only Demo.unity may ship. It is the
    // copy of Training.unity that carries the Start button and the model switcher.
    private const string DemoScene = "Assets/Hummingbird/Scenes/Demo.unity";

    private const string DefaultOutput = @"Builds/HummingbirdWeb";

    public static void Build()
    {
        string output = ReadArg("-buildOutput", DefaultOutput);

        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Fail("WebGL Build Support is not installed for this Unity version.");
            return;
        }

        // Set rather than assert, unlike BuildAndroid: these were never configured for
        // this project, and both are requirements of the host rather than preferences.
        //
        // GitHub Pages cannot send the "Content-Encoding" response header that Unity's
        // compressed files need, so the loader must unpack them in JavaScript instead.
        // Gzip rather than Brotli because that fallback unpacks gzip much faster.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;

        // "Shorter build time" skips the slowest, most memory-hungry link-time
        // optimisation. Set by name so this file still compiles on a machine without
        // the WebGL module installed.
        EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", "BuildTimes");

        var options = new BuildPlayerOptions
        {
            scenes = new[] { DemoScene },
            locationPathName = output,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };

        Debug.Log("[BuildWebGL] scene=" + DemoScene + " -> " + output);
        Debug.Log("[BuildWebGL] compression=" + PlayerSettings.WebGL.compressionFormat
                + " decompressionFallback=" + PlayerSettings.WebGL.decompressionFallback);

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Fail("build result = " + summary.result + ", errors = " + summary.totalErrors);
            return;
        }

        FillBrowserWindow(Path.Combine(output, "index.html"));

        Debug.Log(string.Format(
            "[BuildWebGL] SUCCESS  {0:N1} MB in {1:N0}s  warnings={2}  -> {3}",
            summary.totalSize / (1024f * 1024f),
            summary.totalTime.TotalSeconds,
            summary.totalWarnings,
            summary.outputPath));

        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Unity's default page gives desktop browsers a fixed 960x600 canvas and only fills the
    /// window on phones. Its stylesheet already carries the fill-the-window rules (the
    /// "unity-mobile" class), so the desktop branch is pointed at them here instead of
    /// keeping a whole custom WebGL template in the project for a two-line difference.
    /// </summary>
    private static void FillBrowserWindow(string indexPath)
    {
        string html = File.ReadAllText(indexPath);

        string patched = Regex.Replace(
            html,
            @"canvas\.style\.width = ""\d+px"";\s*canvas\.style\.height = ""\d+px"";",
            "document.querySelector(\"#unity-container\").className = \"unity-mobile\";\n"
          + "        canvas.className = \"unity-mobile\";");

        if (patched == html)
        {
            // Not fatal: the build still runs, just in the small fixed-size canvas.
            Debug.LogWarning("[BuildWebGL] index.html has no fixed desktop canvas size to replace; "
                           + "the page was left as Unity generated it.");
            return;
        }

        File.WriteAllText(indexPath, patched);
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
        Debug.LogError("[BuildWebGL] FAILED: " + reason);
        EditorApplication.Exit(1);
    }
}
