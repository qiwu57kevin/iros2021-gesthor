using UnityEngine;
using System.Collections;

public class KinectFbxController : MonoBehaviour 
{
	private SpeechManager speechManager;
	private KinectFbxRecorder mocapRecorder;


	// void Start()
	// {
	// 	mocapRecorder = GetComponent<KinectFbxRecorder>();
	// }

	// void Update () 
	// {
	// 	if(speechManager == null)
	// 	{
	// 		speechManager = SpeechManager.Instance;
	// 	}

	// 	// use speech recognizer to control the mocap recorder
	// 	if(speechManager != null && speechManager.IsSapiInitialized())
	// 	{
	// 		if(speechManager.IsPhraseRecognized())
	// 		{
	// 			string sPhraseTag = speechManager.GetPhraseTagRecognized();
				
	// 			switch(sPhraseTag)
	// 			{
	// 				case "RECORD":
	// 					if(mocapRecorder)
	// 					{
							
	// 						mocapRecorder.StartRecording();
	// 					}
	// 					break;
						
	// 				case "STOP":
	// 					if(mocapRecorder)
	// 					{
	// 						mocapRecorder.StopRecording();
	// 					}
	// 					break;

	// 			}
				
	// 			speechManager.ClearPhraseRecognized();
	// 		}
	// 	}

	// 	// alternatively, use the Jump-button (space-key)
	// 	if(Input.GetButtonDown("Jump"))
	// 	{
	// 		if(mocapRecorder)
	// 		{
	// 			if(!mocapRecorder.IsRecording())
	// 			{
	// 				mocapRecorder.StartRecording();
	// 			}
	// 			else
	// 			{
	// 				mocapRecorder.StopRecording();
	// 			}
	// 		}
	// 	}

	// }

}
