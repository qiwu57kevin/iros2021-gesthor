using UnityEngine;
//using Windows.Kinect;

using System.Collections;
using System.Collections.Generic;


/// <summary>
/// This interface needs to be implemented by the Kinect gesture managers, like KinectGestures-class itself
/// </summary>
public interface GestureManagerInterface
{
	/// <summary>
	/// Gets the list of gesture joint indexes.
	/// </summary>
	/// <returns>The needed joint indexes.</returns>
	/// <param name="manager">The KinectManager instance</param>
	int[] GetNeededJointIndexes (KinectManager manager);


	/// <summary>
	/// Estimate the state and progress of the given gesture.
	/// </summary>
	/// <param name="userId">User ID</param>
	/// <param name="gestureData">Gesture-data structure</param>
	/// <param name="timestamp">Current time</param>
	/// <param name="jointsPos">Joints-position array</param>
	/// <param name="jointsTracked">Joints-tracked array</param>
	void CheckForGesture (long userId, ref KinectGestures.GestureData gestureData, float timestamp, ref Vector3[] jointsPos, ref bool[] jointsTracked);
}


/// <summary>
/// KinectGestures is utility class that processes programmatic Kinect gestures
/// </summary>
public class KinectGestures : MonoBehaviour, GestureManagerInterface
{

	/// <summary>
	/// This interface needs to be implemented by all Kinect gesture listeners
	/// </summary>
	public interface GestureListenerInterface
	{
		/// <summary>
		/// Invoked when a new user is detected. Here you can start gesture tracking by invoking KinectManager.DetectGesture()-function.
		/// </summary>
		/// <param name="userId">User ID</param>
		/// <param name="userIndex">User index</param>
		void UserDetected(long userId, int userIndex);
		
		/// <summary>
		/// Invoked when a user gets lost. All tracked gestures for this user are cleared automatically.
		/// </summary>
		/// <param name="userId">User ID</param>
		/// <param name="userIndex">User index</param>
		void UserLost(long userId, int userIndex);
		
		/// <summary>
		/// Invoked when a gesture is in progress.
		/// </summary>
		/// <param name="userId">User ID</param>
		/// <param name="userIndex">User index</param>
		/// <param name="gesture">Gesture type</param>
		/// <param name="progress">Gesture progress [0..1]</param>
		/// <param name="joint">Joint type</param>
		/// <param name="screenPos">Normalized viewport position</param>
		void GestureInProgress(long userId, int userIndex, Gestures gesture, float progress, 
		                       KinectInterop.JointType joint, Vector3 screenPos);

		/// <summary>
		/// Invoked if a gesture is completed.
		/// </summary>
		/// <returns><c>true</c>, if the gesture detection must be restarted, <c>false</c> otherwise.</returns>
		/// <param name="userId">User ID</param>
		/// <param name="userIndex">User index</param>
		/// <param name="gesture">Gesture type</param>
		/// <param name="joint">Joint type</param>
		/// <param name="screenPos">Normalized viewport position</param>
		bool GestureCompleted(long userId, int userIndex, Gestures gesture,
		                      KinectInterop.JointType joint, Vector3 screenPos);

		/// <summary>
		/// Invoked if a gesture is cancelled.
		/// </summary>
		/// <returns><c>true</c>, if the gesture detection must be retarted, <c>false</c> otherwise.</returns>
		/// <param name="userId">User ID</param>
		/// <param name="userIndex">User index</param>
		/// <param name="gesture">Gesture type</param>
		/// <param name="joint">Joint type</param>
		bool GestureCancelled(long userId, int userIndex, Gestures gesture, 
		                      KinectInterop.JointType joint);
	}


	/// <summary>
	/// The gesture types.
	/// </summary>
	public enum Gestures
	{
		None = 0,
		RaiseRightHand,
		RaiseLeftHand,
		Psi,
		Tpose,
		Stop,
		Wave,
	}
	
	
	/// <summary>
	/// Programmatic gesture data container.
	/// </summary>
	public struct GestureData
	{
		public long userId;
		public Gestures gesture;
		public int state;
		public float timestamp;
		public int joint;
		public Vector3 jointPos;
		public Vector3 screenPos;
		public float tagFloat;
		public Vector3 tagVector;
		public Vector3 tagVector2;
		public float progress;
		public bool complete;
		public bool cancelled;
		public List<Gestures> checkForGestures;
		public float startTrackingAtTime;
	}
	

	// Gesture related constants, variables and functions
	protected int leftHandIndex;
	protected int rightHandIndex;
		
	protected int leftElbowIndex;
	protected int rightElbowIndex;
		
	protected int leftShoulderIndex;
	protected int rightShoulderIndex;
	
	protected int hipCenterIndex;
	protected int shoulderCenterIndex;

	protected int leftHipIndex;
	protected int rightHipIndex;

	protected int leftKneeIndex;
	protected int rightKneeIndex;
	
	protected int leftAnkleIndex;
	protected int rightAnkleIndex;


	/// <summary>
	/// Gets the list of gesture joint indexes.
	/// </summary>
	/// <returns>The needed joint indexes.</returns>
	/// <param name="manager">The KinectManager instance</param>
	public virtual int[] GetNeededJointIndexes(KinectManager manager)
	{
		leftHandIndex = (int)KinectInterop.JointType.HandLeft;
		rightHandIndex = (int)KinectInterop.JointType.HandRight;
		
		leftElbowIndex = (int)KinectInterop.JointType.ElbowLeft;
		rightElbowIndex = (int)KinectInterop.JointType.ElbowRight;
		
		leftShoulderIndex = (int)KinectInterop.JointType.ShoulderLeft;
		rightShoulderIndex = (int)KinectInterop.JointType.ShoulderRight;
		
		hipCenterIndex = (int)KinectInterop.JointType.SpineBase;
		shoulderCenterIndex = (int)KinectInterop.JointType.SpineShoulder;

		leftHipIndex = (int)KinectInterop.JointType.HipLeft;
		rightHipIndex = (int)KinectInterop.JointType.HipRight;

		leftKneeIndex = (int)KinectInterop.JointType.KneeLeft;
		rightKneeIndex = (int)KinectInterop.JointType.KneeRight;
		
		leftAnkleIndex = (int)KinectInterop.JointType.AnkleLeft;
		rightAnkleIndex = (int)KinectInterop.JointType.AnkleRight;
		
		int[] neededJointIndexes = {
			leftHandIndex, rightHandIndex, leftElbowIndex, rightElbowIndex, leftShoulderIndex, rightShoulderIndex,
			hipCenterIndex, shoulderCenterIndex, leftHipIndex, rightHipIndex, leftKneeIndex, rightKneeIndex, 
			leftAnkleIndex, rightAnkleIndex
		};

		return neededJointIndexes;
	}
	

	protected void SetGestureJoint(ref GestureData gestureData, float timestamp, int joint, Vector3 jointPos)
	{
		gestureData.joint = joint;
		gestureData.jointPos = jointPos;
		gestureData.timestamp = timestamp;
		gestureData.state++;
	}
	
	protected void SetGestureCancelled(ref GestureData gestureData)
	{
		gestureData.state = 0;
		gestureData.progress = 0f;
		gestureData.cancelled = true;
	}
	
	protected void CheckPoseComplete(ref GestureData gestureData, float timestamp, Vector3 jointPos, bool isInPose, float durationToComplete)
	{
		if(isInPose)
		{
			float timeLeft = timestamp - gestureData.timestamp;
			gestureData.progress = durationToComplete > 0f ? Mathf.Clamp01(timeLeft / durationToComplete) : 1.0f;
			Debug.Log ("Pose " + gestureData.gesture + ", progress: " + gestureData.progress);
	
			if(timeLeft >= durationToComplete)
			{
				gestureData.timestamp = timestamp;
				gestureData.jointPos = jointPos;
				gestureData.state++;
				gestureData.complete = true;
			}
		}
		else
		{
			SetGestureCancelled(ref gestureData);
			Debug.Log ("Pose " + gestureData.gesture + " cancelled");
		}
	}
	
	/// <summary>
	/// Estimate the state and progress of the given gesture.
	/// </summary>
	/// <param name="userId">User ID</param>
	/// <param name="gestureData">Gesture-data structure</param>
	/// <param name="timestamp">Current time</param>
	/// <param name="jointsPos">Joints-position array</param>
	/// <param name="jointsTracked">Joints-tracked array</param>
	public virtual void CheckForGesture(long userId, ref GestureData gestureData, float timestamp, ref Vector3[] jointsPos, ref bool[] jointsTracked)
	{
		if(gestureData.complete)
			return;

//		float bandTopY = jointsPos[rightShoulderIndex].y > jointsPos[leftShoulderIndex].y ? jointsPos[rightShoulderIndex].y : jointsPos[leftShoulderIndex].y;
//		float bandBotY = jointsPos[rightHipIndex].y < jointsPos[leftHipIndex].y ? jointsPos[rightHipIndex].y : jointsPos[leftHipIndex].y;
//
//		float bandCenter = (bandTopY + bandBotY) / 2f;
//		float bandSize = (bandTopY - bandBotY);
//
//		float gestureTop = bandCenter + bandSize * 1.2f / 2f;
//		float gestureBottom = bandCenter - bandSize * 1.3f / 4f;
//		float gestureRight = jointsPos[rightHipIndex].x;
//		float gestureLeft = jointsPos[leftHipIndex].x;
		
		switch(gestureData.gesture)
		{
			// check for RaiseRightHand
			case Gestures.RaiseRightHand:
				switch(gestureData.state)
				{
					case 0:  // gesture detection
						if(jointsTracked[rightHandIndex] && jointsTracked[leftHandIndex] && jointsTracked[leftShoulderIndex] &&
							(jointsPos[rightHandIndex].y - jointsPos[leftShoulderIndex].y) > 0.1f &&
				   			(jointsPos[leftHandIndex].y - jointsPos[leftShoulderIndex].y) < 0f)
						{
							SetGestureJoint(ref gestureData, timestamp, rightHandIndex, jointsPos[rightHandIndex]);
						}
						break;
							
					case 1:  // gesture complete
						bool isInPose = jointsTracked[rightHandIndex] && jointsTracked[leftHandIndex] && jointsTracked[leftShoulderIndex] &&
							(jointsPos[rightHandIndex].y - jointsPos[leftShoulderIndex].y) > 0.1f &&
							(jointsPos[leftHandIndex].y - jointsPos[leftShoulderIndex].y) < 0f;

						Vector3 jointPos = jointsPos[gestureData.joint];
						CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, KinectInterop.Constants.PoseCompleteDuration);
						break;
				}
				break;

			// check for RaiseLeftHand
			case Gestures.RaiseLeftHand:
				switch(gestureData.state)
				{
					case 0:  // gesture detection
						if(jointsTracked[leftHandIndex] && jointsTracked[rightHandIndex] && jointsTracked[rightShoulderIndex] &&
							(jointsPos[leftHandIndex].y - jointsPos[rightShoulderIndex].y) > 0.1f &&
				   			(jointsPos[rightHandIndex].y - jointsPos[rightShoulderIndex].y) < 0f)
						{
							SetGestureJoint(ref gestureData, timestamp, leftHandIndex, jointsPos[leftHandIndex]);
						}
						break;
							
					case 1:  // gesture complete
						bool isInPose = jointsTracked[leftHandIndex] && jointsTracked[rightHandIndex] && jointsTracked[rightShoulderIndex] &&
							(jointsPos[leftHandIndex].y - jointsPos[rightShoulderIndex].y) > 0.1f &&
							(jointsPos[rightHandIndex].y - jointsPos[rightShoulderIndex].y) < 0f;

						Vector3 jointPos = jointsPos[gestureData.joint];
						CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, KinectInterop.Constants.PoseCompleteDuration);
						break;
				}
				break;

			// check for Psi
			case Gestures.Psi:
				switch(gestureData.state)
				{
					case 0:  // gesture detection
						if(jointsTracked[rightHandIndex] && jointsTracked[leftHandIndex] && jointsTracked[shoulderCenterIndex] &&
					       (jointsPos[rightHandIndex].y - jointsPos[shoulderCenterIndex].y) > 0.1f &&
					       (jointsPos[leftHandIndex].y - jointsPos[shoulderCenterIndex].y) > 0.1f)
						{
							SetGestureJoint(ref gestureData, timestamp, rightHandIndex, jointsPos[rightHandIndex]);
						}
						break;
							
					case 1:  // gesture complete
						bool isInPose = jointsTracked[rightHandIndex] && jointsTracked[leftHandIndex] && jointsTracked[shoulderCenterIndex] &&
							(jointsPos[rightHandIndex].y - jointsPos[shoulderCenterIndex].y) > 0.1f &&
							(jointsPos[leftHandIndex].y - jointsPos[shoulderCenterIndex].y) > 0.1f;

						Vector3 jointPos = jointsPos[gestureData.joint];
						CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, KinectInterop.Constants.PoseCompleteDuration);
						break;
				}
				break;

			// check for Tpose
			case Gestures.Tpose:
				switch(gestureData.state)
				{
					case 0:  // gesture detection
						if(jointsTracked[rightHandIndex] && jointsTracked[rightElbowIndex] && jointsTracked[rightShoulderIndex] &&
                            Mathf.Abs(jointsPos[rightElbowIndex].y - jointsPos[rightShoulderIndex].y) < 0.1f &&  // 0.07f
                            Mathf.Abs(jointsPos[rightHandIndex].y - jointsPos[rightShoulderIndex].y) < 0.1f &&  // 0.7f
                            jointsTracked[leftHandIndex] && jointsTracked[leftElbowIndex] && jointsTracked[leftShoulderIndex] &&
                            Mathf.Abs(jointsPos[leftElbowIndex].y - jointsPos[leftShoulderIndex].y) < 0.1f &&
                            Mathf.Abs(jointsPos[leftHandIndex].y - jointsPos[leftShoulderIndex].y) < 0.1f)
						{
							SetGestureJoint(ref gestureData, timestamp, rightHandIndex, jointsPos[rightHandIndex]);
						}
						break;
						
					case 1:  // gesture complete
						bool isInPose = jointsTracked[rightHandIndex] && jointsTracked[rightElbowIndex] && jointsTracked[rightShoulderIndex] &&
							Mathf.Abs(jointsPos[rightElbowIndex].y - jointsPos[rightShoulderIndex].y) < 0.1f &&  // 0.7f
							Mathf.Abs(jointsPos[rightHandIndex].y - jointsPos[rightShoulderIndex].y) < 0.1f &&  // 0.7f
							jointsTracked[leftHandIndex] && jointsTracked[leftElbowIndex] && jointsTracked[leftShoulderIndex] &&
							Mathf.Abs(jointsPos[leftElbowIndex].y - jointsPos[leftShoulderIndex].y) < 0.1f &&
							Mathf.Abs(jointsPos[leftHandIndex].y - jointsPos[leftShoulderIndex].y) < 0.1f;
						
						Vector3 jointPos = jointsPos[gestureData.joint];
						CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, KinectInterop.Constants.PoseCompleteDuration);
						break;
				}
				break;
				
			// check for Stop
			case Gestures.Stop:
				switch(gestureData.state)
				{
					case 0:  // gesture detection
						if(jointsTracked[rightHandIndex] && jointsTracked[rightHipIndex] &&
					       (jointsPos[rightHandIndex].y - jointsPos[rightHipIndex].y) < 0.2f &&
				   		   (jointsPos[rightHandIndex].x - jointsPos[rightHipIndex].x) >= 0.4f)
						{
							SetGestureJoint(ref gestureData, timestamp, rightHandIndex, jointsPos[rightHandIndex]);
						}
						else if(jointsTracked[leftHandIndex] && jointsTracked[leftHipIndex] &&
					       (jointsPos[leftHandIndex].y - jointsPos[leftHipIndex].y) < 0.2f &&
				           (jointsPos[leftHandIndex].x - jointsPos[leftHipIndex].x) <= -0.4f)
						{
							SetGestureJoint(ref gestureData, timestamp, leftHandIndex, jointsPos[leftHandIndex]);
						}
						break;
							
					case 1:  // gesture complete
						bool isInPose = (gestureData.joint == rightHandIndex) ?
							(jointsTracked[rightHandIndex] && jointsTracked[rightHipIndex] &&
							(jointsPos[rightHandIndex].y - jointsPos[rightHipIndex].y) < 0.2f &&
				 			(jointsPos[rightHandIndex].x - jointsPos[rightHipIndex].x) >= 0.4f) :
							(jointsTracked[leftHandIndex] && jointsTracked[leftHipIndex] &&
							(jointsPos[leftHandIndex].y - jointsPos[leftHipIndex].y) < 0.2f &&
						 	(jointsPos[leftHandIndex].x - jointsPos[leftHipIndex].x) <= -0.4f);

						Vector3 jointPos = jointsPos[gestureData.joint];
						CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, KinectInterop.Constants.PoseCompleteDuration);
						break;
				}
				break;

			// check for Wave
			case Gestures.Wave:
				switch(gestureData.state)
				{
					case 0:  // gesture detection - phase 1
						if(jointsTracked[rightHandIndex] && jointsTracked[rightElbowIndex] &&
					       (jointsPos[rightHandIndex].y - jointsPos[rightElbowIndex].y) > 0.1f &&
					       (jointsPos[rightHandIndex].x - jointsPos[rightElbowIndex].x) > 0.05f)
						{
							SetGestureJoint(ref gestureData, timestamp, rightHandIndex, jointsPos[rightHandIndex]);
							gestureData.progress = 0.3f;
						}
						else if(jointsTracked[leftHandIndex] && jointsTracked[leftElbowIndex] &&
					            (jointsPos[leftHandIndex].y - jointsPos[leftElbowIndex].y) > 0.1f &&
					            (jointsPos[leftHandIndex].x - jointsPos[leftElbowIndex].x) < -0.05f)
						{
							SetGestureJoint(ref gestureData, timestamp, leftHandIndex, jointsPos[leftHandIndex]);
							gestureData.progress = 0.3f;
						}
						break;
				
					case 1:  // gesture - phase 2
						if((timestamp - gestureData.timestamp) < 1.5f)
						{
							bool isInPose = gestureData.joint == rightHandIndex ?
								jointsTracked[rightHandIndex] && jointsTracked[rightElbowIndex] &&
								(jointsPos[rightHandIndex].y - jointsPos[rightElbowIndex].y) > 0.1f && 
								(jointsPos[rightHandIndex].x - jointsPos[rightElbowIndex].x) < -0.05f :
								jointsTracked[leftHandIndex] && jointsTracked[leftElbowIndex] &&
								(jointsPos[leftHandIndex].y - jointsPos[leftElbowIndex].y) > 0.1f &&
								(jointsPos[leftHandIndex].x - jointsPos[leftElbowIndex].x) > 0.05f;
				
							if(isInPose)
							{
								gestureData.timestamp = timestamp;
								gestureData.state++;
								gestureData.progress = 0.7f;
							}
						}
						else
						{
							// cancel the gesture
							SetGestureCancelled(ref gestureData);
						}
						break;
									
					case 2:  // gesture phase 3 = complete
						if((timestamp - gestureData.timestamp) < 1.5f)
						{
							bool isInPose = gestureData.joint == rightHandIndex ?
								jointsTracked[rightHandIndex] && jointsTracked[rightElbowIndex] &&
								(jointsPos[rightHandIndex].y - jointsPos[rightElbowIndex].y) > 0.1f && 
								(jointsPos[rightHandIndex].x - jointsPos[rightElbowIndex].x) > 0.05f :
								jointsTracked[leftHandIndex] && jointsTracked[leftElbowIndex] &&
								(jointsPos[leftHandIndex].y - jointsPos[leftElbowIndex].y) > 0.1f &&
								(jointsPos[leftHandIndex].x - jointsPos[leftElbowIndex].x) < -0.05f;

							if(isInPose)
							{
								Vector3 jointPos = jointsPos[gestureData.joint];
								CheckPoseComplete(ref gestureData, timestamp, jointPos, isInPose, 0f);
							}
						}
						else
						{
							// cancel the gesture
							SetGestureCancelled(ref gestureData);
						}
						break;
				}
				break;

			// here come more gesture-cases
		}
	}

}
