using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PerspectiveKit.Tests
{
    /// <summary>PlayMode coverage for third-person collision using real Unity physics.</summary>
    public class PerspectiveCameraCollisionTests
    {
        private const int ObstacleLayer = 8;

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
        public IEnumerator WallBlocksThirdPersonCamera()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            Track(CreateWall(new Vector3(0f, 1f, -2f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings).gameObject).GetComponent<PerspectiveCamera>();

            yield return Settle(3);

            float distance = Vector3.Distance(camera.transform.position, target.position);
            Assert.Greater(distance, 0f, "Camera should not be pulled onto the pivot unnecessarily.");
            Assert.Less(distance, settings.distance, "Wall should shorten the boom.");
            AssertCameraClear(camera, settings.collision);
        }

        [UnityTest]
        public IEnumerator PaddingOnlyClearance_NoExtraRadiusPullback()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            Track(CreateWall(new Vector3(0f, 1f, -2f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings).gameObject).GetComponent<PerspectiveCamera>();

            yield return Settle(3);

            Assert.IsTrue(Physics.SphereCast(
                target.position,
                settings.collision.radius,
                Vector3.back,
                out RaycastHit hit,
                settings.distance,
                settings.collision.collisionMask,
                QueryTriggerInteraction.Ignore));

            float expected = Mathf.Max(0f, hit.distance - settings.collision.padding);
            float actual = Vector3.Distance(camera.transform.position, target.position);
            Assert.AreEqual(expected, actual, 0.02f,
                "Clearance must be padding only; SphereCast distance already includes the sphere radius.");
            Assert.IsFalse(Physics.CheckSphere(camera.transform.position, settings.collision.radius,
                settings.collision.collisionMask, QueryTriggerInteraction.Ignore));
        }

        [UnityTest]
        public IEnumerator ClearanceBelowMinimumDistance_NeverForcesMinimum()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            settings.minimumDistance = 2f;
            Track(CreateWall(new Vector3(0f, 1f, -0.5f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings).gameObject).GetComponent<PerspectiveCamera>();

            yield return Settle(3);

            float distance = Vector3.Distance(camera.transform.position, target.position);
            Assert.Less(distance, settings.minimumDistance, "Clearance must win over the preferred minimum.");
            AssertCameraClear(camera, settings.collision);
        }

        [UnityTest]
        public IEnumerator SwitchingToThirdPersonWithObstacle_NeverPenetratesDuringTransition()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            Track(CreateWall(new Vector3(0f, 1f, -2f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings, Perspective.FirstPerson).gameObject)
                .GetComponent<PerspectiveCamera>();
            SetField(camera, "transitionDuration", 0.2f);

            yield return Settle(2);
            camera.TogglePerspective();

            for (int frame = 0; frame < 24; frame++)
            {
                yield return null;
                AssertCameraClear(camera, settings.collision,
                    "Camera penetrated during transition at blend " + camera.Blend);
                Assert.LessOrEqual(
                    Vector3.Distance(camera.transform.position, target.position),
                    settings.distance + 0.001f);
            }
        }

        [UnityTest]
        public IEnumerator ObstacleRemoved_RecoversGraduallyAndNeverOvershoots()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            settings.collision.recoverySpeed = 2f;
            GameObject wall = Track(CreateWall(new Vector3(0f, 1f, -2f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings).gameObject).GetComponent<PerspectiveCamera>();

            yield return Settle(3);
            float blockedDistance = Vector3.Distance(camera.transform.position, target.position);

            UnityEngine.Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            yield return null;
            float firstRecovery = Vector3.Distance(camera.transform.position, target.position);
            Assert.Greater(firstRecovery, blockedDistance, "Recovery should start immediately.");
            Assert.Less(firstRecovery, settings.distance, "Recovery must be smooth, not a snap.");

            yield return Settle(90);
            float recovered = Vector3.Distance(camera.transform.position, target.position);
            Assert.Greater(recovered, firstRecovery);
            Assert.LessOrEqual(recovered, settings.distance + 0.001f, "Recovery must never exceed desired distance.");
        }

        [UnityTest]
        public IEnumerator BlockedPivot_PullsCameraOntoTarget()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            Track(CreateWall(target.position, Vector3.one));
            PerspectiveCamera camera = Track(CreateCamera(target, DefaultSettings()).gameObject)
                .GetComponent<PerspectiveCamera>();

            yield return Settle(3);

            Assert.LessOrEqual(Vector3.Distance(camera.transform.position, target.position), 0.001f);
        }

        [UnityTest]
        public IEnumerator CharacterLayerExcluded_DoesNotBlockCamera()
        {
            GameObject targetObject = Track(CreateTarget(new Vector3(0f, 1f, 0f)));
            targetObject.AddComponent<CapsuleCollider>();
            ThirdPersonSettings settings = DefaultSettings();
            PerspectiveCamera camera = Track(CreateCamera(targetObject.transform, settings).gameObject)
                .GetComponent<PerspectiveCamera>();

            yield return Settle(3);

            Assert.AreEqual(settings.distance,
                Vector3.Distance(camera.transform.position, targetObject.transform.position), 0.01f);
        }

        [UnityTest]
        public IEnumerator FirstPersonOffset_IsPreserved()
        {
            GameObject targetObject = Track(CreateTarget(new Vector3(0f, 1f, 0f)));
            ThirdPersonSettings settings = DefaultSettings();
            PerspectiveCamera camera = Track(CreateCamera(targetObject.transform, settings, Perspective.FirstPerson).gameObject)
                .GetComponent<PerspectiveCamera>();

            FirstPersonSettings firstPerson = new FirstPersonSettings
            {
                offset = new Vector3(0.1f, 0.2f, 0f),
                mouseSensitivity = new Vector2(0.1f, 0.1f),
                stickSensitivity = new Vector2(120f, 120f),
                pitchLimits = new Vector2(-85f, 85f),
                fieldOfView = 75f
            };
            SetField(camera, "firstPerson", firstPerson);

            yield return Settle(3);

            Vector3 expected = targetObject.transform.TransformPoint(firstPerson.offset);
            Assert.Less(Vector3.Distance(camera.transform.position, expected), 0.01f);
        }

        [UnityTest]
        public IEnumerator ObstacleAppearingDuringRecovery_PullsInImmediately()
        {
            Transform target = Track(CreateTarget(new Vector3(0f, 1f, 0f))).transform;
            ThirdPersonSettings settings = DefaultSettings();
            settings.collision.recoverySpeed = 2f;
            GameObject wall = Track(CreateWall(new Vector3(0f, 1f, -2f), new Vector3(10f, 4f, 0.2f)));
            PerspectiveCamera camera = Track(CreateCamera(target, settings).gameObject).GetComponent<PerspectiveCamera>();

            yield return Settle(3);
            UnityEngine.Object.DestroyImmediate(wall);
            yield return Settle(6);
            float recoveringDistance = Vector3.Distance(camera.transform.position, target.position);

            Track(CreateWall(new Vector3(0f, 1f, -1f), new Vector3(10f, 4f, 0.2f)));
            Physics.SyncTransforms();
            yield return null;

            float afterObstacle = Vector3.Distance(camera.transform.position, target.position);
            Assert.Less(afterObstacle, recoveringDistance, "A new obstacle must pull in on the same frame.");
            AssertCameraClear(camera, settings.collision);
        }

        private static GameObject CreateTarget(Vector3 position)
        {
            GameObject go = new GameObject("TestTarget");
            go.transform.position = position;
            return go;
        }

        private static GameObject CreateWall(Vector3 centre, Vector3 size)
        {
            GameObject go = new GameObject("TestWall");
            go.layer = ObstacleLayer;
            go.transform.position = centre;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            Physics.SyncTransforms();
            return go;
        }

        private static PerspectiveCamera CreateCamera(
            Transform target,
            ThirdPersonSettings thirdPerson,
            Perspective initial = Perspective.ThirdPerson)
        {
            GameObject go = new GameObject("TestCamera");
            go.SetActive(false);
            go.AddComponent<Camera>();
            PerspectiveCamera component = go.AddComponent<PerspectiveCamera>();
            SetField(component, "initialPerspective", initial);
            SetField(component, "transitionDuration", 0f);
            SetField(component, "thirdPerson", thirdPerson);
            component.CameraTarget = target;
            go.SetActive(true);
            return component;
        }

        private static ThirdPersonSettings DefaultSettings()
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
                    collisionMask = 1 << ObstacleLayer,
                    recoverySpeed = 8f
                }
            };
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing field: " + name);
            field.SetValue(target, value);
        }

        private static IEnumerator Settle(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                yield return null;
            }
        }

        private static void AssertCameraClear(
            PerspectiveCamera camera,
            CameraCollisionSettings settings,
            string message = "Camera sphere is inside geometry.")
        {
            Assert.IsFalse(
                Physics.CheckSphere(camera.transform.position, settings.radius, settings.collisionMask,
                    QueryTriggerInteraction.Ignore),
                message);
        }

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }
    }
}
