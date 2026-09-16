using UnityEngine;

/// <summary>
/// Agent dengan behavior Seek / Arrive / Wander.
/// - Jika ada target -> Seek/Arrive menuju target.
/// - Jika tidak ada target -> Wander (bergerak acak yang halus).
/// - Selalu menghadap arah gerak.
/// - Menggabungkan avoidance vector dari SteeringSensor (jika ada).
///
/// Alur tiap frame: desired velocity -> steering force -> velocity -> posisi.
/// </summary>
[RequireComponent(typeof(SteeringSensor))]
public class SteeringAgent : MonoBehaviour
{
    public enum SteeringState { Seek, Arrive, Wander }

    [Header("Target")]
    [Tooltip("Kosongkan (null) agar agent otomatis Wander.")]
    [SerializeField] private Transform target;

    [Header("Target Detection (opsional)")]
    [Tooltip("Kalau diisi, agent mencari target sendiri lewat NPCSensor (radius + FOV + line of sight). " +
             "Kalau dikosongkan, target hanya bisa diisi manual lewat Inspector atau SetTarget().")]
    [SerializeField] private NPCSensor detectionSensor;
    [Tooltip("Berapa lama agent tetap mengejar setelah target hilang dari pandangan, " +
             "sebelum menyerah dan kembali Wander.")]
    [SerializeField] private float loseTargetDelay = 2f;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 5f;
    [SerializeField] private float maxForce = 10f;
    [Tooltip("Kecepatan memutar badan ke arah gerak. Kecil = belokan terasa berat.")]
    [SerializeField] private float turnSpeed = 8f;
    [Tooltip("Waktu (detik) yang dibutuhkan agent untuk mencapai kecepatan yang diinginkan. " +
             "Kecil = akselerasi dan pengereman tajam, besar = terasa berat dan licin.")]
    [SerializeField] private float responseTime = 0.2f;

    [Header("Arrive Settings")]
    [Tooltip("Jarak mulai melambat menuju target.")]
    [SerializeField] private float slowRadius = 3f;
    [Tooltip("Jarak dianggap sudah sampai, agent direm sampai berhenti.")]
    [SerializeField] private float stopRadius = 0.2f;

    [Header("Wander Settings")]
    [SerializeField] private float wanderRadius = 3f;
    [SerializeField] private float wanderDistance = 5f;
    [Tooltip("Seberapa cepat arah wander berubah. Kecil = jalan lurus terus, besar = acak gelisah.")]
    [SerializeField] private float wanderStrength = 15f;

    [Header("Obstacle Avoidance")]
    [Tooltip("Seberapa cepat avoidance mengambil alih kendali dari behavior utama saat obstacle " +
             "mendekat. 0 = sensor diabaikan sepenuhnya, 1 = seimbang, >1 = agent lebih penakut " +
             "dan membelok lebih awal.")]
    [SerializeField] private float avoidanceWeight = 1f;

    [Header("Collision")]
    [Tooltip("Menahan posisi agent agar tidak menembus obstacle. Avoidance hanya membelokkan niat gerak; " +
             "tanpa penahan ini agent tetap bisa tembus kalau lajunya lebih cepat dari reaksi sensornya.")]
    [SerializeField] private bool blockOnObstacles = true;
    [SerializeField] private LayerMask collisionLayer;
    [Tooltip("Jarak minimal badan agent ke permukaan obstacle.")]
    [SerializeField] private float bodyRadius = 0.4f;
    [Tooltip("Tinggi titik pengecekan tabrakan dari pivot agent (setinggi badan, bukan kaki).")]
    [SerializeField] private float collisionHeightOffset = 1f;
    [Tooltip("Celah tipis yang disisakan dari permukaan, mencegah agent menempel dan tersangkut.")]
    [SerializeField] private float skinWidth = 0.05f;

    [Header("Ground")]
    [Tooltip("Menempelkan agent ke permukaan tanah. Wajib aktif di map berbukit, " +
             "karena steering ini hanya menghitung gerak horizontal.")]
    [SerializeField] private bool stickToGround = true;
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("Jarak sumbu Y antara pivot agent dan permukaan tanah.")]
    [SerializeField] private float groundOffset = 0f;
    [Tooltip("Ketinggian awal ray ke bawah. Perbesar kalau agent menaiki tanjakan curam.")]
    [SerializeField] private float groundProbeHeight = 2f;
    [Tooltip("Beda tinggi maksimum yang boleh dinaiki dalam satu langkah. " +
             "Mencegah agent tiba-tiba terangkat ke atas gunung atau atap saat melewatinya.")]
    [SerializeField] private float maxStepHeight = 0.5f;

    [Header("Area Bounds")]
    [Tooltip("Menahan agent agar tidak wander keluar area.")]
    [SerializeField] private bool useBounds = true;
    [Tooltip("Pakai posisi awal agent sebagai pusat area.")]
    [SerializeField] private bool useStartPositionAsCenter = true;
    [SerializeField] private Vector3 boundsCenter = Vector3.zero;
    [SerializeField] private float boundsRadius = 20f;
    [Tooltip("Kekuatan tarikan kembali saat agent melewati batas area.")]
    [SerializeField] private float boundsReturnStrength = 2f;

    // State internal
    private Vector3 velocity;
    private Vector3 wanderTarget;
    private SteeringSensor sensor;
    private readonly RaycastHit[] groundBuffer = new RaycastHit[8];
    private float loseTargetTimer;
    private readonly RaycastHit[] collisionBuffer = new RaycastHit[8];

    public SteeringState CurrentState { get; private set; }
    public Vector3 Velocity => velocity;
    public Vector3 DesiredDirection { get; private set; }
    public Vector3 WanderCirclePosition { get; private set; }
    public float WanderRadius => wanderRadius;
    public Transform CurrentTarget => target;
    public bool IsBlocked { get; private set; }
    public float BodyRadius => bodyRadius;
    public float CollisionHeightOffset => collisionHeightOffset;
    public bool UseBounds => useBounds;
    public Vector3 BoundsCenter => boundsCenter;
    public float BoundsRadius => boundsRadius;

    private void Awake()
    {
        sensor = GetComponent<SteeringSensor>();

        // LayerMask default-nya "Nothing". Kalau dibiarkan kosong, raycast tidak akan pernah
        // mengenai apa pun dan penahan tabrakan mati tanpa tanda apa-apa. Ikut saja ke
        // obstacleLayer milik sensor, dan berteriak kalau dua-duanya kosong.
        if (blockOnObstacles && collisionLayer.value == 0)
        {
            if (sensor != null && sensor.ObstacleLayer.value != 0)
            {
                collisionLayer = sensor.ObstacleLayer;
            }
            else
            {
                Debug.LogWarning("[SteeringAgent] Collision Layer dan Obstacle Layer dua-duanya kosong. " +
                                 "Agent akan menembus semua obstacle.", this);
            }
        }

        if (useStartPositionAsCenter)
        {
            boundsCenter = transform.position;
        }

        // Inisialisasi titik wander awal secara acak di sekitar agent
        Vector2 randomPoint = Random.insideUnitCircle.normalized * wanderRadius;
        wanderTarget = new Vector3(randomPoint.x, 0f, randomPoint.y);
    }

    private void Update()
    {
        UpdateTargetFromSensor();

        Vector3 steeringForce;

        if (target == null)
        {
            CurrentState = SteeringState.Wander;
            steeringForce = Wander();
        }
        else
        {
            float distance = HorizontalDistance(transform.position, target.position);
            if (distance <= slowRadius)
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

        // Gabungkan dengan gaya menghindar obstacle.
        // Ray mengikuti arah gerak, bukan arah hadap, karena rotasi tertinggal saat berbelok.
        //
        // Avoidance TIDAK dijumlahkan, melainkan mengambil alih secara bertahap sesuai
        // tingkat ancaman. Kalau dijumlahkan, gaya Seek yang mengarah lurus ke target
        // tetap menang dan agent mentok di tembok sambil bergetar.
        if (sensor != null)
        {
            Vector3 avoidance = sensor.GetAvoidanceForce(velocity);
            float takeover = Mathf.Clamp01(sensor.ThreatLevel * avoidanceWeight);
            steeringForce = Vector3.Lerp(steeringForce, avoidance, takeover);
        }

        // Tarik kembali ke dalam area kalau agent sudah melewati batas
        steeringForce += BoundsForce();

        steeringForce.y = 0f;
        steeringForce = Vector3.ClampMagnitude(steeringForce, maxForce);

        // Integrasi kecepatan
        velocity += steeringForce * Time.deltaTime;
        velocity.y = 0f; // agent ini murni bergerak horizontal
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        // Rem halus sampai berhenti kalau sudah sangat dekat target (khusus Arrive)
        if (CurrentState == SteeringState.Arrive &&
            HorizontalDistance(transform.position, target.position) < stopRadius)
        {
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, maxForce * Time.deltaTime);
        }

        Vector3 movement = velocity * Time.deltaTime;
        if (blockOnObstacles)
        {
            movement = ResolveCollision(movement);
        }
        transform.position += movement;

        if (stickToGround)
        {
            SnapToGround();
        }

        // Menghadap arah gerak
        if (velocity.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Mengisi / mengosongkan target berdasarkan NPCSensor, kalau sensornya dipasang.
    /// Target tidak langsung dilepas saat hilang dari pandangan; ada jeda loseTargetDelay
    /// supaya agent tidak bolak-balik kejar-wander tiap kali target lewat di balik pohon.
    /// </summary>
    private void UpdateTargetFromSensor()
    {
        // Tanpa sensor, agent memakai target manual apa adanya (sesuai modul).
        if (detectionSensor == null) return;

        if (detectionSensor.CanSeePlayer && detectionSensor.Player != null)
        {
            target = detectionSensor.Player;
            loseTargetTimer = loseTargetDelay;
            return;
        }

        if (target == null) return;

        // Target sempat terlihat tapi sekarang hilang -> hitung mundur sebelum menyerah
        loseTargetTimer -= Time.deltaTime;
        if (loseTargetTimer <= 0f)
        {
            target = null;
        }
    }

    /// <summary>
    /// Bergerak lurus secepat mungkin menuju target.
    /// </summary>
    private Vector3 Seek(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        Vector3 desired = toTarget.normalized * maxSpeed;
        DesiredDirection = desired.normalized;
        return SteerTowards(desired);
    }

    /// <summary>
    /// Seperti Seek, tapi melambat secara halus saat mendekati target (slowRadius).
    /// </summary>
    private Vector3 Arrive(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        float speed = maxSpeed;
        if (distance < slowRadius)
        {
            speed = maxSpeed * (distance / slowRadius);
        }

        Vector3 desired = toTarget.normalized * speed;
        DesiredDirection = desired.normalized;
        return SteerTowards(desired);
    }

    /// <summary>
    /// Mengubah kecepatan yang diinginkan menjadi gaya steering.
    /// Dibagi responseTime supaya selisih kecepatan benar-benar dikejar dalam waktu segitu,
    /// bukan sekadar "didorong pelan" seperti rumus polos desired - velocity.
    /// </summary>
    private Vector3 SteerTowards(Vector3 desiredVelocity)
    {
        return (desiredVelocity - velocity) / Mathf.Max(responseTime, 0.01f);
    }

    /// <summary>
    /// Bergerak acak yang halus menggunakan lingkaran proyeksi di depan agent.
    /// Titik di lingkaran digeser sedikit demi sedikit (jitter) supaya arah berubah mulus,
    /// bukan melompat acak tiap frame.
    /// </summary>
    private Vector3 Wander()
    {
        // Geser titik wander secara acak. wanderStrength dikali deltaTime agar hasilnya
        // konsisten di framerate berapa pun, jadi nilainya memang perlu relatif besar.
        wanderTarget += new Vector3(
            Random.Range(-1f, 1f),
            0f,
            Random.Range(-1f, 1f)) * wanderStrength * Time.deltaTime;

        // Kembalikan titik ke permukaan lingkaran wander
        if (wanderTarget.sqrMagnitude < 0.0001f)
        {
            wanderTarget = new Vector3(1f, 0f, 0f);
        }
        wanderTarget = wanderTarget.normalized * wanderRadius;

        // Proyeksikan lingkaran wander di depan arah gerak agent saat ini
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 circleCenter = forward * wanderDistance;
        Vector3 targetOnCircle = circleCenter + wanderTarget;

        WanderCirclePosition = transform.position + circleCenter;
        DesiredDirection = targetOnCircle.normalized;

        Vector3 desired = targetOnCircle.normalized * maxSpeed;
        return SteerTowards(desired);
    }

    /// <summary>
    /// Gaya koreksi menuju pusat area, aktif hanya saat agent keluar dari boundsRadius.
    /// Tanpa ini agent yang sedang Wander bisa berjalan terus meninggalkan map.
    /// </summary>
    private Vector3 BoundsForce()
    {
        if (!useBounds) return Vector3.zero;

        Vector3 offset = transform.position - boundsCenter;
        offset.y = 0f;

        float distance = offset.magnitude;
        if (distance <= boundsRadius) return Vector3.zero;

        // Makin jauh keluar, makin kuat tarikannya
        float overshoot = distance - boundsRadius;
        Vector3 desired = -offset.normalized * maxSpeed;
        return (desired - velocity) * boundsReturnStrength * Mathf.Clamp01(overshoot / boundsRadius + 0.25f);
    }

    /// <summary>
    /// Menahan perpindahan agar tidak menembus obstacle. Kalau jalur gerak terhalang,
    /// sisa gerakan diproyeksikan sejajar permukaan (slide) supaya agent menyusur tembok
    /// dan tidak berhenti mati di depannya.
    /// </summary>
    private Vector3 ResolveCollision(Vector3 movement)
    {
        Vector3 remaining = movement;
        remaining.y = 0f;

        Vector3 moved = Vector3.zero;
        IsBlocked = false;

        // Dua tahap: tahap pertama menangani tembok yang dihadapi, tahap kedua memeriksa
        // hasil geseran menyusur tembok itu - tanpa ini, di sudut ruangan geseran dari
        // tembok pertama langsung menembus tembok kedua.
        for (int pass = 0; pass < 2; pass++)
        {
            float distance = remaining.magnitude;
            if (distance < 0.0001f) return moved;

            Vector3 direction = remaining / distance;
            Vector3 origin = transform.position + moved + Vector3.up * collisionHeightOffset;

            if (!TryFindBlocker(origin, direction, distance, out float allowed, out Vector3 normal))
            {
                return moved + remaining;
            }

            IsBlocked = true;

            normal.y = 0f;
            if (normal.sqrMagnitude < 0.0001f) normal = -direction;
            normal.Normalize();

            // Backface (agent di dalam bangunan): normal menunjuk searah gerak, balikkan
            if (Vector3.Dot(normal, direction) > 0f) normal = -direction;

            float step = Mathf.Clamp(allowed, 0f, distance);
            moved += direction * step;

            // Sisa gerakan digeser sejajar tembok
            Vector3 leftover = direction * (distance - step);
            remaining = Vector3.ProjectOnPlane(leftover, normal);

            // Buang komponen kecepatan yang menekan tembok, supaya tidak menumpuk tiap frame
            velocity = Vector3.ProjectOnPlane(velocity, normal);
        }

        // Masih terhalang setelah dua tembok (terjepit di sudut) -> sisa gerakan dibuang
        return moved;
    }

    /// <summary>
    /// Mencari penghalang terdekat di jalur gerak, lalu mengembalikan berapa jauh agent
    /// masih boleh maju. Memakai dua metode yang saling menutupi kelemahan:
    /// - SphereCast memberi ketebalan sebesar bodyRadius, sehingga sudut tembok dan objek
    ///   tipis tidak lolos di sela-sela garis ray.
    /// - Raycast tipis menangkap backface (tembok dilihat dari dalam bangunan), yang tidak
    ///   selalu dilaporkan oleh SphereCast.
    /// </summary>
    private bool TryFindBlocker(Vector3 origin, Vector3 direction, float distance,
                                out float allowed, out Vector3 normal)
    {
        allowed = float.MaxValue;
        normal = Vector3.zero;

        bool found = false;
        float slopeLimit = sensor != null ? sensor.MaxWalkableSlope : 45f;

        // 1) SphereCast - berbadan, menangkap serempetan diagonal
        int count = Physics.SphereCastNonAlloc(origin, bodyRadius, direction, collisionBuffer,
                                               distance, collisionLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = collisionBuffer[i];

            // distance 0 = sphere sudah tumpang tindih sejak awal, normalnya tidak bisa dipercaya
            if (hit.distance <= 0f) continue;
            if (!IsBlockingSurface(hit, slopeLimit)) continue;

            float candidate = hit.distance - skinWidth;
            if (candidate < allowed)
            {
                allowed = candidate;
                normal = hit.normal;
                found = true;
            }
        }

        // 2) Raycast tipis - menangkap backface
        count = Physics.RaycastNonAlloc(origin, direction, collisionBuffer, distance + bodyRadius,
                                        collisionLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = collisionBuffer[i];

            if (hit.distance <= 0f) continue;
            if (!IsBlockingSurface(hit, slopeLimit)) continue;

            // Jarak ray diukur dari titik pusat, jadi dikurangi radius badan
            float candidate = hit.distance - bodyRadius;
            if (candidate < allowed)
            {
                allowed = candidate;
                normal = hit.normal;
                found = true;
            }
        }

        if (found) allowed = Mathf.Max(0f, allowed);
        return found;
    }

    /// <summary>
    /// Permukaan dianggap penghalang kalau bukan milik agent sendiri dan cukup curam
    /// (tanah serta tanjakan landai tidak menghalangi).
    /// </summary>
    private bool IsBlockingSurface(RaycastHit hit, float slopeLimit)
    {
        Transform hitTransform = hit.collider.transform;
        if (hitTransform == transform || hitTransform.IsChildOf(transform)) return false;

        return Vector3.Angle(hit.normal, Vector3.up) >= slopeLimit;
    }

    /// <summary>
    /// Menembakkan ray ke bawah lalu menaruh agent tepat di permukaan tanah.
    /// Kalau tidak ada tanah di bawah (mis. agent di luar map), posisi Y dibiarkan apa adanya.
    /// </summary>
    private void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
        float maxDistance = groundProbeHeight * 2f + 5f;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundBuffer, maxDistance,
                                            groundLayer, QueryTriggerInteraction.Ignore);

        float highestY = float.MinValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundBuffer[i];

            // Abaikan collider milik agent sendiri
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;

            // Abaikan permukaan yang terlalu tinggi untuk dinaiki (puncak gunung, atap, peti).
            // Tanpa filter ini agent bisa "teleport" ke atas objek yang sedang dilewatinya.
            if (hit.point.y > transform.position.y + maxStepHeight) continue;

            // Dari sisa kandidat, ambil permukaan tertinggi
            if (hit.point.y > highestY)
            {
                highestY = hit.point.y;
                found = true;
            }
        }

        if (!found) return;

        Vector3 position = transform.position;
        position.y = highestY + groundOffset;
        transform.position = position;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
