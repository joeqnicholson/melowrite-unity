using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Melowrite.Core;
using Melowrite.Audio;
using Melowrite.Audio.Effects;
using Melowrite.Audio.Instruments;

// host agnostic middleware, any C# host (unity, monogame, fna, sdl, console)
// core renders buffers, the host owns the device and pumps two hooks
//   audio thread : MeloDirector.Instance.FillBuffer(stereoBuffer, frames)
//   main thread  : MeloDirector.Instance.Tick()   // OnNote delivery
//
// setup once (MeloUnityHost does this for unity)
//   MeloDirector.Init(deviceSampleRate);
//   MeloDirector.ResolvePath = myPathResolver;   // optional, default = path as is
//   MeloDirector.LogError = Console.Error.WriteLine;
//
//   var music = Melo.Load("music.melo");
//   music.PlayChunk();
//   music.SwitchChunk("Combat");                   // next bar
//   music.SwitchSong("boss.melo", MeloSwitch.Bar); // whole song swap, quantized on the audio thread

namespace Melowrite
{
    /// <summary>
    /// when a chunk/song switch lands
    /// </summary>
    public enum MeloSwitch
    {
        Now,     // immediately
        Beat,    // on the next beat
        Bar,     // on the next bar (default - always lands on the music)
        Queue    // when the current chunk finishes its loop
    }

    /// <summary>
    /// front door, everything goes through the shared MeloDirector
    /// </summary>
    public static partial class Melo
    {
        /// <summary>
        /// the mixer the host pumps, rarely needed directly
        /// </summary>
        public static MeloDirector Director => MeloDirector.Instance;

        /// <summary>
        /// load by path (goes through MeloDirector.ResolvePath). engines are pooled, so loading
        /// the same file twice never re-decodes. null if missing
        /// </summary>
        public static MeloInstance Load(string projectPath) => MeloDirector.Instance.Load(projectPath);

        /// <summary>
        /// decode off the main thread so it never hitches a frame. onLoaded fires from Tick with
        /// the ready channel, null on failure. skip the callback to just warm the pool
        /// </summary>
        public static void LoadAsync(string projectPath, Action<MeloInstance> onLoaded = null)
            => MeloDirector.Instance.LoadAsync(projectPath, onLoaded);

        /// <summary>
        /// final mix volume for everything melowrite, 0-1
        /// </summary>
        public static float MasterVolume
        {
            get => MeloDirector.Instance.MasterVolume;
            set => MeloDirector.Instance.MasterVolume = Clamp01(value);
        }

        /// <summary>
        /// stop sequenced playback everywhere, banks stay live for triggers
        /// </summary>
        public static void StopAll() => MeloDirector.Instance.StopAll();

        /// <summary>
        /// dispose every project + clear the soundfont/clip caches. call on scene exit
        /// </summary>
        public static void UnloadAll() => MeloDirector.Instance.UnloadAll();

        /// <summary>
        /// every note hit on any channel (instance, track, midi pitch, vel 0-127). main thread, from Tick
        /// </summary>
        public static event Action<MeloInstance, int, int, int> OnNote
        {
            add    => MeloDirector.Instance.OnNote += value;
            remove => MeloDirector.Instance.OnNote -= value;
        }

        // -- Raw audio-file SFX (wav / mp3 / ogg) --
        /// <summary>
        /// fire and forget. polyphonic, clip decoded once and cached. returns a voice id for Stop/SetVolume etc
        /// volume 0-1, pan -1..1, pitch 1 = normal 2 = octave up, effects = through the sfx bus
        /// </summary>
        public static int PlayOneShot(string file, float volume = 1f, float pan = 0f, float pitch = 1f,
                                      bool loop = false, bool effects = true)
            => MeloDirector.Instance.PlaySfx(file, volume, pan, pitch, loop, effects);

        /// <summary>
        /// same but loops until Stop(voice). engine hums, ambiences
        /// </summary>
        public static int PlayLoop(string file, float volume = 1f, float pan = 0f, float pitch = 1f,
                                   bool effects = true)
            => MeloDirector.Instance.PlaySfx(file, volume, pan, pitch, loop: true, effects: effects);

        public static void Stop(int voice)            => MeloDirector.Instance.Sfx.Stop(voice);
        public static void StopSfx()                  => MeloDirector.Instance.Sfx.StopAll();
        public static void SetVolume(int voice, float v) => MeloDirector.Instance.Sfx.SetVoiceVolume(voice, v);
        public static void SetPan(int voice, float p)    => MeloDirector.Instance.Sfx.SetVoicePan(voice, p);
        public static void SetPitch(int voice, float s)  => MeloDirector.Instance.Sfx.SetVoiceSpeed(voice, s);

        /// <summary>
        /// raw sfx bus only, 0-1. MasterVolume scales everything
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
        /// copy a project bus's effect chain onto the sfx bus, busName e.g. "A"
        /// wetMix >= 0 overrides each effect's Mix. send buses are authored full wet (the dry path
        /// skips the bus) but the sfx chain runs inline, so a verbatim copy plays 100% wet.
        /// send level * bus volume is about right
        /// </summary>
        public static void CopyEffectsFrom(MeloInstance instance, string busName, float wetMix = -1f)
            => MeloDirector.Instance.CopyEffectsFrom(instance, busName, wetMix);

        internal static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>
    /// a playback channel you hold onto. one project at a time, SwitchSong repoints it (quantized)
    /// so the handle stays valid for the life of the game object. sequencer/mix calls are
    /// deferred to the audio thread
    /// </summary>
    public sealed partial class MeloInstance
    {
        private readonly MeloDirector _rt;
        internal MeloEngine _engine;   // current engine, changes when SwitchSong lands. null once Unload()ed
        internal string _path;         // current resolved path, changes on SwitchSong

        /// <summary>
        /// raw engine for whatever the wrappers dont cover. null after Unload()
        /// </summary>
        public MeloEngine Engine => _engine;

        /// <summary>
        /// whatever's playing right now, changes after a SwitchSong lands
        /// </summary>
        public string[] ChunkNames => _engine?.GetChunkNames() ?? Array.Empty<string>();
        public string[] TrackNames => _engine?.GetTrackNames() ?? Array.Empty<string>();
        public string[] BusNames   => _engine?.GetBusNames()   ?? Array.Empty<string>();
        public bool IsLoaded => _engine != null;

#if !UNITY_5_3_OR_NEWER
        // current project path. unity returns the MeloFile asset instead (Melo.Unity.cs) so instance.File == myMeloFile works
        public string File => _path;
#endif

        internal MeloInstance(MeloDirector rt, MeloEngine engine, string path)
        {
            _rt = rt; _engine = engine; _path = path;
        }

        // -- Live state --
        public string ActiveChunk => _engine?.ActiveChunkName ?? "";
        public int CurrentBar => _engine?.CurrentBar ?? 0;
        public int CurrentBeat => _engine?.CurrentBeat ?? 0;
        public int Tempo => _engine?.Tempo ?? 0;
        public int ChunkCount => _engine?.ChunkCount ?? 0;
        public int TrackCount => _engine?.TrackCount ?? 0;
        public string[] GetPaletteNames() => _engine?.GetPaletteNames() ?? Array.Empty<string>();

        // -- Sequenced playback (music) --

        /// <summary>
        /// play a section, loops by default. null = first chunk. fadeOut fades the current one
        /// to silence first then starts this one (no overlap, that's PlayChunkCrossfade)
        /// </summary>
        public void PlayChunk(string chunk = null, bool looping = true, float fadeOut = 0f)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            PlayChunkResolved(idx, looping, fadeOut);
        }

        /// <summary>
        /// by index
        /// </summary>
        public void PlayChunk(int index, bool looping = true, float fadeOut = 0f)
        {
            if (_engine == null) return;
            PlayChunkResolved(index, looping, fadeOut);
        }

        void PlayChunkResolved(int idx, bool looping, float fadeOut)
        {
            if (idx < 0) return;
            if (fadeOut > 0f) _rt.FadeChunk(this, idx, looping, fadeOut);   // fade out then swap on the same engine
            else { _rt.Activate(this); _rt.Defer(() => PlayChunkAt(idx, looping)); }
        }

        /// <summary>
        /// crossfade to another section of this song. a fresh instance fades in while the current one fades out
        /// </summary>
        public void PlayChunkCrossfade(string chunk, float duration, bool looping = true)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            if (idx >= 0) _rt.CrossfadeChunk(this, _path, idx, looping, duration);
        }

        /// <summary>
        /// by index
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
        /// play the whole timeline. fadeOut = fade whatever's playing out first
        /// </summary>
        public void PlayArrangement(float fadeOut = 0f)
        {
            if (_engine == null) return;
            if (fadeOut > 0f) _rt.FadeChunk(this, -1, true, fadeOut);   // -1 = arrangement
            else { _rt.Activate(this); _rt.Defer(() => _engine?.PlayArrangement()); }
        }

        /// <summary>
        /// crossfade into the arrangement (overlapping)
        /// </summary>
        public void PlayArrangementCrossfade(float duration)
        {
            if (_engine != null) _rt.CrossfadeChunk(this, _path, -1, true, duration);   // -1 = arrangement
        }

        /// <summary>
        /// fire a chunk on its own throwaway playhead, no handle. overlaps whatever this channel is
        /// playing and reaps itself when done. heavier than Trigger, its a whole engine per fire
        /// </summary>
        public void PlayChunkHeadless(string chunk = null)
        {
            if (_engine == null) return;
            int idx = string.IsNullOrEmpty(chunk) ? 0 : _engine.GetChunkIndex(chunk);
            if (idx >= 0) _rt.SpawnOneShot(_path, idx);
        }

        /// <summary>
        /// by index
        /// </summary>
        public void PlayChunkHeadless(int index)
        {
            if (_engine != null) _rt.SpawnOneShot(_path, index);
        }

        /// <summary>
        /// stop sequenced playback, tails ring out. channel stays live, Unload() frees it
        /// </summary>
        public void Stop()   => _rt.Defer(() => _engine?.Stop());
        public void Pause()  => _rt.Defer(() => _engine?.Pause());
        public void Resume() => _rt.Defer(() => _engine?.Resume());

        /// <summary>
        /// fade to silence over duration then stop. duration 0 or less stops now
        /// </summary>
        public void FadeOut(float duration = 1f) => _rt.FadeOutInstance(this, duration);

        /// <summary>
        /// switch section of the current project. defaults to next bar so it lands on the music
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
        /// repoint this channel to a different project, quantized. boundary detection is on the
        /// audio thread so its buffer accurate. pooled engines get reused, no re-decode.
        /// no start chunk = arrangement. fadeOut fades the old one out first (overlap = SwitchSongCrossfade)
        /// </summary>
        public void SwitchSong(string projectPath, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, -1, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// start on a named chunk
        /// </summary>
        public void SwitchSong(string projectPath, string startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to == null) return;
            int idx = string.IsNullOrEmpty(startChunk) ? -1 : to.GetChunkIndex(startChunk);
            _rt.ScheduleSwitch(this, to, resolved, idx, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// start on a chunk index
        /// </summary>
        public void SwitchSong(string projectPath, int startChunk, MeloSwitch when = MeloSwitch.Bar, float fadeOut = 0f)
        {
            var to = _rt.ResolveAndLoad(projectPath, out var resolved);
            if (to != null) _rt.ScheduleSwitch(this, to, resolved, startChunk, fadeOut, false, false, true, when);
        }

        /// <summary>
        /// switch to whatever other has loaded. free, reuses its engine. nice from a LoadAsync callback
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

        // -- Crossfade variants, old and new overlap over duration --
        // no start chunk = arrangement
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
        /// cancel a pending SwitchChunk that hasnt fired
        /// </summary>
        public void CancelSwitch() => _rt.Defer(() => { _engine?.CancelSchedule(); _engine?.CancelQueue(); });

        public void SeekToBar(int bar) => _rt.Defer(() => _engine?.SeekToBar(bar));

        // -- Mix --
        public void SetTempo(int bpm)                     => _rt.Defer(() => _engine?.SetTempo(bpm));
        public void SetMasterVolume(float volume)         => _rt.Defer(() => _engine?.SetMasterVolume(volume));
        /// <summary>
        /// whole song pan, -1 left 0 center 1 right
        /// </summary>
        public void SetPan(float pan)                     => _rt.SetChannelPan(this, pan);
        public void SetTrackMuted(string track, bool m)   => _rt.Defer(() => _engine?.SetTrackMuted(track, m));
        public void SetTrackMuted(int track, bool m)      => _rt.Defer(() => _engine?.SetTrackMuted(track, m));
        public void SetTrackVolume(string track, float v) => _rt.Defer(() => _engine?.SetTrackVolume(track, v));
        public void SetTrackVolume(int track, float v)    => _rt.Defer(() => _engine?.SetTrackVolume(track, v));
        /// <summary>
        /// send to bus A (usually reverb) / B (usually delay), 0-1
        /// </summary>
        public void SetSendA(string track, float level)   => _rt.Defer(() => _engine?.SetTrackSend(track, "A", level));
        public void SetSendA(int track, float level)      => _rt.Defer(() => _engine?.SetTrackSendA(track, level));
        public void SetSendB(string track, float level)   => _rt.Defer(() => _engine?.SetTrackSend(track, "B", level));
        public void SetSendB(int track, float level)      => _rt.Defer(() => _engine?.SetTrackSendB(track, level));
        public void SetBusVolume(int bus, float volume)   => _rt.Defer(() => _engine?.SetBusVolume(bus, volume));
        public void SwapPalette(string palette)           => _rt.Defer(() => _engine?.SwapPalette(palette));

        /// <summary>
        /// e.g. SetBusEffectParam(1, 0, "Mix", 0.5f)
        /// </summary>
        public void SetBusEffectParam(int bus, int effect, string param, float value)
            => _rt.Defer(() => _engine?.SetBusEffectParam(bus, effect, param, value));
        /// <summary>
        /// same for a track effect
        /// </summary>
        public void SetTrackEffectParam(int track, int effect, string param, float value)
            => _rt.Defer(() => _engine?.SetTrackEffectParam(track, effect, param, value));

        // -- Live triggering (SFX banks) --
        // fire this project's instruments live. transport can be stopped, no new engine.
        // author the .melo as a bank (tracks = categories, pads = variations)

        /// <summary>
        /// one hit of a named pad. holds for MeloEngine.PadHitSeconds then releases through the pad
        /// envelope, timed on the audio thread. overlaps itself. pan is per trigger, added to the pad's own.
        /// Hold/Release for a held pad, Sustain for while-a-button-is-down
        /// </summary>
        public void Trigger(string track, string pad, float velocity = 1f, float pan = 0f)
            => Trigger(TrackIndex(track), pad, velocity, pan);

        /// <summary>
        /// by track index
        /// </summary>
        public void Trigger(int trackIndex, string pad, float velocity = 1f, float pan = 0f)
        {
            if (_engine == null) return;
            _rt.Activate(this);                        // make sure this bank is in the mix
            _engine.TriggerPad(trackIndex, pad, velocity, pan);   // engine queues it internally
        }

        /// <summary>
        /// held pad (loops, drones). Release() to stop
        /// </summary>
        public void Hold(string track, string pad, float velocity = 1f, float pan = 0f)
        {
            if (_engine == null) return;
            _rt.Activate(this);
            _engine.HoldPad(TrackIndex(track), pad, velocity, pan);
        }

        /// <summary>
        /// call every frame while a button is down. stop calling and it releases ~0.12s later,
        /// no Release() bookkeeping. recalls refresh the hold, never retrigger
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
        /// release a Hold()
        /// </summary>
        public void Release(string track, string pad)
            => _engine?.ReleasePad(TrackIndex(track), pad);

        /// <summary>
        /// melodic note on, midi pitch, vel 0-1
        /// </summary>
        public void NoteOn(int trackIndex, int midiNote, float velocity = 1f)
        {
            if (_engine == null) return;
            _rt.Activate(this);
            _engine.NoteOn(trackIndex, midiNote, velocity);
        }

        /// <summary>
        /// melodic note off
        /// </summary>
        public void NoteOff(int trackIndex, int midiNote) => _engine?.NoteOff(trackIndex, midiNote);

        // -- Raw audio-file SFX through this channel's mix --
        // same calls as the static Melo ones but summed into this project's master chain.
        // voices die with the engine on SwitchSong/Unload

        /// <summary>
        /// same as the static Melo.PlayOneShot but summed into this channel's master chain.
        /// effects=false skips the master effects but keeps master volume
        /// </summary>
        public int PlayOneShot(string file, float volume = 1f, float pan = 0f, float pitch = 1f,
                               bool loop = false, bool effects = true)
        {
            if (_engine == null) return -1;
            string path = MeloDirector.ResolvePath(file);
            if (path == null) { MeloDirector.LogError($"[Melo] Sound not found: {file}"); return -1; }
            _rt.Activate(this);   // make sure this channel is in the mix
            int voice = _engine.PlayOneShot(path, volume, pan, pitch, loop, effects);
            if (voice < 0) MeloDirector.LogError($"[Melo] Couldn't decode: {file}");
            return voice;
        }

        /// <summary>
        /// loops until Stop(voice)
        /// </summary>
        public int PlayLoop(string file, float volume = 1f, float pan = 0f, float pitch = 1f,
                            bool effects = true)
            => PlayOneShot(file, volume, pan, pitch, loop: true, effects: effects);

        public void Stop(int voice)               => _engine?.StopOneShot(voice);
        public void StopSfx()                     => _engine?.StopOneShots();
        public void SetVolume(int voice, float v) => _engine?.SetOneShotVolume(voice, v);
        public void SetPan(int voice, float p)    => _engine?.SetOneShotPan(voice, p);
        public void SetPitch(int voice, float s)  => _engine?.SetOneShotPitch(voice, s);

        private int TrackIndex(string name) => _engine == null ? -1 : _engine.GetTrackIndex(name);

        // -- Typed handles --
        // live project objects (Volume, Pan, Sends, effects). for changes mid playback
        // prefer the queued Set* calls above or RunOnAudioThread

        /// <summary>
        /// by name, check IsValid
        /// </summary>
        public MeloTrack Track(string name)
        {
            if (_engine == null) return default;
            int idx = _engine.GetTrackIndex(name);
            return idx < 0 ? default : new MeloTrack(_engine.Project.Tracks[idx], idx);
        }

        /// <summary>
        /// by index, check IsValid
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
        /// by name, check IsValid
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
        /// by index, 0 is master
        /// </summary>
        public MeloBus Bus(int index)
        {
            if (_engine == null || index < 0 || index >= _engine.Project.Buses.Count) return default;
            return new MeloBus(_engine.Project.Buses[index], index);
        }

        /// <summary>
        /// the master bus
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
        /// anything the wrappers dont cover, runs on the audio thread
        /// </summary>
        public void RunOnAudioThread(Action<MeloEngine> command)
            => _rt.Defer(() => { if (_engine != null) command(_engine); });

        // -- Lifecycle --

        /// <summary>
        /// stop + dispose the engine, drop it from the pool. shared soundfonts go with UnloadAll()
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
    /// mixer + lifecycle. sums every active engine + the sfx bus into one stereo out.
    /// host owns the device and pumps FillBuffer (audio thread) + Tick (main thread).
    /// core only, no framework types
    /// </summary>
    public sealed class MeloDirector
    {
        // -- Host config, set before first use --
        /// <summary>
        /// caller path -> disk path. identity by default, unity points it at StreamingAssets
        /// </summary>
        public static Func<string, string> ResolvePath = p => p;
        public static Action<string> LogError = System.Console.Error.WriteLine;
        public static Action<string> LogWarning = System.Console.WriteLine;

        private static int _initSampleRate = 44100;
        private static MeloDirector _instance;

        /// <summary>
        /// output rate, set BEFORE first use, must match the device. default 44100
        /// </summary>
        public static void Init(int sampleRate) => _initSampleRate = sampleRate > 0 ? sampleRate : 44100;
        public static MeloDirector Instance => _instance ?? (_instance = new MeloDirector(_initSampleRate));

        public float MasterVolume = 1f;
        public event Action<MeloInstance, int, int, int> OnNote;

        private readonly int _sampleRate;
        private readonly ConcurrentQueue<Action> _commands = new ConcurrentQueue<Action>();
        private readonly List<Entry> _active = new List<Entry>();               // audio thread owned
        private readonly Dictionary<string, MeloEngine> _pool = new Dictionary<string, MeloEngine>(); // main thread owned, decode once
        private readonly ConcurrentQueue<NoteHit> _notes = new ConcurrentQueue<NoteHit>();
        private readonly ConcurrentDictionary<MeloEngine, MeloInstance> _owner = new ConcurrentDictionary<MeloEngine, MeloInstance>();
        private float[] _scratch = Array.Empty<float>();
        private float[] _accum = Array.Empty<float>();

        //Sfx
        private readonly MeloAudio _sfx;
        private ReverbEffect _reverb;   // added to _sfx on first SetReverb
        private DelayEffect _delay;
        public MeloAudio Sfx => _sfx;
        public int SampleRate => _sampleRate;

        private sealed class Entry
        {
            public MeloEngine Engine;
            public MeloInstance Instance;   // null for one shots and detached fading engines
            public bool OneShot;
            public volatile bool Finished;
            public int TailLeft;
            // crossfade envelope. output scaled by Gain, ramps GainStep per frame (<0 = fading out).
            // fade out hits 0 = reaped, disposed if Throwaway else Stopped (pooled)
            public float Gain = 1f;
            public float GainStep = 0f;
            public float Pan = 0f;          // -1..1, applied at the mix
            public bool Throwaway;
            public Action OnFadeComplete;   // audio thread, runs instead of reaping when a fade out hits 0
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

        public void CopyEffectsFrom(MeloInstance instance, string busName, float wetMix = -1f)
        {
            var engine = instance?.Engine;
            if (engine == null) { LogWarning("[Melo] CopyEffectsFrom: project not loaded"); return; }
            var project = engine.Project;

            BusTrack bus = null;
            foreach (var b in project.Buses)
                if (string.Equals(b.Name, busName, StringComparison.OrdinalIgnoreCase)) { bus = b; break; }
            if (bus == null) { LogWarning($"[Melo] CopyEffectsFrom: bus '{busName}' not found"); return; }

            // deep clone through the dto round trip, params only, fresh state
            var clones = new List<Effect>();
            foreach (var fx in bus.Effects)
            {
                try
                {
                    var dto = new EffectDto { Type = fx.TypeId, Enabled = fx.Enabled };
                    fx.WriteTo(dto);
                    var clone = ProjectSerializer.EffectFromDto(dto, project.ImpulsesFolder);
                    if (clone != null) clones.Add(clone);
                }
                catch (Exception ex) { LogWarning($"[Melo] couldn't clone effect '{fx?.Name}': {ex.Message}"); }
            }
            // send chains are full wet, knock Mix down to stand in for the send level
            if (wetMix >= 0f)
                foreach (var fx in clones)
                    if (fx is MixEffect mb) mb.Mix = Melo.Clamp01(wetMix);
            _sfx.SetEffectChain(clones);
            _reverb = null; _delay = null;   // manual setters dont own the chain anymore
        }

        // get or decode. one engine per path until UnloadAll, repeat Load/SwitchSong never re-decodes
        internal MeloEngine GetOrLoadEngine(string resolvedPath)
        {
            if (_pool.TryGetValue(resolvedPath, out var cached)) return cached;
            var engine = MeloEngine.Load(resolvedPath, sampleRate: _sampleRate);
            _pool[resolvedPath] = engine;
            var eng = engine;
            eng.OnNote += (t, p, v) => _notes.Enqueue(new NoteHit { Engine = eng, Track = t, Pitch = p, Vel = v });
            return engine;
        }

        // null (and logged) on failure
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

        // -- Async loading --
        private readonly ConcurrentQueue<AsyncLoad> _asyncResults = new ConcurrentQueue<AsyncLoad>();
        private struct AsyncLoad
        {
            public string Path;                    // resolved, null = resolve failed
            public MeloEngine Engine;              // null = decode failed
            public string Error;
            public Action<MeloInstance> OnLoaded;  // null = just warming the pool
        }

        /// <summary>
        /// bumped every publish, log it to confirm which build loaded
        /// </summary>
        public const string Build = "2026.07.30a";

        // one crossfade per channel at a time. a request mid fade is dropped, so hammering
        // the button doesnt stack engines or freeze the game
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
            // already decoded, deliver next Tick
            if (_pool.TryGetValue(path, out var cached))
            {
                _asyncResults.Enqueue(new AsyncLoad { Path = path, Engine = cached, OnLoaded = onLoaded });
                return;
            }
            // decode on the threadpool, the sample/soundfont caches are thread safe.
            // pool insert + callback happen in Tick so _pool stays main thread only
            System.Threading.Tasks.Task.Run(() =>
            {
                MeloEngine engine = null; string err = null;
                try { engine = MeloEngine.Load(path, sampleRate: _sampleRate); }
                catch (Exception ex) { err = ex.Message; }
                _asyncResults.Enqueue(new AsyncLoad { Path = path, Engine = engine, Error = err, OnLoaded = onLoaded });
            });
        }

        // main thread from Tick. pool the engine (or reuse if another load beat us) then hand it to the callback
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
                if (!ReferenceEquals(existing, r.Engine)) r.Engine.Dispose();   // lost a decode race
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

        // from MeloInstance.Unload. drop from the pool, dispose on the audio thread after pulling it from the mix
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

        // whole chunk once on a throwaway engine (overlapping sting). reaps when finished. loads on the calling thread
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

        // one transition per channel at a time. false while one is running, otherwise reserves
        // the channel for seconds. every crossfade/fade entry point calls this first
        internal bool TryBeginTransition(MeloInstance channel, float seconds)
        {
            if (channel == null) return false;
            long now = _clock.ElapsedMilliseconds;
            if (_xfadeBusyUntil.TryGetValue(channel, out var until) && now < until) return false;
            _xfadeBusyUntil[channel] = now + (long)(seconds * 1000f) + 250;   // 250ms decode headroom
            return true;
        }

        // crossfade to a fresh throwaway engine of the same song at chunkIndex (the pool only holds
        // one per song) while the current one fades out. reuses the switch path
        internal void CrossfadeChunk(MeloInstance channel, string path, int chunkIndex, bool looping, float fadeOut)
        {
            if (!TryBeginTransition(channel, fadeOut)) return;
            // decode off the main thread, a sync Load per press freezes the game
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

        // main thread from Tick. decode done, re-anchor the busy window to the real fade start and kick it.
        // failed decode frees the channel so the next press retries
        private void CompleteCrossfade(XfadeLoad r)
        {
            if (r.Engine == null) { _xfadeBusyUntil[r.Channel] = 0; return; }
            _xfadeBusyUntil[r.Channel] = _clock.ElapsedMilliseconds + (long)(r.FadeOut * 1000f);
            var eng = r.Engine;
            eng.OnNote += (t, p, v) => _notes.Enqueue(new NoteHit { Engine = eng, Track = t, Pitch = p, Vel = v });
            ScheduleSwitch(r.Channel, r.Engine, r.Path, r.ChunkIndex, r.FadeOut, true, true, r.Looping, MeloSwitch.Now);
        }

        // plain fade. current engine to silence then swap chunks on the SAME engine (no overlap).
        // nothing playing = just start
        internal void FadeChunk(MeloInstance channel, int chunkIndex, bool looping, float fade)
        {
            // one transition at a time, shared with crossfade
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

        // fade out then stop (the fade reap in FillBuffer does the stop). engine stays loaded
        internal void FadeOutInstance(MeloInstance channel, float duration)
        {
            // one transition at a time, shared with crossfade
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
                slot.Throwaway = false;                           // stop, dont dispose
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
            _sfx.StopAll();

            // dispose every pooled engine then clear the shared caches, the soundfont cache has no other release path
            var engines = new List<MeloEngine>(_pool.Values);
            _pool.Clear();
            _owner.Clear();

            _commands.Enqueue(() =>
            {
                foreach (var e in _active) if (e.OneShot) e.Engine?.Dispose();   // pooled ones below
                _active.Clear();
                _switches.Clear();
                foreach (var e in engines) e?.Dispose();
                SoundFontSynth.ClearCache();
                AudioClipCache.Clear();
            });
        }

        // -- Quantized switching (SwitchSong), all on the audio thread --
        // scheduling is queued as a command so _switches is audio thread only. boundary checks
        // run at the end of FillBuffer. one pending switch per channel, a new one replaces it
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
        private readonly List<Switch> _switches = new List<Switch>();   // audio thread owned

        internal void ScheduleSwitch(MeloInstance channel, MeloEngine to, string toPath, int startChunk, float fade, bool crossfade, bool toThrowaway, bool loopStart, MeloSwitch when) => _commands.Enqueue(() =>
        {
            if (channel == null || to == null) return;
            for (int i = _switches.Count - 1; i >= 0; i--)
                if (_switches[i].Channel == channel) _switches.RemoveAt(i);   // replace pending

            var from = channel._engine;
            if (when == MeloSwitch.Now || from == null) { ExecuteSwitch(channel, from, to, toPath, startChunk, fade, crossfade, toThrowaway, loopStart); return; }
            _switches.Add(new Switch { Channel = channel, From = from, To = to, ToPath = toPath, StartChunk = startChunk, Fade = fade, Crossfade = crossfade, Throwaway = toThrowaway, LoopStart = loopStart, When = when });
        });

        // audio thread. no fade = hard cut, crossfade = overlap, plain fade = old to silence THEN new at full
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

        // chunk >= 0 or arrangement < 0. loopStart=false plays once
        private static void StartEngine(MeloEngine e, int startChunk, bool loopStart)
        {
            if (startChunk < 0) { e.PlayArrangement(); return; }
            if (e.ChunkCount == 0) return;
            int i = startChunk < e.ChunkCount ? startChunk : 0;
            if (loopStart) e.PlayChunk(i); else e.PlayChunkOnce(i);
        }

        // stop the old engine, reuse its slot
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

        // detach the old engine so it fades out and reaps on its own, bring the new one in from silence over the same duration
        private void DoCrossfade(MeloInstance channel, MeloEngine from, MeloEngine to, string toPath, int startChunk, bool toThrowaway, bool loopStart, float fade)
        {
            var slot = ChannelSlot(channel);
            channel._engine = to;
            channel._path = toPath;
            if (slot != null && slot.Engine == from)
            {
                slot.Instance = null;                          // detached, fades then reaps in FillBuffer
                slot.GainStep = -1f / (fade * _sampleRate);
                slot.OnFadeComplete = null;
                slot = null;                                   // channel needs a fresh slot for to
            }
            else from?.Stop();
            if (to == null) { if (slot != null) _active.Remove(slot); return; }
            // incoming ramps 0 -> 1 mirroring the outgoing 1 -> 0
            float fadeInStep = 1f / (fade * _sampleRate);
            if (slot != null) { slot.Engine = to; slot.Gain = 0f; slot.GainStep = fadeInStep; slot.Throwaway = toThrowaway; slot.OnFadeComplete = null; }
            else _active.Add(new Entry { Engine = to, Instance = channel, Gain = 0f, GainStep = fadeInStep, Throwaway = toThrowaway });
            _owner[to] = channel;
            StartEngine(to, startChunk, loopStart);
        }

        // fade the current engine to 0, only then stop it and start the new one at full
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
                slot.Engine = to;                 // reuse the slot, FillBuffer already reset Gain to 1
                slot.Throwaway = toThrowaway;
                channel._engine = to;
                channel._path = toPath;
                _owner[to] = channel;
                StartEngine(to, startChunk, loopStart);
            };
        }

        // audio thread, end of FillBuffer after the buffers advanced
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
                        case MeloSwitch.Beat:  fire = beat != s.PrevBeat; break;   // new beat
                        case MeloSwitch.Bar:   fire = bar != s.PrevBar;   break;   // new bar
                        case MeloSwitch.Queue: fire = bar < s.PrevBar;    break;   // chunk wrapped
                        default:               fire = true; break;
                    }
                }
                s.PrevBar = bar; s.PrevBeat = beat; s.Armed = true;
                if (fire) { _switches.RemoveAt(i); ExecuteSwitch(s.Channel, s.From, s.To, s.ToPath, s.StartChunk, s.Fade, s.Crossfade, s.Throwaway, s.LoopStart); }
            }
        }

        // -- The two hooks the host pumps --

        /// <summary>
        /// AUDIO THREAD. fill buffer with the full mix, stereo interleaved, length >= frames*2. master volume applied
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

                // sum in with fade gain + pan, fast path when neither is set
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
                    if (step > 0f && g >= 1f) e.GainStep = 0f;   // fade in done, back to the fast path
                    if (step < 0f && g <= 0f)   // fade out done
                    {
                        if (e.OnFadeComplete != null)
                        {
                            var act = e.OnFadeComplete;
                            e.OnFadeComplete = null;
                            e.Gain = 1f; e.GainStep = 0f;   // incoming plays at full
                            act();
                        }
                        else   // crossfade tail or FadeOut, stop it and drop it
                        {
                            _owner.TryRemove(e.Engine, out _);
                            if (e.Throwaway) e.Engine.Dispose(); else e.Engine.Stop();
                            _active.RemoveAt(i);
                            continue;
                        }
                    }
                }

                // one shot finished, short tail then reap
                if (e.OneShot && e.Finished)
                {
                    if (e.TailLeft == 0) e.TailLeft = _sampleRate * 2;
                    e.TailLeft -= frames;
                    if (e.TailLeft <= 0) { e.Engine.Dispose(); _active.RemoveAt(i); }
                }
            }

            // sfx bus summed in
            Array.Clear(_scratch, 0, stereoLen);
            _sfx.FillBuffer(_scratch, frames);
            for (int s = 0; s < stereoLen; s++) _accum[s] += _scratch[s];

            // soft knee the sum. engines and the sfx bus limit themselves but the SUM can still clip at the device
            float mv = MasterVolume;
            for (int s = 0; s < stereoLen; s++) buffer[s] = MasterMix.SoftClip(_accum[s] * mv);

            // switch boundary checks live here on the audio thread, never the frame loop
            TickSwitches();
        }

        /// <summary>
        /// MAIN THREAD. once per frame, delivers async load callbacks + queued note hits
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
        /// host teardown. Instance makes a fresh director after this
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
