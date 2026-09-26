using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Report figures and facts, produced in batch mode:
///
///   Unity.exe -batchmode -projectPath "<path to this project>" ^
///       -executeMethod ReportCapture.Run -reportOut "<folder>" -logFile "<log>"
///
/// Opens scenes, renders a temporary camera to PNG and logs measured facts (flower counts, tags,
/// Behavior Parameters, sensor settings). Read-only: it never saves a scene or asset. Must run
/// WITHOUT -nographics, since cam.Render() needs a real graphics device.
/// </summary>
public static class ReportCapture
{
    private static string outDir;
    private static readonly StringBuilder facts = new StringBuilder();

    public static void Run()
    {
        outDir = ReadArg("-reportOut", Path.Combine(Application.dataPath, "../ReportCapture"));
        Directory.CreateDirectory(outDir);

        try
        {
            TrainingScene();
            FlowerIslandScene();
            DemoScene();
            IslandGrid();
            File.WriteAllText(Path.Combine(outDir, "unity_facts.txt"), facts.ToString());
            Debug.Log("[ReportCapture] facts:\n" + facts);
            Debug.Log("[ReportCapture] SUCCESS");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ReportCapture] FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ scenes

    private static void TrainingScene()
    {
        EditorSceneManager.OpenScene("Assets/Hummingbird/Scenes/Training.unity", OpenSceneMode.Single);
        Fact("== Training.unity");
        DescribeAgents();
        FlowerArea area = Object.FindAnyObjectByType<FlowerArea>();
        DescribeIsland(area.gameObject);

        Vector3 c = area.transform.position;
        // The first render after opening a scene came out tinted; render once and overwrite.
        Shot("unity_training_overview.png", c + new Vector3(0f, 9f, -15f), c + new Vector3(0f, 0.5f, 0f), 50f);
        Shot("unity_training_overview.png", c + new Vector3(0f, 9f, -15f), c + new Vector3(0f, 0.5f, 0f), 50f);

        Shot("unity_island_top.png", c + new Vector3(0f, 26f, -0.5f), c, 50f);
        Flower fl = area.GetComponentsInChildren<Flower>(true)[40];
        Vector3 fp = fl.transform.position;
        Shot("unity_flower_closeup.png", fp + new Vector3(0.9f, 0.35f, -1.1f), fp + new Vector3(0f, -0.25f, 0f), 45f);

        HummingbirdAgent agent = Object.FindAnyObjectByType<HummingbirdAgent>(FindObjectsInactive.Include);
        Transform b = agent.transform;
        Shot("unity_bird_closeup.png", b.position + new Vector3(0.45f, 0.18f, -0.55f), b.position, 40f);

        // Ray sensors, drawn from the sensors' own ray geometry (same maths as RaySensorVisualizer).
        List<GameObject> temp = DrawRays(agent);
        Shot("unity_bird_rays.png", b.position + new Vector3(-2.2f, 1.6f, -3.2f), b.position + new Vector3(0f, 0f, 1.5f), 55f);
        foreach (var go in temp) Object.DestroyImmediate(go);
    }

    private static void FlowerIslandScene()
    {
        EditorSceneManager.OpenScene("Assets/Hummingbird/Scenes/Flower Island.unity", OpenSceneMode.Single);
        Fact("== Flower Island.unity");
        DescribeAgents();
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null) Fact("GameManager maxNectar=" + gm.maxNectar + " timerAmount=" + gm.timerAmount);
        FlowerArea area = Object.FindAnyObjectByType<FlowerArea>();
        Vector3 c = area.transform.position;
        Shot("unity_flowerisland_overview.png", c + new Vector3(-11f, 6f, -11f), c + new Vector3(0f, 1f, 0f), 50f);
    }

    private static void DemoScene()
    {
        EditorSceneManager.OpenScene("Assets/Hummingbird/Scenes/Demo.unity", OpenSceneMode.Single);
        Fact("== Demo.unity");
        DescribeAgents();
        DemoController demo = Object.FindAnyObjectByType<DemoController>(FindObjectsInactive.Include);
        Fact("DemoController present=" + (demo != null) + (demo != null ? " birdActiveInScene=" + demo.Bird.activeSelf : ""));
    }

    private static void IslandGrid()
    {
        // Same layout maths as IslandSpawner.Awake, in an unsaved scene, for a picture of a 28-island server.
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Hummingbird/Prefabs/FloatingIsland.prefab");
        int count = 28;
        float spacing = 25f;
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)columns);
        float xo = (columns - 1) * spacing * 0.5f, zo = (rows - 1) * spacing * 0.5f;
        for (int i = 0; i < count; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = new Vector3((i % columns) * spacing - xo, 0f, (i / columns) * spacing - zo);
        }
        Fact("== Grid: " + count + " islands, " + columns + "x" + rows + " at " + spacing + " spacing, total GameObjects="
             + Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length
             + " flowers=" + Object.FindObjectsByType<Flower>(FindObjectsInactive.Include).Length);
        Shot("unity_grid_28.png", new Vector3(0f, 95f, -120f), new Vector3(0f, 0f, -8f), 50f, 1000f);
    }

    // ------------------------------------------------------------------ facts

    private static void DescribeAgents()
    {
        foreach (var a in Object.FindObjectsByType<HummingbirdAgent>(FindObjectsInactive.Include))
        {
            var bp = a.GetComponent<BehaviorParameters>();
            var dr = a.GetComponent<DecisionRequester>();
            var rb = a.GetComponent<Rigidbody>();
            Fact(string.Format("Agent '{0}' active={1} trainingMode={2} MaxStep={3} moveForce={4} pitchSpeed={5} yawSpeed={6}",
                a.name, a.gameObject.activeInHierarchy, a.trainingMode, a.MaxStep, a.moveForce, a.pitchSpeed, a.yawSpeed));
            if (bp != null)
                Fact(string.Format("  BehaviorParameters name={0} vecObs={1} stacked={2} continuous={3} discreteBranches={4} model={5} type={6} device={7} useChildSensors={8}",
                    bp.BehaviorName, bp.BrainParameters.VectorObservationSize, bp.BrainParameters.NumStackedVectorObservations,
                    bp.BrainParameters.ActionSpec.NumContinuousActions, bp.BrainParameters.ActionSpec.NumDiscreteActions,
                    bp.Model != null ? bp.Model.name : "none", bp.BehaviorType, bp.InferenceDevice, bp.UseChildSensors));
            if (dr != null) Fact("  DecisionRequester period=" + dr.DecisionPeriod + " takeActionsBetweenDecisions=" + dr.TakeActionsBetweenDecisions);
            if (rb != null) Fact("  Rigidbody mass=" + rb.mass + " linearDamping=" + rb.linearDamping + " angularDamping=" + rb.angularDamping + " useGravity=" + rb.useGravity);
            foreach (var s in a.GetComponentsInChildren<RayPerceptionSensorComponent3D>(true))
                Fact(string.Format("  RaySensor '{0}' raysPerDirection={1} maxRayDegrees={2} rayLength={3} sphereCastRadius={4} tags=[{5}] startOffset={6} endOffset={7}",
                    s.SensorName, s.RaysPerDirection, s.MaxRayDegrees, s.RayLength, s.SphereCastRadius,
                    string.Join(",", s.DetectableTags.ConvertAll(t => "'" + t + "'")), s.StartVerticalOffset, s.EndVerticalOffset));
        }
    }

    private static void DescribeIsland(GameObject island)
    {
        int transforms = island.GetComponentsInChildren<Transform>(true).Length;
        int flowers = island.GetComponentsInChildren<Flower>(true).Length;
        int plants = 0, nectar = 0, boundary = 0, untaggedColliders = 0;
        foreach (var t in island.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag("flower_plant")) plants++;
            if (t.CompareTag("nectar")) nectar++;
            if (t.CompareTag("boundary")) boundary++;
        }
        foreach (var col in island.GetComponentsInChildren<Collider>(true))
            if (col.CompareTag("Untagged") && !col.isTrigger) untaggedColliders++;
        Fact(string.Format("Island '{0}' GameObjects={1} Flowers={2} flower_plant={3} nectarTagged={4} boundaryTagged={5} untaggedSolidColliders={6}",
            island.name, transforms, flowers, plants, nectar, boundary, untaggedColliders));
    }

    // ------------------------------------------------------------------ drawing

    private static List<GameObject> DrawRays(HummingbirdAgent agent)
    {
        var made = new List<GameObject>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        int total = 0;
        foreach (var sensor in agent.GetComponentsInChildren<RayPerceptionSensorComponent3D>(true))
        {
            var input = sensor.GetRayPerceptionInput();
            var output = RayPerceptionSensor.Perceive(input, false);
            for (int i = 0; i < input.Angles.Count; i++)
            {
                var ext = input.RayExtents(i);
                var ray = output.RayOutputs[i];
                Vector3 end = ray.HasHit ? Vector3.Lerp(ext.StartPositionWorld, ext.EndPositionWorld, ray.HitFraction) : ext.EndPositionWorld;
                Color col = !ray.HasHit ? new Color(0.25f, 0.85f, 1f, 0.8f) : (ray.HitTagIndex >= 0 ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.85f, 0.2f));
                var go = new GameObject("ReportRay");
                var lr = go.AddComponent<LineRenderer>();
                lr.material = mat; lr.positionCount = 2; lr.useWorldSpace = true;
                lr.startWidth = lr.endWidth = 0.025f; lr.startColor = lr.endColor = col;
                lr.SetPosition(0, ext.StartPositionWorld); lr.SetPosition(1, end);
                made.Add(go);
                total++;
                Fact(string.Format("  ray {0}/{1}: hit={2} tagIndex={3} distance={4:F2}", sensor.SensorName, i, ray.HasHit, ray.HitTagIndex,
                    ray.HasHit ? ray.HitFraction * sensor.RayLength : sensor.RayLength));
            }
        }
        Fact("Rays drawn: " + total);
        return made;
    }

    private static void Shot(string file, Vector3 pos, Vector3 target, float fov, float far = 300f)
    {
        var go = new GameObject("ReportCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(target);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = far;
        cam.clearFlags = CameraClearFlags.Skybox;
        Render(cam, file);
        Object.DestroyImmediate(go);
    }

    private static void ShotFrom(string file, Camera source)
    {
        var go = new GameObject("ReportCamera");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(source);
        cam.targetDisplay = 0;
        Render(cam, file);
        Object.DestroyImmediate(go);
    }

    private static void Render(Camera cam, string file)
    {
        const int w = 1920, h = 1080;
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Fact("wrote " + file);
    }

    private static void Fact(string line)
    {
        facts.AppendLine(line);
    }

    private static string ReadArg(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return fallback;
    }
}
