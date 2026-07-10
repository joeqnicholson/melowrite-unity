using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Melowrite.Core;
using Melowrite.Audio;
using Melowrite.Audio.Effects;
using Melowrite.Audio.Instruments;

// Host-agnostic Melowrite middleware. Works in any C# host (Unity, MonoGame, FNA, raw SDL, console).
// The engine (Melowrite.Core) renders buffers; the HOST owns the audio device and pumps two hooks:
//
//   audio thread : MeloDirector.Instance.FillBuffer(stereoBuffer, frames)
//   main thread  : MeloDirector.Instance.Tick()              // delivers OnNote events to your code
//
// Configure once at startup (the Unity plugin does this for you in MeloUnityHost):
//   MeloDirector.Init(deviceSampleRate);
//   MeloDirector.ResolvePath = myPathResolver;   // optional; default = use the path as-is
//   MeloDirector.LogError = Console.Error.WriteLine;
//
// Then it's the same API everywhere:
//   var music = Melo.Load("music.melo");
//   music.PlayChunk();
//   music.SwitchChunk("Combat");                // on the next bar
//   music.SwitchSong("boss.melo", MeloSwitch.Bar); // whole-song swap, quantized on the audio thread

namespace Melowrite
{
    /// <summary>
    /// When a chunk or song switch takes effect.
    /// </summary>
    public enum MeloSwitch
    {
        Now,     // immediately
        Beat,    // on the next beat
        Bar,     // on the next bar (default - always lands on the music)
        Queue    // when the current chunk finishes its loop
    }

    /// <summary>
    /// Flat front door. Everything routes through the shared MeloDirector.
    /// </summary>
    public static partial class Melo
    {
        /// <summary>
        /// The shared mixer the host pumps. Rarely needed directly.
        /// </summary>
        public static MeloDirector Director => MeloDirector.Instance;

        /// <summary>
        /// Load a project by path. Runs through MeloDirector.ResolvePath (identity by default;
        /// the Unity host points it at the Assets/StreamingAssets resolver). The decoded engine is
        /// held and reused, so loading the same project again (or switching to it) never re-decodes.
        /// Null if missing.
        /// </summary>
        public static MeloInstance Load(string projectPath) => MeloDirector.Instance.Load(projectPath);

        /// <summary>
        /// Load a project OFF the main thread so the decode never hitches a frame. Returns immediately.
        /// The optional `onLoaded` fires later on the main thread (from Tick) with the ready channel -
        /// safe to Play/use - or skip it just to warm the project into memory for a later SwitchSong.
        /// onLoaded(null) on failure. Already-loaded projects deliver on the next Tick with no thread hop.
        /// </summary>
        public static void LoadAsync(string projectPath, Action<MeloInstance> onLoaded = null)
            => MeloDirector.Instance.LoadAsync(projectPath, onLoaded);

        /// <summary>
        /// Final mix volume for all Melowrite audio (0-1).
        /// </summary>
        public static float MasterVolume
        {
            get => MeloDirector.Instance.MasterVolume;
            set => MeloDirector.Instance.MasterVolume = Clamp01(value);
        }

        /// <summary>
        /// Stop sequenced playback on every channel (banks stay live for triggers).
        /// </summary>
        public static void StopAll() => MeloDirector.Instance.StopAll();

        /// <summary>
        /// Dispose every loaded project and clear the shared soundfont/clip caches. Reclaims all
        /// Melowrite audio memory; call it on level/scene exit.
        /// </summary>
        public static void UnloadAll() => MeloDirector.Instance.UnloadAll();

        /// <summary>
        /// Every note hit across all channels: (instance, trackIndex, MIDI pitch, velocity 0-127).
        /// Raised on the main thread from Tick().
        /// </summary>
        public static event Action<MeloInstance, int, int, int> OnNote
        {
            add    => MeloDirector.Instance.OnNote += value;
            remove => MeloDirector.Instance.OnNote -= value;
        }

        // -- Raw audio-file SFX (wav / mp3 / ogg) --
        /// <summary>
        /// Fire a sound once. Polyphonic; the clip is decoded once and cached. Returns a voice id
        /// you can Stop/adjust, or ignore for fire-and-forget. volume 0-1, pan -1..+1,
        /// pitch (1 = normal, 2 = octave up), loop, effects = route through the SFX FX bus.
        /// </summary>
        public static int PlayOneShot(string file, float volume = 1f, float pan = 0f, float pitch = 1f,
                                      bool loop = false, bool effects = true)
            => MeloDirector.Instance.PlaySfx(file, volume, pan, pitch, loop, effects);

        public static void Stop(int voice)            => MeloDirector.Instance.Sfx.Stop(voice);
        public static void StopSfx()                  => MeloDirector.Instance.Sfx.StopAll();
        public static void SetVolume(int voice, float v) => MeloDirector.Instance.Sfx.SetVoiceVolume(voice, v);
        public static void SetPan(int voice, float p)    => MeloDirector.Instance.Sfx.SetVoicePan(voice, p);
        public static void SetPitch(int voice, float s)  => MeloDirector.Instance.Sfx.SetVoiceSpeed(voice, s);

        /// <summary>
        /// Master volume for the raw-SFX bus only (0-1). Melo.MasterVolume scales everything.
        /// </summary>
        public static float SfxVolume
        {
            get => MeloDirector.Instance.Sfx.MasterVolume;
            set => MeloDirector.Instance.Sfx.MasterVolume = Clamp01(value);
        }

        // -- Global SFX effects bus --
        public static void SetReverb(float mix = 0.3f, float roomSize = 0.7f, float damping = 0.5f)
            => MeloDirector.Instance.SetReverb(mix, roomSize, damping);
        public static void SetDelay(float mix = 0.3f, float intervalMs = 350f, float decayMs = 1500f)
            => MeloDirector.Instance.SetDelay(mix, intervalMs, decayMs);
        public static void ClearEffects() => MeloDirector.Instance.ClearEffects();

        /// <summary>
        /// Copy an effect chain off one of a project's mixer buses onto the SFX bus. busName e.g. "A".
        /// </summary>
        public static void CopyEffectsFrom(MeloInstance instance, string busName)
            => MeloDirector.Instance.CopyEffectsFrom(instance, busName);

        internal static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>
    /// A persistent playback CHANNEL you hold and control. It plays one project at a time; SwitchSong
    /// re-points this same channel to a different project (quantized), so the handle stays valid for
    /// the life of the game object. Everything you can do to a running project lives here.
    /// Sequencer/mix calls are deferred to the audio thread (race-free).
    /// </summary>
    public sealed partial class MeloInstance
    {
        private readonly MeloDirector _rt;
        internal MeloEngine _engine;   // the CURRENT engine; changes when SwitchSong lands. Null once Unload()ed
        internal string _path;         // the CURRENT project's resolved path; changes on SwitchSong

        /// <summary>
        /// The raw engine, for anything the wrappers don't surface. Null after Unload().
        /// </summary>
        public MeloEngine Engine => _engine;

        /// <summary>
        /// Names reflect the project playing RIGHT NOW (they change after a SwitchSong lands).
        /// </summary>
        public string[] ChunkNames => _engine?.GetChunkNames() ?? Array.Empty<string>();
        public string[] TrackNames => _engine?.GetTrackNames() ?? Array.Empty<string>();
        public string[] BusNames   => _engine?.GetBusNames()   ?? Array.Empty<string>();
        public bool IsLoaded => _engine != null;

#if !UNITY_5_3_OR_NEWER
        // The project playing right now, as its path. In Unity this getter returns the MeloFile asset
        // instead (see Melo.Unity.cs), so `instance.File == myMeloFile` works.
        public string File => _path;
#endif

        internal MeloInstance(MeloDirector rt, MeloEngine engine, string path)
        {
            _rt = rt; _engine = engine; _path = path;
        }

        // -- Live state (read-only) --
        public string ActiveChunk => _engine?.ActiveChunkName ?? "";
        public int CurrentBar => _engine?.CurrentBar ?? 0;
        public int CurrentBeat => _engine?.CurrentBeat ?? 0;
        public int Tempo => _engine?.Tempo ?? 0;
        public int ChunkCount => _engine?.ChunkCount ?? 0;
        public int TrackCount => _engine?.TrackCount ?? 0;
        public string[] GetPaletteNames() => _engine?.GetPaletteNames() ?? Array.Empty<string>();

        // -- Sequenced playback (music) --

        /// <summary>
        /// Start playing a chunk (section) on this channel. Loops by default; looping:false plays it once.
        /// chunk = null plays the first chunk. fadeOut (seconds) fades the current playback out first, then
        /// starts the new chunk at full - no overlap. For an overlapping crossfade use PlayChunkCrossfade.
        /// </summary>
        public void PlayChunk(string chunk = null, bool looping = true, float fadeOut = 0f)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            PlayChunkResolved(idx, looping, fadeOut);
        }

        /// <summary>
        /// Same, by chunk index.
        /// </summary>
        public void PlayChunk(int index, bool looping = true, float fadeOut = 0f)
        {
            if (_engine == null) return;
            PlayChunkResolved(index, looping, fadeOut);
        }

        void PlayChunkResolved(int idx, bool looping, float fadeOut)
        {
            if (idx < 0) return;
            if (fadeOut > 0f) _rt.FadeChunk(this, idx, looping, fadeOut);   // fade current out, then swap on the same engine
            else { _rt.Activate(this); _rt.Defer(() => PlayChunkAt(idx, looping)); }
        }

        /// <summary>
        /// Crossfade to another section of THIS song: a fresh instance starts on `chunk` at full while
        /// the current playback fades out over `duration` seconds (they overlap).
        /// </summary>
        public void PlayChunkCrossfade(string chunk, float duration, bool looping = true)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            if (idx >= 0) _rt.CrossfadeChunk(this, _path, idx, looping, duration);
        }

        /// <summary>
        /// Same, by chunk index.
        /// </summary>
        public void PlayChunkCrossfade(int index, float duration, bool looping = true)
        {
            if (_engine != null) _rt.CrossfadeChunk(this, _path, index, looping, duration);
        }

        void PlayChunkAt(int idx, bool looping)
        {
            if (_engine == null || idx < 0 || idx >= _engine.ChunkCount) return;
            if (looping) _engine.PlayChunk(idx);
            else _engine.PlayChunkOnce(idx);
        }

        /// <summary>
        /// Play the whole arrangement (timeline) instead of a single chunk. fadeOut (seconds) fades the
        /// current playback out first, then starts the arrangement at full - no overlap. For an
        /// overlapping crossfade use PlayArrangementCrossfade.
        /// </summary>
        public void PlayArrangement(float fadeOut = 0f)
        {
            if (_engine == null) return;
            if (fadeOut > 0f) _rt.FadeChunk(this, -1, true, fadeOut);   // -1 = arrangement
            else { _rt.Activate(this); _rt.Defer(() => _engine?.PlayArrangement()); }
        }

        /// <summary>
        /// Crossfade to the arrangement: a fresh instance starts the timeline at full while the current
        /// playback fades out over `duration` seconds (they overlap).
        /// </summary>
        public void PlayArrangementCrossfade(float duration)
        {
            if (_engine != null) _rt.CrossfadeChunk(this, _path, -1, true, duration);   // -1 = arrangement
        }

        /// <summary>
        /// Fire a chunk on its OWN throwaway playhead - "headless" (no channel handle). It overlaps
        /// whatever this channel is playing and auto-cleans when it finishes (great for stings).
        /// Heavier than Trigger() (a separate engine per fire). chunk = null fires the first chunk.
        /// </summary>
        public void PlayChunkHeadless(string chunk = null)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            if (idx >= 0) _rt.SpawnOneShot(_path, idx);
        }

        /// <summary>
        /// Same, by chunk index.
        /// </summary>
        public void PlayChunkHeadless(int index)
        {
            if (_engine != null) _rt.SpawnOneShot(_path, index);
        }

        /// <summary>
        /// Stop sequenced playback (tails ring out). The channel stays live so it can be re-Played or
        /// Triggered; call Unload() to free it.
        /// </summary>
        public void Stop()   => _rt.Defer(() => _engine?.Stop());
        public void Pause()  => _rt.Defer(() => _engine?.Pause());
        public void Resume() => _rt.Defer(() => _engine?.Resume());

        /// <summary>
        /// Fade this song out to silence over `duration` seconds, then stop it. duration &lt;= 0 stops now.
        /// </summary>
        public void FadeOut(float duration = 1f) => _rt.FadeOutInstance(this, duration);

        /// <summary>
        /// Switch to another chunk (section) of the CURRENT project, quantized by `when` - defaults to
        /// the next bar so it always lands on the music.
        /// </summary>
        public void SwitchChunk(string chunk, MeloSwitch when = MeloSwitch.Bar)
            => _rt.Defer(() => ApplyChunkSwitch(chunk, -1, when));
        public void SwitchChunk(int index, MeloSwitch when = MeloSwitch.Bar)
            => _rt.Defer(() => ApplyChunkSwitch(null, index, when));

        void ApplyChunkSwitch(string chunk, int index, MeloSwitch when)
        {
            if (_engine == null) return;
            bool byName = chunk != null;
            switch (when)
            {
                case MeloSwitch.Now:   if (byName) _engine.PlayChunk(chunk);            else _engine.PlayChunk(index); break;
                case MeloSwitch.Beat:  if (byName) _engine.PlayChunkOnNextBeat(chunk);  else _engine.PlayChunkOnNextBeat(index); break;
                case MeloSwitch.Bar:   if (byName) _engine.PlayChunkOnNextBar(chunk);   else _engine.PlayChunkOnNextBar(index); break;
                case MeloSwitch.Queue: if (byName) _engine.QueueChunk(chunk);           else _engine.QueueChunk(index); break;
            }
        }

        /// <summary>
        /// Re-point THIS channel to a DIFFERENT project ("switch song"), quantized by `when`. Detection
        /// happens on the AUDIO thread (buffer-accurate): when the current project reaches the boundary,
        /// this channel stops it and starts the new one. The decoded engine is reused if the project was
        /// already loaded, so the swap never re-decodes. Pass a start chunk (name or index) to pick which
        /// section the new song begins on - otherwise it plays the whole arrangement. fadeOut (seconds)
        /// fades the current song out, THEN the new one starts at full (no overlap). For an overlapping
        /// crossfade use SwitchSongCrossfade. Now swaps immediately.
        /// </summary>
        public void SwitchSong(string projectPath, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, -1, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// ...beginning on a named chunk instead of the arrangement.
        /// </summary>
        public void SwitchSong(string projectPath, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to == null) return;
            int idx = string.IsNullOrEmpty(startChunk) ? -1 : to.GetChunkIndex(startChunk);
            _rt.ScheduleSwitch(this, to, resolved, idx, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// ...beginning on a chunk index.
        /// </summary>
        public void SwitchSong(string projectPath, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, startChunk, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// Re-point this channel to whatever `other` has loaded (reuses other's already-decoded engine,
        /// so it's free - handy from a LoadAsync callback: Melo.LoadAsync(f, song =&gt; player.SwitchSong(song))).
        /// No start chunk = play the new song's arrangement; pass one (name or index) to begin on a section.
        /// </summary>
        public void SwitchSong(MeloInstance other, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
            => SwitchSong(other, -1, when, fadeOut);

        public void SwitchSong(MeloInstance other, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            if (other?._engine == null) return;
            int idx = string.IsNullOrEmpty(startChunk) ? -1 : other._engine.GetChunkIndex(startChunk);
            SwitchSong(other, idx, when, fadeOut);
        }

        public void SwitchSong(MeloInstance other, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            if (other?._engine != null) _rt.ScheduleSwitch(this, other._engine, other._path, startChunk, fadeOut, false, false, true, when);
        }

        // -- Crossfade variants: the old and new OVERLAP as one fades out over `duration` seconds. --
        // No start chunk = the new song's arrangement; pass one (name or index) to begin on a section.
        public void SwitchSongCrossfade(string projectPath, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (!_rt.TryBeginTransition(this, duration)) return;
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, -1, duration, true, false, true, when);
        }

        public void SwitchSongCrossfade(string projectPath, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (!_rt.TryBeginTransition(this, duration)) return;
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to == null) return;
            int idx = string.IsNullOrEmpty(startChunk) ? -1 : to.GetChunkIndex(startChunk);
            _rt.ScheduleSwitch(this, to, resolved, idx, duration, true, false, true, when);
        }

        public void SwitchSongCrossfade(string projectPath, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (!_rt.TryBeginTransition(this, duration)) return;
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, startChunk, duration, true, false, true, when);
        }

        public void SwitchSongCrossfade(MeloInstance other, float duration, MeloSwitch when = MeloSwitch.Bar)
            => SwitchSongCrossfade(other, -1, duration, when);

        public void SwitchSongCrossfade(MeloInstance other, string startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (other?._engine == null) return;
            int idx = string.IsNullOrEmpty(startChunk) ? -1 : other._engine.GetChunkIndex(startChunk);
            SwitchSongCrossfade(other, idx, duration, when);
        }

        public void SwitchSongCrossfade(MeloInstance other, int startChunk, float duration, MeloSwitch when = MeloSwitch.Bar)
        {
            if (other?._engine == null || !_rt.TryBeginTransition(this, duration)) return;
            _rt.ScheduleSwitch(this, other._engine, other._path, startChunk, duration, true, false, true, when);
        }

        /// <summary>
        /// Cancel a pending SwitchChunk (Beat/Bar/Queue) that hasn't fired yet.
        /// </summary>
        public void CancelSwitch() => _rt.Defer(() => { _engine?.CancelSchedule(); _engine?.CancelQueue(); });

        public void SeekToBar(int bar) => _rt.Defer(() => _engine?.SeekToBar(bar));

        // -- Mix --
        public void SetTempo(int bpm)                     => _rt.Defer(() => _engine?.SetTempo(bpm));
        public void SetMasterVolume(float volume)         => _rt.Defer(() => _engine?.SetMasterVolume(volume));
        /// <summary>
        /// Pan this song's whole output: -1 = left, 0 = center, +1 = right. Applied at the mix.
        /// </summary>
        public void SetPan(float pan)                     => _rt.SetChannelPan(this, pan);
        public void SetTrackMuted(string track, bool m)   => _rt.Defer(() => _engine?.SetTrackMuted(track, m));
        public void SetTrackMuted(int track, bool m)      => _rt.Defer(() => _engine?.SetTrackMuted(track, m));
        public void SetTrackVolume(string track, float v) => _rt.Defer(() => _engine?.SetTrackVolume(track, v));
        public void SetTrackVolume(int track, float v)    => _rt.Defer(() => _engine?.SetTrackVolume(track, v));
        /// <summary>
        /// send to bus A (usually reverb) / bus B (usually delay), 0-1
        /// </summary>
        public void SetSendA(string track, float level)   => _rt.Defer(() => _engine?.SetTrackSend(track, "A", level));
        public void SetSendA(int track, float level)      => _rt.Defer(() => _engine?.SetTrackSendA(track, level));
        public void SetSendB(string track, float level)   => _rt.Defer(() => _engine?.SetTrackSend(track, "B", level));
        public void SetSendB(int track, float level)      => _rt.Defer(() => _engine?.SetTrackSendB(track, level));
        public void SetBusVolume(int bus, float volume)   => _rt.Defer(() => _engine?.SetBusVolume(bus, volume));
        public void SwapPalette(string palette)           => _rt.Defer(() => _engine?.SwapPalette(palette));

        /// <summary>
        /// set a parameter on a bus effect, e.g. SetBusEffectParam(1, 0, "Mix", 0.5f)
        /// </summary>
        public void SetBusEffectParam(int bus, int effect, string param, float value)
            => _rt.Defer(() => _engine?.SetBusEffectParam(bus, effect, param, value));
        /// <summary>
        /// set a parameter on a track effect
        /// </summary>
        public void SetTrackEffectParam(int track, int effect, string param, float value)
            => _rt.Defer(() => _engine?.SetTrackEffectParam(track, effect, param, value));

        // -- Live triggering (SFX banks) --
        // Fire this project's instruments on demand: polyphonic, mixed live, works with the transport
        // stopped, no new engine. Author the .melo as a bank (tracks = categories, pads = variations).

        /// <summary>
        /// Fire a one-shot of a named multisampler pad. Overlaps itself.
        /// pan = per-trigger position (-1 left .. +1 right), added to the pad's own
        /// pan; each overlapping trigger keeps its own position.
        /// </summary>
        public void Trigger(string track, string pad, float velocity = 1f, float pan = 0f)
            => Trigger(TrackIndex(track), pad, velocity, pan);

        /// <summary>
        /// Fire a one-shot of a named pad on a track index.
        /// </summary>
        public void Trigger(int trackIndex, string pad, float velocity = 1f, float pan = 0f)
        {
            if (_engine == null) return;
            _rt.Activate(this);                        // make sure this bank is in the mix
            _engine.TriggerPad(trackIndex, pad, velocity, pan);   // engine queues it internally (race-free)
        }

        /// <summary>
        /// Start a held/sustained pad (loops/drones). Release with Release().
        /// </summary>
        public void Hold(string track, string pad, float velocity = 1f, float pan = 0f)
        {
            if (_engine == null) return;
            _rt.Activate(this);
            _engine.HoldPad(TrackIndex(track), pad, velocity, pan);
        }

        /// <summary>
        /// Frame-gated hold: call every frame (e.g. from Update while a button is
        /// down) to keep the pad sounding. Stop calling and it auto-releases with
        /// the pad's normal release a short grace (~0.12s) later - no Release()
        /// bookkeeping. Idempotent: re-calls while held refresh the hold, they
        /// never retrigger the sample.
        /// </summary>
        public void Sustain(string track, string pad, float velocity = 1f, float pan = 0f)
            => Sustain(TrackIndex(track), pad, velocity, pan);

        public void Sustain(int trackIndex, string pad, float velocity = 1f, float pan = 0f)
        {
            if (_engine == null) return;
            _rt.Activate(this);
            _engine.SustainPad(trackIndex, pad, velocity, pan);
        }

        /// <summary>
        /// Release a pad started with Hold().
        /// </summary>
        public void Release(string track, string pad)
            => _engine?.ReleasePad(TrackIndex(track), pad);

        /// <summary>
        /// Melodic note on (MIDI pitch, velocity 0-1).
        /// </summary>
        public void NoteOn(int trackIndex, int midiNote, float velocity = 1f)
        {
            if (_engine == null) return;
            _rt.Activate(this);
            _engine.NoteOn(trackIndex, midiNote, velocity);
        }

        /// <summary>
        /// Melodic note off.
        /// </summary>
        public void NoteOff(int trackIndex, int midiNote) => _engine?.NoteOff(trackIndex, midiNote);

        private int TrackIndex(string name) => _engine == null ? -1 : _engine.GetTrackIndex(name);

        // -- Typed handles --
        // These expose live project objects (Volume, Pan, Sends, effects). For mutations during
        // playback, prefer RunOnAudioThread or the queued Set* methods above.

        /// <summary>
        /// track by name; check IsValid before use
        /// </summary>
        public MeloTrack Track(string name)
        {
            if (_engine == null) return default;
            int idx = _engine.GetTrackIndex(name);
            return idx < 0 ? default : new MeloTrack(_engine.Project.Tracks[idx], idx);
        }

        /// <summary>
        /// track by index; check IsValid before use
        /// </summary>
        public MeloTrack Track(int index)
        {
            if (_engine == null || index < 0 || index >= _engine.Project.Tracks.Count) return default;
            return new MeloTrack(_engine.Project.Tracks[index], index);
        }

        public IEnumerable<MeloTrack> AllTracks()
        {
            if (_engine == null) yield break;
            for (int i = 0; i < _engine.Project.Tracks.Count; i++)
                yield return new MeloTrack(_engine.Project.Tracks[i], i);
        }

        /// <summary>
        /// bus by name; check IsValid before use
        /// </summary>
        public MeloBus Bus(string name)
        {
            if (_engine == null) return default;
            for (int i = 0; i < _engine.Project.Buses.Count; i++)
                if (_engine.Project.Buses[i].Name == name)
                    return new MeloBus(_engine.Project.Buses[i], i);
            return default;
        }

        /// <summary>
        /// bus by index; bus 0 is master
        /// </summary>
        public MeloBus Bus(int index)
        {
            if (_engine == null || index < 0 || index >= _engine.Project.Buses.Count) return default;
            return new MeloBus(_engine.Project.Buses[index], index);
        }

        /// <summary>
        /// the master bus (final output stage)
        /// </summary>
        public MeloBus Master => _engine == null
            ? default
            : new MeloBus(_engine.Project.MasterBus, _engine.Project.Buses.IndexOf(_engine.Project.MasterBus));

        public IEnumerable<MeloBus> AllBuses()
        {
            if (_engine == null) yield break;
            for (int i = 0; i < _engine.Project.Buses.Count; i++)
                yield return new MeloBus(_engine.Project.Buses[i], i);
        }

        // -- Advanced --
        /// <summary>
        /// Run an engine change on the audio thread, for anything the wrappers don't cover.
        /// </summary>
        public void RunOnAudioThread(Action<MeloEngine> command)
            => _rt.Defer(() => { if (_engine != null) command(_engine); });

        // -- Lifecycle --

        /// <summary>
        /// Free this channel's current project: stop it and dispose its engine (releases its samplers +
        /// clips), and drop it from the reuse pool. Shared soundfonts are dropped by Melo.UnloadAll().
        /// </summary>
        public void Unload()
        {
            var e = _engine;
            var p = _path;
            _engine = null;
            _rt.Deactivate(this);
            _rt.Unpool(p, e);
        }
    }

    /// <summary>
    /// Host-agnostic mixer + lifecycle. Sums every active engine + the SFX bus into one stereo
    /// output. The HOST owns the device and pumps FillBuffer (audio thread) and Tick (main thread).
    /// No UnityEngine, no framework types - just Melowrite.Core.
    /// </summary>
    public sealed class MeloDirector
    {
        // -- Host configuration (set before first use) --
        /// <summary>
        /// Turn a caller path into an on-disk path. Default = use as-is. Unity points this at its
        /// Assets/StreamingAssets resolver; MonoGame/SDL pass real paths so identity is fine.
        /// </summary>
        public static Func<string, string> ResolvePath = p => p;
        public static Action<string> LogError = System.Console.Error.WriteLine;
        public static Action<string> LogWarning = System.Console.WriteLine;

        private static int _initSampleRate = 44100;
        private static MeloDirector _instance;

        /// <summary>
        /// Set the output sample rate BEFORE first use (must match the host device). Defaults 44100.
        /// </summary>
        public static void Init(int sampleRate) => _initSampleRate = sampleRate > 0 ? sampleRate : 44100;
        public static MeloDirector Instance => _instance ?? (_instance = new MeloDirector(_initSampleRate));

        public float MasterVolume = 1f;
        public event Action<MeloInstance, int, int, int> OnNote;

        private readonly int _sampleRate;
        private readonly ConcurrentQueue<Action> _commands = new ConcurrentQueue<Action>();
        private readonly List<Entry> _active = new List<Entry>();               // audio-thread owned
        private readonly Dictionary<string, MeloEngine> _pool = new Dictionary<string, MeloEngine>(); // main-thread owned; decode-once reuse
        private readonly ConcurrentQueue<NoteHit> _notes = new ConcurrentQueue<NoteHit>();
        private readonly ConcurrentDictionary<MeloEngine, MeloInstance> _owner = new ConcurrentDictionary<MeloEngine, MeloInstance>();
        private float[] _scratch = Array.Empty<float>();
        private float[] _accum = Array.Empty<float>();

        // Raw audio-file SFX player + its global effect bus.
        private readonly MeloAudio _sfx;
        private ReverbEffect _reverb;   // lazily added to _sfx when SetReverb is first called
        private DelayEffect _delay;
        public MeloAudio Sfx => _sfx;
        public int SampleRate => _sampleRate;

        private sealed class Entry
        {
            public MeloEngine Engine;
            public MeloInstance Instance;   // null for one-shots and detached fading-out engines
            public bool OneShot;
            public volatile bool Finished;
            public int TailLeft;
            // Fade envelope for crossfades: this engine's contribution is scaled by Gain, which ramps
            // by GainStep per frame (<0 = fading out). When a fade-out hits 0 the entry is reaped -
            // disposed if Throwaway (a fresh crossfade instance), else Stopped (a pooled engine).
            public float Gain = 1f;
            public float GainStep = 0f;
            public float Pan = 0f;          // -1 = hard left, 0 = center, +1 = hard right (applied at the mix)
            public bool Throwaway;
            public Action OnFadeComplete;   // runs (audio thread) when a fade-out hits 0, instead of reaping
        }
        private struct NoteHit { public MeloEngine Engine; public int Track, Pitch, Vel; }

        public MeloDirector(int sampleRate)
        {
            _sampleRate = sampleRate > 0 ? sampleRate : 44100;
            _sfx = new MeloAudio(_sampleRate);
        }

        // -- Raw audio-file SFX --
        public int PlaySfx(string file, float volume, float pan, float pitch, bool loop, bool effects)
        {
            string path = ResolvePath(file);
            if (path == null) { LogError($"[Melo] Sound not found: {file}"); return -1; }
            if (!_sfx.LoadClip(path)) { LogError($"[Melo] Couldn't decode: {file}"); return -1; }
            return _sfx.Play(path, volume, pan, pitch, loop, effects);
        }

        public void SetReverb(float mix, float roomSize, float damping)
        {
            if (_reverb == null) { _reverb = new ReverbEffect(); _sfx.AddEffect(_reverb); }
            _reverb.Mix = mix; _reverb.RoomSize = roomSize; _reverb.Damping = damping;
        }

        public void SetDelay(float mix, float intervalMs, float decayMs)
        {
            if (_delay == null) { _delay = new DelayEffect(); _sfx.AddEffect(_delay); }
            _delay.Mix = mix; _delay.Interval = intervalMs; _delay.DecayTime = decayMs;
        }

        public void ClearEffects()
        {
            _sfx.ClearEffects();
            _reverb = null; _delay = null;
        }

        public void CopyEffectsFrom(MeloInstance instance, string busName)
        {
            var engine = instance?.Engine;
            if (engine == null) { LogWarning("[Melo] CopyEffectsFrom: project not loaded"); return; }
            var project = engine.Project;

            BusTrack bus = null;
            foreach (var b in project.Buses)
                if (string.Equals(b.Name, busName, StringComparison.OrdinalIgnoreCase)) { bus = b; break; }
            if (bus == null) { LogWarning($"[Melo] CopyEffectsFrom: bus '{busName}' not found"); return; }

            // Deep-clone each effect (params only, fresh state) via the DTO round-trip.
            var clones = new List<IEffect>();
            foreach (var fx in bus.Effects)
            {
                try
                {
                    var clone = ProjectSerializer.EffectFromDto(ProjectSerializer.EffectToDto(fx), project.ImpulsesFolder);
                    if (clone != null) clones.Add(clone);
                }
                catch (Exception ex) { LogWarning($"[Melo] couldn't clone effect '{fx?.Name}': {ex.Message}"); }
            }
            _sfx.SetEffectChain(clones);
            _reverb = null; _delay = null;   // the manual setters no longer own the chain
        }

        // Get-or-decode the engine for a resolved path. Held in the pool for reuse; each project is
        // decoded exactly once (until UnloadAll), so a repeat Load or SwitchSong never re-decodes.
        internal MeloEngine GetOrLoadEngine(string resolvedPath)
        {
            if (_pool.TryGetValue(resolvedPath, out var cached)) return cached;
            var engine = MeloEngine.Load(resolvedPath, sampleRate: _sampleRate);
            _pool[resolvedPath] = engine;
            var eng = engine;
            eng.OnNote += (t, p, v) => _notes.Enqueue(new NoteHit { Engine = eng, Track = t, Pitch = p, Vel = v });
            return engine;
        }

        // Resolve a caller path and get-or-decode its engine. Null (and logged) on failure.
        internal MeloEngine ResolveAndLoad(string rel, out string resolved)
        {
            resolved = ResolvePath(rel);
            if (resolved == null) { LogError($"[Melo] Song not found: {rel}"); return null; }
            try { return GetOrLoadEngine(resolved); }
            catch (Exception ex) { LogError($"[Melo] Failed to load {rel}: {ex.Message}"); return null; }
        }

        public MeloInstance Load(string rel)
        {
            var engine = ResolveAndLoad(rel, out var path);
            return engine == null ? null : new MeloInstance(this, engine, path);
        }

        // -- Async loading (decode off the main thread) --
        private readonly ConcurrentQueue<AsyncLoad> _asyncResults = new ConcurrentQueue<AsyncLoad>();
        private struct AsyncLoad
        {
            public string Path;                    // resolved; null = resolve failed
            public MeloEngine Engine;              // null = decode failed
            public string Error;
            public Action<MeloInstance> OnLoaded;  // null = fire-and-forget warm
        }

        // Crossfades decode off the main thread, and only one runs per channel at a time: a request that
        // lands while a crossfade is still fading in is ignored outright (a crossfade is atomic), so the
        // button can be hammered without freezing the game or stacking a pile of overlapping engines.
        /// <summary>
        /// Bumped every publish - log it in your Console to confirm which build actually loaded.
        /// </summary>
        public const string Build = "2026.07.07a";
        private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly ConcurrentDictionary<MeloInstance, long> _xfadeBusyUntil = new ConcurrentDictionary<MeloInstance, long>();
        private readonly ConcurrentQueue<XfadeLoad> _xfadeResults = new ConcurrentQueue<XfadeLoad>();
        private struct XfadeLoad
        {
            public MeloInstance Channel;
            public MeloEngine Engine;
            public string Path;
            public int ChunkIndex;
            public bool Looping;
            public float FadeOut;
        }

        public void LoadAsync(string rel, Action<MeloInstance> onLoaded = null)
        {
            string path = ResolvePath(rel);
            if (path == null)
            {
                LogError($"[Melo] Song not found: {rel}");
                _asyncResults.Enqueue(new AsyncLoad { Path = null, OnLoaded = onLoaded });
                return;
            }
            // Already decoded: deliver on the next Tick, no thread hop.
            if (_pool.TryGetValue(path, out var cached))
            {
                _asyncResults.Enqueue(new AsyncLoad { Path = path, Engine = cached, OnLoaded = onLoaded });
                return;
            }
            // Decode on a threadpool thread; the shared sample/soundfont caches are thread-safe. The
            // pool insert + callback finish on the main thread in Tick, so _pool stays single-threaded.
            System.Threading.Tasks.Task.Run(() =>
            {
                MeloEngine engine = null; string err = null;
                try { engine = MeloEngine.Load(path, sampleRate: _sampleRate); }
                catch (Exception ex) { err = ex.Message; }
                _asyncResults.Enqueue(new AsyncLoad { Path = path, Engine = engine, Error = err, OnLoaded = onLoaded });
            });
        }

        // Main thread (from Tick). Pool the freshly-decoded engine (or reuse if another load beat us
        // to it), then hand the ready channel to the callback (if any was given).
        private void CompleteAsyncLoad(AsyncLoad r)
        {
            if (r.Path == null) { r.OnLoaded?.Invoke(null); return; }
            if (r.Engine == null)
            {
                LogError($"[Melo] async load failed: {r.Error}");
                r.OnLoaded?.Invoke(null);
                return;
            }

            MeloEngine engine;
            if (_pool.TryGetValue(r.Path, out var existing))
            {
                engine = existing;
                if (!ReferenceEquals(existing, r.Engine)) r.Engine.Dispose();   // was cached, or lost a decode race
            }
            else
            {
                engine = r.Engine;
                _pool[r.Path] = engine;
                var eng = engine;
                eng.OnNote += (t, p, v) => _notes.Enqueue(new NoteHit { Engine = eng, Track = t, Pitch = p, Vel = v });
            }

            if (r.OnLoaded != null) r.OnLoaded(new MeloInstance(this, engine, r.Path));
        }

        internal void Defer(Action a) => _commands.Enqueue(a);

        internal void Activate(MeloInstance instance) => _commands.Enqueue(() =>
        {
            var eng = instance._engine;
            if (eng == null) return;
            _owner[eng] = instance;
            foreach (var e in _active) if (e.Engine == eng) return;   // already summing
            _active.Add(new Entry { Engine = eng, Instance = instance });
        });

        internal void Deactivate(MeloInstance instance) => _commands.Enqueue(() =>
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (!_active[i].OneShot && _active[i].Instance == instance) _active.RemoveAt(i);
        });

        // Free a pooled engine (from MeloInstance.Unload). Drops it from the reuse pool and disposes
        // it on the audio thread after pulling it out of the mix.
        internal void Unpool(string resolvedPath, MeloEngine engine)
        {
            if (resolvedPath != null && _pool.TryGetValue(resolvedPath, out var e) && e == engine)
                _pool.Remove(resolvedPath);
            if (engine != null) _owner.TryRemove(engine, out _);
            _commands.Enqueue(() =>
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                    if (_active[i].Engine == engine) _active.RemoveAt(i);
                engine?.Dispose();
            });
        }

        // Fire a whole chunk once on its own throwaway engine (overlapping sting). Auto-reaps when
        // it finishes. Loading happens here on the calling thread (off the audio thread).
        internal void SpawnOneShot(string path, int chunkIndex)
        {
            MeloEngine engine;
            try { engine = MeloEngine.Load(path, sampleRate: _sampleRate); }
            catch (Exception ex) { LogError($"[Melo] headless play failed ({path}): {ex.Message}"); return; }

            int idx = chunkIndex;
            if (idx < 0 || idx >= engine.ChunkCount) idx = 0;
            var entry = new Entry { Engine = engine, Instance = null, OneShot = true };
            engine.OnFinished += () => entry.Finished = true;
            engine.PlayChunkOnce(idx);
            _commands.Enqueue(() => _active.Add(entry));
        }

        // One transition (crossfade or fade) at a time per channel. Returns false without changing
        // anything while one is still running; otherwise reserves the channel for `seconds` (the fade
        // length) and returns true. EVERY crossfade/fade entry point calls this first, so hammering the
        // button lands one clean transition and drops every press until it finishes.
        internal bool TryBeginTransition(MeloInstance channel, float seconds)
        {
            if (channel == null) return false;
            long now = _clock.ElapsedMilliseconds;
            if (_xfadeBusyUntil.TryGetValue(channel, out var until) && now < until) return false;
            _xfadeBusyUntil[channel] = now + (long)(seconds * 1000f) + 250;   // +250 ms decode/latency headroom
            return true;
        }

        // PlayChunk with a fade = crossfade to a FRESH throwaway instance of this same project at
        // `chunkIndex` (the pool holds one engine per song, so we spin up a second to overlap) while
        // the channel's current engine fades out. Reuses the switch path. Decode is off the audio thread.
        internal void CrossfadeChunk(MeloInstance channel, string path, int chunkIndex, bool looping, float fadeOut)
        {
            if (!TryBeginTransition(channel, fadeOut)) return;
            // Decode off the main thread - a synchronous full-project Load per press freezes the game.
            System.Threading.Tasks.Task.Run(() =>
            {
                MeloEngine fresh = null;
                try { fresh = MeloEngine.Load(path, sampleRate: _sampleRate); }
                catch (Exception ex) { LogError($"[Melo] crossfade failed ({path}): {ex.Message}"); }
                _xfadeResults.Enqueue(new XfadeLoad
                {
                    Channel = channel, Engine = fresh, Path = path,
                    ChunkIndex = chunkIndex, Looping = looping, FadeOut = fadeOut,
                });
            });
        }

        // Main thread (from Tick). Decode done: re-anchor the busy window to the actual fade start, then
        // kick the crossfade. A failed decode frees the channel so the next press can retry immediately.
        private void CompleteCrossfade(XfadeLoad r)
        {
            if (r.Engine == null) { _xfadeBusyUntil[r.Channel] = 0; return; }
            _xfadeBusyUntil[r.Channel] = _clock.ElapsedMilliseconds + (long)(r.FadeOut * 1000f);
            var eng = r.Engine;
            eng.OnNote += (t, p, v) => _notes.Enqueue(new NoteHit { Engine = eng, Track = t, Pitch = p, Vel = v });
            ScheduleSwitch(r.Channel, r.Engine, r.Path, r.ChunkIndex, r.FadeOut, true, true, r.Looping, MeloSwitch.Now);
        }

        // PlayChunk with a plain fade: fade the channel's CURRENT engine to silence, then swap to the new
        // chunk on that SAME engine at full (no overlap). If nothing's playing yet, just start it now.
        internal void FadeChunk(MeloInstance channel, int chunkIndex, bool looping, float fade)
        {
            // One transition at a time (shared with crossfade): ignore a fade while one is still running.
            long now = _clock.ElapsedMilliseconds;
            if (fade > 0f && _xfadeBusyUntil.TryGetValue(channel, out var busyUntil) && now < busyUntil) return;
            if (fade > 0f) _xfadeBusyUntil[channel] = now + (long)(fade * 1000f);
            _commands.Enqueue(() =>
            {
                var eng = channel._engine;
                if (eng == null) return;
                var slot = ChannelSlot(channel);
                if (slot == null)
                {
                    _owner[eng] = channel;
                    _active.Add(new Entry { Engine = eng, Instance = channel });
                    StartEngine(eng, chunkIndex, looping);
                    return;
                }
                slot.GainStep = -1f / (fade * _sampleRate);
                slot.OnFadeComplete = () => StartEngine(slot.Engine, chunkIndex, looping);
            });
        }

        // Fade the channel's current song out to silence, then stop it (the fade-out reap in FillBuffer
        // does the stop + removal). The engine stays loaded so it can be re-Played.
        internal void FadeOutInstance(MeloInstance channel, float duration)
        {
            // One transition at a time (shared with crossfade): ignore a fade while one is still running.
            long now = _clock.ElapsedMilliseconds;
            if (duration > 0f && _xfadeBusyUntil.TryGetValue(channel, out var busyUntil) && now < busyUntil) return;
            if (duration > 0f) _xfadeBusyUntil[channel] = now + (long)(duration * 1000f);
            _commands.Enqueue(() =>
            {
                var slot = ChannelSlot(channel);
                if (slot == null) { channel._engine?.Stop(); return; }
                if (duration <= 0f)
                {
                    slot.Engine?.Stop();
                    _owner.TryRemove(slot.Engine, out _);
                    _active.Remove(slot);
                    return;
                }
                slot.Throwaway = false;                           // stop (keep the engine), don't dispose
                slot.OnFadeComplete = null;
                slot.GainStep = -1f / (duration * _sampleRate);
            });
        }

        internal void SetChannelPan(MeloInstance channel, float pan) => _commands.Enqueue(() =>
        {
            var slot = ChannelSlot(channel);
            if (slot != null) slot.Pan = pan < -1f ? -1f : (pan > 1f ? 1f : pan);
        });

        public void StopAll() => _commands.Enqueue(() =>
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].OneShot) { _active[i].Engine?.Dispose(); _active.RemoveAt(i); }
                else _active[i].Engine?.Stop();
            }
        });

        public void UnloadAll()
        {
            _sfx.StopAll();   // silence any lingering SFX voices on teardown

            // Snapshot + dispose every pooled engine, then clear the shared caches so the memory
            // actually comes back (the soundfont cache has no other release path).
            var engines = new List<MeloEngine>(_pool.Values);
            _pool.Clear();
            _owner.Clear();

            _commands.Enqueue(() =>
            {
                foreach (var e in _active) if (e.OneShot) e.Engine?.Dispose();   // pooled engines disposed below
                _active.Clear();
                _switches.Clear();
                foreach (var e in engines) e?.Dispose();
                SoundFontSynth.ClearCache();
                AudioClipCache.Clear();
            });
        }

        // -- Quantized channel switching (MeloInstance.SwitchSong), fully on the audio thread --
        // The schedule request is queued as a command so _switches is only ever touched on the audio
        // thread. Boundary detection then runs at the end of FillBuffer. One pending switch per channel
        // (a new SwitchSong on the same channel replaces its pending one).
        private sealed class Switch
        {
            public MeloInstance Channel;
            public MeloEngine From, To;
            public string ToPath;
            public int StartChunk;
            public float Fade;
            public bool Crossfade;
            public bool Throwaway;
            public bool LoopStart;
            public MeloSwitch When;
            public int PrevBar, PrevBeat;
            public bool Armed;
        }
        private readonly List<Switch> _switches = new List<Switch>();   // audio-thread owned

        internal void ScheduleSwitch(MeloInstance channel, MeloEngine to, string toPath, int startChunk, float fade, bool crossfade, bool toThrowaway, bool loopStart, MeloSwitch when) => _commands.Enqueue(() =>
        {
            if (channel == null || to == null) return;
            for (int i = _switches.Count - 1; i >= 0; i--)
                if (_switches[i].Channel == channel) _switches.RemoveAt(i);   // replace any pending switch

            var from = channel._engine;
            if (when == MeloSwitch.Now || from == null) { ExecuteSwitch(channel, from, to, toPath, startChunk, fade, crossfade, toThrowaway, loopStart); return; }
            _switches.Add(new Switch { Channel = channel, From = from, To = to, ToPath = toPath, StartChunk = startChunk, Fade = fade, Crossfade = crossfade, Throwaway = toThrowaway, LoopStart = loopStart, When = when });
        });

        // Audio thread. Pick the swap style: no fade = hard cut; crossfade = old & new overlap; plain
        // fade = old fades to silence, THEN the new starts at full.
        private void ExecuteSwitch(MeloInstance channel, MeloEngine from, MeloEngine to, string toPath, int startChunk, float fade, bool crossfade, bool toThrowaway, bool loopStart)
        {
            if (fade <= 0f || from == null) DoHardSwitch(channel, from, to, toPath, startChunk, toThrowaway, loopStart);
            else if (crossfade) DoCrossfade(channel, from, to, toPath, startChunk, toThrowaway, loopStart, fade);
            else DoFadeOutSwitch(channel, from, to, toPath, startChunk, toThrowaway, loopStart, fade);
        }

        private Entry ChannelSlot(MeloInstance channel)
        {
            foreach (var e in _active) if (!e.OneShot && e.Instance == channel) return e;
            return null;
        }

        // Start an engine on a chunk (>=0) or its arrangement (<0). loopStart=false plays the chunk once.
        private static void StartEngine(MeloEngine e, int startChunk, bool loopStart)
        {
            if (startChunk < 0) { e.PlayArrangement(); return; }
            if (e.ChunkCount == 0) return;
            int i = startChunk < e.ChunkCount ? startChunk : 0;
            if (loopStart) e.PlayChunk(i); else e.PlayChunkOnce(i);
        }

        // Hard cut: stop the old engine, reuse its slot for the new one at full volume.
        private void DoHardSwitch(MeloInstance channel, MeloEngine from, MeloEngine to, string toPath, int startChunk, bool toThrowaway, bool loopStart)
        {
            var slot = ChannelSlot(channel);
            if (from != null) { if (slot != null && slot.Throwaway) from.Dispose(); else from.Stop(); }
            channel._engine = to;
            channel._path = toPath;
            if (to == null) { if (slot != null) _active.Remove(slot); return; }
            if (slot != null) { slot.Engine = to; slot.Gain = 1f; slot.GainStep = 0f; slot.Throwaway = toThrowaway; slot.OnFadeComplete = null; }
            else _active.Add(new Entry { Engine = to, Instance = channel, Throwaway = toThrowaway });
            _owner[to] = channel;
            StartEngine(to, startChunk, loopStart);
        }

        // Crossfade: detach the old engine so it fades out on its own (then reaps), and bring the new one
        // in from silence ramping up over the same duration - the two overlap and sum to a real crossfade.
        private void DoCrossfade(MeloInstance channel, MeloEngine from, MeloEngine to, string toPath, int startChunk, bool toThrowaway, bool loopStart, float fade)
        {
            var slot = ChannelSlot(channel);
            channel._engine = to;
            channel._path = toPath;
            if (slot != null && slot.Engine == from)
            {
                slot.Instance = null;                          // detach; fades then reaps in FillBuffer
                slot.GainStep = -1f / (fade * _sampleRate);
                slot.OnFadeComplete = null;
                slot = null;                                   // channel needs a fresh slot for `to`
            }
            else from?.Stop();
            if (to == null) { if (slot != null) _active.Remove(slot); return; }
            // Incoming starts silent and ramps 0 -> 1 over the fade, mirroring the outgoing 1 -> 0.
            float fadeInStep = 1f / (fade * _sampleRate);
            if (slot != null) { slot.Engine = to; slot.Gain = 0f; slot.GainStep = fadeInStep; slot.Throwaway = toThrowaway; slot.OnFadeComplete = null; }
            else _active.Add(new Entry { Engine = to, Instance = channel, Gain = 0f, GainStep = fadeInStep, Throwaway = toThrowaway });
            _owner[to] = channel;
            StartEngine(to, startChunk, loopStart);
        }

        // Plain fade-out: fade the current engine to silence, and only when it hits 0 stop it and start
        // the new one at full (no overlap).
        private void DoFadeOutSwitch(MeloInstance channel, MeloEngine from, MeloEngine to, string toPath, int startChunk, bool toThrowaway, bool loopStart, float fade)
        {
            var slot = ChannelSlot(channel);
            if (slot == null || slot.Engine != from)
            {
                DoHardSwitch(channel, from, to, toPath, startChunk, toThrowaway, loopStart);
                return;
            }
            bool oldThrowaway = slot.Throwaway;
            slot.GainStep = -1f / (fade * _sampleRate);
            slot.OnFadeComplete = () =>
            {
                if (oldThrowaway) from.Dispose(); else from.Stop();
                _owner.TryRemove(from, out _);
                slot.Engine = to;                 // reuse the slot (Gain already reset to 1 by FillBuffer)
                slot.Throwaway = toThrowaway;
                channel._engine = to;
                channel._path = toPath;
                _owner[to] = channel;
                StartEngine(to, startChunk, loopStart);
            };
        }

        // Audio thread, called at the end of FillBuffer once the outgoing buffers have advanced.
        private void TickSwitches()
        {
            for (int i = _switches.Count - 1; i >= 0; i--)
            {
                var s = _switches[i];
                var e = s.From;
                if (e == null || s.To == null) { _switches.RemoveAt(i); continue; }

                int bar = e.CurrentBar, beat = e.CurrentBeat;
                bool fire = false;
                if (s.Armed)
                {
                    switch (s.When)
                    {
                        case MeloSwitch.Beat:  fire = beat != s.PrevBeat; break;   // crossed into a new beat
                        case MeloSwitch.Bar:   fire = bar != s.PrevBar;   break;   // crossed into a new bar
                        case MeloSwitch.Queue: fire = bar < s.PrevBar;    break;   // chunk looped (position wrapped)
                        default:               fire = true; break;
                    }
                }
                s.PrevBar = bar; s.PrevBeat = beat; s.Armed = true;
                if (fire) { _switches.RemoveAt(i); ExecuteSwitch(s.Channel, s.From, s.To, s.ToPath, s.StartChunk, s.Fade, s.Crossfade, s.Throwaway, s.LoopStart); }
            }
        }

        // -- The two hooks the host pumps --

        /// <summary>
        /// AUDIO THREAD. Fill `buffer` with the full mix as stereo-interleaved floats
        /// (buffer.Length must be &gt;= frames * 2). Master volume applied.
        /// </summary>
        public void FillBuffer(float[] buffer, int frames)
        {
            int stereoLen = frames * 2;

            while (_commands.TryDequeue(out var cmd))
            {
                try { cmd(); } catch (Exception ex) { LogError($"[Melo] command failed: {ex.Message}"); }
            }

            if (_scratch.Length < stereoLen) _scratch = new float[stereoLen];
            if (_accum.Length   < stereoLen) _accum   = new float[stereoLen];
            Array.Clear(_accum, 0, stereoLen);

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                if (e.Engine == null) { _active.RemoveAt(i); continue; }
                Array.Clear(_scratch, 0, stereoLen);
                e.Engine.FillBuffer(_scratch, frames);

                // Sum in, applying the fade-envelope gain + per-source pan (fast path when neither is set).
                float panL = e.Pan <= 0f ? 1f : 1f - e.Pan;
                float panR = e.Pan >= 0f ? 1f : 1f + e.Pan;
                if (e.GainStep == 0f && e.Gain >= 1f && e.Pan == 0f)
                {
                    for (int s = 0; s < stereoLen; s++) _accum[s] += _scratch[s];
                }
                else
                {
                    float g = e.Gain, step = e.GainStep;
                    for (int f = 0; f < frames; f++)
                    {
                        _accum[f * 2]     += _scratch[f * 2]     * g * panL;
                        _accum[f * 2 + 1] += _scratch[f * 2 + 1] * g * panR;
                        g += step;
                        if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
                    }
                    e.Gain = g;
                    if (step > 0f && g >= 1f) e.GainStep = 0f;   // fade-in complete - rejoin the fast path
                    if (step < 0f && g <= 0f)   // fade-out complete
                    {
                        if (e.OnFadeComplete != null)
                        {
                            var act = e.OnFadeComplete;
                            e.OnFadeComplete = null;
                            e.Gain = 1f; e.GainStep = 0f;   // the incoming engine plays at full
                            act();
                        }
                        else   // a fade-out finished (crossfade tail, or FadeOut): stop it and drop it
                        {
                            _owner.TryRemove(e.Engine, out _);
                            if (e.Throwaway) e.Engine.Dispose(); else e.Engine.Stop();
                            _active.RemoveAt(i);
                            continue;
                        }
                    }
                }

                // One-shot finished: render a short tail, then reap it.
                if (e.OneShot && e.Finished)
                {
                    if (e.TailLeft == 0) e.TailLeft = _sampleRate * 2;
                    e.TailLeft -= frames;
                    if (e.TailLeft <= 0) { e.Engine.Dispose(); _active.RemoveAt(i); }
                }
            }

            // Raw audio-file SFX bus (its own polyphonic mixer + effect chain), summed in.
            Array.Clear(_scratch, 0, stereoLen);
            _sfx.FillBuffer(_scratch, frames);
            for (int s = 0; s < stereoLen; s++) _accum[s] += _scratch[s];

            float mv = MasterVolume;
            for (int s = 0; s < stereoLen; s++) buffer[s] = _accum[s] * mv;

            // Channel-switch boundary checks run here, on the audio thread - never on a frame loop.
            TickSwitches();
        }

        /// <summary>
        /// MAIN THREAD. Call once per frame to deliver async-load callbacks and queued note hits.
        /// </summary>
        public void Tick()
        {
            while (_asyncResults.TryDequeue(out var r)) CompleteAsyncLoad(r);
            while (_xfadeResults.TryDequeue(out var x)) CompleteCrossfade(x);
            while (_notes.TryDequeue(out var n))
            {
                _owner.TryGetValue(n.Engine, out var inst);
                OnNote?.Invoke(inst, n.Track, n.Pitch, n.Vel);
            }
        }

        /// <summary>
        /// Dispose everything (host teardown). After this, Instance makes a fresh director.
        /// </summary>
        public void Shutdown()
        {
            foreach (var e in _active) e.Engine?.Dispose();
            _active.Clear();
            foreach (var e in _pool.Values) e?.Dispose();
            _pool.Clear();
            _owner.Clear();
            _xfadeBusyUntil.Clear();
            _sfx?.Dispose();
            if (_instance == this) _instance = null;
        }
    }
}
