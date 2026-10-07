using UnityEditor;

namespace BorrowedSeconds.EditorTools
{
    /// <summary>UI renders from Blender (medals, padlock): crisp, uncompressed, mipmapped for downscaling.
    /// The application icon (Assets/Icon/) is kept uncompressed at full size.</summary>
    public class UiTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith("Assets/Icon/"))
            {
                // the application icon: Unity downsizes it for each platform, so keep the source exact
                var icon = (TextureImporter)assetImporter;
                icon.textureType = TextureImporterType.Default;
                icon.alphaIsTransparency = true;
                icon.mipmapEnabled = false;
                icon.textureCompression = TextureImporterCompression.Uncompressed;
                icon.npotScale = TextureImporterNPOTScale.None;
                icon.maxTextureSize = 1024;
                return;
            }
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
