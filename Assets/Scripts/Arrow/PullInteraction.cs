// 弓弦交互：只管「拉弓」这一件事
//
// 职责（只做这三件）：
//   ① 算拉弓量 pullAmount（0~1）
//   ② 拉弓视觉：弓弦线、箭扣、箭槽的位移
//   ③ 松手时把「拉弓量」交给 ArrowLauncher，由它负责造箭和发射
//
// 不做：Instantiate、算速度、设速度、改箭的交互状态、命中判定
//
// ⚠ 类名和方法名 SetPullInteractor / Release 是场景 UnityEvent 硬绑的，不能改
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class PullInteraction : XRBaseInteractable
{
    public static event Action<float> PullActionReleased;

    [Header("引用")]
    public GameObject socket;          // 箭槽
    public GameObject arrowPrefab;     // 要发射的箭
    public Transform start, end;       // 弓弦起点 / 满弓位置
    public GameObject notch;           // 箭扣

    [Header("发射方向")]
    [Tooltip("留空 = 用本物体（弓弦）的 forward。填「弓」或「手柄」的 forward 通常更准")]
    public Transform aimSource;
    [Tooltip("箭模型朝向修正：模型朝 -Z 填 (0,180,0)；朝 +Y 填 (-90,0,0)")]
    public Vector3 arrowEulerOffset = Vector3.zero;
    [Tooltip("从箭扣往前多远生成箭（避免和弓弦重叠）")]
    public float spawnForwardOffset = 0.02f;

    [Header("拉弓力度（交给 ArrowLauncher 翻译成速度）")]
    [Tooltip("刚拉一点就松手的初速 m/s")]
    public float minSpeed = 5f;
    [Tooltip("拉满弓的初速 m/s")]
    public float maxSpeed = 25f;
    [Tooltip("低于这个拉弓量视为没拉动，不发射")]
    [Range(0f, 1f)]
    public float minPullToShoot = 0.1f;

    [Header("音效")]
    [Tooltip("弓的音效控制器（留空则不发声）")]
    public BowAudio bowAudio;

    [Header("空放保护（弓上没有箭时不发射）")]
    [Tooltip("勾上后：弓上没有箭，松手不会凭空射出一支箭")]
    public bool requireArrowOnString = true;
    [Tooltip("空放音效（可选）")]
    public AudioClip dryFireClip;
    [Tooltip("空放时在 Console 打印原因")]
    public bool logDryFire = true;

    public float pullAmount { get; private set; } = 0.0f;

    LineRenderer _lineRenderer;
    IXRSelectInteractor _pullingInteractor;

    protected override void Awake()
    {
        base.Awake();
        _lineRenderer = GetComponent<LineRenderer>();
    }

    // ── 场景 UnityEvent 绑定的两个方法（不要改名）──

    public void SetPullInteractor(SelectEnterEventArgs args)
    {
        _pullingInteractor = args.interactorObject;
    }

    public void Release()
    {
        if (pullAmount >= minPullToShoot)
        {
            Shoot();
        }
        else
        {
            if (pullAmount > 0f)
                Debug.Log("[弓] 拉弓量 " + pullAmount.ToString("F2") + " 太小，不发射");
            if (bowAudio != null) bowAudio.StopDraw();
        }

        PullActionReleased?.Invoke(pullAmount);
        ResetVisual();
    }

    // ── 拉弓过程 ──

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);

        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
        if (!isSelected || _pullingInteractor == null)
        {
            // 手离开弓弦：拉弓声必须停，否则会一直响下去
            if (bowAudio != null) bowAudio.StopDraw();
            return;
        }

        pullAmount = CalulatePull(_pullingInteractor.transform.position);
        UpdateString();

        // 拉弓音随拉弓量变化（音调/音量）
        if (bowAudio != null) bowAudio.SetDraw(pullAmount);
    }

    /// <summary>拉弓量 = 手的位置在「弓拉伸轴」上的投影 ÷ 满弓长度</summary>
    float CalulatePull(Vector3 handPosition)
    {
        Vector3 pullDirection = handPosition - start.position;
        Vector3 targetDirection = end.position - start.position;
        float maxLength = targetDirection.magnitude;
        targetDirection.Normalize();
        float value = Vector3.Dot(pullDirection, targetDirection) / maxLength;
        return Mathf.Clamp(value, 0f, 1f);
    }

    // ── 发射（把活交给 ArrowLauncher）──

    void Shoot()
    {
        // ── ① 先判定：弓上到底有没有箭 ──
        //    没有箭就"空放"：不生成箭，只给一点反馈
        if (requireArrowOnString && !HasArrowOnString())
        {
            DryFire();
            return;
        }

        Transform aim = aimSource != null ? aimSource : transform;
        Vector3 dir = aim.forward;
        Vector3 spawnPos = notch.transform.position + dir.normalized * spawnForwardOffset;

        // ② 弓上那支箭消耗掉（走到这里说明一定有箭）
        ArrowControl.ArrowDestroy();

        // ③ 造箭 + 赋速 + 标记飞行状态，全部由发箭器负责
        ArrowLauncher.Launch(arrowPrefab, spawnPos, dir, pullAmount, minSpeed, maxSpeed, arrowEulerOffset);

        // 放箭音
        if (bowAudio != null) bowAudio.PlayRelease();


    }

    /// <summary>
    /// 弓上是否真的有箭。
    /// ArrowControl.arrow 是 XRSocketInteractor 选中箭时记录的引用；
    /// 再查一次 activeInHierarchy，避免"引用还在但物体已被禁用"的情况。
    /// </summary>
    bool HasArrowOnString()
    {
        GameObject arrow = ArrowControl.arrow;
        return arrow != null && arrow.activeInHierarchy;
    }

    /// <summary>空放：不生成箭，只给震动 / 音效 / 日志</summary>
    void DryFire()
    {
        if (logDryFire)
            Debug.Log("[弓] 弓上没有箭，本次不发射（拉弓量 " + pullAmount.ToString("F2") + "）");

        if (bowAudio != null)
        {
            if (dryFireClip != null) bowAudio.dryFireClip = dryFireClip;
            bowAudio.PlayDryFire();
        }
        else if (dryFireClip != null)
        {
            AudioSource.PlayClipAtPoint(dryFireClip, transform.position);
        }

    }

    void ResetVisual()
    {
        pullAmount = 0f;
        _pullingInteractor = null;

        if (notch != null)
            notch.transform.localPosition = new Vector3(
                notch.transform.localPosition.x,
                notch.transform.localPosition.y,
                0f);

        UpdateString();
    }

    // ── 拉弓视觉 ──

    void UpdateString()
    {
        if (_lineRenderer == null || notch == null) return;

        Vector3 linePosition = Vector3.forward *
            Mathf.Lerp(start.transform.localPosition.z, end.transform.localPosition.z, pullAmount);

        notch.transform.localPosition = new Vector3(
            notch.transform.localPosition.x,
            notch.transform.localPosition.y,
            linePosition.z + 0.2f);

        _lineRenderer.SetPosition(1, linePosition);

        if (socket != null)
        {
            socket.transform.localPosition = new Vector3(
                socket.transform.localPosition.x,
                socket.transform.localPosition.y,
                linePosition.z + 0.12f);
        }
    }
}