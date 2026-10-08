// Assets/Scripts/Vad/AudioEnergy.cs
// 音频能量计算（纯静态，可单测）：采样 → RMS 分贝（dBFS）。
using UnityEngine;

public static class AudioEnergy
{
    /// <summary>把一段采样换算为 RMS 分贝（dBFS，范围被钳制到 [-80, 0]）。静音返回 -80。</summary>
    public static float ComputeRmsDb(float[] samples, int count = -1)
    {
        int n = count < 0 ? samples.Length : count;
        if (n <= 0) return -80f;

        double sum = 0;
        for (int i = 0; i < n; i++) sum += (double)samples[i] * samples[i];

        float rms = Mathf.Sqrt((float)(sum / n));
        if (rms <= 1e-5f) return -80f;
        return Mathf.Max(-80f, 20f * Mathf.Log10(rms));
    }
}
