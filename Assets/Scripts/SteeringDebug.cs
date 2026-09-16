using UnityEngine;

/// <summary>
/// Menampilkan gizmos untuk membantu praktikan memahami behavior yang sedang aktif:
/// - Arah gerak & desired direction (Seek/Arrive)
/// - Lingkaran wander
/// - Ray sensor obstacle (hijau = aman, merah = terdeteksi)
/// - Batas area wander
/// Pasang di GameObject yang sama dengan SteeringAgent & SteeringSensor.
/// </summary>
[RequireComponent(typeof(SteeringAgent))]
public class SteeringDebug : MonoBehaviour
{
    [Header("Toggle Visualisasi")]
    [SerializeField] private bool showVelocity = true;
    [SerializeField] private bool showDesiredDirection = true;
    [SerializeField] private bool showWanderCircle = true;
    [SerializeField] private bool showSensorRays = true;
    [SerializeField] private bool showBounds = true;
    [SerializeField] private bool showAvoidanceForce = true;
    [SerializeField] private bool showLedgeProbes = true;
    [Tooltip("Tulisan nama state (Seek/Arrive/Wander) di atas kepala agent.")]
    [SerializeField] private bool showStateLabel = true;

    private SteeringAgent agent;
    private SteeringSensor sensor;

    private void Awake()
    {
        agent = GetComponent<SteeringAgent>();
        sensor = GetComponent<SteeringSensor>();
    }

    private void OnDrawGizmos()
    {
        if (agent == null) agent = GetComponent<SteeringAgent>();
        if (sensor == null) sensor = GetComponent<SteeringSensor>();

        Vector3 pos = transform.position;

        // Batas area wander - putih, tampil juga saat edit mode agar mudah diatur
        if (showBounds && agent != null && agent.UseBounds)
        {
            Gizmos.color = Color.white;
            Vector3 center = Application.isPlaying ? agent.BoundsCenter : pos;
            Gizmos.DrawWireSphere(center, agent.BoundsRadius);
        }

        if (!Application.isPlaying || agent == null) return;

        // Nama state di atas kepala agent, seperti gizmo milik NPCBrain
        if (showStateLabel)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 2.2f, agent.CurrentState.ToString());
#endif
        }

        // Garis ke target yang sedang dikejar - merah
        if (agent.CurrentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(pos, agent.CurrentTarget.position);
        }

        // Lingkaran badan untuk penahan tabrakan. Merah = sedang menempel tembok.
        // Kalau lingkaran ini tidak pernah berubah merah saat menabrak, berarti
        // Collision Layer belum terisi atau blockOnObstacles mati.
        Gizmos.color = agent.IsBlocked ? Color.red : Color.gray;
        Gizmos.DrawWireSphere(pos + Vector3.up * agent.CollisionHeightOffset, agent.BodyRadius);

        // Velocity (arah gerak aktual) - biru
        if (showVelocity)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pos, pos + agent.Velocity);
        }

        // Desired direction (arah yang "diinginkan" oleh behavior aktif) - kuning
        if (showDesiredDirection)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos, pos + agent.DesiredDirection * 2f);
        }

        // Lingkaran wander - magenta, hanya tampil saat state = Wander
        if (showWanderCircle && agent.CurrentState == SteeringAgent.SteeringState.Wander)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(agent.WanderCirclePosition, agent.WanderRadius);
            Gizmos.DrawLine(pos, agent.WanderCirclePosition);
        }

        if (sensor == null) return;

        // Ray sensor obstacle (mengikuti arah gerak, bukan arah hadap)
        if (showSensorRays)
        {
            DrawRay(sensor.LastOrigin, sensor.LastForwardDir, sensor.SensorDistance, sensor.CenterHit);
            DrawRay(sensor.LastOrigin, sensor.LastLeftDir, sensor.SensorDistance, sensor.LeftHit);
            DrawRay(sensor.LastOrigin, sensor.LastRightDir, sensor.SensorDistance, sensor.RightHit);
        }

        // Titik cek tanah - bola hijau (ada tanah) / merah (jurang)
        if (showLedgeProbes && sensor.DetectLedges)
        {
            DrawProbe(sensor.ForwardGroundPoint, sensor.ForwardGroundOk);
            DrawProbe(sensor.LeftGroundPoint, sensor.LeftGroundOk);
            DrawProbe(sensor.RightGroundPoint, sensor.RightGroundOk);
        }

        // Gaya menghindar hasil akhir - oranye
        if (showAvoidanceForce && sensor.LastAvoidanceForce.sqrMagnitude > 0.0001f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(sensor.LastOrigin, sensor.LastOrigin + sensor.LastAvoidanceForce.normalized * 2f);
        }
    }

    private void DrawProbe(Vector3 point, bool grounded)
    {
        Gizmos.color = grounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(point, 0.25f);
    }

    private void DrawRay(Vector3 origin, Vector3 direction, float length, bool hit)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        Gizmos.color = hit ? Color.red : Color.green;
        Gizmos.DrawLine(origin, origin + direction.normalized * length);
    }
}
