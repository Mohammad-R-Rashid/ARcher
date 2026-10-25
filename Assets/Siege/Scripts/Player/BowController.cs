using System;
using UnityEngine;

namespace Siege
{
    // First-person bow: a view-model bow hangs off the camera, the string and limbs bend with the
    // draw, and release fires a real arrow that lands on the crosshair. With the crosshair on an enemy
    // the arrow goes to that exact spot, led with that enemy's motion; otherwise aim assist locks onto
    // the AimTarget nearest the crosshair and leads it (falling targets like fireballs include gravity).
    public class BowController : MonoBehaviour
    {
        public Transform cam;
        public Transform bowRoot;          // view-model root (child of camera)
        // Bones of the skinned bow (Stylized Newbie Weapons Pack): limbs flex, string bone pulls back.
        public Transform limbTop, limbBottom, stringBone;
        public BowBender bender;            // mesh-deforming bend (preferred over the rigid limb bones)
        public Transform nockedArrow;      // visual arrow resting on the string
        public Arrow arrowPrefab;
        public LineRenderer trajectory;
        public MonoBehaviour inputSource;  // IBowInput

        [Header("View model (camera-local, metres under the scaled XR origin)")]
        // The grip is pinned to this point of the screen (works for any camera FOV / aspect).
        public Vector2 viewportAnchor = new Vector2(0.78f, 0.3f);
        public float viewDepth = 0.6f;     // metres in front of the lens (far enough for natural perspective)
        public float drawBack = 0.38f;     // string bone pull at full draw (bone-local units, toward the archer)
        public float limbBend = 14f;       // limb flex at full draw (degrees)
        public float cant = 14f;           // bow tilted like an archer holds it (degrees)
        public float arrowScale = 2.2f;    // fired arrows (game metres): big enough to follow in flight at room scale

        [Header("Aim assist")]
        public float assistAngle = 2.5f;   // degrees around the crosshair that snap onto a target
        public float minDrawToFire = 0.04f;   // any real pull fires; a weak one just doesn't carry far

        public static event Action<IDamageable> TargetHit;
        public Vector3? LockedPoint { get; private set; }      // locked target's current aim point
        public AimTarget LockedTarget { get; private set; }
        public float CurrentDraw { get; private set; }
        public bool Armed { get; set; } = true;

        const float StickyAngle = 1.3f;            // a lock holds out to assistAngle x this...
        const float SwitchMargin = 1f;             // ...until another target is this many degrees nearer the crosshair
        const float FireballPriorityAngle = 2f;    // an incoming fireball this close to the crosshair beats anything behind it

        IBowInput m_Input;
        float m_LockAngle;
        float m_Cooldown;
        float m_Recoil;
        float m_RecoilVel;
        float m_LastCreak;
        bool m_WasDrawing;
        AudioSource m_DrawSound;
        Vector3 m_NockRest;
        Quaternion m_LimbTopRest, m_LimbBotRest;
        readonly Vector3[] m_Arc = new Vector3[48];

        void Awake()
        {
            m_Input = inputSource as IBowInput;
            m_LimbTopRest = limbTop.localRotation;
            m_LimbBotRest = limbBottom.localRotation;
            m_NockRest = stringBone.localPosition;
            m_DrawSound = gameObject.AddComponent<AudioSource>();
            m_DrawSound.playOnAwake = false;
            m_DrawSound.spatialBlend = 0f;
            m_DrawSound.volume = 0.7f;
        }

        public static void NotifyHit(IDamageable d) => TargetHit?.Invoke(d);

        // Swap the input source at runtime (IMU glove bridge, autopilot).
        public void SetInput(IBowInput input) => m_Input = input;

        void Update()
        {
            float dt = Time.deltaTime;
            m_Cooldown = Mathf.Max(m_Cooldown - dt, -10f);
            UpdateLock();
            bool ready = Armed && m_Cooldown <= 0f;
            float target = ready && m_Input != null && m_Input.Drawing ? m_Input.Draw : 0f;
            CurrentDraw = MathExt.Damp(CurrentDraw, target, target > CurrentDraw ? 14f : 30f, dt);

            bool drawing = ready && m_Input != null && m_Input.Drawing;
            if (drawing && !m_WasDrawing) BeginDraw();
            if (drawing) UpdateDraw();
            if (m_Input != null && m_Input.ReleasedThisFrame && m_WasDrawing)
            {
                if (CurrentDraw >= minDrawToFire) Fire(CurrentDraw, null);
                EndDraw();
                CurrentDraw = 0f;
            }
            else if (!drawing && m_WasDrawing) EndDraw();
            m_WasDrawing = drawing;

            Pose(dt);
            if (trajectory) trajectory.enabled = false;   // no aim arc: the arrow lands on the crosshair
        }

        void BeginDraw()
        {
            Haptics.BowDrawStart();
            m_LastCreak = 0f;
            var clip = Sfx.ClipFor(Sound.BowDraw);
            if (clip) { m_DrawSound.clip = clip; m_DrawSound.Play(); }
        }

        void UpdateDraw()
        {
            Haptics.BowDrawUpdate(CurrentDraw);
            if (CurrentDraw - m_LastCreak > 0.18f)
            {
                m_LastCreak = CurrentDraw;
                Haptics.BowCreak(CurrentDraw);
            }
            if (m_DrawSound.isPlaying && CurrentDraw > 0.97f) m_DrawSound.Pause();
        }

        void EndDraw()
        {
            Haptics.BowDrawStop();
            m_DrawSound.Stop();
        }

        // Public so tests / the glove bridge can fire directly.
        public void Fire(float power, Vector3? aimDirOverride)
        {
            power = Mathf.Clamp01(power);
            var spawn = LaunchPoint();
            float speed = SpeedFor(power);
            // Aim as for a full-strength shot, then loose at this draw's speed: a light pull drops short.
            var dir = aimDirOverride ?? AimDirection(spawn, SpeedFor(1f));
            var arrow = Instantiate(arrowPrefab, spawn, Quaternion.LookRotation(dir));
            // Longer and proportionally thicker than life so a stuck arrow still reads from across the room.
            arrow.transform.localScale = new Vector3(arrowScale * 1.7f, arrowScale * 1.7f, arrowScale);
            arrow.Launch(dir * speed, power);
            m_Cooldown = Tune.ArrowCooldown;
            m_RecoilVel = 9f + power * 8f;
            Sfx.Play(Sound.BowRelease, cam.position, 0.9f, 0.08f, 0f);
            Sfx.Play(Sound.ArrowWhoosh, cam.position + dir * 6f, 0.5f * power, 0.1f, 0.4f);
            Haptics.BowRelease(power);
            ScreenFX.Punch(0.08f * power);
        }

        // Arrows leave from just ahead of and below the lens, so they fly straight down the crosshair line
        // instead of cutting diagonally across the screen from the bow's corner.
        Vector3 LaunchPoint()
        {
            float s = cam.lossyScale.x;
            return cam.position + cam.forward * 0.25f * s - cam.up * 0.02f * s + cam.right * 0.015f * s;
        }

        float SpeedFor(float power) => Mathf.Lerp(Tune.ArrowMinSpeed, Tune.ArrowMaxSpeed, Mathf.Pow(Mathf.Clamp01(power), 0.8f));
        // (ArrowMinSpeed is a feeble flick: with the full-draw aim, short pulls land well short of the crosshair.)

        // Locks the aim target nearest the crosshair inside the assist cone (trebuchet, knight, kegs, soldiers,
        // fireballs). The lock is sticky so a crowd doesn't flip it every frame, targets hidden behind the castle
        // are skipped, and an incoming fireball right on the crosshair always wins.
        void UpdateLock()
        {
            var prev = LockedTarget;
            LockedPoint = null;
            LockedTarget = null;
            if (!Armed || !cam) return;
            var eye = cam.position;
            var fwd = cam.forward;
            AimTarget best = null, fireball = null;
            float bestA = assistAngle, fireA = FireballPriorityAngle, prevA = float.MaxValue;
            var all = AimTarget.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (!t) continue;
                var p = t.Point;
                float a = Vector3.Angle(fwd, p - eye);
                bool isPrev = t == prev;
                if (a >= (isPrev ? assistAngle * StickyAngle : assistAngle)) continue;
                bool wanted = isPrev || a < bestA || (t.ballistic && a < fireA);
                if (!wanted || !Visible(eye, p)) continue;
                if (isPrev) prevA = a;
                else if (a < bestA) { best = t; bestA = a; }
                if (t.ballistic && a < fireA) { fireball = t; fireA = a; }
            }
            bool keep = prevA < float.MaxValue && !(best && bestA < prevA - SwitchMargin);
            LockedTarget = keep ? prev : best;
            m_LockAngle = keep ? prevA : bestA;
            if (fireball) { LockedTarget = fireball; m_LockAngle = fireA; }
            if (LockedTarget) LockedPoint = LockedTarget.Point;
        }

        // Not hidden behind the castle (the end stops short so a climber against a wall still counts).
        static bool Visible(Vector3 eye, Vector3 p)
        {
            var d = p - eye;
            float len = d.magnitude;
            return len < 1f || !Physics.Raycast(eye, d / len, len - 0.5f, 1 << Layers.Castle, QueryTriggerInteraction.Ignore);
        }

        // The aim target an enemy collider belongs to: the nearest one up its own (Enemy-layer) hierarchy,
        // so a knight's leg finds the knight and a trebuchet beam finds the trebuchet.
        static AimTarget OwnerOf(Collider c, Vector3 point)
        {
            var all = AimTarget.All;
            for (var level = c.transform; level && level.gameObject.layer == Layers.Enemy; level = level.parent)
            {
                AimTarget owner = null;
                float bestD = float.MaxValue;
                for (int i = 0; i < all.Count; i++)
                {
                    var t = all[i];
                    if (!t || !t.transform.IsChildOf(level)) continue;
                    float d = (t.Point - point).sqrMagnitude;
                    if (d < bestD) { bestD = d; owner = t; }
                }
                if (owner) return owner;
            }
            return null;
        }

        static Vector3 Lead(AimTarget t, Vector3 spawn, float speed) => Lead(t.Point, t.Velocity, t.ballistic, spawn, speed);

        // Where a point moving at v will be when an arrow loosed now reaches it: flight time and predicted
        // point are refined a few times. Ballistic targets keep falling meanwhile.
        static Vector3 Lead(Vector3 p, Vector3 v, bool ballistic, Vector3 spawn, float speed)
        {
            var g = ballistic ? Physics.gravity : Vector3.zero;
            var aim = p;
            for (int i = 0; i < 6; i++)
            {
                float time = Mathf.Min(FlightTime(spawn, aim, speed), 6f);
                aim = p + v * time + 0.5f * time * time * g;
            }
            return aim;
        }

        // Flight time along the low arc at this speed (straight-line estimate when out of range).
        static float FlightTime(Vector3 from, Vector3 to, float v)
        {
            var d = to - from;
            var dir = Ballistic(from, to, v);
            if (dir.HasValue)
            {
                float vFlat = v * new Vector2(dir.Value.x, dir.Value.z).magnitude;
                if (vFlat > 0.01f) return new Vector2(d.x, d.z).magnitude / vFlat;
            }
            return d.magnitude / Mathf.Max(v, 0.01f);
        }

        // Low-arc launch direction that lands on target at the given speed (null if out of range).
        static Vector3? Ballistic(Vector3 from, Vector3 to, float v)
        {
            var d = to - from;
            var flat = new Vector3(d.x, 0, d.z);
            float x = flat.magnitude, y = d.y, g = -Physics.gravity.y;
            if (x < 0.01f) return null;
            float disc = v * v * v * v - g * (g * x * x + 2 * y * v * v);
            if (disc < 0) return null;
            float ang = Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * x));
            return (flat / x * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)).normalized;
        }

        Vector3 AimDirection(Vector3 spawn) => AimDirection(spawn, SpeedFor(Mathf.Max(CurrentDraw, 0.5f)));

        Vector3 AimDirection(Vector3 spawn, float speed)
        {
            var aim = m_Input?.AimDirection;
            if (aim.HasValue) return aim.Value.normalized;
            var ray = new Ray(cam.position, cam.forward);
            bool onSomething = Physics.Raycast(ray, out var hit, 2000f, Layers.Mask(Layers.Castle, Layers.Enemy, Layers.Environment, Layers.Debris), QueryTriggerInteraction.Ignore);
            // Crosshair right on an enemy: that exact spot (head shots, beams), led with that enemy's own motion.
            // Only an incoming fireball near the crosshair takes priority over it.
            var owner = onSomething && hit.collider.gameObject.layer == Layers.Enemy ? OwnerOf(hit.collider, hit.point) : null;
            Vector3? target = null;
            if (LockedTarget && LockedTarget.ballistic && m_LockAngle < FireballPriorityAngle) target = Lead(LockedTarget, spawn, speed);
            else if (owner) target = Lead(hit.point, owner.Velocity, owner.ballistic, spawn, speed);
            else if (LockedTarget) target = Lead(LockedTarget, spawn, speed);
            if (target.HasValue)
            {
                var b = Ballistic(spawn, target.Value, speed);
                if (b.HasValue) return b.Value;
            }
            // Whatever is under the crosshair: solve the launch so the arrow actually lands there
            // (compensating for drop), instead of pointing straight at it and falling short.
            if (onSomething)
            {
                var solved = Ballistic(spawn, hit.point, speed);
                return solved ?? (hit.point - spawn).normalized;
            }
            return (cam.position + cam.forward * 2000f - spawn).normalized;
        }

        void Pose(float dt)
        {
            // Recoil spring
            float acc = -160f * m_Recoil - 14f * m_RecoilVel;
            m_RecoilVel += acc * dt;
            m_Recoil += m_RecoilVel * dt;
            float d = CurrentDraw;
            float tremble = d > 0.85f ? (d - 0.85f) * 0.003f : 0f;
            float sway = Mathf.Sin(Time.time * 1.3f) * 0.0012f;
            var lens = cam.GetComponent<Camera>();
            var anchor = cam.InverseTransformPoint(lens.ViewportToWorldPoint(new Vector3(viewportAnchor.x, viewportAnchor.y, viewDepth * cam.lossyScale.z)));
            bowRoot.localPosition = anchor + new Vector3(-0.012f * d + sway, 0.01f * d, m_Recoil * 0.01f)
                                    + UnityEngine.Random.insideUnitSphere * tremble;

            // The bow (and the arrow on it) points where the shot will go, canted like an archer holds it.
            var launch = LaunchPoint();
            var dirWorld = AimDirection(launch, SpeedFor(1f));
            var dirLocal = cam.InverseTransformDirection(dirWorld);
            var aimRot = Quaternion.LookRotation(dirLocal, Vector3.up);
            bowRoot.localRotation = aimRot * Quaternion.Euler(-m_Recoil * 4f, 0f, -cant - d * 4f + m_Recoil * 2f);

            Vector3 nockW;
            if (bender)
            {
                bender.SetDraw(d);
                nockW = bender.NockWorld;
            }
            else
            {
                limbTop.localRotation = m_LimbTopRest * Quaternion.Euler(-d * limbBend, 0, 0);
                limbBottom.localRotation = m_LimbBotRest * Quaternion.Euler(d * limbBend, 0, 0);
                stringBone.localPosition = m_NockRest + Vector3.up * drawBack * d;
                nockW = stringBone.position;
            }
            if (nockedArrow)
            {
                bool show = m_Cooldown <= 0f && Armed;
                nockedArrow.gameObject.SetActive(show);
                // A fresh arrow is nocked after each shot: it slides back along the bow onto the string.
                float slide = show ? Ease.OutCubic(Mathf.Clamp01(-m_Cooldown / 0.25f)) : 0f;
                nockedArrow.rotation = Quaternion.LookRotation(bowRoot.forward, bowRoot.up);
                nockedArrow.position = nockW + bowRoot.forward * (1f - slide) * 0.25f * bowRoot.lossyScale.x;
            }
        }

        void UpdateTrajectory()
        {
            if (!trajectory) return;
            if (CurrentDraw < 0.12f || !nockedArrow) { trajectory.enabled = false; return; }
            trajectory.enabled = true;
            var p = nockedArrow.position;
            float spd = SpeedFor(CurrentDraw);
            var v = AimDirection(p, spd) * spd;
            int n = 0;
            const float step = 0.045f;
            int mask = Layers.Mask(Layers.Castle, Layers.Enemy, Layers.Environment);
            m_Arc[n++] = p;
            for (int i = 1; i < m_Arc.Length; i++)
            {
                var nv = v + Physics.gravity * step;
                var np = p + (v + nv) * 0.5f * step;
                if (Physics.Linecast(p, np, out var hit, mask, QueryTriggerInteraction.Ignore)) { m_Arc[n++] = hit.point; break; }
                m_Arc[n++] = np;
                p = np; v = nv;
            }
            trajectory.positionCount = n;
            for (int i = 0; i < n; i++) trajectory.SetPosition(i, m_Arc[i]);
            var c = new Color(1f, 0.93f, 0.75f, Mathf.InverseLerp(0.12f, 0.5f, CurrentDraw) * 0.8f);
            trajectory.startColor = c;
            trajectory.endColor = new Color(c.r, c.g, c.b, 0f);
        }
    }
}
