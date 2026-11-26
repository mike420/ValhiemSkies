using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the valheim_physical_sky asset bundle for ValheimSkies: the Physical Sky shader only.
// Menu: Valheim Skies > Build Physical Sky Bundle (or -executeMethod PhysicalSkyBundleBuilder.Build).
// The sources live in the mod's repository (unity/PhysicalSky) and are copied here by tools/build_physical_sky.ps1.
public static class PhysicalSkyBundleBuilder
{
    private const string Folder = "Assets/ValheimSkies/PhysicalSky";
    private const string OutputFolder = "AssetBundles/ValheimPhysicalSky";
    private const string BundleName = "valheim_physical_sky";
    private const string ShaderFile = "PhysicalSky.shader";
    private const string ModResourcesFolder = @"E:\Valheim_Mods\ValheimSkies\ValheimSkies\Resources";

    [MenuItem("Valheim Skies/Build Physical Sky Bundle")]
    public static void Build()
    {
        AssetImporter importer = AssetImporter.GetAtPath(Folder + "/" + ShaderFile);
        if (importer == null)
        {
            throw new FileNotFoundException("Missing " + Folder + "/" + ShaderFile);
        }
        importer.assetBundleName = BundleName;
        importer.SaveAndReimport();

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/" + ShaderFile);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
        {
            throw new System.Exception("The Physical Sky shader has errors; see the console.");
        }

        Directory.CreateDirectory(OutputFolder);
        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { Folder + "/" + ShaderFile },
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
