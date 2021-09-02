using UnityEngine;
using UnityEngine.UI;
//using Windows.Kinect;

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using System.IO;


/// <summary>
/// Interaction manager is the component that deals with hand interactions.
/// </summary>
public class InteractionManager : MonoBehaviour 
{
	/// <summary>
	/// The hand event types.
	/// </summary>
	public enum HandEventType : int
    {
        None = 0,
        Grip = 1,
        Release = 2
    }


	// converts hand state to hand event type
	public static HandEventType HandStateToEvent(KinectInterop.HandState handState, HandEventType lastEventType)
	{
		switch(handState)
		{
		case KinectInterop.HandState.Open:
			return HandEventType.Release;

		case KinectInterop.HandState.Closed:
		case KinectInterop.HandState.Lasso:
			return HandEventType.Grip;

		case KinectInterop.HandState.Unknown:
			return lastEventType;
		}

		return HandEventType.None;
	}


}
