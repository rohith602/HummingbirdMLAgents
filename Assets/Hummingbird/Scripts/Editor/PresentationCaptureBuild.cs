using System.IO;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Demo scene as a Windows player for PresentationCapture:
///
///   Unity.exe -batchmode -quit -projectPath "<path to this project>" -buildTarget Win64 ^
///       -executeMethod PresentationCaptureBuild.Run -captureBuildOut "<folder>" -logFile "<log>"
///
/// Writes Resources/CaptureModels.asset (the models to film, each with its trained Stacked Vectors)
/// and builds. No scene is opened for editing or saved. Logs SUCCESS explicitly, because batch-mode
/// exit codes are unreliable.
/// </summary>
public static class PresentationCaptureBuild
{
    private const string Models = "Assets/Hummingbird/NN Models/";
    private const string ListPath = "Assets/Hummingbird/Scripts/Capture/Resources/CaptureModels.asset";

    public static void Run()
    {
        string outDir = Arg("-captureBuildOut", Path.Combine(Path.GetTempPath(), "hb_capture"));

        Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
        CaptureModels list = ScriptableObject.CreateInstance<CaptureModels>();
        list.entries = new[]
        {
            Entry("Early", "Hummingbird_Early_500k.onnx", 1),     // HB_01 at 0.5M steps
            Entry("Mid", "Hummingbird_Mid_2500k.onnx", 1),        // HB_01 at 2.5M steps
            Entry("OnDevice", "OnDevice_Final.onnx", 1),          // HB_01 final, 41.29
            Entry("RentedGpu", "RentedGPU_Final.onnx", 3),        // HB_16 final, 96.58
        };
        AssetDatabase.DeleteAsset(ListPath);
        AssetDatabase.CreateAsset(list, ListPath);
        AssetDatabase.SaveAssets();

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Hummingbird/Scenes/Demo.unity" },
            locationPathName = Path.Combine(outDir, "Hummingbird.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log("[CaptureBuild] SUCCESS " + report.summary.totalSize + " bytes in " + report.summary.totalTime);
        else
            Debug.LogError("[CaptureBuild] FAIL " + report.summary.result + " errors=" + report.summary.totalErrors);
    }

    private static CaptureModels.Entry Entry(string name, string file, int stack)
    {
        ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(Models + file);
        if (model == null) throw new FileNotFoundException("[CaptureBuild] FAIL missing model " + file);
        return new CaptureModels.Entry { name = name, model = model, stackedVectors = stack };
    }

    private static string Arg(string name, string fallback)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
    }
}
