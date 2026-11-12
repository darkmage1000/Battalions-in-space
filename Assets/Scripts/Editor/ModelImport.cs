using UnityEditor;
using UnityEngine;

namespace BIS.EditorTools
{
    /// <summary>
    /// Import settings for the CC0 models in Resources/Models: meshes stay readable (ImportedModels combines and
    /// recolours them at run time), no cameras / lights / animation on static props, textures capped at 1024 px.
    /// </summary>
    public class ModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Contains("Resources/Models/")) return;
            var m = (ModelImporter)assetImporter;
            m.isReadable = true;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            if (!assetPath.Contains("/Characters/")) { m.importAnimation = false; m.animationType = ModelImporterAnimationType.None; return; }
            // Quaternius Universal Animation Library: Humanoid rig, axis conversion baked (the pack's own setup notes)
            m.bakeAxisConversion = true;
            m.animationType = ModelImporterAnimationType.Human;
            m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            m.importAnimation = true;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Contains("Resources/Models/Characters/")) return;
            var m = (ModelImporter)assetImporter;
            var clips = m.defaultClipAnimations;
            foreach (var c in clips)
            {
                bool loop = c.name.EndsWith("_Loop");
                c.loopTime = loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = false; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;   // movement comes from the game, not the clip
            }
            m.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            // HUD pictures (crosshairs, icons): crisp, see-through, never tiled
            if (assetPath.Contains("Resources/UI/"))
            {
                var u = (TextureImporter)assetImporter;
                u.textureType = TextureImporterType.Default;
                u.alphaIsTransparency = true; u.mipmapEnabled = false; u.wrapMode = TextureWrapMode.Clamp;
                u.npotScale = TextureImporterNPOTScale.None; u.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!assetPath.Contains("Resources/Models/")) return;
            var t = (TextureImporter)assetImporter;
            t.maxTextureSize = 1024;
            t.mipmapEnabled = true;
            // palette textures whose colours are read per vertex at run time (GunModels): readable, exact colours
            if (assetPath.Contains("/Guns/")) { t.isReadable = true; t.mipmapEnabled = false; t.filterMode = FilterMode.Point; t.textureCompression = TextureImporterCompression.Uncompressed; }
        }
    }
}
