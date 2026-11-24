using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PerspectiveKit
{
    /// <summary>The perspective the camera is using, or transitioning toward.</summary>
    public enum Perspective
    {
        /// <summary>Camera sits at the character's eye point.</summary>
        FirstPerson,

        /// <summary>Camera orbits the character from behind.</summary>
        ThirdPerson
    }

    /// <summary>Decides who enables and disables an assigned input action.</summary>
    public enum InputOwnership
    {
        /// <summary>
        /// The host controller owns the action. PerspectiveKit only reads it and never
        /// calls <see cref="InputAction.Enable"/> or <see cref="InputAction.Disable"/>.
        /// </summary>
        Host = 0,

        /// <summary>
        /// PerspectiveKit enables the action on enable and disables it on disable, but only
        /// when it was not already enabled by another system at the time it was acquired.
        /// </summary>
        PerspectiveKit = 1
    }

    /// <summary>An input reference together with its ownership contract.</summary>
    [Serializable]
    public struct ManagedInputAction
    {
        /// <summary>Action to read. Null disables this input.</summary>
        public InputActionReference action;

        /// <summary>Whether the host or PerspectiveKit manages the action's enabled state.</summary>
        public InputOwnership ownership;
    }

    /// <summary>Settings used while in first-person perspective.</summary>
    [Serializable]
    public struct FirstPersonSettings
    {
        /// <summary>Offset from CameraTarget in the target's local space. Zero assumes the target is at eye level.</summary>
        public Vector3 offset;

        /// <summary>Mouse look sensitivity, in degrees per pixel.</summary>
        public Vector2 mouseSensitivity;

        /// <summary>Gamepad stick look sensitivity, in degrees per second.</summary>
        public Vector2 stickSensitivity;

        /// <summary>Minimum and maximum pitch in degrees.</summary>
        public Vector2 pitchLimits;

        /// <summary>Camera field of view in degrees.</summary>
        public float fieldOfView;
    }

    /// <summary>Obstruction settings for the third-person camera boom.</summary>
    [Serializable]
    public struct CameraCollisionSettings
    {
        /// <summary>Radius of the sphere used to probe for obstacles.</summary>
        [Min(0.01f)] public float radius;

        /// <summary>Extra clearance kept between the camera sphere and a hit surface.</summary>
        [Min(0f)] public float padding;

        /// <summary>Layers that block the camera. Must not include the character's own colliders.</summary>
        public LayerMask collisionMask;

        /// <summary>Units per second used only when the camera recovers after an obstruction clears.</summary>
        [Min(0f)] public float recoverySpeed;
    }

    /// <summary>Settings used while in third-person perspective.</summary>
    [Serializable]
    public struct ThirdPersonSettings
    {
        /// <summary>Mouse look sensitivity, in degrees per pixel.</summary>
        public Vector2 mouseSensitivity;

        /// <summary>Gamepad stick look sensitivity, in degrees per second.</summary>
        public Vector2 stickSensitivity;

        /// <summary>Minimum and maximum pitch in degrees.</summary>
        public Vector2 pitchLimits;

        /// <summary>Camera field of view in degrees.</summary>
        public float fieldOfView;

        /// <summary>
        /// Preferred minimum boom distance. Applied to the configured TPP pose only;
        /// collision clearance can still put the camera closer than this.
        /// </summary>
        [Min(0f)] public float minimumDistance;

        /// <summary>Distance behind the target.</summary>
        public float distance;

        /// <summary>Height above the target.</summary>
        public float height;

        /// <summary>Sideways offset from the target. Positive is to the right.</summary>
        public float shoulderOffset;

        /// <summary>Obstruction handling for the camera boom.</summary>
        public CameraCollisionSettings collision;
    }
}
