using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

// Makes .melo a recognized Unity asset type. Unity imports each .melo into a MeloFile that carries
// its Assets-relative path, so you can drag a .melo onto a MeloFile field and it resolves in the
// editor AND in builds (no path strings, no editor-only null references).
//
// Icon: drop a square PNG at Editor/MeloFileIcon.png and .melo files show it in the Project window.
// If it's missing, the default asset icon is used. (Bumping the [ScriptedImporter] version below
// forces Unity to re-import existing .melo files so they pick up the icon.)
[ScriptedImporter(3, "melo")]
public sealed class MeloImporter : ScriptedImporter
{
    const string IconPath = "Packages/com.melowrite.audio/Editor/MeloFileIcon.png";

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var file = ScriptableObject.CreateInstance<MeloFile>();

        string p = ctx.assetPath.Replace('\\', '/');   // "Assets/.../music.melo"
        file.ProjectPath = p.StartsWith("Assets/") ? p.Substring("Assets/".Length) : p;

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);   // null if not present yet
        ctx.AddObjectToAsset("melo", file, icon);   // icon = the Project-window thumbnail
        ctx.SetMainObject(file);
    }
}
