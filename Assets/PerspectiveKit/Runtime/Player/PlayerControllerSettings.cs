using System;
using System.Collections.Generic;
using UnityEngine;

namespace PerspectiveKit
{
    /// <summary>How the built-in character chooses the direction it faces.</summary>
    public enum CharacterRotationMode
    {
        /// <summary>Face the direction of movement.</summary>
        MovementDirection,

        /// <summary>Face the camera's horizontal forward direction.</summary>
        CameraDirection,

        /// <summary>Face a world-space aim point derived from a camera ray.</summary>
        MouseAim
    }

    /// <summary>Which screen position is used to build the mouse-aim ray.</summary>
    public enum AimSource
    {
        /// <summary>Use the centre of the camera viewport as a crosshair.</summary>
        ScreenCentre,

        /// <summary>Use the absolute pointer position reported by the Input System.</summary>
        PointerPosition
    }

    /// <summary>Latched ground movement mode.</summary>
    public enum MovementMode
    {
        /// <summary>Legacy behaviour: walkSpeed, or runSpeed while Sprint is held.</summary>
        Walk,

        /// <summary>Latched runSpeed; the Sprint button is ignored.</summary>
        Run,

        /// <summary>Latched sprintSpeed; the Sprint button is ignored.</summary>
        Sprint
    }

    /// <summary>Movement, ground and aiming settings for <c>PerspectiveCharacterController</c>.</summary>
    [Serializable]
    public struct PlayerControllerSettings
    {
        [Tooltip("How the character chooses the direction it faces. Independent of camera perspective.")]
        public CharacterRotationMode rotationMode;

        [Tooltip("Ray source used when rotation mode is MouseAim.")]
        public AimSource aimSource;

        [Tooltip("Ground movement speed while not sprinting.")]
        [Min(0f)] public float walkSpeed;

        [Tooltip("Ground movement speed while sprint is held.")]
        [Min(0f)] public float runSpeed;

        /// <summary>
        /// Latched movement mode. Walk keeps legacy behaviour: Sprint held gives runSpeed.
        /// Run and Sprint are latched speeds and ignore the Sprint button.
        /// </summary>
        [Tooltip("Latched movement mode. Walk: Sprint held gives run speed. Run/Sprint: latched speed, Sprint button ignored.")]
        public MovementMode movementMode;

        /// <summary>Speed used in Sprint mode; must be finite, non-negative and at least runSpeed.</summary>
        [Tooltip("Speed used in Sprint mode. Defaults to runSpeed so existing behaviour is unchanged.")]
        [Min(0f)] public float sprintSpeed;

        [Tooltip("Ground acceleration in units per second squared. Zero means instant acceleration.")]
        [Min(0f)] public float acceleration;

        [Tooltip("Ground deceleration in units per second squared. Zero means instant deceleration, including reversals at the same speed.")]
        [Min(0f)] public float deceleration;

        [Tooltip("Downward acceleration in units per second squared.")]
        [Min(0.01f)] public float gravity;

        [Tooltip("Maximum jump height in units.")]
        [Min(0f)] public float jumpHeight;

        [Tooltip("Small downward velocity applied while grounded to stay attached to slopes and steps.")]
        [Min(0f)] public float stickToGroundForce;

        [Tooltip("Degrees per second used to turn toward movement or camera direction. Zero disables automatic rotation.")]
        [Min(0f)] public float rotationSpeed;

        [Tooltip("Degrees per second used to turn toward the aim direction. Zero disables automatic rotation.")]
        [Min(0f)] public float aimRotationSpeed;

        [Tooltip("Radius of the optional ground-probe sphere. Zero disables the probe.")]
        [Min(0f)] public float groundProbeRadius;

        [Tooltip("Height of the probe centre above the CharacterController's feet.")]
        [Min(0f)] public float groundProbeOffset;

        [Tooltip("Layers considered ground by the optional probe. The character's own layer is ignored.")]
        public LayerMask groundMask;

        [Tooltip("Height of the horizontal aim plane above the character's feet.")]
        [Min(0f)] public float aimPlaneHeight;

        [Tooltip("Farthest aim ray distance. Longer rays fall back to the last valid aim direction.")]
        [Min(0.01f)] public float maxAimDistance;

        /// <summary>Settings used when the component is first added. Nothing is written back by validation.</summary>
        public static PlayerControllerSettings Default => new PlayerControllerSettings
        {
            rotationMode = CharacterRotationMode.MovementDirection,
            aimSource = AimSource.ScreenCentre,
            walkSpeed = 4f,
            runSpeed = 7f,
            movementMode = MovementMode.Walk,
            sprintSpeed = 7f,
            acceleration = 0f,
            deceleration = 0f,
            gravity = 20f,
            jumpHeight = 1.2f,
            stickToGroundForce = 2f,
            rotationSpeed = 540f,
            aimRotationSpeed = 900f,
            groundProbeRadius = 0.15f,
            groundProbeOffset = 0.05f,
            groundMask = 0,
            aimPlaneHeight = 0f,
            maxAimDistance = 100f
        };

        /// <summary>
        /// Reports problems in plain text without changing any value. The caller decides how to surface them.
        /// </summary>
        public bool TryValidate(out string message)
        {
            List<string> problems = new List<string>();

            if (!IsFinite(walkSpeed) || walkSpeed < 0f) problems.Add("Walk speed must be finite and non-negative.");
            if (!IsFinite(runSpeed) || runSpeed < 0f) problems.Add("Run speed must be finite and non-negative.");
            if (IsFinite(runSpeed) && IsFinite(walkSpeed) && runSpeed < walkSpeed)
                problems.Add("Run speed is lower than walk speed.");
            if (!IsFinite(sprintSpeed) || sprintSpeed < 0f)
                problems.Add("Sprint speed must be finite and non-negative.");
            if (IsFinite(sprintSpeed) && IsFinite(runSpeed) && sprintSpeed < runSpeed)
                problems.Add("Sprint speed is lower than run speed.");
            if (!IsFinite(acceleration) || acceleration < 0f)
                problems.Add("Acceleration must be finite and non-negative.");
            if (!IsFinite(deceleration) || deceleration < 0f)
                problems.Add("Deceleration must be finite and non-negative.");
            if (!IsFinite(gravity) || gravity <= 0f) problems.Add("Gravity must be finite and positive.");
            if (!IsFinite(jumpHeight) || jumpHeight < 0f) problems.Add("Jump height must be finite and non-negative.");
            if (!IsFinite(stickToGroundForce) || stickToGroundForce < 0f)
                problems.Add("Stick-to-ground force must be finite and non-negative.");
            if (!IsFinite(rotationSpeed) || rotationSpeed < 0f)
                problems.Add("Rotation speed must be finite and non-negative; zero disables rotation.");
            if (!IsFinite(aimRotationSpeed) || aimRotationSpeed < 0f)
                problems.Add("Aim rotation speed must be finite and non-negative; zero disables rotation.");
            if (!IsFinite(groundProbeRadius) || groundProbeRadius < 0f)
                problems.Add("Ground probe radius must be finite and non-negative.");
            if (!IsFinite(groundProbeOffset) || groundProbeOffset < 0f)
                problems.Add("Ground probe offset must be finite and non-negative.");
            if (!IsFinite(aimPlaneHeight) || aimPlaneHeight < 0f)
                problems.Add("Aim plane height must be finite and non-negative.");
            if (!IsFinite(maxAimDistance) || maxAimDistance <= 0f)
                problems.Add("Maximum aim distance must be finite and positive.");

            if (!Enum.IsDefined(typeof(CharacterRotationMode), rotationMode))
                problems.Add("Rotation mode is not a defined CharacterRotationMode value.");
            if (!Enum.IsDefined(typeof(AimSource), aimSource))
                problems.Add("Aim source is not a defined AimSource value.");
            if (!Enum.IsDefined(typeof(MovementMode), movementMode))
                problems.Add("Movement mode is not a defined MovementMode value.");

            message = problems.Count == 0 ? string.Empty : string.Join(" ", problems);
            return problems.Count == 0;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
