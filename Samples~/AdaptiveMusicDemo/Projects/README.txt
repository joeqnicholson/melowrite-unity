Melowrite project export - unity-demo-2
====================================================================

This folder is a SONG + its assets. It does NOT contain the engine - a game
ships ONE engine and many songs, so the runtime lives separately (see below).

FOLDER LAYOUT
  unity-demo-2.melo            The song. Pass this path to melo_load().
  Samples/               Audio used by samplers + timeline clips
                         (subfolders preserved so same-named files don't collide).
  Soundfonts/            .sf2 files the project references.
  Impulses/              Convolution-reverb impulse responses (if any).

PATH CONVENTION (inside unity-demo-2.melo)
  Refs starting with "~/" are project-relative - resolved against Samples/ or
  Soundfonts/ here at load time, subfolders preserved. Example:
      "~/drum/128-0 Pocket/BD.wav"  ->  Samples/drum/128-0 Pocket/BD.wav
  Bare names resolve the same way; rooted paths (C:\...) are read as-is.

THE ENGINE (get it once, ship it once)
  UNITY: do NOT export the engine - install the Melowrite Unity package instead. It
  already bundles the engine. Install it via Package Manager:
      1. In Unity: Window -> Package Manager
      2. Click the "+" button (top-left) -> "Add package from git URL..."
      3. Paste:  https://github.com/joeqnicholson/melowrite-unity.git
      4. Click Add.
  Then drop THIS folder under Assets/StreamingAssets/ and point the player component
  at the .melo. (Docs: https://www.melowrite.org/docs) Skip the rest of this section.

  C / C++ / native C#: In Melowrite, File -> Export Game -> "Engine". That gives you:
      Melowrite.Core.dll   the whole engine in ONE file (the audio decoder is
                           linked in). Ship just this next to your game .exe.
      melo.h  (C / C++)    Melo.cs  (C#)
  One engine drives any number of these project folders.

USE IT (C / C++ / native C# - Unity users use the package's player component instead)
  C++:  MeloHandle h = melo_load("unity-demo-2.melo", 44100); melo_play_arrangement(h);
        // audio callback: melo_fill_buffer(h, buffer, frames);
  C#:   IntPtr h = Melo.melo_load("unity-demo-2.melo", 44100); Melo.melo_play_arrangement(h);
        // audio callback: Melo.FillBuffer(h, buffer, frames);
  Only melo_fill_buffer / Melo.FillBuffer is safe to call from the audio thread.

Re-exporting is safe: every reference is rebuilt from the authoring project,
so the asset tree is regenerated from scratch each time.
