using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Siege
{
    // Game flow: scan -> tap to place the castle around you -> the enemy trebuchet and siege camp set up
    // straight ahead -> castle/town build, trebuchet assembles -> waves of soldiers raid the gate under
    // artillery cover -> victory (all waves beaten) or defeat (gate breached / keep falls) -> play again.
    // You defend from the castle's back wall, like the archer on a Clash Royale princess tower.
    // Works in AR or in a simulated room (editor / no AR).
    public class GameManager : MonoBehaviour
    {
        public enum Phase { Starting, Scanning, PlaceCastle, PlaceCatapult, Intro, Battle, Victory, Defeat }

        public static GameManager Instance { get; private set; }

        [Header("World scale: game units per real metre")]
        public float unitsPerMeter = 12f;         // recomputed at placement from your real phone height
        public float minCatapultDistanceM = 2.2f;
        public float autoCatapultDistanceM = 5f; // enemy is placed straight ahead, clipped by walls (LiDAR)

        [Header("Standing on your tower")]
        public float keepTopUnits = 12.6f;          // height of the keep's battlements in castle units
        public float eyeAboveKeepM = 0.45f;         // phone sits this far above the battlements
        public float keepOffsetUnits = 7f;          // the keep (your tower) sits at the back of the castle

        [Header("AR")]
        public XROrigin xrOrigin;
        public ARSession arSession;
        public ARRaycastManager raycastManager;
        public ARPlaneManager planeManager;
        public ARMeshManager meshManager;

        [Header("Scene")]
        public Camera cam;
        public Transform shakeTarget;
        public Light sun;
        public HUD hud;
        public BowController bow;
        public TouchBowInput bowInput;
        public Transform reticle;
        public Renderer reticleRenderer;

        [Header("Simulation (editor / devices without AR)")]
        public bool forceSimulation;
        public SimCameraRig simRig;
        public GameObject simRoom;

        [Header("Prefabs")]
        public Castle castlePrefab;
        public Trebuchet trebuchetPrefab;
        public GameObject campPrefab;
        public WaveManager waves;

        public Phase State { get; private set; } = Phase.Starting;
        public bool Simulated { get; private set; }
        public bool IsPlacing => State == Phase.Scanning || State == Phase.PlaceCastle || State == Phase.PlaceCatapult;
        public Castle Castle { get; private set; }
        public Trebuchet Trebuchet { get; private set; }

        Pose m_CastlePose, m_CatapultPose;
        GameObject m_Camp;
        Vector3 m_PlayerGround;
        bool m_HasReticle;
        Pose m_ReticlePose;
        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>();

        void Awake()
        {
            Instance = this;
            Physics.gravity = Vector3.down * Tune.Gravity;
            Physics.IgnoreLayerCollision(Layers.Projectile, Layers.Projectile);
            Physics.IgnoreLayerCollision(Layers.Projectile, Layers.Debris);
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Haptics.Init();
            // The keep's battlement height depends on how the castle prefab was built; measure, don't guess.
            if (castlePrefab && castlePrefab.keepTopHeight > 0f) keepTopUnits = castlePrefab.keepTopHeight;
        }

        void OnApplicationFocus(bool focus) { if (focus) Haptics.ConfigureAudio(); }

        IEnumerator Start()
        {
            if (bow) bow.Armed = false;
            hud.Bind(this);
            Simulated = forceSimulation || Application.isEditor;
            if (!Simulated && arSession)
            {
                yield return ARSession.CheckAvailability();
                Debug.Log($"[Siege] AR availability: {ARSession.state}");
                // On a phone, never silently fall back to the simulated room: say what's wrong instead.
                if (ARSession.state == ARSessionState.Unsupported)
                    hud.SetStatus("This device doesn't support ARKit");
            }
            SetupWorld();
            if (Simulated)
            {
                yield return null;
                // Same sizing as on device, for a 1.4 m phone height.
                ApplyScale(keepTopUnits / (1.4f - eyeAboveKeepM));
                m_PlayerGround = Vector3.zero;
                m_CastlePose = new Pose(Vector3.forward * keepOffsetUnits, Quaternion.identity);
                PlaceCatapultAuto();
            }
            else
            {
                Enter(Phase.Scanning);
            }
        }

        void SetupWorld()
        {
            float s = unitsPerMeter;
            cam.nearClipPlane = 0.03f * s;
            cam.farClipPlane = 40f * s;
            if (Simulated)
            {
                if (xrOrigin) xrOrigin.gameObject.SetActive(false);
                if (arSession) arSession.gameObject.SetActive(false);
                if (simRoom) simRoom.SetActive(true);
                if (simRig)
                {
                    simRig.gameObject.SetActive(true);
                    simRig.transform.localScale = Vector3.one * s;
                    cam = simRig.cam.GetComponent<Camera>();
                    cam.nearClipPlane = 0.03f * s;
                    cam.farClipPlane = 40f * s;
                    shakeTarget = simRig.cam.parent;
                }
            }
            else
            {
                if (simRoom) simRoom.SetActive(false);
                if (simRig) simRig.gameObject.SetActive(false);
                xrOrigin.transform.localScale = Vector3.one * s;
            }
            ApplyScale(s);
            bow.cam = cam.transform;
            bow.bowRoot.SetParent(cam.transform, false);
            hud.cam = cam;
            var fx = FindAnyObjectByType<ScreenFX>();
            if (fx && shakeTarget) fx.shakeTarget = shakeTarget;
        }

        void ApplyScale(float upm)
        {
            unitsPerMeter = upm;
            if (Simulated) { if (simRig) simRig.transform.localScale = Vector3.one * upm; }
            else if (xrOrigin) xrOrigin.transform.localScale = Vector3.one * upm;
            cam.nearClipPlane = 0.03f * upm;
            cam.farClipPlane = 40f * upm;
            Physics.SyncTransforms();
        }

        void Enter(Phase p)
        {
            State = p;
            hud.OnPhase(p);
            if (bowInput) bowInput.enabledForInput = p == Phase.Battle || p == Phase.Intro;
            if (bow) bow.Armed = p == Phase.Battle;
            if (planeManager) planeManager.enabled = IsPlacing || p == Phase.Intro || p == Phase.Battle;
        }

        // ---------------------------------------------------------------- placement
        void Update()
        {
            if (Simulated) return;
            switch (State)
            {
                case Phase.Scanning:
                    if (planeManager && planeManager.trackables.count > 0) Enter(Phase.PlaceCastle);
                    else hud.SetStatus(ARSession.state == ARSessionState.SessionTracking ? "" : $"AR: {ARSession.state} {ARSession.notTrackingReason}");
                    break;
                case Phase.PlaceCastle:
                    UpdateReticle();
                    if (TappedWorld() && m_HasReticle)
                    {
                        Haptics.UI();
                        Sfx.Play2D(Sound.UiTick);
                        PlaceAroundPlayer(m_ReticlePose.position.y);
                    }
                    break;
            }
            if (reticle) reticle.gameObject.SetActive(State == Phase.PlaceCastle && m_HasReticle);
        }

        // Finds the floor (the plane under the crosshair) and shows the castle footprint around your feet.
        void UpdateReticle()
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            m_HasReticle = raycastManager && raycastManager.Raycast(center, m_Hits, TrackableType.PlaneWithinPolygon);
            if (!m_HasReticle) return;
            float floorY = m_Hits[0].pose.position.y;
            var c = cam.transform.position;
            var flatFwd = cam.transform.forward; flatFwd.y = 0;
            m_ReticlePose = new Pose(new Vector3(c.x, floorY, c.z), Quaternion.LookRotation(flatFwd.sqrMagnitude > 0.001f ? flatFwd : Vector3.forward));
            float newUpm = ScaleFor(c.y, floorY);
            var footprintCenter = m_ReticlePose.position + m_ReticlePose.forward * keepOffsetUnits * unitsPerMeter / newUpm;
            reticle.SetPositionAndRotation(footprintCenter + Vector3.up * 0.05f * unitsPerMeter, m_ReticlePose.rotation);
            reticle.localScale = Vector3.one * 28f * (unitsPerMeter / newUpm) * (1f + 0.03f * Mathf.Sin(Time.time * 5f));
            if (reticleRenderer)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Tint", new Color(1.6f, 1.3f, 0.6f, 1));
                reticleRenderer.SetPropertyBlock(mpb);
            }
            float h = (c.y - floorY) / unitsPerMeter;
            hud.SetPlacementWarning(h < 0.7f ? "Stand up and hold the phone at chest height" : null);
        }

        // World scale that puts the keep's battlements just below the phone.
        float ScaleFor(float camY, float floorY)
        {
            float heightM = Mathf.Clamp((camY - floorY) / unitsPerMeter, 0.7f, 2.2f);
            return Mathf.Clamp(keepTopUnits / Mathf.Max(0.3f, heightM - eyeAboveKeepM), 7f, 24f);
        }

        // Builds the castle around the player: you stand on the keep, the castle faces where you look.
        void PlaceAroundPlayer(float floorY)
        {
            var origin = xrOrigin.transform;
            var c = cam.transform.position;
            var groundLocal = origin.InverseTransformPoint(new Vector3(c.x, floorY, c.z));
            ApplyScale(ScaleFor(c.y, floorY));
            m_PlayerGround = origin.TransformPoint(groundLocal);
            var fwd = cam.transform.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            m_CastlePose = new Pose(m_PlayerGround + fwd * keepOffsetUnits, Quaternion.LookRotation(fwd));
            PlaceCatapultAuto();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        bool TappedWorld()
        {
            if (Input.touchCount == 0) return false;
            var t = Input.GetTouch(0);
            if (t.phase != TouchPhase.Began) return false;
            var es = UnityEngine.EventSystems.EventSystem.current;
            return !(es && es.IsPointerOverGameObject(t.fingerId));
        }

        // The enemy sets up straight ahead of where you're looking, on the real floor, short of any wall.
        public void PlaceCatapultAuto()
        {
            var fwd = Simulated ? Vector3.forward : cam.transform.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            float m = unitsPerMeter;
            float dist = autoCatapultDistanceM * m;
            int env = 1 << Layers.Environment;
            var eye = m_CastlePose.position + Vector3.up * 0.3f * m;
            if (!Simulated && Physics.Raycast(eye, fwd, out var wall, dist + 0.6f * m, env))
                dist = Mathf.Max(minCatapultDistanceM * m, wall.distance - 0.6f * m);
            var pos = m_CastlePose.position + fwd * dist;
            if (!Simulated && Physics.Raycast(pos + Vector3.up * 1.5f * m, Vector3.down, out var ground, 3f * m, env))
                pos = ground.point;
            PlaceCatapult(new Pose(pos, Quaternion.identity));
        }

        void PlaceCatapult(Pose pose)
        {
            m_CatapultPose = pose;
            StartCoroutine(BeginRound());
        }

        public void Replace()
        {
            if (State == Phase.Intro) return;
            StopAllCoroutines();
            Cleanup();
            Time.timeScale = 1f;
            if (Simulated) { StartCoroutine(BeginRound()); return; }
            ApplyScale(unitsPerMeter);
            Enter(Phase.PlaceCastle);
        }

        public void PlayAgain()
        {
            if (State == Phase.Intro) return;
            StopAllCoroutines();
            Cleanup();
            Time.timeScale = 1f;
            StartCoroutine(BeginRound());
        }

        void Cleanup()
        {
            if (Castle) Destroy(Castle.gameObject);
            if (waves) waves.Stop();
            if (Trebuchet) Destroy(Trebuchet.gameObject);
            if (m_Camp) Destroy(m_Camp);
            foreach (var s in FindObjectsByType<Soldier>(FindObjectsInactive.Include)) Destroy(s.gameObject);
            foreach (var d in FindObjectsByType<Debris>(FindObjectsInactive.Exclude)) Destroy(d.gameObject);
            foreach (var a in FindObjectsByType<Arrow>(FindObjectsInactive.Exclude)) Destroy(a.gameObject);
            foreach (var f in FindObjectsByType<Fireball>(FindObjectsInactive.Exclude)) Destroy(f.gameObject);
            foreach (var m in FindObjectsByType<LandingMarker>(FindObjectsInactive.Exclude)) Destroy(m.gameObject);
            foreach (var go in GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (go && go.parent == null && (go.name == "Scorch" || go.name.StartsWith("Debris") || go.name.StartsWith("Ladder") || go.name.StartsWith("Ram"))) Destroy(go.gameObject);
        }

        // ---------------------------------------------------------------- round
        IEnumerator BeginRound()
        {
            Enter(Phase.Intro);
            // Castle gate faces the enemy; the trebuchet faces the castle; the siege camp sits off its flank.
            var toCat = Flat(m_CatapultPose.position - m_CastlePose.position);
            var castleRot = Quaternion.LookRotation(toCat.sqrMagnitude > 0.01f ? toCat.normalized : Vector3.forward);
            Castle = Instantiate(castlePrefab, m_CastlePose.position, castleRot);
            var trebRot = Quaternion.LookRotation(-(castleRot * Vector3.forward));
            Trebuchet = Instantiate(trebuchetPrefab, m_CatapultPose.position, trebRot);
            if (Trebuchet.knight) Trebuchet.knight.lookTarget = Castle.keep ? Castle.keep.transform : Castle.transform;
            if (campPrefab)
            {
                var campPos = m_CatapultPose.position + (trebRot * Vector3.right) * 15f + (trebRot * Vector3.forward) * 4f;
                if (Physics.Raycast(campPos + Vector3.up * 30f, Vector3.down, out var g, 80f, 1 << Layers.Environment)) campPos.y = g.point.y;
                m_Camp = Instantiate(campPrefab, campPos, trebRot);
            }
            Castle.KeepDestroyed += OnDefeat;
            Trebuchet.Destroyed += OnTrebuchetDestroyed;
            Trebuchet.Hit += hud.OnCatapultHit;
            Castle.PieceDestroyed += _ => { hud.OnCastleDamaged(); if (Trebuchet && Trebuchet.knight) Trebuchet.knight.Cheer(); };
            hud.SetTargets(Castle, Trebuchet);

            if (Simulated && simRig)
            {
                // Player stands on the keep, phone just above the battlements.
                simRig.transform.position = m_PlayerGround + Vector3.up * (keepTopUnits + eyeAboveKeepM * unitsPerMeter);
                simRig.cam.localPosition = Vector3.zero;
                simRig.LookAt(Vector3.Lerp(m_CastlePose.position, m_CatapultPose.position, 0.75f) + Vector3.up * 2f);
            }

            var castleIntro = StartCoroutine(Castle.BuildIntro());
            yield return new WaitForSeconds(0.5f);
            yield return Trebuchet.RollIn(30f, 2.6f);
            yield return castleIntro;
            Sfx.Play2D(Sound.WarHorn, 0.9f);
            Haptics.CastleImpact(0.4f);
            hud.Banner("DEFEND!", "", new Color(1f, 0.82f, 0.35f), 1.4f);
            yield return new WaitForSeconds(1.2f);
            Enter(Phase.Battle);
            Trebuchet.Begin(Castle, cam.transform);
            if (waves)
            {
                waves.WaveStarted -= OnWaveStarted; waves.WaveStarted += OnWaveStarted;
                waves.GateBreached -= OnGateBreached; waves.GateBreached += OnGateBreached;
                waves.AllWavesCleared -= OnVictory; waves.AllWavesCleared += OnVictory;
                waves.Begin(Castle, m_Camp ? m_Camp.transform : Trebuchet.transform);
            }
        }

        void OnWaveStarted(int wave)
        {
            if (State != Phase.Battle) return;
            Sfx.Play2D(Sound.WarHorn, 0.7f);
            hud.Banner($"WAVE {wave}", "", new Color(1f, 0.55f, 0.35f), 1.2f);
        }

        void OnGateBreached()
        {
            if (State != Phase.Battle) return;
            hud.Banner("THE GATE HAS FALLEN", "", new Color(1f, 0.4f, 0.3f), 1.5f);
            OnDefeat();
        }

        // Destroying the trebuchet silences the bombardment but the raid goes on.
        void OnTrebuchetDestroyed()
        {
            if (State != Phase.Battle) return;
            hud.Banner("TREBUCHET DESTROYED", "", new Color(1f, 0.85f, 0.3f), 1.6f);
            Haptics.Victory();
        }

        void OnVictory()
        {
            if (State != Phase.Battle) return;
            Enter(Phase.Victory);
            if (Trebuchet) Trebuchet.Stop();
            StartCoroutine(VictoryRoutine());
        }

        IEnumerator VictoryRoutine()
        {
            yield return new WaitForSecondsRealtime(0.8f);
            Sfx.Play2D(Sound.Victory, 1f);
            Haptics.Victory();
            hud.Banner("VICTORY!", "", new Color(1f, 0.85f, 0.3f), 0f);
            var colors = new[] { new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.6f, 1f), new Color(1f, 1f, 1f), new Color(1f, 0.35f, 0.3f) };
            for (int i = 0; i < 9 && State == Phase.Victory; i++)
            {
                var p = Castle.Center + new Vector3(Random.Range(-10f, 10f), Random.Range(14f, 24f), Random.Range(-10f, 10f));
                VFX.Firework(p, colors[i % colors.Length], 1.2f);
                Sfx.Play(Sound.Explosion, p, 0.25f, 0.3f);
                Haptics.PieceLand(0.5f);
                yield return new WaitForSeconds(Random.Range(0.3f, 0.6f));
            }
        }

        void OnDefeat()
        {
            if (State != Phase.Battle) return;
            Enter(Phase.Defeat);
            StartCoroutine(DefeatRoutine());
        }

        IEnumerator DefeatRoutine()
        {
            if (Trebuchet) Trebuchet.Stop();
            ScreenFX.SlowMo(0.3f, 1.4f);
            ScreenFX.Damage(1f);
            Haptics.Collapse();
            yield return Castle.CollapseAll();
            if (Trebuchet && Trebuchet.knight) Trebuchet.knight.Cheer();
            Sfx.Play2D(Sound.Defeat, 0.9f);
            hud.Banner("DEFEAT", "", new Color(1f, 0.4f, 0.3f), 0f);
        }
    }
}
