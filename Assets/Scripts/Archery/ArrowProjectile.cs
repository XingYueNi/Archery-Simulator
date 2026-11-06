// 箭头弹体：只管「一支箭被发射之后的所有行为」
//
// 唯一权威状态：IsLaunched
//   —— 外面不需要再猜速度、猜 kinematic、猜交互组件有没有禁用
//
// 插住的处理原则（按需求）：
//   保持箭撞上去那一刻的角度和位置，不强行对齐法线、不重新摆位置。
//   只做两件事：清空动力、关掉碰撞体。
//   可选地沿箭自身前进方向微推一点（embedDepth），让箭头看起来扎进表面，
//   这不是"强制设置位置"，只是沿它已有的朝向挪一小段。
using UnityEngine;

public class ArrowProjectile : MonoBehaviour
{
    // 发射状态（只读）：外部只能读，只有 Launch / Stick 会改
    // 注意 [Header] 只能加在字段上，属性上会编译报错 CS0592
    public bool IsLaunched { get; private set; }
    public bool IsStuck { get; private set; }

    [Header("飞行姿态")]
    [Tooltip("每帧把箭身对齐速度方向（关掉则完全交给物理）")]
    public bool alignToVelocity = true;
    [Tooltip("模型朝向修正：模型朝 -Z 填 (0,180,0)；朝 +Y 填 (-90,0,0)")]
    public Vector3 rotationOffset = Vector3.zero;
    [Tooltip("速度低于这个值就不对齐（停住时不乱转）")]
    public float minAlignSpeed = 0.3f;

    [Header("插住")]
    public bool canStick = true;
    [Tooltip("哪些层可以插（默认除 arrow 层以外全部）")]
    public LayerMask stickMask = ~(1 << 8);
    [Tooltip("命中速度低于这个值不插（轻轻碰一下就算了）")]
    public float minImpactSpeed = 0.3f;
    [Tooltip("插住后沿箭自身方向再推进多少米，让箭头看起来扎进去了（0 = 完全不挪）")]
    public float embedDepth = 0.03f;
    [Tooltip("插住时保留「撞上之前那一帧的飞行朝向」（关闭则保留物理解算后的朝向，可能会被弹转）")]
    public bool keepFlightRotation = true;
    [Tooltip("插住后挂到被命中的物体上（物体缩放不均匀时建议关闭，否则箭会被拉变形）")]
    public bool parentToHitObject = false;
    [Tooltip("只有命中物的缩放是均匀的才挂父级（非均匀缩放会让箭变形）")]
    public bool parentOnlyIfUniformScale = true;
    [Tooltip("命中音效（可选）")]
    public AudioClip hitClip;
    [Tooltip("发射时的破空音（留空自动加载 Resources/Audio/arrow_whoosh）")]
    public AudioClip launchClip;
    [Range(0f, 1f)] public float hitVolume = 0.8f;
    [Range(0f, 1f)] public float launchVolume = 0.6f;

    [Header("清理")]
    [Tooltip("存活超过这么多秒自动销毁（0 = 不限制）")]
    public float maxLife = 30f;
    [Tooltip("掉到这个高度以下直接销毁")]
    public float killY = -20f;

    [Header("调试")]
    public bool debugLog = false;

    Rigidbody _rb;
    Collider[] _cols;
    float _launchTime;

    // 撞上之前那一帧的朝向（用来恢复插入角度，避免被物理弹转）
    Quaternion _preHitRotation;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cols = GetComponentsInChildren<Collider>();
    }

    /// <summary>被发射时调用一次。speed 单位 m/s，方向用当前 transform.forward</summary>
    public void Launch(float speed)
    {
        IsLaunched = true;
        IsStuck = false;
        _launchTime = Time.time;

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.velocity = transform.forward * speed;
            _rb.angularVelocity = Vector3.zero;

            // 动态刚体才支持连续检测，避免高速穿薄墙
            if (_rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        // 破空音：留空时自动从 Resources 加载，不用手动拖
        if (launchClip == null) launchClip = Resources.Load<AudioClip>("Audio/arrow_whoosh");
        if (launchClip != null)
            AudioSource.PlayClipAtPoint(launchClip, transform.position, launchVolume);

        if (debugLog)
            Debug.Log("[Arrow] 发射 速度=" + speed.ToString("F1") + "  方向=" + transform.forward.ToString("F2"));
    }

    void FixedUpdate()
    {
        if (!IsLaunched || IsStuck || _rb == null) return;

        // 飞行姿态：箭身跟着速度方向走（弹道下坠时鼻子自然朝下）
        if (alignToVelocity && !_rb.isKinematic)
        {
            Vector3 v = _rb.velocity;
            if (v.sqrMagnitude > minAlignSpeed * minAlignSpeed)
            {
                Quaternion look = Quaternion.LookRotation(v.normalized);
                if (rotationOffset != Vector3.zero) look *= Quaternion.Euler(rotationOffset);
                _rb.MoveRotation(look);
            }
        }

        // 记录这一帧的朝向：撞上时用它当插入角度
        _preHitRotation = transform.rotation;

        if (maxLife > 0f && Time.time - _launchTime > maxLife) { Destroy(gameObject); return; }
        if (transform.position.y < killY) Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsLaunched || IsStuck || !canStick) return;
        if (((1 << collision.gameObject.layer) & stickMask) == 0) return;

        float speed = _rb != null ? _rb.velocity.magnitude : 0f;
        if (speed < minImpactSpeed) return;

        Stick(collision);
    }

    /// <summary>缩放是否均匀（非均匀缩放当父级会把子物体拉变形）</summary>
    bool ShouldParent(Transform target)
    {
        if (!parentOnlyIfUniformScale) return true;

        Vector3 s = target.lossyScale;
        float max = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        float min = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        if (max < 0.0001f) return false;

        return (max - min) / max < 0.05f;   // 三个轴差 5% 以内算均匀
    }

    void Stick(Collision collision)
    {
        IsStuck = true;

        // ① 清空动力：不再被物理推动
        //    这里用 FreezeAll 而不是 isKinematic：
        //    「动态刚体(连续检测) → kinematic」会触发 Unity 警告
        //    "Kinematic body only supports Speculative Continuous collision detection"
        if (_rb != null)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.constraints = RigidbodyConstraints.FreezeAll;
            _rb.detectCollisions = false;
        }

        // ② 关掉碰撞体：不参与后续物理
        if (_cols != null)
            for (int i = 0; i < _cols.Length; i++)
                if (_cols[i] != null) _cols[i].enabled = false;

        // ③ 恢复「撞上之前」的朝向：物理解算会施加角冲量把箭弹转，这里覆盖掉
        if (keepFlightRotation)
            transform.rotation = _preHitRotation;

        // ④ 沿箭自身朝向微推一点，让箭头看起来扎进去（不是强制对齐）
        if (embedDepth > 0.0001f)
            transform.position += transform.forward * embedDepth;

        // ⑤ 挂到被命中的物体上（非均匀缩放的物体会把箭拉变形，所以先检查）
        if (parentToHitObject && collision.transform != null && ShouldParent(collision.transform))
            transform.SetParent(collision.transform, true);

        // 命中音：留空自动加载
        if (hitClip == null) hitClip = Resources.Load<AudioClip>("Audio/arrow_hit");
        if (hitClip != null)
            AudioSource.PlayClipAtPoint(hitClip, transform.position, hitVolume);

        if (debugLog)
            Debug.Log("[Arrow] 插住 " + collision.gameObject.name +
                      "  角度=" + transform.eulerAngles.ToString("F0"));
    }
}