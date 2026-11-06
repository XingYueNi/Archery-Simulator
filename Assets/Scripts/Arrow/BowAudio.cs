// ─────────────────────────────────────────────────────────────
//  弓的音效：拉弓（跟手部动作联动）+ 放箭 + 空放
//
//  关键设计：拉弓声不是"拉着就一直响"，而是"在动才响"
//    · 玩家往后拉时：出吱呀声，音调随拉弓量升高
//    · 手停住不动：声音淡出消失（否则像一张卡住的唱片）
//    · 继续拉 / 往回收：声音重新出现
//
//  实现：每帧比较拉弓量的变化速度
//    speed = |当前拉弓量 - 上一帧拉弓量| / deltaTime
//    超过 moveThreshold 才算"在动"
//
//  音效文件由 tools/make_bow_sfx.py 程序化合成，放在
//    Assets/Resources/Audio/bow_draw.wav / bow_release.wav
//  留空 Clip 字段会自动从 Resources 加载，不用手动拖。
// ─────────────────────────────────────────────────────────────
using UnityEngine;

public class BowAudio : MonoBehaviour
{
    [Header("音效（留空自动从 Resources/Audio 加载）")]
    public AudioClip drawClip;
    public AudioClip releaseClip;
    public AudioClip dryFireClip;

    [Header("音量")]
    [Range(0f, 1f)] public float drawVolume = 0.5f;
    [Range(0f, 1f)] public float releaseVolume = 0.9f;
    [Range(0f, 1f)] public float dryFireVolume = 0.6f;

    [Header("拉弓音的音调")]
    [Tooltip("拉弓量刚过阈值时的音调（1 = 原始音高）")]
    public float pitchAtMin = 0.9f;
    [Tooltip("拉满弓时的音调")]
    public float pitchAtMax = 1.3f;

    [Header("只在动作时发声")]
    [Tooltip("拉弓量超过这个值才开始有声音")]
    [Range(0f, 1f)] public float startThreshold = 0.05f;
    [Tooltip("拉弓量每秒变化超过这个值才算「在动」（0.08 ≈ 每秒 8%）")]
    public float moveThreshold = 0.08f;
    [Tooltip("停顿超过这么多秒就静音")]
    public float stopDelay = 0.08f;
    [Tooltip("淡入淡出速度（越大切换越干脆）")]
    public float fadeSpeed = 18f;
    [Tooltip("只在往后拉时发声（关掉则松弦往回放也发声）")]
    public bool onlyWhenPulling = true;

    AudioSource _draw;      // 循环的拉弓音
    AudioSource _sfx;       // 一次性音效

    bool _playing;
    float _volume;
    float _prevAmount;
    float _lastMoveTime = -999f;
    bool _hasPrev;
    bool _warnedMissingClip;

    void Awake()
    {
        if (drawClip == null) drawClip = Resources.Load<AudioClip>("Audio/bow_draw");
        if (releaseClip == null) releaseClip = Resources.Load<AudioClip>("Audio/bow_release");
        if (dryFireClip == null) dryFireClip = Resources.Load<AudioClip>("Audio/arrow_hit");

        _draw = gameObject.AddComponent<AudioSource>();
        _draw.clip = drawClip;
        _draw.loop = true;
        _draw.playOnAwake = false;
        _draw.spatialBlend = 0f;        // 2D：弓跟着手走，3D 定位反而会飘
        _draw.volume = 0f;

        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0f;
    }

    /// <summary>每帧传入当前拉弓量（0~1）</summary>
    public void SetDraw(float amount)
    {
        amount = Mathf.Clamp01(amount);

        // ① 判断"这一帧手在动吗"
        if (!_hasPrev)
        {
            _prevAmount = amount;
            _hasPrev = true;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float delta = amount - _prevAmount;
        float speed = Mathf.Abs(delta) / dt;
        _prevAmount = amount;

        bool moving = onlyWhenPulling ? (delta > 0f) : true;
        if (moving && speed > moveThreshold)
        {
            _lastMoveTime = Time.time;
            if (amount > startThreshold) StartPlayIfNeeded();
        }
        else if (amount <= startThreshold)
        {
            _lastMoveTime = -999f;      // 松手了，立刻静音
        }

        // ② 停顿够久就没声
        bool shouldSound = amount > startThreshold && (Time.time - _lastMoveTime) <= stopDelay;

        // ③ 淡入淡出，避免"啪"一下切断
        float targetVolume = shouldSound ? drawVolume * Mathf.InverseLerp(startThreshold, 1f, amount) : 0f;
        _volume = Mathf.MoveTowards(_volume, targetVolume, fadeSpeed * Time.deltaTime);

        if (_draw != null)
        {
            _draw.volume = _volume;
            _draw.pitch = Mathf.Lerp(pitchAtMin, pitchAtMax, amount);

            if (_volume > 0.001f) StartPlayIfNeeded();
            else if (_playing) StopPlay();
        }
    }

    /// <summary>立即停掉拉弓音（放箭 / 空放 / 松手 / 手离开弓弦时调用）</summary>
    public void StopDraw()
    {
        _volume = 0f;
        _lastMoveTime = -999f;
        _hasPrev = false;
        StopPlay();
    }

    /// <summary>正常放箭</summary>
    public void PlayRelease()
    {
        StopDraw();
        if (releaseClip != null) _sfx.PlayOneShot(releaseClip, releaseVolume);
    }

    /// <summary>空放（弓上没有箭）</summary>
    public void PlayDryFire()
    {
        StopDraw();
        if (dryFireClip != null) _sfx.PlayOneShot(dryFireClip, dryFireVolume);
    }

    void StartPlayIfNeeded()
    {
        if (_playing) return;

        if (drawClip == null)
        {
            if (!_warnedMissingClip)
            {
                _warnedMissingClip = true;
                Debug.LogWarning("[弓音效] 没有 bow_draw.wav，拉弓不会出声。检查 Assets/Resources/Audio/");
            }
            return;
        }

        _draw.clip = drawClip;
        _draw.Play();
        _playing = true;
    }

    void StopPlay()
    {
        if (!_playing) return;
        if (_draw != null) _draw.Stop();
        _playing = false;
    }
}
