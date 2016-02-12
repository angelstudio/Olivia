using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class BrushCollection {
        internal const string defaultProceduralBrushName = "_DefaultProceduralBrush";

        internal static string globalCustomBrushPath;
        internal static string localCustomBrushPath;

        private Texture2D[] previewTextures;
        public Texture2D[] PreviewTextures {
            get {
                // TODO: We shouldn't have to check this everytime. Initialization should be done elsewhere
                //if(previewTextures == null) {
                //    UpdatePreviewTextures();
                //}
                return previewTextures;
            }
        }

        public SortedDictionary<string, TerrainBrush> brushes;
        
        public BrushCollection() {
            string mainDirectoryWithAssetsRemoved = TerrainFormerInspector.settings.mainDirectory.Remove(0, 6);
            globalCustomBrushPath = Path.Combine(Application.dataPath + mainDirectoryWithAssetsRemoved, "Textures/Brushes");
            localCustomBrushPath = globalCustomBrushPath.Remove(0, globalCustomBrushPath.IndexOf("Assets"));

            brushes = new SortedDictionary<string, TerrainBrush>();
            brushes.Add(defaultProceduralBrushName, new ProceduralBrush(defaultProceduralBrushName));
            RefreshCustomBrushes();
        }
        
        // The parameter UpdatedBrushes requires local Unity assets paths
        internal void RefreshCustomBrushes(string[] updatedBrushes = null) {
            // If there is no data on which brushes need to be updated, assume every brush must be updated
            if(updatedBrushes == null) {
                updatedBrushes = Directory.GetFiles(globalCustomBrushPath, "*", SearchOption.AllDirectories);

                for(int i = 0; i < updatedBrushes.Length; i++) {
                    updatedBrushes[i] = Utilities.GetLocalAssetPath(updatedBrushes[i]);
                }
            }
            
            // Get the custom brush textures
            foreach(string path in updatedBrushes) {
                if(path.EndsWith(".meta")) continue;
                
                Texture2D tex = AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) as Texture2D;
                if(tex == null) continue;
                
                TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(path);
                if(textureImporter.textureType != TextureImporterType.Advanced || textureImporter.isReadable == false ||
                    textureImporter.wrapMode != TextureWrapMode.Clamp || textureImporter.textureFormat != TextureImporterFormat.AutomaticTruecolor) {
                    textureImporter.textureType = TextureImporterType.Advanced;
                    textureImporter.isReadable = true;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;
                    textureImporter.textureFormat = TextureImporterFormat.AutomaticTruecolor;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    
                    // Reload the texture with the updated settings
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }

                if(tex.width != tex.height) continue;

                string brushName = Path.GetFileNameWithoutExtension(path);

                if(brushes.ContainsKey(brushName)) {
                    CustomBrush customBrush = brushes[brushName] as CustomBrush;
                    if(customBrush == null) continue;
                    customBrush.sourceTexture = tex;
                } else {
                    brushes.Add(brushName, new CustomBrush(brushName, tex));
                }
            }
        }
        
        internal void UpdatePreviewTextures() {
            previewTextures = new Texture2D[brushes.Count];

            int i = 0;
            foreach(TerrainBrush terrainBrush in brushes.Values) { 
                previewTextures[i] = terrainBrush.CreatePreviewTexture();
                i++;
            }
        }

        internal void RemoveDeletedBrushes(string[] deletedBrushes) {
            foreach(string deletedBrush in deletedBrushes) {
                string deletedBrushFilename = Path.GetFileNameWithoutExtension(deletedBrush);
                brushes.Remove(deletedBrushFilename);
            }
        }
    }
}
