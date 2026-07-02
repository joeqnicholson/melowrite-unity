using UnityEngine;

// A .melo project as a first-class Unity asset (imported by the Editor's MeloImporter). Drag one
// onto a public MeloFile field and hand it to Melo.Load. Think of it like an AudioClip: it's the
// file/data, not the playback. The reference survives into builds, so you never touch path strings.
public sealed class MeloFile : ScriptableObject
{
    // Path to the .melo relative to the Assets folder, e.g. "MelowriteProjects/Battle/music.melo".
    // Filled in by the importer; the build hook ships the file and its sibling asset folders.
    public string ProjectPath;
}
