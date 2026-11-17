using UnityEngine;

namespace MordomoProto.Fisica
{
    // Consultas físicas (varredura, sobreposição, penetração, folga) usando o collider REAL do robô
    // (BoxCollider 0,55 x 1,10 x 0,65 m). Não depende de callbacks de colisão.
    //
    // Observação sobre a altura: a consulta ignora os 2 cm inferiores (FloorSkin) e colliders cujo
    // topo esteja abaixo disso, apenas para não tratar o piso como obstáculo. As dimensões
    // horizontais do collider NÃO são reduzidas (Margin só pode aumentá-las).
    public sealed class FootprintProbe
    {
        public readonly BoxCollider Box;
        public readonly Transform Owner;
        public float FloorSkin = 0.02f;
        public float Margin = 0f;
        public Collider LastBlocker { get; private set; }

        readonly Collider[] _buf = new Collider[64];
        readonly Collider[] _cand = new Collider[64];
        readonly RaycastHit[] _hits = new RaycastHit[32];

        public FootprintProbe(BoxCollider box, Transform owner)
        {
            Box = box; Owner = owner;
        }

        public Vector3 HalfExtents
        {
            get
            {
                Vector3 s = Vector3.Scale(Box.size, Box.transform.lossyScale);
                return new Vector3(s.x * 0.5f + Margin, (s.y - FloorSkin) * 0.5f, s.z * 0.5f + Margin);
            }
        }

        public Vector3 CenterLocal
        {
            get
            {
                Vector3 c = Vector3.Scale(Box.center, Box.transform.lossyScale);
                return new Vector3(c.x, c.y + FloorSkin * 0.5f, c.z);
            }
        }

        public bool Valid(Collider c)
        {
            return c != null && c != Box && !c.isTrigger && !c.transform.IsChildOf(Owner)
                   && c.bounds.max.y > FloorSkin;
        }

        public bool OverlapsAt(Vector3 pos, Quaternion rot)
        {
            int n = Physics.OverlapBoxNonAlloc(pos + rot * CenterLocal, HalfExtents, _buf, rot,
                                               ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (Valid(_buf[i])) { LastBlocker = _buf[i]; return true; }
            }
            return false;
        }

        // Maior fração [0,1] do movimento p0->p1 (e q0->q1) que não causa sobreposição.
        // Varre a translação com BoxCast e testa sobreposição em sub-passos (rotação inclusa).
        public float SafeFraction(Vector3 p0, Quaternion q0, Vector3 p1, Quaternion q1,
                                  float maxLinearStep, float maxAngularStepDeg, float contactSkin)
        {
            float dist = (p1 - p0).magnitude;
            // Quaternion.Angle perde precisão (float) para giros < ~0,05°; usa também a diferença de yaw.
            float dyaw = Mathf.Abs(Mathf.DeltaAngle(q0.eulerAngles.y, q1.eulerAngles.y));
            float ang = Mathf.Max(Quaternion.Angle(q0, q1), dyaw);
            if (dist < 1e-9f && ang < 1e-6f) return 1f;
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(dist / maxLinearStep, ang / maxAngularStepDeg)), 1, 64);
            Vector3 prevP = p0; Quaternion prevQ = q0;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = Vector3.Lerp(p0, p1, t);
                Quaternion q = Quaternion.Slerp(q0, q1, t);
                Vector3 seg = p - prevP;
                float segLen = seg.magnitude;
                if (segLen > 1e-5f)
                {
                    int cnt = Physics.BoxCastNonAlloc(prevP + prevQ * CenterLocal, HalfExtents, seg / segLen,
                                                      _hits, prevQ, segLen, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.MaxValue;
                    for (int j = 0; j < cnt; j++)
                    {
                        RaycastHit h = _hits[j];
                        if (!Valid(h.collider)) continue;
                        if (h.distance <= 0f && h.point == Vector3.zero) continue; // já sobreposto no início
                        if (h.distance < best) { best = h.distance; LastBlocker = h.collider; }
                    }
                    if (best < float.MaxValue)
                    {
                        float frac = ((i - 1) + Mathf.Max(0f, best - contactSkin) / segLen) / n;
                        return Mathf.Clamp01(frac);
                    }
                }
                if (OverlapsAt(p, q)) return (i - 1) / (float)n;
                prevP = p; prevQ = q;
            }
            return 1f;
        }

        // Rotação pura: o arco de totalDegrees (com o sinal) é livre? (equivalente ao ArcIsClear do controlador atual)
        public bool ArcIsClear(Vector3 pos, Quaternion rot, float totalDegrees, float stepDeg = 2f)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(totalDegrees) / stepDeg));
            for (int i = 1; i <= n; i++)
            {
                Quaternion q = rot * Quaternion.Euler(0f, totalDegrees * i / n, 0f);
                if (OverlapsAt(pos, q)) return false;
            }
            return true;
        }

        // Penetração máxima (m) entre o collider real e os obstáculos, via ComputePenetration.
        public float MeasurePenetration(Vector3 pos, Quaternion rot)
        {
            Vector3 center = pos + rot * Vector3.Scale(Box.center, Box.transform.lossyScale);
            Vector3 half = Vector3.Scale(Box.size, Box.transform.lossyScale) * 0.5f;
            int n = Physics.OverlapBoxNonAlloc(center, half, _buf, rot, ~0, QueryTriggerInteraction.Ignore);
            float max = 0f;
            for (int i = 0; i < n; i++)
            {
                Collider c = _buf[i];
                if (!Valid(c)) continue;
                Vector3 dir; float d;
                if (Physics.ComputePenetration(Box, pos, rot, c, c.transform.position, c.transform.rotation, out dir, out d))
                    if (d > max) max = d;
            }
            return max;
        }

        // Menor distância (m) entre o perímetro da pegada (altura média da carroceria) e qualquer obstáculo.
        public float MeasureClearance(Vector3 pos, Quaternion rot, float searchRadius = 1.0f)
        {
            Vector3 hx = HalfExtents;
            Vector3 big = new Vector3(hx.x + searchRadius, hx.y, hx.z + searchRadius);
            int n = Physics.OverlapBoxNonAlloc(pos + rot * CenterLocal, big, _buf, rot, ~0, QueryTriggerInteraction.Ignore);
            int m = 0;
            for (int i = 0; i < n; i++) if (Valid(_buf[i])) _cand[m++] = _buf[i];
            if (m == 0) return float.PositiveInfinity;

            Vector3 s = Vector3.Scale(Box.size, Box.transform.lossyScale);
            float hxs = s.x * 0.5f, hzs = s.z * 0.5f;
            float ly = 0.25f;
            float best = float.PositiveInfinity;
            const float step = 0.05f;
            int nx = Mathf.CeilToInt(2f * hxs / step), nz = Mathf.CeilToInt(2f * hzs / step);
            for (int i = 0; i <= nx; i++)
            {
                float x = -hxs + 2f * hxs * i / nx;
                best = Mathf.Min(best, Sample(pos, rot, new Vector3(x, ly, hzs), m));
                best = Mathf.Min(best, Sample(pos, rot, new Vector3(x, ly, -hzs), m));
            }
            for (int k = 1; k < nz; k++)
            {
                float z = -hzs + 2f * hzs * k / nz;
                best = Mathf.Min(best, Sample(pos, rot, new Vector3(hxs, ly, z), m));
                best = Mathf.Min(best, Sample(pos, rot, new Vector3(-hxs, ly, z), m));
            }
            return best;
        }

        float Sample(Vector3 pos, Quaternion rot, Vector3 local, int m)
        {
            Vector3 w = pos + rot * local;
            float best = float.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                Vector3 cp = _cand[i].ClosestPoint(w);
                float d = (cp - w).magnitude;
                if (d < best) best = d;
            }
            return best;
        }
    }
}
