using UnityEngine;

/// <summary>
/// Mendeteksi obstacle sederhana di depan agent menggunakan raycast,
/// lalu menghasilkan gaya menghindar (avoidance force) untuk SteeringAgent.
/// </summary>
public class SteeringSensor : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private float rayLength = 3f;
    [SerializeField] private float rayHeightOffset = 0.5f;
    [Tooltip("Sudut ray kiri/kanan dari arah depan, dalam derajat.")]
    [SerializeField] private float sideRayAngle = 30f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Avoidance Settings")]
    [SerializeField] private float avoidanceStrength = 15f;

    // Untuk keperluan visualisasi di SteeringDebug
    public bool CenterHit { get; private set; }
    public bool LeftHit { get; private set; }
    public bool RightHit { get; private set; }
    public Vector3 LastAvoidanceForce { get; private set; }

    /// <summary>
    /// Menghitung gaya menghindar berdasarkan hasil raycast depan, kiri, dan kanan.
    /// Dipanggil oleh SteeringAgent setiap frame.
    /// </summary>
    public Vector3 GetAvoidanceForce()
    {
        Vector3 origin = transform.position + Vector3.up * rayHeightOffset;
        Vector3 forward = transform.forward;
        Vector3 leftDir = Quaternion.Euler(0f, -sideRayAngle, 0f) * forward;
        Vector3 rightDir = Quaternion.Euler(0f, sideRayAngle, 0f) * forward;

        Vector3 avoidance = Vector3.zero;

        CenterHit = Physics.Raycast(origin, forward, out RaycastHit centerHitInfo, rayLength, obstacleLayer);
        LeftHit = Physics.Raycast(origin, leftDir, out RaycastHit leftHitInfo, rayLength, obstacleLayer);
        RightHit = Physics.Raycast(origin, rightDir, out RaycastHit rightHitInfo, rayLength, obstacleLayer);

        if (CenterHit)
        {
            // Dorong menjauh dari normal permukaan obstacle di depan
            avoidance += centerHitInfo.normal * avoidanceStrength;
        }
        if (LeftHit)
        {
            // Obstacle di kiri -> dorong ke kanan
            avoidance += transform.right * avoidanceStrength * (1f - leftHitInfo.distance / rayLength);
        }
        if (RightHit)
        {
            // Obstacle di kanan -> dorong ke kiri
            avoidance -= transform.right * avoidanceStrength * (1f - rightHitInfo.distance / rayLength);
        }

        LastAvoidanceForce = avoidance;
        return avoidance;
    }

    // Data ray untuk digambar oleh SteeringDebug
    public Vector3 RayOrigin => transform.position + Vector3.up * rayHeightOffset;
    public Vector3 ForwardDir => transform.forward;
    public Vector3 LeftDir => Quaternion.Euler(0f, -sideRayAngle, 0f) * transform.forward;
    public Vector3 RightDir => Quaternion.Euler(0f, sideRayAngle, 0f) * transform.forward;
    public float RayLength => rayLength;
}
