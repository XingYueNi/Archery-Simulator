// 箭槽状态：记录「当前搭在弓上的那支箭」
//
// 职责（只做这一件）：
//   被 XRSocketInteractor 选中时，记住这支箭，并把它的碰撞体关掉
//   （不关的话，箭插在弓上会和弓/手互相挤压然后弹飞）
//
// 不做：飞行、命中、插住、发射
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ArrowControl : MonoBehaviour
{
    [Tooltip("留空则自动取本物体上的 XRSocketInteractor")]
    public XRSocketInteractor _socket;

    /// <summary>当前搭在弓上的箭（没有则为 null）</summary>
    public static GameObject arrow;

    void Awake()
    {
        if (_socket == null) _socket = GetComponent<XRSocketInteractor>();

        if (_socket != null)
            _socket.selectEntered.AddListener(OnSelected);
        else
            Debug.LogWarning("[ArrowControl] 这个物体上没有 XRSocketInteractor，箭槽功能不会生效");
    }

    void OnDestroy()
    {
        if (_socket != null) _socket.selectEntered.RemoveListener(OnSelected);
    }

    void OnSelected(SelectEnterEventArgs args)
    {
        GameObject go = args.interactableObject.transform.gameObject;

        // 关掉这支箭的所有碰撞体（不限定 BoxCollider，兼容任意形状）
        Collider[] cols = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].enabled = false;

        arrow = go;
    }

    /// <summary>消耗掉弓上那支箭（发射时调用）</summary>
    public static void ArrowDestroy()
    {
        if (arrow != null) Destroy(arrow);
        arrow = null;
    }
}