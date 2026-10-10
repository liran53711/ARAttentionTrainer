// Assets/Scripts/Core/PermissionFlow.cs
// 真机权限流：前置相机（PassthroughCameraAccess）。
// 在应用启动时请求，轮询等待用户在头显内完成授权（最长 60 秒）。
// ⚠️ 如果用户对权限弹窗"全部拒绝"，应用将无法恢复（需卸载重装）——
//    正式版 UI 需要在请求前用大字提示"请务必点击允许"。
// 备注：Scene（空间数据）权限等用到 MRUK 锚点时再加，M1 阶段只要相机权限（少一个弹窗）。
using System.Collections;
using UnityEngine;

public class PermissionFlow : MonoBehaviour
{
    public bool Ready { get; private set; }
    public bool CameraGranted { get; private set; }

    IEnumerator Start()
    {
        CameraGranted = OVRPermissionsRequester.IsPermissionGranted(
            OVRPermissionsRequester.Permission.PassthroughCameraAccess);

        if (!CameraGranted)
        {
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.PassthroughCameraAccess,
            });

            float t = 0f;
            while (t < 60f)
            {
                CameraGranted = OVRPermissionsRequester.IsPermissionGranted(
                    OVRPermissionsRequester.Permission.PassthroughCameraAccess);
                if (CameraGranted) break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        Ready = CameraGranted;
        Debug.Log($"[Permission] Ready={Ready} (Camera={CameraGranted})");
    }
}
