// Assets/Scripts/Logging/SessionLogger.cs
// 训练会话数据记录（设计文档 §5）：元数据(session.json) + 事件流(events.jsonl) + 周期采样(samples.csv)。
// 只记录数值与事件，绝不记录图像/音频（隐私 + Meta 数据政策要求）。
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class SessionLogger
{
    readonly StreamWriter _events;
    readonly StreamWriter _samples;
    readonly double _startTime;
    bool _sampleHeaderWritten;
    bool _closed;

    public string SessionDir { get; }

    public SessionLogger(string rootDir, string childName = "", string ipName = "", int interventionLevel = 1)
    {
        SessionDir = Path.Combine(rootDir, "sessions", BuildSessionFolderName(DateTime.Now));
        Directory.CreateDirectory(SessionDir);
        _startTime = Time.realtimeSinceStartup;

        // AutoFlush：每行立即落盘（训练中断/断电也不丢数据，且支持边跑边看）
        _events = new StreamWriter(Path.Combine(SessionDir, "events.jsonl"), false, Encoding.UTF8) { AutoFlush = true };
        _samples = new StreamWriter(Path.Combine(SessionDir, "samples.csv"), false, Encoding.UTF8) { AutoFlush = true };

        var meta = new StringBuilder();
        meta.Append('{');
        meta.Append("\"startedAt\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",");
        meta.Append("\"childName\":\"").Append(Escape(childName)).Append("\",");
        meta.Append("\"ipName\":\"").Append(Escape(ipName)).Append("\",");
        meta.Append("\"interventionLevel\":").Append(interventionLevel).Append(',');
        meta.Append("\"app\":\"ARAttentionTrainer\",");
        meta.Append("\"unity\":\"").Append(Application.unityVersion).Append("\"");
        meta.Append('}');
        File.WriteAllText(Path.Combine(SessionDir, "session.json"), meta.ToString(), Encoding.UTF8);
    }

    public float NowSeconds => (float)(Time.realtimeSinceStartup - _startTime);

    public void LogEvent(string type, string detail = "")
    {
        if (_closed) return;
        _events.WriteLine(
            $"{{\"t\":{NowSeconds.ToString("F2", CultureInfo.InvariantCulture)}," +
            $"\"type\":\"{Escape(type)}\",\"detail\":\"{Escape(detail)}\"}}");
    }

    public void LogSample(bool looking, bool speaking, bool targetValid, float fps, float inferMs)
    {
        if (_closed) return;
        if (!_sampleHeaderWritten)
        {
            _samples.WriteLine("t,looking,speaking,targetValid,fps,inferMs");
            _sampleHeaderWritten = true;
        }
        _samples.WriteLine(FormatSampleCsv(NowSeconds, looking, speaking, targetValid, fps, inferMs));
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _events.WriteLine(
            $"{{\"t\":{NowSeconds.ToString("F2", CultureInfo.InvariantCulture)},\"type\":\"SessionEnd\",\"detail\":\"\"}}");
        _events.Flush(); _events.Dispose();
        _samples.Flush(); _samples.Dispose();
    }

    // ── 静态函数（单元测试覆盖） ──────────────────────────────

    public static string FormatSampleCsv(float t, bool looking, bool speaking, bool targetValid, float fps, float inferMs)
        => string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2},{3},{4:F1},{5:F1}",
            t, looking ? 1 : 0, speaking ? 1 : 0, targetValid ? 1 : 0, fps, inferMs);

    public static string BuildSessionFolderName(DateTime t) => t.ToString("yyyyMMdd_HHmmss");

    static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
}
