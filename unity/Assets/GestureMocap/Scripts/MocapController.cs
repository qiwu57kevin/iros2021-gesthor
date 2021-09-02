using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;
using UnityStandardAssets.Characters.FirstPerson;

public class MocapController : MonoBehaviour
{
    [Tooltip("The maximum frame the animation is recorded.")]
	public int maxFrameCount = 100;
	[Tooltip("The position this recording is referring to.")]
	public Vector3 positionReferredTo = Vector3.zero;
    private KinectFbxRecorder motionRecorder;
    public GameObject targetIndicator;
    public GameObject currentIndicator;

    [Header("Voice Recognition")]
    public ConfidenceLevel confidence = ConfidenceLevel.Low;
    public Text infoText;
    protected DictationRecognizer dictationRecognizer;

    // private bool isUserSpeaking = false;
    private bool isRecording = false;

    private string filename = "";
    private GestureRecording gestureRecording;
    private AudioRecorder audioRecorder;
    private Camera recordingCam;

    private Transform human;
    public Transform target;
    public string targetID;
    public SimObjType targetSimObjType;
    public TargetObjType targetObjType;

    private string sceneName;
    private int sceneNum;
    private Mode mode = Mode.none;

    private Dictionary<string, GameObject> selectableObjects;
    private string[] selectableObjIDs;
    private Vector3[] selectablePositions;

    public int recordingCount = 0; // recording count is used to keep track of the number of recordings that should be done for each scene
    private int targetCount;
    public int maxRecordingCount = 10;

    private void Start() 
    {
        audioRecorder = FindObjectOfType<AudioRecorder>();
        recordingCam = GameObject.Find("headcam").GetComponent<Camera>();

        human = GameObject.Find("HumanMocapAnimator").transform;

        sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Split('_')[0];
        sceneNum = int.Parse(sceneName.Substring(9));
        if(sceneNum%100<=20) mode = Mode.train; // Check if we should save recordings as in train/val/test dataset
        else if(sceneNum%100<=25) mode = Mode.val;
        else if(sceneNum%100<=30) mode = Mode.test;
        if (mode == Mode.none)
        {
            Debug.LogError("You must have a valid scenen number between 1-30!");
            return;
        }

        // selectableObjects = FindObjectOfType<PhysicsSceneManager>().SpawnedObjects.Where(type => Enum.IsDefined(typeof(TargetObjType), target.GetComponent<SimObjPhysics>().ObjType.ToString()) && type.transform.parent.name!="Objects").ToList();
        selectableObjects = FindObjectOfType<PhysicsSceneManager>().ObjectIdToSimObjPhysics.Where(item => Enum.GetNames(typeof(TargetObjType)).Any(s => item.Key.ToLower().Contains(s.ToLower()))).ToDictionary(item => item.Key, item => item.Value.gameObject);
        targetCount = selectableObjects.Count;
        selectableObjIDs = selectableObjects.Keys.ToList().Shuffle_().ToArray();

        // Get selectable positions for the human
        selectablePositions = GameObject.Find("FPSController").GetComponent<PhysicsRemoteFPSAgentController>().getReachablePositions();

        // Initialize the motion recorder
        motionRecorder = FindObjectOfType<KinectFbxRecorder>();
        motionRecorder.maxFrameCount = maxFrameCount;
        motionRecorder.positionReferredTo = positionReferredTo;

        // Initialize dictation recognizer
        StartDictationEngine();

        if(!SelectTarget()) return;
        infoText.text = $" This is the No. {recordingCount+1} recording. \n Please speak an instruction with {targetObjType.ToString()}: \n You can choose a verb from the following: {String.Join(", ", verbs)}";
    }

    /// <summary>
    /// Hypotethis are thrown super fast, but could have mistakes.
    /// </summary>
    /// <param name="text"></param>
    private void DictationRecognizer_OnDictationHypothesis(string text)
    {
        // if (isRecording)
        // {
        //     infoText.text = text;
        // }
    }

    /// <summary>
    /// thrown when engine has some messages, that are not specifically errors
    /// </summary>
    /// <param name="completionCause"></param>
    private void DictationRecognizer_OnDictationComplete(DictationCompletionCause completionCause)
    {
        if (completionCause != DictationCompletionCause.Complete)
        {
            Debug.LogWarningFormat("Dictation completed unsuccessfully: {0}.", completionCause);


            switch (completionCause)
            {
                case DictationCompletionCause.TimeoutExceeded:
                case DictationCompletionCause.PauseLimitExceeded:
                    //we need a restart
                    CloseDictationEngine();
                    StartDictationEngine();
                    break;

                case DictationCompletionCause.UnknownError:
                case DictationCompletionCause.AudioQualityFailure:
                case DictationCompletionCause.MicrophoneUnavailable:
                case DictationCompletionCause.NetworkFailure:
                    //error without a way to recover
                    CloseDictationEngine();
                    break;

                case DictationCompletionCause.Canceled:
                    //happens when focus moved to another application 

                case DictationCompletionCause.Complete:
                    CloseDictationEngine();
                    StartDictationEngine();
                    break;
            }
        }
    }

    /// <summary>
    /// Resulted complete phrase will be determined once the person stops speaking. the best guess from the PC will go on the result.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="confidence"></param>
    private void DictationRecognizer_OnDictationResult(string text, ConfidenceLevel confidence)
    {
        // if (!isRecording)
        if (true)
        {
            if(text == "start")
            {
                // infoText.text = "Complete sentence is: ";

                isRecording = true;

                filename = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_mocap";
                gestureRecording = new GestureRecording();

                // audioRecorder.StartRecording();
                recordingCount += 1;
                
                motionRecorder.StartRecording(filename, mode, ref gestureRecording);

                if (!SelectTarget()) {infoText.text="Target not selected"; return;}
                infoText.text =  $"You are now in scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}" + $" \n This is the No. {recordingCount+1} recording." + $"\n The current target is {targetObjType.ToString()}:";
            }

            if(text == "stop")
            {
                // Terminate recording
                if (motionRecorder.IsRecording())
                {
                    motionRecorder.StopRecording(false);
                    maxRecordingCount += 1; // we need one more recording
                }
            }
        }
        else
        {
            // // Check if the recorded instruction contains the target
            // if (!CheckInsturctionWithTarget(text))
            // {
            //     infoText.text = $"Your instruction does not contain the {targetObjType.ToString()}. Please start again.";
            //     audioRecorder.RestartRecording();
            //     return;
            // }

            // infoText.text = $"Complete sentence is: <b>{text}</b>";
            // gestureRecording.instruction = text;

            // if (isRecording)
            // {
            //     isRecording = false;

            //     recordingCount += 1;
                
            //     motionRecorder.StartRecording(filename, mode, ref gestureRecording);

            //     if (!SelectTarget()) {infoText.text="Target not selected"; return;}
            //     infoText.text = $" The instruction you just spoke is: {text}. \n This is the No. {recordingCount+1} recording. \n Please speak an instruction with {targetObjType.ToString()}: \n You can choose a verb from the following: {String.Join(", ", verbs)}";
            // }
        }
    }

    public void LogAllInfo()
    {
        CamCapture(recordingCam, filename, ref gestureRecording);
        // audioRecorder.Save(ref gestureRecording, mode, filename);
        LogEnvironmentInfo(ref gestureRecording);
        SaveRecording(filename, gestureRecording);
    }


    /// <summary>
    /// Switch to the next scene in build settings when the number of recordings is satisfied.
    /// </summary>
    public void SwitchScene()
    {
        // Check if the maximum number of recording is reached
        if (recordingCount >= maxRecordingCount)
        {
            // Switch to the next scene
            int nextSceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            if (nextSceneIndex==UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings-1)
            {
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            }
            CloseDictationEngine();
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneIndex+1);
        }
    }

    private bool SelectTarget()
    {
        // Destroy current dicator first
        if (currentIndicator) Destroy(currentIndicator);
        targetID = selectableObjIDs[recordingCount%targetCount];
        target = selectableObjects[targetID].transform;
        targetObjType = (TargetObjType)Enum.Parse(typeof(TargetObjType), Enum.GetNames(typeof(TargetObjType)).Where(s => target.GetComponent<SimObjPhysics>().Type.ToString().ToLower().Contains(s.ToLower())).ToArray()[0]);

        if(target == null) 
        {
            infoText.text = "Cannot find a target object!";
            return false;
        }
        // Instantiate a target indicator for recording
        currentIndicator = Instantiate(targetIndicator, target.position, Quaternion.identity);
        return true;
    }

    private bool CheckInsturctionWithTarget(string text)
    {
        text = text.ToLower();
        return text.Contains(targetObjType.ToString().ToLower());
    }

    private void LogEnvironmentInfo(ref GestureRecording recording)
    {
        // Log room type and number
        sceneNum = int.Parse(sceneName.Substring(9));
        recording.sceneNum = sceneNum;
        if (sceneNum <= 30) recording.sceneType = "Kitchen";
        else if (sceneNum <= 300) recording.sceneType = "LivingRoom";
        else if (sceneNum <= 400) recording.sceneType = "Bedroom";
        else if (sceneNum <= 500) recording.sceneType = "Bathroom";
        recording.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // Log human position and orientation
        recording.humanPos = human.position/10f;
        recording.humanRot = human.rotation.eulerAngles.y/360f;

        // Log target information
        recording.targetPos = target.position/10f;
        recording.targetID = targetID;
        recording.targetType = targetObjType.ToString();
        recording.targetSimObjType = target.GetComponent<SimObjPhysics>().Type.ToString();
        recording.targetToHuman = recording.targetPos - recording.humanPos;

        string[] actions = new string[]{"go to ", "bring ", "take ", "come to ", "find "};
        string[] preps = new string[]{"this ", "that ", "the "};
        recording.instruction = actions[UnityEngine.Random.Range(0, actions.Length)] + preps[UnityEngine.Random.Range(0, preps.Length)] + recording.targetType.ToLower();
    }

    private void SelectTargetFromInstruction(string sentence)
    {
        // Assume the last word of the sentence is the target
        string targetStr = sentence.Split(' ').Last();
    }

    private void DictationRecognizer_OnDictationError(string error, int hresult)
    {
        Debug.LogErrorFormat("Dictation error: {0}; HResult = {1}.", error, hresult);
    }


    private void OnApplicationQuit()
    {
        CloseDictationEngine();
    }

    private void StartDictationEngine()
    {
        isRecording = false;

        dictationRecognizer = new DictationRecognizer(confidence);

        dictationRecognizer.DictationHypothesis += DictationRecognizer_OnDictationHypothesis;
        dictationRecognizer.DictationResult += DictationRecognizer_OnDictationResult;
        dictationRecognizer.DictationComplete += DictationRecognizer_OnDictationComplete;
        dictationRecognizer.DictationError += DictationRecognizer_OnDictationError;

        dictationRecognizer.Start();
    }

    private void CloseDictationEngine()
    {
        if (dictationRecognizer != null)
        {
            dictationRecognizer.DictationHypothesis -= DictationRecognizer_OnDictationHypothesis;
            dictationRecognizer.DictationComplete -= DictationRecognizer_OnDictationComplete;
            dictationRecognizer.DictationResult -= DictationRecognizer_OnDictationResult;
            dictationRecognizer.DictationError -= DictationRecognizer_OnDictationError;

            if (dictationRecognizer.Status == SpeechSystemStatus.Running)
                dictationRecognizer.Stop();
            
            dictationRecognizer.Dispose();
        }
    }

    public bool RandomizeHuman(Transform human)
    {
        KinectManager km = KinectManager.Instance;
        km.enabled = false;
        if (human == null || selectablePositions == null || target.position == null)
        {
            Debug.LogError("Cannot randomize the human position and rotation");
            return false;
        }
        // First, let's position the human in the selected positions
        Vector3 selectedPosition = selectablePositions[UnityEngine.Random.Range(0,selectablePositions.Length)];
        human.position = new Vector3(selectedPosition.x, human.position.y, selectedPosition.z);
        // Second, let's make the human face the target first
        human.LookAt(new Vector3(target.position.x, human.position.y, target.position.z));                 
        // Third, let's randomly rotate the human in (-90,90)
        human.Rotate(human.up, UnityEngine.Random.Range(-90f,90f));
        km.enabled = true;
        FindObjectOfType<AvatarController>().ResetInitialTransform(); // TODO: need to find another way to make it work
        return true;
    }

    // Capture camera image
    public void CamCapture(Camera Cam, string filename, ref GestureRecording recording)
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture rt = new RenderTexture(224, 224, 24);
        Cam.targetTexture = rt;
        RenderTexture.active = Cam.targetTexture;
 
        Cam.Render();
 
        Texture2D Image = new Texture2D(Cam.targetTexture.width, Cam.targetTexture.height);
        Image.ReadPixels(new Rect(0, 0, Cam.targetTexture.width, Cam.targetTexture.height), 0, 0);
        Image.Apply();
        RenderTexture.active = currentRT;
        Cam.targetTexture = null;
 
        var Bytes = Image.EncodeToPNG();
        Destroy(Image);
 
        File.WriteAllBytes(Application.dataPath + $"/GestureMocap/Recordings/{mode.ToString()}/images/" + filename + ".png", Bytes);
        recording.image += filename + ".png";
    }

    // Save GestureRecording object as a JSON file format
    public void SaveRecording(string fileName, GestureRecording recording)
    {
        string path = Application.dataPath + $"/GestureMocap/Recordings/{mode.ToString()}/summaries/" + filename + ".json";
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.WriteAllText(path, JsonUtility.ToJson(recording));
    }

    private static string[] verbs = new string[]{
        "Go", "Bring", "Take", "Pick", "Clean", "Open", "Close", "Turn", "Find", "Remove", "Want", "Need"
    };
}

/// <summary>
/// Object type that can be selected as the target
/// </summary>
public enum TargetObjType: int
{
    // Kitchen (9)
    Apple,
	Tomato,
	Bread,
	Knife,
	Fork,
	Spoon,
	Potato,
	Plate,
	Cup,
    // LivingRoom (4)
    Television,
    Newspaper,
    Remote,
    Sofa,
    // Bedroom (3)
    Clock,
    Bed,
    Poster,
    // BasketBall,
    // Bathroom (3)
    Plunger,
    Paper,
    Towel,
    Shower,
    // All (7)
    Box,
    Chair,
    Window,
    Table,
    Lamp,
    Laptop,
}

// The current mode of recording
public enum Mode
{
    none,
    train,
    val,
    test,
}
