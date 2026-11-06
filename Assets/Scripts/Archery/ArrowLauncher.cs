// 发箭器：唯一的「造箭入口」
//
// 为什么单独一个类：
//   生成箭、算初速、初始化飞行状态 这三件事必须在一起，
//   否则「一支箭是不是已经被发射」就没人负责，外面只能靠猜
//   （之前插住功能失败，就是因为靠猜）。
//
// 弓只负责算「拉弓量 0~1」，发箭器负责把拉弓量翻译成速度。
using UnityEngine;

public static class ArrowLauncher
{
    /// <summary>
    /// 在指定位置造一支箭并发射出去。
    /// </summary>
    /// <param name="prefab">箭预制体</param>
    /// <param name="position">出生位置（一般用箭扣）</param>
    /// <param name="direction">发射方向（会自动归一化）</param>
    /// <param name="pullAmount">拉弓量 0~1</param>
    /// <param name="minSpeed">最小拉弓量时的初速</param>
    /// <param name="maxSpeed">拉满弓时的初速</param>
    /// <param name="eulerOffset">模型朝向修正</param>
    /// <returns>生成出来的箭（失败返回 null）</returns>
    public static GameObject Launch(
        GameObject prefab,
        Vector3 position,
        Vector3 direction,
        float pullAmount,
        float minSpeed,
        float maxSpeed,
        Vector3 eulerOffset)
    {
        if (prefab == null) { Debug.LogWarning("[ArrowLauncher] 没有箭预制体"); return null; }

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

        // ① 造箭：朝向 = 速度方向（不要再抄弓弦的 rotation）
        Quaternion rot = Quaternion.LookRotation(dir);
        if (eulerOffset != Vector3.zero) rot *= Quaternion.Euler(eulerOffset);

        GameObject arrow = Object.Instantiate(prefab, position, rot);

        // ② 射出去的箭不再参与抓取（否则会落在箭槽触发区里被吸回去）
        UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable grab =
            arrow.GetComponent<UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable>();
        if (grab != null) grab.enabled = false;

        // ③ 赋初速 + 标记状态（唯一权威）
        float speed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.Clamp01(pullAmount));

        // 预制体上没挂就自动补一个（这样不用手动改预制体，插住功能照样生效）
        ArrowProjectile proj = arrow.GetComponent<ArrowProjectile>();
        if (proj == null) proj = arrow.AddComponent<ArrowProjectile>();

        proj.Launch(speed);              // 箭自己设速度、自己标记「已发射」

        return arrow;
    }
}