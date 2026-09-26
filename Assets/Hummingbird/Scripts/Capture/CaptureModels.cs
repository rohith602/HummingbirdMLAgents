using System;
using Unity.InferenceEngine;
using UnityEngine;

/// <summary>
/// The models PresentationCapture can film, each paired with the Stacked Vectors value it was
/// trained with (HB_01 checkpoints 1, HB_16 3). Written by PresentationCaptureBuild into a
/// Resources folder so a player build includes the models.
/// </summary>
public class CaptureModels : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string name;
        public ModelAsset model;
        public int stackedVectors;
    }

    public Entry[] entries = new Entry[0];

    public Entry Find(string name)
    {
        foreach (Entry e in entries) if (e.name == name) return e;
        return null;
    }
}
