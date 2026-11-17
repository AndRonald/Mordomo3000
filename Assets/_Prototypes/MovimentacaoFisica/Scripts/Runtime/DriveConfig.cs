using System;
using UnityEngine;

namespace MordomoProto.Fisica
{
    [Serializable]
    public class DriveConfig
    {
        [Header("Geometria")]
        public float track = 0.483f;
        public float wheelRadius = 0.14f;

        [Header("χ — multiplicador da bitola efetiva (parâmetro, NÃO calibrado para robô real)")]
        [Tooltip("χ da planta: como o corpo realmente gira. 1 = diferencial ideal.")]
        public float chi = 1f;
        [Tooltip("χ assumido pelo controlador ao converter (v, yawRate) em velocidades de roda.")]
        public float controllerChi = 1f;

        [Header("Limites (valores atuais do RobotNavigationController)")]
        public float maxSpeed = 0.6f;
        public float maxWheelSpeed = 0.6f;
        public float acceleration = 0.7f;
        public float braking = 1.0f;
        public float maxYawRateDeg = 55f;

        public DriveParameters ToParameters()
        {
            return new DriveParameters
            {
                Track = track,
                WheelRadius = wheelRadius,
                Chi = chi,
                ControllerChi = controllerChi,
                MaxSpeed = maxSpeed,
                MaxWheelSpeed = maxWheelSpeed,
                Acceleration = acceleration,
                Braking = braking,
                MaxYawRate = maxYawRateDeg * Math.PI / 180.0
            };
        }

        public DriveConfig Clone() { return (DriveConfig)MemberwiseClone(); }
    }
}
