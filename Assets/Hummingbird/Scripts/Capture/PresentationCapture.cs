using System;
using System.Collections;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

/// <summary>
/// Films the Demo scene's bird to PNG frames for the presentation videos. Inert unless the player
/// is launched with -capture, so the Android app and the Editor never run it:
///
///   Hummingbird.exe -capture -captureModel Early -captureSize 1078x1312 -captureOut "C:\frames\early"
///
/// Models come from Resources/CaptureModels (written by PresentationCaptureBuild). Each model is set
/// together with its own Stacked Vectors value inside a disable/enable cycle, exactly like
/// DemoController.SelectModel, because changing either without that cycle silently wedges the agent.
/// Time.captureFramerate makes every frame exactly 1/30 s of game time, so the video plays at true
/// speed however slowly this laptop renders.
/// </summary>
public class PresentationCapture : MonoBehaviour
{
    private const int Fps = 30;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-capture") < 0) return;
        new GameObject("PresentationCapture").AddComponent<PresentationCapture>();
    }

    private static string Arg(string name, string fallback)
    {
        string[] a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
    }

    private IEnumerator Start()
    {
        string modelName = Arg("-captureModel", "Final");
        string[] size = Arg("-captureSize", "1650x414").Split('x');
        int width = int.Parse(size[0]), height = int.Parse(size[1]);
        string outDir = Arg("-captureOut", Path.Combine(Application.persistentDataPath, "capture"));
        int warmup = int.Parse(Arg("-captureWarmup", "30"));
        int frames = int.Parse(Arg("-captureFrames", "360"));
        float zoom = float.Parse(Arg("-captureZoom", "1"), System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outDir);

        CaptureModels list = Resources.Load<CaptureModels>("CaptureModels");
        CaptureModels.Entry entry = list.Find(modelName);
        if (entry == null) { Debug.LogError("[Capture] FAIL unknown model " + modelName); Application.Quit(2); yield break; }

        Time.captureFramerate = Fps;

        // Same cycle as DemoController.SelectModel, but for any of the listed models.
        GameObject bird = FindAnyObjectByType<DemoController>().Bird;
        BehaviorParameters bp = bird.GetComponent<BehaviorParameters>();
        bird.SetActive(false);
        bp.BrainParameters.NumStackedVectorObservations = entry.stackedVectors;
        bp.Model = entry.model;
        bird.SetActive(true);
        bird.GetComponent<Agent>().EndEpisode();

        CameraFollowBird follow = FindAnyObjectByType<CameraFollowBird>();
        follow.SetTarget(bird.transform);
        if (Math.Abs(zoom - 1f) > 0.001f) follow.Zoom(zoom);
        Camera cam = follow.GetComponent<Camera>();

        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Debug.Log("[Capture] model=" + modelName + " stack=" + entry.stackedVectors + " size=" + width + "x" + height);

        for (int f = 0; f < warmup + frames; f++)
        {
            yield return new WaitForEndOfFrame();

            // Only the island and the bird: the demo's buttons and labels are built at runtime,
            // so they are switched off every frame rather than once.
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;

            // No flight trails: they clutter a small video frame. Every frame, because the bird's
            // own Start turns them on from its Inspector default after this coroutine begins.
            if (FlightTrail.Visible || f < warmup) FlightTrail.SetAll(false);
            if (f < warmup) continue;

            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, string.Format("f{0:D4}.png", f - warmup)), tex.EncodeToPNG());
        }

        Debug.Log("[Capture] SUCCESS " + frames + " frames -> " + outDir);
        Application.Quit(0);
    }
}
