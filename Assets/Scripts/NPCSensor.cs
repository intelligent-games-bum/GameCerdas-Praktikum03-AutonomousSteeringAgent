using UnityEngine;

public class NPCSensor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform directionMarker;

    [Header("Sensor Parameters")]
    [SerializeField] [Min(0f)] private float detectionRadius = 10f;
    [SerializeField] [Range(0f, 360f)] private float fieldOfViewAngle = 90f;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float eyeHeight = 1f;

    [Header("Debug (read only)")]
    [SerializeField] private float currentDistance;
    [SerializeField] private bool withinRadius;
    [SerializeField] private bool withinFov;
    [SerializeField] private bool hasLineOfSight;
    [SerializeField] private bool canSeePlayer;

    [Header("Gizmos")]
    [SerializeField] private bool alwaysDrawGizmos = true;

    public bool CanSeePlayer => canSeePlayer;
    public float DistanceToPlayer => currentDistance;
    public Transform Player => player;
    public Vector3 PlayerPosition => player != null ? player.position : transform.position;

    private void Start()
    {
        if (player == null) Debug.LogError("[NPCSensor] 'player' is not assigned.", this);
        if (directionMarker == null) directionMarker = transform;
    }

    private void Update()
    {
        if (player == null)
        {
            canSeePlayer = false;
            return;
        }
        Perceive();
    }

    private void Perceive()
    {
        currentDistance = Vector3.Distance(transform.position, player.position);
        withinRadius = currentDistance <= detectionRadius;

        if (!withinRadius)
        {
            withinFov = false;
            hasLineOfSight = false;
            canSeePlayer = false;
            return;
        }

        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(directionMarker.forward, directionToPlayer);
        withinFov = angle <= fieldOfViewAngle * 0.5f;

        if (!withinFov)
        {
            hasLineOfSight = false;
            canSeePlayer = false;
            return;
        }

        hasLineOfSight = CheckLineOfSight();
        canSeePlayer = hasLineOfSight;
    }

    private bool CheckLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * eyeHeight;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, obstacleMask))
        {
            return false;
        }
        return true;
    }

    private void OnDrawGizmos() { if (alwaysDrawGizmos) DrawSensorGizmos(); }
    private void OnDrawGizmosSelected() { if (!alwaysDrawGizmos) DrawSensorGizmos(); }

    private void DrawSensorGizmos()
    {
        Vector3 origin = transform.position;
        Vector3 forward = directionMarker != null ? directionMarker.forward : transform.forward;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, detectionRadius);

        Quaternion leftRot = Quaternion.AngleAxis(-fieldOfViewAngle * 0.5f, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(fieldOfViewAngle * 0.5f, Vector3.up);
        Vector3 leftDir = leftRot * forward;
        Vector3 rightDir = rightRot * forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + leftDir * detectionRadius);
        Gizmos.DrawLine(origin, origin + rightDir * detectionRadius);

        if (player != null)
        {
            Gizmos.color = canSeePlayer ? Color.red : Color.gray;
            Gizmos.DrawLine(origin, player.position);
        }
    }
}