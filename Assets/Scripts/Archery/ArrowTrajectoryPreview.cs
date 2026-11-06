// 弹道预览：拉弓时画出箭会飞过的轨迹 + 落点
//
// 为什么能"精确"预测：
//   箭发射后只受重力（Rigidbody 的 drag = 0，没有空气阻力），
//   所以 p(t) = p0 + v0*t + 0.5*g*t²，逐步积分即可，和真实飞行完全一致。
//
// 用法：
//   ① 挂在弓上（或弓的子物体）
//   ② Bow 留空会自动找场景里的 PullInteraction
//   ③ 可选：指定一条 LineRenderer 和一个落点标记（不指定就自动生成）
//   ④ 拉弓时就能看到抛物线，松开后消失
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class ArrowTrajectoryPreview : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("留空则自动找场景里的 PullInteraction")]
    public PullInteraction bow;
    [Tooltip("留空则自动生成一条 LineRenderer")]
    public LineRenderer line;
    [Tooltip("落点标记（留空则自动生成一个小圆盘）")]
    public Transform hitMarker;

    [Header("显示条件")]
    [Tooltip("必须真的抓着弓弦才显示（关掉则只要拉弓量够就显示）")]
    public bool requireGrabbed = true;
    [Tooltip("只在拉弓时显示")]
    public bool showOnlyWhenPulling = true;
    [Tooltip("拉弓量超过这个值才显示（抓弦但没拉开时 pullAmount 会有 0.05~0.15 的底值）")]
    [Range(0f, 1f)] public float minPullToShow = 0.15f;
    [Tooltip("弓上没有箭时不显示轨迹（和 PullInteraction 的空放保护对应）")]
    public bool requireArrowOnString = true;

    [Header("模拟")]
    [Tooltip("模拟多少步（步数 × 步长 = 最远预测时间）")]
    public int maxSteps = 80;
    [Tooltip("每步时长（秒），越小越平滑但越费")]
    public float timeStep = 0.02f;
    [Tooltip("哪些层会挡住箭（决定落点）")]
    public LayerMask hitMask = ~0;
    [Tooltip("命中点往前推一点，避免标记陷进表面")]
    public float markerOffset = 0.02f;
    [Tooltip("最远预测距离（米），防止拉满时线拉到天边")]
    public float maxDistance = 60f;
    [Tooltip("离起点多近的命中算「打到自己」，直接忽略（拉满时箭扣贴脸，会打到手/头）")]
    public float selfHitIgnoreDistance = 0.5f;

    [Header("调试")]
    [Tooltip("每 0.5 秒打印一次：拉弓量、初速、是否命中、命中物体")]
    public bool debugLog = false;

    [Header("外观")]
    public float lineWidth = 0.02f;
    public Color lineColorStart = new Color(1f, 0.9f, 0.4f, 0.9f);
    public Color lineColorEnd = new Color(1f, 0.6f, 0.2f, 0.2f);
    public float markerSize = 0.12f;

    LineRenderer _line;
    Transform _marker;
    Material _lineMat;
    Material _markerMat;

    Vector3[] _points = new Vector3[0];

    void Awake()
    {
        if (bow == null) bow = FindObjectOfType<PullInteraction>();

        // ── 线 ──
        _line = line;
        if (_line == null)
        {
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
        }

        _line.positionCount = 0;
        _line.useWorldSpace = true;
        _line.startWidth = lineWidth;
        _line.endWidth = lineWidth * 0.4f;
        _line.numCapVertices = 2;
        _line.material = GetLineMaterial();
        _line.startColor = lineColorStart;
        _line.endColor = lineColorEnd;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = false;

        // ── 落点标记 ──
        _marker = hitMarker;
        if (_marker == null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ArrowHitMarker";
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * markerSize;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = GetMarkerMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _marker = go.transform;
        }

        if (_marker != null) _marker.gameObject.SetActive(false);
    }

    Material GetLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _lineMat = new Material(sh);
        _lineMat.color = lineColorStart;
        return _lineMat;
    }

    Material GetMarkerMaterial()
    {
        if (_markerMat != null) return _markerMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        _markerMat = new Material(sh);
        _markerMat.color = new Color(1f, 0.85f, 0.3f, 0.85f);
        return _markerMat;
    }

    void LateUpdate()
    {
        if (bow == null) { Hide(); return; }

        // ① 必须真的抓着弓弦（isSelected = 手正选中着弓弦这个交互物体）
        if (requireGrabbed && !bow.isSelected) { Hide(); return; }

        // ② 拉弓量要够大（抓弦但没拉开时 pullAmount 会有个底值，避免亮一条短线）
        float pull = bow.pullAmount;
        if (showOnlyWhenPulling && pull < minPullToShow) { Hide(); return; }

        if (bow.arrowPrefab == null) { Hide(); return; }

        // ③ 弓上没有箭 → 不显示预测
        //    否则会出现「空拉也有轨迹」的错觉，和 PullInteraction 的空放保护不一致
        if (requireArrowOnString && !HasArrowOnString()) { Hide(); return; }

        // 起点、方向、初速 —— 和 ArrowLauncher / ArrowProjectile 用同一套参数
        Transform aim = bow.aimSource != null ? bow.aimSource : bow.transform;
        Vector3 dir = aim.forward.normalized;

        Vector3 origin = bow.notch != null ? bow.notch.transform.position : aim.position;
        origin += dir * bow.spawnForwardOffset;

        float speed = Mathf.Lerp(bow.minSpeed, bow.maxSpeed, Mathf.Clamp01(pull));
        if (speed <= 0.01f) { Hide(); return; }

        Simulate(origin, dir * speed);
        Draw();

        if (debugLog && Time.time - _lastLogTime > 0.5f)
        {
            _lastLogTime = Time.time;
            string hitInfo = _hasHit
                ? ("命中 " + _lastHitName + " @ " + _hitPoint.ToString("F2"))
                : "未命中（飞了 " + _validCount + " 步）";
            Debug.Log("[轨迹] 拉弓量=" + pull.ToString("F2") +
                      "  初速=" + speed.ToString("F1") +
                      "  方向=" + dir.ToString("F2") +
                      "  起点=" + origin.ToString("F2") +
                      "  " + hitInfo);
        }
    }

    /// <summary>逐步积分 + 每步射线检测，算出轨迹和落点</summary>
    void Simulate(Vector3 origin, Vector3 velocity)
    {
        Vector3 g = Physics.gravity;
        Vector3 pos = origin;
        Vector3 v = velocity;

        if (_points.Length != maxSteps + 1) _points = new Vector3[maxSteps + 1];
        _points[0] = pos;
        int count = 1;

        bool hit = false;
        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;

        for (int i = 0; i < maxSteps; i++)
        {
            Vector3 prev = pos;

            // 半隐式欧拉：先更新速度再更新位置（和 Unity 的物理积分一致）
            v += g * timeStep;
            pos += v * timeStep;

            // 这一段路上有没有东西挡住
            Vector3 seg = pos - prev;
            float dist = seg.magnitude;
            if (dist > 0.0001f)
            {
                int n = Physics.RaycastNonAlloc(prev, seg / dist, _hitBuf, dist + 0.001f,
                                                hitMask, QueryTriggerInteraction.Ignore);

                // 找最近的、且离起点足够远的命中
                // （拉满弓时箭扣贴脸，第一个命中往往是自己的手/头，要跳过）
                float bestDist = float.MaxValue;
                RaycastHit best = default(RaycastHit);
                bool found = false;

                for (int k = 0; k < n; k++)
                {
                    RaycastHit h = _hitBuf[k];
                    if (h.collider == null) continue;
                    if (Vector3.Distance(origin, h.point) < selfHitIgnoreDistance) continue;   // 打到自己，跳过
                    float d = Vector3.Distance(prev, h.point);
                    if (d < bestDist) { bestDist = d; best = h; found = true; }
                }

                if (found)
                {
                    pos = best.point;
                    hitPoint = best.point;
                    hitNormal = best.normal;
                    _lastHitName = best.collider.gameObject.name;
                    hit = true;
                }
            }

            _points[count] = pos;
            count++;

            if (hit) break;
            if (pos.y < -50f) break;
            if (Vector3.Distance(origin, pos) > maxDistance) break;
        }

        _validCount = count;
        _hasHit = hit;
        _hitPoint = hitPoint;
        _hitNormal = hitNormal;
    }

    readonly RaycastHit[] _hitBuf = new RaycastHit[8];
    float _lastLogTime;
    string _lastHitName = "";
    int _validCount;
    bool _hasHit;
    Vector3 _hitPoint;
    Vector3 _hitNormal;

    void Draw()
    {
        if (_line == null) return;

        if (_validCount < 2) { _line.enabled = false; _line.positionCount = 0; return; }

        _line.positionCount = _validCount;
        for (int i = 0; i < _validCount; i++) _line.SetPosition(i, _points[i]);
        _line.enabled = true;

        if (_marker != null)
        {
            if (_hasHit)
            {
                _marker.gameObject.SetActive(true);
                _marker.position = _hitPoint + _hitNormal * markerOffset;
                _marker.rotation = Quaternion.LookRotation(_hitNormal);   // 平贴在表面上
            }
            else
            {
                _marker.gameObject.SetActive(false);
            }
        }
    }

    void Hide()
    {
        if (_line != null) { _line.enabled = false; _line.positionCount = 0; }
        if (_marker != null) _marker.gameObject.SetActive(false);
    }

    /// <summary>
    /// 弓上是否真的有箭。
    /// ArrowControl.arrow 由 XRSocketInteractor 在选中箭时记录；
    /// 再查 activeInHierarchy，避免"引用还在但物体被禁用"。
    /// </summary>
    bool HasArrowOnString()
    {
        GameObject arrow = ArrowControl.arrow;
        return arrow != null && arrow.activeInHierarchy;
    }

    void OnDestroy()
    {
        if (_lineMat != null) Destroy(_lineMat);
        if (_markerMat != null) Destroy(_markerMat);
    }
}