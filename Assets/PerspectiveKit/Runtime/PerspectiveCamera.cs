using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PerspectiveKit
{
    /// <summary>
    /// Drives a camera between first-person and third-person perspectives without touching
    /// character movement or animation. The character only supplies a target Transform; this
    /// component reads it and writes its own transform.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PerspectiveCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform cameraTarget;
        [SerializeField] private Perspective initialPerspective = Perspective.ThirdPerson;
        [Min(0f), SerializeField] private float transitionDuration = 0.25f;

        [Header("Input")]
        [SerializeField] private ManagedInputAction lookInput;
        [SerializeField] private ManagedInputAction togglePerspectiveInput;

        [Header("First person")]
        [SerializeField] private FirstPersonSettings firstPerson = new FirstPersonSettings
        {
            offset = Vector3.zero,
            mouseSensitivity = new Vector2(0.1f, 0.1f),
            stickSensitivity = new Vector2(120f, 120f),
            pitchLimits = new Vector2(-85f, 85f),
            fieldOfView = 75f
        };

        [Header("Third person")]
        [SerializeField] private ThirdPersonSettings thirdPerson = new ThirdPersonSettings
        {
            mouseSensitivity = new Vector2(0.1f, 0.1f),
            stickSensitivity = new Vector2(120f, 120f),
            pitchLimits = new Vector2(-60f, 80f),
            fieldOfView = 65f,
            minimumDistance = 0.5f,
            distance = 4f,
            height = 1.5f,
            shoulderOffset = 0.4f,
            collision = new CameraCollisionSettings
            {
                radius = 0.2f,
                padding = 0.05f,
                collisionMask = ~0,
                recoverySpeed = 8f
            }
        };

        private Camera _camera;
        private Perspective _perspective;
        private float _blend;
        private float _yaw;
        private float _pitch;
        private float _smoothedDistance;
        private bool _distanceInitialized;
        private bool _rotationInitialized;
        private bool _warnedNoTarget;
        private bool _warnedInvalidTarget;
        private bool _perspectiveExplicitlySet;
        private bool _lookEnabledByUs;
        private bool _toggleEnabledByUs;

        /// <summary>Raised once whenever <see cref="SetPerspective"/> changes the requested perspective.</summary>
        public event Action<Perspective> PerspectiveChanged;

        /// <summary>The Transform the camera follows. Usually a child of the character at head height.</summary>
        public Transform CameraTarget
        {
            get => cameraTarget;
            set
            {
                cameraTarget = value;
                _warnedNoTarget = false;
                _warnedInvalidTarget = false;
                _rotationInitialized = false;
                _distanceInitialized = false;
            }
        }

        /// <summary>The requested perspective. During a transition this is the destination, not the current pose.</summary>
        public Perspective Perspective => _perspective;

        /// <summary>0 is fully first-person, 1 is fully third-person. Intermediate values indicate a transition.</summary>
        public float Blend => _blend;

        /// <summary>True while the blend has not yet reached the requested perspective.</summary>
        public bool IsTransitioning => !Mathf.Approximately(_blend, TargetBlend);

        private float TargetBlend => _perspective == Perspective.FirstPerson ? 0f : 1f;

        private float EasedBlend => Mathf.SmoothStep(0f, 1f, _blend);

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            // A host script's Awake may run before ours and set a perspective already.
            if (!_perspectiveExplicitlySet)
            {
                _perspective = initialPerspective;
                _blend = TargetBlend;
            }
        }

        private void OnEnable()
        {
            AcquireInput(lookInput, ref _lookEnabledByUs);
            AcquireInput(togglePerspectiveInput, ref _toggleEnabledByUs);
        }

        private void OnDisable()
        {
            ReleaseInput(lookInput, ref _lookEnabledByUs);
            ReleaseInput(togglePerspectiveInput, ref _toggleEnabledByUs);
        }

        private void Update()
        {
            if (TryReadAction(togglePerspectiveInput, out InputAction toggle) && toggle.WasPressedThisFrame())
            {
                TogglePerspective();
            }

            if (!TryReadAction(lookInput, out InputAction look))
            {
                return;
            }

            Vector2 delta = look.ReadValue<Vector2>();
            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector2 mouseSensitivity = Vector2.Lerp(firstPerson.mouseSensitivity, thirdPerson.mouseSensitivity, EasedBlend);
            Vector2 stickSensitivity = Vector2.Lerp(firstPerson.stickSensitivity, thirdPerson.stickSensitivity, EasedBlend);
            InputControl control = look.activeControl;
            bool isMouse = control != null && control.device is Mouse;
            Vector2 sensitivity = isMouse ? mouseSensitivity : stickSensitivity;
            float scale = isMouse ? 1f : Time.unscaledDeltaTime;

            _yaw += delta.x * sensitivity.x * scale;
            _pitch -= delta.y * sensitivity.y * scale;
        }

        private void LateUpdate()
        {
            if (cameraTarget == null)
            {
                if (!_warnedNoTarget)
                {
                    Debug.LogWarning("PerspectiveKit: CameraTarget is not assigned.", this);
                    _warnedNoTarget = true;
                }
                return;
            }

            if (cameraTarget == transform || cameraTarget.IsChildOf(transform))
            {
                if (!_warnedInvalidTarget)
                {
                    Debug.LogError("PerspectiveKit: CameraTarget must not be the camera or a child of it.", this);
                    _warnedInvalidTarget = true;
                }
                return;
            }

            if (!_rotationInitialized)
            {
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _rotationInitialized = true;
            }

            _blend = transitionDuration <= 0f
                ? TargetBlend
                : Mathf.MoveTowards(_blend, TargetBlend, Time.unscaledDeltaTime / transitionDuration);

            float t = EasedBlend;
            Vector2 pitchLimits = Vector2.Lerp(firstPerson.pitchLimits, thirdPerson.pitchLimits, t);
            _pitch = Mathf.Clamp(_pitch, pitchLimits.x, pitchLimits.y);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 firstPersonPosition = cameraTarget.TransformPoint(firstPerson.offset);
            float boomDistance = Mathf.Max(thirdPerson.minimumDistance, thirdPerson.distance);
            Vector3 thirdPersonPosition = cameraTarget.position +
                rotation * new Vector3(thirdPerson.shoulderOffset, thirdPerson.height, -boomDistance);

            Vector3 desiredPosition = Vector3.Lerp(firstPersonPosition, thirdPersonPosition, t);
            Vector3 offset = desiredPosition - cameraTarget.position;
            float desiredDistance = offset.magnitude;
            Vector3 direction = desiredDistance > 0.0001f ? offset / desiredDistance : Vector3.zero;

            CameraCollisionResult collisionResult = CameraCollision.Solve(
                cameraTarget.position, direction, desiredDistance, thirdPerson.collision);
            float safeDistance = collisionResult.safeDistance;

            if (!_distanceInitialized)
            {
                // Requires a valid target and a completed collision query; never a zero default.
                _smoothedDistance = safeDistance;
                _distanceInitialized = true;
            }

            if (collisionResult.blocked)
            {
                // Pull in immediately. Safety is not eased.
                _smoothedDistance = safeDistance;
            }
            else
            {
                _smoothedDistance = Mathf.Min(
                    Mathf.MoveTowards(
                        _smoothedDistance,
                        safeDistance,
                        thirdPerson.collision.recoverySpeed * Time.unscaledDeltaTime),
                    safeDistance);
            }

            float finalDistance = Mathf.Min(_smoothedDistance, desiredDistance, safeDistance);
            Vector3 finalPosition = cameraTarget.position + direction * finalDistance;

            // Safety net for cast misses: hit.distance already includes the sphere radius and
            // the solver keeps positive padding, so a clear hit must not trigger this check.
            if (CameraCollision.IsBlockedAt(finalPosition, thirdPerson.collision.radius,
                    thirdPerson.collision.collisionMask))
            {
                finalDistance = 0f;
                _smoothedDistance = 0f;
                finalPosition = cameraTarget.position;
            }

            transform.SetPositionAndRotation(finalPosition, rotation);
            _camera.fieldOfView = Mathf.Lerp(firstPerson.fieldOfView, thirdPerson.fieldOfView, t);
        }

        /// <summary>Requests a perspective. Calling this with the current perspective does nothing.</summary>
        public void SetPerspective(Perspective value)
        {
            if (_perspective == value)
            {
                return;
            }

            _perspective = value;
            _perspectiveExplicitlySet = true;
            PerspectiveChanged?.Invoke(value);
        }

        /// <summary>Switches between first-person and third-person.</summary>
        public void TogglePerspective()
        {
            SetPerspective(_perspective == Perspective.FirstPerson ? Perspective.ThirdPerson : Perspective.FirstPerson);
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
    }
}
