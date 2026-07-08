# Changelog

All notable changes to the Melowrite Audio Engine for Unity are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0]

- `.melo` files are recognized Unity assets: drag one onto a `public MeloFile` field and pass it
  to `Melo.Load(file)`. The reference works in the editor and in builds - no file-path strings.
- The static `Melo` API: `Melo.Load(...)` (a `MeloFile` or an Assets-relative path) returns a
  `MeloInstance` you hold - a persistent playback instance with chunks, arrangement, mixer, sends,
  bus/track effect params, palette swaps, typed `Track`/`Bus` handles, and SFX-bank pad triggers.
- The `MeloSource` component (**Add Component > Melowrite > Melo Source**) - the `AudioSource` of
  Melowrite, wrapping a `MeloInstance` for no-code use: drag a `.melo` on and get start chunk, loop,
  play-on-awake, plus live volume / pan / tempo in the Inspector. The full `MeloInstance` surface is
  forwarded on the component (`source.PlayChunk(...)`, `source.SwitchSong(...)`, triggers, typed
  `Track`/`Bus` handles), `SwitchSong` also accepts another `MeloSource`, and the wrapped instance
  is at `source.Instance`.
- Adaptive switching with a single timing enum: `song.SwitchChunk(name, MeloSwitch.Bar/Beat/Queue/Now)`
  quantizes section changes to the music, and `song.SwitchSong(otherFile, MeloSwitch.Bar)` re-points the
  same instance to a whole different project on the boundary (no chunk = the new song's arrangement).
  Both take an optional `fadeOut` (seconds) for a plain fade-out; `SwitchSongCrossfade` /
  `PlayChunkCrossfade` do an overlapping crossfade instead. `song.File` tells you what's playing now.
- Loaded projects are held and reused: `Melo.LoadAsync(file)` decodes a project up front (off the
  main thread) so a later `SwitchSong` doesn't re-decode. `Melo.UnloadAll()` frees everything.
- One-shots: `Melo.PlayOneShot(file)` / `Melo.PlayOneShot(AudioClip)` fire a sound;
  `song.PlayChunkHeadless(chunk)` overlays a whole section. A global SFX effects bus
  (`Melo.SetReverb` / `SetDelay`) and `Melo.CopyEffectsFrom(song, bus)`.
- Reference a `.melo` from anywhere in `Assets`; a build hook ships it (and its sibling
  `Samples/`/`Soundfonts/`/`Impulses/`/`Presets/`) into builds automatically.
- Real-time sequencer with synths, samplers, multi-sampler, SoundFont playback, and effects
  (reverb, convolution reverb, delay, filter, EQ, chorus).

### Known limitations
- Windows desktop only - the native `miniaudio.dll` is built for Windows x86_64. Other platforms
  need their own native build of that DLL.
