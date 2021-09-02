// Copyright Allen Institute for Artificial Intelligence 2017
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Utility;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityStandardAssets.ImageEffects;
using System.Linq;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.AI;
using Newtonsoft.Json.Linq;
using MIConvexHull;

namespace UnityStandardAssets.Characters.FirstPerson {
    [RequireComponent(typeof(CharacterController))]

    abstract public class BaseFPSAgentController : MonoBehaviour {
        // debug draw bounds of objects in editor
#if UNITY_EDITOR
        protected List<Bounds> gizmobounds = new List<Bounds>();
#endif
        [SerializeField]
        public SimObjPhysics[] VisibleSimObjPhysics {
            get;
            protected set;
        }
        [SerializeField] protected bool IsHandDefault = true;
        [SerializeField] public GameObject ItemInHand = null; // current object in inventory
        [SerializeField] public GameObject AgentHand = null;
        [SerializeField] protected GameObject DefaultHandPosition = null;
        [SerializeField] protected Transform rotPoint;
        [SerializeField] protected GameObject DebugPointPrefab;
        [SerializeField] private GameObject GridRenderer = null;
        [SerializeField] protected GameObject DebugTargetPointPrefab;
        [SerializeField] protected bool inTopLevelView = false;
        [SerializeField] protected Vector3 lastLocalCameraPosition;
        [SerializeField] protected Quaternion lastLocalCameraRotation;
        public float autoResetTimeScale = 1.0f;

        protected float gridVisualizeY = 0.005f; // used to visualize reachable position grid, offset from floor
        protected HashSet<int> initiallyDisabledRenderers = new HashSet<int>();
        // first person controller parameters
        [SerializeField]
        protected bool m_IsWalking;
        [SerializeField]
        protected float m_WalkSpeed;
        [SerializeField]
        protected float m_RunSpeed;
        [SerializeField]
        protected float m_GravityMultiplier;
        protected static float gridSize = 0.25f;
        // time the checkIfObjectHasStoppedMoving coroutine waits for objects to stop moving
        protected float TimeToWaitForObjectsToComeToRest = 0.0f;
        // determins default move distance for move actions
        protected float moveMagnitude;

        // determines rotation increment of rotate functions
        protected float rotateStepDegrees = 90.0f;
        protected bool snapToGrid;
        protected bool continuousMode;// deprecated, use snapToGrid instead
        public ImageSynthesis imageSynthesis;
        public GameObject VisibilityCapsule = null;// used to keep track of currently active VisCap: see different vis caps for modes below
        public GameObject TallVisCap;// meshes used for Tall mode
        public GameObject BotVisCap;// meshes used for Bot mode
        public GameObject DroneVisCap;// meshes used for Drone mode
        public GameObject DroneBasket;// reference to the drone's basket object
        public GameObject IKArm; // reference to the IK_Robot_Arm_Controller arm
        private bool isVisible = true;
        public bool inHighFrictionArea = false;
        public int fixedUpdateCount { get; protected set; }
        public int updateCount { get; protected set; }
        // outbound object filter
        private SimObjPhysics[] simObjFilter = null;
        private VisibilityScheme visibilityScheme = VisibilityScheme.Collider;

        private Dictionary<int, Dictionary<string, object>> originalLightingValues = null;

        public AgentState agentState = AgentState.Emit;

        // these object types can have a placeable surface mesh associated ith it
        // this is to be used with screenToWorldTarget to filter out raycasts correctly
        protected List<SimObjType> hasPlaceableSurface = new List<SimObjType>() {
            SimObjType.Bathtub, SimObjType.Sink, SimObjType.Drawer, SimObjType.Cabinet,
            SimObjType.CounterTop, SimObjType.Shelf
        };

        public const float DefaultAllowedErrorInShortestPath = 0.0001f;

        public bool IsVisible {
            get { return isVisible; }

            set {
                // first default all Vis capsules of all modes to not enabled
                HideAllAgentRenderers();

                // The VisibilityCapsule will be set to either Tall or Bot 
                // from the SetAgentMode call in BaseFPSAgentController's Initialize()
                foreach (Renderer r in VisibilityCapsule.GetComponentsInChildren<Renderer>()) {
                    r.enabled = value;
                }

                isVisible = value;
            }
        }

        public bool IsProcessing {
            get {
                return this.agentState == AgentState.Processing;
            }
        }

        // convenciance function that can be called
        // when autoSyncTransforms is disabled and the
        // transform has been manually moved
        public void autoSyncTransforms() {
            if (!Physics.autoSyncTransforms) {
                Physics.SyncTransforms();
            }
        }

        public bool ReadyForCommand {
            get {
                return this.agentState == AgentState.Emit || this.agentState == AgentState.ActionComplete;
            }

        }

        protected float maxDownwardLookAngle = 60f;
        protected float maxUpwardLookAngle = 30f;
        // allow agent to push sim objects that can move, for physics
        protected bool PushMode = false;
        protected int actionCounter;
        protected Vector3 targetTeleport;
        public AgentManager agentManager;
        public Camera m_Camera;
        [SerializeField] protected float cameraOrthSize;
        protected float m_XRotation;
        protected float m_ZRotation;
        protected Vector2 m_Input;
        protected Vector3 m_MoveDir = Vector3.zero;
        public CharacterController m_CharacterController;
        protected CollisionFlags m_CollisionFlags;
        protected Vector3 lastPosition;

        protected string lastAction;
        public bool lastActionSuccess {
            get;
            protected set;
        }
        public string errorMessage;
        protected ServerActionErrorCode errorCode;


        public System.Object actionReturn;
        [SerializeField] protected Vector3 standingLocalCameraPosition;
        [SerializeField] protected Vector3 crouchingLocalCameraPosition;
        public float maxVisibleDistance = 1.5f; // changed from 1.0f to account for objects randomly spawned far away on tables/countertops, which would be not visible at 1.0f
        protected float[,,] flatSurfacesOnGrid = new float[0, 0, 0];
        protected float[,] distances = new float[0, 0];
        protected float[,,] normals = new float[0, 0, 0];
        protected bool[,] isOpenableGrid = new bool[0, 0];
        protected string[] segmentedObjectIds = new string[0];
        [SerializeField] public string[] objectIdsInBox = new string[0];
        protected int actionIntReturn;
        protected float actionFloatReturn;
        protected float[] actionFloatsReturn;
        protected Vector3[] actionVector3sReturn;
        protected string[] actionStringsReturn;
        public bool alwaysReturnVisibleRange = false;
        // initial states
        protected Vector3 init_position;
        protected Quaternion init_rotation;
        public int actionDuration = 3;

        // internal state variables
        private float lastEmitTime;
        protected List<string> collisionsInAction;// tracking collided objects
        protected string[] collidedObjects;// container for collided objects
        protected HashSet<Collider> collidersToIgnoreDuringMovement = new HashSet<Collider>();
        protected Quaternion targetRotation;

#if UNITY_WEBGL
        // Javascript communication
        private JavaScriptInterface jsInterface = null;
		public Quaternion TargetRotation
		{
			get { return targetRotation; }
		}
#endif

        // Arm
        protected IK_Robot_Arm_Controller Arm;

        private PhysicsSceneManager _physicsSceneManager = null;
        // use as reference to the PhysicsSceneManager object
        protected PhysicsSceneManager physicsSceneManager {
            get {
                if (_physicsSceneManager == null) {
                    _physicsSceneManager = GameObject.Find("PhysicsSceneManager").GetComponent<PhysicsSceneManager>();
                }
                return _physicsSceneManager;
            }
        }

        // reference to prefab for activiting the cracked camera effect via CameraCrack()
        [SerializeField] GameObject CrackedCameraCanvas = null;

        // Initialize parameters from environment variables
        protected virtual void Awake() {
#if UNITY_WEBGL
                this.jsInterface = this.GetComponent<JavaScriptInterface>();
                this.jsInterface.enabled = true;
#endif

            // character controller parameters
            m_CharacterController = GetComponent<CharacterController>();
            this.m_WalkSpeed = 2;
            this.m_RunSpeed = 10;
            this.m_GravityMultiplier = 2;

            agentManager = GameObject.Find("PhysicsSceneManager").GetComponentInChildren<AgentManager>();
        }

        // Use this for initialization
        public virtual void Start() {
            m_Camera = this.gameObject.GetComponentInChildren<Camera>();

            // set agent initial states
            targetRotation = transform.rotation;
            collidedObjects = new string[0];
            collisionsInAction = new List<string>();

            // setting default renderer settings
            // this hides renderers not used in tall mode, and also sets renderer
            // culling in FirstPersonCharacterCull.cs to ignore tall mode renderers
            HideAllAgentRenderers();

            // record initial positions and rotations
            init_position = transform.position;
            init_rotation = transform.rotation;

            // default nav mesh agent to false cause WHY DOES THIS BREAK THINGS I GUESS IT DOESN TLIKE TELEPORTING
            this.GetComponent<NavMeshAgent>().enabled = false;

            // Recording initially disabled renderers and scene bounds
            // then setting up sceneBounds based on encapsulating all renderers
            foreach (Renderer r in GameObject.FindObjectsOfType<Renderer>()) {
                if (!r.enabled) {
                    initiallyDisabledRenderers.Add(r.GetInstanceID());
                } else {
                    agentManager.SceneBounds.Encapsulate(r.bounds);
                }
            }

            // On start, activate gravity
            Vector3 movement = Vector3.zero;
            movement.y = Physics.gravity.y * m_GravityMultiplier;
            m_CharacterController.Move(movement);
        }

        // defaults all agent renderers, from all modes (tall, bot, drone), to hidden for initialization default
        protected void HideAllAgentRenderers() {
            foreach (Renderer r in TallVisCap.GetComponentsInChildren<Renderer>()) {
                if (r.enabled) {
                    r.enabled = false;
                }
            }

            foreach (Renderer r in BotVisCap.GetComponentsInChildren<Renderer>()) {
                if (r.enabled) {
                    r.enabled = false;
                }
            }

            foreach (Renderer r in DroneVisCap.GetComponentsInChildren<Renderer>()) {
                if (r.enabled) {
                    r.enabled = false;
                }
            }
        }

        public void actionFinishedEmit(bool success, object actionReturn = null) {
            actionFinished(success: success, newState: AgentState.Emit, actionReturn: actionReturn);
        }

        protected virtual void actionFinished(bool success, AgentState newState, object actionReturn = null) {
            if (!this.IsProcessing) {
                Debug.LogError("ActionFinished called with agentState not in processing ");
            }

            lastActionSuccess = success;
            this.agentState = newState;
            this.actionReturn = actionReturn;
            actionCounter = 0;
            targetTeleport = Vector3.zero;

#if UNITY_EDITOR
            Debug.Log($"lastAction: '{this.lastAction}'");
            Debug.Log($"lastActionSuccess: '{success}'");
            if (!success) {
                Debug.Log($"Action failed with error message '{this.errorMessage}'.");
            } else if (actionReturn != null) {
                Debug.Log($"actionReturn: '{actionReturn}'");
            }
#endif
        }

        public virtual void actionFinished(bool success, object actionReturn = null, string errorMessage = null) {
            if (errorMessage != null) {
                this.errorMessage = errorMessage;
            }
            actionFinished(success: success, newState: AgentState.ActionComplete, actionReturn: actionReturn);
            this.resumePhysics();
        }

        protected virtual void resumePhysics() { }



        public Vector3[] getReachablePositions(
            float gridMultiplier = 1.0f,
            int maxStepCount = 10000,
            bool visualize = false,
            Color? gridColor = null,
            bool directionsRelativeAgent = false
        ) { // max step count represents a 100m * 100m room. Adjust this value later if we end up making bigger rooms?
            CapsuleCollider cc = GetComponent<CapsuleCollider>();

            float sw = m_CharacterController.skinWidth;
            Queue<(int, int)> rightForwardQueue = new Queue<(int, int)>();
            rightForwardQueue.Enqueue((0, 0));
            Vector3 startPosition = transform.position;

            Vector3 right;
            Vector3 forward;
            if (directionsRelativeAgent) {
                right = transform.right;
                forward = transform.forward;
            } else {
                right = new Vector3(1.0f, 0.0f, 0.0f);
                forward = new Vector3(0.0f, 0.0f, 1.0f);
            }

            (int, int)[] rightForwardOffsets = { (1, 0), (0, 1), (-1, 0), (0, -1) };

            HashSet<Vector3> goodPoints = new HashSet<Vector3>();
            HashSet<(int, int)> seenRightForwards = new HashSet<(int, int)>();
            int layerMask = 1 << 8;
            int stepsTaken = 0;
            while (rightForwardQueue.Count != 0) {
                stepsTaken += 1;

                // Computing the new position based using an offset from the startPosition
                // guarantees that floating point errors won't result in slight differences
                // between the same points.
                (int, int) rightForward = rightForwardQueue.Dequeue();
                Vector3 p = startPosition + gridSize * gridMultiplier * (
                    right * rightForward.Item1 + forward * rightForward.Item2
                );
                if (!goodPoints.Contains(p)) {
                    goodPoints.Add(p);
                    HashSet<Collider> objectsAlreadyColliding = new HashSet<Collider>(objectsCollidingWithAgent());

                    foreach ((int, int) rightForwardOffset in rightForwardOffsets) {
                        (int, int) newRightForward = (
                            rightForward.Item1 + rightForwardOffset.Item1,
                            rightForward.Item2 + rightForwardOffset.Item2
                        );
                        Vector3 newPosition = startPosition + gridSize * gridMultiplier * (
                            right * newRightForward.Item1 +
                            forward * newRightForward.Item2
                        );
                        if (seenRightForwards.Contains(newRightForward)) {
                            continue;
                        }
                        seenRightForwards.Add(newRightForward);

                        RaycastHit[] hits = capsuleCastAllForAgent(
                            capsuleCollider: cc,
                            skinWidth: sw,
                            startPosition: p,
                            dir: right * rightForwardOffset.Item1 + forward * rightForwardOffset.Item2,
                            moveMagnitude: gridSize * gridMultiplier,
                            layerMask: layerMask
                        );

                        bool shouldEnqueue = true;
                        foreach (RaycastHit hit in hits) {
                            if (hit.transform.gameObject.name != "Floor" &&
                                !ancestorHasName(hit.transform.gameObject, "FPSController") &&
                                !objectsAlreadyColliding.Contains(hit.collider)
                            ) {
                                shouldEnqueue = false;
                                break;
                            }
                        }

                        if (!shouldEnqueue) {
                            continue;
                        }

                        bool inBounds = agentManager.SceneBounds.Contains(newPosition);
                        if (shouldEnqueue && !inBounds) {
                            throw new InvalidOperationException(
                                "In " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                                ", position " + newPosition.ToString() +
                                " can be reached via capsule cast but is beyond the scene bounds."
                            );
                        }

                        shouldEnqueue = shouldEnqueue && inBounds && (
                            handObjectCanFitInPosition(newPosition, 0.0f) ||
                            handObjectCanFitInPosition(newPosition, 90.0f) ||
                            handObjectCanFitInPosition(newPosition, 180.0f) ||
                            handObjectCanFitInPosition(newPosition, 270.0f)
                        );
                        if (shouldEnqueue) {
                            rightForwardQueue.Enqueue(newRightForward);

                            if (visualize) {
                                var gridRenderer = Instantiate(GridRenderer, Vector3.zero, Quaternion.identity);
                                var gridLineRenderer = gridRenderer.GetComponentInChildren<LineRenderer>();
                                if (gridColor.HasValue) {
                                    gridLineRenderer.startColor = gridColor.Value;
                                    gridLineRenderer.endColor = gridColor.Value;
                                }
                                // gridLineRenderer.startColor = ;
                                // gridLineRenderer.endColor = ;
                                gridLineRenderer.positionCount = 2;
                                // gridLineRenderer.startWidth = 0.01f;
                                // gridLineRenderer.endWidth = 0.01f;
                                gridLineRenderer.SetPositions(new Vector3[] {
                                    new Vector3(p.x, gridVisualizeY, p.z),
                                    new Vector3(newPosition.x, gridVisualizeY, newPosition.z)
                                });
                            }
#if UNITY_EDITOR
                            Debug.DrawLine(p, newPosition, Color.cyan, 100000f);
#endif
                        }
                    }
                }
                // default maxStepCount to scale based on gridSize
                if (stepsTaken > Math.Floor(maxStepCount / (gridSize * gridSize))) {
                    throw new InvalidOperationException("Too many steps taken in GetReachablePositions.");
                }
            }

            Vector3[] reachablePos = new Vector3[goodPoints.Count];
            goodPoints.CopyTo(reachablePos);

#if UNITY_EDITOR
            Debug.Log("count of reachable positions: " + reachablePos.Length);
#endif

            return reachablePos;
        }

        public void GetReachablePositions(
            int? maxStepCount = null,
            bool directionsRelativeAgent = false
        ) {
            Vector3[] reachablePositions;
            if (maxStepCount.HasValue) {
                reachablePositions = getReachablePositions(
                    maxStepCount: maxStepCount.Value,
                    directionsRelativeAgent: directionsRelativeAgent
                );
            } else {
                reachablePositions = getReachablePositions(
                    directionsRelativeAgent: directionsRelativeAgent
                );
            }

            actionFinishedEmit(
                success: true,
                actionReturn: reachablePositions
            );
        }

        public void Initialize(ServerAction action) {
            if (action.agentMode.ToLower() == "default" ||
               action.agentMode.ToLower() == "locobot" ||
               action.agentMode.ToLower() == "drone" ||
               action.agentMode.ToLower() == "arm"
               ) {
                // set agent mode to Default, Bot or Drone accordingly
                SetAgentMode(action.agentMode);
            } else {
                errorMessage = "agentMode must be set to 'default' or 'bot' or 'drone' or 'hand'";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            if (action.gridSize == 0) {
                action.gridSize = 0.25f;
            }

            // note: this overrides the default FOV values set in SetAgentMode()
            if (action.fieldOfView > 0 && action.fieldOfView < 180) {
                m_Camera.fieldOfView = action.fieldOfView;
            } else if (action.fieldOfView < 0 || action.fieldOfView >= 180) {
                errorMessage = "fov must be set to (0, 180) noninclusive.";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            if (action.timeScale > 0) {
                if (Time.timeScale != action.timeScale) {
                    Time.timeScale = action.timeScale;
                }
            } else {
                errorMessage = "Time scale must be > 0";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            if (action.rotateStepDegrees <= 0.0) {
                errorMessage = "rotateStepDegrees must be a non-zero, non-negative float";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            // default is 90 defined in the ServerAction class, specify whatever you want the default to be
            if (action.rotateStepDegrees > 0.0) {
                this.rotateStepDegrees = action.rotateStepDegrees;
            }

            this.snapToGrid = action.snapToGrid;

            if (action.renderDepthImage || action.renderSemanticSegmentation || action.renderInstanceSegmentation || action.renderNormalsImage) {
                this.updateImageSynthesis(true);
            }

            if (action.visibilityDistance > 0.0f) {
                this.maxVisibleDistance = action.visibilityDistance;
            }

            var navmeshAgent = this.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var collider = this.GetComponent<CapsuleCollider>();

            if (collider != null && navmeshAgent != null) {
                navmeshAgent.radius = collider.radius;
                navmeshAgent.height = collider.height;
            }

            // navmeshAgent.radius = 

            if (action.gridSize <= 0 || action.gridSize > 5) {
                errorMessage = "grid size must be in the range (0,5]";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            } else {
                gridSize = action.gridSize;
                StartCoroutine(checkInitializeAgentLocationAction());
            }

            // initialize how long the default wait time for objects to stop moving is
            this.TimeToWaitForObjectsToComeToRest = action.TimeToWaitForObjectsToComeToRest;

            // Debug.Log("Object " + action.controllerInitialization.ToString() + " dict "  + (action.controllerInitialization.variableInitializations == null));//+ string.Join(";", action.controllerInitialization.variableInitializations.Select(x => x.Key + "=" + x.Value).ToArray()));

            if (action.controllerInitialization != null && action.controllerInitialization.variableInitializations != null) {
                foreach (KeyValuePair<string, TypedVariable> entry in action.controllerInitialization.variableInitializations) {
                    Debug.Log(" Key " + entry.Value.type + " field " + entry.Key);
                    Type t = Type.GetType(entry.Value.type);
                    FieldInfo field = t.GetField(entry.Key, BindingFlags.Public | BindingFlags.Instance);
                    field.SetValue(this, entry.Value);
                }

            }

            this.visibilityScheme = action.GetVisibilityScheme();
            this.originalLightingValues = null;
        }

        public void SetAgentMode(string mode) {
            string whichMode = mode.ToLower();

            // null check for camera, used to ensure no missing references on initialization
            if (m_Camera == null) {
                m_Camera = this.gameObject.GetComponentInChildren<Camera>();
            }

            FirstPersonCharacterCull fpcc = m_Camera.GetComponent<FirstPersonCharacterCull>();

            // determine if we are in Tall or Bot mode (or other modes as we go on)
            if (whichMode == "default" || whichMode == "arm") {
                // toggle FirstPersonCharacterCull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = TallVisCap;
                m_CharacterController.center = new Vector3(0, 0, 0);
                m_CharacterController.radius = 0.2f;
                m_CharacterController.height = 1.8f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = false;
                m_Camera.GetComponent<PostProcessLayer>().enabled = false;

                // camera position
                m_Camera.transform.localPosition = new Vector3(0, 0.675f, 0);

                // camera FOV
                m_Camera.fieldOfView = 90f;

                // set camera stand/crouch local positions for Tall mode
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition + new Vector3(0, -0.675f, 0);// bigger y offset if tall

                // enable arm component
                if (whichMode == "arm") {
                    Debug.Log("initializing arm");
                    IKArm.SetActive(true);
                    Arm = this.GetComponentInChildren<IK_Robot_Arm_Controller>();
                    var armTarget = Arm.transform.Find("robot_arm_FK_IK_rig").Find("IK_rig").Find("IK_pos_rot_manipulator");
                    Vector3 pos = armTarget.transform.localPosition;
                    pos.z = 0.4f; // pulls the arm in from being fully extended
                    armTarget.transform.localPosition = pos;
                    var ikSolver = this.GetComponentInChildren<FK_IK_Solver>();
                    Debug.Log("running manipulate arm");
                    ikSolver.ManipulateArm();
                }
            } else if (whichMode == "locobot") {
                // toggle FirstPersonCharacterCull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = BotVisCap;
                m_CharacterController.center = new Vector3(0, -0.45f, 0);
                m_CharacterController.radius = 0.175f;
                m_CharacterController.height = 0.9f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = true;
                m_Camera.GetComponent<PostProcessLayer>().enabled = true;

                // camera position
                m_Camera.transform.localPosition = new Vector3(0, -0.0312f, 0);

                // camera FOV
                m_Camera.fieldOfView = 60f;

                // set camera stand/crouch local positions for Tall mode
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition + new Vector3(0, -0.2206f, 0);// smaller y offset if Bot

                // limit camera from looking too far down
                this.maxDownwardLookAngle = 30f;
                this.maxUpwardLookAngle = 30f;
                // this.horizonAngles = new float[] { 30.0f, 0.0f, 330.0f };
            } else if (whichMode == "drone") {
                // toggle first person character cull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = DroneVisCap;
                m_CharacterController.center = new Vector3(0, 0, 0);
                m_CharacterController.radius = 0.2f;
                m_CharacterController.height = 0.0f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = false;
                m_Camera.GetComponent<PostProcessLayer>().enabled = false;

                // camera position set forward a bit for drone
                m_Camera.transform.localPosition = new Vector3(0, 0, 0.2f);

                // camera FOV for drone
                m_Camera.fieldOfView = 150f;

                // default camera stand/crouch for drone mode since drone doesn't stand or crouch
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition;

                // drone also needs to toggle on the drone basket
                DroneBasket.SetActive(true);
            }
        }

        public IEnumerator checkInitializeAgentLocationAction() {
            yield return null;

            Vector3 startingPosition = this.transform.position;
            // move ahead
            // move back

            float mult = 1 / gridSize;
            float grid_x1 = Convert.ToSingle(Math.Floor(this.transform.position.x * mult) / mult);
            float grid_z1 = Convert.ToSingle(Math.Floor(this.transform.position.z * mult) / mult);

            float[] xs = new float[] { grid_x1, grid_x1 + gridSize };
            float[] zs = new float[] { grid_z1, grid_z1 + gridSize };
            List<Vector3> validMovements = new List<Vector3>();

            foreach (float x in xs) {
                foreach (float z in zs) {
                    this.transform.position = startingPosition;
                    autoSyncTransforms();

                    yield return null;

                    Vector3 target = new Vector3(x, this.transform.position.y, z);
                    Vector3 dir = target - this.transform.position;
                    Vector3 movement = dir.normalized * 100.0f;
                    if (movement.magnitude > dir.magnitude) {
                        movement = dir;
                    }

                    movement.y = Physics.gravity.y * this.m_GravityMultiplier;

                    m_CharacterController.Move(movement);

                    for (int i = 0; i < actionDuration; i++) {
                        yield return null;
                        Vector3 diff = this.transform.position - target;


                        if ((Math.Abs(diff.x) < 0.005) && (Math.Abs(diff.z) < 0.005)) {
                            validMovements.Add(movement);
                            break;
                        }
                    }

                }
            }

            this.transform.position = startingPosition;
            autoSyncTransforms();
            yield return null;
            if (validMovements.Count > 0) {
                Debug.Log("Initialize: got total valid initial targets: " + validMovements.Count);
                Vector3 firstMove = validMovements[0];
                firstMove.y = Physics.gravity.y * this.m_GravityMultiplier;

                m_CharacterController.Move(firstMove);
                snapAgentToGrid();
                actionFinished(true, new InitializeReturn {
                    cameraNearPlane = m_Camera.nearClipPlane,
                    cameraFarPlane = m_Camera.farClipPlane
                });
            } else {
                Debug.Log("Initialize: no valid starting positions found");
                actionFinished(false);
            }
        }

        [ObsoleteAttribute(message: "This action is deprecated. Call RandomizeColors instead.", error: false)]
        public void ChangeColorOfMaterials() {
            RandomizeColors();
        }

        public void RandomizeColors() {
            ColorChanger colorChangeComponent = physicsSceneManager.GetComponent<ColorChanger>();
            colorChangeComponent.RandomizeColor();
            actionFinished(true);
        }

        public void ResetColors() {
            ColorChanger colorChangeComponent = physicsSceneManager.GetComponent<ColorChanger>();
            colorChangeComponent.ResetColors();
            actionFinished(true);
        }

        /**
         *
         * @REMARK: float[] = {float, float} cannot be a compile time constant, hence why there are
         *          null defaults.
         * @REMARK: Union types are not (intended) to be supported in C# until C# 10.0. So, sadly, one
         *          must pass in hue=[value, value] for hue=value (and similarly for brightness and
         *          saturation).
         *
         * @param synchronized denotes if all lights should be multiplied by the same randomized
         *        intensity and be randomized to the same color. When false, each lighting object gets
         *        its own independent randomized intensity and randomized color.
         * @param brightness sets the bounds with which the light intensity is multiplied by. If its a
         *        tuple(float, float), values must each be greater than 0, where the multiplier is
         *        then sampled from [brightness[0] : brightness[1]]. If brightness[0] is greater than
         *        brightness[1], the values are swapped. Defaults to (0.5, 1.5).
         * @param randomizeColor specifies if the color of the light should be randomized, or if only
         *        its intensity should change.
         * @param hue provides the (min, max) range of possible hue values for a light's color.
         *        Valid values are in [0 : 1], where:
         *          - 0 maps to a hue of 0 degrees (i.e., red-ish)
         *          - 0.5 maps to a hue of 180 degrees (i.e., green-ish)
         *          - 1 maps to a hue of 360 degrees (i.e., red-ish)
         * @param saturation provides the (min, max) range of possible saturation values for a light's
         *        color. Valid values are in [0 : 1], where 0 corresponds to grayscale and 1 corresponds
         *        to full saturation. Defaults to [0.5 : 1].
         */
        public void RandomizeLighting(
            bool synchronized = false,
            float[] brightness = null,
            bool randomizeColor = true,
            float[] hue = null,
            float[] saturation = null
        ) {
            if (!randomizeColor && (hue != null || saturation != null)) {
                if (hue != null) {
                    throw new ArgumentException(
                        $"Cannot pass in randomizeColor=False while also providing hue={hue}."
                    );
                }
                if (saturation != null) {
                    throw new ArgumentException(
                        $"Cannot pass in randomizeColor=False while also providing saturation={saturation}."
                    );
                }
            }

            if (brightness == null) {
                brightness = new float[] { 0.5f, 1.5f };
            }
            if (brightness[0] < 0 || brightness[1] < 0) {
                throw new ArgumentOutOfRangeException(
                    $"Each brightness must be >= 0, not brightness={brightness}."
                );
            }

            if (hue == null) {
                hue = new float[] { 0, 1 };
            }
            if (saturation == null) {
                saturation = new float[] { 0.5f, 1 };
            }

            if (saturation.Length != 2 || hue.Length != 2 || brightness.Length != 2) {
                throw new ArgumentException(
                    "Ranges for hue, saturation, and brightness must each have 2 values. You gave " +
                    $"saturation={saturation}, hue={hue}, brightness={brightness}."
                );
            }

            if (hue[0] < 0 || hue[0] > 1 || hue[1] < 0 || hue[1] > 1) {
                throw new ArgumentOutOfRangeException($"hue range must be in [0:1], not {hue}");
            }
            if (saturation[0] < 0 || saturation[0] > 1 || saturation[1] < 0 || saturation[1] > 1) {
                throw new ArgumentOutOfRangeException($"saturation range must be in [0:1], not {saturation}");
            }

            float newRandomFloat() {
                return Random.Range(brightness[0], brightness[1]);
            }
            Color newRandomColor() {
                // NOTE: This function weirdly IGNORES out of bounds arguments.
                //       So, they are checked above.
                // NOTE: value is an extraneous degree of freedom here,
                //       since it can be controlled by brightness.
                //       Hence why value=1.
                return Random.ColorHSV(
                    hueMin: hue[0],
                    hueMax: hue[1],
                    saturationMin: saturation[0],
                    saturationMax: saturation[1],
                    valueMin: 1,
                    valueMax: 1
                );
            }

            float intensityMultiplier = newRandomFloat();
            Color randomColor = newRandomColor();

            bool setOriginalMultipliers = originalLightingValues == null;
            if (setOriginalMultipliers) {
                originalLightingValues = new Dictionary<int, Dictionary<string, object>>();
            }

            // include both lights and reflection probes
            Light[] lights = GameObject.FindObjectsOfType<Light>();
            foreach (Light light in lights) {
                if (!synchronized) {
                    intensityMultiplier = newRandomFloat();
                    randomColor = newRandomColor();
                }
                int id = light.gameObject.GetInstanceID();
                if (setOriginalMultipliers) {
                    originalLightingValues[id] = new Dictionary<string, object>() {
                        // NOTE: make sure these are synced with ResetLighting()!
                        ["intensity"] = light.intensity,
                        ["range"] = light.range,
                        ["color"] = light.color
                    };
                }
                light.intensity = (float)originalLightingValues[id]["intensity"] * intensityMultiplier;
                light.range = (float)originalLightingValues[id]["range"] * intensityMultiplier;
                if (randomizeColor) {
                    light.color = randomColor;
                }
            }

            ReflectionProbe[] reflectionProbes = GameObject.FindObjectsOfType<ReflectionProbe>();
            foreach (ReflectionProbe reflectionProbe in reflectionProbes) {
                if (!synchronized) {
                    intensityMultiplier = newRandomFloat();
                }
                int id = reflectionProbe.gameObject.GetInstanceID();
                if (setOriginalMultipliers) {
                    // NOTE: make sure these are synced with ResetLighting()!
                    originalLightingValues[id] = new Dictionary<string, object>() {
                        ["intensity"] = reflectionProbe.intensity,
                        ["blendDistance"] = reflectionProbe.intensity
                    };
                }
                reflectionProbe.intensity = (
                    (float)originalLightingValues[id]["intensity"] * intensityMultiplier
                );
                reflectionProbe.blendDistance = (
                    (float)originalLightingValues[id]["blendDistance"] * intensityMultiplier
                );
            }

            actionFinished(success: true);
        }

        public void ResetLighting() {
            if (originalLightingValues == null) {
                actionFinishedEmit(success: true);
                return;
            }

            Light[] lights = GameObject.FindObjectsOfType<Light>();
            foreach (Light light in lights) {
                int id = light.gameObject.GetInstanceID();
                light.intensity = (float)originalLightingValues[id]["intensity"];
                light.range = (float)originalLightingValues[id]["range"];
                light.color = (Color)originalLightingValues[id]["color"];
            }

            ReflectionProbe[] reflectionProbes = GameObject.FindObjectsOfType<ReflectionProbe>();
            foreach (ReflectionProbe reflectionProbe in reflectionProbes) {
                int id = reflectionProbe.gameObject.GetInstanceID();
                reflectionProbe.intensity = (float)originalLightingValues[id]["intensity"];
                reflectionProbe.blendDistance = (float)originalLightingValues[id]["blendDistance"];
            }

            originalLightingValues = null;
            actionFinished(success: true);
        }

        // for all translational movement, check if the item the player is holding will hit anything, or if the agent will hit anything
        // NOTE: (XXX) All four movements below no longer use base character controller Move() due to doing initial collision blocking
        // checks before actually moving. Previously we would moveCharacter() first and if we hit anything reset, but now to match
        // Luca's movement grid and valid position generation, simple transform setting is used for movement instead.

        // XXX revisit what movement means when we more clearly define what "continuous" movement is
        protected bool moveInDirection(
            Vector3 direction,
            string objectId = "",
            float maxDistanceToObject = -1.0f,
            bool forceAction = false,
            bool manualInteract = false,
            HashSet<Collider> ignoreColliders = null
        ) {
            Vector3 targetPosition = transform.position + direction;
            if (checkIfSceneBoundsContainTargetPosition(targetPosition) &&
                CheckIfItemBlocksAgentMovement(direction, forceAction) && // forceAction = true allows ignoring movement restrictions caused by held objects
                CheckIfAgentCanMove(direction, ignoreColliders)) {

                // only default hand if not manually interacting with things    
                if (!manualInteract) {
                    DefaultAgentHand();
                }

                Vector3 oldPosition = transform.position;
                transform.position = targetPosition;
                this.snapAgentToGrid();

                if (objectId != "" && maxDistanceToObject > 0.0f) {
                    if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                        errorMessage = "No object with ID " + objectId;
                        transform.position = oldPosition;
                        return false;
                    }
                    SimObjPhysics sop = physicsSceneManager.ObjectIdToSimObjPhysics[objectId];
                    if (distanceToObject(sop) > maxDistanceToObject) {
                        errorMessage = "Agent movement would bring it beyond the max distance of " + objectId;
                        transform.position = oldPosition;
                        return false;
                    }
                }
                return true;
            } else {
                return false;
            }
        }

        public void MoveGlobal(float x, float z) {
            actionFinished(moveInDirection(direction: new Vector3(x, 0f, z)));
        }

        protected float distanceToObject(SimObjPhysics sop) {
            float dist = 10000.0f;
            foreach (Collider c in sop.GetComponentsInChildren<Collider>()) {
                Vector3 closestPoint = c.ClosestPointOnBounds(transform.position);
                Vector3 p0 = new Vector3(transform.position.x, 0f, transform.position.z);
                Vector3 p1 = new Vector3(closestPoint.x, 0f, closestPoint.z);
                dist = Math.Min(Vector3.Distance(p0, p1), dist);
            }
            return dist;
        }

        public void DistanceToObject(string objectId) {
            float dist = distanceToObject(physicsSceneManager.ObjectIdToSimObjPhysics[objectId]);
#if UNITY_EDITOR
            Debug.Log(dist);
#endif
            actionFinished(true, dist);
        }

        public bool CheckIfAgentCanMove(
            Vector3 offset,
            HashSet<Collider> ignoreColliders = null
        ) {

            RaycastHit[] sweepResults = capsuleCastAllForAgent(
                GetComponent<CapsuleCollider>(),
                m_CharacterController.skinWidth,
                transform.position,
                offset.normalized,
                offset.magnitude,
                1 << 8 | 1 << 10
            );
            // check if we hit an environmental structure or a sim object that we aren't actively holding. If so we can't move
            if (sweepResults.Length > 0) {
                foreach (RaycastHit res in sweepResults) {
                    if (ignoreColliders != null && ignoreColliders.Contains(res.collider)) {
                        continue;
                    }

                    // Don't worry if we hit something thats in our hand.
                    if (ItemInHand != null && ItemInHand.transform == res.transform) {
                        continue;
                    }

                    if (res.transform.gameObject != this.gameObject && res.transform.GetComponent<PhysicsRemoteFPSAgentController>()) {

                        PhysicsRemoteFPSAgentController maybeOtherAgent = res.transform.GetComponent<PhysicsRemoteFPSAgentController>();
                        int thisAgentNum = agentManager.agents.IndexOf(this);
                        int otherAgentNum = agentManager.agents.IndexOf(maybeOtherAgent);
                        errorMessage = $"Agent {otherAgentNum} is blocking Agent {thisAgentNum} from moving by {offset.ToString("F4")}.";
                        return false;
                    }

                    // including "Untagged" tag here so that the agent can't move through objects that are transparent
                    if ((!collidersToIgnoreDuringMovement.Contains(res.collider)) && (
                            res.transform.GetComponent<SimObjPhysics>() ||
                            res.transform.tag == "Structure" ||
                            res.transform.tag == "Untagged"
                        )) {
                        int thisAgentNum = agentManager.agents.IndexOf(this);
                        errorMessage = $"{res.transform.name} is blocking Agent {thisAgentNum} from moving by {offset.ToString("F4")}.";
                        // the moment we find a result that is blocking, return false here
                        return false;
                    }
                }
            }
            return true;
        }

        public void DisableObject(string objectId) {
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[objectId].gameObject.SetActive(false);
                actionFinished(true);
            } else {
                actionFinished(false);
            }
        }

        public void EnableObject(string objectId) {
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[objectId].gameObject.SetActive(true);
                actionFinished(true);
            } else {
                actionFinished(false);
            }
        }

        // remove a given sim object from the scene. Pass in the object's objectID string to remove it.
        public void RemoveFromScene(string objectId) {
            // pass name of object in from action.objectId
            if (objectId == null) {
                errorMessage = "objectId required for RemoveFromScene";
                actionFinished(false);
                return;
            }

            // see if the object exists in this scene
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[objectId].transform.gameObject.SetActive(false);
                physicsSceneManager.SetupScene();
                actionFinished(true);
                return;
            }

            errorMessage = objectId + " could not be found in this scene, so it can't be removed";
            actionFinished(false);
        }

        // remove a list of given sim object from the scene.
        public void RemoveObjsFromScene(string[] objectIds) {
            if (objectIds == null || objectIds[0] == null) {
                errorMessage = "objectIds was not initialized correctly. Please make sure each element in the objectIds list is initialized.";
                actionFinished(false);
                return;
            }
            bool fail = false;
            foreach (string objIds in objectIds) {
                if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objIds)) {
                    physicsSceneManager.ObjectIdToSimObjPhysics[objIds].transform.gameObject.SetActive(false);
                } else {
                    fail = true;
                }
            }
            physicsSceneManager.SetupScene();
            if (fail) {
                errorMessage = "some objectsin objectIds were not removed correctly.";
                actionFinished(false);
            } else {
                actionFinished(true);
            }
            return;
        }

        // Sweeptest to see if the object Agent is holding will prohibit movement
        public bool CheckIfItemBlocksAgentMovement(Vector3 offset, bool forceAction = false) {
            bool result = false;

            // if forceAction true, ignore collision restrictions caused by held objects
            if (forceAction) {
                return true;
            }
            // if there is nothing in our hand, we are good, return!
            if (ItemInHand == null) {
                //  Debug.Log("Agent has nothing in hand blocking movement");
                return true;
            } else {
                // otherwise we are holding an object and need to do a sweep using that object's rb

                Rigidbody rb = ItemInHand.GetComponent<Rigidbody>();

                RaycastHit[] sweepResults = rb.SweepTestAll(
                    offset.normalized,
                    offset.magnitude,
                    QueryTriggerInteraction.Ignore
                );
                if (sweepResults.Length > 0) {
                    foreach (RaycastHit res in sweepResults) {
                        // did the item in the hand touch the agent? if so, ignore it's fine
                        if (res.transform.tag == "Player") {
                            result = true;
                            break;
                        } else {
                            errorMessage = $"{res.transform.name} is blocking the Agent from moving by {offset.ToString("F4")} with {ItemInHand.name}";
                            result = false;
                            return result;
                        }

                    }
                }

                // if the array is empty, nothing was hit by the sweeptest so we are clear to move
                else {
                    // Debug.Log("Agent Body can move " + orientation);
                    result = true;
                }

                return result;
            }
        }

        protected bool checkIfSceneBoundsContainTargetPosition(Vector3 position) {
            if (!agentManager.SceneBounds.Contains(position)) {
                errorMessage = "Scene bounds do not contain target position: " + position;
                return false;
            } else {
                return true;
            }
        }


        // This effectively freezes objects that exceed the MassThreshold configured
        // during initialization and reduces the chance of an object held by the
        // arm from moving a large mass object.  This also eliminates the chance
        // of a large mass object moving vs. relying on the CollisionListener to prevent it.
        public void MakeObjectsStaticKinematicMassThreshold() {
            foreach (SimObjPhysics sop in GameObject.FindObjectsOfType<SimObjPhysics>()) {
                // check if the sopType is something that can be hung
                if (sop.Type == SimObjType.Towel || sop.Type == SimObjType.HandTowel || sop.Type == SimObjType.ToiletPaper) {
                    // if this object is actively hung on its corresponding object specific receptacle... skip it so it doesn't fall on the floor
                    if (sop.GetComponentInParent<ObjectSpecificReceptacle>()) {
                        continue;
                    }
                }

                if (CollisionListener.useMassThreshold && sop.Mass > CollisionListener.massThreshold) {
                    Rigidbody rb = sop.GetComponent<Rigidbody>();
                    rb.isKinematic = true;
                    sop.PrimaryProperty = SimObjPrimaryProperty.Static;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                }
            }
            actionFinished(true);
        }

        // if you want to do something like throw objects to knock over other objects, use this action to set all objects to Kinematic false
        // otherwise objects will need to be hit multiple times in order to ensure kinematic false toggle
        // use this by initializing the scene, then calling randomize if desired, and then call this action to prepare the scene so all objects will react to others upon collision.
        // note that SOMETIMES rigidbodies will continue to jitter or wiggle, especially if they are stacked against other rigidbodies.
        // this means that the isSceneAtRest bool will always be false
        public void MakeAllObjectsMoveable() {
            physicsSceneManager.MakeAllObjectsMoveable();
            actionFinished(true);
        }

        public void MakeAllObjectsStationary() {
            foreach (SimObjPhysics sop in GameObject.FindObjectsOfType<SimObjPhysics>()) {
                Rigidbody rb = sop.GetComponent<Rigidbody>();
                rb.isKinematic = true;

                sop.PrimaryProperty = SimObjPrimaryProperty.Static;
            }

#if UNITY_EDITOR
            Debug.Log("Echoes! Three Freeze!");
#endif

            actionFinished(true);
        }

        // this does not appear to be used except for by the python unit test?
        // May deprecate this at some point?
        public void RotateLook(ServerAction response) {
            transform.rotation = Quaternion.Euler(new Vector3(0.0f, response.rotation.y, 0.0f));
            m_Camera.transform.localEulerAngles = new Vector3(response.horizon, 0.0f, 0.0f);
            actionFinished(true);

        }

        // rotate view with respect to mouse or server controls - I'm not sure when this is actually used
        protected virtual void RotateView() {
            // turn up & down
            if (Mathf.Abs(m_XRotation) > Mathf.Epsilon) {
                transform.Rotate(Vector3.right * m_XRotation, Space.Self);
            }

            // turn left & right
            if (Mathf.Abs(m_ZRotation) > Mathf.Epsilon) {
                transform.Rotate(Vector3.up * m_ZRotation, Space.Self);
            }

            // heading
            float eulerX = Mathf.Round(transform.eulerAngles.x);

            // rotating
            float eulerY = Mathf.Round(transform.eulerAngles.y);

            // TODO: make this as a precondition
            // move this out of Unity
            // constrain vertical turns in safe range
            float X_SAFE_RANGE = 30.0f;
            if (eulerX < 180.0f) {
                eulerX = Mathf.Min(X_SAFE_RANGE, eulerX);
            } else {
                eulerX = 360.0f - Mathf.Min(X_SAFE_RANGE, 360.0f - eulerX);
            }

            // freeze y-axis
            transform.rotation = Quaternion.Euler(eulerX, eulerY, 0);

        }

        // Check if agent is collided with other objects
        protected bool IsCollided() {
            return collisionsInAction.Count > 0;
        }

        public bool IsInteractable(SimObjPhysics sop) {
            if (sop == null) {
                throw new NullReferenceException("null SimObjPhysics passed to IsInteractable");
            }

            return GetAllVisibleSimObjPhysics(agentCamera: this.m_Camera, maxDistance: this.maxVisibleDistance, filterSimObjs: new List<SimObjPhysics> { sop }).Length == 1;
        }

        public virtual SimpleSimObj[] allSceneObjects() {
            return GameObject.FindObjectsOfType<SimObj>();
        }

        public void ResetObjectFilter() {
            this.simObjFilter = null;
            // this could technically be a FastEmit action
            // but could cause confusion since the result of this 
            // action should return all the objects. Resetting the filter
            // should cause all the objects to get returned, which FastEmit would not do.
            actionFinished(true);
        }
        public void SetObjectFilter(string[] objectIds) {
            SimObjPhysics[] simObjects = GameObject.FindObjectsOfType<SimObjPhysics>();
            HashSet<SimObjPhysics> filter = new HashSet<SimObjPhysics>();
            HashSet<string> filterObjectIds = new HashSet<string>(objectIds);
            foreach (var simObj in simObjects) {
                if (filterObjectIds.Contains(simObj.ObjectID)) {
                    filter.Add(simObj);
                }
            }
            simObjFilter = filter.ToArray();
            // this could technically be a FastEmit action
            // but could cause confusion since the result of this 
            // action should return a limited set of objects. Setting the filter
            // should cause only the objects in the filter to get returned, 
            // which FastEmit would not do.
            actionFinished(true);
        }

        public virtual ObjectMetadata[] generateObjectMetadata() {
            SimObjPhysics[] simObjects = null;
            if (this.simObjFilter != null) {
                simObjects = this.simObjFilter;
            } else {
                simObjects = GameObject.FindObjectsOfType<SimObjPhysics>();
            }

            HashSet<SimObjPhysics> visibleSimObjsHash = new HashSet<SimObjPhysics>(GetAllVisibleSimObjPhysics(
                this.m_Camera,
                this.maxVisibleDistance,
                this.simObjFilter));

            int numObj = simObjects.Length;
            List<ObjectMetadata> metadata = new List<ObjectMetadata>();
            Dictionary<string, List<string>> parentReceptacles = new Dictionary<string, List<string>>();

#if UNITY_EDITOR
            // debug draw bounds reset list
            gizmobounds.Clear();
#endif

            for (int k = 0; k < numObj; k++) {
                SimObjPhysics simObj = simObjects[k];
                ObjectMetadata meta = ObjectMetadataFromSimObjPhysics(simObj, visibleSimObjsHash.Contains(simObj));
                if (meta.receptacle) {

                    List<string> containedObjectsAsID = new List<String>();
                    foreach (GameObject go in simObj.ContainedGameObjects()) {
                        containedObjectsAsID.Add(go.GetComponent<SimObjPhysics>().ObjectID);
                    }
                    List<string> roid = containedObjectsAsID;// simObj.Contains();

                    foreach (string oid in roid) {
                        if (!parentReceptacles.ContainsKey(oid)) {
                            parentReceptacles[oid] = new List<string>();
                        }
                        parentReceptacles[oid].Add(simObj.ObjectID);
                    }
                    meta.receptacleObjectIds = roid.ToArray();
                }
                meta.distance = Vector3.Distance(transform.position, simObj.gameObject.transform.position);
                metadata.Add(meta);
            }
            foreach (ObjectMetadata meta in metadata) {
                if (parentReceptacles.ContainsKey(meta.objectId)) {
                    meta.parentReceptacles = parentReceptacles[meta.objectId].ToArray();
                }
            }
            return metadata.ToArray();
        }

        // generates object metatada based on sim object's properties
        public virtual ObjectMetadata ObjectMetadataFromSimObjPhysics(SimObjPhysics simObj, bool isVisible) {
            ObjectMetadata objMeta = new ObjectMetadata();
            GameObject o = simObj.gameObject;
            objMeta.name = o.name;
            objMeta.position = o.transform.position;
            objMeta.rotation = o.transform.eulerAngles;
            objMeta.objectType = Enum.GetName(typeof(SimObjType), simObj.Type);
            objMeta.receptacle = simObj.IsReceptacle;

            objMeta.openable = simObj.IsOpenable;
            if (objMeta.openable) {
                objMeta.isOpen = simObj.IsOpen;
                objMeta.openness = simObj.openness;
            }

            objMeta.toggleable = simObj.IsToggleable;
            if (objMeta.toggleable) {
                objMeta.isToggled = simObj.IsToggled;
            }

            objMeta.breakable = simObj.IsBreakable;
            if (objMeta.breakable) {
                objMeta.isBroken = simObj.IsBroken;
            }

            objMeta.canFillWithLiquid = simObj.IsFillable;
            if (objMeta.canFillWithLiquid) {
                objMeta.isFilledWithLiquid = simObj.IsFilled;
            }

            objMeta.dirtyable = simObj.IsDirtyable;
            if (objMeta.dirtyable) {
                objMeta.isDirty = simObj.IsDirty;
            }

            objMeta.cookable = simObj.IsCookable;
            if (objMeta.cookable) {
                objMeta.isCooked = simObj.IsCooked;
            }

            // if the sim object is moveable or pickupable
            if (simObj.IsPickupable || simObj.IsMoveable || simObj.salientMaterials.Length > 0) {
                // this object should report back mass and salient materials

                string[] salientMaterialsToString = new string[simObj.salientMaterials.Length];

                for (int i = 0; i < simObj.salientMaterials.Length; i++) {
                    salientMaterialsToString[i] = simObj.salientMaterials[i].ToString();
                }

                objMeta.salientMaterials = salientMaterialsToString;

                // this object should also report back mass since it is moveable/pickupable
                objMeta.mass = simObj.Mass;

            }

            // can this object change others to hot?
            objMeta.canChangeTempToHot = simObj.canChangeTempToHot;

            // can this object change others to cold?
            objMeta.canChangeTempToCold = simObj.canChangeTempToCold;

            // placeholder for heatable objects -kettle, pot, pan
            // objMeta.abletocook = simObj.abletocook;
            // if(objMeta.abletocook) {
            //     objMeta.isReadyToCook = simObj.IsHeated;
            // }

            objMeta.sliceable = simObj.IsSliceable;
            if (objMeta.sliceable) {
                objMeta.isSliced = simObj.IsSliced;
            }

            objMeta.canBeUsedUp = simObj.CanBeUsedUp;
            if (objMeta.canBeUsedUp) {
                objMeta.isUsedUp = simObj.IsUsedUp;
            }

            // object temperature to string
            objMeta.ObjectTemperature = simObj.CurrentObjTemp.ToString();

            objMeta.pickupable = simObj.IsPickupable;
            objMeta.isPickedUp = simObj.isPickedUp;// returns true for if this object is currently being held by the agent

            objMeta.moveable = simObj.IsMoveable;

            objMeta.objectId = simObj.ObjectID;

            // TODO: using the isVisible flag on the object causes weird problems
            // in the multiagent setting, explicitly giving this information for now.
            objMeta.visible = isVisible; // simObj.isVisible;

            objMeta.obstructed = !isVisible;// if object is not interactable, it means it is obstructed

            objMeta.isMoving = simObj.inMotion;// keep track of if this object is actively moving


            objMeta.objectOrientedBoundingBox = simObj.ObjectOrientedBoundingBox;

            objMeta.axisAlignedBoundingBox = simObj.AxisAlignedBoundingBox;

            return objMeta;
        }

        public SceneBounds GenerateSceneBounds(Bounds bounding) {
            SceneBounds b = new SceneBounds();
            List<float[]> cornerPoints = new List<float[]>();
            float[] xs = new float[]{
                bounding.center.x + bounding.size.x/2f,
                bounding.center.x - bounding.size.x/2f
            };
            float[] ys = new float[]{
                bounding.center.y + bounding.size.y/2f,
                bounding.center.y - bounding.size.y/2f
            };
            float[] zs = new float[]{
                bounding.center.z + bounding.size.z/2f,
                bounding.center.z - bounding.size.z/2f
            };
            foreach (float x in xs) {
                foreach (float y in ys) {
                    foreach (float z in zs) {
                        cornerPoints.Add(new float[] { x, y, z });
                    }
                }
            }
            b.cornerPoints = cornerPoints.ToArray();

            b.center = bounding.center;
            b.size = bounding.size;

            return b;
        }

        public virtual MetadataPatch generateMetadataPatch() {
            MetadataPatch patch = new MetadataPatch();
            patch.lastAction = this.lastAction;
            patch.lastActionSuccess = this.lastActionSuccess;
            patch.actionReturn = this.actionReturn;
            if (errorCode != ServerActionErrorCode.Undefined) {
                patch.errorCode = Enum.GetName(typeof(ServerActionErrorCode), errorCode);
            }
            patch.errorMessage = this.errorMessage;
            return patch;
        }

        public virtual MetadataWrapper generateMetadataWrapper() {
            // AGENT METADATA
            AgentMetadata agentMeta = new AgentMetadata();
            agentMeta.name = "agent";
            agentMeta.position = transform.position;
            agentMeta.rotation = transform.eulerAngles;

            float cameraX = m_Camera.transform.rotation.eulerAngles.x;
            agentMeta.cameraHorizon = cameraX > 180 ? cameraX - 360 : cameraX;
            agentMeta.inHighFrictionArea = inHighFrictionArea;

            // OTHER METADATA
            MetadataWrapper metaMessage = new MetadataWrapper();
            metaMessage.agent = agentMeta;
            metaMessage.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            metaMessage.objects = this.generateObjectMetadata();
            metaMessage.isSceneAtRest = physicsSceneManager.isSceneAtRest;
            metaMessage.sceneBounds = GenerateSceneBounds(agentManager.SceneBounds);
            metaMessage.collided = collidedObjects.Length > 0;
            metaMessage.collidedObjects = collidedObjects;
            metaMessage.screenWidth = Screen.width;
            metaMessage.screenHeight = Screen.height;
            metaMessage.cameraPosition = m_Camera.transform.position;
            metaMessage.cameraOrthSize = cameraOrthSize;
            cameraOrthSize = -1f;
            metaMessage.fov = m_Camera.fieldOfView;
            metaMessage.lastAction = lastAction;
            metaMessage.lastActionSuccess = lastActionSuccess;
            metaMessage.errorMessage = errorMessage;
            metaMessage.actionReturn = this.actionReturn;

            if (errorCode != ServerActionErrorCode.Undefined) {
                metaMessage.errorCode = Enum.GetName(typeof(ServerActionErrorCode), errorCode);
            }

            List<InventoryObject> ios = new List<InventoryObject>();

            if (ItemInHand != null) {
                SimObjPhysics so = ItemInHand.GetComponent<SimObjPhysics>();
                InventoryObject io = new InventoryObject();
                io.objectId = so.ObjectID;
                io.objectType = Enum.GetName(typeof(SimObjType), so.Type);
                ios.Add(io);
            }

            metaMessage.inventoryObjects = ios.ToArray();

            // TODO: remove from base.
            // HAND
            metaMessage.hand = new HandMetadata();
            metaMessage.hand.position = AgentHand.transform.position;
            metaMessage.hand.localPosition = AgentHand.transform.localPosition;
            metaMessage.hand.rotation = AgentHand.transform.eulerAngles;
            metaMessage.hand.localRotation = AgentHand.transform.localEulerAngles;

            // TODO: remove from base.
            // ARM
            if (Arm != null) {
                metaMessage.arm = Arm.GenerateMetadata();
            }

            // EXTRAS
            metaMessage.flatSurfacesOnGrid = flatten3DimArray(flatSurfacesOnGrid);
            metaMessage.distances = flatten2DimArray(distances);
            metaMessage.normals = flatten3DimArray(normals);
            metaMessage.isOpenableGrid = flatten2DimArray(isOpenableGrid);
            metaMessage.segmentedObjectIds = segmentedObjectIds;
            metaMessage.objectIdsInBox = objectIdsInBox;
            metaMessage.actionIntReturn = actionIntReturn;
            metaMessage.actionFloatReturn = actionFloatReturn;
            metaMessage.actionFloatsReturn = actionFloatsReturn;
            metaMessage.actionStringsReturn = actionStringsReturn;
            metaMessage.actionVector3sReturn = actionVector3sReturn;

            if (alwaysReturnVisibleRange) {
                metaMessage.visibleRange = visibleRange();
            }

            // test time
            metaMessage.currentTime = TimeSinceStart();

            // Resetting things
            flatSurfacesOnGrid = new float[0, 0, 0];
            distances = new float[0, 0];
            normals = new float[0, 0, 0];
            isOpenableGrid = new bool[0, 0];
            segmentedObjectIds = new string[0];
            objectIdsInBox = new string[0];
            actionIntReturn = 0;
            actionFloatReturn = 0.0f;
            actionFloatsReturn = new float[0];
            actionStringsReturn = new string[0];
            actionVector3sReturn = new Vector3[0];

            return metaMessage;
        }


        public void updateImageSynthesis(bool status) {
            if (this.imageSynthesis == null) {
                imageSynthesis = this.gameObject.GetComponentInChildren<ImageSynthesis>() as ImageSynthesis;
            }
            imageSynthesis.enabled = status;
        }

        // This should only be used by DebugInputField and HideNSeekController
        // Once all those invocations have been converted to Dictionary<string, object>
        // this can be removed
        public void ProcessControlCommand(ServerAction serverAction) {

            errorMessage = "";
            errorCode = ServerActionErrorCode.Undefined;
            collisionsInAction = new List<string>();

            lastAction = serverAction.action;
            lastActionSuccess = false;
            lastPosition = new Vector3(transform.position.x, transform.position.y, transform.position.z);
            System.Reflection.MethodInfo method = this.GetType().GetMethod(serverAction.action);

            this.agentState = AgentState.Processing;
            try {
                if (method == null) {
                    errorMessage = "Invalid action: " + serverAction.action;
                    errorCode = ServerActionErrorCode.InvalidAction;
                    Debug.LogError(errorMessage);
                    actionFinished(false);
                } else {
                    method.Invoke(this, new object[] { serverAction });
                }
            } catch (Exception e) {
                Debug.LogError("Caught error with invoke for action: " + serverAction.action);
                Debug.LogError("Action error message: " + errorMessage);
                Debug.LogError(e);

                errorMessage += e.ToString();
                actionFinished(false);
            }

        }

        // the parameter name is different to avoid failing a test
        // that looks for methods with identical param names, since
        // we dispatch using method + param names
        public void ProcessControlCommand(Dictionary<string, object> actionDict) {
            ProcessControlCommand(new DynamicServerAction(actionDict));
        }

        public void ProcessControlCommand(DynamicServerAction controlCommand) {
            ProcessControlCommand(controlCommand: controlCommand, target: this);
        }

        public void ProcessControlCommand(DynamicServerAction controlCommand, object target) {
            errorMessage = "";
            errorCode = ServerActionErrorCode.Undefined;
            collisionsInAction = new List<string>();

            lastAction = controlCommand.action;
            lastActionSuccess = false;
            lastPosition = new Vector3(transform.position.x, transform.position.y, transform.position.z);
            this.agentState = AgentState.Processing;

            try {
                ActionDispatcher.Dispatch(target: target, dynamicServerAction: controlCommand);
            } catch (InvalidArgumentsException e) {
                errorMessage =
                $"\n\tAction: \"{controlCommand.action}\" called with invalid argument{(e.InvalidArgumentNames.Count() > 1 ? "s" : "")}: {string.Join(", ", e.InvalidArgumentNames.Select(name => $"'{name}'").ToArray())}" +
                $"\n\tExpected arguments: {string.Join(", ", e.ParameterNames)}" +
                $"\n\tYour arguments: {string.Join(", ", e.ArgumentNames.Select(name => $"'{name}'"))}" +
                $"\n\tValid ways to call \"{controlCommand.action}\" action:\n\t\t{string.Join("\n\t\t", e.PossibleOverwrites)}";
                errorCode = ServerActionErrorCode.InvalidArgument;

                var possibleOverwrites = ActionDispatcher.getMatchingMethodOverwrites(target.GetType(), controlCommand);
                actionFinished(false);
            } catch (ToObjectArgumentActionException e) {
                Dictionary<string, string> typeMap = new Dictionary<string, string>{
                    {"Single", "float"},
                    {"Double", "float"},
                    {"Int16", "int"},
                    {"Int32", "int"},
                    {"Int64", "int"}
                };
                Type underlingType = Nullable.GetUnderlyingType(e.parameterType);
                string typeName = underlingType == null ? e.parameterType.Name : underlingType.Name;
                if (typeMap.ContainsKey(typeName)) {
                    typeName = typeMap[typeName];
                }
                errorMessage = $"action: {controlCommand.action} has an invalid argument: {e.parameterName} (=={e.parameterValueAsStr})." +
                    $" Cannot convert to: {typeName}";
                errorCode = ServerActionErrorCode.InvalidArgument;
                actionFinished(false);
            } catch (MissingArgumentsActionException e) {
                errorMessage = "action: " + controlCommand.action + " is missing the following arguments: " + string.Join(",", e.ArgumentNames.ToArray());
                errorCode = ServerActionErrorCode.MissingArguments;
                actionFinished(false);
            } catch (AmbiguousActionException e) {
                errorMessage = "Ambiguous action: " + controlCommand.action + " " + e.Message;
                errorCode = ServerActionErrorCode.AmbiguousAction;
                actionFinished(false);
            } catch (InvalidActionException) {
                errorCode = ServerActionErrorCode.InvalidAction;
                actionFinished(success: false, errorMessage: "Invalid action: " + controlCommand.action);
            } catch (TargetInvocationException e) {
                // TargetInvocationException is called whenever an action
                // throws an exception. It is used to short circuit errors,
                // which terminates the action immediately.
                actionFinished(
                    success: false,
                    errorMessage: $"{e.InnerException.GetType().Name}: {e.InnerException.Message}"
                );
            } catch (Exception e) {
                Debug.LogError("Caught error with invoke for action: " + controlCommand.action);
                Debug.LogError("Action error message: " + errorMessage);
                errorMessage += e.ToString();
                actionFinished(success: false, errorMessage: errorMessage);
            }

            // #if UNITY_EDITOR
            //     if (errorMessage != "") {
            //         Debug.LogError(errorMessage);
            //     }
            // #endif
        }

        // no op action
        public void Pass() {
            actionFinished(true);
        }

        // no op action
        public void Done() {
            actionFinished(true);
        }

        // Handle collisions - CharacterControllers don't apply physics innately, see "PushMode" check below
        // XXX: this will be used for truly continuous movement over time, for now this is unused
        protected void OnControllerColliderHit(ControllerColliderHit hit) {
            if (!enabled) {
                return;
            }

            if (hit.gameObject.GetComponent<StructureObject>()) {
                if (hit.gameObject.GetComponent<StructureObject>().WhatIsMyStructureObjectTag == StructureObjectTag.Floor) {
                    return;
                }
            }


            if (!collisionsInAction.Contains(hit.gameObject.name)) {
                collisionsInAction.Add(hit.gameObject.name);
            }

            Rigidbody body = hit.collider.attachedRigidbody;
            // don't move the rigidbody if the character is on top of it
            if (m_CollisionFlags == CollisionFlags.Below) {
                return;
            }

            if (body == null || body.isKinematic) {
                return;
            }

            // push objects out of the way if moving through them and they are Moveable or CanPickup (Physics)
            if (PushMode) {
                float pushPower = 2.0f;
                Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
                body.velocity = pushDir * pushPower;
            }
            // if we touched something with a rigidbody that needs to simulate physics, generate a force at the impact point
            // body.AddForce(m_CharacterController.velocity * 15f, ForceMode.Force);
            // body.AddForceAtPosition (m_CharacterController.velocity * 15f, hit.point, ForceMode.Acceleration);// might have to adjust the force vector scalar later
        }

        // Helper method that parses objectId parameter to return the sim object that it target.
        // The action is halted if the objectId does not appear in the scene.
        protected SimObjPhysics getTargetObject(string objectId, bool forceAction = false) {
            // an objectId was given, so find that target in the scene if it exists
            if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                throw new ArgumentException($"objectId: {objectId} is not the objectId on any object in the scene!");
            }

            // if object is in the scene and visible, assign it to 'target'
            SimObjPhysics target = getInteractableSimObjectFromId(objectId: objectId, forceVisible: forceAction);

            // target not found!
            if (target == null) {
                throw new NullReferenceException("Target object not found within the specified visibility.");
            }

            return target;
        }

        // Helper method that parses (x and y) parameters to return the
        // sim object that they target.
        protected SimObjPhysics getTargetObject(float x, float y, bool forceAction) {
            if (x < 0 || x > 1 || y < 0 || y > 1) {
                throw new ArgumentOutOfRangeException("x/y must be in [0:1]");
            }

            // reverse the y so that the origin (0, 0) can be passed in as the top left of the screen
            y = 1.0f - y;

            // cast ray from screen coordinate into world space. If it hits an object
            Ray ray = m_Camera.ViewportPointToRay(new Vector3(x, y, 0));
            RaycastHit hit;

            bool hitObject = Physics.Raycast(
                ray: ray,
                hitInfo: out hit,
                maxDistance: Mathf.Infinity,
                layerMask: LayerMask.GetMask("Default", "SimObjVisible", "Agent", "PlaceableSurface"),
                queryTriggerInteraction: QueryTriggerInteraction.Ignore
            );

            if (!hitObject || hit.transform.GetComponent<SimObjPhysics>() == null) {
                throw new InvalidOperationException($"No SimObject found at (x: {x}, y: {y})");
            }

            SimObjPhysics target = hit.transform.GetComponent<SimObjPhysics>();

            if (!forceAction && !isPosInView(targetPosition: hit.point)) {
                throw new InvalidOperationException(
                    $"Target sim object: ({target.ObjectID}) at screen coordinate: ({x}, {y}) is beyond your visibilityDistance: {maxVisibleDistance}!\n" +
                    "Hint: Ignore this check by passing in forceAction=True or update visibility distance, call controller.reset(visibilityDistance=<new visibility distance>)."
                );
            }
            return target;
        }

        // checks if the target position in space is within the agent's current viewport
        // and/or within the max visible distance
        protected bool isPosInView(Vector3 targetPosition, bool inViewport = true, bool inMaxVisibleDistance = true) {
            // now check if the target position is within bounds of the Agent's forward (z) view
            Vector3 tmp = m_Camera.transform.position;
            tmp.y = targetPosition.y;

            if (inMaxVisibleDistance && Vector3.Distance(tmp, targetPosition) > maxVisibleDistance) {
                errorMessage = "target is outside of maxVisibleDistance";
                return false;
            }

            // now make sure that the targetPosition is within the Agent's x/y view, restricted by camera
            Vector3 vp = m_Camera.WorldToViewportPoint(targetPosition);
            if (inViewport && (vp.z < 0 || vp.x > 1.0f || vp.y < 0.0f || vp.y > 1.0f || vp.y < 0.0f)) {
                errorMessage = "target is outside of Agent Viewport";
                return false;
            }

            return true;
        }

        protected bool isPosInView(Vector3 targetPosition, ref SimObjPhysics target, float x, float y, bool inViewport = true, bool inMaxVisibleDistance = true) {
            bool result = isPosInView(
                targetPosition: targetPosition,
                inViewport: inViewport,
                inMaxVisibleDistance: inMaxVisibleDistance);

            if (errorMessage == "target is outside of maxVisibleDistance") {
                errorMessage = $"target hit ({target.objectID}) at ({x}, {y}) is outside the Agent's maxVisibleDistance range";
                target = null;

            }

            if (errorMessage == "target is outside of AgentViewport") {
                errorMessage = $"target hit ({target.objectID}) at ({x}, {y}) is outside the agent's viewport";
                target = null;
            }

            return result;
        }

        protected bool screenToWorldTarget(
            float x,
            float y,
            ref SimObjPhysics target,
            bool forceAction = false,
            bool checkVisible = true) {

            // this version doesn't use a RaycastHit, so pass just a defualt one
            RaycastHit hit = new RaycastHit();

            return screenToWorldTarget(
                x: x,
                y: y,
                target: ref target,
                forceAction: forceAction,
                hit: out hit);
        }

        // used for all actions that need a sim object target
        // instead of objectId, use screen coordinates to raycast toward potential targets
        // will set the target object by reference if raycast is successful
        protected bool screenToWorldTarget(
            float x,
            float y,
            ref SimObjPhysics target,
            out RaycastHit hit,
            bool forceAction = false,
            bool checkVisible = true) {
            if (x < 0 || x > 1 || y < 0 || y > 1) {
                throw new ArgumentOutOfRangeException("x/y must be in [0:1]");
            }

            // reverse the y so that the origin (0, 0) can be passed in as the top left of the screen
            y = 1.0f - y;

            // cast ray from screen coordinate into world space. If it hits an object
            Ray ray = m_Camera.ViewportPointToRay(new Vector3(x, y, 0.0f));

            // check if something was hit by raycast
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, 1 << 0 | 1 << 8 | 1 << 10 | 1 << 11, QueryTriggerInteraction.Ignore)) {

                // DEBUG STUFF PLEASE COMMENT OUT UNLESS USING//////
                // GameObject empty = new GameObject("empty");
                // Instantiate(empty, hit.point, Quaternion.identity);
                // GameObject.Destroy(empty);
                ///////////////////////////////////////

                if (hit.transform.GetComponentInParent<SimObjPhysics>()) {

                    target = hit.transform.GetComponentInParent<SimObjPhysics>();

                    // if not in view, target passed by ref will be set to null after error message generation
                    if (!isPosInView(
                        targetPosition: hit.point,
                        inMaxVisibleDistance: false,
                        inViewport: true,
                        x: x,
                        y: y,
                        target: ref target)) {
                        return false;
                    }

                    // now check if the object is flagged as Visible by the visibility point logic
                    if (checkVisible && !forceAction && !IsInteractable(target)) {
                        // the potential target sim object hit by the ray is not currently visible to the agent
                        errorMessage = $"target hit ({target.objectID}) at ({x}, {y}) is not currently Visible to Agent";
                        target = null;
                        return false;
                    }
                }

                // object hit was not a sim object
                else {
                    errorMessage = $"no sim objects found at ({x},{y})";
                    return false;
                }

                // something was hit by the raycast, but one of the checks failed
                if (errorMessage != "") {
                    // errorMessage should be set in isPosInView
                    return false;
                }
            }

            // target should have been assigned so we are good to go
            return true;
        }

        // returns whether an object hit at (x,y) screen coordinates is in the camera viewport
        // if checkVisible = true, it will also check if the object hit is visible to the agent
        public void GetObjectInFrame(float x, float y, bool checkVisible = false) {
            SimObjPhysics target = null;
            screenToWorldTarget(
                x: x,
                y: y,
                target: ref target,
                checkVisible: checkVisible);
            // if checkVisible is true and target is found, the object is also interactable to the agent
            // this does not account for objects behind transparent objects like shower glass, as the raycast check
            // will hit the transparent object FIRST

            if (target != null) {
                actionFinishedEmit(success: true, actionReturn: target.ObjectID);
            }

            if (target == null) {
                actionFinishedEmit(success: false, actionReturn: errorMessage);
            }
        }

        public void GetCoordinateFromRaycast(float x, float y) {
            if (x < 0 || y < 0 || x > 1 || y > 1) {
                throw new ArgumentOutOfRangeException($"x and y must be in [0:1] not (x={x}, y={y}).");
            }

            Ray ray = m_Camera.ViewportPointToRay(new Vector3(x, 1 - y, 0));
            RaycastHit hit;
            Physics.Raycast(
                ray: ray,
                hitInfo: out hit,
                maxDistance: Mathf.Infinity,
                layerMask: LayerMask.GetMask("Default", "Agent", "SimObjVisible", "PlaceableSurface"),
                queryTriggerInteraction: QueryTriggerInteraction.Ignore
            );

            actionFinishedEmit(
                success: true,
                actionReturn: hit.point
            );
        }

        protected void snapAgentToGrid() {
            if (this.snapToGrid) {
                float mult = 1 / gridSize;
                float gridX = Convert.ToSingle(Math.Round(this.transform.position.x * mult) / mult);
                float gridZ = Convert.ToSingle(Math.Round(this.transform.position.z * mult) / mult);

                this.transform.position = new Vector3(gridX, transform.position.y, gridZ);
            }
        }

        protected bool isPositionOnGrid(Vector3 xyz) {
            if (this.snapToGrid) {
                float mult = 1 / gridSize;
                float gridX = Convert.ToSingle(Math.Round(xyz.x * mult) / mult);
                float gridZ = Convert.ToSingle(Math.Round(xyz.z * mult) / mult);

                return (
                    Mathf.Approximately(gridX, xyz.x) &&
                    Mathf.Approximately(gridZ, xyz.z)
                );
            } else {
                return true;
            }
        }

        // move in cardinal directions
        virtual protected void moveCharacter(ServerAction action, int targetOrientation) {
            // TODO: Simplify this???
            // resetHand(); when I looked at this resetHand in DiscreteRemoteFPSAgent was just commented out doing nothing so...
            moveMagnitude = gridSize;
            if (action.moveMagnitude > 0) {
                moveMagnitude = action.moveMagnitude;
            }
            int currentRotation = (int)Math.Round(transform.rotation.eulerAngles.y, 0);
            Dictionary<int, Vector3> actionOrientation = new Dictionary<int, Vector3>();
            actionOrientation.Add(0, new Vector3(0f, 0f, 1.0f));
            actionOrientation.Add(90, new Vector3(1.0f, 0.0f, 0.0f));
            actionOrientation.Add(180, new Vector3(0f, 0f, -1.0f));
            actionOrientation.Add(270, new Vector3(-1.0f, 0.0f, 0.0f));
            int delta = (currentRotation + targetOrientation) % 360;

            Vector3 m;
            if (actionOrientation.ContainsKey(delta)) {
                m = actionOrientation[delta];

            } else {
                actionOrientation = new Dictionary<int, Vector3>();
                actionOrientation.Add(0, transform.forward);
                actionOrientation.Add(90, transform.right);
                actionOrientation.Add(180, transform.forward * -1);
                actionOrientation.Add(270, transform.right * -1);
                m = actionOrientation[targetOrientation];
            }

            m *= moveMagnitude;

            m.y = Physics.gravity.y * this.m_GravityMultiplier;
            m_CharacterController.Move(m);
            actionFinished(true);
            // StartCoroutine(checkMoveAction(action));
        }

        // do not use this base version, use the override from PhysicsRemote or Stochastic
        public virtual void MoveLeft(ServerAction action) {
            moveCharacter(action, 270);
        }

        public virtual void MoveRight(ServerAction action) {
            moveCharacter(action, 90);
        }

        public virtual void MoveAhead(ServerAction action) {
            moveCharacter(action, 0);
        }

        public virtual void MoveBack(ServerAction action) {
            moveCharacter(action, 180);
        }

        // overwritten by stochastic
        public virtual void MoveRelative(ServerAction action) {
            var moveLocal = new Vector3(action.x, 0, action.z);
            Vector3 moveWorldSpace = transform.rotation * moveLocal;
            moveWorldSpace.y = Physics.gravity.y * this.m_GravityMultiplier;
            m_CharacterController.Move(moveWorldSpace);
            actionFinished(true);
        }

        // free rotate, change forward facing of Agent
        // this is currently overwritten by Rotate in Stochastic Controller
        public virtual void Rotate(Vector3 rotation) {
            transform.rotation = Quaternion.Euler(new Vector3(0.0f, rotation.y, 0.0f));
            actionFinished(true);
        }

        // rotates controlCommand.degrees degrees left w/ respect to current forward
        public virtual void RotateLeft(ServerAction controlCommand) {
            transform.Rotate(0, -controlCommand.degrees, 0);
            actionFinished(true);
        }

        // rotates controlCommand.degrees degrees right w/ respect to current forward
        public virtual void RotateRight(ServerAction controlCommand) {
            transform.Rotate(0, controlCommand.degrees, 0);
            actionFinished(true);
        }

        // iterates to next allowed downward horizon angle for AgentCamera (max 60 degrees down)
        public virtual void LookDown(ServerAction controlCommand) {
            m_Camera.transform.Rotate(controlCommand.degrees, 0, 0);
            actionFinished(true);
        }

        // iterates to next allowed upward horizon angle for agent camera (max 30 degrees up)
        public virtual void LookUp(ServerAction controlCommand) {
            m_Camera.transform.Rotate(-controlCommand.degrees, 0, 0);
            actionFinished(true);
        }

        protected bool checkForUpDownAngleLimit(string direction, float degrees) {
            bool result = true;
            // check the angle between the agent's forward vector and the proposed rotation vector
            // if it exceeds the min/max based on if we are rotating up or down, return false

            // first move the rotPoint to the camera
            rotPoint.transform.position = m_Camera.transform.position;
            // zero out the rotation first
            rotPoint.transform.rotation = m_Camera.transform.rotation;


            // print(Vector3.Angle(rotPoint.transform.forward, m_CharacterController.transform.forward));
            if (direction == "down") {
                rotPoint.Rotate(new Vector3(degrees, 0, 0));
                // note: maxDownwardLookAngle is negative because SignedAngle() returns a... signed angle... so even though the input is LookDown(degrees) with
                // degrees being positive, it still needs to check against this negatively signed direction.
                if (Mathf.Round(Vector3.SignedAngle(rotPoint.transform.forward, m_CharacterController.transform.forward, m_CharacterController.transform.right) * 10.0f) / 10.0f < -maxDownwardLookAngle) {
                    result = false;
                }
            }

            if (direction == "up") {
                rotPoint.Rotate(new Vector3(-degrees, 0, 0));
                if (Mathf.Round(Vector3.SignedAngle(rotPoint.transform.forward, m_CharacterController.transform.forward, m_CharacterController.transform.right) * 10.0f) / 10.0f > maxUpwardLookAngle) {
                    result = false;
                }
            }
            return result;
        }

        ///////////////////////////////////////////
        //////////////// TELEPORT /////////////////
        ///////////////////////////////////////////

        // As opposed to an action, these args are required because we explicitly
        // want base classes to pass all of them in.
        protected void teleport(
            Vector3? position, Vector3? rotation, float? horizon, bool forceAction
        ) {
            teleportFull(
                position: position == null ? transform.position : (Vector3)position,
                rotation: rotation == null ? transform.localEulerAngles : (Vector3)rotation,
                horizon: horizon == null ? m_Camera.transform.localEulerAngles.x : (float)horizon,
                forceAction: forceAction
            );
        }

        ///////////////////////////////////////////
        ////////////// TELEPORT FULL //////////////
        ///////////////////////////////////////////

        // this is not used with non-grounded agents (e.g., drones)
        protected void assertTeleportedNearGround(Vector3? targetPosition) {
            // position should not change if it's null.
            if (targetPosition == null) {
                return;
            }

            Vector3 pos = (Vector3)targetPosition;
            // we must sync the rigidbody prior to executing the
            // move otherwise the agent will end up in a different
            // location from the targetPosition
            autoSyncTransforms();
            m_CharacterController.Move(new Vector3(0f, Physics.gravity.y * this.m_GravityMultiplier, 0f));

            // perhaps like y=2 was specified, with an agent's standing height of 0.9
            if (Mathf.Abs(transform.position.y - pos.y) > 0.05f) {
                throw new InvalidOperationException(
                    "After teleporting and adjusting agent position to floor, there was too large a change" +
                    $"({Mathf.Abs(transform.position.y - pos.y)} > 0.05) in the y component." +
                    " Consider using `forceAction=true` if you'd like to teleport anyway."
                );
            }
        }

        protected void teleportFull(
            Vector3 position, Vector3 rotation, float horizon, bool forceAction
        ) {
            // Note: using Mathf.Approximately uses Mathf.Epsilon, which is significantly
            // smaller than 1e-2f. I'm not confident that will work in many cases.
            if (!forceAction && (Mathf.Abs(rotation.x) >= 1e-2f || Mathf.Abs(rotation.z) >= 1e-2f)) {
                throw new ArgumentOutOfRangeException(
                    "No agents currently can change in pitch or roll. So, you must set rotation(x=0, y=yaw, z=0)." +
                    $" You gave {rotation.ToString("F6")}."
                );
            }

            // recall that horizon=60 is look down 60 degrees and horizon=-30 is look up 30 degrees
            if (!forceAction && (horizon > maxDownwardLookAngle || horizon < -maxUpwardLookAngle)) {
                throw new ArgumentOutOfRangeException(
                    $"Each horizon must be in [{-maxUpwardLookAngle}:{maxDownwardLookAngle}]. You gave {horizon}."
                );
            }

            if (!forceAction && !agentManager.SceneBounds.Contains(position)) {
                throw new ArgumentOutOfRangeException(
                    $"Teleport position {position.ToString("F6")} out of scene bounds! Ignore this by setting forceAction=true."
                );
            }

            if (!forceAction && !isPositionOnGrid(position)) {
                throw new ArgumentOutOfRangeException(
                    $"Teleport position {position.ToString("F6")} is not on the grid of size {gridSize}."
                );
            }

            // cache old values in case there's a failure
            Vector3 oldPosition = transform.position;
            Quaternion oldRotation = transform.rotation;
            float oldHorizon = m_Camera.transform.localEulerAngles.x;

            // here we actually teleport 
            transform.position = position;
            transform.localEulerAngles = new Vector3(0, rotation.y, 0);
            m_Camera.transform.localEulerAngles = new Vector3(horizon, 0, 0);

            if (!forceAction &&
                isAgentCapsuleColliding(
                    collidersToIgnore: collidersToIgnoreDuringMovement, includeErrorMessage: true
                )
            ) {
                transform.position = oldPosition;
                transform.rotation = oldRotation;
                m_Camera.transform.localEulerAngles = new Vector3(oldHorizon, 0, 0);
                throw new InvalidOperationException(errorMessage);
            }
        }

        protected T[] flatten2DimArray<T>(T[,] array) {
            int nrow = array.GetLength(0);
            int ncol = array.GetLength(1);
            T[] flat = new T[nrow * ncol];
            for (int i = 0; i < nrow; i++) {
                for (int j = 0; j < ncol; j++) {
                    flat[i * ncol + j] = array[i, j];
                }
            }
            return flat;
        }

        protected T[] flatten3DimArray<T>(T[,,] array) {
            int n0 = array.GetLength(0);
            int n1 = array.GetLength(1);
            int n2 = array.GetLength(2);
            T[] flat = new T[n0 * n1 * n2];
            for (int i = 0; i < n0; i++) {
                for (int j = 0; j < n1; j++) {
                    for (int k = 0; k < n2; k++) {
                        flat[i * n1 * n2 + j * n2 + k] = array[i, j, k];
                    }
                }
            }
            return flat;
        }

        protected List<Vector3> visibleRange() {
            int n = 5;
            List<Vector3> points = new List<Vector3>();
            points.Add(transform.position);
            updateAllAgentCollidersForVisibilityCheck(false);
            for (int i = 0; i < n; i++) {
                for (int j = 0; j < n; j++) {
                    RaycastHit hit;
                    Ray ray = m_Camera.ViewportPointToRay(new Vector3(
                        (i + 0.5f) / n, (j + 0.5f) / n, 0.0f));
                    if (Physics.Raycast(ray, out hit, 100f, (1 << 8) | (1 << 10))) {
                        points.Add(hit.point);
                    }
                }
            }
            updateAllAgentCollidersForVisibilityCheck(true);
            return points;
        }

        //*** Maybe make this better */
        // This function should be called before and after doing a visibility check (before with
        // enableColliders == false and after with it equaling true). It, in particular, will
        // turn off/on all the colliders on agents which should not block visibility for the current agent
        // (invisible agents for example).
        protected void updateAllAgentCollidersForVisibilityCheck(bool enableColliders) {
            foreach (BaseFPSAgentController agent in this.agentManager.agents) {
                bool overlapping = (transform.position - agent.transform.position).magnitude < 0.001f;
                if (overlapping || agent == this || !agent.IsVisible) {
                    foreach (Collider c in agent.GetComponentsInChildren<Collider>()) {
                        if (ItemInHand == null || !hasAncestor(c.transform.gameObject, ItemInHand)) {
                            // if we are in arm mode, also do the ItemInHand collider re-enable check
                            // for all objects that might be currently picked up by the arm
                            if (Arm != null) {
                                foreach (SimObjPhysics inArmHand in Arm.heldObjects.Keys) {
                                    // print("here");
                                    if (!hasAncestor(c.transform.gameObject, inArmHand.transform.gameObject)) {
                                        c.enabled = enableColliders;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected bool hasAncestor(GameObject child, GameObject potentialAncestor) {
            if (child == potentialAncestor) {
                return true;
            } else if (child.transform.parent != null) {
                return hasAncestor(child.transform.parent.gameObject, potentialAncestor);
            } else {
                return false;
            }
        }

        protected bool ancestorHasName(GameObject go, string name) {
            if (go.name == name) {
                return true;
            } else if (go.transform.parent != null) {
                return ancestorHasName(go.transform.parent.gameObject, name);
            } else {
                return false;
            }
        }

        protected static SimObjPhysics ancestorSimObjPhysics(GameObject go) {
            if (go == null) {
                return null;
            }
            SimObjPhysics so = go.GetComponent<SimObjPhysics>();
            if (so != null) {
                return so;
            } else if (go.transform.parent != null) {
                return ancestorSimObjPhysics(go.transform.parent.gameObject);
            } else {
                return null;
            }
        }

        public void VisibleRange() {
            actionFinished(true, visibleRange());
        }

        public float TimeSinceStart() {
            return Time.time;
        }

        protected bool objectIsWithinViewport(SimObjPhysics sop) {
            if (sop.VisibilityPoints.Length > 0) {
                Transform[] visPoints = sop.VisibilityPoints;
                foreach (Transform point in visPoints) {
                    Vector3 viewPoint = m_Camera.WorldToViewportPoint(point.position);
                    float ViewPointRangeHigh = 1.0f;
                    float ViewPointRangeLow = 0.0f;

                    if (viewPoint.z > 0 &&
                        viewPoint.x < ViewPointRangeHigh && viewPoint.x > ViewPointRangeLow && // within x bounds of viewport
                        viewPoint.y < ViewPointRangeHigh && viewPoint.y > ViewPointRangeLow // within y bounds of viewport
                    ) {
                        return true;
                    }
                }
            } else {
#if UNITY_EDITOR
                Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics prefab!");
#endif
            }
            return false;
        }

        private bool isSimObjVisible(Camera agentCamera, SimObjPhysics sop, float maxDistance) {
            bool visible = false;
            // check against all visibility points, accumulate count. If at least one point is visible, set object to visible
            if (sop.VisibilityPoints != null && sop.VisibilityPoints.Length > 0) {
                Transform[] visPoints = sop.VisibilityPoints;
                int visPointCount = 0;

                foreach (Transform point in visPoints) {
                    float xdelta = Math.Abs(this.transform.position.x - point.position.x);
                    if (xdelta > maxDistance) {
                        continue;
                    }

                    float zdelta = Math.Abs(this.transform.position.z - point.position.z);
                    if (zdelta > maxDistance) {
                        continue;
                    }

                    // if the object is too far above the Agent, skip
                    float ydelta = point.position.y - this.transform.position.y;
                    if (ydelta > maxDistance) {
                        continue;
                    }

                    double distance = Math.Sqrt((xdelta * xdelta) + (zdelta * zdelta));
                    if (distance > maxDistance) {
                        continue;
                    }

                    // if this particular point is in view...
                    if (CheckIfVisibilityPointInViewport(sop, point, agentCamera, sop.IsReceptacle)) {
                        visPointCount++;
#if !UNITY_EDITOR
                        // If we're in the unity editor then don't break on finding a visible
                        // point as we want to draw lines to each visible point.
                        break;
#endif
                    }
                }

                // if we see at least one vis point, the object is "visible"
                if (visPointCount > 0) {
#if UNITY_EDITOR
                    sop.debugIsVisible = true;
#endif
                    visible = true;
                }
            } else {
                Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics " + sop + ".");
            }
            return visible;
        }

        private bool isSimObjVisible(Camera agentCamera, SimObjPhysics sop, float maxDistance, Plane[] planes) {
            bool visible = false;
            // check against all visibility points, accumulate count. If at least one point is visible, set object to visible
            if (sop.VisibilityPoints != null && sop.VisibilityPoints.Length > 0) {
                Transform[] visPoints = sop.VisibilityPoints;
                int visPointCount = 0;

                foreach (Transform point in visPoints) {
                    bool outsidePlane = false;
                    for (int i = 0; i < planes.Length; i++) {
                        if (!planes[i].GetSide(point.position)) {
                            outsidePlane = true;
                            break;
                        }
                    }

                    if (outsidePlane) {
                        continue;
                    }


                    float xdelta = Math.Abs(this.transform.position.x - point.position.x);
                    if (xdelta > maxDistance) {
                        continue;
                    }

                    float zdelta = Math.Abs(this.transform.position.z - point.position.z);
                    if (zdelta > maxDistance) {
                        continue;
                    }

                    // if the object is too far above the Agent, skip
                    float ydelta = point.position.y - this.transform.position.y;
                    if (ydelta > maxDistance) {
                        continue;
                    }

                    double distance = Math.Sqrt((xdelta * xdelta) + (zdelta * zdelta));
                    if (distance > maxDistance) {
                        continue;
                    }

                    // if this particular point is in view...
                    if (CheckIfVisibilityPointRaycast(sop, point, agentCamera, false) ||
                        CheckIfVisibilityPointRaycast(sop, point, agentCamera, true)) {
                        visPointCount++;
#if !UNITY_EDITOR
                        // If we're in the unity editor then don't break on finding a visible
                        // point as we want to draw lines to each visible point.
                        break;
#endif
                    }
                }

                // if we see at least one vis point, the object is "visible"
                if (visPointCount > 0) {
#if UNITY_EDITOR
                    sop.debugIsVisible = true;
#endif
                    visible = true;
                }
            } else {
                Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics " + sop + ".");
            }
            return visible;
        }

        // pass in forceVisible bool to force grab all objects of type sim obj
        // if not, gather all visible sim objects maxVisibleDistance away from camera view
        public SimObjPhysics[] VisibleSimObjs(bool forceVisible = false) {
            if (forceVisible) {
                return GameObject.FindObjectsOfType(typeof(SimObjPhysics)) as SimObjPhysics[];
            } else {
                return GetAllVisibleSimObjPhysics(m_Camera, maxVisibleDistance);
            }
        }
        protected SimObjPhysics[] GetAllVisibleSimObjPhysics(Camera agentCamera, float maxDistance, IEnumerable<SimObjPhysics> filterSimObjs = null) {

            if (this.visibilityScheme == VisibilityScheme.Collider) {
                return GetAllVisibleSimObjPhysicsCollider(agentCamera, maxDistance, filterSimObjs);
            } else {
                return GetAllVisibleSimObjPhysicsDistance(agentCamera, maxDistance, filterSimObjs);
            }
        }

        // this is a faster version of the visibility check, but is not entirely
        // consistent with the collider based method.  In particular, if an object
        // is within range of the maxVisibleDistance, but obscurred only within this
        // range and is visibile outside of the range, it will get reported as invisible
        // by the new scheme, but visible in the current scheme.
        private SimObjPhysics[] GetAllVisibleSimObjPhysicsDistance(Camera agentCamera, float maxDistance, IEnumerable<SimObjPhysics> filterSimObjs) {
            if (filterSimObjs == null) {
                filterSimObjs = physicsSceneManager.ObjectIdToSimObjPhysics.Values;
            }

            List<SimObjPhysics> visible = new List<SimObjPhysics>();
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(agentCamera);
            foreach (var sop in filterSimObjs) {
                if (isSimObjVisible(agentCamera, sop, this.maxVisibleDistance, planes)) {
                    visible.Add(sop);
                }
            }
            return visible.ToArray();
        }

        private SimObjPhysics[] GetAllVisibleSimObjPhysicsCollider(Camera agentCamera, float maxDistance, IEnumerable<SimObjPhysics> filterSimObjs) {
            List<SimObjPhysics> currentlyVisibleItems = new List<SimObjPhysics>();

#if UNITY_EDITOR
            foreach (KeyValuePair<string, SimObjPhysics> pair in physicsSceneManager.ObjectIdToSimObjPhysics) {
                // Set all objects to not be visible
                pair.Value.debugIsVisible = false;
            }
#endif

            HashSet<SimObjPhysics> filter = null;
            if (filterSimObjs != null) {
                filter = new HashSet<SimObjPhysics>(filterSimObjs);
                if (filter.Count == 0) {
                    return currentlyVisibleItems.ToArray();
                }
            }

            Vector3 agentCameraPos = agentCamera.transform.position;

            // get all sim objects in range around us that have colliders in layer 8 (visible), ignoring objects in the SimObjInvisible layer
            // this will make it so the receptacle trigger boxes don't occlude the objects within them.
            CapsuleCollider agentCapsuleCollider = GetComponent<CapsuleCollider>();
            Vector3 point0, point1;
            float radius;
            agentCapsuleCollider.ToWorldSpaceCapsule(out point0, out point1, out radius);
            if (point0.y <= point1.y) {
                point1.y += maxDistance;
            } else {
                point0.y += maxDistance;
            }

            // Turn off the colliders corresponding to this agent
            // and any invisible agents.
            updateAllAgentCollidersForVisibilityCheck(false);

            Collider[] colliders_in_view = Physics.OverlapCapsule(point0, point1, maxDistance, 1 << 8, QueryTriggerInteraction.Collide);

            if (colliders_in_view != null) {
                HashSet<SimObjPhysics> testedSops = new HashSet<SimObjPhysics>();
                foreach (Collider item in colliders_in_view) {
                    SimObjPhysics sop = ancestorSimObjPhysics(item.gameObject);
                    // now we have a reference to our sim object
                    if ((sop != null && !testedSops.Contains(sop)) && (filter == null || filter.Contains(sop))) {
                        testedSops.Add(sop);
                        // check against all visibility points, accumulate count. If at least one point is visible, set object to visible
                        if (sop.VisibilityPoints != null && sop.VisibilityPoints.Length > 0) {
                            Transform[] visPoints = sop.VisibilityPoints;
                            int visPointCount = 0;

                            foreach (Transform point in visPoints) {
                                // if this particular point is in view...
                                if (CheckIfVisibilityPointInViewport(sop, point, agentCamera, false)) {
                                    visPointCount++;
#if !UNITY_EDITOR
                                    // If we're in the unity editor then don't break on finding a visible
                                    // point as we want to draw lines to each visible point.
                                    break;
#endif
                                }
                            }

                            // if we see at least one vis point, the object is "visible"
                            if (visPointCount > 0) {
#if UNITY_EDITOR
                                sop.debugIsVisible = true;
#endif
                                if (!currentlyVisibleItems.Contains(sop)) {
                                    currentlyVisibleItems.Add(sop);
                                }
                            }
                        } else {
                            Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics " + sop + ".");
                        }
                    }
                }
            }

            // check against anything in the invisible layers that we actually want to have occlude things in this round.
            // normally receptacle trigger boxes must be ignored from the visibility check otherwise objects inside them will be occluded, but
            // this additional check will allow us to see inside of receptacle objects like cabinets/fridges by checking for that interior
            // receptacle trigger box. Oh boy!
            Collider[] invisible_colliders_in_view = Physics.OverlapCapsule(point0, point1, maxDistance, 1 << 9, QueryTriggerInteraction.Collide);

            if (invisible_colliders_in_view != null) {
                foreach (Collider item in invisible_colliders_in_view) {
                    if (item.tag == "Receptacle") {
                        SimObjPhysics sop;

                        sop = item.GetComponentInParent<SimObjPhysics>();

                        // now we have a reference to our sim object
                        if (sop && (filter == null || filter.Contains(sop))) {
                            // check against all visibility points, accumulate count. If at least one point is visible, set object to visible
                            if (sop.VisibilityPoints.Length > 0) {
                                Transform[] visPoints = sop.VisibilityPoints;
                                int visPointCount = 0;

                                foreach (Transform point in visPoints) {
                                    // if this particular point is in view...
                                    if (CheckIfVisibilityPointInViewport(sop, point, agentCamera, true)) {
                                        visPointCount++;
                                    }
                                }

                                // if we see at least one vis point, the object is "visible"
                                if (visPointCount > 0) {
#if UNITY_EDITOR
                                    sop.debugIsVisible = true;
#endif
                                    if (!currentlyVisibleItems.Contains(sop)) {
                                        currentlyVisibleItems.Add(sop);
                                    }
                                }
                            } else {
                                Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics prefab!");
                            }

                        }
                    }
                }
            }

            // Turn back on the colliders corresponding to this agent and invisible agents.
            updateAllAgentCollidersForVisibilityCheck(true);

            // populate array of visible items in order by distance
            currentlyVisibleItems.Sort((x, y) => Vector3.Distance(x.transform.position, agentCameraPos).CompareTo(Vector3.Distance(y.transform.position, agentCameraPos)));
            return currentlyVisibleItems.ToArray();
        }

        // check if the visibility point on a sim object, sop, is within the viewport
        // has a inclueInvisible bool to check against triggerboxes as well, to check for visibility with things like Cabinets/Drawers
        protected bool CheckIfVisibilityPointRaycast(
            SimObjPhysics sop,
            Transform point,
            Camera agentCamera,
            bool includeInvisible) {
            bool result = false;
            // now cast a ray out toward the point, if anything occludes this point, that point is not visible
            RaycastHit hit;

            float distFromPointToCamera = Vector3.Distance(point.position, m_Camera.transform.position);

            // adding slight buffer to this distance to ensure the ray goes all the way to the collider of the object being cast to
            float raycastDistance = distFromPointToCamera + 0.5f;

            LayerMask mask = (1 << 8) | (1 << 9) | (1 << 10);

            // change mask if its a floor so it ignores the receptacle trigger boxes on the floor
            if (sop.Type == SimObjType.Floor) {
                mask = (1 << 8) | (1 << 10);
            }


            // check raycast against both visible and invisible layers, to check against ReceptacleTriggerBoxes which are normally
            // ignored by the other raycast
            if (includeInvisible) {
                if (Physics.Raycast(agentCamera.transform.position, point.position - agentCamera.transform.position, out hit, raycastDistance, mask)) {
                    if (hit.transform != sop.transform) {
                        result = false;
                    }

                    // if this line is drawn, then this visibility point is in camera frame and not occluded
                    // might want to use this for a targeting check as well at some point....
                    else {
                        result = true;
                        sop.debugIsInteractable = true;

#if UNITY_EDITOR
                        Debug.DrawLine(agentCamera.transform.position, point.position, Color.cyan);
#endif
                    }
                }
            }

            // only check against the visible layer, ignore the invisible layer
            // so if an object ONLY has colliders on it that are not on layer 8, this raycast will go through them
            else {
                if (Physics.Raycast(agentCamera.transform.position, point.position - agentCamera.transform.position, out hit, raycastDistance, (1 << 8) | (1 << 10))) {
                    if (hit.transform != sop.transform) {
                        // we didn't directly hit the sop we are checking for with this cast,
                        // check if it's because we hit something see-through
                        SimObjPhysics hitSop = hit.transform.GetComponent<SimObjPhysics>();
                        if (hitSop != null && hitSop.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.CanSeeThrough)) {
                            // we hit something see through, so now find all objects in the path between
                            // the sop and the camera
                            RaycastHit[] hits;
                            hits = Physics.RaycastAll(agentCamera.transform.position, point.position - agentCamera.transform.position,
                                raycastDistance, (1 << 8), QueryTriggerInteraction.Ignore);

                            float[] hitDistances = new float[hits.Length];
                            for (int i = 0; i < hitDistances.Length; i++) {
                                hitDistances[i] = hits[i].distance; // Vector3.Distance(hits[i].transform.position, m_Camera.transform.position);
                            }

                            Array.Sort(hitDistances, hits);

                            foreach (RaycastHit h in hits) {

                                if (h.transform == sop.transform) {
                                    // found the object we are looking for, great!
                                    result = true;
                                    break;
                                } else {
                                    // Didn't find it, continue on only if the hit object was translucent
                                    SimObjPhysics sopHitOnPath = null;
                                    sopHitOnPath = h.transform.GetComponentInParent<SimObjPhysics>();
                                    if (sopHitOnPath == null || !sopHitOnPath.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.CanSeeThrough)) {
                                        break;
                                    }
                                }
                            }
                        }
                    } else {
                        // if this line is drawn, then this visibility point is in camera frame and not occluded
                        // might want to use this for a targeting check as well at some point....
                        result = true;
                        sop.debugIsInteractable = true;
                    }
                }
            }

            return result;
        }

        protected bool CheckIfVisibilityPointInViewport(
            SimObjPhysics sop,
            Transform point,
            Camera agentCamera,
            bool includeInvisible
        ) {
            bool result = false;

            Vector3 viewPoint = agentCamera.WorldToViewportPoint(point.position);

            float ViewPointRangeHigh = 1.0f;
            float ViewPointRangeLow = 0.0f;

            if (viewPoint.z > 0 //&& viewPoint.z < maxDistance * DownwardViewDistance // is in front of camera and within range of visibility sphere
                &&
                viewPoint.x < ViewPointRangeHigh && viewPoint.x > ViewPointRangeLow // within x bounds of viewport
                &&
                viewPoint.y < ViewPointRangeHigh && viewPoint.y > ViewPointRangeLow) // within y bounds of viewport
            {
                result = CheckIfVisibilityPointRaycast(sop, point, agentCamera, includeInvisible);
            }

#if UNITY_EDITOR
            if (result == true) {
                Debug.DrawLine(agentCamera.transform.position, point.position, Color.cyan);
            }
#endif

            return result;
        }

        public void DefaultAgentHand() {
            ResetAgentHandPosition();
            ResetAgentHandRotation();
            IsHandDefault = true;
        }

        public void ResetAgentHandPosition() {
            AgentHand.transform.position = DefaultHandPosition.transform.position;
        }

        public void ResetAgentHandRotation() {
            AgentHand.transform.rotation = this.transform.rotation;
        }

        // randomly repositions sim objects in the current scene
        public void InitialRandomSpawn(
            int randomSeed = 0,
            bool forceVisible = false,
            bool placeStationary = true,
            ObjectTypeCount[] numDuplicatesOfType = null,
            String[] excludedReceptacles = null,
            String[] excludedObjectIds = null,
            int numPlacementAttempts = 5,
            bool allowFloor = false
        ) {
            if (numPlacementAttempts <= 0) {
                errorMessage = "numPlacementAttempts must be a positive integer.";
                actionFinished(false);
                return;
            }

            // something is in our hand AND we are trying to spawn it. Quick drop the object
            if (ItemInHand != null) {
                Rigidbody rb = ItemInHand.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.None;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                GameObject topObject = GameObject.Find("Objects");
                if (topObject != null) {
                    ItemInHand.transform.parent = topObject.transform;
                } else {
                    ItemInHand.transform.parent = null;
                }

                ItemInHand.GetComponent<SimObjPhysics>().isInAgentHand = false; // agent hand flag
                DefaultAgentHand();// also default agent hand
                ItemInHand = null;
            }

            // default excludedReceptacles if null
            if (excludedReceptacles == null) {
                excludedReceptacles = new String[0];
            }

            List<SimObjType> listOfExcludedReceptacleTypes = new List<SimObjType>();

            // check if strings used for excludedReceptacles are valid object types
            foreach (string receptacleType in excludedReceptacles) {
                try {
                    SimObjType objType = (SimObjType)System.Enum.Parse(typeof(SimObjType), receptacleType);
                    listOfExcludedReceptacleTypes.Add(objType);
                } catch (Exception) {
                    errorMessage = "invalid Object Type used in excludedReceptacles array: " + receptacleType;
                    actionFinished(false);
                    return;
                }
            }
            if (!allowFloor) {
                listOfExcludedReceptacleTypes.Add(SimObjType.Floor);
            }

            if (excludedObjectIds == null) {
                excludedObjectIds = new String[0];
            }

            HashSet<SimObjPhysics> excludedSimObjects = new HashSet<SimObjPhysics>();
            foreach (String objectId in excludedObjectIds) {
                if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                    errorMessage = "Cannot find sim object with id '" + objectId + "'";
                    actionFinished(false);
                    return;
                }
                excludedSimObjects.Add(physicsSceneManager.ObjectIdToSimObjPhysics[objectId]);
            }

            bool success = physicsSceneManager.RandomSpawnRequiredSceneObjects(
                seed: randomSeed,
                spawnOnlyOutside: forceVisible,
                maxPlacementAttempts: numPlacementAttempts,
                staticPlacement: placeStationary,
                excludedSimObjects: excludedSimObjects,
                numDuplicatesOfType: numDuplicatesOfType,
                excludedReceptacleTypes: listOfExcludedReceptacleTypes
            );

            if (success && !placeStationary) {
                // Let things come to rest for 2 seconds.
                bool autoSim = Physics.autoSimulation;
                Physics.autoSimulation = false;
                for (int i = 0; i < 100; i++) {
                    Physics.Simulate(0.02f);
                }
                Physics.autoSimulation = autoSim;
            }
            physicsSceneManager.ResetObjectIdToSimObjPhysics();

            actionFinished(success);
        }

        // On demand public function for getting what sim objects are visible at that moment 
        public List<SimObjPhysics> GetAllVisibleSimObjPhysics(float maxDistance) {
            var camera = this.GetComponentInChildren<Camera>();
            return new List<SimObjPhysics>(GetAllVisibleSimObjPhysics(camera, maxDistance));
        }

        // not sure what this does, maybe delete?
        public void SetTopLevelView(bool topView = false) {
            inTopLevelView = topView;
            actionFinished(true);
        }

        public void ToggleMapView() {
            SyncTransform[] syncInChildren;

            List<StructureObject> structureObjsList = new List<StructureObject>();
            StructureObject[] structureObjs = FindObjectsOfType(typeof(StructureObject)) as StructureObject[];
            StructureObject ceiling = null;

            foreach (StructureObject structure in structureObjs) {
                switch (structure.WhatIsMyStructureObjectTag) {
                    case StructureObjectTag.Ceiling:
                        ceiling = structure;
                        goto case StructureObjectTag.LightFixture;
                    case StructureObjectTag.LightFixture:
                    case StructureObjectTag.CeilingLight:
                        structureObjsList.Add(structure);
                        break;
                }
            }

            if (inTopLevelView) {
                inTopLevelView = false;
                m_Camera.orthographic = false;
                m_Camera.transform.localPosition = lastLocalCameraPosition;
                m_Camera.transform.localRotation = lastLocalCameraRotation;

                // restore agent body culling
                m_Camera.transform.GetComponent<FirstPersonCharacterCull>().StopCullingThingsForASecond = false;
                syncInChildren = gameObject.GetComponentsInChildren<SyncTransform>();
                foreach (SyncTransform sync in syncInChildren) {
                    sync.StopSyncingForASecond = false;
                }

                foreach (StructureObject so in structureObjsList) {
                    UpdateDisplayGameObject(so.gameObject, true);
                }
            } else {
                // stop culling the agent's body so it's visible from the top?
                m_Camera.transform.GetComponent<FirstPersonCharacterCull>().StopCullingThingsForASecond = true;
                syncInChildren = gameObject.GetComponentsInChildren<SyncTransform>();
                foreach (SyncTransform sync in syncInChildren) {
                    sync.StopSyncingForASecond = true;
                }

                inTopLevelView = true;
                lastLocalCameraPosition = m_Camera.transform.localPosition;
                lastLocalCameraRotation = m_Camera.transform.localRotation;

                Bounds b;
                if (ceiling != null) {
                    // There's a ceiling component in the room!
                    // Let's use it's bounds. (Likely iTHOR.)
                    b = ceiling.GetComponent<Renderer>().bounds;
                } else {
                    // There's no component in the room!
                    // Let's use the bounds from every object. (Likely RoboTHOR.)
                    b = new Bounds();
                    b.min = agentManager.SceneBounds.min;
                    b.max = agentManager.SceneBounds.max;
                }
                float midX = (b.max.x + b.min.x) / 2.0f;
                float midZ = (b.max.z + b.min.z) / 2.0f;
                m_Camera.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                m_Camera.transform.position = new Vector3(midX, b.max.y + 5, midZ);
                m_Camera.orthographic = true;

                m_Camera.orthographicSize = Math.Max((b.max.x - b.min.x) / 2f, (b.max.z - b.min.z) / 2f);

                cameraOrthSize = m_Camera.orthographicSize;
                foreach (StructureObject so in structureObjsList) {
                    UpdateDisplayGameObject(so.gameObject, false);
                }
            }
            actionFinished(true);
        }

        /* 
        Get the 2D (x, z) convex hull of a GameObject. See the Get2DSemanticHulls
        function for more information.

        Will return null if the input game object has no mesh vertices.
        */
        protected List<List<float>> Get2DSemanticHull(GameObject go) {
            List<MIConvexHull.DefaultVertex2D> vertices = new List<MIConvexHull.DefaultVertex2D>();
            float maxY = -float.PositiveInfinity;

            foreach (MeshFilter meshFilter in go.GetComponentsInChildren<MeshFilter>()) {
                foreach (Vector3 localVertex in meshFilter.mesh.vertices) {
                    Vector3 globalVertex = meshFilter.transform.TransformPoint(localVertex);
                    vertices.Add(new MIConvexHull.DefaultVertex2D(x: globalVertex.x, y: globalVertex.z));
                    maxY = Math.Max(maxY, globalVertex.y);
                }
            }

            if (vertices.Count == 0) {
                return null;
            }

            ConvexHullCreationResult<DefaultVertex2D> miconvexHull = null;

            miconvexHull = MIConvexHull.ConvexHull.Create2D(
                data: vertices,
                tolerance: 1e-10
            );

#if UNITY_EDITOR
            DefaultVertex2D[] pointsOnHullArray = miconvexHull.Result.ToArray();
            for (int i = 0; i < pointsOnHullArray.Length; i++) {
                DefaultVertex2D p0 = pointsOnHullArray[i];
                DefaultVertex2D p1 = pointsOnHullArray[(i + 1) % pointsOnHullArray.Length];
                Debug.DrawLine(
                    start: new Vector3((float)p0.X, maxY, (float)p0.Y),
                    end: new Vector3((float)p1.X, maxY, (float)p1.Y),
                    color: Color.red,
                    duration: 100.0f
                );
            }
#endif

            List<List<float>> toReturn = new List<List<float>>();
            foreach (DefaultVertex2D v in miconvexHull.Result) {
                List<float> tuple = new List<float>();
                tuple.Add((float)v.X);
                tuple.Add((float)v.Y);
                toReturn.Add(tuple);
            }
            return toReturn;
        }

        /*
        For each objectId, create a convex hull of the object from a top-down view.
        The convex hull will be represented as a list of (x, z) world coordinates
        such that the boundary formed by these coordinates forms the convex hull
        of these points (smallest convex region enclosing the object's points).

        If the objectIds (or objectTypes) parameter is non-null, then only objects with
        those ids (or types) will be returned.
        
        ONLY ONE OF objectIds OR objectTypes IS ALLOWED TO BE NON-NULL.

        Returns a dictionary mapping object ids to a list of (x,z) coordinates corresponding
        to the convex hull of the corresponding object.
        */
        public void Get2DSemanticHulls(
            List<string> objectIds = null,
            List<string> objectTypes = null
        ) {
            if (objectIds != null && objectTypes != null) {
                throw new ArgumentException(
                    "Only one of objectIds and objectTypes can have a non-null value."
                );
            }

            HashSet<string> allowedObjectTypesSet = null;
            if (objectTypes != null) {
                allowedObjectTypesSet = new HashSet<string>(objectTypes);
            }

            // Only consider sim objects which correspond to objectIds if given.
            SimObjPhysics[] sopsFilteredByObjectIds = null;
            if (objectIds != null) {
                sopsFilteredByObjectIds = objectIds.Select(
                    key => physicsSceneManager.ObjectIdToSimObjPhysics[key]
                ).ToArray();
            } else {
                sopsFilteredByObjectIds = GameObject.FindObjectsOfType<SimObjPhysics>();
            }

            Dictionary<string, List<List<float>>> objectIdToConvexHull = new Dictionary<string, List<List<float>>>();
            foreach (SimObjPhysics sop in sopsFilteredByObjectIds) {
                // Skip objects that don't have one of the required types (if given)
                if (
                    allowedObjectTypesSet != null
                    && !allowedObjectTypesSet.Contains(sop.Type.ToString())
                ) {
                    continue;
                }

#if UNITY_EDITOR
                Debug.Log(sop.ObjectID);
#endif

                List<List<float>> hullPoints = Get2DSemanticHull(sop.gameObject);
                if (hullPoints != null) {
                    objectIdToConvexHull[sop.ObjectID] = Get2DSemanticHull(sop.gameObject);
                }
            }
            actionFinishedEmit(true, objectIdToConvexHull);
        }

        public void Get2DSemanticHull(string objectId) {
            if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                errorMessage = $"No object with ID {objectId}";
                actionFinishedEmit(false);
            } else {
                actionFinishedEmit(
                    true,
                    Get2DSemanticHull(physicsSceneManager.ObjectIdToSimObjPhysics[objectId].gameObject)
                );
            }
        }

        public void UpdateDisplayGameObject(GameObject go, bool display) {
            if (go != null) {
                foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>() as MeshRenderer[]) {
                    if (!initiallyDisabledRenderers.Contains(mr.GetInstanceID())) {
                        mr.enabled = display;
                    }
                }
            }
        }

        public void VisualizePath(ServerAction action) {
            var path = action.positions;
            if (path == null || path.Count == 0) {
                this.errorMessage = "Invalid path with 0 points.";
                actionFinished(false);
                return;
            }

            var id = action.objectId;

            getReachablePositions(1.0f, 10000, action.grid);

            Instantiate(DebugTargetPointPrefab, path[path.Count - 1], Quaternion.identity);
            new List<bool>();
            var go = Instantiate(DebugPointPrefab, path[0], Quaternion.identity);
            var textMesh = go.GetComponentInChildren<TextMesh>();
            textMesh.text = id;

            var lineRenderer = go.GetComponentInChildren<LineRenderer>();
            lineRenderer.startWidth = 0.015f;
            lineRenderer.endWidth = 0.015f;

            lineRenderer.positionCount = path.Count;
            lineRenderer.SetPositions(path.ToArray());
            actionFinished(true);
        }

        // this one is used for in-editor debug draw, currently calls to this are commented out
        private void VisualizePath(Vector3 startPosition, NavMeshPath path) {
            var pathDistance = 0.0;

            for (int i = 0; i < path.corners.Length - 1; i++) {
                Debug.DrawLine(path.corners[i], path.corners[i + 1], Color.red, 10.0f);
                Debug.Log("P i:" + i + " : " + path.corners[i] + " i+1:" + i + 1 + " : " + path.corners[i]);
                pathDistance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }

            if (pathDistance > 0.0001) {
                // Better way to draw spheres
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                go.GetComponent<Collider>().enabled = false;
                go.transform.position = startPosition;
            }
        }

        private string[] objectTypeToObjectIds(string objectTypeString) {
            List<string> objectIds = new List<string>();
            try {
                SimObjType objectType = (SimObjType)Enum.Parse(typeof(SimObjType), objectTypeString.Replace(" ", String.Empty), true);
                foreach (var s in physicsSceneManager.ObjectIdToSimObjPhysics) {
                    if (s.Value.ObjType == objectType) {
                        objectIds.Add(s.Value.objectID);
                    }
                }
            } catch (ArgumentException exception) {
                Debug.Log(exception);
            }
            return objectIds.ToArray();
        }

        public void ObjectTypeToObjectIds(string objectType) {
            try {
                var objectIds = objectTypeToObjectIds(objectType);
                actionFinished(true, objectIds.ToArray());
            } catch (ArgumentException exception) {
                errorMessage = "Invalid object type '" + objectType + "'. " + exception.Message;
                actionFinished(false);
            }
        }

        protected SimObjPhysics getInteractableSimObjectFromId(string objectId, bool forceVisible = false) {
            SimObjPhysics sop = getSimObjectFromId(objectId);
            if (sop == null) {
                errorMessage = "Object with id '" + objectId + "' is null";
                return null;
            }

            if (forceVisible || IsInteractable(sop)) {
                return sop;
            }

            return null;
        }

        protected SimObjPhysics getSimObjectFromId(string objectId) {
            if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                errorMessage = "Cannot find sim object with id '" + objectId + "'";
                return null;
            }

            SimObjPhysics sop = physicsSceneManager.ObjectIdToSimObjPhysics[objectId];

            return sop;
        }
        private SimObjPhysics getSimObjectFromTypeOrId(string objectType, string objectId) {
            if (!String.IsNullOrEmpty(objectType) && String.IsNullOrEmpty(objectId)) {
                var ids = objectTypeToObjectIds(objectType);
                if (ids.Length == 0) {
                    errorMessage = "Object type '" + objectType + "' was not found in the scene.";
                    return null;
                } else if (ids.Length > 1) {
                    errorMessage = "Multiple objects of type '" + objectType + "' were found in the scene, cannot disambiguate.";
                    return null;
                }

                objectId = ids[0];
            }

            if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                errorMessage = "Cannot find sim object with id '" + objectId + "'";
                return null;
            }

            SimObjPhysics sop = physicsSceneManager.ObjectIdToSimObjPhysics[objectId];
            if (sop == null) {
                errorMessage = "Object with id '" + objectId + "' is null";
                return null;
            }

            return sop;
        }

        private SimObjPhysics getSimObjectFromTypeOrId(ServerAction action) {
            var objectId = action.objectId;
            var objectType = action.objectType;
            return getSimObjectFromTypeOrId(objectType, objectId);
        }

        public void VisualizeGrid() {
            var reachablePositions = getReachablePositions(1.0f, 10000, true);
            actionFinished(true, reachablePositions);
        }

        public void ObjectNavExpertAction(ServerAction action) {
            SimObjPhysics sop = getSimObjectFromTypeOrId(action);
            var path = getShortestPath(sop, true);
            if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {

                int parts = (int)Math.Round(360f / rotateStepDegrees);
                if (Math.Abs((parts * 1.0f) - 360f / rotateStepDegrees) > 1e-5) {
                    errorMessage = "Invalid rotate step degrees for agent, must divide 360 without a remainder.";
                    actionFinished(false);
                    return;
                }

                int numLeft = parts / 2;
                int numRight = numLeft + (parts % 2 == 0 ? 1 : 0);
                Vector3 startPosition = this.transform.position;
                Quaternion startRotation = this.transform.rotation;
                Vector3 startCameraRot = m_Camera.transform.localEulerAngles;

                if (path.corners.Length <= 1) {
                    if (objectIsWithinViewport(sop)) {
                        actionFinished(true);
                        return;
                    }

                    int relRotate = 0;
                    int relHorizon = 0;
                    int bestNumActions = 1000000;
                    for (int i = -numLeft; i <= numRight; i++) {
                        transform.Rotate(0.0f, i * rotateStepDegrees, 0.0f);
                        for (int horizon = -1; horizon <= 2; horizon++) {
                            m_Camera.transform.localEulerAngles = new Vector3(30f * horizon, 0.0f, 0.0f);
                            if (objectIsWithinViewport(sop)) {
                                int numActions = Math.Abs(i) + Math.Abs(horizon - (int)(startCameraRot.x / 30f));
                                if (numActions < bestNumActions) {
                                    bestNumActions = numActions;
                                    relRotate = i;
                                    relHorizon = horizon - (int)(startCameraRot.x / 30f);
                                }
                            }
                        }
                        m_Camera.transform.localEulerAngles = startCameraRot;
                        transform.rotation = startRotation;
                    }

#if UNITY_EDITOR
                    Debug.Log("Expert rotate and horizon:");
                    Debug.Log(relRotate);
                    Debug.Log(relHorizon);
                    // When in the editor, rotate the agent and camera into the expert direction
                    m_Camera.transform.localEulerAngles = new Vector3(startCameraRot.x + 30f * relHorizon, 0.0f, 0.0f);
                    transform.Rotate(0.0f, relRotate * rotateStepDegrees, 0.0f);
#endif

                    if (relRotate != 0) {
                        if (relRotate < 0) {
                            actionFinished(true, "RotateLeft");
                        } else {
                            actionFinished(true, "RotateRight");
                        }
                    } else if (relHorizon != 0) {
                        if (relHorizon < 0) {
                            actionFinished(true, "LookUp");
                        } else {
                            actionFinished(true, "LookDown");
                        }
                    } else {
                        errorMessage = "Object doesn't seem visible from any rotation/horizon.";
                        actionFinished(false);
                    }
                    return;
                }

                Vector3 nextCorner = path.corners[1];

                int whichBest = 0;
                float bestDistance = 1000f;
                for (int i = -numLeft; i <= numRight; i++) {
                    transform.Rotate(0.0f, i * rotateStepDegrees, 0.0f);

                    bool couldMove = moveInDirection(this.transform.forward * gridSize);
                    if (couldMove) {
                        float newDistance = Math.Abs(nextCorner.x - transform.position.x) + Math.Abs(nextCorner.z - transform.position.z);
                        if (newDistance + 1e-6 < bestDistance) {
                            bestDistance = newDistance;
                            whichBest = i;
                        }
                    }
                    transform.position = startPosition;
                    transform.rotation = startRotation;
                }

                if (bestDistance >= 1000f) {
                    errorMessage = "Can't seem to move in any direction...";
                    actionFinished(false);
                }

#if UNITY_EDITOR
                transform.Rotate(0.0f, Math.Sign(whichBest) * rotateStepDegrees, 0.0f);
                if (whichBest == 0) {
                    moveInDirection(this.transform.forward * gridSize);
                }
                Debug.Log(whichBest);
#endif

                if (whichBest < 0) {
                    actionFinished(true, "RotateLeft");
                } else if (whichBest > 0) {
                    actionFinished(true, "RotateRight");
                } else {
                    actionFinished(true, "MoveAhead");
                }
                return;
            } else {
                errorMessage = "Path to target could not be found";
                actionFinished(false);
                return;
            }
        }

        public UnityEngine.AI.NavMeshPath getShortestPath(SimObjPhysics sop, bool useAgentTransform, ServerAction action = null) {
            var startPosition = this.transform.position;
            var startRotation = this.transform.rotation;
            if (!useAgentTransform) {
                startPosition = action.position;
                startRotation = Quaternion.Euler(action.rotation);
            }

            return GetSimObjectNavMeshTarget(sop, startPosition, startRotation, DefaultAllowedErrorInShortestPath);
        }


        private void getShortestPath(
            string objectType,
            string objectId,
            Vector3 startPosition,
            Quaternion startRotation,
            float allowedError
        ) {
            SimObjPhysics sop = getSimObjectFromTypeOrId(objectType, objectId);
            if (sop == null) {
                actionFinished(false);
                return;
            }
            var path = GetSimObjectNavMeshTarget(sop, startPosition, startRotation, allowedError);
            if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                // VisualizePath(startPosition, path);
                actionFinishedEmit(true, path);
                return;
            } else {
                Debug.Log("AI navmesh error");
                errorMessage = "Path to target could not be found";
                actionFinishedEmit(false);
                return;
            }
        }

        public void GetShortestPath(
            Vector3 position,
            Vector3 rotation,
            string objectType = null,
            string objectId = null,
            float allowedError = DefaultAllowedErrorInShortestPath
        ) {
            getShortestPath(objectType, objectId, position, Quaternion.Euler(rotation), allowedError);
        }

        public void GetShortestPath(
            Vector3 position,
            string objectType = null,
            string objectId = null,
            float allowedError = DefaultAllowedErrorInShortestPath
        ) {
            getShortestPath(objectType, objectId, position, Quaternion.Euler(Vector3.zero), allowedError);
        }

        public void GetShortestPath(
            string objectType = null,
            string objectId = null,
            float allowedError = DefaultAllowedErrorInShortestPath
        ) {
            getShortestPath(objectType, objectId, this.transform.position, this.transform.rotation, allowedError);
        }

        private bool GetPathFromReachablePositions(
            IEnumerable<Vector3> sortedPositions,
            Vector3 targetPosition,
            Transform agentTransform,
            string targetSimObjectId,
            UnityEngine.AI.NavMeshPath path) {

            Vector3 fixedPosition = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            // bool success = false;
            var PhysicsController = this;
            var targetSOP = getSimObjectFromId(targetSimObjectId);
            foreach (var pos in sortedPositions) {
                agentTransform.position = pos;
                agentTransform.LookAt(targetPosition);

                if (IsInteractable(targetSOP)) {
                    fixedPosition = pos;
                    // success = true;
                    break;
                }
            }

            var pathSuccess = UnityEngine.AI.NavMesh.CalculatePath(agentTransform.position, fixedPosition, UnityEngine.AI.NavMesh.AllAreas, path);
            return pathSuccess;
        }

        protected Collider[] overlapCollider(BoxCollider box, Vector3 newCenter, float rotateBy, int layerMask) {
            Vector3 center, halfExtents;
            Quaternion orientation;
            box.ToWorldSpaceBox(out center, out halfExtents, out orientation);
            orientation = Quaternion.Euler(0f, rotateBy, 0f) * orientation;

            return Physics.OverlapBox(newCenter, halfExtents, orientation, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected Collider[] overlapCollider(SphereCollider sphere, Vector3 newCenter, int layerMask) {
            Vector3 center;
            float radius;
            sphere.ToWorldSpaceSphere(out center, out radius);
            return Physics.OverlapSphere(newCenter, radius, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected Collider[] overlapCollider(CapsuleCollider capsule, Vector3 newCenter, float rotateBy, int layerMask) {
            Vector3 point0, point1;
            float radius;
            capsule.ToWorldSpaceCapsule(out point0, out point1, out radius);

            // Normalizing
            Vector3 oldCenter = (point0 + point1) / 2.0f;
            point0 = point0 - oldCenter;
            point1 = point1 - oldCenter;

            // Rotating and recentering
            var rotator = Quaternion.Euler(0f, rotateBy, 0f);
            point0 = rotator * point0 + newCenter;
            point1 = rotator * point1 + newCenter;

            return Physics.OverlapCapsule(point0, point1, radius, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected bool handObjectCanFitInPosition(Vector3 newAgentPosition, float rotation) {
            if (ItemInHand == null) {
                return true;
            }

            SimObjPhysics soInHand = ItemInHand.GetComponent<SimObjPhysics>();

            Vector3 handObjPosRelAgent =
                Quaternion.Euler(0, rotation - transform.eulerAngles.y, 0) *
                (transform.position - ItemInHand.transform.position);

            Vector3 newHandPosition = handObjPosRelAgent + newAgentPosition;

            int layerMask = 1 << 8;
            foreach (CapsuleCollider cc in soInHand.GetComponentsInChildren<CapsuleCollider>()) {
                foreach (Collider c in overlapCollider(cc, newHandPosition, rotation, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            foreach (BoxCollider bc in soInHand.GetComponentsInChildren<BoxCollider>()) {
                foreach (Collider c in overlapCollider(bc, newHandPosition, rotation, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            foreach (SphereCollider sc in soInHand.GetComponentsInChildren<SphereCollider>()) {
                foreach (Collider c in overlapCollider(sc, newHandPosition, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            return true;
        }

        // cast a capsule the same size as the agent
        // used to check for collisions
        public RaycastHit[] capsuleCastAllForAgent(
            CapsuleCollider capsuleCollider,
            float skinWidth,
            Vector3 startPosition,
            Vector3 dir,
            float moveMagnitude,
            int layerMask
        ) {
            // make sure to offset this by capsuleCollider.center since we shrank the capsule size
            Vector3 center = capsuleCollider.transform.position + capsuleCollider.center;
            float radius = capsuleCollider.radius + skinWidth;
            float innerHeight = capsuleCollider.height / 2.0f - radius;
            Vector3 point1 = new Vector3(startPosition.x, center.y + innerHeight, startPosition.z);
            Vector3 point2 = new Vector3(startPosition.x, center.y - innerHeight + skinWidth, startPosition.z);
            return Physics.CapsuleCastAll(
                point1: point1,
                point2: point2,
                radius: radius,
                direction: dir,
                maxDistance: moveMagnitude,
                layerMask: layerMask,
                queryTriggerInteraction: QueryTriggerInteraction.Ignore
            );
        }

        protected bool isAgentCapsuleColliding(
            HashSet<Collider> collidersToIgnore = null,
            bool includeErrorMessage = false
        ) {
            int layerMask = 1 << 8;
            foreach (Collider c in PhysicsExtensions.OverlapCapsule(GetComponent<CapsuleCollider>(), layerMask, QueryTriggerInteraction.Ignore)) {
                if ((!hasAncestor(c.transform.gameObject, gameObject)) && (
                    collidersToIgnore == null || !collidersToIgnoreDuringMovement.Contains(c))
                ) {
                    if (includeErrorMessage) {
                        SimObjPhysics sop = ancestorSimObjPhysics(c.gameObject);
                        String collidedWithName;
                        if (sop != null) {
                            collidedWithName = sop.ObjectID;
                        } else {
                            collidedWithName = c.gameObject.name;
                        }
                        errorMessage = $"Collided with: {collidedWithName}.";
                    }
#if UNITY_EDITOR
                    Debug.Log("Collided with: ");
                    Debug.Log(c);
                    Debug.Log(c.enabled);
#endif
                    return true;
                }
            }
            return false;
        }

        protected Collider[] objectsCollidingWithAgent() {
            int layerMask = 1 << 8;
            return PhysicsExtensions.OverlapCapsule(GetComponent<CapsuleCollider>(), layerMask, QueryTriggerInteraction.Ignore);
        }

        public bool getReachablePositionToObjectVisible(SimObjPhysics targetSOP, out Vector3 pos, float gridMultiplier = 1.0f, int maxStepCount = 10000) {
            CapsuleCollider cc = GetComponent<CapsuleCollider>();
            float sw = m_CharacterController.skinWidth;
            Queue<Vector3> pointsQueue = new Queue<Vector3>();
            pointsQueue.Enqueue(transform.position);
            Vector3[] directions = {
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 1.0f),
                new Vector3(-1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, -1.0f)
            };
            Quaternion originalRot = transform.rotation;

            HashSet<Vector3> goodPoints = new HashSet<Vector3>();
            HashSet<Vector3> seenPoints = new HashSet<Vector3>();
            int layerMask = 1 << 8;
            int stepsTaken = 0;
            pos = Vector3.negativeInfinity;
            while (pointsQueue.Count != 0) {
                stepsTaken += 1;
                Vector3 p = pointsQueue.Dequeue();
                if (!goodPoints.Contains(p)) {
                    goodPoints.Add(p);
                    transform.position = p;
                    var rot = transform.rotation;
                    // make sure to rotate just the Camera, not the whole agent
                    m_Camera.transform.LookAt(targetSOP.transform, transform.up);

                    bool isVisible = IsInteractable(targetSOP);

                    transform.rotation = rot;

                    if (isVisible) {
                        pos = p;
                        return true;
                    }

                    HashSet<Collider> objectsAlreadyColliding = new HashSet<Collider>(objectsCollidingWithAgent());
                    foreach (Vector3 d in directions) {
                        Vector3 newPosition = p + d * gridSize * gridMultiplier;
                        if (seenPoints.Contains(newPosition)) {
                            continue;
                        }
                        seenPoints.Add(newPosition);

                        RaycastHit[] hits = capsuleCastAllForAgent(
                            capsuleCollider: cc,
                            skinWidth: sw,
                            startPosition: p,
                            dir: d,
                            moveMagnitude: (gridSize * gridMultiplier),
                            layerMask: layerMask
                        );

                        bool shouldEnqueue = true;
                        foreach (RaycastHit hit in hits) {
                            if (hit.transform.gameObject.name != "Floor" &&
                                !ancestorHasName(hit.transform.gameObject, "FPSController") &&
                                !objectsAlreadyColliding.Contains(hit.collider)
                            ) {
                                shouldEnqueue = false;
                                break;
                            }
                        }

                        if (!shouldEnqueue) {
                            continue;
                        }

                        bool inBounds = agentManager.SceneBounds.Contains(newPosition);
                        if (errorMessage == "" && !inBounds) {
                            errorMessage = "In " +
                                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                                ", position " + newPosition.ToString() +
                                " can be reached via capsule cast but is beyond the scene bounds.";
                        }

                        shouldEnqueue = shouldEnqueue && inBounds && (
                            handObjectCanFitInPosition(newPosition, 0.0f) ||
                            handObjectCanFitInPosition(newPosition, 90.0f) ||
                            handObjectCanFitInPosition(newPosition, 180.0f) ||
                            handObjectCanFitInPosition(newPosition, 270.0f)
                        );
                        if (shouldEnqueue) {
                            pointsQueue.Enqueue(newPosition);
#if UNITY_EDITOR
                            Debug.DrawLine(p, newPosition, Color.cyan, 100000f);
#endif
                        }
                    }
                }
                if (stepsTaken > Math.Floor(maxStepCount / (gridSize * gridSize))) {
                    errorMessage = "Too many steps taken in GetReachablePositions.";
                    break;
                }
            }

            Vector3[] reachablePos = new Vector3[goodPoints.Count];
            goodPoints.CopyTo(reachablePos);
#if UNITY_EDITOR
            Debug.Log(reachablePos.Length);
#endif
            return false;
        }

        private UnityEngine.AI.NavMeshPath GetSimObjectNavMeshTarget(
            SimObjPhysics targetSOP,
            Vector3 initialPosition,
            Quaternion initialRotation,
            float allowedError,
            bool visualize = false
        ) {
            var targetTransform = targetSOP.transform;
            var targetSimObject = targetTransform.GetComponentInChildren<SimObjPhysics>();
            var PhysicsController = this;
            var agentTransform = PhysicsController.transform;

            var originalAgentPosition = agentTransform.position;
            var orignalAgentRotation = agentTransform.rotation;
            var originalCameraRotation = m_Camera.transform.rotation;

            var fixedPosition = Vector3.negativeInfinity;

            agentTransform.position = initialPosition;
            agentTransform.rotation = initialRotation;
            getReachablePositionToObjectVisible(targetSimObject, out fixedPosition);
            agentTransform.position = originalAgentPosition;
            agentTransform.rotation = orignalAgentRotation;
            m_Camera.transform.rotation = originalCameraRotation;

            var path = new UnityEngine.AI.NavMeshPath();

            var sopPos = targetSOP.transform.position;
            // var target = new Vector3(sopPos.x, initialPosition.y, sopPos.z);

            bool pathSuccess = SafelyComputeNavMeshPath(initialPosition, fixedPosition, path, allowedError);

            var pathDistance = 0.0f;
            for (int i = 0; i < path.corners.Length - 1; i++) {
#if UNITY_EDITOR
                // Debug.DrawLine(path.corners[i], path.corners[i + 1], Color.red, 10.0f);
                Debug.Log("Corner " + i + ": " + path.corners[i]);
#endif
                pathDistance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }
            return path;
        }

        protected float getFloorY(float x, float start_y, float z) {
            int layerMask = ~(LayerMask.GetMask("Agent") | LayerMask.GetMask("SimObjInvisible"));

            float y = start_y;
            RaycastHit hit;
            Ray ray = new Ray(new Vector3(x, y, z), -transform.up);
            if (!Physics.Raycast(ray, out hit, 100f, layerMask)) {
                errorMessage = "Could not find the floor";
                return float.NegativeInfinity;
            }
            return hit.point.y;
        }

        protected float getFloorY(float x, float z) {
            int layerMask = ~LayerMask.GetMask("Agent");

            Ray ray = new Ray(transform.position, -transform.up);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 10f, layerMask)) {
                errorMessage = "Could not find the floor";
                return float.NegativeInfinity;
            }
            return getFloorY(x, hit.point.y + 0.1f, z);
        }

        protected bool SafelyComputeNavMeshPath(
            Vector3 start,
            Vector3 target,
            UnityEngine.AI.NavMeshPath path,
            float allowedError
        ) {
            float floorY = Math.Min(
                getFloorY(start.x, start.y, start.z),
                getFloorY(target.x, target.y, target.z)
            );
            Vector3 startPosition = new Vector3(start.x, floorY, start.z);
            Vector3 targetPosition = new Vector3(target.x, floorY, target.z);

            this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = true;

            NavMeshHit startHit;
            bool startWasHit = UnityEngine.AI.NavMesh.SamplePosition(
                startPosition, out startHit, Math.Max(0.2f, allowedError), UnityEngine.AI.NavMesh.AllAreas
            );

            NavMeshHit targetHit;
            bool targetWasHit = UnityEngine.AI.NavMesh.SamplePosition(
                targetPosition, out targetHit, Math.Max(0.2f, allowedError), UnityEngine.AI.NavMesh.AllAreas
            );

            if (!startWasHit || !targetWasHit) {
                if (!startWasHit) {
                    errorMessage = $"No point on NavMesh near {startPosition}.";
                }
                if (!targetWasHit) {
                    errorMessage = $"No point on NavMesh near {targetPosition}.";
                }
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                return false;
            }

            float startOffset = Vector3.Distance(
                startHit.position,
                new Vector3(startPosition.x, startHit.position.y, startPosition.z)
            );
            float targetOffset = Vector3.Distance(
                targetHit.position,
                new Vector3(targetPosition.x, targetHit.position.y, targetPosition.z)
            );
            if (startOffset > allowedError && targetOffset > allowedError) {
                errorMessage = $"Closest point on NavMesh was too far from the agent: " +
                    $" (startPosition={startPosition.ToString("F3")}," +
                    $" closest navmesh position {startHit.position.ToString("F3")}) and" +
                    $" (targetPosition={targetPosition.ToString("F3")}," +
                    $" closest navmesh position {targetHit.position.ToString("F3")}).";
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                return false;
            }

#if UNITY_EDITOR
            Debug.Log($"Attempting to find path from {startHit.position} to {targetHit.position}.");
#endif
            bool pathSuccess = UnityEngine.AI.NavMesh.CalculatePath(
                startHit.position, targetHit.position, UnityEngine.AI.NavMesh.AllAreas, path
            );
            if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
#if UNITY_EDITOR
                VisualizePath(startHit.position, path);
#endif
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                return true;
            } else {
                errorMessage = $"Could not find path between {startHit.position.ToString("F3")}" +
                    $" and {targetHit.position.ToString("F3")} using the NavMesh.";
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                return false;
            }
        }
        public void GetShortestPathToPoint(
            Vector3 position, float x, float y, float z, float allowedError = DefaultAllowedErrorInShortestPath
        ) {
            var path = new UnityEngine.AI.NavMeshPath();
            if (SafelyComputeNavMeshPath(position, new Vector3(x, y, z), path, allowedError)) {
                actionFinished(true, path);
            } else {
                actionFinished(false);
            }
        }

        public void GetShortestPathToPoint(
            float x,
            float y,
            float z,
            float allowedError = DefaultAllowedErrorInShortestPath
        ) {
            var startPosition = this.transform.position;
            GetShortestPathToPoint(startPosition, x, y, z, allowedError);
        }

        public void VisualizeShortestPaths(ServerAction action) {

            SimObjPhysics sop = getSimObjectFromTypeOrId(action.objectType, action.objectId);
            if (sop == null) {
                actionFinished(false);
                return;
            }

            getReachablePositions(1.0f, 10000, action.grid, action.gridColor);

            Instantiate(DebugTargetPointPrefab, sop.transform.position, Quaternion.identity);
            var results = new List<bool>();
            for (var i = 0; i < action.positions.Count; i++) {
                var pos = action.positions[i];
                var go = Instantiate(DebugPointPrefab, pos, Quaternion.identity);
                var textMesh = go.GetComponentInChildren<TextMesh>();
                textMesh.text = i.ToString();

                var path = GetSimObjectNavMeshTarget(sop, pos, Quaternion.identity, 0.1f);

                var lineRenderer = go.GetComponentInChildren<LineRenderer>();

                if (action.pathGradient != null && action.pathGradient.colorKeys.Length > 0) {
                    lineRenderer.colorGradient = action.pathGradient;
                }
                lineRenderer.startWidth = 0.015f;
                lineRenderer.endWidth = 0.015f;

                results.Add(path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete);

                if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                    lineRenderer.positionCount = path.corners.Length;
                    lineRenderer.SetPositions(path.corners.Select(c => new Vector3(c.x, gridVisualizeY + 0.005f, c.z)).ToArray());
                }
            }
            actionFinished(true, results.ToArray());
        }

        public void CameraCrack(int randomSeed = 0) {
            GameObject canvas = Instantiate(CrackedCameraCanvas);
            CrackedCameraManager camMan = canvas.GetComponent<CrackedCameraManager>();

            camMan.SpawnCrack(randomSeed);
            actionFinished(true);
        }

        public void OnTriggerStay(Collider other) {
            if (other.CompareTag("HighFriction")) {
                inHighFrictionArea = true;
            } else {
                inHighFrictionArea = false;
            }
        }


        void Update() {
            this.updateCount++;
        }

        void FixedUpdate() {
            this.fixedUpdateCount++;
        }

        public void ResetUpdateCounters() {
            this.fixedUpdateCount = 0;
            this.updateCount = 0;
        }

        public void unrollSimulatePhysics(IEnumerator enumerator, float fixedDeltaTime) {
            this.fixedUpdateCount = ContinuousMovement.unrollSimulatePhysics(
                enumerator,
                fixedDeltaTime
            );
        }

        public void GetSceneBounds() {
            Vector3[] positions = new Vector3[2];
            positions[0] = agentManager.SceneBounds.min;
            positions[1] = agentManager.SceneBounds.max;

#if UNITY_EDITOR
            Debug.Log(positions[0]);
            Debug.Log(positions[1]);
#endif
            actionFinished(true, positions);
        }

#if UNITY_EDITOR
        void OnDrawGizmos() {
            //// check for valid spawn points in GetSpawnCoordinatesAboveObject action
            // Gizmos.color = Color.magenta;
            // if(validpointlist.Count > 0)
            // {
            //     foreach(Vector3 yes in validpointlist)
            //     {
            //         Gizmos.DrawCube(yes, new Vector3(0.01f, 0.01f, 0.01f));
            //     }
            // }

            // draw axis aligned bounds of objects after actionFinished() calls
            // if(gizmobounds != null)
            // {
            //     Gizmos.color = Color.yellow;
            //     foreach(Bounds g in gizmobounds)
            //     {
            //         Gizmos.DrawWireCube(g.center, g.size);
            //     }
            // }
        }
#endif

        public void TestActionDispatchSAAmbig2(float foo, bool def = false) {
            actionFinished(true);
        }

        public void TestActionDispatchSAAmbig2(float foo) {
            actionFinished(true);
        }

        public void TestActionDispatchSAAmbig(ServerAction action) {
            actionFinished(true);
        }

        public void TestActionDispatchSAAmbig(float foo) {
            actionFinished(true);
        }

        public void TestActionDispatchNoopServerAction(ServerAction action) {
            actionFinished(true, "serveraction");
        }

        public void TestFastEmit(string rvalue) {
            actionFinishedEmit(true, rvalue);
        }

        public void TestActionDispatchNoopAllDefault2(float param12, float param10 = 0.0f, float param11 = 1.0f) {
            actionFinished(true, "somedefault");
        }

        public void TestActionDispatchNoopAllDefault(float param10 = 0.0f, float param11 = 1.0f) {
            actionFinished(true, "alldefault");
        }

        public void TestActionDispatchNoop2(bool param3, string param4 = "foo") {
            actionFinished(true, "param3 param4/default " + param4);
        }

        public void TestActionReflectParam(string rvalue) {
            actionFinished(true, rvalue);
        }

        public void TestActionDispatchNoop(string param6, string param7) {
            actionFinished(true, "param6 param7");
        }

        public void TestActionDispatchNoop(bool param1, bool param2) {
            actionFinished(true, "param1 param2");
        }

        public void TestActionDispatchConflict(string param22) {
            actionFinished(true);
        }
        public void TestActionDispatchConflict(bool param22) {
            actionFinished(true);
        }

        public void TestActionDispatchNoop(bool param1) {
            actionFinished(true, "param1");
        }

        public void TestActionDispatchNoop() {
            actionFinished(true, "emptyargs");
        }

        public void TestActionDispatchFindAmbiguous(string typeName) {
            List<string> actions = ActionDispatcher.FindAmbiguousActions(Type.GetType(typeName));
            actionFinished(true, actions);
        }

        public void TestActionDispatchFindConflicts(string typeName) {
            Dictionary<string, List<string>> conflicts = ActionDispatcher.FindMethodVariableNameConflicts(Type.GetType(typeName));
            string[] ignore = new string[] { "GetComponent", "StopCoroutine" };
            foreach (var methodName in ignore) {
                if (conflicts.ContainsKey(methodName)) {
                    conflicts.Remove(methodName);
                }
            }
            actionFinished(true, conflicts);
        }
    }
}
