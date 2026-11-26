using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the valheim_physical_sky asset bundle for ValheimSkies: the Physical Sky shader, the Moon shader and the
// moon picture. Menu: Valheim Skies > Build Physical Sky Bundle (or -executeMethod PhysicalSkyBundleBuilder.Build).
// The sources live in the mod's repository (unity/PhysicalSky) and are copied here by tools/build_physical_sky.ps1.
public static class PhysicalSkyBundleBuilder
{
    private const string Folder = "Assets/ValheimSkies/PhysicalSky";
    private const string OutputFolder = "AssetBundles/ValheimPhysicalSky";
    private const string BundleName = "valheim_physical_sky";
    private const string ModResourcesFolder = @"E:\Valheim_Mods\ValheimSkies\ValheimSkies\Resources";

    private static readonly string[] Shaders = { "PhysicalSky.shader", "Moon.shader" };
    private const string MoonFile = "moon.png";

    [MenuItem("Valheim Skies/Build Physical Sky Bundle")]
    public static void Build()
    {
        foreach (string file in Shaders)
        {
            AssetImporter importer = AssetImporter.GetAtPath(Folder + "/" + file);
            if (importer == null)
            {
                throw new FileNotFoundException("Missing " + Folder + "/" + file);
            }
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/" + file);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                throw new System.Exception(file + " has errors; see the console.");
            }
        }

        // The moon: colour with alpha as transparency, clamped, mipmapped, kept at its own size
        TextureImporter moon = AssetImporter.GetAtPath(Folder + "/" + MoonFile) as TextureImporter;
        if (moon == null)
        {
            throw new FileNotFoundException("Missing " + Folder + "/" + MoonFile);
        }
        moon.textureType = TextureImporterType.Default;
        moon.sRGBTexture = true;
        moon.alphaSource = TextureImporterAlphaSource.FromInput;
        moon.alphaIsTransparency = true;
        moon.mipmapEnabled = true;
        moon.wrapMode = TextureWrapMode.Clamp;
        moon.filterMode = FilterMode.Trilinear;
        moon.npotScale = TextureImporterNPOTScale.None;
        moon.textureCompression = TextureImporterCompression.Uncompressed;
        moon.assetBundleName = BundleName;
        moon.SaveAndReimport();

        Directory.CreateDirectory(OutputFolder);
        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { Folder + "/" + Shaders[0], Folder + "/" + Shaders[1], Folder + "/" + MoonFile },
        };
        BuildPipeline.BuildAssetBundles(OutputFolder, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

        string built = Path.Combine(OutputFolder, BundleName);
        if (!File.Exists(built))
        {
            throw new System.Exception("The bundle was not built.");
        }
        File.Copy(built, Path.Combine(ModResourcesFolder, BundleName), true);
        Debug.Log($"Built {BundleName} ({new FileInfo(built).Length / 1024} KB)");
    }
}
