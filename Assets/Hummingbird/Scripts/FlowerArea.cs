using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a collection of flower plants and attached flowers
/// </summary>
public class FlowerArea : MonoBehaviour
{
    // The diameter of the area where the agent and flowers can be used
    // for observing relative distance from agent to flower
    public const float AreaDiameter = 20f;

    // These three are created here at field-initializer time rather than in Awake(), because
    // Awake() is not early enough on every path. When an island is Instantiated at runtime (the
    // race demo, and IslandSpawner on the training server) Unity interleaves Awake and OnEnable
    // as it walks the new hierarchy, so the agent's
    // OnEnable -> LazyInitialize -> OnEpisodeBegin -> ResetFlowers chain can reach
    // FindChildFlowers() before this component's own Awake() has run, and every collection below
    // is still null -- a NullReferenceException, thrown before a single flower is registered.
    // Field initializers run when the component is constructed, which is ahead of Awake and
    // OnEnable on every path, so the race cannot happen at all.

    // The list of all flower plants in this flower area (flower plants have multiple flowers)
    private List<GameObject> flowerPlants = new List<GameObject>();

    // A lookup dictionary for looking up a flower from a nectar collider
    private Dictionary<Collider, Flower> nectarFlowerDictionary = new Dictionary<Collider, Flower>();

    // Backing field for the Flowers property, and a guard so FindChildFlowers()
    // only ever runs once, no matter when it's first triggered.
    private List<Flower> flowers = new List<Flower>();
    private bool flowersFound = false;

    // The list of all flowers in the flower area.
    // Lazily found on first access (see EnsureFlowersFound) rather than in Awake()
    // or Start(), because relying on either creates a race condition: populating in
    // Start() isn't guaranteed to run before other scripts' Start() (e.g.
    // GameManager could ask for a flower before this list exists), while populating
    // in Awake() isn't guaranteed to run after every Flower's own Awake() has set
    // its nectarCollider (causing duplicate-null dictionary keys). First access is
    // always safe because every real caller queries this from a Start()-phase
    // method or later, by which point every object's Awake() has already run.
    public List<Flower> Flowers
    {
        get
        {
            EnsureFlowersFound();
            return flowers;
        }
    }

    /// <summary>
    /// Reset the flowers and flower plants
    /// </summary>
    public void ResetFlowers()
    {
        EnsureFlowersFound();

        // Rotate each flower plant around the Y axis and subtly around X and Z
        foreach (GameObject flowerPlant in flowerPlants)
        {
            float xRotation = UnityEngine.Random.Range(-5f, 5f);
            float yRotation = UnityEngine.Random.Range(-180f, 180f);
            float zRotation = UnityEngine.Random.Range(-5f, 5f);
            flowerPlant.transform.localRotation = Quaternion.Euler(xRotation, yRotation, zRotation);
        }

        // Reset each flower
        foreach (Flower flower in flowers)
        {
            flower.ResetFlower();
        }
    }

    /// <summary>
    /// Gets the <see cref="Flower"/> that a nectar collider belongs to
    /// </summary>
    /// <param name="collider">The nectar collider</param>
    /// <returns>The matching flower</returns>
    public Flower GetFlowerFromNectar(Collider collider)
    {
        EnsureFlowersFound();
        return nectarFlowerDictionary[collider];
    }

    // Awake() used to create the three collections above. It has been removed rather than left
    // empty, and must not come back: EnsureFlowersFound() can legitimately run before Awake() on a
    // runtime-instantiated island, so re-assigning fresh collections in Awake() would silently
    // discard every flower already registered -- an empty arena with no error, which is a worse
    // failure than the NullReferenceException this replaced. The field initializers are the only
    // place these should be created.

    /// <summary>
    /// Finds all flowers exactly once, on whichever comes first: this object's own
    /// Start(), or an external caller asking for Flowers/ResetFlowers/
    /// GetFlowerFromNectar before that. See the comment on the Flowers property for
    /// why this can't safely live in Awake() or unconditionally in Start().
    /// </summary>
    private void EnsureFlowersFound()
    {
        if (flowersFound) return;
        flowersFound = true;
        FindChildFlowers(transform);
    }

    /// <summary>
    /// Called when the game starts
    /// </summary>
    private void Start()
    {
        EnsureFlowersFound();
    }

    /// <summary>
    /// Recursively finds all flowers and flower plants that are children of a parent transform
    /// </summary>
    /// <param name="parent">The parent of the children to check</param>
    private void FindChildFlowers(Transform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.CompareTag("flower_plant"))
            {
                // Found a flower plant, add it to the flowerPlants list
                flowerPlants.Add(child.gameObject);

                // Look for flowers within the flower plant
                FindChildFlowers(child);
            }
            else
            {
                // Not a flower plant, look for a Flower component
                Flower flower = child.GetComponent<Flower>();
                if (flower != null)
                {
                    // Found a flower, add it to the flowers list
                    flowers.Add(flower);

                    // Add the nectar collider to the lookup dictionary
                    nectarFlowerDictionary.Add(flower.nectarCollider, flower);

                    // Note: there are no flowers that are children of other flowers
                }
                else
                {
                    // Flower component not found, so check children
                    FindChildFlowers(child);
                }
            }
        }
    }
}
