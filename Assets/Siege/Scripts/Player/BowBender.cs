using UnityEngine;

namespace Siege
{
    // Bends the view-model bow like a real one. The pack bow only has one bone per limb, which can only
    // kink at the grip, so instead the mesh itself is deformed: each limb flexes progressively toward the
    // archer (most at the tips, none at the grip), the tips draw slightly inward, and the string forms a
    // straight-sided V from each tip to the nock. Bow space: limbs along ±Z, archer toward +Y.
    public class BowBender : MonoBehaviour
    {
        public SkinnedMeshRenderer source;
        public float tipFlex = 0.16f;       // how far the tips come back toward the archer at full draw
        public float tipPinch = 0.05f;      // fraction the tips move inward at full draw
        public float stringPull = 0.5f;     // nock travel at full draw (bow units)
        public float gripHalf = 0.12f;      // rigid riser half-length

        public Vector3 NockWorld => m_Space ? m_Space.TransformPoint(m_Nock) : transform.position;
        Transform m_Space;
        Vector3 m_Nock;

        Mesh m_Mesh;
        Vector3[] m_Rest, m_Work;
        bool[] m_IsString;
        float m_TipZ, m_TipY, m_StringY;
        float m_Draw = -1f;

        void Awake()
        {
            // A GameObject can't carry two renderers: the bent copy lives on a child with the same space.
            var child = new GameObject("BentBow").transform;
            child.SetParent(source.transform, false);
            child.gameObject.layer = gameObject.layer;
            var mf = child.gameObject.AddComponent<MeshFilter>();
            var mr = child.gameObject.AddComponent<MeshRenderer>();
            m_Space = child;
            mr.sharedMaterials = source.sharedMaterials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Mesh = Instantiate(source.sharedMesh);
            m_Mesh.MarkDynamic();
            mf.sharedMesh = m_Mesh;
            source.enabled = false;

            m_Rest = m_Mesh.vertices;
            m_Work = (Vector3[])m_Rest.Clone();
            m_IsString = new bool[m_Rest.Length];
            int stringBone = -1;
            for (int i = 0; i < source.bones.Length; i++)
                if (source.bones[i] && source.bones[i].name == "String") stringBone = i;
            var weights = source.sharedMesh.boneWeights;
            for (int i = 0; i < m_Rest.Length; i++)
            {
                var w = weights.Length > i ? weights[i] : default;
                m_IsString[i] = stringBone >= 0 && ((w.boneIndex0 == stringBone && w.weight0 > 0.5f) || (w.boneIndex1 == stringBone && w.weight1 > 0.5f));
                if (!m_IsString[i]) m_TipZ = Mathf.Max(m_TipZ, Mathf.Abs(m_Rest[i].z));
            }
            // The string's resting line (it runs tip to tip at a fixed Y).
            float sy = 0f; int n = 0;
            for (int i = 0; i < m_Rest.Length; i++) if (m_IsString[i]) { sy += m_Rest[i].y; n++; }
            m_StringY = n > 0 ? sy / n : 0.3f;
            m_TipY = m_StringY;
            SetDraw(0f);
        }

        float Flex(float z)
        {
            float t = Mathf.Clamp01((Mathf.Abs(z) - gripHalf) / Mathf.Max(0.01f, m_TipZ - gripHalf));
            return t * t;   // a limb bends along its length, most at the tip
        }

        public void SetDraw(float d)
        {
            d = Mathf.Clamp01(d);
            if (Mathf.Abs(d - m_Draw) < 0.002f) return;
            m_Draw = d;
            float tipY = m_TipY + tipFlex * d;
            float tipZ = m_TipZ * (1f - tipPinch * d);
            float nockY = m_StringY + stringPull * d;
            for (int i = 0; i < m_Rest.Length; i++)
            {
                var v = m_Rest[i];
                if (m_IsString[i])
                {
                    // Straight segments from each tip to the nock; keep the string's own thickness.
                    float t = Mathf.Clamp01(Mathf.Abs(v.z) / Mathf.Max(0.01f, m_TipZ));
                    // Fingers round the string off at the nock: a smooth quadratic blend instead of a sharp point.
                    const float w = 0.14f;
                    float f = t >= w ? t : t * t / (2f * w) + w * 0.5f;
                    float lineY = Mathf.Lerp(nockY, tipY, f);
                    v.y = lineY + (v.y - m_StringY);
                    v.z *= 1f - tipPinch * d;
                }
                else
                {
                    float f = Flex(v.z);
                    v.y += tipFlex * d * f;
                    v.z *= 1f - tipPinch * d * f;
                }
                m_Work[i] = v;
            }
            m_Mesh.vertices = m_Work;
            m_Mesh.RecalculateNormals();
            m_Mesh.RecalculateBounds();
            m_Nock = new Vector3(0f, Mathf.Lerp(nockY, tipY, 0.07f), 0f);   // where the rounded string sits at the centre
        }
    }
}
