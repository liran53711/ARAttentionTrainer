// Assets/Scripts/Vad/VadLogic.cs
// 语音活动检测（纯逻辑，可单测）：判断"教师在讲话"与"一段话讲完"。
// 原理：音量能量(dBFS) 与自适应噪声底比较——
//   进入讲话：energy > floor + enterMarginDb，且持续 enterDebounceSec（防尖峰）
//   结束讲话：energy < floor + exitMarginDb，且连续静音 endSilenceSec（桥接换气停顿）
//   短促杂音（有效发声 < minSpeechSec）→ 静默取消，不发"讲完"事件
// 噪声底：开场 2 秒静音校准；运行中向下快、向上慢（适应教室噪声漂移）。
using UnityEngine;

public class VadLogic
{
    readonly float _enterMarginDb;
    readonly float _exitMarginDb;
    readonly float _minSpeechSec;
    readonly float _endSilenceSec;
    readonly float _enterDebounceSec;

    const float CalibrateSeconds = 2f;
    bool _calibrating = true;
    float _calibrateSum;
    int _calibrateCount;
    float _calibrateTime;

    float _noiseFloorDb = -60f;
    float _aboveTime;        // 连续超过进入阈的时长
    float _silenceTime;      // 连续低于退出阈的时长
    float _speechActiveTime; // 本段语音"有效发声"累计

    public float NoiseFloorDb => _noiseFloorDb;
    public bool Speaking { get; private set; }
    public bool SpeechStarted { get; private set; } // 本帧瞬态
    public bool SpeechEnded { get; private set; }   // 本帧瞬态（"一段话讲完"）

    public VadLogic(float enterMarginDb = 8f, float exitMarginDb = 4f,
                    float minSpeechSec = 0.4f, float endSilenceSec = 0.5f,
                    float enterDebounceSec = 0.15f)
    {
        _enterMarginDb = enterMarginDb;
        _exitMarginDb = exitMarginDb;
        _minSpeechSec = minSpeechSec;
        _endSilenceSec = endSilenceSec;
        _enterDebounceSec = enterDebounceSec;
    }

    public void Reset()
    {
        _calibrating = true;
        _calibrateSum = 0f; _calibrateCount = 0; _calibrateTime = 0f;
        _noiseFloorDb = -60f;
        _aboveTime = 0f; _silenceTime = 0f; _speechActiveTime = 0f;
        Speaking = false; SpeechStarted = false; SpeechEnded = false;
    }

    public void Tick(float energyDb, float dt)
    {
        if (dt <= 0f) dt = 1f / 60f;
        SpeechStarted = false;
        SpeechEnded = false;

        // ── 开场校准 ──
        if (_calibrating)
        {
            _calibrateSum += energyDb;
            _calibrateCount++;
            _calibrateTime += dt;
            if (_calibrateTime < CalibrateSeconds) return;
            _noiseFloorDb = _calibrateSum / Mathf.Max(1, _calibrateCount);
            _noiseFloorDb = Mathf.Clamp(_noiseFloorDb, -80f, -20f);
            _calibrating = false;
        }

        if (!Speaking)
        {
            // 噪声底自适应：静音段向下快速跟踪、向上缓慢漂移
            if (energyDb < _noiseFloorDb)
                _noiseFloorDb = Mathf.Lerp(_noiseFloorDb, energyDb, Mathf.Clamp01(dt * 2f));
            else
                _noiseFloorDb += dt * 0.5f;
            _noiseFloorDb = Mathf.Clamp(_noiseFloorDb, -80f, -20f);

            if (energyDb > _noiseFloorDb + _enterMarginDb)
            {
                _aboveTime += dt;
                if (_aboveTime >= _enterDebounceSec)
                {
                    Speaking = true;
                    SpeechStarted = true;
                    _speechActiveTime = 0f;
                    _silenceTime = 0f;
                    _aboveTime = 0f;
                }
            }
            else _aboveTime = 0f;
        }
        else
        {
            if (energyDb > _noiseFloorDb + _exitMarginDb)
            {
                _speechActiveTime += dt;
                _silenceTime = 0f;
            }
            else
            {
                _silenceTime += dt;
                if (_silenceTime >= _endSilenceSec)
                {
                    bool realSpeech = _speechActiveTime >= _minSpeechSec;
                    Speaking = false;
                    if (realSpeech) SpeechEnded = true; // 短促杂音：静默取消，不发事件
                    _silenceTime = 0f;
                    _speechActiveTime = 0f;
                }
            }
        }
    }
}
