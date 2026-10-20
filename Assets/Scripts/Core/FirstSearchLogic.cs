// Assets/Scripts/Core/FirstSearchLogic.cs
// 首寻 3D 漫游 · 纯逻辑（2026-10-20）：
// 第一次找到老师前，系统没有老师锚点——精灵绕孩子 360° 真实漫游（可飞出视野），
// 孩子需要转头/转身跟随空间语音找人；转头过程中相机有机会扫到老师。
// 路径点按"孩子开始搜索时的位置+朝向"生成，之后不随头转（否则永远在视野内）。
using System.Collections.Generic;
using UnityEngine;

public struct SearchTickResult
{
    public bool active;
    public Vector3 target;
    public bool voiceDue;
    public int voiceSlot;
    public int waypointIndex;
    public bool waypointChanged;
}

public class FirstSearchLogic
{
    public bool Active { get; private set; }
    public Vector3 CurrentTarget { get; private set; }
    public int WaypointsVisited { get; private set; }
    public int VoiceCount { get; private set; }

    readonly float _moveSpeed, _radius, _holdSeconds, _voiceInterval;
    readonly List<Vector3> _waypoints = new List<Vector3>();
    int _index;
    float _legT, _legDuration, _voiceT;
    bool _firstVoicePending;

    public FirstSearchLogic(float moveSpeed = 1.4f, float radius = 1.6f,
                            float holdSeconds = 0.4f, float voiceInterval = 4f)
    {
        _moveSpeed = Mathf.Max(0.2f, moveSpeed);
        _radius = Mathf.Max(0.3f, radius);
        _holdSeconds = Mathf.Max(0f, holdSeconds);
        _voiceInterval = Mathf.Max(0.5f, voiceInterval);
    }

    /// <summary>开始搜索。childPos/childForward 是开始瞬间的头位/朝向；floorY=地板高度；room=房间边界。</summary>
    public void Begin(Vector3 childPos, Vector3 childForward, float floorY, Bounds room)
    {
        _waypoints.Clear();
        Vector3 fwd = new Vector3(childForward.x, 0f, childForward.z);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        // 绕一圈：前 → 左前 → 左 → 左后 → 后 → 右后 → 右 → 右前
        float[] angles = { 0f, -45f, -90f, -135f, 180f, 135f, 90f, 45f };
        float[] radiusMul = { 0.85f, 1.00f, 0.90f, 1.05f, 0.80f, 1.00f, 0.90f, 1.05f };
        float[] heights = { 1.15f, 0.95f, 1.40f, 1.10f, 1.50f, 1.00f, 1.35f, 1.20f };

        for (int i = 0; i < angles.Length; i++)
        {
            float rad = angles[i] * Mathf.Deg2Rad;
            Vector3 dir = fwd * Mathf.Cos(rad) + right * Mathf.Sin(rad);
            Vector3 p = childPos + dir * (_radius * radiusMul[i]);
            p.y = floorY + heights[i];
            p = ClampToRoom(p, room, floorY);
            _waypoints.Add(p);
        }

        Active = true;
        _index = 0;
        CurrentTarget = _waypoints[0];
        WaypointsVisited = 0;
        VoiceCount = 0;
        _legT = 0f;
        _voiceT = 0f;
        _firstVoicePending = true;
        _legDuration = Mathf.Max(1.2f, Vector3.Distance(childPos, CurrentTarget) / _moveSpeed) + _holdSeconds;
    }

    public SearchTickResult Tick(float dt)
    {
        var res = new SearchTickResult();
        if (!Active) return res;

        _legT += dt;
        bool changed = false;
        // while（而非 if）：大 dt（测试/掉帧）时也能正确追赶路径点
        int guard = 0;
        while (_legT >= _legDuration && guard++ < 32)
        {
            _legT -= _legDuration;
            Vector3 prev = _waypoints[_index];
            _index = (_index + 1) % _waypoints.Count;
            CurrentTarget = _waypoints[_index];
            WaypointsVisited++;
            changed = true;
            _legDuration = Mathf.Max(1.2f, Vector3.Distance(prev, CurrentTarget) / _moveSpeed) + _holdSeconds;
        }

        // 语音节拍：开场句 0.15s 后，之后每 voiceInterval 一句；槽位轮换
        bool due = false;
        _voiceT += dt;
        if (_firstVoicePending)
        {
            if (_voiceT >= 0.15f) { due = true; _firstVoicePending = false; _voiceT = 0f; }
        }
        else if (_voiceT >= _voiceInterval)
        {
            due = true;
            _voiceT = Mathf.Repeat(_voiceT, _voiceInterval);
        }
        if (due)
        {
            res.voiceDue = true;
            res.voiceSlot = VoiceCount % 5;
            VoiceCount++;
        }

        res.active = true;
        res.target = CurrentTarget;
        res.waypointIndex = _index;
        res.waypointChanged = changed;
        return res;
    }

    public void Stop()
    {
        Active = false;
    }

    /// <summary>测试/诊断：漫游路径点（只读副本）。</summary>
    public Vector3[] PreviewWaypoints() => _waypoints.ToArray();

    static Vector3 ClampToRoom(Vector3 p, Bounds room, float floorY)
    {
        const float margin = 0.35f;
        if (room.size.x > 2f * margin + 0.2f)
            p.x = Mathf.Clamp(p.x, room.min.x + margin, room.max.x - margin);
        if (room.size.z > 2f * margin + 0.2f)
            p.z = Mathf.Clamp(p.z, room.min.z + margin, room.max.z - margin);
        float yMin = floorY + 0.75f;
        float yMax = room.max.y - 0.25f;
        if (yMax > yMin) p.y = Mathf.Clamp(p.y, yMin, yMax);
        return p;
    }
}
