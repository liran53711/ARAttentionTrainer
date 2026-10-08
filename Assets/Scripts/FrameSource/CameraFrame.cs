// Assets/Scripts/FrameSource/CameraFrame.cs
// 所有帧源的统一输出结构：相机画面 + 拍下这一帧时的位姿和内参。
// 关键：cameraPose 必须与帧同步缓存——异步推理期间头部在转动，
//       如果用"当前"位姿去算框的位置，画面和框就会错位。
using UnityEngine;

/// <summary>单帧数据：纹理 + 拍照时刻的位姿 + 简化内参。</summary>
public struct CameraFrame
{
    public Texture texture;        // GPU 纹理（来自真机相机 / 视频文件 / 网络摄像头）
    public Pose cameraPose;        // 拍照时刻的相机位姿（必须与帧同步缓存）
    public float focalLengthPx;    // 简化内参：像素焦距（3D 定位与距离估算要用）
    public long timestampNs;       // 拍照时刻（纳秒）
    public bool valid;             // 本帧是否有效
}
