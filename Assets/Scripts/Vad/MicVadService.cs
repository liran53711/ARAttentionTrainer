// Assets/Scripts/Vad/MicVadService.cs
// 麦克风采集 → 能量计算 → VadLogic。PC 和 Quest 通用。
// 真机注意：需要 RECORD_AUDIO 权限；Quest 有"授权后首次运行无音频"的已知问题，
// 对策（M3.5 真机阶段）：在加载场景先请求权限，授权完成后再启动采集。
using UnityEngine;

public class MicVadService : MonoBehaviour
{
    public VadLogic Logic { get; private set; }
    public float LastEnergyDb { get; private set; } = -80f;
    public bool MicRunning { get; private set; }

    const int SampleRate = 16000;   // Quest 麦克风原生采样率
    const int WindowSamples = 1024; // 能量计算窗口（约64ms @16kHz）

    AudioClip _clip;
    readonly float[] _buffer = new float[WindowSamples];

    void Awake()
    {
        Logic = new VadLogic();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif

        try
        {
            _clip = Microphone.Start(null, true, 1, SampleRate);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VAD] Microphone.Start 异常：{e.Message}");
        }
        MicRunning = _clip != null && Microphone.IsRecording(null);
        if (MicRunning)
            Debug.Log("[VAD] 麦克风已启动（前2秒为噪声底校准，请保持安静）");
        else
            Debug.LogWarning("[VAD] 麦克风启动失败——驱动层将回退为键盘模拟讲话");
    }

    void Update()
    {
        if (!MicRunning || _clip == null || !Microphone.IsRecording(null)) return;

        int pos = Microphone.GetPosition(null);
        if (pos < WindowSamples) return;
        _clip.GetData(_buffer, pos - WindowSamples);
        LastEnergyDb = AudioEnergy.ComputeRmsDb(_buffer);
        Logic.Tick(LastEnergyDb, Time.deltaTime);
    }

    void OnDestroy()
    {
        if (MicRunning) Microphone.End(null);
    }
}
