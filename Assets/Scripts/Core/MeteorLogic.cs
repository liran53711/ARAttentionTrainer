// Assets/Scripts/Core/MeteorLogic.cs
// 流星引导 · 纯逻辑层（2026-10-20，可脱离 Unity 运行、测试机/单测直接驱动）：
//   ① 贝塞尔轨迹（流星弧线、星尘路径绕行）
//   ② 星环槽位（围绕老师的 8 个落星位置）
//   ③ 星星进度（StarProgressLogic：注视 2.5s/颗 → 8 颗集满 → 流星雨；星星只增不减，
//      走神不清零；老师长时间（默认 15s）不在才清空星环）
using UnityEngine;

public static class MeteorLogic
{
    /// <summary>二次贝塞尔采样。</summary>
    public static Vector3 Bezier(Vector3 p0, Vector3 ctrl, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * ctrl + t * t * p2;
    }

    /// <summary>星环槽位：老师脸周围一圈（默认 8 个），环面朝向观察者。
    /// slot: 0..count-1（角度均布）；返回世界坐标。</summary>
    public static Vector3 RingSlot(Vector3 teacherPos, Vector3 viewerPos, int slot, int count, float radius,
                                   float verticalSquash = 0.8f)
    {
        Vector3 fwd = teacherPos - viewerPos;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float ang = Mathf.PI * 2f * slot / Mathf.Max(1, count);
        return teacherPos + right * (Mathf.Cos(ang) * radius)
                          + Vector3.up * (Mathf.Sin(ang) * radius * verticalSquash);
    }

    /// <summary>星尘路径控制点：从 start（孩子前方地面）到 end（老师身旁地面），
    /// 向"远离孩子"的一侧弯，避免路径穿过孩子身体。detour=最小绕行距离。</summary>
    public static Vector3 PathControl(Vector3 childPos, Vector3 start, Vector3 end, float detour = 0.9f)
    {
        Vector3 mid = (start + end) * 0.5f;
        Vector3 dir = end - start;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return mid + Vector3.up * 0.02f;
        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
        Vector3 toChild = childPos - start;
        toChild.y = 0f;
        float sideDot = Vector3.Dot(side, toChild);
        // 孩子在线段的哪一侧，就往另一侧绕
        float sign = sideDot > 0f ? -1f : 1f;
        float extra = Mathf.Max(detour, 0.9f - Mathf.Abs(sideDot)); // 孩子越接近路径，绕行越大
        return mid + side * (sign * extra);
    }
}

/// <summary>星星进度事件。</summary>
public enum StarEvent { None, StarDropped, ShowerTriggered, Cleared }

/// <summary>星星进度（纯逻辑）：注视老师累计 2.5s → 落一颗星；8 颗 → 流星雨。
/// 星星只增不减（走神不清零）；老师离开超过 clearAfter 秒 → 清空星环（新会话）。</summary>
public class StarProgressLogic
{
    public int LitStars { get; private set; }
    public int StarsPerRing { get; }
    public float PerStarSeconds { get; }
    public float StarProgress01 => Mathf.Clamp01(_sinceStar / PerStarSeconds);
    public bool InCooldown => _cooldown > 0f;

    float _sinceStar;     // 当前这颗星的注视累计
    float _cooldown;
    float _notLookingT;   // 连续未注视时长（用于清空判定）

    readonly float _cooldownSeconds;
    readonly float _clearAfterSeconds;

    public StarProgressLogic(float perStarSeconds = 2.5f, int starsPerRing = 8,
                             float cooldownSeconds = 8f, float clearAfterSeconds = 15f)
    {
        PerStarSeconds = Mathf.Max(0.2f, perStarSeconds);
        StarsPerRing = Mathf.Max(1, starsPerRing);
        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        _clearAfterSeconds = Mathf.Max(1f, clearAfterSeconds);
    }

    public void Reset()
    {
        LitStars = 0; _sinceStar = 0f; _cooldown = 0f; _notLookingT = 0f;
    }

    /// <summary>每帧推进。lookingAtTeacher=人脸居中且属于人体（假脸已过滤）。</summary>
    public StarEvent Tick(float dt, bool lookingAtTeacher)
    {
        if (_cooldown > 0f)
        {
            _cooldown = Mathf.Max(0f, _cooldown - dt);
            return StarEvent.None;
        }
        if (!lookingAtTeacher)
        {
            _notLookingT += dt;
            if (_notLookingT >= _clearAfterSeconds && LitStars > 0)
            {
                Reset();
                return StarEvent.Cleared; // 老师长时间不在 → 星环清空（重新开始）
            }
            return StarEvent.None;        // 走神：不推进、不衰减（星星只增不减）
        }
        _notLookingT = 0f;
        _sinceStar += dt;
        if (_sinceStar >= PerStarSeconds)
        {
            _sinceStar -= PerStarSeconds;
            LitStars++;
            if (LitStars >= StarsPerRing)
            {
                LitStars = 0;              // 满环 → 流星雨 → 冷却 → 重新积累
                _cooldown = _cooldownSeconds;
                return StarEvent.ShowerTriggered;
            }
            return StarEvent.StarDropped;
        }
        return StarEvent.None;
    }
}
