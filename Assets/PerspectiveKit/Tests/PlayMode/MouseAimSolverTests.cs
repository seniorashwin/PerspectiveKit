using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PerspectiveKit.Tests
{
    /// <summary>PlayMode coverage for the stateless mouse-aim solver.</summary>
    public class MouseAimSolverTests
    {
        private const float Tolerance = 0.01f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            _created.Clear();
        }

        [UnityTest]
        public IEnumerator ScreenCentre_RayHitsPlane_ReturnsFreshDirection()
        {
            Camera camera = CreateCamera(new Vector3(0f, 4f, -10f), new Vector3(5f, 0f, 0f));
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 direction);

            Assert.IsTrue(fresh, "A centre ray with a valid hit must report a fresh result.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator PointerPosition_RayHitsPlane_ReturnsFreshDirection()
        {
            Camera camera = CreateCamera(new Vector3(0f, 4f, -10f), Vector3.zero);
            yield return null;

            Vector2 pixel = camera.WorldToScreenPoint(new Vector3(5f, 0f, 0f));
            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, pixel, AimSource.PointerPosition,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 direction);

            Assert.IsTrue(fresh, "A pointer ray with a valid hit must report a fresh result.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator AimPlaneHeight_ChangesTheIntersectionPlane()
        {
            Camera camera = CreateCamera(new Vector3(0f, 2f, -10f), new Vector3(3f, 1f, 0f));
            yield return null;

            bool raisedHit = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                1f, 100f, Vector3.zero, Vector3.zero, out Vector3 raised);
            bool groundHit = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 ground);

            Assert.IsTrue(raisedHit);
            Assert.IsTrue(groundHit);
            AssertDirection(raised, Vector3.right);
            Assert.Greater(Vector3.Distance(raised, ground), Tolerance,
                "Changing the plane height must move the intersection point.");
        }

        [UnityTest]
        public IEnumerator ParallelRay_UsesFallback()
        {
            Camera camera = CreateCamera(new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 10f));
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.forward, Vector3.right, out Vector3 direction);

            Assert.IsFalse(fresh);
            AssertDirection(direction, Vector3.forward);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator BehindCameraIntersection_UsesFallback()
        {
            Camera camera = CreateCamera(new Vector3(0f, 2f, 0f), Vector3.zero);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.right, Vector3.zero, out Vector3 direction);

            Assert.IsFalse(fresh);
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator IntersectionBeyondMaxDistance_UsesFallback()
        {
            Camera camera = CreateCamera(new Vector3(0f, 5f, -100f), Vector3.zero);
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 10f, Vector3.zero, Vector3.zero, out Vector3 direction);

            Assert.IsFalse(fresh, "Distance is measured from the camera origin along the ray.");
            AssertDirection(direction, Vector3.forward);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator DegenerateHorizontalDirection_UsesFallback()
        {
            Camera camera = CreateCamera(new Vector3(0f, 5f, 0f), Vector3.zero);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.right, out Vector3 direction);

            Assert.IsFalse(fresh);
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator InvalidInputsAndEnums_UseFallback()
        {
            Camera camera = CreateCamera(new Vector3(0f, 4f, -10f), Vector3.zero);
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                null, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.right, Vector3.zero, out Vector3 direction);
            Assert.IsFalse(fresh, "Null camera must fall back.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);

            fresh = MouseAimSolver.TryGetAimDirection(
                camera, new Vector3(float.NaN, 0f, 0f), Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.right, Vector3.zero, out direction);
            Assert.IsFalse(fresh, "NaN character position must fall back.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);

            fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, new Vector2(float.PositiveInfinity, 0f), AimSource.PointerPosition,
                0f, 100f, Vector3.right, Vector3.zero, out direction);
            Assert.IsFalse(fresh, "Infinite pointer position must fall back.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);

            fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 0f, Vector3.right, Vector3.zero, out direction);
            Assert.IsFalse(fresh, "Zero max distance must fall back.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);

            fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, (AimSource)99,
                0f, 100f, Vector3.right, Vector3.zero, out direction);
            Assert.IsFalse(fresh, "Undefined enum must fall back.");
            AssertDirection(direction, Vector3.right);
            AssertFiniteHorizontal(direction);
        }

        [UnityTest]
        public IEnumerator FallbackOrder_IsLastDirectionThenCameraThenFacingThenWorld()
        {
            Camera camera = CreateCamera(new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 10f));
            yield return null;

            bool fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.right, new Vector3(0f, 0f, -1f), out Vector3 lastDirection);
            Assert.IsFalse(fresh);
            AssertDirection(lastDirection, Vector3.right, "Last valid direction must win.");

            fresh = MouseAimSolver.TryGetAimDirection(
                camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.right, out Vector3 cameraDirection);
            Assert.IsFalse(fresh);
            AssertDirection(cameraDirection, Vector3.forward, "Camera forward must win next.");

            fresh = MouseAimSolver.TryGetAimDirection(
                null, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, new Vector3(0f, 0f, -1f), out Vector3 facingDirection);
            Assert.IsFalse(fresh);
            AssertDirection(facingDirection, Vector3.back, "Current facing must win next.");

            fresh = MouseAimSolver.TryGetAimDirection(
                null, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 worldDirection);
            Assert.IsFalse(fresh);
            AssertDirection(worldDirection, Vector3.forward, "World forward is the final fallback.");
        }

        [UnityTest]
        public IEnumerator OutputIsAlwaysFiniteNormalizedAndHorizontal()
        {
            Camera camera = CreateCamera(new Vector3(0f, 4f, -10f), new Vector3(5f, 0f, 0f));
            yield return null;

            MouseAimSolver.TryGetAimDirection(camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 fresh);
            MouseAimSolver.TryGetAimDirection(null, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, new Vector3(float.NaN, 0f, 0f), new Vector3(0f, float.PositiveInfinity, 0f), out Vector3 fallback);
            MouseAimSolver.TryGetAimDirection(camera, Vector3.zero, Vector2.zero, AimSource.ScreenCentre,
                0f, 100f, Vector3.zero, Vector3.zero, out Vector3 secondFresh);

            AssertFiniteHorizontal(fresh);
            AssertFiniteHorizontal(fallback);
            AssertFiniteHorizontal(secondFresh);
        }

        [UnityTest]
        public IEnumerator SolverDoesNotMutateCallerState()
        {
            Camera camera = CreateCamera(new Vector3(0f, 4f, -10f), Vector3.zero);
            yield return null;

            Vector3 character = Vector3.zero;
            Vector2 pointer = Vector2.one;
            Vector3 cached = Vector3.right;
            Vector3 facing = Vector3.forward;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;

            MouseAimSolver.TryGetAimDirection(
                camera, character, pointer, AimSource.PointerPosition,
                0f, 100f, cached, facing, out _);

            Assert.AreEqual(character, Vector3.zero);
            Assert.AreEqual(pointer, Vector2.one);
            Assert.AreEqual(cached, Vector3.right);
            Assert.AreEqual(facing, Vector3.forward);
            Assert.AreEqual(cameraPosition, camera.transform.position);
            Assert.AreEqual(cameraRotation, camera.transform.rotation);
        }

        private static void AssertDirection(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.Less(Vector3.Distance(actual, expected.normalized), Tolerance,
                message ?? "Aim direction mismatch.");
        }

        private static void AssertFiniteHorizontal(Vector3 direction)
        {
            Assert.IsFalse(float.IsNaN(direction.x) || float.IsInfinity(direction.x), "x must be finite.");
            Assert.IsFalse(float.IsNaN(direction.y) || float.IsInfinity(direction.y), "y must be finite.");
            Assert.IsFalse(float.IsNaN(direction.z) || float.IsInfinity(direction.z), "z must be finite.");
            Assert.Less(Mathf.Abs(direction.y), 0.0001f, "Direction must be horizontal.");
            Assert.Less(Mathf.Abs(direction.magnitude - 1f), Tolerance, "Direction must be normalized.");
        }

        private Camera CreateCamera(Vector3 position, Vector3 lookAt)
        {
            GameObject go = new GameObject("AimCamera");
            go.transform.position = position;
            go.transform.LookAt(lookAt);
            Camera camera = go.AddComponent<Camera>();
            camera.pixelRect = new Rect(0f, 0f, 640f, 480f);
            _created.Add(go);
            return camera;
        }
    }
}
