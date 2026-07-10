using System;
using System.Collections.Generic;
using UnityEngine;
using Melowrite;
using Melowrite.Core;    // MeloEngine (Engine, RunOnAudioThread)
using Melowrite.Audio;   // MeloTrack, MeloBus

/// <summary>
/// The AudioSource of Melowrite. Drag a .melo, get volume / pan / tempo in the Inspector, and call the
/// same methods you'd call on a MeloInstance - the FULL surface is forwarded below. The underlying
/// instance is also at .Instance.
/// </summary>
[AddComponentMenu("Melowrite/Melo Source")]
public class MeloSource : MonoBehaviour
{
    [Tooltip("The .melo project to play.")]
    public MeloFile song;

    [Tooltip("Enter the chunk Index: 0, 1, 2, or the chunk name you'd like to play. Leave blank to play the arrangement")]
    public string startChunk = "";

    [Tooltip("Loop the start chunk (ignored for the arrangement, which inherits the editor setting by default).")]
    public bool loop = true;

    [Tooltip("Start playing on Awake.")]
    public bool playOnAwake = true;

    [Header("Mix (live)")]
    [Range(0f, 1f)] public float volume = 1f;
    [Range(-1f, 1f)] public float pan = 0f;
    [Tooltip("Beats per minute. 0 = the project's authored tempo.")]
    public int tempo = 0;

    /// <summary>
    /// The wrapped instance. Every method below just forwards to this.
    /// </summary>
    public MeloInstance Instance { get; private set; }

    void Awake()
    {
        if (song == null) return;
        Instance = Melo.Load(song);
        if (playOnAwake) Play();
    }

    void Update()
    {
        if (Instance == null) return;
        Instance.SetMasterVolume(volume);
        Instance.SetPan(pan);
        if (tempo > 0) Instance.SetTempo(tempo);
    }

    void OnDestroy() => Instance?.Stop();

    /// <summary>
    /// Play the chosen chunk (or the arrangement). Loads the song if needed.
    /// </summary>
    public void Play()
    {
        if (Instance == null && song != null) Instance = Melo.Load(song);
        if (Instance == null) return;
        if (string.IsNullOrEmpty(startChunk)) Instance.PlayArrangement();
        else if (int.TryParse(startChunk, out int idx)) Instance.PlayChunk(idx, loop);   // all-digits = chunk index
        else Instance.PlayChunk(startChunk, loop);                                        // otherwise a chunk name
    }

    /// <summary>
    /// Load a song from code (the Inspector Song field is optional). Replaces whatever was
    /// loaded, stops the old one, and hands back the instance so you can drive it immediately.
    /// Doesn't auto-play - call Play() or PlayChunk(...) after.
    /// </summary>
    public MeloInstance Load(MeloFile file)
    {
        Instance?.Stop();
        song = file;
        Instance = file != null ? Melo.Load(file) : null;
        return Instance;
    }

    /// <summary>
    /// Same as Load but decodes off the main thread; onLoaded fires (on the main thread) when
    /// it's ready to play, so a big project can't hitch the frame it loads on.
    /// </summary>
    public void LoadAsync(MeloFile file, Action<MeloInstance> onLoaded = null)
    {
        Instance?.Stop();
        song = file;
        Instance = null;
        if (file == null) { onLoaded?.Invoke(null); return; }
        Melo.LoadAsync(file, inst => { Instance = inst; onLoaded?.Invoke(inst); });
    }

    // -- Read-only state --
    public MeloEngine Engine => Instance?.Engine;
    public MeloFile File => Instance != null ? Instance.File : song;
    public bool IsLoaded => Instance != null && Instance.IsLoaded;
    public string ActiveChunk => Instance != null ? Instance.ActiveChunk : "";
    public int CurrentBar => Instance != null ? Instance.CurrentBar : 0;
    public int CurrentBeat => Instance != null ? Instance.CurrentBeat : 0;
    public int Tempo => Instance != null ? Instance.Tempo : 0;   // live BPM (the `tempo` field is your override)
    public int ChunkCount => Instance != null ? Instance.ChunkCount : 0;
    public int TrackCount => Instance != null ? Instance.TrackCount : 0;
    public string[] ChunkNames => Instance != null ? Instance.ChunkNames : Array.Empty<string>();
    public string[] TrackNames => Instance != null ? Instance.TrackNames : Array.Empty<string>();
    public string[] BusNames => Instance != null ? Instance.BusNames : Array.Empty<string>();
    public string[] GetPaletteNames() => Instance != null ? Instance.GetPaletteNames() : Array.Empty<string>();
    public MeloBus Master => Instance != null ? Instance.Master : default;

    // -- Playback --
    public void PlayChunk(string chunk = null, bool looping = true, float fadeOut = 0f) => Instance?.PlayChunk(chunk, looping, fadeOut);
    public void PlayChunk(int index, bool looping = true, float fadeOut = 0f) => Instance?.PlayChunk(index, looping, fadeOut);
    public void PlayChunkCrossfade(string chunk, float duration, bool looping = true) => Instance?.PlayChunkCrossfade(chunk, duration, looping);
    public void PlayChunkCrossfade(int index, float duration, bool looping = true) => Instance?.PlayChunkCrossfade(index, duration, looping);
    public void PlayArrangement(float fadeOut = 0f) => Instance?.PlayArrangement(fadeOut);
    public void PlayArrangementCrossfade(float duration) => Instance?.PlayArrangementCrossfade(duration);
    public void PlayChunkHeadless(string chunk = null) => Instance?.PlayChunkHeadless(chunk);
    public void PlayChunkHeadless(int index) => Instance?.PlayChunkHeadless(index);

    public void Stop() => Instance?.Stop();
    public void Pause() => Instance?.Pause();
    public void Resume() => Instance?.Resume();
    public void FadeOut(float duration = 1f) => Instance?.FadeOut(duration);
    public void CancelSwitch() => Instance?.CancelSwitch();
    public void SeekToBar(int bar) => Instance?.SeekToBar(bar);

    public void SwitchChunk(string chunk, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchChunk(chunk, when);
    public void SwitchChunk(int index, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchChunk(index, when);

    // -- SwitchSong (fade-out) - by path, MeloFile, MeloInstance, or another MeloSource --
    public void SwitchSong(string path, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(path, when, fadeOut);
    public void SwitchSong(string path, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(path, startChunk, when, fadeOut);
    public void SwitchSong(string path, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(path, startChunk, when, fadeOut);
    public void SwitchSong(MeloFile file, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(file, when, fadeOut);
    public void SwitchSong(MeloFile file, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(file, startChunk, when, fadeOut);
    public void SwitchSong(MeloFile file, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(file, startChunk, when, fadeOut);
    public void SwitchSong(MeloInstance other, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other, when, fadeOut);
    public void SwitchSong(MeloInstance other, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other, startChunk, when, fadeOut);
    public void SwitchSong(MeloInstance other, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other, startChunk, when, fadeOut);
    public void SwitchSong(MeloSource other, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other != null ? other.Instance : null, when, fadeOut);
    public void SwitchSong(MeloSource other, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other != null ? other.Instance : null, startChunk, when, fadeOut);
    public void SwitchSong(MeloSource other, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f) => Instance?.SwitchSong(other != null ? other.Instance : null, startChunk, when, fadeOut);

    // -- SwitchSongCrossfade (overlap) - by path, MeloFile, MeloInstance, or another MeloSource --
    public void SwitchSongCrossfade(string path, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(path, duration, when);
    public void SwitchSongCrossfade(string path, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(path, startChunk, duration, when);
    public void SwitchSongCrossfade(string path, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(path, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloFile file, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(file, duration, when);
    public void SwitchSongCrossfade(MeloFile file, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(file, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloFile file, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(file, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloInstance other, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other, duration, when);
    public void SwitchSongCrossfade(MeloInstance other, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloInstance other, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloSource other, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other != null ? other.Instance : null, duration, when);
    public void SwitchSongCrossfade(MeloSource other, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other != null ? other.Instance : null, startChunk, duration, when);
    public void SwitchSongCrossfade(MeloSource other, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar) => Instance?.SwitchSongCrossfade(other != null ? other.Instance : null, startChunk, duration, when);

    // -- Mix --
    /// <summary>
    /// These write the serialized field too (not just the instance): the field is what Update() pushes
    /// every frame and what the Inspector shows, so a code-set value has to land there or it won't stick.
    /// </summary>
    public void SetTempo(int bpm) { tempo = bpm; Instance?.SetTempo(bpm); }
    public void SetMasterVolume(float v) { volume = v; Instance?.SetMasterVolume(v); }
    public void SetPan(float p) { pan = p; Instance?.SetPan(p); }
    public void SetTrackVolume(string track, float v) => Instance?.SetTrackVolume(track, v);
    public void SetTrackVolume(int track, float v) => Instance?.SetTrackVolume(track, v);
    public void SetTrackMuted(string track, bool m) => Instance?.SetTrackMuted(track, m);
    public void SetTrackMuted(int track, bool m) => Instance?.SetTrackMuted(track, m);
    public void SetSendA(string track, float level) => Instance?.SetSendA(track, level);
    public void SetSendA(int track, float level) => Instance?.SetSendA(track, level);
    public void SetSendB(string track, float level) => Instance?.SetSendB(track, level);
    public void SetSendB(int track, float level) => Instance?.SetSendB(track, level);
    public void SetBusVolume(int bus, float v) => Instance?.SetBusVolume(bus, v);
    public void SwapPalette(string palette) => Instance?.SwapPalette(palette);
    public void SetBusEffectParam(int bus, int effect, string param, float value) => Instance?.SetBusEffectParam(bus, effect, param, value);
    public void SetTrackEffectParam(int track, int effect, string param, float value) => Instance?.SetTrackEffectParam(track, effect, param, value);

    // -- Triggers (SFX bank) --
    public void Trigger(string track, string pad, float velocity = 1f, float pan = 0f) => Instance?.Trigger(track, pad, velocity, pan);
    public void Trigger(int trackIndex, string pad, float velocity = 1f, float pan = 0f) => Instance?.Trigger(trackIndex, pad, velocity, pan);
    public void Hold(string track, string pad, float velocity = 1f, float pan = 0f) => Instance?.Hold(track, pad, velocity, pan);
    public void Sustain(string track, string pad, float velocity = 1f, float pan = 0f) => Instance?.Sustain(track, pad, velocity, pan);
    public void Sustain(int trackIndex, string pad, float velocity = 1f, float pan = 0f) => Instance?.Sustain(trackIndex, pad, velocity, pan);
    public void Release(string track, string pad) => Instance?.Release(track, pad);
    public void NoteOn(int trackIndex, int midiNote, float velocity = 1f) => Instance?.NoteOn(trackIndex, midiNote, velocity);
    public void NoteOff(int trackIndex, int midiNote) => Instance?.NoteOff(trackIndex, midiNote);

    // -- Typed handles --
    public MeloTrack Track(string name) => Instance != null ? Instance.Track(name) : default;
    public MeloTrack Track(int index) => Instance != null ? Instance.Track(index) : default;
    public IEnumerable<MeloTrack> AllTracks() => Instance != null ? Instance.AllTracks() : Array.Empty<MeloTrack>();
    public MeloBus Bus(string name) => Instance != null ? Instance.Bus(name) : default;
    public MeloBus Bus(int index) => Instance != null ? Instance.Bus(index) : default;
    public IEnumerable<MeloBus> AllBuses() => Instance != null ? Instance.AllBuses() : Array.Empty<MeloBus>();

    // -- Advanced / lifecycle --
    public void RunOnAudioThread(Action<MeloEngine> command) => Instance?.RunOnAudioThread(command);
    public void Unload() { Instance?.Unload(); Instance = null; }
}
