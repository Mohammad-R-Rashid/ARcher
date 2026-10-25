using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Enemy counterweight trebuchet. The crew winch the arm down on the windlass, a fireball is laid in the
    // sling pouch and, on the knight's order, the counterweight drops. The swing is simulated: a rigid arm
    // driven by torque from a hinged counterweight and the sling pouch (point masses on ropes, solved as
    // position-based constraints), so the pouch whips round and lets go on a ballistic arc to the target,
    // the arm is checked by its buffer rope past vertical and rocks to rest. Arrow hits splinter wood; damage
    // escalates through smoke and fire to a full break-up.
    public class Trebuchet : MonoBehaviour, IDamageable
    {
        [Header("Rig")]
        public Transform body;          // parent of the frame and moving parts; recoils, wobbles, unfolds in the intro
        public Transform arm;           // pivots on the axle about its local X
        public Transform counterweight; // pivots on the hinge at the short arm's end
        public Transform windlass;
        public Transform slingTip, pouch;
        public Transform ropeWinch, ropeArm;
        public Transform banner;
        public LineRenderer sling, rope;
        public MeshFilter frame;
        public Knight knight;
        public Soldier[] crew;
        public TNTBarrel[] barrels;
        public Material debrisMaterial;

        [Header("Prefabs")]
        public Fireball fireballPrefab;
        public GameObject landingMarkerPrefab;

        [Header("Pose (degrees, + tips the long arm down at the back)")]
        public float loadedAngle = 40f;
        public float firedAngle = -90f;

        [Header("Engine")]
        public float reloadTime = Tune.CatapultReload * 2f;   // with the full crew hauling
        public float slingLength = 4.5f;    // metres, arm tip to pouch
        public float slingLay = 3.2f;       // metres the loaded pouch lies ahead of the tip, in the trough
        public float releaseAngle = 45f;    // pouch climb angle at which the fireball lets go
        public float fireballScale = 0.65f;

        public float Health { get; private set; }
        public float MaxHealth => Tune.CatapultHealth;
        public bool Alive => Health > 0f;
        public Vector3 AimCenter => m_Aim ? m_Aim.Point : Core;

        public event Action<float, Vector3, bool> Hit;   // damage, point, isKnight
        public event Action Destroyed;

        // Swing model (kg, rad/s). Tuned offline: release ~1.5 s after the trigger at ~30 m/s and 45 deg, the
        // buffer rope checks the arm at ~-102 deg a second later, then it rocks onto -90 within ~2 s.
        const float MassCounterweight = 10500f, MassArm = 1000f, MassBall = 450f, MassPouch = 20f;
        const float ArmInertiaShape = 0.45f;   // tapered beam: most of its mass near the axle
        const float ArmComFraction = 0.24f;    // arm's centre of mass, fraction of the way out to the tip
        const float AxleFriction = 0.15f, FollowThroughDamping = 0.8f;
        const float CatchAngle = 12f, CatchRebound = 0.35f;   // buffer rope: degrees past fired, bounce kept
        const float HingeDamping = 1.5f, HingeDampingSpent = 3f, PouchDrag = 0.5f, TroughFriction = 3f;
        const float ServoStiffness = 160f;     // winch/trigger holding the arm when it isn't swinging free
        const float SubStep = 1f / 600f;
        const float FxSize = 3.2f;             // effect scale at life size (the old catapult's)
        const float DrumRadius = 0.45f;
        const float RopeSlack = 1f, RopeReel = 1.5f, HookTime = 1f, DropTime = 0.35f;   // m, m/s, s, s
        const float PouchDrawSpeed = 2.5f;     // m/s a hand drags the sling down the trough
        const float RunSpeed = 6f, KnightWalkIn = 5f, KnightPace = 2.2f;   // m/s, m, m/s
        const float IntroFold = 0.01f;
        const int SlingSegments = 6, RopeSegments = 8;
        static readonly Vector3 FireballSeat = new Vector3(0f, 0.32f, 0f);
        static readonly Vector3 PouchRing = new Vector3(0f, 0.195f, 0.53f);   // sling rings at the pouch's ends

        Castle m_Castle;
        Transform m_Camera;
        Fireball m_Loaded;
        Collider[] m_Colliders;
        Renderer m_FrameRenderer;
        AimTarget m_Aim;
        Coroutine m_Loop, m_Cheer, m_KnightWalk;
        int m_Shots;
        Vector3 m_KickPos, m_BodyPos, m_BodyScale = Vector3.one;
        Quaternion m_BodyRot = Quaternion.identity;
        float m_Wobble, m_Unfold = 1f;
        GameObject m_Smoke, m_Fire;
        bool m_Broken;

        // Arm: angle in radians (+ = long end down), swinging free or held by the servo at m_ArmTarget.
        float m_Theta, m_Omega, m_ArmTarget;
        bool m_Free;
        float m_ArmSign = 1f;
        Quaternion m_ArmRest = Quaternion.identity;
        Vector3 m_TipLocal, m_HingeLocal;
        Vector3 m_Pivot, m_Axis, m_R0Tip, m_R0Hinge, m_R0Com;   // world geometry at angle 0, per frame
        float m_Inertia = 1f;

        // Counterweight and pouch: point masses hanging from the hinge and the tip (world space).
        Vector3 m_CwPos, m_CwVel, m_CwDir = Vector3.down;
        Quaternion m_CwRest = Quaternion.identity;
        float m_CwLen = 2.3f;
        Vector3 m_PouchPos, m_PouchVel, m_PouchLie;
        float m_PouchFloor = 0.6f, m_PouchPin, m_PouchDraw;
        float m_LastScale;

        // Shot in flight
        Vector3 m_ShotTarget, m_ReleaseVel;
        CastlePiece m_ShotPiece;
        bool m_ShotNearMiss, m_ShotPending, m_Released, m_ReleaseNow, m_Caught, m_SlamNow, m_Whooshed;
        float m_SwingTime;

        // Windlass rope (model metres paid out). Its loose end lies in the trough until it is hooked onto the
        // arm for winching, and drops off again at the trigger.
        Quaternion m_WindlassRest = Quaternion.identity;
        float m_RopeLen = -1f, m_Slack, m_Drum, m_RopeHook;
        Vector3 m_RopeDrop;
        bool m_RopeOn, m_Winching;

        // Intro (crew/knight posts in root-local space)
        bool m_Introduced;
        float m_IntroTime = 2.6f, m_IntroRun = 18f;
        Vector3[] m_CrewHome = new Vector3[0];
        Quaternion[] m_CrewHomeRot = new Quaternion[0];
        Vector3 m_KnightHome, m_CrankSpot;
        Quaternion m_KnightHomeRot = Quaternion.identity, m_CrankRot = Quaternion.identity;
        int m_CrewCount;
        Transform[] m_Props = new Transform[0];
        Vector3[] m_PropScale = new Vector3[0];

        float Scale => transform.lossyScale.y;
        float Fx => FxSize * transform.lossyScale.x;
        Transform Base => body ? body : transform;
        float ArmDeg => m_Theta * Mathf.Rad2Deg;
        Vector3 Core => m_FrameRenderer ? m_FrameRenderer.bounds.center : transform.position;

        void Awake()
        {
            Health = MaxHealth;
            m_Colliders = GetComponentsInChildren<Collider>(true);
            m_FrameRenderer = frame ? frame.GetComponent<Renderer>() : null;
            if (barrels != null) foreach (var b in barrels) if (b) b.owner = this;
            if (knight) knight.owner = this;
            if (body) { m_BodyPos = body.localPosition; m_BodyRot = body.localRotation; m_BodyScale = body.localScale; }

            m_Aim = Base.gameObject.AddComponent<AimTarget>();
            m_Aim.offset = SolidPoint();

            CaptureRig();
            m_Theta = m_ArmTarget = firedAngle * Mathf.Deg2Rad;
            CaptureParty();
            // Folded flat until RollIn raises it (never sunk below the floor: AR floors don't hide what's under them).
            m_Unfold = IntroFold;
            ShowProps(0f);
            PoseBody();
            ResetSim();
        }

        // After every Awake (the crew's included) so the run-in start isn't captured as anyone's facing.
        void Start()
        {
            if (!m_Introduced) PlaceParty(0f);   // waiting back at camp until the intro calls them in
        }

        void CaptureRig()
        {
            float s = Mathf.Max(0.0001f, Scale);
            if (ropeWinch && windlass && ropeWinch.IsChildOf(windlass)) ropeWinch.SetParent(windlass.parent, true);
            if (windlass) m_WindlassRest = windlass.localRotation;
            if (pouch) m_PouchFloor = Base.InverseTransformPoint(pouch.position).y;
            if (sling) { sling.useWorldSpace = true; sling.positionCount = SlingSegments * 2 + 1; }
            if (rope) { rope.useWorldSpace = true; rope.positionCount = RopeSegments + 1; }

            if (counterweight)
            {
                m_CwRest = Quaternion.Inverse(transform.rotation) * counterweight.rotation;
                var mf = counterweight.GetComponent<MeshFilter>();
                var com = mf && mf.sharedMesh ? counterweight.TransformPoint(mf.sharedMesh.bounds.center) : counterweight.position - transform.up * 2.3f * s;
                var hang = com - counterweight.position;
                m_CwLen = Mathf.Max(0.5f, hang.magnitude / s);
                m_CwDir = hang.sqrMagnitude > 1e-6f ? transform.InverseTransformDirection(hang.normalized) : Vector3.down;
            }

            if (!arm) return;
            if (slingTip && !slingTip.IsChildOf(arm)) slingTip.SetParent(arm, true);
            if (ropeArm && !ropeArm.IsChildOf(arm)) ropeArm.SetParent(arm, true);
            if (pouch && pouch.IsChildOf(arm)) pouch.SetParent(Base, true);
            m_ArmRest = arm.localRotation;
            m_TipLocal = slingTip ? arm.InverseTransformPoint(slingTip.position) : new Vector3(0f, 0f, -12.5f);
            m_HingeLocal = counterweight ? arm.InverseTransformPoint(counterweight.position) : new Vector3(0f, 0f, 3.2f);
            m_ArmSign = 1f;
            UpdateArmFrame();
            // + must swing the long end down, whatever axis convention the model came in with.
            if (Vector3.Dot(Vector3.Cross(m_Axis, m_R0Tip), transform.up) > 0f) { m_ArmSign = -1f; UpdateArmFrame(); }

            float loaded = loadedAngle * Mathf.Deg2Rad;
            var tip = Base.InverseTransformPoint(ArmPoint(m_R0Tip, loaded));
            m_PouchLie = new Vector3(tip.x, m_PouchFloor, tip.z + slingLay);
            // the loose rope end rests on the ground under where the hook sits when the arm is loaded
            if (ropeArm) m_RopeDrop = Base.InverseTransformPoint(ArmPoint(ropeArm.position - m_Pivot, loaded));
            m_RopeDrop.y = 0.05f;
        }

        void CaptureParty()
        {
            int n = crew != null ? crew.Length : 0;
            m_CrewHome = new Vector3[n];
            m_CrewHomeRot = new Quaternion[n];
            var drum = windlass ? windlass.position : transform.position;
            var wmf = windlass ? windlass.GetComponent<MeshFilter>() : null;
            var handle = wmf && wmf.sharedMesh ? windlass.TransformVector(Vector3.right * wmf.sharedMesh.bounds.extents.x) : Vector3.zero;
            if (knight) { m_KnightHome = transform.InverseTransformPoint(knight.transform.position); m_KnightHomeRot = Quaternion.Inverse(transform.rotation) * knight.transform.rotation; }
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!crew[i]) continue;
                var c = crew[i].transform;
                m_CrewHome[i] = transform.InverseTransformPoint(c.position);
                m_CrewHomeRot[i] = Quaternion.Inverse(transform.rotation) * c.rotation;
                m_CrewCount++;
                // The knight's post if he has to crank the windlass alone: the hand's spot nearest a handle at
                // the drum's ends (on his own side when it's a tie).
                float d = Mathf.Min(Vector3.Distance(c.position, drum + handle), Vector3.Distance(c.position, drum - handle))
                        + 0.1f * Vector3.Distance(m_CrewHome[i], m_KnightHome);
                if (d >= best) continue;
                best = d;
                m_CrankSpot = m_CrewHome[i];
                var face = transform.InverseTransformDirection(drum - c.position);
                face.y = 0f;
                m_CrankRot = face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : m_CrewHomeRot[i];
            }
            if (best == float.MaxValue) { m_CrankSpot = m_KnightHome; m_CrankRot = m_KnightHomeRot; }

            // Powder kegs and spare shot (the root's other visible children) are set down with the last lift.
            var props = new List<Transform>();
            if (body)
                foreach (Transform c in transform)
                {
                    if (c == body || (knight && c == knight.transform) || (sling && c == sling.transform) || (rope && c == rope.transform)) continue;
                    if (crew != null && Array.Exists(crew, x => x && x.transform == c)) continue;
                    if (c.GetComponentInChildren<Renderer>(true)) props.Add(c);
                }
            m_Props = props.ToArray();
            m_PropScale = new Vector3[m_Props.Length];
            for (int i = 0; i < m_Props.Length; i++) m_PropScale[i] = m_Props[i].localScale;
        }

        // Aim-assist point on solid timber the player sees head-on: just behind the front face of one tower's
        // leg, a little over half way up to the axle. (The frame's bounding-box centre is empty air between the
        // towers.) Rays from the front against the frame mesh, in Base space.
        Vector3 SolidPoint()
        {
            var axle = arm ? Base.InverseTransformPoint(arm.position) : new Vector3(0f, 9f, 0f);
            float y = axle.y * 0.55f;
            var fallback = new Vector3(0f, y, axle.z);
            var mesh = frame ? frame.sharedMesh : null;
            if (!mesh || !mesh.isReadable) return fallback;
            var toBase = Base.worldToLocalMatrix * frame.transform.localToWorldMatrix;
            var v = mesh.vertices;
            var tri = mesh.triangles;
            float front = float.MinValue, half = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = toBase.MultiplyPoint3x4(v[i]);
                front = Mathf.Max(front, v[i].z);
                half = Mathf.Max(half, Mathf.Abs(v[i].x));
            }
            var band = new List<int>();
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                float a = v[tri[i]].y, b = v[tri[i + 1]].y, c = v[tri[i + 2]].y;
                if (Mathf.Min(a, Mathf.Min(b, c)) <= y && Mathf.Max(a, Mathf.Max(b, c)) >= y) band.Add(i);
            }
            // Walk outward on the side away from the knight; the first run of hits a beam wide is the tower leg.
            float side = knight && Base.InverseTransformPoint(knight.transform.position).x > 0f ? -1f : 1f;
            const float Step = 0.1f;
            float run = -1f;
            for (float x = 0f; x <= half + Step; x += Step)
            {
                if (x <= half && FrontHit(v, tri, band, new Vector3(side * x, y, front + 1f), out _))
                {
                    if (run < 0f) run = x;
                    continue;
                }
                if (run >= 0f && x - run >= 0.3f && FrontHit(v, tri, band, new Vector3(side * (run + x - Step) * 0.5f, y, front + 1f), out var p))
                    return p + Vector3.back * 0.2f;
                run = -1f;
            }
            return fallback;
        }

        static bool FrontHit(Vector3[] v, int[] tri, List<int> band, Vector3 origin, out Vector3 point)
        {
            float best = float.MaxValue;
            foreach (int i in band)
                if (RayTriangle(origin, Vector3.back, v[tri[i]], v[tri[i + 1]], v[tri[i + 2]], out float t) && t < best) best = t;
            point = origin + Vector3.back * best;
            return best < float.MaxValue;
        }

        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0f || u + w > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        public void Begin(Castle castle, Transform cam)
        {
            if (m_Castle) m_Castle.PieceDestroyed -= OnPieceDestroyed;
            m_Castle = castle;
            m_Camera = cam;
            if (!m_Introduced) FinishIntro();
            if (m_Castle) m_Castle.PieceDestroyed += OnPieceDestroyed;
            if (m_Loop != null) StopCoroutine(m_Loop);
            m_Loop = StartCoroutine(SiegeLoop());
        }

        public void Stop()
        {
            if (m_Loop != null) StopCoroutine(m_Loop);
            if (m_Cheer != null) StopCoroutine(m_Cheer);
            if (m_KnightWalk != null) StopCoroutine(m_KnightWalk);
            m_Loop = m_Cheer = m_KnightWalk = null;
            m_Winching = false;
            m_PouchDraw = 0f;
            // A shot already swinging is called off: the fireball fizzles in the sling instead of flying.
            m_ShotPending = m_ReleaseNow = false;
            if (m_Loaded && m_Free)
            {
                VFX.Dust(m_Loaded.transform.position, 0.5f * Fx);
                Destroy(m_Loaded.gameObject);
                m_Loaded = null;
            }
            if (m_Castle) m_Castle.PieceDestroyed -= OnPieceDestroyed;
            if (knight) knight.SetState(Knight.State.Idle);
            SetCrew(Soldier.State.Idle);
        }

        void OnDestroy()
        {
            if (m_Castle) m_Castle.PieceDestroyed -= OnPieceDestroyed;
        }

        void LateUpdate()
        {
            if (m_Broken) return;
            float dt = Time.deltaTime;
            // Recoil / hit wobble on the whole engine
            m_KickPos = MathExt.Damp(m_KickPos, Vector3.zero, 5f, dt);
            m_Wobble = MathExt.Damp(m_Wobble, 0f, 4f, dt);
            m_RopeHook = Mathf.MoveTowards(m_RopeHook, m_RopeOn ? 1f : 0f, dt / (m_RopeOn ? HookTime : DropTime));
            PoseBody();
            if (!arm) return;
            if (!m_Introduced || Mathf.Abs(Scale - m_LastScale) > 1e-4f) ResetSim();   // kinematic until it's up
            else if (dt > 0f) Simulate(dt);
            ApplyRig(dt);
            DrawLines();

            if (m_ReleaseNow) { m_ReleaseNow = false; Release(m_ShotTarget, m_ShotPiece, m_ShotNearMiss); }
            if (m_SlamNow) { m_SlamNow = false; Slam(); }
            if (m_Free && !m_Whooshed && ArmDeg < 0f)
            {
                m_Whooshed = true;
                var src = Sfx.Play(Sound.ArrowWhoosh, pouch ? pouch.position : transform.position, 1f, 0.05f);
                if (src) src.pitch *= 0.45f;   // a heavy sling, not an arrow
            }
        }

        void PoseBody()
        {
            if (!body) return;
            float t = Time.time;
            body.localPosition = m_BodyPos + m_KickPos;
            body.localRotation = m_BodyRot * Quaternion.Euler(Mathf.Sin(t * 24f) * m_Wobble * 0.5f + m_KickPos.z * 3f, 0f, Mathf.Cos(t * 19f) * m_Wobble * 0.3f);
            body.localScale = new Vector3(m_BodyScale.x, m_BodyScale.y * m_Unfold, m_BodyScale.z);
        }

        // ---------------------------------------------------------------- swing simulation
        Vector3 ArmPoint(Vector3 r0, float theta) => m_Pivot + Quaternion.AngleAxis(theta * Mathf.Rad2Deg, m_Axis) * r0;

        void UpdateArmFrame()
        {
            var rest = Matrix4x4.TRS(arm.localPosition, m_ArmRest, arm.localScale);
            if (arm.parent) rest = arm.parent.localToWorldMatrix * rest;
            m_Pivot = rest.MultiplyPoint3x4(Vector3.zero);
            m_Axis = rest.MultiplyVector(Vector3.right).normalized * m_ArmSign;
            m_R0Tip = rest.MultiplyPoint3x4(m_TipLocal) - m_Pivot;
            m_R0Hinge = rest.MultiplyPoint3x4(m_HingeLocal) - m_Pivot;
            m_R0Com = m_R0Tip * ArmComFraction;
            float a = m_R0Hinge.magnitude, b = m_R0Tip.magnitude;
            m_Inertia = Mathf.Max(1f, MassArm * (a * a + a * b + b * b) / 3f * ArmInertiaShape);
        }

        // Counterweight and pouch hang at rest (intro, rescale).
        void ResetSim()
        {
            m_LastScale = Scale;
            if (!arm) return;
            UpdateArmFrame();
            float s = Scale;
            m_CwPos = ArmPoint(m_R0Hinge, m_Theta) + transform.TransformDirection(m_CwDir) * m_CwLen * s;
            m_CwVel = Vector3.zero;
            m_PouchPos = m_PouchPin > 0f ? Base.TransformPoint(m_PouchLie) : ArmPoint(m_R0Tip, m_Theta) + Vector3.down * slingLength * s * 0.98f;
            var lp = Base.InverseTransformPoint(m_PouchPos);
            if (lp.y < m_PouchFloor) { lp.y = m_PouchFloor; m_PouchPos = Base.TransformPoint(lp); }
            m_PouchVel = Vector3.zero;
        }

        void Simulate(float dt)
        {
            // The frame itself moves (wobble, recoil, turning to aim): spread that over the sub-steps too.
            Vector3 p0 = m_Pivot, a0 = m_Axis, t0 = m_R0Tip, h0 = m_R0Hinge;
            UpdateArmFrame();
            Vector3 p1 = m_Pivot, a1 = m_Axis, t1 = m_R0Tip, h1 = m_R0Hinge;
            void Blend(float a)
            {
                m_Pivot = Vector3.Lerp(p0, p1, a);
                m_Axis = Vector3.Slerp(a0, a1, a).normalized;
                m_R0Tip = Vector3.Lerp(t0, t1, a);
                m_R0Hinge = Vector3.Lerp(h0, h1, a);
                m_R0Com = m_R0Tip * ArmComFraction;
            }
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / SubStep), 1, 32);
            float h = dt / n;
            float s = Scale;
            var g = Physics.gravity;
            float cwLen = m_CwLen * s, slingLen = slingLength * s;
            float caught = (firedAngle - CatchAngle) * Mathf.Deg2Rad;
            bool pinned = m_PouchPin >= 1f;
            float friction = Mathf.Exp(-TroughFriction * h);
            float hinge = Mathf.Exp(-(m_Released ? HingeDampingSpent : HingeDamping) * h);
            float draw = 1f - Mathf.Exp(-10f * m_PouchDraw * h);
            var lie = Base.TransformPoint(m_PouchLie);
            for (int i = 0; i < n; i++)
            {
                float prevTheta = m_Theta;
                Blend((float)i / n);
                var hingeBefore = ArmPoint(m_R0Hinge, m_Theta);
                Blend((i + 1f) / n);
                if (m_Free)
                {
                    // the arm's own weight, axle friction, and the rope brake once the shot is away
                    var rc = Quaternion.AngleAxis(m_Theta * Mathf.Rad2Deg, m_Axis) * m_R0Com;
                    m_Omega += h * Vector3.Dot(g * MassArm, Vector3.Cross(m_Axis, rc)) / m_Inertia;
                    m_Omega *= Mathf.Exp(-(AxleFriction + (m_Released ? FollowThroughDamping : 0f)) * h);
                }
                else
                {
                    m_Omega += h * (ServoStiffness * (m_ArmTarget - m_Theta) - 2f * Mathf.Sqrt(ServoStiffness) * m_Omega);
                }
                m_Theta += h * m_Omega;

                var hingeVel = (ArmPoint(m_R0Hinge, m_Theta) - hingeBefore) / h;
                m_CwVel += g * h;
                m_CwVel = hingeVel + (m_CwVel - hingeVel) * hinge;
                var cwPrev = m_CwPos;
                m_CwPos += m_CwVel * h;
                var pouchPrev = m_PouchPos;
                if (!pinned)
                {
                    m_PouchVel += g * h;
                    if (m_Released) m_PouchVel *= Mathf.Exp(-PouchDrag * h);
                    if (m_PouchDraw > 0f)
                    {
                        // a hand drags the sling along the trough to where it's laid
                        var want = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(lie - m_PouchPos, Base.up) * 2f, PouchDrawSpeed * s);
                        m_PouchVel += (want - Vector3.ProjectOnPlane(m_PouchVel, Base.up)) * draw;
                    }
                    m_PouchPos += m_PouchVel * h;
                }

                // Rope constraints: the counterweight strap is rigid, the sling only pulls.
                Constrain(ref m_CwPos, 1f / MassCounterweight, m_R0Hinge, cwLen, true);
                bool grounded = false;
                if (!pinned)
                {
                    bool laden = m_Loaded && !m_Released;
                    Constrain(ref m_PouchPos, 1f / (laden ? MassPouch + MassBall : MassPouch), m_R0Tip, slingLen, false);
                    var lp = Base.InverseTransformPoint(m_PouchPos);
                    if (lp.y < m_PouchFloor) { lp.y = m_PouchFloor; m_PouchPos = Base.TransformPoint(lp); grounded = true; }
                }

                m_CwVel = (m_CwPos - cwPrev) / h;
                if (!pinned)
                {
                    m_PouchVel = (m_PouchPos - pouchPrev) / h;
                    if (grounded && m_PouchDraw <= 0f) { m_PouchVel.x *= friction; m_PouchVel.z *= friction; }
                }
                if (m_Free)
                {
                    m_Omega = (m_Theta - prevTheta) / h;
                    if (!m_Released) CheckRelease();
                    // The buffer rope stops the arm past vertical: a hard knock and a small bounce back.
                    else if (m_Theta < caught && m_Omega < 0f)
                    {
                        m_Omega *= -CatchRebound;
                        if (!m_Caught) { m_Caught = true; m_SlamNow = true; }
                    }
                }
            }
            if (m_PouchPin > 0f)
            {
                m_PouchPos = Vector3.Lerp(m_PouchPos, lie, m_PouchPin);
                m_PouchVel *= 1f - m_PouchPin;
            }
            if (m_Free) m_SwingTime += dt;
        }

        // Position-based distance constraint between a point mass and a point on the arm; the arm takes its
        // share of the correction through its inertia only while it swings free.
        void Constrain(ref Vector3 x, float invMass, Vector3 r0, float length, bool rigid)
        {
            var r = Quaternion.AngleAxis(m_Theta * Mathf.Rad2Deg, m_Axis) * r0;
            var d = x - (m_Pivot + r);
            float dist = d.magnitude;
            if (dist < 1e-5f) return;
            float c = dist - length;
            if (!rigid && c <= 0f) return;
            var nrm = d / dist;
            float gradTheta = -Vector3.Dot(nrm, Vector3.Cross(m_Axis, r));
            float invArm = m_Free ? gradTheta * gradTheta / m_Inertia : 0f;
            float lambda = -c / (invMass + invArm);
            x += nrm * (invMass * lambda);
            if (m_Free) m_Theta += gradTheta / m_Inertia * lambda;
        }

        // Let go once the pouch is climbing forward at the release angle (or if the moment was missed).
        void CheckRelease()
        {
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float vf = Vector3.Dot(m_PouchVel, fwd), vu = m_PouchVel.y;
            float elev = Mathf.Atan2(vu, vf) * Mathf.Rad2Deg;
            bool timely = ArmDeg < -25f && vf > 0f && vu > 0f && elev <= releaseAngle;
            if (!timely && ArmDeg > firedAngle - 10f && m_SwingTime < 2.5f) return;
            m_Released = true;
            float a = releaseAngle * Mathf.Deg2Rad;
            m_ReleaseVel = timely ? m_PouchVel : (fwd * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * Mathf.Max(m_PouchVel.magnitude, 20f * Scale);
            if (m_ShotPending) { m_ShotPending = false; m_ReleaseNow = true; }
        }

        void ApplyRig(float dt)
        {
            float s = Scale;
            arm.localRotation = m_ArmRest * Quaternion.Euler(m_ArmSign * ArmDeg, 0f, 0f);
            if (counterweight)
            {
                // Always hangs world-down from the hinge, lagging on its pendulum.
                var hinge = ArmPoint(m_R0Hinge, m_Theta);
                var hang = m_CwPos - hinge;
                var rest = transform.rotation * m_CwRest;
                counterweight.position = hinge;
                counterweight.rotation = hang.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(transform.TransformDirection(m_CwDir), hang) * rest : rest;
            }
            if (pouch)
            {
                // Opening faces up while it lies in the trough, toward the tip once the cords pull taut; its ring
                // axis runs across the swing.
                var toTip = ArmPoint(m_R0Tip, m_Theta) - m_PouchPos;
                float dist = toTip.magnitude;
                float taut = Mathf.InverseLerp(slingLength * s * 0.8f, slingLength * s, dist);
                var up = Vector3.Slerp(Base.up, dist > 1e-4f ? toTip / dist : Base.up, taut);
                var fwd = Vector3.Cross(transform.right, up);
                pouch.position = m_PouchPos;
                pouch.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-6f ? fwd : transform.forward, up) * Quaternion.Euler(0f, 90f, 0f);
            }
            if (ropeWinch && ropeArm)
            {
                // Hooked on, winching takes up any slack (at a hauling pace) and then whatever the arm gives; it
                // pays out if the arm pulls. The loose end just lies slack. The drum only turns with hooked rope.
                float d = Vector3.Distance(ropeWinch.position, RopeEnd()) / Mathf.Max(0.0001f, s);
                if (m_RopeHook < 1f || m_RopeLen < 0f) m_Slack = RopeSlack;
                else if (m_Winching) m_Slack = Mathf.MoveTowards(m_Slack, 0f, RopeReel * dt);
                else m_Slack = Mathf.Max(0f, m_RopeLen - d);
                float len = d + m_Slack;
                if (m_RopeHook >= 1f && m_RopeLen >= 0f) m_Drum += (m_RopeLen - len) / DrumRadius * Mathf.Rad2Deg;
                m_RopeLen = len;
                if (windlass) windlass.localRotation = m_WindlassRest * Quaternion.Euler(m_Drum, 0f, 0f);
            }
        }

        Vector3 RopeEnd() => Vector3.Lerp(Base.TransformPoint(m_RopeDrop), ropeArm.position, Mathf.SmoothStep(0f, 1f, m_RopeHook));

        void DrawLines()
        {
            float s = Scale;
            if (sling)
            {
                sling.enabled = pouch && slingTip;
                if (sling.enabled)
                {
                    // Two cords from the rings at the pouch's ends meeting at the arm tip.
                    var tip = slingTip.position;
                    var left = pouch.TransformPoint(new Vector3(PouchRing.x, PouchRing.y, -PouchRing.z));
                    var right = pouch.TransformPoint(PouchRing);
                    for (int i = 0; i <= SlingSegments; i++)
                    {
                        float t = (float)i / SlingSegments;
                        sling.SetPosition(i, Sagged(left, tip, slingLength * s, t));
                        sling.SetPosition(SlingSegments * 2 - i, Sagged(right, tip, slingLength * s, t));
                    }
                }
            }
            if (rope && ropeWinch && ropeArm)
            {
                rope.enabled = Alive;
                var a = ropeWinch.position;
                var b = RopeEnd();
                for (int i = 0; i <= RopeSegments; i++)
                    rope.SetPosition(i, Sagged(a, b, m_RopeLen * s, (float)i / RopeSegments));
            }
        }

        // Point along a rope of the given length hung between a and b (parabolic sag), kept off the ground.
        Vector3 Sagged(Vector3 a, Vector3 b, float length, float t)
        {
            float d = Vector3.Distance(a, b);
            float sag = length > d ? Mathf.Sqrt(3f * d * (length - d) / 8f) : 0f;
            var p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t);
            var lp = Base.InverseTransformPoint(p);
            if (lp.y < 0.05f) { lp.y = 0.05f; p = Base.TransformPoint(lp); }
            return p;
        }

        // ---------------------------------------------------------------- intro
        // Assembles in place: the engine unfolds upward in four heaving lifts (it never dips below the floor),
        // the powder kegs and shot are set down with the last one, and the crew run in from the camp behind it.
        // (Name and signature kept from the catapult so GameManager is unchanged.)
        public IEnumerator RollIn(float distance = 30f, float duration = 2.6f)
        {
            const int Lifts = 4;
            m_IntroTime = Mathf.Max(0.5f, duration);
            m_IntroRun = Mathf.Min(distance / Mathf.Max(0.0001f, Scale), 18f);
            if (knight) knight.SetState(Knight.State.Walk);
            int stage = 0;
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float lift = Mathf.Clamp01(k / 0.85f) * Lifts;
                int i = Mathf.Min((int)lift, Lifts - 1);
                m_Unfold = Mathf.Lerp(IntroFold, 1f, (i + (lift >= Lifts ? 1f : Ease.OutBack(lift - i))) / Lifts);
                if ((int)lift != stage) { stage = (int)lift; Heave(stage == Lifts ? 1f : 0.5f); }
                ShowProps(Mathf.Clamp01(lift - (Lifts - 1)));
                if (stage < Lifts && Time.frameCount % 5 == 0)
                {
                    var off = UnityEngine.Random.insideUnitCircle * 6f * Scale;
                    VFX.Dust(transform.position + transform.right * off.x + transform.forward * off.y, 0.7f * Fx);
                }
                PlaceParty(k);
                yield return null;
            }
            if (stage < Lifts) Heave(1f);
            Haptics.PieceLand(0.5f);
            FinishIntro();
            m_PouchVel = transform.forward * 2f * Scale;
            m_CwVel = transform.forward * 0.6f * Scale;
        }

        void Heave(float strength)
        {
            var p = transform.position;
            float s = Scale;
            Sfx.Play(Sound.Thud, p, 0.4f + 0.5f * strength);
            VFX.Dust(p + transform.forward * 5f * s, strength * Fx);
            VFX.Dust(p - transform.forward * 6f * s, strength * Fx);
            m_Wobble = Mathf.Max(m_Wobble, 1.5f * strength);
        }

        void FinishIntro()
        {
            m_Introduced = true;
            m_Unfold = 1f;
            ShowProps(1f);
            PoseBody();
            PlaceParty(1f);
            SetCrew(Soldier.State.Idle);
            if (knight) knight.SetState(Knight.State.Idle);
            ResetSim();
        }

        void ShowProps(float k)
        {
            for (int i = 0; i < m_Props.Length; i++)
            {
                var p = m_Props[i];
                if (!p) continue;   // a keg that already went off
                if (p.gameObject.activeSelf != k > 0f) p.gameObject.SetActive(k > 0f);
                p.localScale = m_PropScale[i] * (k >= 1f ? 1f : Mathf.Max(0.01f, Ease.OutBack(k)));
            }
        }

        // Smoothstep peaks at 1.5x its average pace: the longest run that stays under RunSpeed.
        float RunDistance(float span) => Mathf.Min(m_IntroRun, RunSpeed * span * m_IntroTime / 1.5f);

        void PlaceParty(float k)
        {
            if (crew != null)
                for (int i = 0; i < crew.Length && i < m_CrewHome.Length; i++)
                {
                    if (!crew[i] || !crew[i].Alive) continue;
                    float from = 0.05f + 0.035f * i;
                    var home = m_CrewHome[i];
                    PlaceRunner(crew[i].transform, home, m_CrewHomeRot[i], Mathf.InverseLerp(from, 0.95f, k), RunDistance(0.95f - from), Mathf.Sign(home.x) * (1f + 0.25f * i));
                }
            if (knight) PlaceRunner(knight.transform, m_KnightHome, m_KnightHomeRot, Mathf.InverseLerp(0f, 0.9f, k), Mathf.Min(KnightWalkIn, RunDistance(0.9f)), Mathf.Sign(m_KnightHome.x) * 1.5f);
        }

        // Runs in from `run` metres behind the post (off to the post's own side, so nobody cuts across the
        // trough), then turns to face its job. The legs follow the real movement (Soldier gait, Knight walk).
        void PlaceRunner(Transform t, Vector3 home, Quaternion homeRot, float u, float run, float lateral)
        {
            var start = home + new Vector3(lateral, 0f, -run);
            var travel = home - start;
            var runRot = Quaternion.LookRotation(travel.sqrMagnitude > 1e-4f ? travel : Vector3.forward);
            t.position = transform.TransformPoint(Vector3.Lerp(start, home, Mathf.SmoothStep(0f, 1f, u)));
            t.rotation = transform.rotation * Quaternion.Slerp(runRot, homeRot, Mathf.InverseLerp(0.75f, 1f, u));
        }

        // The knight strides to a post (root-local), turning to face along the way, then takes up a job.
        IEnumerator KnightWalk(Vector3 to, Quaternion toRot, Knight.State then)
        {
            var kt = knight.transform;
            var from = transform.InverseTransformPoint(kt.position);
            var fromRot = Quaternion.Inverse(transform.rotation) * kt.rotation;
            var travel = to - from;
            travel.y = 0f;
            var walkRot = travel.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(travel) : fromRot;
            float dur = Mathf.Max(0.6f, travel.magnitude / KnightPace);
            knight.SetState(Knight.State.Walk);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float u = t / dur;
                kt.position = transform.TransformPoint(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, u)));
                var r = u < 0.25f ? Quaternion.Slerp(fromRot, walkRot, u / 0.25f) : Quaternion.Slerp(walkRot, toRot, Mathf.InverseLerp(0.7f, 1f, u));
                kt.rotation = transform.rotation * r;
                yield return null;
            }
            kt.position = transform.TransformPoint(to);
            kt.rotation = transform.rotation * toRot;
            knight.SetState(then);
        }

        IEnumerator KnightReturn()
        {
            yield return KnightWalk(m_KnightHome, m_KnightHomeRot, Knight.State.Idle);
            m_KnightWalk = null;
        }

        // ---------------------------------------------------------------- crew
        void SetCrew(Soldier.State state)
        {
            if (crew == null) return;
            foreach (var c in crew) if (c && c.Alive) c.SetState(state);
        }

        int CrewAlive()
        {
            int n = 0;
            if (crew != null) foreach (var c in crew) if (c && c.Alive) n++;
            return n;
        }

        void OnPieceDestroyed(CastlePiece piece)
        {
            if (!Alive || m_Winching) return;
            if (m_Cheer != null) StopCoroutine(m_Cheer);
            m_Cheer = StartCoroutine(CrewCheer());
        }

        IEnumerator CrewCheer()
        {
            SetCrew(Soldier.State.Cheer);
            yield return new WaitForSeconds(1.6f);
            if (!m_Winching && Alive) SetCrew(Soldier.State.Idle);
            m_Cheer = null;
        }

        // ---------------------------------------------------------------- siege loop
        IEnumerator SiegeLoop()
        {
            yield return new WaitForSeconds(0.8f);
            while (Alive && m_Castle && !m_Castle.Fallen)
            {
                yield return Reload();
                if (!Alive) yield break;
                yield return LoadFireball();
                // Choose target
                m_Shots++;
                bool nearMiss = m_Camera && m_Shots % Tune.NearMissEvery == 0;
                CastlePiece piece = null;
                Vector3 target;
                if (nearMiss)
                    target = NearMissPoint();
                else
                {
                    piece = m_Castle.PickTarget();
                    target = piece ? m_Castle.AimPoint(piece, transform.position) : m_Castle.Center;
                    if (UnityEngine.Random.value < Tune.MissChance)
                    {
                        var off = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(6f, 10f);
                        target = new Vector3(target.x + off.x, m_Castle.transform.position.y + 1f, target.z + off.y);
                        piece = null;
                    }
                }
                yield return AimAt(target);
                if (!Alive) yield break;
                yield return Fire(target, piece, nearMiss);
                yield return new WaitForSeconds(Tune.CatapultCooldown * Mathf.Lerp(0.7f, 1f, Health / MaxHealth));
            }
        }

        Vector3 NearMissPoint()
        {
            // Aim to skim just past the player's head and carry on behind them.
            var cam = m_Camera;
            var side = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * cam.right;
            var past = (cam.position - transform.position);
            past.y = 0;
            float m = GameManager.Instance ? GameManager.Instance.unitsPerMeter : 20f;
            return cam.position + side * 0.35f * m + Vector3.up * 0.12f * m + past.normalized * 0.1f * m;
        }

        // The loose rope end goes back up onto the arm's hook while the arm finishes rocking, the windlass takes
        // up the slack, then the crew haul the arm down a ratchet click at a time. Fewer hands, slower winding;
        // with no crew left the knight takes a place at the windlass and cranks it alone.
        IEnumerator Reload()
        {
            m_RopeOn = true;
            while (m_RopeHook < 1f && Alive) yield return null;
            m_Free = false;
            m_ArmTarget = m_Theta;
            m_Winching = true;
            float start = m_Theta, end = loadedAngle * Mathf.Deg2Rad;
            const int Clicks = 16;
            int click = 0;
            bool knightCranks = false;
            var winch = windlass ? windlass.position : transform.position;
            SetCrew(Soldier.State.Haul);
            Sfx.Play(Sound.CatapultCrank, winch, 0.8f);
            for (float k = 0f; k < 1f && Alive;)
            {
                int hands = CrewAlive();
                if (hands == 0 && !knightCranks && knight)
                {
                    knightCranks = true;
                    yield return KnightWalk(m_CrankSpot, m_CrankRot, Knight.State.Crank);
                    continue;
                }
                if (knight && knightCranks) knight.crankPhase = m_Drum / 360f;   // his handle turns with the drum
                if (m_Slack > 0.05f) { yield return null; continue; }   // still taking up the slack
                float dur = reloadTime * (hands == 0 ? 2.2f : Mathf.Lerp(1.7f, 1f, (hands - 1) / Mathf.Max(1f, m_CrewCount - 1f)));
                k = Mathf.Min(1f, k + Time.deltaTime / Mathf.Max(0.1f, dur));
                float stepped = Mathf.Min(1f, Mathf.Floor(k * Clicks) / Clicks + Mathf.SmoothStep(0, 1f / Clicks, (k * Clicks) % 1f));
                m_ArmTarget = Mathf.Lerp(start, end, stepped);
                // the sling is dragged down the trough as the arm comes down onto it
                m_PouchDraw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.85f, k));
                if ((int)(k * Clicks) != click)
                {
                    click = (int)(k * Clicks);
                    Sfx.Play(Sound.CatapultCrank, winch, 0.45f, 0.1f);
                }
                yield return null;
            }
            m_ArmTarget = end;
            m_Winching = false;
            SetCrew(Soldier.State.Idle);
            if (knight && knightCranks && Alive) m_KnightWalk = StartCoroutine(KnightReturn());
            else if (knight) knight.SetState(Knight.State.Idle);
        }

        IEnumerator LoadFireball()
        {
            // The sling is drawn the rest of the way into the trough and laid out, then the fireball is
            // rolled in and lit.
            m_PouchDraw = 1f;
            var lie = Base.TransformPoint(m_PouchLie);
            for (float t = 0; t < 2f && Vector3.Distance(m_PouchPos, lie) > 0.3f * Scale; t += Time.deltaTime)
            {
                yield return null;
                lie = Base.TransformPoint(m_PouchLie);
            }
            m_PouchDraw = 0f;
            float from = m_PouchPin;
            for (float t = 0; t < 0.4f; t += Time.deltaTime)
            {
                m_PouchPin = Mathf.Lerp(from, 1f, Mathf.SmoothStep(0f, 1f, t / 0.4f));
                yield return null;
            }
            m_PouchPin = 1f;
            if (!fireballPrefab || !pouch) yield break;
            m_Loaded = Instantiate(fireballPrefab, pouch.TransformPoint(FireballSeat), pouch.rotation, pouch);
            m_Loaded.transform.localPosition = FireballSeat;
            m_Loaded.transform.localScale = Vector3.zero;
            var full = Vector3.one * (fireballScale * transform.lossyScale.x / Mathf.Max(0.001f, pouch.lossyScale.x));
            for (float t = 0; t < 0.35f && m_Loaded; t += Time.deltaTime)
            {
                m_Loaded.transform.localScale = full * Ease.OutBack(t / 0.35f);
                yield return null;
            }
            if (!m_Loaded) yield break;
            m_Loaded.transform.localScale = full;
            m_Loaded.Ignite();
            Sfx.Play(Sound.Thud, pouch.position, 0.4f);
        }

        IEnumerator AimAt(Vector3 target)
        {
            var flat = target - transform.position;
            flat.y = 0;
            if (flat.sqrMagnitude < 0.01f) yield break;
            var from = transform.rotation;
            var to = Quaternion.LookRotation(flat.normalized, Vector3.up);
            float angle = Quaternion.Angle(from, to);
            if (angle < 0.5f) yield break;
            // A heavy engine: levered round in one slow, grinding heave.
            float dur = Mathf.Clamp(angle / 10f, 0.4f, Tune.CatapultAim * 2.5f);
            Sfx.Play(Sound.CatapultCrank, transform.position, 0.4f, 0.1f);
            VFX.Dust(transform.position - transform.forward * 6f * Scale, 0.6f * Fx);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, Ease.InOutCubic(t / dur));
                yield return null;
            }
            transform.rotation = to;
            Sfx.Play(Sound.Thud, transform.position, 0.35f);
        }

        IEnumerator Fire(Vector3 target, CastlePiece piece, bool nearMiss)
        {
            while (m_KnightWalk != null && Alive) yield return null;   // back at his post to give the order
            if (knight) knight.SetState(Knight.State.Pull);
            yield return new WaitForSeconds(0.35f);
            // Trigger pin out: the arm swings free and the swing simulation does the rest (release, slam).
            m_ShotTarget = target;
            m_ShotPiece = piece;
            m_ShotNearMiss = nearMiss;
            m_ShotPending = m_Loaded != null;
            m_Released = m_ReleaseNow = m_Caught = m_SlamNow = m_Whooshed = false;
            m_SwingTime = 0f;
            m_PouchPin = m_PouchDraw = 0f;
            m_PouchVel = Vector3.zero;
            m_Free = true;
            m_RopeOn = false;   // the winch rope drops off the hook
            Sfx.Play(Sound.Thud, slingTip ? slingTip.position : m_Pivot, 0.5f, 0.1f);
            for (float t = 0; t < 4f && !m_Caught && Alive; t += Time.deltaTime) yield return null;
            // Follow-through: let the arm rock until it has nearly settled (the winch catches the rest).
            for (float t = 0; t < 2.5f && Alive; t += Time.deltaTime)
            {
                if (Mathf.Abs(ArmDeg - firedAngle) < 4f && Mathf.Abs(m_Omega * Mathf.Rad2Deg) < 12f) break;
                yield return null;
            }
            if (knight) knight.SetState(Knight.State.Idle);
        }

        void Release(Vector3 target, CastlePiece piece, bool nearMiss)
        {
            if (!m_Loaded) return;
            var start = m_Loaded.transform.position;
            float T = FlightTime(start, target, m_ReleaseVel);
            var g = Physics.gravity;
            var v = (target - start) / T - 0.5f * g * T;
            GameObject marker = null;
            if (!nearMiss && landingMarkerPrefab)
            {
                var landing = target;
                if (Physics.Raycast(target + Vector3.up * 20f, Vector3.down, out var hit, 60f, Layers.Mask(Layers.Castle, Layers.Environment)))
                    landing = hit.point + hit.normal * 0.15f;
                marker = Instantiate(landingMarkerPrefab, landing, Quaternion.identity);
                marker.GetComponent<LandingMarker>()?.Init(T);
            }
            m_Loaded.Launch(v, T, target, m_Castle, piece, m_Camera, nearMiss, marker, m_Colliders);
            m_Loaded = null;
            Sfx.Play(Sound.CatapultLaunch, transform.position, 1f);
            float prox = m_Camera ? Mathf.Clamp01(1f - Vector3.Distance(transform.position, m_Camera.position) / 250f) : 0.3f;
            Haptics.CatapultLaunch(prox);
        }

        // Catapult timing sets the bounds; within them, the flight time whose launch velocity is closest to how
        // the pouch was actually moving, so the fireball carries straight on out of the sling.
        float FlightTime(Vector3 from, Vector3 to, Vector3 pouchVel)
        {
            float fallback = Tune.FireballFlightTime * Mathf.Clamp(Vector3.Distance(from, to) / 110f, 0.75f, 1.35f);
            var d = to - from;
            var g = Physics.gravity;
            float best = fallback, err = float.MaxValue;
            const int Samples = 48;
            for (int i = 0; i <= Samples; i++)
            {
                float T = Mathf.Lerp(fallback * 0.6f, fallback * 1.6f, (float)i / Samples);
                float e = (d / T - 0.5f * g * T - pouchVel).sqrMagnitude;
                if (e < err) { err = e; best = T; }
            }
            return best;
        }

        // The buffer rope checks the arm past vertical: the whole frame lurches forward and kicks up dust.
        void Slam()
        {
            float s = Scale;
            Sfx.Play(Sound.CatapultSlam, m_Pivot, 1f);
            m_KickPos = Vector3.forward * 0.3f + Vector3.up * 0.1f;
            m_Wobble = 2f;
            VFX.Dust(transform.position + transform.forward * 5f * s, 1.2f * Fx);
            VFX.Dust(transform.position - transform.forward * 5f * s, 1.2f * Fx);
        }

        // ---------------------------------------------------------------- damage
        public void TakeHit(HitInfo hit) => ApplyDamage(hit, false);

        public void ApplyDamage(HitInfo hit, bool knightHit)
        {
            if (!Alive) return;
            float before = Health;
            Health = Mathf.Max(0f, Health - hit.damage);
            m_Wobble = Mathf.Max(m_Wobble, 0.4f + hit.damage * 0.3f);
            VFX.Splinters(hit.point, -hit.direction, 1f + hit.damage * 0.15f);
            Hit?.Invoke(hit.damage, hit.point, knightHit);
            float f = Health / MaxHealth;
            // smoke and fire take hold on the timber of the tower the player aims at
            if (f <= 0.66f && before / MaxHealth > 0.66f) m_Smoke = VFX.SmokeColumn(transform, AimCenter, 0.7f * Fx);
            if (f <= 0.33f && before / MaxHealth > 0.33f)
            {
                m_Fire = VFX.Fire(transform, AimCenter + Vector3.up * 1.5f * Scale, 1.1f * Fx);
                Sfx.Loop(Sound.FireCrackle, m_Fire ? m_Fire.transform : transform, 0.5f);
            }
            if (Health <= 0f) StartCoroutine(BreakApart(hit.point));
        }

        IEnumerator BreakApart(Vector3 impact)
        {
            Stop();
            m_Broken = true;
            m_Free = false;
            var center = Core;
            var towerA = AimCenter;
            var towerB = m_Aim ? Base.TransformPoint(Vector3.Scale(m_Aim.offset, new Vector3(-1f, 1f, 1f))) : center;
            if (m_Aim) m_Aim.enabled = false;
            ScreenFX.SlowMo(0.22f, 1.1f);
            float s = Scale, fx = Fx;
            VFX.Explosion(towerA, 1.6f * fx);
            VFX.Explosion(towerB, 1.1f * fx, false);
            if (arm) VFX.Explosion(m_Pivot, 1f * fx, false);
            Sfx.Play(Sound.ExplosionTnt, center, 1f, 0.02f);
            Sfx.Play(Sound.WoodBreak, center, 1f, 0.02f);
            Haptics.CatapultDestroyed();
            ScreenFX.Shake(0.9f);
            ScreenFX.Punch(0.8f);
            ScreenFX.Heat(0.5f);
            if (m_Loaded) Destroy(m_Loaded.gameObject);
            m_Loaded = null;
            if (barrels != null) foreach (var b in barrels) if (b) b.Detonate(false);

            // Frame shatters; arm, counterweight and fittings fall as whole pieces, keeping the motion they had.
            if (frame)
            {
                if (m_FrameRenderer) m_FrameRenderer.enabled = false;
                foreach (var c in frame.GetComponents<Collider>()) c.enabled = false;
                MeshFracture.Shatter(frame.transform, frame.sharedMesh, debrisMaterial, 18, center - Vector3.up * s, 26f * s);
            }
            var armComVel = arm ? Vector3.Cross(m_Axis, Quaternion.AngleAxis(ArmDeg, m_Axis) * m_R0Com) * m_Omega : Vector3.zero;
            var loose = new List<Transform>();
            foreach (var t in new[] { arm, counterweight, pouch, windlass, banner })
                if (t && !loose.Contains(t)) loose.Add(t);
            foreach (var t in loose) t.SetParent(null, true);
            foreach (var t in loose)
            {
                t.gameObject.layer = Layers.Debris;
                foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
                var mf = t.GetComponent<MeshFilter>();
                var box = t.gameObject.AddComponent<BoxCollider>();
                if (mf && mf.sharedMesh) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.2f); }
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var outward = Vector3.ProjectOnPlane(t.position - center, Vector3.up).normalized;
                if (t == arm)
                {
                    // topples off its bearings, still turning, and tips over sideways
                    rb.mass = 6f;
                    rb.linearVelocity = armComVel + (Vector3.up * 3f + outward * 2f) * s;
                    rb.angularVelocity = m_Axis * m_Omega + transform.forward * UnityEngine.Random.Range(-1.2f, 1.2f);
                }
                else if (t == counterweight)
                {
                    rb.mass = 14f;
                    rb.linearVelocity = m_CwVel + outward * 1.5f * s;
                    rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 1.5f;
                }
                else if (t == pouch)
                {
                    rb.mass = 0.5f;
                    rb.linearVelocity = m_PouchVel + UnityEngine.Random.insideUnitSphere * 4f * s;
                    rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;
                }
                else
                {
                    rb.mass = 3f;
                    rb.linearVelocity = ((t.position - center).normalized + Vector3.up * 1.4f) * UnityEngine.Random.Range(8f, 14f) * s;
                    rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;
                }
                t.gameObject.AddComponent<Debris>();
            }
            if (sling) sling.enabled = false;
            if (rope) rope.enabled = false;
            if (knight) knight.Ragdoll(center, 20f * s);
            if (crew != null) foreach (var c in crew) if (c && c.Alive) c.Kill(center, 16f * s);
            if (m_Smoke) VFX.Release(m_Smoke, 8f);
            VFX.Fire(null, transform.position + Vector3.up * 0.4f * s, 1.6f * fx);
            VFX.SmokeColumn(null, transform.position, 1.2f * fx);
            yield return new WaitForSecondsRealtime(0.4f);
            Destroyed?.Invoke();
        }
    }
}
