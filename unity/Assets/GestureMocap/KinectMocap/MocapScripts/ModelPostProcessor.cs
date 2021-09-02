using UnityEngine;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;


public class ModelPostProcessor : AssetPostprocessor 
{
	//void OnPostprocessModel(GameObject root)
	void OnPreprocessAnimation()
	{
		ModelImporter importer = assetImporter as ModelImporter;

		if (!importer || !importer.importAnimation)
			return;

		ModelImporterClipAnimation[] animations = importer.defaultClipAnimations;
		TakeInfo[] takeInfos = importer.importedTakeInfos;

		bool bAnimModified = false;
		for (int i = 0; i < animations.Length; i++) 
		{
			ModelImporterClipAnimation anim = animations [i];
			TakeInfo take = takeInfos [i];

			int firstFrame = Mathf.FloorToInt (take.startTime * take.sampleRate);
			int lastFrame = Mathf.FloorToInt (take.stopTime * take.sampleRate);

			if (anim.firstFrame != firstFrame || anim.lastFrame != lastFrame || !anim.loopTime) 
			{
				anim.firstFrame = firstFrame;
				anim.lastFrame = lastFrame;
				anim.loopTime = true;

				animations [i] = anim;
				bAnimModified = true;
			}
		}

		if (bAnimModified) 
		{
			importer.clipAnimations = animations;
		}
	}
}
#endif