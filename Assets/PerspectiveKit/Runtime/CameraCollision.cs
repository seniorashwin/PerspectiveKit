using UnityEngine;

namespace PerspectiveKit
{
    /// <summary>Result of a camera collision query.</summary>
    public struct CameraCollisionResult
    {
        /// <summary>Maximum distance the camera centre may travel along the boom.</summary>
        public float safeDistance;

        /// <summary>True when geometry limits the boom or the pivot itself is blocked.</summary>
        public bool blocked;
    }

    /// <summary>Stateless third-person camera collision queries using Unity physics.</summary>
    public static class CameraCollision
    {
        private const float MinProbeRadius = 0.01f;
        private const float MinBoomDistance = 0.0001f;
        // Keep a positive gap so the final-position CheckSphere cannot report a false overlap
        // when the sphere is exactly tangent to a surface.
        private const float MinPadding = 0.001f;

        /// <summary>
        /// Checks the pivot for overlap, then casts a sphere toward the desired camera position.
        /// An overlapping pivot always wins over the cast, which can otherwise miss when it starts inside geometry.
        /// </summary>
        public static CameraCollisionResult Solve(
            Vector3 pivot,
            Vector3 direction,
            float desiredDistance,
            CameraCollisionSettings settings)
        {
            float radius = Mathf.Max(MinProbeRadius, settings.radius);
            float padding = Mathf.Max(MinPadding, settings.padding);

            if (Physics.CheckSphere(pivot, radius, settings.collisionMask, QueryTriggerInteraction.Ignore))
            {
                return new CameraCollisionResult { safeDistance = 0f, blocked = true };
            }

            if (desiredDistance <= MinBoomDistance)
            {
                return new CameraCollisionResult { safeDistance = 0f, blocked = false };
            }

            if (Physics.SphereCast(
                    pivot,
                    radius,
                    direction,
                    out RaycastHit hit,
                    desiredDistance,
                    settings.collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                // For a sphere cast, hit.distance is the distance the sphere travelled
                // before contact, so the radius is already accounted for. Only padding
                // is subtracted; subtracting the radius again wastes camera distance.
                return new CameraCollisionResult
                {
                    safeDistance = Mathf.Max(0f, hit.distance - padding),
                    blocked = true
                };
            }

            return new CameraCollisionResult { safeDistance = desiredDistance, blocked = false };
        }

        /// <summary>Checks whether a point is inside blocking geometry, used for the final camera position.</summary>
        public static bool IsBlockedAt(Vector3 point, float radius, LayerMask mask)
        {
            return Physics.CheckSphere(point, Mathf.Max(MinProbeRadius, radius), mask, QueryTriggerInteraction.Ignore);
        }
    }
}
