// 弓箭刷新点：在指定位置维持若干支箭，玩家一拿走就补回（无限箭）
//
// 用法：
//   ① 把本物体拖到你想刷新的位置（桌面 / 架子 / 方块上方 2~3cm）
//   ② Play。箭会出现在这个位置；拿走一支，原地立刻补一支
//
// 箭预制体不用手动指定：自动从 PullInteraction.arrowPrefab 取，
// 取不到再回退到 Resources 里的 Arrow.prefab。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ArrowSpawner : MonoBehaviour
{
    [Header("刷新位置")]
    [Tooltip("留空则用本物体自己的位置")]
    public Transform spawnAnchor;

    [Header("箭预制体")]
    [Tooltip("留空则自动从 PullInteraction.arrowPrefab 或 Resources 里找")]
    public GameObject arrowPrefab;

    [Header("箭堆")]
    [Tooltip("这个点维持几支箭（1 = 只放一支，拿走立刻补）")]
    public int pileCount = 1;
    [Tooltip("补箭间隔（秒）")]
    public float respawnDelay = 0.15f;
    [Tooltip("离开刷新点多远算「被拿走」（米）")]
    public float takeDistance = 0.25f;
    [Tooltip("随机散开半径（0 = 正好在点上；多支时建议 0.08~0.15 避免重叠）")]
    public float randomRadius = 0f;
    [Tooltip("随机倾角（度）")]
    public float randomTilt = 0f;

    [Header("行为")]
    [Tooltip("冻结在刷新点：不会掉下去 / 滚走（玩家抓取时由 XRGrabInteractable 接管）")]
    public bool freezeAtSpawn = true;
    [Tooltip("强制设置层名（留空 = 用预制体自己的层）")]
    public string forceLayerName = "";

    [Header("上限")]
    [Tooltip("全场最多同时存在多少支（防止无限堆积）")]
    public int maxLiveArrows = 20;

    [Header("状态（只读）")]
    public int currentPileCount;

    readonly List<GameObject> _pile = new List<GameObject>();
    readonly List<GameObject> _all = new List<GameObject>();
    float _nextSpawnTime;
    bool _ready;

    void Start()
    {
        if (spawnAnchor == null) spawnAnchor = transform;

        // 预制体只认一个来源：弓上配的那个（避免两个预制体分叉）
        GameObject fromBow = ResolveFromBow();
        if (fromBow != null && arrowPrefab != fromBow)
        {
            arrowPrefab = fromBow;
            Debug.Log("[ArrowSpawner] 已统一使用弓上的箭预制体：" + arrowPrefab.name);
        }

        if (arrowPrefab == null)
        {
            arrowPrefab = ResolvePrefab();
            if (arrowPrefab == null)
            {
                Debug.LogError("[ArrowSpawner] 找不到箭预制体：请在 Inspector 里手动指定 arrowPrefab");
                enabled = false;
                return;
            }
            Debug.Log("[ArrowSpawner] 回退到 Resources 里的预制体：" + arrowPrefab.name);
        }

        _ready = true;
        StartCoroutine(FillRoutine());
    }

    /// <summary>取弓上配的箭预制体（唯一来源）</summary>
    GameObject ResolveFromBow()
    {
        PullInteraction pull = FindObjectOfType<PullInteraction>();
        if (pull != null && pull.arrowPrefab != null) return pull.arrowPrefab;
        return null;
    }

    GameObject ResolvePrefab()
    {
        // ① 弓上已经配好的那个
        PullInteraction pull = FindObjectOfType<PullInteraction>();
        if (pull != null && pull.arrowPrefab != null) return pull.arrowPrefab;

        // ② Resources 兜底（项目里箭预制体在 Resources 下）
        GameObject loaded = Resources.Load<GameObject>("Bow-and-Arrow-Assets-main/Prefab/Arrow");
        if (loaded != null) return loaded;

        return null;
    }

    IEnumerator FillRoutine()
    {
        yield return null;   // 等一帧，确保 XR Interaction Manager 初始化完
        for (int i = 0; i < pileCount; i++)
        {
            SpawnOne();
            yield return null;
        }
    }

    void Update()
    {
        if (!_ready) return;

        PrunePile();
        currentPileCount = _pile.Count;

        if (_pile.Count >= pileCount) return;
        if (Time.time < _nextSpawnTime) return;

        SpawnOne();
        _nextSpawnTime = Time.time + respawnDelay;
    }

    /// <summary>把「已经被拿走 / 已销毁」的箭从堆里剔除</summary>
    void PrunePile()
    {
        for (int i = _pile.Count - 1; i >= 0; i--)
        {
            GameObject go = _pile[i];

            if (go == null) { _pile.RemoveAt(i); continue; }                        // 被销毁（射出去）

            XRGrabInteractable grab = go.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected) { _pile.RemoveAt(i); continue; }   // 被手抓住 / 被箭槽吸住

            if (ArrowControl.arrow == go) { _pile.RemoveAt(i); continue; }          // 插进弓了

            float d = Vector3.Distance(go.transform.position, spawnAnchor.position);
            if (d > takeDistance) { _pile.RemoveAt(i); continue; }                  // 被挪走了
        }

        _all.RemoveAll(g => g == null);
    }

    GameObject SpawnOne()
    {
        if (arrowPrefab == null || spawnAnchor == null) return null;
        if (_all.Count >= maxLiveArrows) return null;

        // 位置：刷新点 + 可选随机散开
        Vector3 pos = spawnAnchor.position;
        if (randomRadius > 0f)
        {
            Vector2 c = Random.insideUnitCircle * randomRadius;
            pos += spawnAnchor.right * c.x + spawnAnchor.forward * c.y;
        }

        Quaternion rot = spawnAnchor.rotation;
        if (randomTilt > 0f)
        {
            rot *= Quaternion.Euler(
                Random.Range(-randomTilt, randomTilt),
                Random.Range(0f, 360f),
                Random.Range(-randomTilt, randomTilt));
        }

        GameObject go = Instantiate(arrowPrefab, pos, rot);
        go.name = arrowPrefab.name + "_Spawned";

        if (!string.IsNullOrEmpty(forceLayerName))
        {
            int layer = LayerMask.NameToLayer(forceLayerName);
            if (layer >= 0) SetLayerRecursive(go, layer);
            else Debug.LogWarning("[ArrowSpawner] 找不到层：" + forceLayerName);
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (freezeAtSpawn)
            {
                // 用 FreezeAll 冻结而不是 isKinematic：
                // kinematic 的箭被抓起来再松手时，XRI 会报
                // "Cannot throw a kinematic Rigidbody..."
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;

                // 玩家抓起来时解除冻结，交给手和物理
                XRGrabInteractable grab = go.GetComponent<XRGrabInteractable>();
                if (grab != null)
                {
                    Rigidbody captured = rb;
                    grab.selectEntered.AddListener(delegate (SelectEnterEventArgs args)
                    {
                        if (captured != null) captured.constraints = RigidbodyConstraints.None;
                    });
                }
            }
            else
            {
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.None;
            }
        }

        _pile.Add(go);
        _all.Add(go);
        return go;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    [ContextMenu("立即补满箭堆（调试）")]
    public void RefillNow()
    {
        PrunePile();
        while (_pile.Count < pileCount) SpawnOne();
    }

    void OnDrawGizmosSelected()
    {
        Transform p = spawnAnchor != null ? spawnAnchor : transform;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(p.position, 0.1f);
        Gizmos.DrawRay(p.position, p.forward * 0.3f);
        if (takeDistance > 0f)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(p.position, takeDistance);
        }
    }
}
