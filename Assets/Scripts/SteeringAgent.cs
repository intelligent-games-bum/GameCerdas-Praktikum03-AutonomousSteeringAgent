using UnityEngine;

/// <summary>
/// Agent dengan behavior Seek / Arrive / Wander.
/// - Jika ada target -> Seek/Arrive menuju target.
/// - Jika tidak ada target -> Wander (bergerak acak yang halus).
/// - Selalu menghadap arah gerak.
/// - Menggabungkan avoidance vector dari SteeringSensor (jika ada).
/// </summary>
[RequireComponent(typeof(SteeringSensor))]
public class SteeringAgent : MonoBehaviour
{
    public enum SteeringState { Seek, Arrive, Wander }

    [Header("Target")]
    [Tooltip("Kosongkan (null) agar agent otomatis Wander.")]
    [SerializeField] private Transform target;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float maxForce = 10f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Arrive Settings")]
    [Tooltip("Jarak mulai melambat menuju target.")]
    [SerializeField] private float slowingRadius = 3f;
    [Tooltip("Jarak dianggap sudah sampai.")]
    [SerializeField] private float arrivalThreshold = 0.2f;

    [Header("Wander Settings")]
    [SerializeField] private float wanderRadius = 3f;
    [SerializeField] private float wanderDistance = 5f;
    [SerializeField] private float wanderJitter = 1f;

    // State internal
    private Vector3 velocity;
    private Vector3 wanderTarget;
    private SteeringSensor sensor;

    public SteeringState CurrentState { get; private set; }
    public Vector3 Velocity => velocity;
    public Vector3 DesiredDirection { get; private set; }
    public Vector3 WanderCirclePosition { get; private set; }
    public float WanderRadius => wanderRadius;

    private void Awake()
    {
        sensor = GetComponent<SteeringSensor>();

        // Inisialisasi titik wander awal secara acak di sekitar agent
        Vector2 randomPoint = Random.insideUnitCircle.normalized * wanderRadius;
        wanderTarget = new Vector3(randomPoint.x, 0f, randomPoint.y);
    }

    private void Update()
    {
        Vector3 steeringForce;

        if (target == null)
        {
            CurrentState = SteeringState.Wander;
            steeringForce = Wander();
        }
        else
        {
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance <= slowingRadius)
            {
                CurrentState = SteeringState.Arrive;
                steeringForce = Arrive(target.position);
            }
            else
            {
                CurrentState = SteeringState.Seek;
                steeringForce = Seek(target.position);
            }
        }

        // Tambahkan gaya menghindar obstacle (jika sensor mendeteksi sesuatu)
        Vector3 avoidance = sensor != null ? sensor.GetAvoidanceForce() : Vector3.zero;
        steeringForce += avoidance;

        steeringForce = Vector3.ClampMagnitude(steeringForce, maxForce);

        // Integrasi kecepatan
        velocity += steeringForce * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        // Jangan bergerak kalau sudah sangat dekat target (khusus Arrive)
        if (CurrentState == SteeringState.Arrive &&
            Vector3.Distance(transform.position, target.position) < arrivalThreshold)
        {
            velocity = Vector3.zero;
        }

        transform.position += velocity * Time.deltaTime;

        // Menghadap arah gerak
        if (velocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Bergerak lurus secepat mungkin menuju target.
    /// </summary>
    private Vector3 Seek(Vector3 targetPosition)
    {
        Vector3 desired = (targetPosition - transform.position).normalized * maxSpeed;
        DesiredDirection = desired.normalized;
        return desired - velocity;
    }

    /// <summary>
    /// Seperti Seek, tapi melambat secara halus saat mendekati target (slowingRadius).
    /// </summary>
    private Vector3 Arrive(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;

        float speed = maxSpeed;
        if (distance < slowingRadius)
        {
            speed = maxSpeed * (distance / slowingRadius);
        }

        Vector3 desired = toTarget.normalized * speed;
        DesiredDirection = desired.normalized;
        return desired - velocity;
    }

    /// <summary>
    /// Bergerak acak yang halus menggunakan lingkaran proyeksi di depan agent.
    /// </summary>
    private Vector3 Wander()
    {
        // Geser titik wander secara acak kecil (jitter) tiap frame
        wanderTarget += new Vector3(
            Random.Range(-1f, 1f) * wanderJitter,
            0f,
            Random.Range(-1f, 1f) * wanderJitter) * Time.deltaTime;

        wanderTarget = wanderTarget.normalized * wanderRadius;

        // Proyeksikan lingkaran wander di depan arah gerak agent saat ini
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
        Vector3 circleCenter = forward * wanderDistance;
        Vector3 targetOnCircle = circleCenter + wanderTarget;

        WanderCirclePosition = transform.position + circleCenter;
        DesiredDirection = targetOnCircle.normalized;

        Vector3 desired = targetOnCircle.normalized * maxSpeed;
        return desired - velocity;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
