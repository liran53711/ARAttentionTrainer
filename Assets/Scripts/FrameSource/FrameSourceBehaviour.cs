// Assets/Scripts/FrameSource/FrameSourceBehaviour.cs
// 帧源的抽象基类：让"帧源"可以作为 MonoBehaviour 字段被 Inspector 引用
//（接口 IFrameSource 本身无法序列化，所以用抽象基类桥接）。
using UnityEngine;

public abstract class FrameSourceBehaviour : MonoBehaviour, IFrameSource
{
    public abstract bool IsReady { get; }
    public abstract bool TryGetFrame(out CameraFrame frame);
}
