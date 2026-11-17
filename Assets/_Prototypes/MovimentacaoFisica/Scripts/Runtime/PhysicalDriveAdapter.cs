using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace MordomoProto.Fisica
{
    public enum AdapterState { Idle, Navigating, Paused, Blocked, NoPath, Arrived }

    // Como impedir que o NavMeshAgent avance sozinho (a semântica deve ser medida, não presumida).
    public enum AgentAdvanceMode
    {
        OverwritePose,   // agente livre; sua posição é sobrescrita pela pose física a cada FixedUpdate
        FreezeSpeed,     // agent.speed ~ 0 (o agente quase não avança); steeringTarget continua sendo usado
        StoppedAgent     // agent.isStopped = true o tempo todo; pose física copiada em nextPosition
    }

    // Fase A: o NavMeshAgent é SÓ planejador. Este adaptador lê steeringTarget/remainingDistance, converte em (v, ω),
    // comanda o KinematicDriveBody (FixedUpdate) e copia a pose física para agent.nextPosition.
    // NUNCA chama agent.Move(). O corpo físico é a única fonte da posição real.
    [RequireComponent(typeof(NavMeshAgent), typeof(KinematicDriveBody))]
    [DefaultExecutionOrder(-50)]   // roda antes do KinematicDriveBody.FixedUpdate
    public sealed class PhysicalDriveAdapter : MonoBehaviour
    {
        public NavMeshAgent agent;
        public KinematicDriveBody body;
        // Escolhido empiricamente (Fase A): StoppedAgent -> o agente não avança sozinho e ainda fornece steeringTarget/remainingDistance.
        public AgentAdvanceMode mode = AgentAdvanceMode.StoppedAgent;

        [Header("Política de proa (mesmos valores do controlador atual)")]
        public float turnInPlaceAngle = 65f;
        public float fullSpeedHeadingAngle = 10f;
        public float minTurningSpeedFraction = 0.25f;
        [Tooltip("Ganho proporcional proa -> taxa de giro (1/s); saturado em maxYawRateDeg.")]
        public float yawGain = 3f;

        [Header("Chegada / bloqueio")]
        public float arrivalDistance = 0.05f;
        public float blockedSeconds = 0.5f;

        public AdapterState State { get; private set; }
        public Vector3 Destination { get { return _dest; } }
        public event Action Arrived;

        // Modo integrado (Fase B): quem decide se há navegação ativa é o controlador legado (via ponte). Nesse modo o adaptador
        // não decide chegada/NoPath/Blocked: só segue a rota do agente enquanto o controlador estiver em Navigating.
        public Func<bool> ExternalNavigating;
        public bool ExternallyDriven { get { return ExternalNavigating != null; } }
        public int ForcedStopCount, InterferenceFrames;       // vezes que o controlador soltou isStopped / moveu o transform por cima do corpo
        public float MaxInterferenceMeters, MaxInterferenceDegrees;

        // ---- métricas (medidas pela POSE FÍSICA, não por agent.velocity) ----
        public double MaxRealSpeed, MaxYawRateDegPerSec, MaxLinearAccel;
        public double MaxDivergence, SumDivergence, MaxPostSyncDivergence, MaxStepDisplacement, MaxStepRotationDeg;
        public long Steps, SyncSamples;
        public double TotalAdapterMicros, MaxAdapterMicros;
        public double MeanDivergence { get { return SyncSamples > 0 ? SumDivergence / SyncSamples : 0.0; } }
        public double MeanAdapterMicros { get { return Steps > 0 ? TotalAdapterMicros / Steps : 0.0; } }

        Vector3 _dest; bool _hasDest;
        float _agentSpeed, _blockedTimer, _cmdV, _cmdW;
        bool _paused, _havePrev;
        Vector3 _prevPos; float _prevYaw; double _prevSpeed;

        void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (body == null) body = GetComponent<KinematicDriveBody>();
            agent.updatePosition = false;
            agent.updateRotation = false;
            _agentSpeed = agent.speed;
            ApplyMode();
        }

        // Reset completo (usado pelo reset da variante): reposiciona corpo, agente e estado interno do adaptador.
        public bool HardReset(Vector3 pos, float yawDeg)
        {
            float soma = pos.x + pos.y + pos.z + yawDeg;
            if (float.IsNaN(soma) || float.IsInfinity(soma))
            {
                UnityEngine.Debug.LogWarning("[PhysicalDriveAdapter] HardReset rejeitado: pose não finita (nada foi alterado).");
                return false;
            }
            _hasDest = false; _paused = false; _blockedTimer = 0f; _cmdV = 0f; _cmdW = 0f;
            State = AdapterState.Idle;
            if (agent != null && agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; }
            body.Teleport(pos, yawDeg);
            if (agent != null) agent.Warp(pos);
            transform.SetPositionAndRotation(body.Body.position, body.Body.rotation);
            Physics.SyncTransforms();
            return true;
        }

        void EnforceStopped()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if ((ExternallyDriven || mode == AgentAdvanceMode.StoppedAgent) && !agent.isStopped)
            {
                agent.isStopped = true; ForcedStopCount++;
            }
        }

        // Roda ANTES do Update do controlador (ordem -50): reafirma isStopped para que o controlador não mova nada.
        void Update() { if (ExternallyDriven) EnforceStopped(); }

        // Roda DEPOIS (a ordem de LateUpdate também é -50, mas o controlador não usa LateUpdate): desfaz qualquer interferência.
        void LateUpdate()
        {
            if (!ExternallyDriven) return;
            EnforceStopped();
            Vector3 p = body.Position; Quaternion q = body.Rotation;
            float dPos = Horizontal(transform.position, p);
            float dRot = Quaternion.Angle(transform.rotation, q);
            if (dPos > 0.03f || dRot > 3f)
            {
                InterferenceFrames++;
                if (dPos > MaxInterferenceMeters) MaxInterferenceMeters = dPos;
                if (dRot > MaxInterferenceDegrees) MaxInterferenceDegrees = dRot;
                var rb = body.Body; transform.SetPositionAndRotation(rb.position, rb.rotation);
            }
            if (agent.isOnNavMesh && Horizontal(agent.nextPosition, p) > 0.001f)
                agent.nextPosition = new Vector3(p.x, agent.nextPosition.y, p.z);
        }

        void ApplyMode()
        {
            agent.speed = mode == AgentAdvanceMode.FreezeSpeed ? 0.01f : _agentSpeed;
            agent.isStopped = _paused || mode == AgentAdvanceMode.StoppedAgent;
        }

        public void SetMode(AgentAdvanceMode m) { mode = m; ApplyMode(); }

        public bool EnsureOnNavMesh()
        {
            if (agent.isOnNavMesh) return true;
            return agent.Warp(body.Position) || agent.isOnNavMesh;
        }

        public bool SetDestination(Vector3 d)
        {
            if (!EnsureOnNavMesh()) { State = AdapterState.NoPath; return false; }
            _dest = d; _hasDest = true; _blockedTimer = 0f; _paused = false;
            ApplyMode();
            bool ok = agent.SetDestination(d);
            State = ok ? AdapterState.Navigating : AdapterState.NoPath;
            return ok;
        }

        // Parada deliberada: limpa a rota; v,ω -> 0 pela rampa de frenagem do modelo.
        public void Stop()
        {
            _hasDest = false; _paused = false; _blockedTimer = 0f;
            if (agent.isOnNavMesh) agent.ResetPath();
            State = AdapterState.Idle;
        }

        public bool Resume()
        {
            if (!_hasDest) return false;
            return SetDestination(_dest);
        }

        public void SetPaused(bool p)
        {
            _paused = p;
            if (_hasDest) State = p ? AdapterState.Paused : AdapterState.Navigating;
            ApplyMode();
        }

        static float Horizontal(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

        void FixedUpdate()
        {
            long t0 = Stopwatch.GetTimestamp();
            float dt = Time.fixedDeltaTime;
            Vector3 pos = body.Position;
            float yaw = (float)(body.Pose.Yaw * Mathf.Rad2Deg);

            // --- métricas de passo (pose física anterior -> atual) ---
            if (_havePrev && dt > 0f)
            {
                float disp = Horizontal(pos, _prevPos);
                double speed = disp / dt;
                float dyaw = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw));
                if (speed > MaxRealSpeed) MaxRealSpeed = speed;
                if (dyaw / dt > MaxYawRateDegPerSec) MaxYawRateDegPerSec = dyaw / dt;
                double acc = Math.Abs(speed - _prevSpeed) / dt; if (Steps > 2 && acc > MaxLinearAccel) MaxLinearAccel = acc;
                if (disp > MaxStepDisplacement) MaxStepDisplacement = disp;
                if (dyaw > MaxStepRotationDeg) MaxStepRotationDeg = dyaw;
                _prevSpeed = speed;
            }
            _prevPos = pos; _prevYaw = yaw; _havePrev = true;

            // --- sincroniza o agente com a pose física (sem agent.Move) ---
            if (agent.isOnNavMesh)
            {
                Vector3 np = agent.nextPosition;
                float div = Horizontal(np, pos);
                if (div > MaxDivergence) MaxDivergence = div;
                SumDivergence += div; SyncSamples++;
                agent.nextPosition = new Vector3(pos.x, np.y, pos.z);
                float post = Horizontal(agent.nextPosition, pos);
                if (post > MaxPostSyncDivergence) MaxPostSyncDivergence = post;
            }

            ComputeCommand(pos, yaw, dt);
            body.SetCommand(_cmdV, _cmdW);

            double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
            TotalAdapterMicros += us; Steps++;
            if (us > MaxAdapterMicros) MaxAdapterMicros = us;
        }

        public int RemainingFallbackFrames;   // quadros em que remainingDistance veio infinito e foi calculado pelos cantos

        float RemainingAlongCorners(Vector3 pos)
        {
            var c = agent.path.corners;
            if (c == null || c.Length == 0) return float.PositiveInfinity;
            Vector3 st = agent.steeringTarget;
            int i = 0; float best = float.MaxValue;
            for (int k = 0; k < c.Length; k++) { float d = Horizontal(c[k], st); if (d < best) { best = d; i = k; } }
            float rem = Horizontal(pos, c[i]);
            for (int k = i; k + 1 < c.Length; k++) rem += Horizontal(c[k], c[k + 1]);
            return rem;
        }

        void ComputeCommand(Vector3 pos, float yawDeg, float dt)
        {
            if (ExternallyDriven)
            {
                bool act = ExternalNavigating() && agent.isOnNavMesh && agent.hasPath;
                State = act ? AdapterState.Navigating : AdapterState.Idle;
                if (!act) { _cmdV = 0f; _cmdW = 0f; return; }
            }
            else if (!_hasDest || _paused || State == AdapterState.NoPath || State == AdapterState.Blocked)
            { _cmdV = 0f; _cmdW = 0f; return; }
            if (!agent.isOnNavMesh) { _cmdV = 0f; _cmdW = 0f; return; }
            if (agent.pathPending) return;   // replanejando: mantém o comando anterior (sem solavanco)

            if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            { if (!ExternallyDriven) State = AdapterState.NoPath; _cmdV = 0f; _cmdW = 0f; return; }

            float remaining = agent.remainingDistance;
            if (float.IsInfinity(remaining) || float.IsNaN(remaining))
            {
                // Com isStopped=true o agente mantém caminho/steeringTarget, mas em caminhos longos remainingDistance pode vir +Infinito.
                // Medido na cópia do bairro (Sala->Jantar). Nesse caso a distância restante é calculada pelos cantos do caminho.
                remaining = RemainingAlongCorners(pos);
                RemainingFallbackFrames++;
                if (float.IsInfinity(remaining) || float.IsNaN(remaining)) { _cmdV = 0f; _cmdW = 0f; return; }
            }

            if (!ExternallyDriven && remaining <= arrivalDistance && Math.Abs(body.LinearSpeed) < 0.02f)
            {
                _hasDest = false; State = AdapterState.Arrived; _cmdV = 0f; _cmdW = 0f;
                if (agent.isOnNavMesh) agent.ResetPath();
                var h = Arrived; if (h != null) h();
                return;
            }

            Vector3 t = agent.steeringTarget;
            Vector3 to = new Vector3(t.x - pos.x, 0f, t.z - pos.z);
            float maxYaw = body.config.maxYawRateDeg * Mathf.Deg2Rad;
            float vmax = body.config.maxSpeed;

            float v = 0f, w = 0f;
            if (to.sqrMagnitude > 1e-6f && remaining > arrivalDistance * 2f)
            {
                Vector3 fwd = new Vector3(Mathf.Sin(yawDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(yawDeg * Mathf.Deg2Rad));
                float err = Vector3.SignedAngle(fwd, to.normalized, Vector3.up);   // + = alvo à direita
                float aerr = Mathf.Abs(err);
                w = Mathf.Clamp(err * Mathf.Deg2Rad * yawGain, -maxYaw, maxYaw);
                if (aerr < turnInPlaceAngle)
                {
                    float al = Mathf.InverseLerp(turnInPlaceAngle, fullSpeedHeadingAngle, aerr);
                    v = vmax * Mathf.Lerp(minTurningSpeedFraction, 1f, al);
                }
            }
            else v = vmax * 0.25f;   // reta final: segue só com o perfil de frenagem abaixo

            // perfil de frenagem até o destino
            float stopDist = Mathf.Max(0f, remaining - 0.01f);
            v = Mathf.Min(v, Mathf.Sqrt(2f * body.config.braking * 0.8f * stopDist));

            // bloqueio físico sustentado -> Blocked (parada segura; sem forçar)
            if (body.ContactStopped && (v > 0.05f || Mathf.Abs(w) > 0.1f)) _blockedTimer += dt; else _blockedTimer = 0f;
            if (!ExternallyDriven && _blockedTimer >= blockedSeconds) { State = AdapterState.Blocked; v = 0f; w = 0f; }

            _cmdV = v; _cmdW = w;
        }
    }
}
