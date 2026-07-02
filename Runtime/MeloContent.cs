using System;
using System.IO;
using UnityEngine;

namespace Melowrite.Audio
{
    // Resolves a Melowrite content path (a .melo project, or an audio file) to a real disk path.
    // Paths are relative to the Assets folder, e.g. "MelowriteProjects/Battle/music.melo";
    // a leading "Assets/" is accepted and stripped.
    //
    //   - Editor: read from Assets/<path>.
    //   - Build: read from StreamingAssets/Melowrite/<path>, where MelowriteBuildProcessor
    //     mirrors project bundles during the build.
    public static class MeloContent
    {
        // Subfolder under StreamingAssets the build hook mirrors project bundles into.
        // Keep in sync with MelowriteBuildProcessor.
        public const string BuildSubfolder = "Melowrite";

        // Resolve an Assets-relative content path to an absolute disk path that exists, or null.
        public static string Resolve(string assetRelativePath)
        {
            if (string.IsNullOrEmpty(assetRelativePath)) return null;
            string rel = Normalize(assetRelativePath);

#if UNITY_EDITOR
            // Editor: read from the Assets folder, so you can edit the project in place and press Play.
            string inAssets = Path.Combine(Application.dataPath, rel);
            if (File.Exists(inAssets)) return inAssets;
#endif
            // Build (and editor fallback): the bundle mirror under StreamingAssets.
            string mirrored = Path.Combine(Application.streamingAssetsPath, BuildSubfolder, rel);
            if (File.Exists(mirrored)) return mirrored;

            return null;
        }

        // Turn "Assets/Foo/x.melo", "Foo/x.melo", or "\Foo\x.melo" into "Foo/x.melo".
        public static string Normalize(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            p = p.Replace('\\', '/').TrimStart('/');
            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("Assets/".Length);
            return p;
        }
    }
}
