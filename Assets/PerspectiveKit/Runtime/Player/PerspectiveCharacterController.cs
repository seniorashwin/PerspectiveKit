using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PerspectiveKit
{
    /// <summary>
    /// Optional built-in character controller. It moves the player with a CharacterController
    /// and never reads or writes PerspectiveCamera or its input actions.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PerspectiveCharacterController : MonoBehaviour
    {
        private const float MinHorizontalSqrMagnitude = 1e-6f;
        private const float MinQuaternionSqrMagnitude = 1e-8f;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;

        [Header("Input")]
        [SerializeField] private ManagedInputAction moveInput;
        [SerializeField] private ManagedInputAction sprintInput;
        [SerializeField] private ManagedInputAction jumpInput;
        [SerializeField] private ManagedInputAction pointerPositionInput;
        [SerializeField] private ManagedInputAction cycleModeInput;

        [Header("Movement")]
        [SerializeField] private PlayerControllerSettings settings = PlayerControllerSettings.Default;

        private CharacterController _characterController;
        private Camera _aimCamera;
        private Vector3 _lastCameraForward;
        private Vector3 _lastAimDirection;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private MovementMode _movementMode;
        private bool _jumpRequested;
        private bool _isGrounded;
        private int _groundMaskBits;
        private bool _moveEnabledByUs;
        private bool _sprintEnabledByUs;
        private bool _jumpEnabledByUs;
        private bool _pointerEnabledByUs;
        private bool _cycleModeEnabledByUs;

        /// <summary>Transform used for camera-relative movement and aiming. May lack a Camera.</summary>
        public Transform CameraTransform
        {
            get => cameraTransform;
            set
            {
                cameraTransform = value;
                ResolveAimCamera();
                _lastCameraForward = Vector3.zero;
            }
        }

        /// <summary>Current settings snapshot. Values are changed only through the inspector.</summary>
        public PlayerControllerSettings Settings => settings;

        /// <summary>True while the character is standing or descending with ground contact.</summary>
        public bool IsGrounded => _isGrounded;

        /// <summary>Current latched movement mode. Walk keeps the legacy Sprint hold-to-run behaviour.</summary>
        public MovementMode MovementMode => _movementMode;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _movementMode = Enum.IsDefined(typeof(MovementMode), settings.movementMode)
                ? settings.movementMode
                : MovementMode.Walk;
            ResolveAimCamera();
            RefreshGroundMask();
        }

        private void OnEnable()
        {
            AcquireInput(moveInput, ref _moveEnabledByUs);
            AcquireInput(sprintInput, ref _sprintEnabledByUs);
            AcquireInput(jumpInput, ref _jumpEnabledByUs);
            AcquireInput(pointerPositionInput, ref _pointerEnabledByUs);
            AcquireInput(cycleModeInput, ref _cycleModeEnabledByUs);
            ResetMovementState();
        }

        private void OnDisable()
        {
            ReleaseInput(moveInput, ref _moveEnabledByUs);
            ReleaseInput(sprintInput, ref _sprintEnabledByUs);
            ReleaseInput(jumpInput, ref _jumpEnabledByUs);
            ReleaseInput(pointerPositionInput, ref _pointerEnabledByUs);
            ReleaseInput(cycleModeInput, ref _cycleModeEnabledByUs);
        }

        private void Reset()
        {
            settings = PlayerControllerSettings.Default;
        }

        private void OnValidate()
        {
            if (!settings.TryValidate(out string message))
            {
                Debug.LogWarning("PerspectiveKit: " + message, this);
            }
            RefreshGroundMask();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector2 move = ReadVector2(moveInput);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            bool sprintHeld = ReadButton(sprintInput);
            if (TryReadAction(jumpInput, out InputAction jump) && jump.WasPressedThisFrame())
            {
                _jumpRequested = true;
            }

            if (TryReadAction(cycleModeInput, out InputAction cycleMode) && cycleMode.WasPressedThisFrame())
            {
                CycleMovementMode();
            }

            GetHorizontalCameraBasis(out Vector3 cameraForward, out Vector3 cameraRight);
            Vector3 planarDirection = cameraRight * move.x + cameraForward * move.y;
            if (planarDirection.sqrMagnitude > 1f)
            {
                planarDirection.Normalize();
            }

            float speed;
            switch (_movementMode)
            {
                case MovementMode.Run:
                    speed = settings.runSpeed;
                    break;
                case MovementMode.Sprint:
                    speed = settings.sprintSpeed;
                    break;
                default:
                    // Walk keeps the legacy hold-to-run behaviour.
                    speed = sprintHeld ? settings.runSpeed : settings.walkSpeed;
                    break;
            }

            Vector3 targetVelocity = planarDirection * speed;
            // Equal or lower speed (including a reversal) uses deceleration; zero keeps instant legacy behaviour.
            float rate = targetVelocity.sqrMagnitude > _horizontalVelocity.sqrMagnitude
                ? settings.acceleration
                : settings.deceleration;
            if (rate <= 0f)
            {
                _horizontalVelocity = targetVelocity;
            }
            else
            {
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * deltaTime);
            }

            ApplyGroundAndGravity(_horizontalVelocity, deltaTime);
            UpdateFacing(planarDirection, cameraForward, deltaTime);
        }

        /// <summary>
        /// Teleports the character after validating the inputs and resets all movement, jump,
        /// grounded and aim-cache state. The original CharacterController enabled state is preserved.
        /// </summary>
        public void Respawn(Vector3 position, Quaternion rotation)
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_characterController == null ||
                !IsFinite(position) ||
                !IsFinite(rotation) ||
                QuaternionLengthSquared(rotation) < MinQuaternionSqrMagnitude)
            {
                Debug.LogWarning("PerspectiveKit: Respawn requires finite position and a non-zero finite rotation.", this);
                return;
            }

            bool wasEnabled = _characterController.enabled;
            _characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation.normalized);
            _characterController.enabled = wasEnabled;
            ResetMovementState();
        }

        private void ApplyGroundAndGravity(Vector3 planarVelocity, float deltaTime)
        {
            bool nativeGrounded = _characterController.isGrounded;
            bool probeGrounded = false;

            // Never let the probe report ground contact during ascent.
            if (_verticalVelocity <= 0f && _groundMaskBits != 0)
            {
                Vector3 localFeet = new Vector3(
                    _characterController.center.x,
                    _characterController.center.y - _characterController.height * 0.5f,
                    _characterController.center.z);
                Vector3 feet = transform.TransformPoint(localFeet);
                Vector3 probeCentre = feet + Vector3.up * settings.groundProbeOffset;
                probeGrounded = Physics.CheckSphere(
                    probeCentre,
                    settings.groundProbeRadius,
                    _groundMaskBits,
                    QueryTriggerInteraction.Ignore);
            }

            bool contact = nativeGrounded || probeGrounded;
            bool jumpNow = _jumpRequested;
            _jumpRequested = false;

            if (contact && jumpNow && _verticalVelocity <= 0f)
            {
                _verticalVelocity = Mathf.Sqrt(2f * settings.gravity * settings.jumpHeight);
            }
            else if (contact && _verticalVelocity <= 0f)
            {
                _verticalVelocity = -settings.stickToGroundForce;
            }
            else
            {
                _verticalVelocity -= settings.gravity * deltaTime;
            }

            _isGrounded = contact && _verticalVelocity <= 0f;
            Vector3 displacement = (planarVelocity + Vector3.up * _verticalVelocity) * deltaTime;
            _characterController.Move(displacement);
        }

        private void GetHorizontalCameraBasis(out Vector3 forward, out Vector3 right)
        {
            forward = Vector3.zero;
            if (cameraTransform != null)
            {
                forward = cameraTransform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < MinHorizontalSqrMagnitude)
                {
                    forward = _lastCameraForward;
                }
                if (forward.sqrMagnitude >= MinHorizontalSqrMagnitude)
                {
                    _lastCameraForward = forward.normalized;
                }
            }

            if (forward.sqrMagnitude < MinHorizontalSqrMagnitude)
            {
                forward = Vector3.forward;
                _lastCameraForward = forward;
            }

            forward.Normalize();
            right = new Vector3(forward.z, 0f, -forward.x);
        }

        private void UpdateFacing(Vector3 moveDirection, Vector3 cameraForward, float deltaTime)
        {
            Vector3 facing;
            float rotationSpeed;

            switch (settings.rotationMode)
            {
                case CharacterRotationMode.MovementDirection:
                    facing = moveDirection.sqrMagnitude >= MinHorizontalSqrMagnitude
                        ? moveDirection.normalized
                        : transform.forward;
                    rotationSpeed = settings.rotationSpeed;
                    break;

                case CharacterRotationMode.CameraDirection:
                    facing = cameraForward;
                    rotationSpeed = settings.rotationSpeed;
                    break;

                case CharacterRotationMode.MouseAim:
                    AimSource source = settings.aimSource;
                    Vector2 pointer = Vector2.zero;
                    if (source == AimSource.PointerPosition)
                    {
                        InputAction action = pointerPositionInput.action == null ? null : pointerPositionInput.action.action;
                        if (action == null)
                        {
                            source = AimSource.ScreenCentre;
                        }
                        else
                        {
                            pointer = action.ReadValue<Vector2>();
                        }
                    }

                    bool fresh = MouseAimSolver.TryGetAimDirection(
                        _aimCamera,
                        transform.position,
                        pointer,
                        source,
                        settings.aimPlaneHeight,
                        settings.maxAimDistance,
                        _lastAimDirection,
                        transform.forward,
                        out Vector3 aimDirection);
                    if (fresh)
                    {
                        _lastAimDirection = aimDirection;
                    }
                    facing = aimDirection;
                    rotationSpeed = settings.aimRotationSpeed;
                    break;

                default:
                    return;
            }

            if (rotationSpeed <= 0f || facing.sqrMagnitude < MinHorizontalSqrMagnitude)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * deltaTime);
        }

        /// <summary>
        /// Sets the latched movement mode. Undefined enum values are ignored.
        /// Respawn preserves the selected mode.
        /// </summary>
        public void SetMovementMode(MovementMode mode)
        {
            if (!Enum.IsDefined(typeof(MovementMode), mode))
            {
                return;
            }
            _movementMode = mode;
        }

        private void CycleMovementMode()
        {
            _movementMode = _movementMode == MovementMode.Walk
                ? MovementMode.Run
                : _movementMode == MovementMode.Run ? MovementMode.Sprint : MovementMode.Walk;
        }

        private void ResetMovementState()
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _jumpRequested = false;
            _isGrounded = false;
            _lastAimDirection = Vector3.zero;
            _lastCameraForward = Vector3.zero;
        }

        private void ResolveAimCamera()
        {
            _aimCamera = cameraTransform == null ? null : cameraTransform.GetComponent<Camera>();
        }

        private void RefreshGroundMask()
        {
            if (gameObject != null)
            {
                _groundMaskBits = settings.groundMask.value & ~(1 << gameObject.layer);
            }
        }

        private static void AcquireInput(ManagedInputAction managed, ref bool enabledByUs)
        {
            if (managed.ownership != InputOwnership.PerspectiveKit || managed.action == null)
            {
                return;
            }

            InputAction action = managed.action.action;
            if (action != null && !action.enabled)
            {
                action.Enable();
                enabledByUs = true;
            }
        }

        private static void ReleaseInput(ManagedInputAction managed, ref bool enabledByUs)
        {
            if (enabledByUs && managed.action != null)
            {
                managed.action.action.Disable();
            }
            enabledByUs = false;
        }

        private static bool TryReadAction(ManagedInputAction managed, out InputAction action)
        {
            action = managed.action == null ? null : managed.action.action;
            return action != null;
        }

        private static Vector2 ReadVector2(ManagedInputAction managed)
        {
            return TryReadAction(managed, out InputAction action) ? action.ReadValue<Vector2>() : Vector2.zero;
        }

        private static bool ReadButton(ManagedInputAction managed)
        {
            return TryReadAction(managed, out InputAction action) && action.IsPressed();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                   !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        private static float QuaternionLengthSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        }
    }
}
