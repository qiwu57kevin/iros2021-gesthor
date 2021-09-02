using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;


/// <summary>
/// Background removal manager is the component that manages Kinect background removal, i.e. cutting out user body silhouettes.
/// </summary>
public class BackgroundRemovalManager : MonoBehaviour 
{
	[Tooltip("Whether the hi-res (color camera resolution) is preferred for the foreground image. Otherwise the depth camera resolution will be used.")]
	public bool colorCameraResolution = true;
}
