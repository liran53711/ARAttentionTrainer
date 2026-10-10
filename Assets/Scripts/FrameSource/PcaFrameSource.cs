// Assets/Scripts/FrameSource/PcaFrameSource.cs
// 真机帧源：Quest 3 前置相机（Meta MRUK 的 PassthroughCameraAccess 组件）。
// 关键点（都来自组件源码 Meta.XR.PassthroughCameraAccess）：
//   · 取帧的同时缓存相机位姿——异步推理期间头部会转动，用"当前"位姿会漂移
//   · 焦距单位是"传感器像素"，按纹理/传感器分辨率比例换算成纹理像素
//   · 需要 horizonos.permission.HEADSET_CAMERA 权限（配合 PermissionFlow）
using Meta.XR;
using UnityEngine;

[RequireComponent(typeof(PassthroughCameraAccess))]
public class PcaFrameSource : FrameSourceBehaviour
{
    PassthroughCameraAccess _camera;

    void Awake()
    {
        _camera = GetComponent<PassthroughCameraAccess>();
    }

    public override bool IsReady => _camera != null && _camera.IsPlaying;

    public override bool TryGetFrame(out CameraFrame frame)
    {
        frame = default;
        if (_camera == null || !_camera.IsPlaying) return false;

        var tex = _camera.GetTexture();
        if (tex == null) return false;

        var intrinsics = _camera.Intrinsics;

        frame.texture = tex;
        frame.cameraPose = _camera.GetCameraPose(); // 帧同步位姿（不要用实时头显位姿）
        float scale = intrinsics.SensorResolution.x > 0
            ? tex.width / (float)intrinsics.SensorResolution.x
            : 1f;
        frame.focalLengthPx = intrinsics.FocalLength.x * scale;
        frame.timestampNs = _camera.Timestamp.Ticks * 100L; // DateTime tick(100ns) → ns（仅用于排序/去重）
        frame.valid = true;
        return true;
    }
}
