// Assets/Scripts/Core/OneEuroFilter.cs
// One Euro Filter：低延迟的实时平滑滤波（Casiez et al. 2012）。
// 用途：抹平检测框/头部点的抖动，同时保留真实运动（滤波器截止频率随速度自适应——
// 慢速时强平滑（去抖），快速时弱平滑（跟得上））。
// 纯逻辑类，由 EditMode 单元测试覆盖。
using UnityEngine;

public class OneEuroFilter
{
    readonly float _minCutoff;  // 最小截止频率（Hz）：越小越平滑
    readonly float _beta;       // 速度系数：越大越"跟手"
    readonly float _dCutoff;    // 导数信号的截止频率（Hz）
    bool _initialized;
    float _xPrev;
    float _dxPrev;

    public OneEuroFilter(float minCutoff = 1f, float beta = 0.05f, float dCutoff = 1f)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
    }

    public void Reset()
    {
        _initialized = false;
        _xPrev = 0f;
        _dxPrev = 0f;
    }

    static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + tau / dt);
    }

    /// <summary>输入新采样值，返回滤波后的值。dt 为距上次采样的秒数。</summary>
    public float Filter(float x, float dt)
    {
        if (dt <= 0f) dt = 1f / 60f;

        if (!_initialized)
        {
            _initialized = true;
            _xPrev = x;
            _dxPrev = 0f;
            return x;
        }

        // 1) 估计并平滑导数（速度）
        float dx = (x - _xPrev) / dt;
        float aD = Alpha(_dCutoff, dt);
        _dxPrev = aD * dx + (1f - aD) * _dxPrev;

        // 2) 速度越大 → 截止频率越高 → 越跟手
        float cutoff = _minCutoff + _beta * Mathf.Abs(_dxPrev);
        float a = Alpha(cutoff, dt);
        _xPrev = a * x + (1f - a) * _xPrev;
        return _xPrev;
    }
}
