using UnityEditor;

namespace BorrowedSeconds.EditorTools
{
    /// <summary>UI renders from Blender (medals, padlock): crisp, uncompressed, mipmapped for downscaling.</summary>
    public class UiTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/UI/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.filterMode = UnityEngine.FilterMode.Trilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
