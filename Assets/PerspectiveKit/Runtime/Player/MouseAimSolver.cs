using UnityEngine;

namespace PerspectiveKit
{
    /// <summary>Stateless camera-ray to ground-plane aim calculation.</summary>
    public static class MouseAimSolver
    {
        private const float MinVerticalComponent = 1e-5f;
        private const float MinHorizontalSqrMagnitude = 1e-6f;

        /// <summary>
        /// Computes a horizontal aim direction from a camera ray intersecting a plane through the character.
        /// </summary>
        /// <param name="camera">Camera used to build the ray. Not a PerspectiveCamera.</param>
        /// <param name="characterPosition">Character pivot on the ground.</param>
        /// <param name="pointerPosition">Absolute pointer pixels; ignored for <see cref="AimSource.ScreenCentre"/>.</param>
        /// <param name="source">Which screen position the ray starts from.</param>
        /// <param name="aimPlaneHeight">Height above the character pivot where the horizontal plane sits.</param>
        /// <param name="maxAimDistance">
        /// Farthest valid intersection distance, measured along the ray from the camera origin.
        /// Longer rays use the fallback chain.
        /// </param>
        /// <param name="lastValidDirection">Caller-owned cache of the last valid horizontal direction.</param>
        /// <param name="currentFacing">Character's current forward direction, used as the third fallback.</param>
        /// <param name="aimDirection">Finite, normalized horizontal direction with y = 0 in every case.</param>
        /// <returns>True only for a fresh valid ray/plane result; false when a fallback was used.</returns>
        public static bool TryGetAimDirection(
            Camera camera,
            Vector3 characterPosition,
            Vector2 pointerPosition,
            AimSource source,
            float aimPlaneHeight,
            float maxAimDistance,
            Vector3 lastValidDirection,
            Vector3 currentFacing,
            out Vector3 aimDirection)
        {
            if (camera == null ||
                source != AimSource.ScreenCentre && source != AimSource.PointerPosition ||
                !IsFinite(characterPosition) ||
                !IsFinite(aimPlaneHeight) ||
                !IsFinite(maxAimDistance) ||
                maxAimDistance <= 0f ||
                source == AimSource.PointerPosition && !IsFinite(pointerPosition))
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            Ray ray = source == AimSource.ScreenCentre
                ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : camera.ScreenPointToRay(new Vector3(pointerPosition.x, pointerPosition.y, 0f));

            if (!IsFinite(ray.origin) || !IsFinite(ray.direction))
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            // A ray parallel to the horizontal plane has no unique intersection.
            if (Mathf.Abs(ray.direction.y) < MinVerticalComponent)
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            Vector3 planePoint = new Vector3(
                characterPosition.x,
                characterPosition.y + aimPlaneHeight,
                characterPosition.z);
            if (!IsFinite(planePoint))
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            Plane plane = new Plane(Vector3.up, planePoint);
            if (!plane.Raycast(ray, out float enter) ||
                !IsFinite(enter) ||
                enter <= 0f ||
                enter > maxAimDistance)
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            Vector3 hitPoint = ray.GetPoint(enter);
            if (!IsFinite(hitPoint))
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            Vector3 horizontal = hitPoint - characterPosition;
            horizontal.y = 0f;
            if (!IsFinite(horizontal) || horizontal.sqrMagnitude < MinHorizontalSqrMagnitude)
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            aimDirection = horizontal.normalized;
            if (!IsFinite(aimDirection) || Mathf.Abs(aimDirection.y) > MinVerticalComponent)
            {
                return Fallback(camera, lastValidDirection, currentFacing, out aimDirection);
            }

            return true;
        }

        private static bool Fallback(
            Camera camera,
            Vector3 lastValidDirection,
            Vector3 currentFacing,
            out Vector3 aimDirection)
        {
            if (TryHorizontal(lastValidDirection, out aimDirection)) return false;
            if (camera != null && TryHorizontal(camera.transform.forward, out aimDirection)) return false;
            if (TryHorizontal(currentFacing, out aimDirection)) return false;
            aimDirection = Vector3.forward;
            return false;
        }

        private static bool TryHorizontal(Vector3 value, out Vector3 direction)
        {
            direction = Vector3.forward;
            if (!IsFinite(value)) return false;

            Vector3 horizontal = value;
            horizontal.y = 0f;
            if (!IsFinite(horizontal) || horizontal.sqrMagnitude < MinHorizontalSqrMagnitude) return false;

            direction = horizontal.normalized;
            if (!IsFinite(direction) || Mathf.Abs(direction.y) > MinVerticalComponent)
            {
                direction = Vector3.forward;
                return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
