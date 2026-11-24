# PerspectiveKit Development Log

Living document. Update at the end of every approved development stage.
Last updated: 2026-11-24 (Stage 2 implementation, verification pending).

## 1. Purpose, scope, constraints

**Purpose:** reusable, commercial-quality FPP/TPP camera system for Unity 6, distributed on itch.io. Developers integrate it into their own character controllers without replacing movement or animation.

**Scope (v1):** perspective switching, configurable New Input System input, smooth transitions, independent FPP/TPP settings, TPP distance/height/shoulder offset, TPP obstruction handling, configurable character mesh visibility in FPP, inspector configuration, clean separation from character movement.

**Constraints:**
- Unity 6, C#, Linux and Windows development.
- Native Unity APIs only. No Cinemachine, no paid dependencies, no external services.
- Runtime and editor code separated.
- Avoid unnecessary abstractions; design for extensibility without building future features.

**Verified environment:** Unity `6000.6.0f1`; `com.unity.inputsystem 1.20.0`; new Input System active (`activeInputHandler: 1`); Test Framework `1.8.0` available via `com.unity.feature.development`; no Cinemachine, no URP/HDRP installed.

## 2. Stage status

| Stage | Scope | Status |
|---|---|---|
| 1 | Core component, FPP/TPP switching, settings, transitions, FPP offset, input ownership | **Completed** (2026-11-24) |
| 2 | Third-person camera collision and obstruction handling | **In progress** — implemented 2026-11-24; Unity verification and automated test run pending |
| 3 | Character renderer visibility in FPP | **Planned**, not specified |
| 4 | Automated test suite, demo scene, distribution packaging | **Planned**, not specified |

## 3. Architecture decisions and reasons

| Decision | Reason |
|---|---|
| One `PerspectiveCamera` component plus one stateless collision helper (Stage 2) | Smallest surface for users; helper only where logic is risky/testable. |
| Camera reads a developer-supplied `CameraTarget` Transform and never moves the character | Keeps movement/animation ownership with the host controller. |
| Settings as serialized structs on the component, not ScriptableObjects | Inspector configuration without asset indirection. |
| `PerspectiveKit.Runtime` and `PerspectiveKit.Editor` assembly definitions | Enforces runtime/editor separation mechanically. |
| Input references with per-action `InputOwnership` (`Host` default, `PerspectiveKit`) | Shared actions must never be disabled by PerspectiveKit. Only actions this component enabled are disabled by it. |
| Mouse and stick sensitivity are separate fields | They use different units (degrees/pixel vs degrees/second); avoids a hidden conversion factor. |
| Read input in `Update`, write camera in `LateUpdate` | Target transform is final when sampled; `WasPressedThisFrame` stays reliable. |
| Yaw/pitch shared, blend interpolates position/FOV/pitch limits | Switching never snaps orientation; interruption of a transition needs no state machine. |
| FPP offset applied with `TransformPoint` (target local space) | Follows character rotation/crouch; zero default preserves eye-level targets. |
| Final collision cap will apply to the blended pose, never scaled by blend (Stage 2) | Prevents a transition from weakening obstacle safety. |
| Keep everything under `Assets/PerspectiveKit` for now | No package/preparation work until initial implementation is validated. |

## 3b. Stage 2 review decisions (2026-11-24)

These decisions were reviewed before implementation and are now reflected in the Stage 2 code. Their runtime behaviour has **not** been verified in Unity.

1. No collision bypass for short desired distances: the FPP-degenerate case is handled by the origin check, not by skipping the cast while `distance <= radius`.
2. `finalDistance = Min(_smoothedDistance, desiredDistance, safeDistance)` at every frame, including recovery and when the obstruction disappears.
3. `minimumDistance` is a clamp on the configured TPP boom length only. It never appears in the collision solver and never overrides clearance.
4. `CheckSphere` at the pivot covers colliders overlapping the cast origin; a second `CheckSphere` at the candidate final position covers cast misses. Causes of misses that remain possible are documented limitations.
5. `_smoothedDistance` is initialized lazily on the first valid frame with a target, preserved across disable/enable, and invalidated when `CameraTarget` changes. It is never reset on perspective change.
6. Pull-in is immediate: when an obstacle is detected, `_smoothedDistance` snaps to the safe distance. Smoothing applies only to recovery after the obstruction clears. `pullInSpeed` is removed from the settings because a hard safety clamp made it ineffective.

## 4. Stage 1 — implementation record

**Files created:**
- `Assets/PerspectiveKit/Runtime/PerspectiveCamera.cs` — component: input reading with ownership rules, yaw/pitch, blend, pose calculation, transform/FOV write, CameraTarget validation.
- `Assets/PerspectiveKit/Runtime/PerspectiveCameraSettings.cs` — `Perspective`, `InputOwnership`, `ManagedInputAction`, `FirstPersonSettings`, `ThirdPersonSettings`.
- `Assets/PerspectiveKit/Runtime/Input/PerspectiveKit_Actions.inputactions` — minimal Camera map: Look and TogglePerspective.
- `Assets/PerspectiveKit/Runtime/PerspectiveKit.Runtime.asmdef`, `Assets/PerspectiveKit/Editor/PerspectiveKit.Editor.asmdef`.
- Empty folders: `Assets/PerspectiveKit/Tests/EditMode`, `Tests/PlayMode`, `Demo`.

**Post-implementation fix:** a host script's `Awake` could call `SetPerspective` before this component's `Awake`, which then overwrote it with `initialPerspective`. Added `_perspectiveExplicitlySet`. Also added a guard that rejects a `CameraTarget` equal to the camera or one of its children, to prevent a feedback loop that moved the camera backwards every frame.

**Verification — verified by the agent:**
- `dotnet build` of both runtime `.cs` files against this project's Unity 6 `UnityEngine.CoreModule.dll` and `Unity.InputSystem.dll`: succeeded, **0 errors, 2 expected CS0649 warnings** (serialized inspector fields).
- All JSON/asmdef/inputactions files parsed successfully.
- Unity generated `.meta` files on import.

**Verification — reported by the user, manual in Unity 6 (not executed by the agent):**
- FPP/TPP toggle works with PerspectiveKit input ownership.
- Camera drift no longer occurs.
- CameraTarget validation guard is applied.

**Automated tests: NOT run.** No PlayMode or EditMode tests have been created or executed. The obstruction-during-transition PlayMode test is deferred to Stage 2.

## 4b. Stage 2 — implementation record (2026-11-24)

**Files created:**
- `Assets/PerspectiveKit/Runtime/CameraCollision.cs` — stateless `Solve` (pivot overlap check, spherecast) and `IsBlockedAt` (final-position overlap check).
- `Assets/PerspectiveKit/Tests/PlayMode/PerspectiveKit.Tests.PlayMode.asmdef` — PlayMode test assembly.
- `Assets/PerspectiveKit/Tests/PlayMode/PerspectiveCameraCollisionTests.cs` — 9 physics-based PlayMode tests (8 original + the padding-clearance test added after review).

**Files modified:**
- `Assets/PerspectiveKit/Runtime/PerspectiveCameraSettings.cs` — added `CameraCollisionSettings` (radius, padding, collisionMask, recoverySpeed; no `pullInSpeed`) and `ThirdPersonSettings.minimumDistance` and `collision`.
- `Assets/PerspectiveKit/Runtime/PerspectiveCamera.cs` — added `_smoothedDistance` / `_distanceInitialized`; TPP pose uses `Max(minimumDistance, distance)`; collision applied to the final blended position; immediate pull-in, recovery-only smoothing; reset of `_distanceInitialized` in the `CameraTarget` setter. Stage 1 input, events, blend and FPP behaviour untouched.

**Clearance detail (corrected 2026-11-24):** Unity 6 defines `RaycastHit.distance` for a swept volume as the distance travelled before contact, so the sphere radius is already included. `CameraCollision.Solve` uses `safeDistance = Max(0, hit.distance - padding)`; subtracting the radius again was removed. A minimum padding of `0.001f` keeps the final-position `CheckSphere` from reporting a false tangency. That second `CheckSphere` remains the safety net for cast misses.

**Clearance correction — exact change:** `Assets/PerspectiveKit/Runtime/CameraCollision.cs` changed `safeDistance = Mathf.Max(0f, hit.distance - radius - padding)` to `safeDistance = Mathf.Max(0f, hit.distance - padding)`, with the explanatory comment updated. `PerspectiveCamera.cs` gained a comment documenting why the final-position overlap check must not fire on a clear hit. A new PlayMode test `PaddingOnlyClearance_NoExtraRadiusPullback` compares the settled camera distance with `hit.distance - padding`.

**Verification — verified by the agent:**
- Runtime compile check (`Compile.csproj`, netstandard2.1) against this project's Unity 6 `UnityEngine.CoreModule.dll`, `UnityEngine.PhysicsModule.dll` and `Unity.InputSystem.dll`: **succeeded, 0 errors**, 2 expected CS0649 warnings.
- Test compile check (`Tests.csproj`) additionally referencing `UnityEngine.TestRunner.dll` and `nunit.framework.dll`: **succeeded, 0 errors**, same 2 warnings.
- After the clearance correction, both compile checks were re-run: **runtime and test builds both succeeded, 0 errors**, same 2 expected warnings.
- JSON/asmdef parsing: not re-run this stage (the new asmdef is simple JSON; Unity will validate on import).

**Automated tests: created but NOT executed.** The project is open in the Unity editor (PID 40195, `Temp/UnityLockfile` present), so a batchmode test run was not possible. No test result is claimed. Manual results from the user are recorded separately in section 6 and must not be read as automated test results.

## 5. Known bugs, limitations, unresolved issues

- **Not implemented (by design):** character renderer visibility (Stage 3).
- **Stage 2 was verified manually by the user in Unity 6** (see section 6). The nine PlayMode tests still have not been executed, so no automated result is claimed.
- **Collision mask must exclude the character's layer.** The default mask is `~0` (everything); with a character collider on the pivot layer the `CheckSphere` origin test will report blocked and pin the camera to the target. Set `ThirdPerson.collision.collisionMask` before use.
- **No automatic layer setup** and no camera fade on obstruction.
- **`minimumDistance` is a framing clamp only**; obstruction clearance can put the camera closer than it.
- **SphereCast limitations remain:** non-convex mesh colliders, thin/backface surfaces and fast-moving obstacles can be missed. The final-position `CheckSphere` is a safety net, not a guarantee for every case.
- **Recovery speed needs manual tuning:** too high feels like a snap, too low feels stuck.
- **CameraTarget guard is ancestry-only.** It catches a target under the camera, but a host script that writes `cameraTarget.position` from the camera each frame can still produce feedback drift.
- **Touch/pen look delta** is scaled like a stick, not like mouse pixels. Only mouse-style devices are detected.
- **No runtime zoom, custom inspector, automatic collision-layer setup, or package distribution structure.**
- **Ownership is a contract:** if another system enables an action after PerspectiveKit enabled it, PerspectiveKit will still disable it on teardown.
- **`Assets/Scenes/SampleScene.unity` was modified outside this implementation work** (Ground, Player, CameraTarget, configured `PerspectiveCamera`). Treat as user work; do not overwrite.
- **Assumption not verified:** the manual test report says PerspectiveKit input ownership was used; the last scene file inspected still showed `ownership: 0` (`Host`) for both actions. No conclusion is drawn from this discrepancy.

## 6. Manual Unity verification record

| Step | Result | Source |
|---|---|---|
| Drop component on camera, assign CameraTarget, press toggle | FPP/TPP switch works | User-reported manual test |
| Leave component in TPP, observe position over time | No backwards drift | User-reported manual test |
| Assign camera (or child) as CameraTarget | Guard rejects it; error logged | Guard verified in code; Unity behaviour not re-verified by agent |
| Compile project | 0 errors, 2 expected CS0649 warnings | Agent-executed standalone compile check, not a Unity compile |

**Stage 2 — USER-VERIFIED manual results (reported by the user, Unity 6):**
| Step | Result | Source |
|---|---|---|
| TPP camera collision against walls | Works | User-reported manual test |
| Camera recovery after obstruction is removed | Smooth | User-reported manual test |
| FPP/TPP transitions with collision active | Remain smooth | User-reported manual test |
| Looking around near walls | No visible clipping | User-reported manual test |

**Still not manually verified:** clearance below `minimumDistance`, blocked-pivot behaviour, and exact padding distance. These are covered by PlayMode tests that have not been executed.

No automated test results are recorded because the tests have not been run.

## 7. Chronological progress

- **2026-11-24 — Architecture:** inspected clean Unity 6 project; proposed and got approval for component architecture, folder layout, input ownership, renderer restoration, collision safety, FPP offset, and testing strategy.
- **2026-11-24 — Stage 1:** created folder structure, core component, settings, input asset and asmdefs. Standalone compile check passed. Ponytail review found and fixed the pre-`Awake` perspective overwrite; added a Stage 2 collision comment.
- **2026-11-24 — Drift investigation:** analysed the five hypotheses; concluded the pose math is stateless and that a camera-parented CameraTarget causes feedback drift. Added the ancestry guard. Did not reproduce in Unity.
- **2026-11-24 — Manual verification:** user confirmed toggle, drift and guard behaviour in Unity 6. Stage 1 recorded as completed.
- **2026-11-24 — Stage 2 plan review:** collision-safety clarifications recorded in section 3b; pull-in changed to immediate, `pullInSpeed` removed.
- **2026-11-24 — Stage 2 implementation:** created collision helper, collision settings and 8 PlayMode tests; runtime and test compile checks passed with 0 errors. Tests not executed (Unity editor lock); manual Unity verification pending. Log updated.
- **2026-11-24 — Clearance correction:** removed the extra radius subtraction from `CameraCollision.Solve`, added the padding-only clearance test, re-ran both compile checks (0 errors), and confirmed the final-position overlap check remains consistent. Tests still not executed.
- **2026-11-24 — Stage 2 manual verification:** user reported walls, recovery, transitions and near-wall look working in Unity 6. Recorded as user-verified, not automated.
- **2026-11-24 — Stage 2 Ponytail review:** reviewed collision helper, component integration, settings, 9 tests, allocations and Unity 6/Input System compatibility. No critical bug found; optional improvements recorded in section 8. No code changed by the review.
- **2026-11-24 — Test fixture correction:** `CreateCamera()` deactivates the GameObject before adding/configuring `PerspectiveCamera` and activates it afterwards, so `Awake()` sees `initialPerspective`. Compile check passed; PlayMode tests still not executed.

## 8. Stage 2 code review (Ponytail, 2026-11-24)

**Scope reviewed:** `CameraCollision.cs`, `PerspectiveCamera.cs`, `PerspectiveCameraSettings.cs`, all nine PlayMode tests, runtime/test compile checks, and Unity 6 API usage. Load assumed: one camera component per player, one update per frame; no shared-state or multi-process concern.

**Critical issues: none found.** The clearance formula, the pivot/final overlap checks, the hard clamp after smoothing, the `_distanceInitialized` lifecycle and the transition path were all traced line by line. No code was changed by this review.

**Optional improvements (not applied):**
1. `CameraCollisionSettings.collisionMask` defaults to `~0`. With a character collider on the pivot layer the origin `CheckSphere` pins the camera. The API documents that the mask must exclude character colliders, and automatic layer detection was explicitly ruled out, so this stays a setup requirement.
2. No test covers the `_distanceInitialized` lifecycle (reset on target change, preserved across disable/enable). The nine tests cover collision outcomes but not that state contract.
3. `recoverySpeed` allows `0`, which disables recovery entirely. `[Min(0f)]` permits it; a positive minimum would prevent an accidental stuck camera.
4. `CameraCollisionResult` is public but only consumed inside the runtime assembly. It could be `internal` to shrink the public API.
5. The final-position `CheckSphere` runs every frame even on a clear hit. It is deliberate safety, not measured slowness; three physics queries per frame are cheap for one camera.

**Review verdict:** no must-fix issues; ship Stage 2 as implemented. Not checked: PlayMode test execution and automated profiling, both blocked by the open Unity editor.

## 8b. PlayMode test fixture correction (2026-11-24)

**Problem:** `CreateCamera()` in `PerspectiveCameraCollisionTests.cs` added `PerspectiveCamera` to an active GameObject. Unity runs `Awake()` during `AddComponent`, so `initialPerspective` was still the default ThirdPerson when `Awake` set `_perspective` and `_blend`. The later reflection write of FirstPerson was too late; `FirstPersonOffset_IsPreserved` saw a TPP pose roughly 4 units from the expected FPP position.

**Change (test fixture only):** `CreateCamera()` now calls `go.SetActive(false)` before adding components, applies all reflected settings and `CameraTarget` while inactive, then calls `go.SetActive(true)`. Unity defers `Awake()` until activation, so the component initializes from the configured fields. Production runtime files were not touched; the failing assertion and all other test setups are unchanged.

**Verification — verified by the agent:** PlayMode test assembly compile check after the change: **succeeded, 0 errors**, 2 expected CS0649 warnings from runtime serialized fields. `git diff --check` reported no whitespace errors.

**Automated tests: still NOT executed.** The Unity editor holds the project lock; no PlayMode result is claimed. The fix is expected to make the fixture deterministic, but that expectation is unverified in Unity.
