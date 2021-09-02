/******************************************************************************
 * Copyright (C) Leap Motion, Inc. 2011-2017.                                 *
 * Leap Motion proprietary and  confidential.                                 *
 *                                                                            *
 * Use subject to the terms of the Leap Motion SDK Agreement available at     *
 * https://developer.leapmotion.com/sdk_agreement, or another agreement       *
 * between Leap Motion and you, your company or other organization.           *
 ******************************************************************************/

using UnityEngine;
using System.Collections;
using Leap;

namespace Leap.Unity 
{
	/**
	* Manages the orientation of the bones in a model rigged for skeletal animation.
	* 
	* The class expects that the graphics model bones corresponding to bones in the Leap Motion 
	* hand model are in the same order in the bones array.
	*/
	public class LeapRiggedFinger : FingerModel 
	{
	    /** Allows the mesh to be stretched to align with finger joint positions
	     * Only set to true when mesh is not visible
	     */
	    [HideInInspector]
	    public bool deformPosition = false;

	    public Vector3 modelFingerPointing = Vector3.forward;
	    public Vector3 modelPalmFacing = -Vector3.up;

		public float smoothFactor = 20f;

		//public AvatarController avatarController;
		public Transform headTransform;


		public Quaternion Reorientation() 
		{
	    	return Quaternion.Inverse(Quaternion.LookRotation(modelFingerPointing, -modelPalmFacing));
	    }

    	private RiggedHand riggedHand;

		/** Updates the bone rotations. */
		public override void UpdateFinger() 
		{
			for (int i = 0; i < bones.Length; ++i) 
			{
				if (bones[i] != null) 
				{
					Quaternion boneRotation = GetRiggedBoneRotation(i); // * Reorientation();
					bones[i].rotation = smoothFactor != 0f ? Quaternion.Slerp(bones[i].rotation, boneRotation, smoothFactor * Time.deltaTime) : boneRotation;
					//          if (deformPosition) {
					//            bones[i].position = GetJointPosition(i);
					//          }
				}
			}
		}

		public Quaternion GetRiggedBoneRotation(int bone_type) 
		{
			if (finger_ != null) 
			{
				Quaternion boneRotation = finger_.Bone ((Bone.BoneType)(bone_type)).Rotation.ToQuaternion();

				if (headTransform) 
				{
					boneRotation = headTransform.rotation * boneRotation;
				}

				return boneRotation * Reorientation();
			}

			if (bones[bone_type]) 
			{
				return bones[bone_type].rotation;
			}

			return Quaternion.identity;
		}


		public void SetupRiggedFinger (bool useMetaCarpals) 
		{
			findBoneTransforms(useMetaCarpals);
			modelFingerPointing = calulateModelFingerPointing();
		}

		private void findBoneTransforms(bool useMetaCarpals) 
		{
			if (!useMetaCarpals || fingerType == Finger.FingerType.TYPE_THUMB) 
			{
				bones[1] = transform;
				bones[2] = transform.GetChild(0).transform;
				bones[3] = transform.GetChild(0).transform.GetChild(0).transform;
			}
			else 
			{
				bones[0] = transform;
				bones[1] = transform.GetChild(0).transform;
				bones[2] = transform.GetChild(0).transform.GetChild(0).transform;
				bones[3] = transform.GetChild(0).transform.GetChild(0).transform.GetChild(0).transform;
			}
		}

		private Vector3 calulateModelFingerPointing() 
		{
			Vector3 distance = transform.InverseTransformPoint(transform.position) -  transform.InverseTransformPoint(transform.GetChild(0).transform.position);
			Vector3 zeroed = RiggedHand.CalculateZeroedVector(distance);

			return zeroed;
		}

	} 
}
