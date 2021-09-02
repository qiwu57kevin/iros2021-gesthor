using UnityEngine;
using System.Collections;

public class AnimationPlayer : MonoBehaviour 
{
	[Tooltip("Name of the played animation clip.")]
	public string animationName;

	// singleton instance
	private static AnimationPlayer instance = null;


	public static AnimationPlayer Instance 
	{
		get 
		{
			return instance;
		}
	}


	void Awake()
	{
		instance = this;
	}


//	void Start () 
//	{
//		Animator animator = GetComponent<Animator>();
//
//		if (animator && animationName != string.Empty) 
//		{
//			animator.Play(animationName);
//		}
//	}


	/// <summary>
	/// Sets the animation clip as default animator state and plays it.
	/// </summary>
	/// <returns><c>true</c> on success, <c>false</c> otherwise.</returns>
	/// <param name="modelFilePath">Fbx-model file path.</param>
	/// <param name="animClipName">Animation clip name.</param>
	public bool PlayAnimationClip(string modelFilePath, string animClipName)
	{
		this.animationName = animClipName;
		Animator animator = GetComponent<Animator>();

#if UNITY_EDITOR
		if(animator)
		{
			RuntimeAnimatorController animatorCtrlRT = animator.runtimeAnimatorController;

			string animatorPath = animatorCtrlRT ? UnityEditor.AssetDatabase.GetAssetPath(animatorCtrlRT) : string.Empty;
			Object[] fbxObjects = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(modelFilePath);

			if(fbxObjects != null && animatorPath != string.Empty)
			{
				foreach(Object fbxObject in fbxObjects)
				{
					if(fbxObject is AnimationClip && fbxObject.name == animClipName)
					{
						AnimationClip fbxAnimClip = (AnimationClip)fbxObject;

						if(fbxAnimClip)
						{
							UnityEditor.Animations.AnimatorController animatorCtrl = animatorCtrlRT as UnityEditor.Animations.AnimatorController;
							UnityEditor.Animations.ChildAnimatorState[] animStates = animatorCtrl.layers.Length > 0 ?
								animatorCtrl.layers[0].stateMachine.states : new UnityEditor.Animations.ChildAnimatorState[0];
							bool bStateFound = false;

							for(int i = 0; i < animStates.Length; i++)
							{
								UnityEditor.Animations.ChildAnimatorState animState = animStates[i];

								if(animState.state.name == animClipName)
								{
									animatorCtrl.layers[0].stateMachine.states[i].state.motion = fbxAnimClip;
									animatorCtrl.layers[0].stateMachine.defaultState = animatorCtrl.layers[0].stateMachine.states[i].state;

									bStateFound = true;
									break;
								}
							}

							if(!bStateFound && animatorCtrl.layers.Length > 0)
							{
								UnityEditor.Animations.AnimatorState animState = animatorCtrl.layers[0].stateMachine.AddState(animClipName);
								animState.motion = fbxAnimClip;

								animatorCtrl.layers[0].stateMachine.defaultState = animState;
							}
						}

						break;
					}
				}
			}

		}
#endif

		if (animator && animClipName != string.Empty) 
		{
			animator.Play(animClipName);
			return true;
		}

		return false;
	}
	
}
