using System.Collections.Generic;
using UnityEngine;

namespace MordomoProto.Fisica
{
    public sealed class CircuitInfo
    {
        public string Name;
        public float Width;
        public Vector3 Origin;
        public Vector3 StartPosition;
        public float StartYawDeg;
        public List<Vector2> Path = new List<Vector2>();   // (x, z) da linha de centro até o ponto de entrega
        public Vector2 Goal;
        public Vector2 RoomCenter;
        public Bounds DoorZone, CurveZone, RoomZone;       // zonas (XZ) para registrar folga
        public Collider Obstacle;
        public float ObstacleZMin, ObstacleZMax, BypassX;
        public float ArcStartZ;
        public Transform Root;
    }

    // Geometria do circuito isolado: corredor reto (3,0 m) -> curva de 90° à direita -> corredor reto (3,0 m)
    // -> porta de 1,20 m -> sala 3,0 x 3,0 m com o ponto de entrega no centro.
    public static class CircuitBuilder
    {
        public const float Leg1Length = 3.0f;
        public const float Leg2Length = 3.0f;
        public const float DoorWidth = 1.20f;
        public const float WallThickness = 0.2f;
        public const float WallHeight = 2.0f;

        static bool _make = true;
        static GameObject Box(string name, Transform parent, float minX, float maxX, float minZ, float maxZ,
                              float y0, float y1, Material mat)
        {
            if (!_make) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((minX + maxX) * 0.5f, (y0 + y1) * 0.5f, (minZ + maxZ) * 0.5f);
            go.transform.localScale = new Vector3(maxX - minX, y1 - y0, maxZ - minZ);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public static CircuitInfo Build(Transform parent, Vector3 origin, float width, bool withObstacle,
                                        Material wallMat = null, Material floorMat = null, Material obstacleMat = null,
                                        string name = null, bool createGeometry = true)
        {
            var info = new CircuitInfo();
            info.Name = name ?? ("Circuito_" + width.ToString("F2"));
            info.Width = width;
            info.Origin = origin;
            _make = createGeometry;
            Transform root = null;
            if (createGeometry) { root = new GameObject(info.Name).transform; root.SetParent(parent, false); }
            info.Root = root;

            float W = width, ox = origin.x, oz = origin.z, t = WallThickness, H = WallHeight, back = 1.0f;
            float hw = W * 0.5f;
            float zc = oz + Leg1Length + hw;                 // linha de centro do trecho 2
            float xEnd = ox + hw + Leg2Length;               // plano da parede da porta
            const float doorT = 0.15f, roomLen = 3.0f, roomHalf = 1.5f;
            float openHalf = DoorWidth * 0.5f;
            float xr = xEnd + doorT + roomLen;

            Box("Piso", root, ox - hw - 3f, xr + 3f, oz - back - 3f, zc + roomHalf + 3f, -0.2f, 0f, floorMat);
            Box("Parede_Fundo", root, ox - hw - t, ox + hw + t, oz - back - t, oz - back, 0f, H, wallMat);
            Box("Parede_Externa_T1", root, ox - hw - t, ox - hw, oz - back, zc + hw + t, 0f, H, wallMat);
            Box("Parede_Externa_T2", root, ox - hw - t, xEnd, zc + hw, zc + hw + t, 0f, H, wallMat);
            Box("Parede_Interna_T1", root, ox + hw, ox + hw + t, oz - back, oz + Leg1Length, 0f, H, wallMat);
            Box("Parede_Interna_T2", root, ox + hw, xEnd, oz + Leg1Length - t, oz + Leg1Length, 0f, H, wallMat);
            Box("Porta_Inferior", root, xEnd, xEnd + doorT, zc - roomHalf - t, zc - openHalf, 0f, H, wallMat);
            Box("Porta_Superior", root, xEnd, xEnd + doorT, zc + openHalf, zc + roomHalf + t, 0f, H, wallMat);
            Box("Sala_Superior", root, xEnd + doorT, xr + t, zc + roomHalf, zc + roomHalf + t, 0f, H, wallMat);
            Box("Sala_Inferior", root, xEnd + doorT, xr + t, zc - roomHalf - t, zc - roomHalf, 0f, H, wallMat);
            Box("Sala_Fundo", root, xr, xr + t, zc - roomHalf - t, zc + roomHalf + t, 0f, H, wallMat);

            // marcador visual do ponto de entrega (sem collider)
            float roomCx = xEnd + doorT + roomLen * 0.5f;
            if (createGeometry)
            {
                var mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mark.name = "Ponto_Entrega";
                Object.DestroyImmediate(mark.GetComponent<Collider>());
                mark.transform.SetParent(root, false);
                mark.transform.position = new Vector3(roomCx, 0.005f, zc);
                mark.transform.localScale = new Vector3(0.5f, 0.005f, 0.5f);
                if (obstacleMat != null) mark.GetComponent<MeshRenderer>().sharedMaterial = obstacleMat;
            }

            info.ObstacleZMin = oz + 1.6f; info.ObstacleZMax = oz + 1.9f;
            if (withObstacle)
            {
                float obsMaxX = ox - hw + 0.4f;
                var o = Box("Obstaculo", root, ox - hw, obsMaxX, info.ObstacleZMin, info.ObstacleZMax, 0f, 0.5f, obstacleMat);
                if (o != null) info.Obstacle = o.GetComponent<Collider>();
                info.BypassX = (obsMaxX + (ox + hw)) * 0.5f;
            }

            // trajetória de referência (linha de centro)
            float R = hw;
            info.ArcStartZ = zc - R;
            var pts = info.Path;
            for (float z = oz; z < zc - R; z += 0.05f) pts.Add(new Vector2(ox, z));
            Vector2 c = new Vector2(ox + R, zc - R);
            int n = Mathf.Max(8, Mathf.CeilToInt(R * Mathf.PI * 0.5f / 0.02f));
            for (int i = 0; i <= n; i++)
            {
                float phi = Mathf.PI - (Mathf.PI * 0.5f) * i / n;
                pts.Add(new Vector2(c.x + R * Mathf.Cos(phi), c.y + R * Mathf.Sin(phi)));
            }
            for (float x = ox + R + 0.05f; x < roomCx; x += 0.05f) pts.Add(new Vector2(x, zc));
            pts.Add(new Vector2(roomCx, zc));

            info.Goal = new Vector2(roomCx, zc);
            info.RoomCenter = info.Goal;
            info.StartPosition = new Vector3(ox, 0f, oz);
            info.StartYawDeg = 0f;
            info.DoorZone = new Bounds(new Vector3((xEnd + doorT * 0.5f), 0.5f, zc), new Vector3(0.8f + doorT + 0.65f, 1f, 2.0f));
            info.CurveZone = new Bounds(new Vector3(ox + 0.25f, 0.5f, oz + Leg1Length + hw * 0.5f + 0.1f), new Vector3(W + 0.5f, 1f, W + 1.0f));
            info.RoomZone = new Bounds(new Vector3(roomCx, 0.5f, zc), new Vector3(roomLen, 1f, 2f * roomHalf));
            return info;
        }

        // Rota de desvio: sai da pose atual, passa pela folga ao lado do obstáculo e reencontra a linha de centro.
        public static List<Vector2> BuildBypass(CircuitInfo info, Vector2 from)
        {
            float ox = info.Origin.x;
            var list = new List<Vector2>();
            list.Add(from);
            list.Add(new Vector2(info.BypassX, info.ObstacleZMin + 0.15f));
            list.Add(new Vector2(info.BypassX, info.ObstacleZMax + 0.55f));
            float zRejoin = Mathf.Min(info.ObstacleZMax + 0.9f, info.ArcStartZ - 0.2f);
            list.Add(new Vector2(ox, zRejoin));
            int idx = info.Path.FindIndex(p => p.y > zRejoin + 0.05f || p.x > ox + 0.001f);
            if (idx < 0) idx = info.Path.Count - 1;
            for (int i = idx; i < info.Path.Count; i++) list.Add(info.Path[i]);
            // densifica para o seguidor
            var dense = new List<Vector2>();
            for (int i = 0; i < list.Count - 1; i++)
            {
                Vector2 a = list[i], b = list[i + 1];
                float len = Vector2.Distance(a, b);
                int k = Mathf.Max(1, Mathf.CeilToInt(len / 0.05f));
                for (int j = 0; j < k; j++) dense.Add(Vector2.Lerp(a, b, j / (float)k));
            }
            dense.Add(list[list.Count - 1]);
            return dense;
        }
    }
}
