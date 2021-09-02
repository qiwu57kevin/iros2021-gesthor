using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using System.Linq;


public class KinectFbxRecorder : MonoBehaviour 
{
	[Tooltip("Index of the player, tracked by this component. 0 means the 1st player, 1 - the 2nd one, 2 - the 3rd one, etc.")]
	public int playerIndex = 0;

	[Tooltip("Smooth factor used for avatar movements and joint rotations.")]
	public float smoothFactor = 10f;

	[Tooltip("Reference to the LeapMotion hand-pool, if you want to track the user hands with LeapMotion sensor.")]
	public Leap.Unity.HandPool leapMotionHandPool = null;

	[Tooltip("Whether to allow recording of LeapMotion-tracked hand animations only.")]
	public bool allowLeapMotionOnly = false;

	[Tooltip("Name of the recorded animation.")]
	public string animationName = string.Empty;

	[Tooltip("Unity scale factor of the loaded fbx model.")]
	public float importScaleFactor = 1f;
	
	[Tooltip("Output file format. Leave empty for the default format.")]
	public string outputFileFormat = string.Empty;

	[Tooltip("Time mode for the recorded animation - determines the frame rate.")]
	public MocapFbxWrapper.TimeMode animationTimeMode = MocapFbxWrapper.TimeMode.DefaultMode;

	[Tooltip("Maximum FPS (frame rate) for saving animation frames. The time between frames will be minimum 1/FPS seconds.")]
	public float maxFramesPerSecond = 0f;
	
	[Tooltip("UI-Text to display information messages.")]
	public UnityEngine.UI.Text infoText;
	
	[Tooltip("UI-Text to display voice recognition messages.")]
	public UnityEngine.UI.Text voiceText;
	
	[Tooltip("Whether to start recording, right after the scene starts.")]
	public bool recordAtStart = false;

	// the avatar object
	private GameObject avatarObject = null;

	// whether it is recording at the moment
	private bool isRecording = false;

	// reference to KinectManager
	private KinectManager manager = null;

	// reference to AvatarController
	private AvatarController avatarCtrl = null;

	// time variables used for recording
	private long lastBodyFrameTime = 0;
	private float fStartTime = 0f;
	private float fCurrentTime = 0f;
	private float fRelTime = 0f;
	private float fMinFrameTime = 0f;
	private int iCurrentFrame = 0;
	private int iSavedFrame = 0;
	private bool bScaleSaved = false;

	// unity scale factor
	private float fUnityScale = 0f;

	// fbx global time mode as FPS
	private float fGlobalFps = 0f;

	// fbx global scale factor
	private float fFbxScale = 0f;

	// if the fbx wrapper is available
	private bool bFbxAvailable = false;

	// if the fbx-system is successfully initialized
	private bool bFbxInited = false;

	// leap camera references
	//private Leap.Unity.LeapVRCameraControl leapCamControl = null;
	private Leap.Unity.LeapCameraImage leapImgRetriever = null;
	private Leap.Unity.LeapRiggedHand riggedLHand = null;
	private Leap.Unity.LeapRiggedHand riggedRHand = null;

	[Header("Record animation in a csv file")]
	// This part will save the animtion to a csv file
	private float[] currentMuscles; // an array containig current muscle values
    private float[,] animationHumanPoses; // stack all currentHumanPose in one array
	private int muscleCount;

	private HumanPose currentPose = new HumanPose(); // keeps track of currentPose while animated
    private HumanPose poseToSet; // reassemble poses from .csv data
    private Animator animator;
    private HumanPoseHandler poseHandler; // to record and retarget animation
    [Tooltip("The maximum frame the animation is recorded.")]
	public int maxFrameCount = 100;
	[Tooltip("The position this recording is referring to.")]
	public Vector3 positionReferredTo = Vector3.zero;

	private Mode mode = Mode.none;
	private MocapController mocapController;

    void Awake()
	{
		try 
		{
			// ensure the fbx wrapper is available
			bool bNeedRestart = false;
			if(MocapFbxWrapper.EnsureFbxWrapperAvailability(ref bNeedRestart))
			{
				bFbxAvailable = true;
				
				if(bNeedRestart)
				{
					//KinectInterop.RestartLevel(gameObject, "MF");
					return;
				}
			}
			else
			{
				throw new Exception("Fbx-Unity-Wrapper is not available!");
			}

			// set the time mode, if needed
			// if(animationTimeMode != MocapFbxWrapper.TimeMode.DefaultMode)
			// {
			// 	int iTimeMode = (int)animationTimeMode;
			// 	MocapFbxWrapper.SetGlobalTimeMode(iTimeMode, MocapFbxWrapper.FrameRates[iTimeMode]);
			// }

			avatarCtrl = FindObjectOfType<AvatarController>();
			avatarObject = avatarCtrl.gameObject;

			mocapController = FindObjectOfType<MocapController>();

			InstantiateHumanAvatar();
		} 
		catch (Exception ex) 
		{
			Debug.LogError(ex.Message);
			Debug.LogException(ex);
			
			if(infoText != null)
			{
				infoText.text = ex.Message;
			}
		}
	}
	

	void Start()
	{
		UnityEngine.XR.InputTracking.disablePositionalTracking = true;

		// check if fbx-wrapper is available
		if(!bFbxAvailable)
			return;

		try 
		{
			// check the KinectManager availability
			if(!manager)
			{
				manager = KinectManager.Instance;
			}

			if(!manager)
			{
				throw new Exception("KinectManager not found, probably not initialized. See the log for details");
			}

			// initialize fbx wrapper
			if(!bFbxInited)
			{
				bFbxInited = MocapFbxWrapper.InitFbxWrapper();
				
				if(!bFbxInited)
				{
					throw new Exception("Fbx wrapper could not be initialized.");
				}
			}
			
			if(infoText != null)
			{
				infoText.text = "Say 'Start' or Press 'Space' to start the recording.";
			}

			//Debug.Log("Global FPS: " + MocapFbxWrapper.GetGlobalFps());

			// if(recordAtStart)
			// {
			// 	StartRecording();
			// }
		} 
		catch (Exception ex) 
		{
			Debug.LogError(ex.Message);
			Debug.LogException(ex);
			
			if(infoText != null)
			{
				infoText.text = ex.Message;
			}
		}

		muscleCount = HumanTrait.MuscleCount; // count the number of muscles of the avatar
        animationHumanPoses = new float[maxFrameCount+10, muscleCount];
        currentMuscles = new float[muscleCount];

		animator = avatarObject.GetComponent<Animator>();
        poseHandler = new HumanPoseHandler(animator.avatar, avatarObject.gameObject.transform);
	}


	void Update () 
	{
		if(isRecording && animationName != string.Empty)
		{
			// save the body frame, if any
			if(manager /**&& manager.IsInitialized()*/)
			{
				long userId = manager.GetUserIdByIndex(playerIndex);
				long liFrameTime = manager.GetBodyFrameTimestamp();
				//long liFrameTime = (long)(Time.time * 1000);

				// check if new frame is available
				bool bNewFrameAvailable = !allowLeapMotionOnly ? userId != 0 && avatarCtrl && liFrameTime != lastBodyFrameTime : true; // here we should check for new LM-frame

				if(bNewFrameAvailable && Time.time >= (fCurrentTime + fMinFrameTime))
				{
					fCurrentTime = Time.time;
					fRelTime = fCurrentTime - fStartTime;
					iCurrentFrame = Mathf.FloorToInt(fRelTime * fGlobalFps);

					if(infoText)
					{
						infoText.text = string.Format("Recording @ {0:F3}... Press 'Space' again to stop the recorder.", fRelTime);
					}
					
					if(iSavedFrame < iCurrentFrame)
					{
						iSavedFrame = iCurrentFrame;  // skip saving multiple frames
						lastBodyFrameTime = liFrameTime;
					}

					// Save the content to a csv file
					if (iSavedFrame > 0)
					{
						poseHandler.GetHumanPose(ref currentPose);

						for (int i = 0; i < muscleCount; i++) 
						{
							animationHumanPoses[iSavedFrame-1, i] = Mathf.Clamp((float)System.Math.Round(currentPose.muscles[i],3), -1f, 1f); // round to 3 decimal places
						}

						if (iSavedFrame >= maxFrameCount)
						{
							StopRecording();
						}
					}
				}
			}
		}
	}

	public bool InstantiateHumanAvatar()
	{
		if(avatarObject != null)
		{
			AvatarController ac = avatarObject.GetComponent<AvatarController>();
			if(ac == null)
			{
				ac = avatarObject.AddComponent<AvatarController>();
				ac.playerIndex = playerIndex;

				ac.mirroredMovement = false;
				ac.verticalMovement = true;

				ac.smoothFactor = smoothFactor;

				//ac.Awake();
			}

			KinectManager km = KinectManager.Instance;
			if(km)
			{
				if(km.IsUserDetected(playerIndex))
				{
					ac.SuccessfulCalibration(km.GetUserIdByIndex(playerIndex), false);
				}

				km.refreshAvatarControllers();
			}

			if(leapMotionHandPool != null && leapMotionHandPool.gameObject.activeInHierarchy)
			{
				// setup left hand
				int boneIndex = ac.GetBoneIndexByJoint (KinectInterop.JointType.WristLeft, false);
				Transform transLeftHand = ac.GetBoneTransform(boneIndex);

				int headIndex = ac.GetBoneIndexByJoint (KinectInterop.JointType.Neck, false);
				Transform headTransform = ac.GetBoneTransform(headIndex);

				riggedLHand = transLeftHand.gameObject.GetComponent<Leap.Unity.LeapRiggedHand>();
				if(riggedLHand == null)
				{
					riggedLHand = transLeftHand.gameObject.AddComponent<Leap.Unity.LeapRiggedHand>();
					riggedLHand.Handedness = Leap.Unity.Chirality.Left;
					riggedLHand.smoothFactor = smoothFactor;
					riggedLHand.headTransform = headTransform;
					riggedLHand.SetupRiggedHand();
				}

				// fix left thumb's finger-pointing vector
				Leap.Unity.LeapRiggedFinger riggedLThumb = (Leap.Unity.LeapRiggedFinger)riggedLHand.fingers[(int)Leap.Finger.FingerType.TYPE_THUMB];
				if(riggedLThumb != null)
				{
					riggedLThumb.modelFingerPointing = new Vector3(-1f, 0f, 1f);
				}

				// setup right hand
				boneIndex = ac.GetBoneIndexByJoint (KinectInterop.JointType.WristRight, false);
				Transform transRightHand = ac.GetBoneTransform(boneIndex);

				riggedRHand = transRightHand.gameObject.GetComponent<Leap.Unity.LeapRiggedHand>();
				if(riggedRHand == null)
				{
					riggedRHand = transRightHand.gameObject.AddComponent<Leap.Unity.LeapRiggedHand>();
					riggedRHand.Handedness = Leap.Unity.Chirality.Right;
					riggedRHand.smoothFactor = smoothFactor;
					riggedRHand.headTransform = headTransform;
					riggedRHand.SetupRiggedHand();
				}

				// fix right thumb's finger-pointing vector
				Leap.Unity.LeapRiggedFinger riggedRThumb = (Leap.Unity.LeapRiggedFinger)riggedRHand.fingers[(int)Leap.Finger.FingerType.TYPE_THUMB];
				if(riggedRThumb != null)
				{
					riggedRThumb.modelFingerPointing = new Vector3(1f, 0f, 1f);
				}

				// add hands to the pool
				leapMotionHandPool.AddNewGroup("LeapRiggedHands", riggedLHand, riggedRHand);
				// leapMotionHandPool.Start();  // start the pool to fill in the models

				// set non-kinect hand control for avatar's hands
				ac.externalHandRotations = true;
			}
			else
			{
				// use Kinect hand controls
				ac.externalHandRotations = false;
				ac.fingerOrientations = true;
			}
		}

		return true;
	}

	/// <summary>
	/// Starts the recording.
	/// </summary>
	/// <returns><c>true</c>, if recording was started, <c>false</c> otherwise.</returns>
	public bool StartRecording(string filename, Mode recordingMode, ref GestureRecording recording)
	{
		animationName = filename;
		recording.motion += filename + ".csv";

		if(avatarObject == null)
			return false;

		if(isRecording)
			return false;

		isRecording = true;

		mode = recordingMode;
		
		if(isRecording)
		{
			Debug.Log("Recording started.");
			if(infoText != null)
			{
				infoText.text = $"Recording... Say 'Stop' or press 'Space' again to stop the recorder. The recorder will stop after {maxFrameCount} frames.";
			}

			// initialize times
			fStartTime = fCurrentTime = Time.time;
			fRelTime = 0f;
			fMinFrameTime = maxFramesPerSecond > 0f ? 1f / maxFramesPerSecond : 0f;

			iCurrentFrame = 0;
			iSavedFrame = -1;  // 0
			bScaleSaved = false;

			// get global time mode as fps
			fGlobalFps = MocapFbxWrapper.GetGlobalFps();
			Debug.Log("Global FPS: " + fGlobalFps);

			// get global scale factor
			fFbxScale = 100f / MocapFbxWrapper.GetGlobalScaleFactor();

			// Unity scale factor
			fUnityScale = importScaleFactor != 0f ? 1f / importScaleFactor : 1f;
		}

		return isRecording;
	}


	/// <summary>
	/// Stops the recording.
	/// </summary>
	public void StopRecording(bool saveRecording = true)
	{
		if(isRecording)
		{
			if(saveRecording)
			{
				// Save the animation as csv
				SaveAnimation(animationName);
				mocapController.LogAllInfo();
			}

			// SaveOutputFileIfNeeded();
			isRecording = false;

			Debug.Log("Recording stopped.");
			if(infoText != null)
			{
				infoText.text = "Recording stopped.";
			}

			// Randomly position the agent
            mocapController.RandomizeHuman(GameObject.Find("HumanMocapAnimator").transform);
		}

		if(infoText != null)
		{
			if (saveRecording)
			{
				infoText.text = "All info saved correctly. Say 'Start' or press 'Space' to start the recorder.";
			}
			else
			{
				infoText.text = "Recording is interrupted. Say 'Start' or press 'Space' to start the recorder.";
			}
		}

		if (voiceText != null)
		{
			// voiceText.text = "N/A";
		}

		// Switch scene
		if (mocapController.recordingCount >= mocapController.maxRecordingCount)
		{
			mocapController.SwitchScene();
			// UnityEditor.EditorApplication.isPlaying = false;
		}
	}

	/// <summary>
	/// Determines whether file recording is in progress at the moment
	/// </summary>
	/// <returns><c>true</c> if file recording is in progress; otherwise, <c>false</c>.</returns>
	public bool IsRecording()
	{
		return isRecording;
	}


	/// <summary>
	/// Gets the avatar controller.
	/// </summary>
	/// <returns>The avatar controller.</returns>
	public AvatarController GetAvatarController()
	{
		return avatarCtrl;
	}

	// ----- end of public functions -----

	void OnDestroy()
	{
		// check if fbx-wrapper is available
		if(!bFbxAvailable)
			return;

		// finish recording, if needed
		isRecording = false;
		
		// terminate the fbx wrapper
		if(bFbxInited)
		{
			MocapFbxWrapper.TermFbxFrapper();
			bFbxInited = false;
		}

	}

	// Save entire animation as a csv file
    public void SaveAnimation(string filename)
    {
        string path = Application.dataPath + $"/GestureMocap/Recordings/{mode.ToString()}/motions/" + filename + ".csv";
        TextWriter sw = new StreamWriter(path);
        string line;

		// Check if there is any frame missing
		int validFrame = -1;
		int invalidFrame = -1;
		bool needRewrite = false;
		for (int frame = 0; frame < maxFrameCount; frame++)
		{
			if (Mathf.Abs(animationHumanPoses[frame, 0]) >= 0.001f) 
			{
				validFrame = frame; 
				if (needRewrite)
				{
					for (int _frame = invalidFrame; _frame < validFrame; _frame++)
					{
						for (int i = 0; i < muscleCount; i++) // and all values composing one Pose
						{
							animationHumanPoses[_frame, i] = animationHumanPoses[validFrame, i];
						}
					}
					needRewrite = false;
				}
				invalidFrame = validFrame;
			}
			else if (validFrame == frame-1)
			{
				invalidFrame = frame;
				needRewrite = true;
			}
		}

		// Add head line
		sw.WriteLine(String.Join(";",Enumerable.Range(0,muscleCount)));
        for (int frame = 0; frame < maxFrameCount; frame++) // run through all frames 
        {
            // line = $"{positionReferredTo.x};{positionReferredTo.y};{positionReferredTo.z};";
			line = "";
            for (int i = 0; i < muscleCount; i++) // and all values composing one Pose
            {
                line = line + animationHumanPoses[frame, i].ToString() + (i==muscleCount-1? "":";");
            }
            sw.WriteLine(line);
        }
        sw.Close();
    }
}
