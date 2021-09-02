using UnityEditor;
using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class Build {
    static void OSXIntel64() {
#if UNITY_2017_3_OR_NEWER
        var buildTarget = BuildTarget.StandaloneOSX;
#else
		var buildTarget = BuildTarget.StandaloneOSXIntel64;
#endif
        build(GetBuildName(), buildTarget);
    }

    static string GetBuildName() {
        return Environment.GetEnvironmentVariable("UNITY_BUILD_NAME");
    }

    static void Linux64() {
        build(GetBuildName(), BuildTarget.StandaloneLinux64);
    }

    static void WebGL() {
        build(GetBuildName(), BuildTarget.WebGL);
    }

    static void build(string buildName, BuildTarget target) {
        var defines = GetDefineSymbolsFromEnv();
        if (defines != "") {
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, GetDefineSymbolsFromEnv());
        }

        List<string> scenes = GetScenes();
        foreach (string scene in scenes) {
            Debug.Log("Adding Scene " + scene);
        }
        BuildPipeline.BuildPlayer(scenes.ToArray(), buildName, target, BuildOptions.StrictMode | BuildOptions.UncompressedAssetBundle);
    }

    private static List<string> GetScenes() {
        List<string> envScenes = GetScenesFromEnv();
        if (envScenes.Count > 0) {
            return envScenes;
        } else {
            return GetAllScenePaths();
        }
    }

    private static List<string> GetAllScenePaths() {
        List<string> files = new List<string>();
        List<string> scenes = new List<string>();
        files.AddRange(Directory.GetFiles("Assets/Scenes/"));

        if (IncludePrivateScenes()) {
            files.AddRange(Directory.GetFiles("Assets/Private/Scenes/"));
        }

        foreach (string f in files) {
            if (f.EndsWith(".unity")) {
                scenes.Add(f);
            }
        }

        // uncomment for faster builds for testing
        return scenes;//.Where(x => x.Contains("FloorPlan1_")).ToList();
    }

    private static List<string> GetScenesFromEnv() {
        if (Environment.GetEnvironmentVariables().Contains(("BUILD_SCENES"))) {
            return Environment.GetEnvironmentVariable("BUILD_SCENES").Split(',').Select(
                x => "Assets/Scenes/" + x + ".unity"
            ).ToList();
        } else {
            return new List<string>();
        }
    }

    private static bool IncludePrivateScenes() {
        string privateScenes = Environment.GetEnvironmentVariable("INCLUDE_PRIVATE_SCENES");

        return privateScenes != null && privateScenes.ToLower() == "true";
    }

    private static string GetDefineSymbolsFromEnv() {
        return Environment.GetEnvironmentVariable("DEFINES");
    }
}
