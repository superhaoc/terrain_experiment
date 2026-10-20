using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class MeshUtils
{

    [MenuItem("GPUTerrain/Create Mesh Patch")]
    public static void CreateTerrainPatch()
    {

        
        var mesh = Utils.CreateTerrainPatchMesh();

          string path = GetSelectedDir();
            path += "/Plane1.mesh";
            AssetDatabase.CreateAsset(mesh,path);
            AssetDatabase.Refresh();

    }


    public static string GetSelectedDir(){
            string path = null;
            if(Selection.activeObject){
                path = AssetDatabase.GetAssetPath(Selection.activeObject);
            }
            if(!string.IsNullOrEmpty(path)){
                if(!System.IO.Directory.Exists(path)){
                    path = System.IO.Path.GetDirectoryName(path);
                }
            }
            if(string.IsNullOrEmpty(path)){
                path = "Assets";
            }
            return path;
        }


}
