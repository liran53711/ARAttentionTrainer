// Assets/Scripts/Core/PermissionFlow.cs
// 真机权限流：前置相机（PassthroughCameraAccess）+ 空间数据（Scene，MRUK 用）。
// 在应用启动时请求，轮询等待用户在头显内完成授权（最长 60 秒）。
// ⚠️ 如果用户对权限弹窗"全部拒绝"，应用将无法恢复（需卸载重装）——
//    正式版 UI 需要在请求前用大字提示"请务必点击允许"。
// 备注：Scene 权限自 2026-10-10 起加入（MRUK 空间数据定位，为 3D 精灵铺垫）。
using System.Collections;
using UnityEngine;

public class PermissionFlow : MonoBehaviour
{
    public bool Ready { get; private set; }
    public bool CameraGranted { get; private set; }
    public bool SceneGranted { get; private set; }

    IEnumerator Start()
    {
        CameraGranted = OVRPermissionsRequester.IsPermissionGranted(
            OVRPermissionsRequester.Permission.PassthroughCameraAccess);
        SceneGranted = OVRPermissionsRequester.IsPermissionGranted(
            OVRPermissionsRequester.Permission.Scene);

        if (!CameraGranted || !SceneGranted)
        {
            var wanted = new System.Collections.Generic.List<OVRPermissionsRequester.Permission>();
            if (!CameraGranted) wanted.Add(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
            if (!SceneGranted) wanted.Add(OVRPermissionsRequester.Permission.Scene);
            OVRPermissionsRequester.Request(wanted);

            float t = 0f;
            while (t < 60f)
            {
                CameraGranted = OVRPermissionsRequester.IsPermissionGranted(
                    OVRPermissionsRequester.Permission.PassthroughCameraAccess);
                SceneGranted = OVRPermissionsRequester.IsPermissionGranted(
                    OVRPermissionsRequester.Permission.Scene);
                if (CameraGranted && SceneGranted) break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        Ready = CameraGranted && SceneGranted;
        Debug.Log($"[Permission] Ready={Ready} (Camera={CameraGranted}, Scene={SceneGranted})");
    }
}
