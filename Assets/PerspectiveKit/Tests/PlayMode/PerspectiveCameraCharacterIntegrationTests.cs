using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace PerspectiveKit.Tests
{
    /// <summary>Integration coverage for PerspectiveCamera and the optional built-in controller.</summary>
    public class PerspectiveCameraCharacterIntegrationTests
    {
        private const int ObstacleLayer = 8;
        private const float MoveDuration = 0.5f;
        private const float Tolerance = 0.05f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<InputActionAsset> _assets = new List<InputActionAsset>();
        private readonly List<InputActionReference> _references = new List<InputActionReference>();
        private Gamepad _gamepad;

        [SetUp]
        public void SetUp()
        {
            _gamepad = InputSystem.AddDevice<Gamepad>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();

            foreach (InputActionReference reference in _references)
            {
                if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
            }
            _references.Clear();

            foreach (InputActionAsset asset in _assets)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
            _assets.Clear();

            if (_gamepad != null) InputSystem.RemoveDevice(_gamepad);
            _gamepad = null;
        }

        [UnityTest]
        public IEnumerator ExternalTransformTargetFollowsCameraWithoutController()
        {
            GameObject targetObject = Track(new GameObject("PlainTarget"));
            targetObject.transform.position = Vector3.zero;
            Assert.IsNull(targetObject.GetComponent<PerspectiveCharacterController>(),
                "This fixture must use a plain Transform, not the built-in controller.");

            PerspectiveCamera camera = CreateCamera(targetObject.transform, CameraSettings(0), Perspective.ThirdPerson);
            Vector3 expectedOffset = new Vector3(0f, 0f, -4f);
            yield return null;
            AssertVec(camera.transform.position, targetObject.transform.position + expectedOffset);

            TimeWindow window = new TimeWindow();
            while (window.Elapsed < MoveDuration)
            {
                yield return null;
                targetObject.transform.position += Vector3.right * 2f * Time.deltaTime;
                window.Elapsed += Time.deltaTime;
            }
            yield return null;

            AssertVec(camera.transform.position, targetObject.transform.position + expectedOffset,
                Tolerance, "Camera must follow a plain externally moved Transform.");
        }

        [UnityTest]
        public IEnumerator CameraTracksMovingPlayer_AndToggleWhileMoving_WithSeparateOwnedActions()
        {
            CreateGround();
            Transform cameraTransform = CreateCameraTransform();
            ThirdPersonSettings settings = CameraSettings(0);
            PerspectiveCamera camera = CreateCamera(cameraTransform, settings, Perspective.ThirdPerson);
            PerspectiveCharacterController player = CreatePlayer(Vector3.zero, cameraTransform);
            Transform head = CreateHead(player.transform);
            camera.CameraTarget = head;
            player.CameraTransform = cameraTransform;
            yield return null;

            InputAction move = GetAction(player, "moveInput");
            InputAction toggle = GetAction(camera, "togglePerspectiveInput");
            Assert.IsTrue(move.enabled && toggle.enabled,
                "Separately owned actions for both components must be enabled together.");

            SendGamepad(new Vector2(0f, 1f));
            TimeWindow firstWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, firstWindow);
            float expected = PlayerControllerSettings.Default.walkSpeed * firstWindow.Elapsed * 0.8f;
            Assert.Greater(player.transform.position.z, expected, "Player must move forward.");
            Vector3 tppOffset = new Vector3(settings.shoulderOffset, settings.height, -settings.distance);
            AssertVec(camera.transform.position, head.position + tppOffset, 0.15f,
                "Camera must track the moving player while preserving the TPP offset.");

            SendGamepad(new Vector2(0f, 1f), north: true);
            yield return null;
            SendGamepad(new Vector2(0f, 1f));
            yield return null;

            Assert.AreEqual(Perspective.FirstPerson, camera.Perspective, "Toggle must switch to FPP while moving.");
            AssertVec(camera.transform.position, head.position, 0.15f,
                "FPP camera must sit at the target with zero offset.");
            Assert.IsTrue(move.enabled && toggle.enabled, "Ownership must not interfere after switching.");

            float zBeforeSwitchMovement = player.transform.position.z;
            TimeWindow secondWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, secondWindow);
            float expectedAfter = PlayerControllerSettings.Default.walkSpeed * secondWindow.Elapsed * 0.8f;
            Assert.Greater(player.transform.position.z, zBeforeSwitchMovement + expectedAfter,
                "Movement must continue after switching perspective.");

            player.enabled = false;
            yield return null;
            Assert.IsTrue(toggle.enabled,
                "Disabling the controller must not disable the camera's independently owned action.");
        }

        [UnityTest]
        public IEnumerator CameraCollisionFollowsMovingPlayer()
        {
            CreateGround();
            GameObject wall = Track(new GameObject("Obstacle"));
            wall.layer = ObstacleLayer;
            wall.transform.position = new Vector3(0f, 0f, -2f);
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(10f, 4f, 0.2f);
            Physics.SyncTransforms();

            ThirdPersonSettings cameraSettings = CameraSettings(1 << ObstacleLayer);
            Transform cameraTransform = CreateCameraTransform();
            PerspectiveCamera camera = CreateCamera(cameraTransform, cameraSettings, Perspective.ThirdPerson);
            PerspectiveCharacterController player = CreatePlayer(new Vector3(0f, 0f, 2f), cameraTransform);
            Transform head = CreateHead(player.transform);
            head.localPosition = Vector3.zero;
            camera.CameraTarget = head;
            yield return null;

            float wallFront = wall.transform.position.z + wallCollider.size.z * 0.5f;
            float startZ = player.transform.position.z;
            SendGamepad(new Vector2(0f, -1f));
            TimeWindow window = new TimeWindow();
            while (window.Elapsed < MoveDuration)
            {
                yield return null;
                window.Elapsed += Time.deltaTime;

                Vector3 cameraPosition = camera.transform.position;
                Assert.IsFalse(
                    Physics.CheckSphere(cameraPosition, cameraSettings.collision.radius,
                        cameraSettings.collision.collisionMask, QueryTriggerInteraction.Ignore),
                    "Camera sphere must not overlap the obstacle while following the player.");
                Assert.LessOrEqual(Vector3.Distance(cameraPosition, head.position),
                    cameraSettings.distance + 0.01f, "Boom must never exceed its configured distance.");
                Assert.Less(Vector3.Distance(cameraPosition, head.position),
                    cameraSettings.distance - 0.1f, "Obstacle must shorten the boom.");
                Assert.Greater(cameraPosition.z, wallFront, "Camera must stay in front of the wall.");
            }

            float expected = PlayerControllerSettings.Default.walkSpeed * window.Elapsed * 0.8f;
            Assert.Less(player.transform.position.z, startZ - expected,
                "Player must move toward the obstacle during the test.");
        }

        private static ThirdPersonSettings CameraSettings(int collisionMask)
        {
            return new ThirdPersonSettings
            {
                mouseSensitivity = new Vector2(0.1f, 0.1f),
                stickSensitivity = new Vector2(120f, 120f),
                pitchLimits = new Vector2(-60f, 80f),
                fieldOfView = 65f,
                minimumDistance = 0.5f,
                distance = 4f,
                height = 0f,
                shoulderOffset = 0f,
                collision = new CameraCollisionSettings
                {
                    radius = 0.2f,
                    padding = 0.05f,
                    collisionMask = collisionMask,
                    recoverySpeed = 8f
                }
            };
        }

        private PerspectiveCamera CreateCamera(
            Transform target,
            ThirdPersonSettings thirdPerson,
            Perspective initial)
        {
            GameObject go = Track(new GameObject("IntegrationCamera"));
            go.SetActive(false);
            go.AddComponent<Camera>();
            PerspectiveCamera camera = go.AddComponent<PerspectiveCamera>();
            SetField(camera, "initialPerspective", initial);
            SetField(camera, "transitionDuration", 0f);
            SetField(camera, "thirdPerson", thirdPerson);
            SetField(camera, "togglePerspectiveInput", CreateManaged(
                "<Gamepad>/buttonNorth", InputActionType.Button, InputOwnership.PerspectiveKit, false, "CameraToggle"));
            camera.CameraTarget = target;
            go.SetActive(true);
            return camera;
        }

        private PerspectiveCharacterController CreatePlayer(Vector3 position, Transform cameraTransform)
        {
            GameObject go = Track(new GameObject("IntegrationPlayer"));
            go.SetActive(false);
            go.transform.position = position;
            CharacterController characterController = go.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.4f;
            characterController.center = new Vector3(0f, 1f, 0f);

            PerspectiveCharacterController player = go.AddComponent<PerspectiveCharacterController>();
            SetField(player, "settings", PlayerControllerSettings.Default);
            SetField(player, "cameraTransform", cameraTransform);
            SetField(player, "moveInput", CreateManaged(
                "<Gamepad>/leftStick", InputActionType.Value, InputOwnership.PerspectiveKit, false, "PlayerMove"));
            SetField(player, "sprintInput", CreateManaged(
                "<Gamepad>/leftStickPress", InputActionType.Button, InputOwnership.PerspectiveKit, false, "PlayerSprint"));
            SetField(player, "jumpInput", CreateManaged(
                "<Gamepad>/buttonSouth", InputActionType.Button, InputOwnership.PerspectiveKit, false, "PlayerJump"));
            SetField(player, "pointerPositionInput", CreateManaged(
                "<Mouse>/position", InputActionType.Value, InputOwnership.PerspectiveKit, false, "PlayerPointer"));
            go.SetActive(true);
            return player;
        }

        private Transform CreateHead(Transform parent)
        {
            GameObject go = Track(new GameObject("IntegrationTarget"));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            return go.transform;
        }

        private Transform CreateCameraTransform()
        {
            GameObject go = Track(new GameObject("CameraRig"));
            go.transform.rotation = Quaternion.identity;
            return go.transform;
        }

        private void CreateGround()
        {
            GameObject ground = Track(new GameObject("IntegrationGround"));
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(50f, 1f, 50f);
        }

        private ManagedInputAction CreateManaged(
            string binding,
            InputActionType type,
            InputOwnership ownership,
            bool enableNow,
            string name)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("IntegrationMap");
            InputAction action = map.AddAction(name, type, binding);
            InputActionReference reference = InputActionReference.Create(action);
            _assets.Add(asset);
            _references.Add(reference);
            if (enableNow) action.Enable();
            return new ManagedInputAction { action = reference, ownership = ownership };
        }

        private void SendGamepad(Vector2 leftStick, bool north = false)
        {
            GamepadState state = new GamepadState { leftStick = leftStick };
            if (north) state = state.WithButton(GamepadButton.North);
            InputSystem.QueueStateEvent(_gamepad, state);
        }

        private static InputAction GetAction(object component, string fieldName)
        {
            ManagedInputAction managed = (ManagedInputAction)GetField(component, fieldName);
            return managed.action == null ? null : managed.action.action;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing field: " + name);
            field.SetValue(target, value);
        }

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing field: " + name);
            return field.GetValue(target);
        }

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }

        private sealed class TimeWindow
        {
            public float Elapsed;
        }

        private static IEnumerator MeasureTime(float duration, TimeWindow window)
        {
            window.Elapsed = 0f;
            while (window.Elapsed < duration)
            {
                yield return null;
                window.Elapsed += Time.deltaTime;
            }
        }

        private static void AssertVec(Vector3 actual, Vector3 expected, float tolerance = Tolerance,
            string message = null)
        {
            Assert.Less(Vector3.Distance(actual, expected), tolerance, message ?? "Position mismatch.");
        }
    }
}
