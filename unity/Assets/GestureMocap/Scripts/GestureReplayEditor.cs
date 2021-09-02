using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Barracuda;

[CustomEditor(typeof(GestureReplay))]
public class GestureReplayEditor : Editor
{
    public override void OnInspectorGUI()
    {
        GestureReplay mScript = (GestureReplay)target;

        mScript.sequence_length = EditorGUILayout.IntField("Sequence length", mScript.sequence_length);

        mScript.filePath = EditorGUILayout.TextField("Loaded file path", mScript.filePath);
        mScript.modelName = EditorGUILayout.TextField("Loaded model name", mScript.modelName);
        mScript.mode = (Mode)EditorGUILayout.EnumPopup("Train/Val/Test", mScript.mode);

        if(GUILayout.Button("Play Gesture from CSV File"))
        {
            if (mScript.LoadAnimation(mScript.filePath)) mScript.reapplyPoses = true;
        }

        if(GUILayout.Button("Play Gesture from Loaded ONNX File"))
        {
            if (mScript.LoadAnimationFromModelName(mScript.modelName)) mScript.reapplyPoses = true;
        }
    }
}
