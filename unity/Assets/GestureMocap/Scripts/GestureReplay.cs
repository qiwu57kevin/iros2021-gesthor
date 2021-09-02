using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

public class GestureReplay : MonoBehaviour
{
    public bool reapplyPoses;
    public string filePath;
    public NNModel modelAsset;
    public string modelName;
    private Model m_RuntimeModel;
    private HumanPose poseToSet; // reassemble poses from .csv data
    private HumanPoseHandler poseHandler; // to record and retarget animation
    private int muscleCount;
    private float[] currentMuscles; // an array containig current muscle values
    private float[,] animationHumanPoses; // stack all currentHumanPose in one array
    private int counterPlay = 0; // count animation playback frames
    private int counterLoad = 0; // count number of frames of loaded animation
    private Vector3 initialPos;
    private Quaternion initialRot;
    private GameObject humanMocapAnimator;
    public int sequence_length = 100;

    // replay from neural network
    // public NNModel model;
    // private Model m_RuntimeModel;
    public Mode mode = Mode.train;


    void FixedUpdate()
    {
        if (reapplyPoses) { reapplyPosesAnimation();}
    }

    IEnumerator Pause(float pauseTime)
    {
        yield return new WaitForSeconds(pauseTime);
    }

    private void Start() 
    {
        humanMocapAnimator = GameObject.Find("Malcolm");
        Animator animator = humanMocapAnimator.GetComponent<Animator>();
        poseHandler = new HumanPoseHandler(animator.avatar, humanMocapAnimator.transform);

        muscleCount = HumanTrait.MuscleCount; // count the number of muscles of the avatar
        currentMuscles = new float[muscleCount]; 

        // Model model = ModelLoader.Load($"{Application.dataPath}/Gesture_Seq/gesture_seq_pred.onnx");

        // Get inital position and rotation
        poseHandler.GetHumanPose(ref poseToSet);
        initialPos = poseToSet.bodyPosition;
        initialRot = poseToSet.bodyRotation; 
    }

    // Refill animationHumanPoses with values from loaded csv files
    public bool LoadAnimation(string loadedFile)
    {
        animationHumanPoses = new float[sequence_length, muscleCount];
        
        // Disable body controller
        // humanMocapAnimator.GetComponent<Animator>().runtimeAnimatorController = null;

        string path = Directory.GetCurrentDirectory();
        // path = path + $"/Assets/GestureMocap/Recordings/{mode.ToString()}/motions/" + (loadedFile.EndsWith(".csv")? loadedFile:(loadedFile+".csv"));
        path = path + (loadedFile.EndsWith(".csv")? loadedFile:(loadedFile+".csv"));

        try
        {
            StreamReader sr = new StreamReader(path);
            int frame = 0;
            string[] line;
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine().Split(';');
                if(frame != 0)
                {
                    for (int muscleNum = 0; muscleNum < line.Length - 1; muscleNum++)
                    {
                        animationHumanPoses[frame-1, muscleNum] = float.Parse(line[muscleNum]);
                    }
                }
                frame++;
            }
            counterLoad = frame-1;
            return true;
        }
        catch
        {
            Debug.LogError($"File at path {path} is not found. Please specify a correct path.");
            return false;
        }

    }

    public bool LoadAnimation(NNModel model)
    {
        var worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, m_RuntimeModel);
        var inputs = new Dictionary<string, Tensor>();
        inputs["input_3"] = new Tensor(1, 1, 1, 32);
        inputs["input_4"] = new Tensor(1, 1, 1, 3);

        for(int i=0;i<32;i++) inputs["input_3"][0,0,0,i] = 0f;
        for(int i=0;i<3;i++) inputs["input_4"][0,0,0,i] = 0.25f;

        worker.Execute(inputs);

        Tensor output = worker.PeekOutput();
        inputs["input_3"].Dispose();
        inputs["input_4"].Dispose();

        animationHumanPoses = new float[sequence_length, muscleCount];

        // Disable body controller
        GetComponent<Animator>().runtimeAnimatorController = null;

        for(int frame=0;frame<sequence_length;frame++)
        {
            for(int feat=0;feat<95;feat++)
            {
                animationHumanPoses[frame,feat] = output[0,0,feat,frame];
            }
        }
        counterLoad = sequence_length;

        output.Dispose();
        return true;
    }
        
    public bool LoadAnimationFromModelName(string name)
    {
        m_RuntimeModel = ModelLoader.Load(Application.dataPath+"/GestureMocap/"+name);
        var worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, m_RuntimeModel);
        var inputs = new Dictionary<string, Tensor>();
        inputs["input_3"] = new Tensor(1, 1, 1, 32);
        inputs["input_4"] = new Tensor(1, 1, 1, 3);

        for(int i=0;i<32;i++) inputs["input_3"][0,0,0,i] = 0f;
        for(int i=0;i<3;i++) inputs["input_4"][0,0,0,i] = 0.25f;

        worker.Execute(inputs);

        Tensor output = worker.PeekOutput();
        inputs["input_3"].Dispose();
        inputs["input_4"].Dispose();

        animationHumanPoses = new float[sequence_length, muscleCount];

        // Disable body controller
        GetComponent<Animator>().runtimeAnimatorController = null;

        for(int frame=0;frame<sequence_length;frame++)
        {
            for(int feat=0;feat<95;feat++)
            {
                animationHumanPoses[frame,feat] = output[0,0,feat,frame];
            }
        }
        counterLoad = sequence_length;

        output.Dispose();
        return true;
    }

    // Loop through array and apply poses one frame after another. 
    public void reapplyPosesAnimation()
    {
        if(counterPlay==0)
        {
            // Set the transform
            humanMocapAnimator.transform.position = Vector3.zero;
            // Local position
            Vector3 localPos = humanMocapAnimator.transform.InverseTransformPoint(initialPos);
            poseToSet.bodyPosition = localPos;
        }

        poseToSet.bodyRotation = Quaternion.identity;

        // // int currentFrame = counterPlay%counterLoad;   
        
        for (int i = 0; i < muscleCount; i++) { currentMuscles[i] = animationHumanPoses[counterPlay, i]; } // somehow cannot directly modify muscle values
        poseToSet.muscles = currentMuscles;
        poseHandler.SetHumanPose(ref poseToSet);

        counterPlay++;

        // Stop reapplying poses
        if(counterPlay == counterLoad)
        {
            counterPlay = 0;
            counterLoad = 0;
            reapplyPoses = false;
        }              
    }
}
