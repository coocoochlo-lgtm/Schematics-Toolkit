using UnityEditor;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Sprocket's frames import at their own size (Unity's default rounds 76×92 to 64×128, squishing it), without
    /// mipmaps or smoothing, so new frames dropped into Resources/Textures/sprocket look right without any setup.
    /// </summary>
    internal sealed class SprocketTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Textures/sprocket/"))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = UnityEngine.FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
