# ValheimSkies

Valheim mod that swaps the night sky for HD space skies. It puts a chosen cubemap on the `_StarFieldTex` slot of the
game's sky material, so the skies show best at night. Settings: Sky (or Vanilla), Random Sky, Show Message and
Hide Yggdrasil (the big tree in the sky). Client side only.

## Building

The sky images live in the `valheim_skies` asset bundle, which is **not in git** (it is about 180 MB). Build it first:

1. Open the Unity project at `E:\Unity_Projects\ValhiemSkies` (Unity 6000.0.76f1).
2. Run **Valheim Skies > Build Skies Bundle**. The Console should show an "all cube edges join up" line for each of
   the 11 skies. The bundle is copied into `ValheimSkies/Resources/`.
3. Build the mod: VS MSBuild `ValheimSkies.sln -restore -p:Configuration=Release`. The build stops with an error if
   `Resources/valheim_skies` is missing, and copies the bundle next to the DLL in the game's `BepInEx/plugins`.

The bundle builder (`Assets/Editor/SkyBundleBuilder.cs`, in the Unity project) makes one Cubemap per sky from the six
faces in `Assets/HD_Skies/<folder>`:

- Each face is flipped vertically and put straight into its cubemap slot (a Cubemap stores faces upside down compared
  to a 6 sided skybox; this is the layout the original Badgers Valheim Skies uses).
- The pixels along all 12 cube edges are compared, and the build fails if any edge does not join up.
- A 1 level dither is added (the skies are dark gradients the game brightens, which shows banding), the cubemap has no
  mip chain, and it is compressed to BC7.

The sky names and asset names are listed in `SkyCatalog.cs` and must be kept in step with the builder.
