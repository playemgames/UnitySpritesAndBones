using UnityEngine;
using System.Collections;
using UnityEditor;
using System.IO;

/// <summary> Editor class to help managing assets. Should be in Editor folder. </summary>
static public class AssetUtility {
    /// <summary> Creates the given asset at path or replaces it if it exists in the path </summary>
    /// <typeparam name="T"> Type of the asset </typeparam>
    /// <param name="path"> Either full path or relative path from the Assets directory. Without any extension </param>
    static public void CreateOrReplaceAssetAtPath<T>(T asset, string path) where T : Object, new()
    {
        string fullDirectoryPath = Path.GetDirectoryName(path);
        fullDirectoryPath = Path.IsPathRooted(path) ? fullDirectoryPath : Path.GetFullPath(Path.Combine("Assets", fullDirectoryPath));

        if(!string.IsNullOrEmpty(fullDirectoryPath) && !Directory.Exists(fullDirectoryPath)) {
            Directory.CreateDirectory(fullDirectoryPath);
            Debug.Log("Creating directory at: " + fullDirectoryPath);
        }
        T outputAsset = AssetDatabase.LoadMainAssetAtPath(path) as T;
        if(outputAsset != null) {
            EditorUtility.CopySerialized(asset, outputAsset);
            AssetDatabase.SaveAssets();
        }
        else {
            outputAsset = Object.Instantiate(asset) as T;
            EditorUtility.CopySerialized(asset, outputAsset);
            AssetDatabase.CreateAsset(outputAsset, Path.Combine("Assets", path + ".asset"));
        }
    }
}