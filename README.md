# Melowrite Audio Engine for Unity

> **Platform:** Windows desktop today (Editor + Standalone). The bundled native
> decoder `miniaudio.dll` is Windows x86_64. The engine itself is managed C#
> (IL2CPP-ready); other platforms and consoles just need a `miniaudio` build for that
> target. See **Other platforms** at the bottom.

## Install

### Unity Package Manager - Git URL (recommended)

In Unity: **Window > Package Manager > + > Add package from git URL...** and paste:

```text
https://github.com/joeqnicholson/melowrite-unity.git
```

To pin a specific release, append a tag: `...melowrite-unity.git#0.1.0`.

If the package lives in a subfolder of a larger repo instead of its own repo, use
the `?path=` form:

```text
https://github.com/joeqnicholson/melowrite.git?path=/unity-plugin/Melowrite
```

## Export From Melowrite

Use `File > Export Project...` (Ctrl+E) and choose the `Project` target, then put the exported folder
anywhere in your `Assets` folder, for example:

```text
Assets/MelowriteProjects/Battle/
  music.melo
  Samples/
  Soundfonts/
  Impulses/
  Presets/
```

At build time the plugin automatically ships every `.melo` (and the `Samples/`, `Soundfonts/`,
`Impulses/`, `Presets/` folders next to it) into your player.

## Play In Unity

`.melo` files are recognized assets, so you just drag one onto a public field. Give your script a
`MeloFile` field (think of it like an `AudioClip`), drop your project onto it in the Inspector, and load it:

```csharp
using UnityEngine;
using Melowrite;

public class Music : MonoBehaviour
{
    public MeloFile song;      // drag your .melo here in the Inspector (like an AudioClip)
    public MeloSource source;

    void Start()
    {
        source.Load(song);
        source.PlayChunk(0);
    }

    public void OnCombat() => source.SwitchChunk("Combat");   // lands on the next bar
}
```

No file paths, no `AudioSource`, and the reference works in the editor and in builds. Want to load
a song dynamically by name instead? Pass a path:

```csharp
MeloInstance song = Melo.Load("MelowriteProjects/Battle/music.melo");
```

Either way the `.melo` lives under your `Assets` folder and the build hook ships it automatically.
Audio is handled by a hidden object created on first use; you never place it.

Loading decodes samples, which can hitch a frame for a big project. Load it off the main thread with
`LoadAsync` - the callback runs on the main thread with the ready instance, so nothing touches a frame:

```csharp
Melo.LoadAsync(project, song => song.PlayChunk());   // decodes in the background
```

> The `Melo` / `MeloInstance` / `MeloSwitch` API is host-agnostic - Unity is just a thin adapter
> (`MeloUnityHost`). The exact same API runs in MonoGame, FNA, or a raw SDL/console app: include
> the shared middleware source + `Melowrite.Core.dll`, then pump two hooks -
> `MeloDirector.Instance.FillBuffer(stereo, frames)` from your audio callback and
> `MeloDirector.Instance.Tick()` once per frame. Reference adapters ship with the engine SDK.

## No code: the Melo Source component

Add a **Melo Source** (**Add Component > Melowrite**) - it's the `AudioSource` of Melowrite: drag a
`.melo` on and get **volume**, **pan**, and **tempo** in the Inspector, plus start-chunk / **loop** /
**play on awake**. Every `MeloInstance` method is on the component (`source.PlayChunk(...)`,
`source.SwitchSong(...)`, `source.FadeOut(...)`, etc.), and the complete instance is at `source.Instance`.

## Adaptive music

Switch sections (chunks) or whole songs, quantized so the change lands on the music. Every switch
takes a `MeloSwitch` timing - `Now`, `Beat`, `Bar`, or `Queue` (when the current chunk finishes its
loop). `Bar` is the default.

```csharp
song.SwitchChunk("Combat");                    // on the next bar (default)
song.SwitchChunk("Boss", MeloSwitch.Queue);    // when the current section finishes its loop
song.SwitchChunk("Hit", MeloSwitch.Beat);      // on the next beat
song.SwitchChunk("GameOver", MeloSwitch.Now);  // right now

song.CancelSwitch();                           // call off a pending switch
```

Re-point an instance to a whole different project with the same timing - it waits for the current
project's boundary. The decoded engine is reused if you loaded it up front, so the swap doesn't
re-decode and `song.File` reflects whatever is playing now:

```csharp
public MeloFile bossFile;   // drag another .melo

void Start() => Melo.LoadAsync(bossFile);   // warm it up front (background) so the swap never hitches

// later, when the fight starts:
song.SwitchSong(bossFile);                  // re-points this instance on the next bar
```

Or load a brand-new song on the fly and switch to it the moment it's ready - `SwitchSong` takes either
a `MeloFile` or a loaded instance:

```csharp
Melo.LoadAsync(coolSongFile, coolSong => song.SwitchSong(coolSong, MeloSwitch.Bar));
```

Add a `fadeOut` (seconds) for a plain fade-out - the current fades to silence, then the new starts at
full. For an overlapping crossfade, use the `...Crossfade` variants. `SwitchSong` with no chunk plays
the new song's whole arrangement:

```csharp
song.SwitchSong(bossFile, "Intro", MeloSwitch.Bar, 2f);   // fade out over 2s, then Intro at full
song.PlayChunk("Bridge", fadeOut: 1.5f);                  // fade out, then Bridge

song.SwitchSongCrossfade(bossFile, "Intro", 2f);          // 2s crossfade (overlap) into Intro
song.PlayChunkCrossfade("Bridge", 1.5f);                  // 1.5s crossfade to another section of this song
```

## Runtime Control

Control the song live. Sequencer and mix calls are applied on the audio thread before the
next buffer:

```csharp
_song.SetTrackMuted("Bass", true);
_song.SetBusVolume(1, 0.5f);
_song.Track("Lead").Volume = 0.7f;
if (_song.Bus("Reverb").TryGetEffect<MeloReverb>(out var rev)) rev.Mix = 0.6f;
```

Trigger sounds from a bank project (tracks = categories, multisampler pads = named sounds):

```csharp
var sfx = Melo.Load("SFX/bank.melo");
sfx.Trigger("Footsteps", "Grass");
sfx.Trigger("Explosions", "Big", 0.8f);
sfx.Trigger("Footsteps", "Grass", 1f, pan: -0.5f);   // per-trigger stereo position
```

Every pad call takes an optional `pan` (-1 left .. +1 right), added to the pad's own
pan. The position is captured per trigger, so overlapping sounds each keep their own.

Hold a pad and release it yourself (loops, drones), or let it follow a button:

```csharp
sfx.Hold("Ambience", "EngineHum");       // starts and holds until...
sfx.Release("Ambience", "EngineHum");    // ...the pad's normal release plays

void Update()
{
    // Frame-gated hold: keeps sounding while you keep calling it, and stops
    // itself ~0.12s after the last call. No Release() bookkeeping.
    if (chargeHeld) sfx.Sustain("Weapons", "ChargeLoop");
}
```

Or play a raw audio file / `AudioClip` directly:

```csharp
Melo.PlayOneShot("SFX/hit.wav");
Melo.PlayOneShot(myAudioClip);
```

Free memory when leaving a level (Unity's scene unload does not):

```csharp
_song.Unload();     // this song
Melo.UnloadAll();   // everything, plus the shared soundfont/clip caches
```

For anything the wrappers don't cover:

```csharp
_song.RunOnAudioThread(engine => engine.SetTempo(140));
```

## Going deeper

The wrappers cover the common calls, but the whole engine is reachable - the same
classes the Melowrite editor itself edits. `using Melowrite.Audio.Instruments;` and:

```csharp
var engine = _song.Engine;                      // the raw MeloEngine behind this instance

var track = _song.Track("Footsteps");           // typed handle: Volume/Pan/Mute/Sends/Effects
if (track.Instrument is MultiSampler ms)        // every pad owns a full Sampler
{
    var pad = ms.GetPad(ms.PadIndexByName("Grass"));
    var s = pad.Sampler;
    s.Mode = SamplerMode.Random;                // Single / Velocity / Random
    s.Attack = 0.005f; s.Release = 0.25f;       // ADSR, Volume, Pan, PitchOffsetSemitones...
    Sampler.LoadSampleIntoSlot(s.AddSlot(), path);  // grow a variation bank at runtime
    s.PreviewSlot(2);                           // audition one exact slot
}
```

Scalar properties (ADSR, volume, pan, mode) are safe to set from the game thread at any
time. Loading samples decodes from disk - do it at load time, not mid-gameplay. For
sequencer state that must change in step with playback, use `RunOnAudioThread`.

## Other platforms (Mac / Linux / console / mobile)

The engine is managed C# (the sequencer, mixer, and MeltySynth all run through IL2CPP),
so it ports anywhere Unity does. The only native piece is `miniaudio`, used purely to
decode sample/clip files (WAV/MP3/FLAC/OGG). The engine fills audio buffers and Unity
owns the device, so there is no platform audio backend to port. You only need the decoders.

To target a platform other than Windows x64:

1. Build `miniaudio` for that platform (the decoders are portable C; no console audio
   backend is required) and drop the binary into `Runtime/Plugins/` with its Unity
   import settings restricted to that platform.
2. Platforms that statically link native plugins (iOS, and some consoles under IL2CPP)
   need `[DllImport("__Internal")]` instead of `[DllImport("miniaudio")]`. Build the
   engine from source with the `MELO_STATIC_NATIVE` define so the binding resolves the
   statically-linked symbols.
3. `Runtime/link.xml` (included) stops IL2CPP's managed stripper from deleting the
   reflection-based `.melo` deserializer. Leave it in place.

A WAV-only game can skip MP3/OGG/FLAC decoding entirely, shrinking the native
footprint further.
