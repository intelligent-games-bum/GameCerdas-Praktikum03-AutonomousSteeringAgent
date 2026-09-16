using UnityEngine;

/// <summary>
/// Menampilkan gizmos untuk membantu praktikan memahami behavior yang sedang aktif:
/// - Arah gerak & desired direction (Seek/Arrive)
/// - Lingkaran wander
/// - Ray sensor obstacle (hijau = aman, merah = terdeteksi)
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

        // Velocity (arah gerak aktual) - biru
        if (showVelocity && Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pos, pos + agent.Velocity);
        }

        // Desired direction (arah yang "diinginkan" oleh behavior aktif) - kuning
        if (showDesiredDirection && Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos, pos + agent.DesiredDirection * 2f);
        }

        // Lingkaran wander - magenta, hanya tampil saat state = Wander
        if (showWanderCircle && Application.isPlaying && agent.CurrentState == SteeringAgent.SteeringState.Wander)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(agent.WanderCirclePosition, agent.WanderRadius);
            Gizmos.DrawLine(pos, agent.WanderCirclePosition);
        }

        // Ray sensor obstacle
        if (showSensorRays && sensor != null && Application.isPlaying)
        {
            DrawRay(sensor.RayOrigin, sensor.ForwardDir, sensor.RayLength, sensor.CenterHit);
            DrawRay(sensor.RayOrigin, sensor.LeftDir, sensor.RayLength, sensor.LeftHit);
            DrawRay(sensor.RayOrigin, sensor.RightDir, sensor.RayLength, sensor.RightHit);
        }
    }

    private void DrawRay(Vector3 origin, Vector3 direction, float length, bool hit)
    {
        Gizmos.color = hit ? Color.red : Color.green;
        Gizmos.DrawLine(origin, origin + direction.normalized * length);
    }
}
