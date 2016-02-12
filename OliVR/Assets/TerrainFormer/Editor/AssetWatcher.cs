using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class AssetWatcher : AssetPostprocessor {
        public static Action<string[]> OnAssetsImported;
        public static Action<string[], string[]> OnAssetsMoved;
        public static Action<string[]> OnAssetsDeleted;
        
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssetsDestination, string[] movedAssetsSource) {
            if(OnAssetsImported != null && importedAssets != null) {
                OnAssetsImported(importedAssets);
            }

            if(OnAssetsMoved != null && movedAssetsSource != null) {
                OnAssetsMoved(movedAssetsSource, movedAssetsDestination);
            }

            if(OnAssetsDeleted != null && deletedAssets != null) {
                OnAssetsDeleted(deletedAssets);
            }
        }

        private void OnPreprocessTexture() {
            // Return if the BrushCollection hasn't been initialized prior to this method being called
            if(string.IsNullOrEmpty(BrushCollection.localCustomBrushPath)) return;

            if(assetPath.StartsWith(BrushCollection.localCustomBrushPath)) {
                TextureImporter textureImporter = (TextureImporter)assetImporter;
                if(textureImporter.textureType != TextureImporterType.Advanced || textureImporter.isReadable == false ||
                    textureImporter.wrapMode != TextureWrapMode.Clamp || textureImporter.textureFormat != TextureImporterFormat.AutomaticTruecolor) {
                    textureImporter.textureType = TextureImporterType.Advanced;
                    textureImporter.isReadable = true;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;
                    textureImporter.textureFormat = TextureImporterFormat.AutomaticTruecolor;
                }
            }
        }
    }
}
