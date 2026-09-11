using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Melowrite.Audio
{
    /// <summary>
    /// chunk/track/bus/palette names straight out of the json, no samples, no engine.
    /// cheap enough for an inspector to run on every file change
    /// </summary>
    public sealed class MeloProjectMetadata
    {
        public string Name { get; private set; } = "";
        public int Tempo { get; private set; }
        public int BeatsPerBar { get; private set; }
        public int BeatUnit { get; private set; }
        public List<string> ChunkNames { get; } = new();
        public List<string> TrackNames { get; } = new();
        public List<string> BusNames { get; } = new();
        public List<string> PaletteNames { get; } = new();

        /// <summary>
        /// null if it cant be read or parsed
        /// </summary>
        public static MeloProjectMetadata? Read(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(projectPath));
                var root = doc.RootElement;
                var meta = new MeloProjectMetadata();

                if (root.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    meta.Name = nameEl.GetString() ?? "";
                if (root.TryGetProperty("Tempo", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                    meta.Tempo = tEl.GetInt32();
                if (root.TryGetProperty("BeatsPerBar", out var bEl) && bEl.ValueKind == JsonValueKind.Number)
                    meta.BeatsPerBar = bEl.GetInt32();
                if (root.TryGetProperty("BeatUnit", out var buEl) && buEl.ValueKind == JsonValueKind.Number)
                    meta.BeatUnit = buEl.GetInt32();

                ReadNamedArray(root, "Chunks", meta.ChunkNames);
                ReadNamedArray(root, "Tracks", meta.TrackNames);
                ReadNamedArray(root, "Buses", meta.BusNames);

                // palettes live on tracks, dedupe them
                if (root.TryGetProperty("Tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Array)
                {
                    var seen = new HashSet<string>();
                    foreach (var t in tracksEl.EnumerateArray())
                    {
                        if (t.TryGetProperty("Palette", out var pEl)
                            && pEl.ValueKind == JsonValueKind.String)
                        {
                            var p = pEl.GetString();
                            if (!string.IsNullOrEmpty(p) && seen.Add(p))
                                meta.PaletteNames.Add(p);
                        }
                    }
                }

                return meta;
            }
            catch
            {
                return null;
            }
        }

        private static void ReadNamedArray(JsonElement root, string property, List<string> dst)
        {
            if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    dst.Add(nameEl.GetString() ?? "");
            }
        }
    }
}
