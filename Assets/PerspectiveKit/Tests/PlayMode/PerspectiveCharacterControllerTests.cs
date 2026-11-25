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
    /// <summary>PlayMode coverage for the optional built-in character controller.</summary>
    public class PerspectiveCharacterControllerTests
    {
        private const float Tolerance = 0.05f;
        private const int GroundLayer = 7;
        private const float MoveDuration = 0.5f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<InputActionAsset> _assets = new List<InputActionAsset>();
        private readonly List<InputActionReference> _references = new List<InputActionReference>();
        private Gamepad _gamepad;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            _gamepad = InputSystem.AddDevice<Gamepad>();
            _mouse = InputSystem.AddDevice<Mouse>();
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
                if (asset == null) continue;
                // InputActionAsset.OnDestroy disables maps and releases their shared state.
                UnityEngine.Object.DestroyImmediate(asset);
            }
            _assets.Clear();

            if (_gamepad != null) InputSystem.RemoveDevice(_gamepad);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            _gamepad = null;
            _mouse = null;
        }

        [UnityTest]
        public IEnumerator DefaultsAreAppliedAndValidationDoesNotOverwrite()
        {
            GameObject go = Track(new GameObject("Player"));
            go.SetActive(false);
            go.AddComponent<CharacterController>();
            PerspectiveCharacterController controller = go.AddComponent<PerspectiveCharacterController>();

            Assert.AreEqual(PlayerControllerSettings.Default.walkSpeed, controller.Settings.walkSpeed);
            Assert.AreEqual(PlayerControllerSettings.Default.runSpeed, controller.Settings.runSpeed);
            Assert.AreEqual(PlayerControllerSettings.Default.gravity, controller.Settings.gravity);

            PlayerControllerSettings invalid = controller.Settings;
            invalid.walkSpeed = float.NaN;
            SetField(controller, "settings", invalid);
            LogAssert.Expect(LogType.Warning, "PerspectiveKit: Walk speed must be finite and non-negative.");
            Invoke(controller, "OnValidate");
            Assert.IsTrue(float.IsNaN(controller.Settings.walkSpeed),
                "OnValidate must not silently replace a developer value.");

            Invoke(controller, "Reset");
            Assert.AreEqual(PlayerControllerSettings.Default.walkSpeed, controller.Settings.walkSpeed);
            yield break;
        }

        [UnityTest]
        public IEnumerator WalkIsCameraRelative_AndDiagonalsAreNormalized()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity), withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return null;
            Vector3 start = controller.transform.position;
            TimeWindow straightWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, straightWindow);
            float straight = Vector3.Distance(start, controller.transform.position);
            float straightExpected = PlayerControllerSettings.Default.walkSpeed * straightWindow.Elapsed;
            Assert.Greater(straight, straightExpected * 0.85f, "Forward input must move the character.");

            controller.Respawn(Vector3.zero, Quaternion.identity);
            SendInput(new Vector2(1f, 1f));
            yield return null;
            start = controller.transform.position;
            TimeWindow diagonalWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, diagonalWindow);
            float diagonal = Vector3.Distance(start, controller.transform.position);
            float diagonalSpeed = diagonal / diagonalWindow.Elapsed;
            float straightSpeed = straight / straightWindow.Elapsed;
            Assert.Less(Mathf.Abs(diagonalSpeed - straightSpeed), 0.5f,
                "Diagonal input must be normalized to the same speed as straight input.");
        }

        [UnityTest]
        public IEnumerator WalkFollowsCameraYaw()
        {
            Quaternion yaw90 = Quaternion.Euler(0f, 90f, 0f);
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                CreateCameraTransform(new Vector3(0f, 5f, -10f), yaw90), withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return null;
            Vector3 start = controller.transform.position;
            TimeWindow window = new TimeWindow();
            yield return MeasureTime(MoveDuration, window);
            Vector3 delta = controller.transform.position - start;
            float expected = PlayerControllerSettings.Default.walkSpeed * window.Elapsed * 0.85f;
            Assert.Greater(delta.x, expected, "With a 90 degree camera yaw, forward must move along +X.");
            Assert.Less(Mathf.Abs(delta.z), expected * 0.25f);
        }

        [UnityTest]
        public IEnumerator SprintUsesRunSpeedOnlyWhileHeld()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity), withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return null;
            Vector3 start = controller.transform.position;
            TimeWindow walkWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, walkWindow);
            float walked = Vector3.Distance(start, controller.transform.position);
            float walkSpeed = walked / walkWindow.Elapsed;

            controller.Respawn(Vector3.zero, Quaternion.identity);
            SendInput(new Vector2(0f, 1f), sprint: true);
            yield return null;
            start = controller.transform.position;
            TimeWindow sprintWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, sprintWindow);
            float sprinted = Vector3.Distance(start, controller.transform.position);
            float sprintSpeed = sprinted / sprintWindow.Elapsed;
            float sprintExpected = PlayerControllerSettings.Default.runSpeed * sprintWindow.Elapsed * 0.85f;
            Assert.Greater(sprinted, sprintExpected, "Sprint must use runSpeed.");
            Assert.Greater(sprintSpeed, walkSpeed * 1.3f, "Sprint must be faster than walking.");
        }

        [UnityTest]
        public IEnumerator JumpRisesThenLandsAndGroundProbeExcludesSelf()
        {
            PlayerControllerSettings settings = FastRotationSettings();
            settings.groundMask = 1 << GroundLayer;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings,
                CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity), withGround: true);

            yield return null;
            Assert.IsTrue(controller.IsGrounded, "The ground probe must report contact while standing.");

            SendInput(Vector2.zero, jump: true);
            yield return null;
            SendInput(Vector2.zero);

            float peak = 0f;
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                peak = Mathf.Max(peak, controller.transform.position.y);
            }

            Assert.Greater(peak, 0.2f, "Jump must lift the character.");
            Assert.Less(controller.transform.position.y, 0.1f, "Character must land again.");
            Assert.IsTrue(controller.IsGrounded, "Grounded must be restored after landing.");
        }

        [UnityTest]
        public IEnumerator RisingIsNotReportedAsGrounded()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity), withGround: true);

            float waitTime = 0f;
            while (!controller.IsGrounded && waitTime < 1f)
            {
                yield return null;
                waitTime += Time.deltaTime;
            }
            Assert.IsTrue(controller.IsGrounded, "Ground contact must be established before jumping.");

            SendInput(Vector2.zero, jump: true);
            yield return null;
            SendInput(Vector2.zero);
            yield return null;

            Assert.IsFalse(controller.IsGrounded, "Ascent must not count as grounded even near the floor.");
            Assert.Greater(controller.transform.position.y, 0f);
        }

        [UnityTest]
        public IEnumerator MovementDirectionFacesTravel()
        {
            PlayerControllerSettings settings = FastRotationSettings();
            settings.rotationMode = CharacterRotationMode.MovementDirection;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings,
                CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity), withGround: true);

            SendInput(new Vector2(1f, 0f));
            for (int frame = 0; frame < 20; frame++) yield return null;
            AssertDirection(controller.transform.forward, Vector3.right);
        }

        [UnityTest]
        public IEnumerator CameraDirectionFacesCameraWhileMovingIndependently()
        {
            PlayerControllerSettings settings = FastRotationSettings();
            settings.rotationMode = CharacterRotationMode.CameraDirection;
            Quaternion yaw90 = Quaternion.Euler(0f, 90f, 0f);
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings,
                CreateCameraTransform(new Vector3(0f, 5f, -10f), yaw90), withGround: true);

            SendInput(new Vector2(0f, -1f));
            yield return null;
            Vector3 start = controller.transform.position;
            TimeWindow window = new TimeWindow();
            yield return MeasureTime(MoveDuration, window);

            AssertDirection(controller.transform.forward, Vector3.right, "Facing must follow camera yaw (+X).");
            float expected = PlayerControllerSettings.Default.walkSpeed * window.Elapsed * 0.85f;
            Assert.Less(controller.transform.position.x, start.x - expected,
                "Movement must remain input-relative (-X).");
        }

        [UnityTest]
        public IEnumerator MouseAimFacesAimPointWhileMovingForward()
        {
            PlayerControllerSettings settings = FastRotationSettings();
            settings.rotationMode = CharacterRotationMode.MouseAim;
            settings.aimSource = AimSource.PointerPosition;
            Camera camera = CreateCameraTransform(new Vector3(0f, 5f, -10f), Quaternion.identity).gameObject.AddComponent<Camera>();
            camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
            yield return null;
            Vector2 pixel = camera.WorldToScreenPoint(new Vector3(10f, 0f, 0f));

            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, camera.transform, withGround: true);
            SendPointer(pixel);
            SendInput(new Vector2(0f, 1f));
            yield return null;
            Vector3 start = controller.transform.position;
            TimeWindow window = new TimeWindow();
            yield return MeasureTime(MoveDuration, window);

            float expected = PlayerControllerSettings.Default.walkSpeed * window.Elapsed * 0.85f;
            Assert.Greater(controller.transform.position.z, start.z + expected,
                "WASD movement must stay camera-relative.");
            Assert.Greater(Vector3.Dot(controller.transform.forward, Vector3.right), 0.7f,
                "Facing must follow the aim point to the side.");
        }

        [UnityTest]
        public IEnumerator NearVerticalAndMissingCameraRemainSafe()
        {
            Camera camera = CreateCameraTransform(Vector3.zero, Quaternion.LookRotation(Vector3.up, Vector3.forward))
                .gameObject.AddComponent<Camera>();
            PerspectiveCharacterController vertical = CreateController(Vector3.zero, FastRotationSettings(),
                camera.transform, withGround: true);

            SendInput(new Vector2(0f, 1f));
            for (int frame = 0; frame < 10; frame++) yield return null;
            AssertFinite(vertical.transform.position);
            AssertFinite(vertical.transform.forward);

            PerspectiveCharacterController noCamera = CreateController(new Vector3(0f, 0f, 10f), FastRotationSettings(),
                null, withGround: true);
            SendInput(new Vector2(0f, 1f));
            for (int frame = 0; frame < 10; frame++) yield return null;
            AssertFinite(noCamera.transform.position);

            noCamera.CameraTransform = camera.transform;
            Assert.AreSame(camera.transform, noCamera.CameraTransform);
            noCamera.CameraTransform = null;
            Assert.IsNull(noCamera.CameraTransform);
        }

        [UnityTest]
        public IEnumerator HostOwnedActionsAreNeverDisabled()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                null, withGround: true, ownership: InputOwnership.Host, enableHostActions: true);
            InputAction move = GetAction(controller, "moveInput");

            Assert.IsTrue(move.enabled);
            controller.enabled = false;
            yield return null;
            Assert.IsTrue(move.enabled, "Host-owned actions must stay enabled after disable.");
            controller.enabled = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerspectiveKitOwnedActionsFollowLifecycle()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                null, withGround: true, ownership: InputOwnership.PerspectiveKit, enableHostActions: false);
            InputAction move = GetAction(controller, "moveInput");

            Assert.IsTrue(move.enabled, "Owned action must be enabled on enable.");
            controller.enabled = false;
            yield return null;
            Assert.IsFalse(move.enabled, "Owned action must be disabled on disable.");
            controller.enabled = true;
            yield return null;
            Assert.IsTrue(move.enabled, "Owned action must be re-enabled on re-enable.");
        }

        [UnityTest]
        public IEnumerator RespawnValidatesAndResetsState()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero, FastRotationSettings(),
                null, withGround: true);

            controller.Respawn(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 45f, 0f));
            AssertVec(controller.transform.position, new Vector3(1f, 0f, 2f));
            Assert.Greater(Vector3.Dot(controller.transform.forward, Quaternion.Euler(0f, 45f, 0f) * Vector3.forward), 0.99f);

            controller.enabled = false;
            controller.Respawn(new Vector3(2f, 0f, 3f), Quaternion.identity);
            Assert.IsFalse(controller.enabled, "Respawn must preserve controller enabled state.");
            AssertVec(controller.transform.position, new Vector3(2f, 0f, 3f));
            controller.enabled = true;

            Vector3 before = controller.transform.position;
            const string invalidRespawnWarning =
                "PerspectiveKit: Respawn requires finite position and a non-zero finite rotation.";
            LogAssert.Expect(LogType.Warning, invalidRespawnWarning);
            controller.Respawn(new Vector3(float.NaN, 0f, 0f), Quaternion.identity);
            LogAssert.Expect(LogType.Warning, invalidRespawnWarning);
            controller.Respawn(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f));
            LogAssert.Expect(LogType.Warning, invalidRespawnWarning);
            controller.Respawn(Vector3.zero, new Quaternion(float.PositiveInfinity, 0f, 0f, 1f));
            AssertVec(controller.transform.position, before, "Invalid respawn inputs must be rejected.");

            Assert.AreEqual(0f, ReadPrivateFloat(controller, "_verticalVelocity"));
            Assert.IsFalse(ReadPrivateBool(controller, "_jumpRequested"));
            Assert.IsFalse(controller.IsGrounded);
            yield break;
        }

        [UnityTest]
        public IEnumerator AccelerationRampsUpRatherThanInstantFullSpeed()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.acceleration = 4f;
            settings.deceleration = 4f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);

            SendInput(new Vector2(0f, 1f));
            TimeWindow firstWindow = new TimeWindow();
            yield return MeasureTime(0.2f, firstWindow);
            float firstDistance = controller.transform.position.z;
            float fullSpeedDistance = settings.walkSpeed * firstWindow.Elapsed;
            Assert.Greater(firstDistance, 0f, "The character must start moving.");
            Assert.Less(firstDistance, fullSpeedDistance * 0.6f,
                "Acceleration must prevent reaching full speed instantly.");

            float zBeforeSecondWindow = firstDistance;
            TimeWindow secondWindow = new TimeWindow();
            yield return MeasureTime(0.5f, secondWindow);
            float secondDistance = controller.transform.position.z - zBeforeSecondWindow;
            Assert.Greater(secondDistance / secondWindow.Elapsed, firstDistance / firstWindow.Elapsed * 1.2f,
                "Speed must keep increasing while acceleration is applied.");
        }

        [UnityTest]
        public IEnumerator DecelerationContinuesAfterInputReleaseThenStops()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.acceleration = 0f;
            settings.deceleration = 4f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return MeasureTime(0.3f, new TimeWindow());
            SendInput(Vector2.zero);

            float zAtRelease = controller.transform.position.z;
            TimeWindow coastWindow = new TimeWindow();
            yield return MeasureTime(0.15f, coastWindow);
            float coastDistance = controller.transform.position.z - zAtRelease;
            Assert.Greater(coastDistance, 0f, "Deceleration must let movement continue briefly.");
            Assert.Less(coastDistance, settings.walkSpeed * coastWindow.Elapsed,
                "Speed must fall while decelerating.");

            yield return MeasureTime(1f, new TimeWindow());
            float zStopped = controller.transform.position.z;
            yield return MeasureTime(0.2f, new TimeWindow());
            Assert.Less(Mathf.Abs(controller.transform.position.z - zStopped), 0.05f,
                "The character must stop once horizontal velocity reaches zero.");
        }

        [UnityTest]
        public IEnumerator RespawnClearsResidualHorizontalVelocity()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.acceleration = 0f;
            settings.deceleration = 2f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return MeasureTime(0.3f, new TimeWindow());
            SendInput(Vector2.zero);
            Vector3 respawnPosition = new Vector3(0f, 0f, 10f);
            controller.Respawn(respawnPosition, Quaternion.identity);

            yield return MeasureTime(0.3f, new TimeWindow());
            Vector3 afterRespawn = controller.transform.position;
            Assert.Less(Mathf.Abs(afterRespawn.x - respawnPosition.x), 0.05f,
                "Respawn must clear horizontal velocity so the character does not coast.");
            Assert.Less(Mathf.Abs(afterRespawn.z - respawnPosition.z), 0.05f,
                "Respawn must clear horizontal velocity so the character does not coast.");
        }

        [Test]
        public void ValidationRejectsInvalidAccelerationAndDeceleration()
        {
            PlayerControllerSettings valid = PlayerControllerSettings.Default;
            Assert.IsTrue(valid.TryValidate(out _));

            PlayerControllerSettings settings = valid;
            settings.acceleration = float.NaN;
            Assert.IsFalse(settings.TryValidate(out _), "NaN acceleration must be rejected.");
            settings = valid;
            settings.acceleration = float.PositiveInfinity;
            Assert.IsFalse(settings.TryValidate(out _), "Positive infinite acceleration must be rejected.");
            settings = valid;
            settings.acceleration = float.NegativeInfinity;
            Assert.IsFalse(settings.TryValidate(out _), "Negative infinite acceleration must be rejected.");
            settings = valid;
            settings.acceleration = -1f;
            Assert.IsFalse(settings.TryValidate(out _), "Negative acceleration must be rejected.");

            settings = valid;
            settings.deceleration = float.NaN;
            Assert.IsFalse(settings.TryValidate(out _), "NaN deceleration must be rejected.");
            settings = valid;
            settings.deceleration = float.PositiveInfinity;
            Assert.IsFalse(settings.TryValidate(out _), "Positive infinite deceleration must be rejected.");
            settings = valid;
            settings.deceleration = float.NegativeInfinity;
            Assert.IsFalse(settings.TryValidate(out _), "Negative infinite deceleration must be rejected.");
            settings = valid;
            settings.deceleration = -1f;
            Assert.IsFalse(settings.TryValidate(out _), "Negative deceleration must be rejected.");
        }

        [UnityTest]
        public IEnumerator DirectionReversalUsesDecelerationInsteadOfSnapping()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.acceleration = 8f;
            settings.deceleration = 4f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);

            SendInput(new Vector2(0f, 1f));
            yield return MeasureTime(0.5f, new TimeWindow());
            Vector3 beforeReversal = (Vector3)GetField(controller, "_horizontalVelocity");
            Assert.Greater(beforeReversal.z, 3f, "The character must be moving forward before reversing.");

            SendInput(new Vector2(0f, -1f));
            yield return null;
            Vector3 afterReversal = (Vector3)GetField(controller, "_horizontalVelocity");
            Assert.Greater(afterReversal.z, 0f, "Reversal must not snap velocity backwards in one frame.");
            Assert.Less(Vector3.Distance(beforeReversal, afterReversal), 0.5f,
                "Reversal must move velocity by the deceleration step, not the full speed change.");
        }

        [UnityTest]
        public IEnumerator CycleMovementMode_WalksThroughAllModesAndWraps()
        {
            ManagedInputAction cycle = CreateManaged("<Gamepad>/dpad/right", InputActionType.Button,
                InputOwnership.PerspectiveKit, false, "CycleMode");
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true, cycleModeInput: cycle);

            Assert.AreEqual(MovementMode.Walk, controller.MovementMode);
            yield return PressCycleMode();
            Assert.AreEqual(MovementMode.Run, controller.MovementMode);
            yield return PressCycleMode();
            Assert.AreEqual(MovementMode.Sprint, controller.MovementMode);
            yield return PressCycleMode();
            Assert.AreEqual(MovementMode.Walk, controller.MovementMode);
        }

        [UnityTest]
        public IEnumerator SprintModeUsesSprintSpeed()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = 10f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);
            controller.SetMovementMode(MovementMode.Sprint);

            SendInput(new Vector2(0f, 1f));
            TimeWindow window = new TimeWindow();
            yield return MeasureTime(MoveDuration, window);
            float expected = settings.sprintSpeed * window.Elapsed * 0.85f;
            Assert.Greater(controller.transform.position.z, expected, "Sprint mode must use sprintSpeed.");
        }

        [UnityTest]
        public IEnumerator RunAndSprintModesIgnoreSprintHold()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = 10f;
            PerspectiveCharacterController controller = CreateController(Vector3.zero, settings, null, withGround: true);

            controller.SetMovementMode(MovementMode.Run);
            SendInput(new Vector2(0f, 1f), sprint: true);
            TimeWindow runWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, runWindow);
            float runDistance = controller.transform.position.z;
            Assert.Less(runDistance, settings.runSpeed * runWindow.Elapsed * 1.15f,
                "Run mode must ignore the Sprint button.");

            controller.Respawn(Vector3.zero, Quaternion.identity);
            controller.SetMovementMode(MovementMode.Sprint);
            SendInput(new Vector2(0f, 1f), sprint: true);
            TimeWindow sprintWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, sprintWindow);
            Assert.Greater(controller.transform.position.z, settings.sprintSpeed * sprintWindow.Elapsed * 0.85f,
                "Sprint mode must use sprintSpeed even while Sprint is held.");
        }

        [UnityTest]
        public IEnumerator WalkModePreservesLegacySprintHoldToRun()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true);

            SendInput(new Vector2(0f, 1f));
            TimeWindow walkWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, walkWindow);
            float walked = controller.transform.position.z;
            Assert.Less(walked, PlayerControllerSettings.Default.runSpeed * walkWindow.Elapsed * 0.85f,
                "Walk mode without Sprint must not reach run speed.");

            controller.Respawn(Vector3.zero, Quaternion.identity);
            SendInput(new Vector2(0f, 1f), sprint: true);
            TimeWindow runWindow = new TimeWindow();
            yield return MeasureTime(MoveDuration, runWindow);
            Assert.Greater(controller.transform.position.z,
                PlayerControllerSettings.Default.runSpeed * runWindow.Elapsed * 0.85f,
                "Walk mode with Sprint held must keep legacy run speed.");
        }

        [UnityTest]
        public IEnumerator MissingCycleActionDoesNotChangeMode()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true);

            SendInput(Vector2.zero, cycleMode: true);
            yield return null;
            SendInput(Vector2.zero);
            yield return null;
            Assert.AreEqual(MovementMode.Walk, controller.MovementMode);
        }

        [UnityTest]
        public IEnumerator HostOwnedCycleActionSurvivesControllerDisable()
        {
            ManagedInputAction cycle = CreateManaged("<Gamepad>/dpad/right", InputActionType.Button,
                InputOwnership.Host, true, "CycleHost");
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true, cycleModeInput: cycle);
            InputAction cycleAction = GetAction(controller, "cycleModeInput");

            controller.enabled = false;
            yield return null;
            Assert.IsTrue(cycleAction.enabled, "Host-owned cycle action must stay enabled.");
            controller.enabled = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PerspectiveKitOwnedCycleActionFollowsLifecycle()
        {
            ManagedInputAction cycle = CreateManaged("<Gamepad>/dpad/right", InputActionType.Button,
                InputOwnership.PerspectiveKit, false, "CycleOwned");
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true, cycleModeInput: cycle);
            InputAction cycleAction = GetAction(controller, "cycleModeInput");

            Assert.IsTrue(cycleAction.enabled);
            controller.enabled = false;
            yield return null;
            Assert.IsFalse(cycleAction.enabled);
            controller.enabled = true;
            yield return null;
            Assert.IsTrue(cycleAction.enabled);
        }

        [Test]
        public void ValidationRejectsInvalidSprintSpeed()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = float.NaN;
            Assert.IsFalse(settings.TryValidate(out _));
            settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = float.PositiveInfinity;
            Assert.IsFalse(settings.TryValidate(out _));
            settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = float.NegativeInfinity;
            Assert.IsFalse(settings.TryValidate(out _));
            settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = -1f;
            Assert.IsFalse(settings.TryValidate(out _));
        }

        [Test]
        public void ValidationRejectsSprintSpeedBelowRunSpeed()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.sprintSpeed = settings.runSpeed - 1f;
            Assert.IsFalse(settings.TryValidate(out string message));
            Assert.That(message, Does.Contain("Sprint speed is lower than run speed."));
        }

        [Test]
        public void SetMovementModeRejectsUndefinedEnumValue()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true);
            controller.SetMovementMode(MovementMode.Run);
            controller.SetMovementMode((MovementMode)99);
            Assert.AreEqual(MovementMode.Run, controller.MovementMode);
        }

        [UnityTest]
        public IEnumerator RespawnPreservesSelectedMovementMode()
        {
            PerspectiveCharacterController controller = CreateController(Vector3.zero,
                PlayerControllerSettings.Default, null, withGround: true);
            controller.SetMovementMode(MovementMode.Sprint);
            controller.Respawn(new Vector3(0f, 0f, 5f), Quaternion.identity);
            yield return null;
            Assert.AreEqual(MovementMode.Sprint, controller.MovementMode);
        }

        private IEnumerator PressCycleMode()
        {
            SendInput(Vector2.zero, cycleMode: true);
            yield return null;
            SendInput(Vector2.zero);
            yield return null;
        }

        private static PlayerControllerSettings FastRotationSettings()
        {
            PlayerControllerSettings settings = PlayerControllerSettings.Default;
            settings.rotationSpeed = 3600f;
            settings.aimRotationSpeed = 3600f;
            return settings;
        }

        private PerspectiveCharacterController CreateController(
            Vector3 position,
            PlayerControllerSettings settings,
            Transform camera,
            bool withGround,
            InputOwnership ownership = InputOwnership.PerspectiveKit,
            bool enableHostActions = false,
            ManagedInputAction cycleModeInput = default)
        {
            if (withGround) CreateGround();

            GameObject go = Track(new GameObject("TestPlayer"));
            go.SetActive(false);
            go.transform.position = position;
            CharacterController characterController = go.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.4f;
            characterController.center = new Vector3(0f, 1f, 0f);

            PerspectiveCharacterController controller = go.AddComponent<PerspectiveCharacterController>();
            SetField(controller, "settings", settings);
            SetField(controller, "cameraTransform", camera);
            SetField(controller, "moveInput", CreateManaged("<Gamepad>/leftStick", InputActionType.Value,
                ownership, enableHostActions, "Move"));
            SetField(controller, "sprintInput", CreateManaged("<Gamepad>/leftStickPress", InputActionType.Button,
                ownership, enableHostActions, "Sprint"));
            SetField(controller, "jumpInput", CreateManaged("<Gamepad>/buttonSouth", InputActionType.Button,
                ownership, enableHostActions, "Jump"));
            SetField(controller, "pointerPositionInput", CreateManaged("<Mouse>/position", InputActionType.Value,
                ownership, enableHostActions, "Pointer"));
            SetField(controller, "cycleModeInput", cycleModeInput);
            go.SetActive(true);
            return controller;
        }

        private ManagedInputAction CreateManaged(
            string binding,
            InputActionType type,
            InputOwnership ownership,
            bool enableNow,
            string name)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("TestMap");
            InputAction action = map.AddAction(name, type, binding);
            InputActionReference reference = InputActionReference.Create(action);
            _assets.Add(asset);
            _references.Add(reference);
            if (enableNow) action.Enable();
            return new ManagedInputAction { action = reference, ownership = ownership };
        }

        private void CreateGround()
        {
            GameObject ground = Track(new GameObject("TestGround"));
            ground.layer = GroundLayer;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(50f, 1f, 50f);
        }

        private Transform CreateCameraTransform(Vector3 position, Quaternion rotation)
        {
            GameObject go = Track(new GameObject("TestCamera"));
            go.transform.position = position;
            go.transform.rotation = rotation;
            return go.transform;
        }

        private void SendInput(Vector2 leftStick, bool sprint = false, bool jump = false, bool cycleMode = false)
        {
            GamepadState state = new GamepadState { leftStick = leftStick };
            if (sprint) state = state.WithButton(GamepadButton.LeftStick);
            if (jump) state = state.WithButton(GamepadButton.South);
            if (cycleMode) state = state.WithButton(GamepadButton.DpadRight);
            InputSystem.QueueStateEvent(_gamepad, state);
        }

        private void SendPointer(Vector2 position)
        {
            // The action may resolve to any enabled Mouse; give them all the same pixel.
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Mouse mouse)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
                }
            }
        }

        private static InputAction GetAction(PerspectiveCharacterController controller, string fieldName)
        {
            ManagedInputAction managed = (ManagedInputAction)GetField(controller, fieldName);
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

        private static void Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing method: " + name);
            method.Invoke(target, null);
        }

        private static float ReadPrivateFloat(object target, string name)
        {
            return (float)GetField(target, name);
        }

        private static bool ReadPrivateBool(object target, string name)
        {
            return (bool)GetField(target, name);
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

        private static void AssertDirection(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.Less(Vector3.Distance(actual.normalized, expected.normalized), 0.1f,
                message ?? "Direction mismatch.");
        }

        private static void AssertVec(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.Less(Vector3.Distance(actual, expected), Tolerance, message ?? "Position mismatch.");
        }

        private static void AssertFinite(Vector3 value)
        {
            Assert.IsFalse(float.IsNaN(value.x) || float.IsInfinity(value.x));
            Assert.IsFalse(float.IsNaN(value.y) || float.IsInfinity(value.y));
            Assert.IsFalse(float.IsNaN(value.z) || float.IsInfinity(value.z));
        }
    }
}
