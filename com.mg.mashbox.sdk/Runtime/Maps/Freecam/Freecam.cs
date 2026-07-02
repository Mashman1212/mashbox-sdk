using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;
using MashBoxSDK.Utility;

namespace MashBoxSDK.Maps
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class Freecam : MonoBehaviour
    {
        [SerializeField] private float lookSpeed = 120f;
        [SerializeField] private float maxMoveSpeed = 10f;
        [SerializeField] private float moveForce = 100f;
        [SerializeField] private float turboMultiplier = 3f;
        [SerializeField] private Transform rotationTransform;
        [SerializeField] private Rigidbody body;
        [SerializeField] private SphereCollider sphereCollider;

        private Vector2 rotationInput;
        private Vector3 movementInput;
        private Vector3 finalMoveForce;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureComponents();
            EnsureOptionalFmodListener();
            EnsureOptionalFmodBanks();
        }

        private void OnEnable()
        {
            EnsureComponents();

            var mainCamera = Camera.main;
            if (mainCamera == null || mainCamera.transform.IsChildOf(transform))
                return;

            transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
            body.position = mainCamera.transform.position;
            body.rotation = mainCamera.transform.rotation;

            if (rotationTransform != null)
                rotationTransform.localRotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void FixedUpdate()
        {
#if !ENABLE_INPUT_SYSTEM
            return;
#else
            if (Gamepad.current == null || body == null || rotationTransform == null)
                return;

            movementInput.x = Gamepad.current.leftStick.ReadValue().x;
            movementInput.z = Gamepad.current.leftStick.ReadValue().y;
            var targetElevation = -Gamepad.current.leftShoulder.ReadValue() + Gamepad.current.rightShoulder.ReadValue();
            movementInput.y = Mathf.Lerp(movementInput.y, targetElevation, Time.fixedDeltaTime * 12f);
            rotationInput = Gamepad.current.rightStick.ReadValue();

            var moveForceDirection = Vector3.zero;
            var flattenedForward = rotationTransform.rotation * Vector3.forward;
            flattenedForward.y = 0f;
            flattenedForward.Normalize();

            moveForceDirection += flattenedForward * movementInput.z;
            moveForceDirection += (rotationTransform.rotation * Vector3.right) * movementInput.x;
            moveForceDirection += Vector3.up * (movementInput.y * 0.25f);

            var currentMaxMoveSpeed = maxMoveSpeed;
            var isTurbo = Gamepad.current.leftStickButton.isPressed;
            if (isTurbo)
                currentMaxMoveSpeed *= turboMultiplier;

            #if UNITY_6000_0_OR_NEWER
            if (body.linearVelocity.magnitude >= currentMaxMoveSpeed)
#else
            if (body.velocity.magnitude >= currentMaxMoveSpeed)
#endif
                return;

            finalMoveForce = moveForceDirection * moveForce;
            if (isTurbo)
                finalMoveForce *= turboMultiplier;

            body.AddForce(finalMoveForce, ForceMode.Acceleration);
#endif
        }

        private void Update()
        {
            if (rotationTransform == null)
                return;

            var pitchRotation = rotationInput.y * lookSpeed * Time.deltaTime;
            var yawRotation = rotationInput.x * lookSpeed * Time.deltaTime;
            rotationTransform.Rotate(-Vector3.right, pitchRotation, Space.Self);
            rotationTransform.Rotate(Vector3.up, yawRotation, Space.World);
        }

        private void OnGUI()
        {
            var scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.15f);
            var margin = 12f * scale;
            var panelWidth = 240f * scale;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.RoundToInt(24f * scale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.95f, 0.95f, 1f) }
            };

            var instructionsStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = Mathf.RoundToInt(14f * scale),
                wordWrap = true,
                normal = { textColor = new Color(1f, 1f, 1f, 0.95f) }
            };

            const string instructionsText = "Left Stick: Move\nRight Stick: Look\nLB / RB: Move Down / Up\nL3: Turbo\nRT: Fire";
            var titleHeight = titleStyle.CalcHeight(new GUIContent("Free Cam"), panelWidth - (24f * scale));
            var instructionsHeight = instructionsStyle.CalcHeight(new GUIContent(instructionsText), panelWidth - (24f * scale));
            var panelHeight = titleHeight + instructionsHeight + (30f * scale);
            var panelRect = new Rect(margin, Screen.height - panelHeight - margin, panelWidth, panelHeight);

            var previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUI.Label(new Rect(panelRect.x + 12f, panelRect.y + 8f, panelRect.width - 24f, 28f), "Free Cam", titleStyle);
            GUI.Label(
                new Rect(panelRect.x + (12f * scale), panelRect.y + titleHeight + (12f * scale), panelRect.width - (24f * scale), instructionsHeight + (4f * scale)),
                instructionsText,
                instructionsStyle);

            DrawCrosshair();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var movers = GameObject.FindGameObjectsWithTag("WorldMover");
            foreach (var mover in movers)
                Destroy(mover);
        }

        private void EnsureComponents()
        {
            if (rotationTransform == null)
            {
                rotationTransform = transform.childCount > 0
                    ? transform.GetChild(0)
                    : transform;
            }

            if (body == null)
                body = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

            body.isKinematic = false;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            #if UNITY_6000_0_OR_NEWER
            body.linearDamping = 10f;
#else
            body.drag = 10f;
#endif
            body.mass = 1f;

            if (sphereCollider == null)
                sphereCollider = GetComponent<SphereCollider>() ?? gameObject.AddComponent<SphereCollider>();

            sphereCollider.radius = 0.15f;
            sphereCollider.material = CreateCameraPhysicsMaterial();
        }

#if UNITY_6000_0_OR_NEWER
        private static PhysicsMaterial CreateCameraPhysicsMaterial()
#else
        private static PhysicMaterial CreateCameraPhysicsMaterial()
#endif
        {
#if UNITY_6000_0_OR_NEWER
            return new PhysicsMaterial("Camera_Mat")
#else
            return new PhysicMaterial("Camera_Mat")
#endif
            {
                staticFriction = 0f,
                dynamicFriction = 0f
            };
        }

        private void EnsureOptionalFmodListener()
        {
#if MGFMOD
            var targetObject = rotationTransform != null ? rotationTransform.gameObject : gameObject;
            MBFmodReflection.AddStudioListener(targetObject);
#endif
        }

        private void EnsureOptionalFmodBanks()
        {
#if MGFMOD
            if (GetComponent<MBFreecamFmodBankLoader>() == null)
                gameObject.AddComponent<MBFreecamFmodBankLoader>();
#endif
        }

        private void DrawCrosshair()
        {
            const float armLength = 6f;
            const float armThickness = 2f;
            const float gap = 2f;
            var centerX = Screen.width * 0.5f;
            var centerY = Screen.height * 0.5f;
            var previousColor = GUI.color;
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.9f);

            GUI.DrawTexture(new Rect(centerX - gap - armLength, centerY - (armThickness * 0.5f), armLength, armThickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX + gap, centerY - (armThickness * 0.5f), armLength, armThickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX - (armThickness * 0.5f), centerY - gap - armLength, armThickness, armLength), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX - (armThickness * 0.5f), centerY + gap, armThickness, armLength), Texture2D.whiteTexture);

            GUI.color = previousColor;
        }
    }
}
