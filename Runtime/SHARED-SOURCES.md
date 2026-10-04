# Shared middleware sources

The 12 shared `Melo*.cs` middleware files come from `engine-sdk/Middleware`.
Edit them in the SDK, then run `scripts/sync-unity-middleware.ps1`.
Unity build and publish scripts also refresh these copies automatically.
Do not edit shared copies here; the next sync replaces them.

Unity-specific files remain maintained here: `Melo.Unity.cs`,
`MeloUnityHost.cs`, `MeloSource.cs`, `MeloContent.cs`, and `MeloFile.cs`.
Existing `.meta` files are preserved when shared sources are copied.

Shared C# copies and Plugins/*.dll are generated and ignored in this repository.
Run build-unity-plugin.bat to assemble the local package, or
scripts/publish-unity.ps1 to build and zip it. Publishing the standalone Unity
repository always assembles the complete package first.

The .meta files remain tracked: their GUIDs and platform importer settings are
Unity-specific source data, not copies from the SDK. Builds preserve them.
