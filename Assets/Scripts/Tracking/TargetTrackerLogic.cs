// Assets/Scripts/Tracking/TargetTrackerLogic.cs
// 目标跟踪（纯逻辑）：从每帧检测结果里选出"老师"并维护其头部点。
// 策略（对应设计文档 §4.2.1 第1条 + §4.3）：
//   - 头部点 = 人框"顶部往下25%高度"处的水平中心（近似脸/头位置）
//   - 首次锁定选面积最大的框；此后用近邻匹配持续跟踪（老师移动是连续的）
//   - 滞回：出现明显更大的其他候选（面积 > 当前 × 1.25）才切换目标，防跳
//   - 丢失：<1s 保持位置不切换；≥1s 位置冻结（frozen）；≥10s 锚点失效（valid=false）
//   - 输出经 One Euro 滤波平滑，抑制检测框抖动
using System.Collections.Generic;
using UnityEngine;

/// <summary>目标快照：每帧跟踪器对外输出的全部信息。</summary>
public struct TargetSnapshot
{
    public bool valid;            // 锚点可用（含冻结期）
    public bool frozen;           // 丢失已超 lostKeepSec，位置为冻结值
    public Vector2 headPoint01;   // 平滑后的头部点（归一化，左上原点）
    public float confidence;      // 最近一次匹配的置信度
    public float lastSeenSeconds; // 距最近一次"检测到并匹配"的秒数
}

public class TargetTrackerLogic
{
    readonly float _switchHysteresis;
    readonly float _lostKeepSec;
    readonly float _idleDropSec;
    readonly float _matchRadius;
    readonly OneEuroFilter _fx = new OneEuroFilter();
    readonly OneEuroFilter _fy = new OneEuroFilter();

    bool _hasTarget;
    Vector2 _headPoint;
    float _confidence;
    float _lastSeen;

    public TargetTrackerLogic(float switchHysteresis = 1.25f, float lostKeepSec = 1f,
                              float idleDropSec = 10f, float matchRadius = 0.25f)
    {
        _switchHysteresis = switchHysteresis;
        _lostKeepSec = lostKeepSec;
        _idleDropSec = idleDropSec;
        _matchRadius = matchRadius;
    }

    public void Reset()
    {
        _hasTarget = false;
        _lastSeen = 0f;
        _confidence = 0f;
        _fx.Reset();
        _fy.Reset();
    }

    /// <summary>人框 → 头部点（框顶 + 25% 高度处，水平居中）。</summary>
    public static Vector2 HeadPointOf(Rect normalizedBox)
        => new Vector2(normalizedBox.x + normalizedBox.width * 0.5f,
                       normalizedBox.y + normalizedBox.height * 0.25f);

    static float AreaOf(Rect b) => b.width * b.height;

    public TargetSnapshot Tick(List<DetectionResult> detections, float dt)
    {
        if (dt <= 0f) dt = 1f / 30f;

        // 1) 最大面积候选
        int bestIdx = -1; float bestArea = -1f;
        for (int i = 0; i < detections.Count; i++)
        {
            float a = AreaOf(detections[i].normalizedBox);
            if (a > bestArea) { bestArea = a; bestIdx = i; }
        }

        // 2) 与当前目标做近邻匹配（老师的位置帧间是连续的）
        int matchIdx = -1; float matchDist = float.MaxValue;
        if (_hasTarget)
        {
            for (int i = 0; i < detections.Count; i++)
            {
                float d = Vector2.Distance(HeadPointOf(detections[i].normalizedBox), _headPoint);
                if (d <= _matchRadius && d < matchDist) { matchIdx = i; matchDist = d; }
            }
        }

        // 3) 决策：新锁定 / 切换 / 正常跟踪 / 本帧丢失
        int chosen = -1;
        bool snapped = false; // 需要把滤波器重置到新位置（避免平滑拖尾）
        if (!_hasTarget)
        {
            if (bestIdx >= 0) { chosen = bestIdx; snapped = true; }
        }
        else if (matchIdx >= 0)
        {
            float mArea = AreaOf(detections[matchIdx].normalizedBox);
            if (bestIdx >= 0 && bestIdx != matchIdx && bestArea > mArea * _switchHysteresis)
            { chosen = bestIdx; snapped = true; }
            else
            { chosen = matchIdx; }
        }
        // else: 本帧无匹配 → 走丢失路径

        // 4) 更新
        if (chosen >= 0)
        {
            var d = detections[chosen];
            var hp = HeadPointOf(d.normalizedBox);
            if (snapped) { _hasTarget = true; _fx.Reset(); _fy.Reset(); }
            _headPoint = new Vector2(_fx.Filter(hp.x, dt), _fy.Filter(hp.y, dt));
            _confidence = d.confidence;
            _lastSeen = 0f;
        }
        else
        {
            _lastSeen += dt;
        }

        var snap = new TargetSnapshot
        {
            valid = _hasTarget && _lastSeen < _idleDropSec,
            frozen = _hasTarget && _lastSeen >= _lostKeepSec && _lastSeen < _idleDropSec,
            headPoint01 = _headPoint,
            confidence = _confidence,
            lastSeenSeconds = _lastSeen,
        };

        if (!snap.valid) _hasTarget = false; // 彻底掉线，允许下次重新锁定
        return snap;
    }
}
