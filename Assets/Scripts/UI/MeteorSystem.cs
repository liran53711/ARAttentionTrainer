// Assets/Scripts/UI/MeteorSystem.cs
// 流星引导 · 世界空间视图（2026-10-20，占位素材版）：
//   · 流星池（默认 12）：发光头 billboard + TrailRenderer 拖尾，沿贝塞尔从"天际"划向老师脸旁
//   · 星环：8 颗星星围绕老师脸周（世界坐标，随老师/观察者平滑更新），点亮时弹入+闪烁
//   · 流星雨：批量流星错峰划过 + 星环爆闪
//   · 指路流星（孩子未注视时）：飞向老师所在的视野方位并划出视野
// 占位素材：复用 FxStar 材质（star_soft.png）+ GuideArrow shader 染色；尾迹用 TrailRenderer 渐变。
// 正式素材就位后：把 meteor_head / trail 贴图替换进来即可（见技术方案素材清单）。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class MeteorSystem : MonoBehaviour
{
    [Header("素材（占位：注入 StarDot / FxGlow 材质）")]
    public Material starMat;      // 流星头 + 星环（居中星图）
    public Material trailMat;     // 拖尾（柔光）
    [Tooltip("观察者（相机）——billboard 朝向与星环计算用")]
    public Transform eye;

    [Header("星环")]
    public int ringCount = 8;
    public float ringRadius = 0.35f;
    [Tooltip("星环位置平滑时间（老师移动/转头时星星滑过去）")]
    public float ringSmoothTime = 0.25f;

    [Header("流星（2026-10-20 三调：用户反馈不明显 → 头 0.35m / 拖尾 0.7s / 划行 1.4s）")]
    public int poolSize = 12;
    public float meteorDuration = 1.4f;
    [Tooltip("流星头尺寸（米）")]
    public float headSize = 0.35f;
    public float trailTime = 0.7f;
    public float trailWidth = 0.16f;
    [Tooltip("普通流星同屏上限（流星雨时临时放开到池上限）")]
    public int maxConcurrent = 3;

    class Meteor
    {
        public GameObject go;
        public MeshRenderer head;
        public TrailRenderer trail;
        public Vector3 p0, ctrl, p2;
        public float t, duration;
        public bool active;
        public bool showerMode;
    }

    readonly List<Meteor> _pool = new List<Meteor>();
    readonly Transform[] _ring = new Transform[8];
    readonly MeshRenderer[] _ringR = new MeshRenderer[8];
    readonly Vector3[] _ringVel = new Vector3[8];
    readonly Vector3[] _ringTarget = new Vector3[8];
    readonly bool[] _ringLit = new bool[8];
    int _numActive;

    // 星环目标（每帧由驱动层推入）
    Vector3 _teacherPos;
    Vector3 _viewerPos;
    bool _hasRingTarget;

    void Awake()
    {
        // 流星池
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("Meteor" + i);
            go.transform.SetParent(transform, false);
            var head = MakeQuad(go.transform, headSize);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.minVertexDistance = 0.01f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
            trail.material = trailMat != null ? trailMat
                           : starMat != null ? starMat
                           : new Material(Shader.Find("ARAttention/GuideArrow"));
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(0.65f, 0.5f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.emitting = false;
            go.SetActive(false);
            _pool.Add(new Meteor { go = go, head = head, trail = trail });
        }
        // 星环（2026-10-20 占位加大：0.09→0.16m）
        for (int i = 0; i < 8; i++)
        {
            var go = new GameObject("RingStar" + i);
            go.transform.SetParent(transform, false);
            var r = MakeQuad(go.transform, 0.16f);
            _ringR[i] = r;
            _ring[i] = go.transform;
            go.SetActive(false);
        }
    }

    MeshRenderer MakeQuad(Transform parent, float size)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Head";
        quad.transform.SetParent(parent, false);
        quad.transform.localScale = Vector3.one * size;
        var r = quad.GetComponent<MeshRenderer>();
        Object.Destroy(quad.GetComponent<Collider>());
        r.sharedMaterial = starMat != null ? new Material(starMat) : new Material(Shader.Find("ARAttention/GuideArrow"));
        r.sharedMaterial.SetColor("_Color", new Color(1f, 1f, 0.92f, 1f)); // 亮白金（星环 Tick 会再覆盖闪烁）
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// <summary>驱动层每帧推送老师位置（星环跟随；观察者取 eye 位置）。</summary>
    public void SetRingTarget(Vector3 teacherPos)
    {
        _teacherPos = teacherPos;
        _viewerPos = eye != null ? eye.position : teacherPos + Vector3.forward * 2f;
        _hasRingTarget = true;
    }

    /// <summary>点亮星环第 index 颗（0..ringCount-1），弹入动画+常亮闪烁。</summary>
    public void LightStar(int index)
    {
        if (index < 0 || index >= _ringR.Length || _ringR[index] == null) return;
        _ringLit[index] = true;
        _ring[index].gameObject.SetActive(true);
    }

    public void ClearStars()
    {
        for (int i = 0; i < _ringR.Length; i++)
        {
            _ringLit[i] = false;
            if (_ring[i] != null) _ring[i].gameObject.SetActive(false);
        }
    }

    /// <summary>发一颗流星：从"天际"沿弧线落到老师脸旁的目标槽位（世界空间）。</summary>
    public void SpawnToTeacher(int slot)
    {
        if (!_hasRingTarget) return;
        Vector3 end = _ringTarget[Mathf.Clamp(slot, 0, _ringTarget.Length - 1)];
        Vector3 up = Vector3.up;
        Vector3 away = _teacherPos - _viewerPos; away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
        away.Normalize();
        // 起点：老师上方 1.6m + 远离孩子方向 1.0m（更靠近视野，孩子能看到更多划行过程）
        Vector3 start = _teacherPos + up * 1.6f + away * 1.0f;
        SpawnMeteor(start, Vector3.Lerp(start, end, 0.5f) + up * 0.5f, end, meteorDuration, false);
    }

    /// <summary>指路流星：朝老师所在的视野方位划出屏幕（不落到老师身边）。</summary>
    public void SpawnGuideMeteor(Vector3 dirWorld)
    {
        Vector3 start = _viewerPos + Vector3.up * 0.6f + dirWorld.normalized * 0.6f;
        Vector3 end = _viewerPos + dirWorld.normalized * 30f;
        SpawnMeteor(start, Vector3.Lerp(start, end, 0.3f) + Vector3.up * 1.2f, end, meteorDuration * 1.1f, false);
    }

    /// <summary>流星雨：批量流星错峰划过（约 1.5s 内全部出发）。</summary>
    public void PlayShower()
    {
        if (!_hasRingTarget) return;
        Vector3 up = Vector3.up;
        Vector3 away = _teacherPos - _viewerPos; away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
        away.Normalize();
        Vector3 right = Vector3.Cross(up, away).normalized;
        int n = Mathf.Min(10, poolSize - _numActive);
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Lerp(-0.9f, 0.9f, i / Mathf.Max(1f, n - 1f));
            Vector3 dir = Quaternion.AngleAxis(ang * 70f, up) * -away; // 扇形散布（朝向老师一侧）
            Vector3 start = _teacherPos + up * (2.0f + Random.Range(0f, 0.7f)) + dir * (1.4f + Random.Range(0f, 0.8f));
            Vector3 end = _ringTarget[Random.Range(0, ringCount)] + Random.insideUnitSphere * 0.15f;
            // 错峰：用 duration 拉长（每条流星内部 t 从负延迟开始）
            var m = SpawnMeteor(start, Vector3.Lerp(start, end, 0.5f) + up * 0.5f, end, meteorDuration, true);
            if (m != null) m.t = -Random.Range(0f, 0.7f); // 负延迟=错峰出发
        }
        // 星环爆闪：保持星星激活直到爆闪结束（协程末尾再关闭），标记清空
        for (int i = 0; i < _ringR.Length; i++)
        {
            if (!_ringLit[i]) continue;
            _ringLit[i] = false;
            StartCoroutine(BurstStar(_ring[i], _ringR[i]));
        }
    }

    System.Collections.IEnumerator BurstStar(Transform t, MeshRenderer r)
    {
        float time = 0f;
        Vector3 baseScale = Vector3.one * 0.16f;
        while (time < 0.8f && t != null && r != null)
        {
            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / 0.8f);
            t.localScale = baseScale * (1f + 2.2f * k);
            if (r.sharedMaterial != null)
                r.sharedMaterial.SetColor("_Color", new Color(1f, 0.95f, 0.7f, 1f - k));
            yield return null;
        }
        if (t != null) t.gameObject.SetActive(false);
    }

    Meteor SpawnMeteor(Vector3 p0, Vector3 ctrl, Vector3 p2, float duration, bool shower)
    {
        Meteor m = null;
        foreach (var cand in _pool)
            if (!cand.active) { m = cand; break; }
        bool wasActive = false;
        if (m == null)
        {
            if (!shower) return null;                       // 普通流星已满 → 丢弃
            // 流星雨优先：抢占一条正在飞的流星雨流星（计数不变）
            foreach (var cand in _pool)
                if (cand.active && cand.showerMode) { m = cand; wasActive = true; break; }
            if (m == null) return null;
        }
        m.active = true; m.showerMode = shower;
        m.p0 = p0; m.ctrl = ctrl; m.p2 = p2;
        m.t = 0f; m.duration = duration;
        m.go.SetActive(true);
        m.go.transform.position = p0;
        m.trail.Clear();
        m.trail.emitting = true;
        if (!wasActive) _numActive++;
        return m;
    }

    /// <summary>每帧推进（由驱动层调用；也可挂 Update——这里显式 Tick 以配合测试机）。</summary>
    public void Tick(float now, float dt)
    {
        // 星环位置平滑跟随老师
        if (_hasRingTarget)
        {
            for (int i = 0; i < ringCount && i < _ring.Length; i++)
            {
                _ringTarget[i] = MeteorLogic.RingSlot(_teacherPos, _viewerPos, i, ringCount, ringRadius);
                if (!_ringLit[i]) continue;
                _ring[i].position = Vector3.SmoothDamp(_ring[i].position, _ringTarget[i], ref _ringVel[i], ringSmoothTime);
                // 朝向观察者 + 闪烁
                if (eye != null && _ring[i].gameObject.activeSelf)
                    _ring[i].rotation = Quaternion.LookRotation(_ring[i].position - eye.position, Vector3.up);
                float tw = 0.75f + 0.25f * Mathf.Sin(now * 5f + i * 1.3f);
                _ring[i].localScale = Vector3.one * (0.16f * tw);
                if (_ringR[i] != null && _ringR[i].sharedMaterial != null)
                    _ringR[i].sharedMaterial.SetColor("_Color", new Color(1f, 0.93f, 0.65f, 0.55f + 0.45f * tw));
            }
        }
        // 流星推进
        for (int i = 0; i < _pool.Count; i++)
        {
            var m = _pool[i];
            if (!m.active) continue;
            m.t += dt;
            if (m.t < 0f) { m.go.SetActive(false); continue; } // 错峰等待
            m.go.SetActive(true);
            float k = Mathf.Clamp01(m.t / m.duration);
            float ease = k * k; // easeIn：越接近落地越快
            m.go.transform.position = MeteorLogic.Bezier(m.p0, m.ctrl, m.p2, ease);
            if (eye != null)
                m.go.transform.rotation = Quaternion.LookRotation(m.go.transform.position - eye.position, Vector3.up);
            if (k >= 1f)
            {
                m.active = false;
                m.trail.emitting = false;
                m.go.SetActive(false);
                _numActive = Mathf.Max(0, _numActive - 1);
            }
        }
    }
}
