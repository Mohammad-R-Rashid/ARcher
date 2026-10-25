using UnityEngine;

namespace Siege
{
    // Allied archer on the battlements (blue tabard). Picks the nearest raider in range, turns, draws and
    // looses a real arrow with lead and some human error, then reloads. A little help - the player is
    // still the main defender. Falls when the tower or wall under him collapses.
    public class Defender : MonoBehaviour
    {
        public Transform hips, torso, head, armL, armR;
        public Arrow arrowPrefab;
        public float range = 75f;
        public float arrowSpeed = 95f;
        public float spreadDeg = 2.2f;          // aim error: roughly a third of shots hit a walking man
        public Vector2 reload = new Vector2(2.8f, 4.2f);
        public float arrowScale = 2.2f;

        CastlePiece m_Stand;
        Quaternion m_TorsoR, m_ArmLR, m_ArmRR, m_HeadR;
        float m_Next, m_Draw, m_Yaw, m_Phase;
        Soldier m_Target;
        bool m_Fallen;

        void Awake()
        {
            m_Stand = GetComponentInParent<CastlePiece>();
            m_TorsoR = torso.localRotation; m_ArmLR = armL.localRotation; m_ArmRR = armR.localRotation; m_HeadR = head.localRotation;
            m_Next = Time.time + Random.Range(1f, 3f);
            m_Phase = Random.value * 10f;
            m_Yaw = transform.eulerAngles.y;
        }

        void Update()
        {
            if (m_Fallen) return;
            if (m_Stand && !m_Stand.Alive) { Fall(); return; }
            var gm = GameManager.Instance;
            bool battle = gm && gm.State == GameManager.Phase.Battle && gm.waves;
            if (battle && (!m_Target || !m_Target.Alive)) m_Target = PickTarget(gm.waves);
            if (!battle) m_Target = null;

            // Turn the whole body toward the target (or back to watching the field).
            float want = m_Target ? Quaternion.LookRotation(Flat(m_Target.transform.position - transform.position)).eulerAngles.y
                                  : (transform.parent ? transform.parent.eulerAngles.y : m_Yaw);
            m_Yaw = Mathf.LerpAngle(m_Yaw, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
            transform.rotation = Quaternion.Euler(0, m_Yaw, 0);

            bool drawing = m_Target && Time.time > m_Next - 0.9f;
            m_Draw = MathExt.Damp(m_Draw, drawing ? 1f : 0f, drawing ? 3f : 10f, Time.deltaTime);
            if (m_Target && Time.time >= m_Next)
            {
                Loose(m_Target);
                m_Next = Time.time + Random.Range(reload.x, reload.y);
                m_Draw = 0f;
            }
            Pose();
        }

        Soldier PickTarget(WaveManager waves)
        {
            Soldier best = null;
            float bd = range;
            foreach (var s in waves.Soldiers)
            {
                if (!s || !s.Alive) continue;
                float d = Vector3.Distance(s.transform.position, transform.position);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        void Loose(Soldier s)
        {
            if (!arrowPrefab) return;
            var from = armL.position + transform.forward * 0.6f + Vector3.up * 0.2f;
            var at = s.GetComponent<AimTarget>();
            var p = at ? at.Point : s.transform.position + Vector3.up * 1.2f;
            var v = at ? at.Velocity : Vector3.zero;
            float t = Vector3.Distance(from, p) / arrowSpeed;
            p += v * t;
            var dir = Ballistic(from, p, arrowSpeed) ?? (p - from).normalized;
            dir = Quaternion.Euler(Random.Range(-spreadDeg, spreadDeg), Random.Range(-spreadDeg, spreadDeg), 0) * dir;
            var arrow = Instantiate(arrowPrefab, from, Quaternion.LookRotation(dir));
            arrow.transform.localScale = new Vector3(arrowScale * 1.7f, arrowScale * 1.7f, arrowScale);
            arrow.fromPlayer = false;
            arrow.Launch(dir * arrowSpeed, 0.8f);
            var src = Sfx.Play(Sound.BowRelease, from, 0.35f, 0.1f);
        }

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

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-4f ? v : Vector3.forward; }

        // Archer's stance: bow arm extended forward, draw hand pulls back to the cheek.
        void Pose()
        {
            float d = m_Draw;
            float breathe = Mathf.Sin(Time.time * 2f + m_Phase) * 1.5f;
            torso.localRotation = m_TorsoR * Quaternion.Euler(breathe, -20f * d, 0);
            armL.localRotation = m_ArmLR * Quaternion.Euler(Mathf.Lerp(-20f, -88f, Mathf.Max(d, 0.35f)), 0, Mathf.Lerp(0f, 8f, d));
            armR.localRotation = m_ArmRR * Quaternion.Euler(Mathf.Lerp(-15f, -95f, d), 0, Mathf.Lerp(5f, -55f, d));
            head.localRotation = m_HeadR * Quaternion.Euler(0, 15f * d, 0);
        }

        void Fall()
        {
            m_Fallen = true;
            transform.SetParent(null, true);
            var rb = gameObject.AddComponent<Rigidbody>();
            gameObject.AddComponent<BoxCollider>().size = new Vector3(0.6f, 1.8f, 0.6f);
            rb.linearVelocity = Random.insideUnitSphere * 3f + Vector3.up * 2f;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
            gameObject.AddComponent<Debris>();
        }
    }
}
