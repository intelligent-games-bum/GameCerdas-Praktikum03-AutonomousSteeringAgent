using UnityEngine;

/// <summary>
/// Mendeteksi obstacle sederhana di depan agent menggunakan tiga raycast (tengah, kiri, kanan),
/// lalu menghasilkan gaya menghindar (avoidance force) untuk SteeringAgent.
///
/// Catatan penting:
/// - Ray ditembakkan searah GERAK agent (dikirim oleh SteeringAgent), bukan searah hadap.
///   Saat agent berbelok, rotasi tertinggal dari arah gerak karena di-Slerp, sehingga
///   transform.forward bukan arah yang benar-benar ditempuh.
/// - Semua gaya dijaga tetap horizontal (y = 0) supaya agent tidak pernah terdorong ke atas.
/// </summary>
public class SteeringSensor : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("Agar raycast juga mengenai sisi BELAKANG MeshCollider. Tanpa ini, tembok rumah " +
             "tidak terdeteksi saat agent berada di dalam bangunan, sehingga dia menembus keluar. " +
             "Catatan: ini setelan physics global, berlaku untuk seluruh scene.")]
    [SerializeField] private bool queryBackfaces = true;
    [Tooltip("Panjang ray sensor. Makin panjang = agent bereaksi lebih awal.")]
    [SerializeField] private float sensorDistance = 3f;
    [SerializeField] private float rayHeightOffset = 0.5f;
    [Tooltip("Sudut ray kiri/kanan dari arah gerak, dalam derajat.")]
    [SerializeField] private float sideRayAngle = 30f;
    [SerializeField] private LayerMask obstacleLayer;

    [Tooltip("Permukaan yang kemiringannya di bawah nilai ini dianggap TANAH, bukan obstacle. " +
             "Berguna kalau ground/bukit ikut berada di layer Obstacle.")]
    [SerializeField] private float maxWalkableSlope = 45f;

    [Header("Ledge Detection")]
    [Tooltip("Mencegah agent berjalan keluar dari tanah / jatuh ke jurang.")]
    [SerializeField] private bool detectLedges = true;
    [SerializeField] private LayerMask groundLayer = ~0;
    [Tooltip("Seberapa jauh di depan agent titik tanah diperiksa.")]
    [SerializeField] private float ledgeLookahead = 2.5f;
    [Tooltip("Beda ketinggian yang masih dianggap tanah. Lebih dari ini dianggap jurang.")]
    [SerializeField] private float ledgeDropLimit = 2f;
    [Tooltip("Pengali kekuatan dorongan menjauhi tepi, relatif terhadap avoidanceStrength.")]
    [SerializeField] private float ledgeStrengthMultiplier = 2f;

    [Header("Avoidance Settings")]
    [Tooltip("Besar gaya menghindar mentah. Bobot campurannya diatur di SteeringAgent (avoidanceWeight).")]
    [SerializeField] private float avoidanceStrength = 15f;
    [Tooltip("Selisih jarak (meter) yang harus dilewati sebelum agent bersedia berganti sisi belokan. " +
             "Mencegah agent bimbang kiri-kanan di celah sempit. Besarkan kalau masih bergetar.")]
    [SerializeField] private float sideSwitchMargin = 0.75f;
    [Tooltip("Waktu peredaman gaya menghindar (detik). 0 = tanpa peredaman (tajam tapi mudah bergetar).")]
    [SerializeField] private float avoidanceSmoothing = 0.15f;

    // Buffer raycast dipakai ulang agar tidak menghasilkan garbage tiap frame
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    // Sisi belokan terakhir saat menghindari tepi. Disimpan agar agent tidak
    // bolak-balik kiri-kanan (oscillate) ketika berdiri tepat di ujung tanah.
    private float ledgeTurnSide = 1f;

    // Sisi belokan saat menghindari obstacle, dipertahankan selama satu "episode" penghindaran
    private float obstacleTurnSide = 1f;
    private bool wasAvoiding;

    // Gaya menghindar hasil peredaman antar frame
    private Vector3 smoothedAvoidance;

    // Untuk keperluan visualisasi di SteeringDebug
    public bool CenterHit { get; private set; }
    public bool LeftHit { get; private set; }
    public bool RightHit { get; private set; }
    public Vector3 LastAvoidanceForce { get; private set; }

    /// <summary>
    /// Seberapa mendesak keadaan di depan agent, 0 (lapang) sampai 1 (mentok / tepi jurang).
    /// Dipakai SteeringAgent untuk menentukan seberapa besar avoidance mengambil alih kendali
    /// dari behavior utama.
    /// </summary>
    public float ThreatLevel { get; private set; }

    // Arah ray terakhir yang dipakai (diisi tiap kali GetAvoidanceForce dipanggil)
    public Vector3 LastOrigin { get; private set; }
    public Vector3 LastForwardDir { get; private set; }
    public Vector3 LastLeftDir { get; private set; }
    public Vector3 LastRightDir { get; private set; }
    public float SensorDistance => sensorDistance;
    public float MaxWalkableSlope => maxWalkableSlope;
    public LayerMask ObstacleLayer => obstacleLayer;

    // Hasil deteksi tepi tanah (untuk gizmos)
    public bool DetectLedges => detectLedges;
    public bool ForwardGroundOk { get; private set; } = true;
    public bool LeftGroundOk { get; private set; } = true;
    public bool RightGroundOk { get; private set; } = true;
    public Vector3 ForwardGroundPoint { get; private set; }
    public Vector3 LeftGroundPoint { get; private set; }
    public Vector3 RightGroundPoint { get; private set; }

    private void Awake()
    {
        if (queryBackfaces)
        {
            Physics.queriesHitBackfaces = true;
        }

        // Nilai awal agar gizmos tetap masuk akal sebelum frame pertama
        LastForwardDir = transform.forward;
        LastLeftDir = Quaternion.Euler(0f, -sideRayAngle, 0f) * transform.forward;
        LastRightDir = Quaternion.Euler(0f, sideRayAngle, 0f) * transform.forward;
        LastOrigin = transform.position + Vector3.up * rayHeightOffset;
    }

    /// <summary>
    /// Menghitung gaya menghindar berdasarkan hasil raycast tengah, kiri, dan kanan.
    /// Dipanggil oleh SteeringAgent setiap frame.
    /// </summary>
    /// <param name="travelDirection">Arah gerak agent saat ini (boleh belum ternormalisasi).</param>
    public Vector3 GetAvoidanceForce(Vector3 travelDirection)
    {
        // Arah acuan: arah gerak, jatuh balik ke arah hadap kalau agent sedang diam
        Vector3 forward = travelDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        // Sumbu kanan relatif arah gerak (bukan transform.right, karena rotasi bisa tertinggal)
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 origin = transform.position + Vector3.up * rayHeightOffset;
        Vector3 leftDir = Quaternion.AngleAxis(-sideRayAngle, Vector3.up) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(sideRayAngle, Vector3.up) * forward;

        LastOrigin = origin;
        LastForwardDir = forward;
        LastLeftDir = leftDir;
        LastRightDir = rightDir;

        CenterHit = CastObstacle(origin, forward, out RaycastHit centerHitInfo);
        LeftHit = CastObstacle(origin, leftDir, out RaycastHit leftHitInfo);
        RightHit = CastObstacle(origin, rightDir, out RaycastHit rightHitInfo);

        Vector3 avoidance = Vector3.zero;

        // 1) Dorongan mundur dari permukaan obstacle di depan (dibuat horizontal).
        //    Bobotnya sengaja setengah: dorongan mundur penuh membuat agent berhenti di depan
        //    tembok lalu didorong maju lagi oleh behavior utama - itu sumber getaran juga.
        if (CenterHit)
        {
            Vector3 normal = centerHitInfo.normal;
            normal.y = 0f;
            // Kalau normal murni vertikal (mis. kena lantai/atap), pakai arah balik sebagai pengganti
            normal = normal.sqrMagnitude < 0.0001f ? -forward : normal.normalized;

            // Saat mengenai backface (agent di dalam bangunan), normal menunjuk searah gerak.
            // Dipakai apa adanya, gaya justru mendorong agent MENEMBUS tembok. Balikkan.
            if (Vector3.Dot(normal, forward) > 0f) normal = -forward;

            avoidance += normal * avoidanceStrength * 0.5f * Urgency(centerHitInfo.distance);
        }

        // 2) Dorongan menyamping: PILIH sisi yang lebih lapang, jangan menjumlahkan kiri + kanan.
        //    Kalau dijumlahkan, obstacle di kiri dan kanan sekaligus (lorong/sudut tembok)
        //    akan saling meniadakan dan agent menabrak lurus ke depan.
        float leftClearance = LeftHit ? leftHitInfo.distance : sensorDistance;
        float rightClearance = RightHit ? rightHitInfo.distance : sensorDistance;

        bool avoidingNow = CenterHit || LeftHit || RightHit;

        if (LeftHit || RightHit)
        {
            float side = ChooseSide(leftClearance, rightClearance, rightClearance >= leftClearance ? 1f : -1f);
            float urgency = Urgency(Mathf.Min(leftClearance, rightClearance));
            avoidance += right * side * avoidanceStrength * urgency;
        }
        else if (CenterHit)
        {
            // Tabrakan tegak lurus dengan kedua sisi bebas: pilih sisi mengikuti kemiringan permukaan
            // supaya agent menyusur tembok, bukan mentok bolak-balik di depannya.
            float fallback = Vector3.Dot(right, centerHitInfo.normal) >= 0f ? 1f : -1f;
            float side = ChooseSide(leftClearance, rightClearance, fallback);
            avoidance += right * side * avoidanceStrength * Urgency(centerHitInfo.distance);
        }

        wasAvoiding = avoidingNow;

        // 3) Deteksi tepi tanah: titik di depan yang tidak punya tanah diperlakukan
        //    seperti tembok, supaya agent tidak berjalan keluar dari map.
        if (detectLedges)
        {
            avoidance += LedgeForce(origin, forward, leftDir, rightDir, right);
        }

        avoidance.y = 0f;

        // Seberapa mendesak keadaan di depan: obstacle terdekat atau tepi tanah
        float rawThreat = 0f;
        if (CenterHit) rawThreat = Mathf.Max(rawThreat, Urgency(centerHitInfo.distance));
        if (LeftHit || RightHit) rawThreat = Mathf.Max(rawThreat, Urgency(Mathf.Min(leftClearance, rightClearance)));
        if (detectLedges)
        {
            if (!ForwardGroundOk) rawThreat = 1f;
            else if (!LeftGroundOk || !RightGroundOk) rawThreat = Mathf.Max(rawThreat, 0.7f);
        }

        // Redam antar frame supaya gaya tidak bisa berbalik arah dalam sekejap (anti getaran)
        float smoothingStep = avoidanceSmoothing > 0f ? Time.deltaTime / avoidanceSmoothing : 1f;
        smoothedAvoidance = Vector3.Lerp(smoothedAvoidance, avoidance, smoothingStep);
        ThreatLevel = Mathf.Lerp(ThreatLevel, rawThreat, smoothingStep);

        LastAvoidanceForce = smoothedAvoidance;
        return smoothedAvoidance;
    }

    /// <summary>
    /// Menentukan sisi belokan dengan histeresis: begitu agent memilih satu sisi, sisi itu
    /// dipertahankan selama obstacle masih terdeteksi, dan baru berganti kalau sisi seberang
    /// benar-benar lebih lapang (selisihnya melebihi sideSwitchMargin).
    /// Tanpa ini, di celah sempit keputusan bisa berbalik tiap frame dan agent bergetar.
    /// </summary>
    private float ChooseSide(float leftClearance, float rightClearance, float fallbackSide)
    {
        // Episode penghindaran baru -> bebas memilih
        if (!wasAvoiding)
        {
            obstacleTurnSide = fallbackSide;
            return obstacleTurnSide;
        }

        float chosenClearance = obstacleTurnSide > 0f ? rightClearance : leftClearance;
        float otherClearance = obstacleTurnSide > 0f ? leftClearance : rightClearance;

        if (otherClearance > chosenClearance + sideSwitchMargin)
        {
            obstacleTurnSide = -obstacleTurnSide;
        }

        return obstacleTurnSide;
    }

    /// <summary>
    /// Memeriksa tiga titik di depan agent (depan, serong kiri, serong kanan).
    /// Titik tanpa tanah di bawahnya dianggap jurang dan menghasilkan dorongan menjauh.
    /// </summary>
    private Vector3 LedgeForce(Vector3 origin, Vector3 forward, Vector3 leftDir, Vector3 rightDir, Vector3 right)
    {
        ForwardGroundPoint = origin + forward * ledgeLookahead;
        LeftGroundPoint = origin + leftDir * ledgeLookahead;
        RightGroundPoint = origin + rightDir * ledgeLookahead;

        ForwardGroundOk = HasGround(ForwardGroundPoint);
        LeftGroundOk = HasGround(LeftGroundPoint);
        RightGroundOk = HasGround(RightGroundPoint);

        if (ForwardGroundOk && LeftGroundOk && RightGroundOk) return Vector3.zero;

        float strength = avoidanceStrength * ledgeStrengthMultiplier;
        Vector3 force = Vector3.zero;

        if (!LeftGroundOk) force += right * strength;   // tepi di kiri -> geser ke kanan
        if (!RightGroundOk) force -= right * strength;  // tepi di kanan -> geser ke kiri

        if (!ForwardGroundOk)
        {
            // Tepi tepat di depan: rem mundur sambil memutar ke sisi yang masih ada tanahnya.
            if (LeftGroundOk && !RightGroundOk) ledgeTurnSide = -1f;
            else if (RightGroundOk && !LeftGroundOk) ledgeTurnSide = 1f;
            // Kalau kedua sisi sama-sama aman (atau sama-sama jurang), pertahankan
            // arah putar sebelumnya agar tidak bimbang bolak-balik.

            force += -forward * strength;
            force += right * ledgeTurnSide * strength;
        }

        return force;
    }

    /// <summary>
    /// True kalau ada tanah di bawah titik tersebut dalam batas ledgeDropLimit.
    /// </summary>
    private bool HasGround(Vector3 point)
    {
        Vector3 probeOrigin = point + Vector3.up * 0.5f;
        float maxDistance = 0.5f + rayHeightOffset + ledgeDropLimit;

        int count = Physics.RaycastNonAlloc(probeOrigin, Vector3.down, hitBuffer, maxDistance,
                                            groundLayer, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Transform hitTransform = hitBuffer[i].collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Raycast yang hanya mengembalikan obstacle "sungguhan": permukaan landai (tanah, bukit,
    /// ramp) diabaikan meskipun berada di obstacleLayer, dan collider milik agent sendiri dilewati.
    /// Diambil hit valid yang paling dekat.
    /// </summary>
    private bool CastObstacle(Vector3 origin, Vector3 direction, out RaycastHit result)
    {
        result = default;

        int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, sensorDistance,
                                            obstacleLayer, QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];

            // Origin berada di dalam collider -> normal tidak bisa dipercaya
            if (hit.distance <= 0f) continue;

            // Collider milik agent sendiri
            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;

            // Permukaan yang cukup datar dianggap tanah, bukan tembok
            if (Vector3.Angle(hit.normal, Vector3.up) < maxWalkableSlope) continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                result = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 0 = obstacle masih di ujung jangkauan sensor, 1 = obstacle tepat menempel di agent.
    /// </summary>
    private float Urgency(float hitDistance)
    {
        if (sensorDistance <= 0f) return 0f;
        return Mathf.Clamp01(1f - hitDistance / sensorDistance);
    }
}
