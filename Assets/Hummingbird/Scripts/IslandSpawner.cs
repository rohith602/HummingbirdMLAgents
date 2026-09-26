using UnityEngine;

/// <summary>
/// Builds the training grid at runtime by cloning the FloatingIsland prefab into
/// a roughly square grid as soon as the scene loads.
///
/// Why this is done in code instead of simply placing N islands in Training.unity:
/// one island is ~3,800 GameObjects, so a 28-island scene would be ~107,000 of them.
/// That is heavy to author on a low-RAM machine, it bloats the build, and — the
/// part that actually matters — it bakes the island count into the build. Spawning
/// instead makes the count a launch flag, so the question "how many islands does
/// this rented box actually fit?" is answered by re-running the same build with a
/// different number, rather than rebuilding and re-uploading it for every guess.
///
/// mlagents-learn passes everything after --env-args straight through to the
/// player, so the count is set like this:
///     mlagents-learn ... --env-args -logFile out.log --islands 28
/// </summary>
public class IslandSpawner : MonoBehaviour
{
    // The island to clone. Assign to Assets/Hummingbird/Prefabs/FloatingIsland.prefab.
    public GameObject islandPrefab;

    // Used when no --islands flag was passed, e.g. when pressing Play in the Editor.
    // Kept at 1 so opening the scene by hand stays light on this machine.
    public int defaultIslandCount = 1;

    // Centre-to-centre gap between islands. FlowerArea.AreaDiameter is 20f and the
    // island's visible geometry measures ~19.5 units across, so 25 leaves a real
    // margin and keeps one island's colliders clear of its neighbour's.
    public float spacing = 25f;

    // The island already present in Training.unity. It is reused as the first grid
    // slot instead of being destroyed, so a 1-island run allocates nothing new.
    // Wired explicitly in the Inspector rather than looked up by name, because once
    // clones exist there are several similarly-named objects and GameObject.Find
    // gives no ordering guarantee about which one it returns.
    public GameObject existingIsland;

    private void Awake()
    {
        if (islandPrefab == null)
        {
            Debug.LogError("IslandSpawner: islandPrefab is not assigned, no islands will be spawned.");
            return;
        }

        int count = Mathf.Max(1, ReadIslandCountFromCommandLine(defaultIslandCount));

        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)columns);

        // Centre the grid on the origin so the scene camera still frames it.
        float xOffset = (columns - 1) * spacing * 0.5f;
        float zOffset = (rows - 1) * spacing * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector3 position = new Vector3(
                (i % columns) * spacing - xOffset,
                0f,
                (i / columns) * spacing - zOffset);

            if (i == 0 && existingIsland != null)
            {
                existingIsland.transform.position = position;
                continue;
            }

            GameObject clone = Instantiate(islandPrefab, position, Quaternion.identity);
            clone.name = "FloatingIsland_" + i;
        }

        // Logged so the Unity -logFile on the rented box confirms what actually ran.
        // Without this there is no way to tell a working --islands flag from one that
        // was silently ignored, and the whole point of the run is measuring the count.
        Debug.Log("IslandSpawner: spawned " + count + " islands in a " + columns + "x" + rows +
                  " grid at " + spacing + "-unit spacing.");
    }

    /// <summary>
    /// Reads "--islands N" from the process command line, falling back to the
    /// Inspector value when the flag is absent or unparseable.
    /// </summary>
    private int ReadIslandCountFromCommandLine(int fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();

        // Stop one short of the end: a trailing "--islands" has no value after it.
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--islands" && args[i] != "-islands") continue;

            int parsed;
            if (int.TryParse(args[i + 1], out parsed) && parsed > 0) return parsed;

            Debug.LogWarning("IslandSpawner: could not read an island count from '" +
                             args[i + 1] + "', falling back to " + fallback + ".");
        }

        return fallback;
    }
}
