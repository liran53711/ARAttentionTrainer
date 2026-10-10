// Assets/Scripts/Core/MrukBootstrap.cs
// 启动阶段加载空间数据（MRUK）——为射线定距与 3D 精灵房间锚定铺垫。
// 流程（2026-10-10 实测定型；每步都有日志）：
//   ① 等 Scene 权限（安装已预授权）
//   ② 快速加载"已有"空间数据（15s 兜底）
//   ③ 没有数据 → 自动重试模式：每 30 秒重试、最长 10 分钟，面板明示引导。
//      —— 本机 OS 禁用了"应用内拉起扫描"（xrRequestSceneCaptureFB unsupported，
//         权限已声明且 granted 仍被平台门控）；数据来源=系统后台自动扫描（本地能力）
//         或用户主动走「设置→物理空间」扫描。
using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class MrukBootstrap : MonoBehaviour
{
    public bool HasRoomData { get; private set; }
    /// <summary>地板在世界坐标中的高度。</summary>
    public float FloorY { get; private set; }
    /// <summary>一行式状态（数据面板展示 + 日志）。</summary>
    public string Summary { get; private set; } = "初始化中…";

    IEnumerator Start()
    {
        // ① 等 Scene 权限（通常已由安装预授权；没授权时 PermissionFlow 会弹窗）
        float t = 0f;
        while (t < 45f && !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
        {
            Summary = "无 Scene 权限";
            Debug.LogWarning("[MRUK] 无 Scene 权限——空间数据不可用（其余功能不受影响）");
            yield break;
        }
        Debug.Log("[MRUK] ① Scene 权限 OK");

        // ② 确保 MRUK 实例存在
        if (MRUK.Instance == null)
        {
            var go = new GameObject("MRUK_Singleton");
            var mrukComp = go.AddComponent<MRUK>();
            // 裸组件时 SceneSettings 为 null → MRUK.Update() 每帧 NRE（实测刷屏）。
            // 赋默认实例并关闭自加载——加载完全由本组件显式控制。
            var mrukSettings = new MRUK.MRUKSettings();
            mrukSettings.LoadSceneOnStartup = false;
            mrukComp.SceneSettings = mrukSettings;
            yield return null;
            yield return null;
        }
        Debug.Log("[MRUK] ② MRUK 单例就绪");

        // ③ 先加载"已有"空间数据（快速路径，15s 兜底）
        Summary = "加载空间数据…";
        var task = MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
        float wait = 0f;
        while (!task.IsCompleted && wait < 15f) { wait += Time.deltaTime; yield return null; }
        if (task.IsCompleted && MRUK.Instance.GetCurrentRoom() != null)
        {
            OnRoomsReady(task.Result);
            yield break;
        }

        // ④ 没有数据 → 平台限制说明（2026-10-10 实测：本机 OS 对第三方应用禁用了
        //    "应用内拉起扫描"（xrRequestSceneCaptureFB unsupported，即使权限已声明且 granted）。
        //    现实通道：a) 系统后台自动扫描（AssistedSceneCapture，本地能力）；b) 设置→物理空间 手动扫。
        //    策略：后台每 30 秒重试加载，最长 10 分钟；数据一到自动就位。
        Debug.Log("[MRUK] ③ 无已存数据 → 进入自动重试模式（系统自动扫描 / 用户在系统设置里扫描）");
        Summary = "空间数据准备中：可戴着头显走动等待自动扫描，或在「设置→物理空间」扫描";

        float retryEnd = Time.realtimeSinceStartup + 600f;
        float nextTry = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < retryEnd)
        {
            yield return new WaitForSeconds(1f);
            if (Time.realtimeSinceStartup < nextTry) continue;
            nextTry = Time.realtimeSinceStartup + 30f;

            var t2 = MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            float w2 = 0f;
            while (!t2.IsCompleted && w2 < 10f) { w2 += Time.deltaTime; yield return null; }
            if (t2.IsCompleted && MRUK.Instance.GetCurrentRoom() != null)
            {
                OnRoomsReady(t2.Result);
                yield break;
            }
            Debug.Log($"[MRUK] 重试未就绪（{(t2.IsCompleted ? t2.Result.ToString() : "超时")}），30 秒后再试…");
        }
        Summary = "空间数据 10 分钟内未就绪——请在「设置→物理空间」扫描后重启";
        Debug.LogWarning("[MRUK] " + Summary);
    }

    /// <summary>当前房间的世界边界（含地板/天花板高度）——IP 世界锚定的"空间围栏"。
    /// 注意：无地板/天花板锚点时 MRUK 会返回全零 Bounds——此处直接拒绝。</summary>
    public bool TryGetRoomBounds(out Bounds bounds)
    {
        bounds = default;
        if (!HasRoomData || MRUK.Instance == null) return false;
        var room = MRUK.Instance.GetCurrentRoom();
        if (room == null) return false;
        var b = room.GetRoomBounds();
        if (b.size.sqrMagnitude < 0.01f) return false;
        bounds = b;
        return true;
    }

    void OnRoomsReady(MRUK.LoadDeviceResult result)
    {
        HasRoomData = true;
        var room = MRUK.Instance.GetCurrentRoom();
        var floor = room.FloorAnchor;
        FloorY = floor != null ? floor.transform.position.y : 0f;
        int anchors = room.Anchors != null ? room.Anchors.Count : 0;
        Summary = $"房间 ✓ {MRUK.Instance.Rooms.Count} 间 | 锚点 {anchors} | 地板 {FloorY:F2}m";
        Debug.Log($"[MRUK] ✅ {Summary}（{result}）");
    }
}
