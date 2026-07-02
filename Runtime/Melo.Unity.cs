using System;
using System.Collections.Generic;
using UnityEngine;

namespace Melowrite
{
    // Unity-only extras on the shared Melo API: drag-and-drop .melo assets and AudioClip SFX.
    // MonoGame / SDL / console hosts simply don't compile this file.
    public static partial class Melo
    {
        // Maps a project's resolved path back to the MeloFile asset it came from, so MeloInstance.File
        // can hand you the asset (for `instance.File == myMeloFile`). Populated whenever you load or
        // switch to a MeloFile.
        private static readonly Dictionary<string, MeloFile> _fileByPath = new Dictionary<string, MeloFile>();

        internal static void Register(MeloFile file)
        {
            if (file == null) return;
            string resolved = MeloDirector.ResolvePath(file.ProjectPath);
            if (resolved != null) _fileByPath[resolved] = file;
        }

        internal static MeloFile FileForPath(string resolvedPath)
            => resolvedPath != null && _fileByPath.TryGetValue(resolvedPath, out var f) ? f : null;

        // Load a project from a .melo file dragged into the Inspector. Null if the file is null.
        public static MeloInstance Load(MeloFile file)
        {
            if (file == null) return null;
            Register(file);
            return Load(file.ProjectPath);
        }

        // Load a .melo off the main thread. Returns immediately; the optional `onLoaded` fires on the
        // main thread with the ready channel, or skip it just to warm the project for a later SwitchSong.
        public static void LoadAsync(MeloFile file, Action<MeloInstance> onLoaded = null)
        {
            if (file == null) { onLoaded?.Invoke(null); return; }
            Register(file);
            LoadAsync(file.ProjectPath, onLoaded);
        }

        // Play a Unity AudioClip once; no path needed. The clip's PCM is copied into the SFX player
        // once (cached by instance id). Clip must not be Streaming load type (use Decompress On Load).
        public static int PlayOneShot(AudioClip clip, float volume = 1f, float pan = 0f, float pitch = 1f,
                                      bool loop = false, bool effects = true)
        {
            if (clip == null) { Debug.LogError("[Melo] PlayOneShot: null AudioClip"); return -1; }
            var sfx = MeloDirector.Instance.Sfx;
            string key = "unity:" + clip.GetInstanceID();

            // Copy the clip's PCM into the SFX player once; keyed by instance id so repeat fires reuse it.
            if (!sfx.IsLoaded(key))
            {
                int ch = Mathf.Max(1, clip.channels);
                int frames = clip.samples;
                if (frames < 2) { Debug.LogError($"[Melo] AudioClip '{clip.name}' has no samples"); return -1; }

                var interleaved = new float[frames * ch];
                if (!clip.GetData(interleaved, 0))
                {
                    Debug.LogError($"[Melo] Couldn't read AudioClip '{clip.name}' (set its Load Type to Decompress On Load, not Streaming)");
                    return -1;
                }
                var left = new float[frames];
                var right = new float[frames];
                if (ch >= 2)
                    for (int i = 0; i < frames; i++) { left[i] = interleaved[i * ch]; right[i] = interleaved[i * ch + 1]; }
                else
                    for (int i = 0; i < frames; i++) { left[i] = interleaved[i]; right[i] = interleaved[i]; }

                sfx.LoadClipPcm(key, left, right, clip.frequency);
            }
            return sfx.Play(key, volume, pan, pitch, loop, effects);
        }
    }

    // Unity-only members on the persistent channel: the MeloFile getter and a MeloFile SwitchSong.
    public sealed partial class MeloInstance
    {
        // The .melo asset this channel is playing right now (changes after a SwitchSong lands). Lets you
        // write `if (player.File == carnivalFile)`. Null if the channel wasn't loaded from a MeloFile.
        public MeloFile File => Melo.FileForPath(_path);

        // Re-point this channel to a different project by dragging in its MeloFile. Optional start
        // chunk (name or index) picks the section the new song begins on.
        public void SwitchSong(MeloFile file, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSong(file.ProjectPath, when, fadeOut);
        }

        public void SwitchSong(MeloFile file, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSong(file.ProjectPath, startChunk, when, fadeOut);
        }

        public void SwitchSong(MeloFile file, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSong(file.ProjectPath, startChunk, when, fadeOut);
        }

        // Crossfade (overlap) variants of SwitchSong.
        public void SwitchSongCrossfade(MeloFile file, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSongCrossfade(file.ProjectPath, duration, when);
        }

        public void SwitchSongCrossfade(MeloFile file, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSongCrossfade(file.ProjectPath, startChunk, duration, when);
        }

        public void SwitchSongCrossfade(MeloFile file, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (file == null) return;
            Melo.Register(file);
            SwitchSongCrossfade(file.ProjectPath, startChunk, duration, when);
        }
    }
}
