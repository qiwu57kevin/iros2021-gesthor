using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using sceneManager = UnityEngine.SceneManagement.SceneManager;
using UnityStandardAssets.Characters.FirstPerson;

public class GenerateMenuItem : MonoBehaviour 
{
    [MenuItem("MyMenu/Add MocapModule to Scenes in Build Settings")]
    static void AddMocapModule()
    {
        var selection = new List<string>();
        selection.AddRange(GetSceneNames(1, 30));
        selection.AddRange(GetSceneNames(201, 230));
        selection.AddRange(GetSceneNames(301, 330));
        selection.AddRange(GetSceneNames(401, 430));

        foreach(string sceneName in selection)
        {
            EditorSceneManager.OpenScene(sceneName);
            GameObject mocapModule = GameObject.Find("MocapModule");
            if (mocapModule == null)
            {
                mocapModule = AssetDatabase.LoadAssetAtPath("Assets/GestureMocap/Prefabs/MocapModule.prefab", typeof(GameObject)) as GameObject;
                // GameObject spawnedObj = Instantiate(mocapModule, Vector3.zero, Quaternion.identity);
                GameObject spawnedObj = PrefabUtility.InstantiatePrefab(mocapModule) as GameObject;
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }
        }
    }   

    private static List<string> GetSceneNames(int startIndex, int lastIndex, string nameTemplate = "", string pathPrefix = "Assets/Scenes") {
        var scenes = new List<string>();
        for (var i = startIndex; i <= lastIndex; i++) {

            var scene = pathPrefix + "/FloorPlan" + nameTemplate + i + "_physics.unity";
            scenes.Add(scene);

        }
        return scenes;
    } 
}
    