using System;
using UnityEngine;

// Attach to the imported model root (+Z forward). Run after Animator.
[DefaultExecutionOrder(1000)]
public sealed class SpriteExpressionController : MonoBehaviour
{
    [SerializeField] SkinnedMeshRenderer face;
    [SerializeField] Transform headExpression;
    [SerializeField] bool autoBlink = true;
    [SerializeField, Range(0, 20)] float curiousTiltDegrees = 10f;
    static readonly string[] Names = { "Smile", "Surprised", "Curious", "Encourage", "Blink" };
    readonly float[] weights = new float[5];
    readonly int[] indices = new int[5];
    readonly int[] corrections = new int[3];
    Quaternion restRotation;
    Vector3 tiltAxis;
    float nextBlink, blinkStart = -100f;

    void Awake()
    {
        if (!face)
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh && renderer.sharedMesh.GetBlendShapeIndex("Blink") >= 0) { face = renderer; break; }
        if (!headExpression)
            foreach (var bone in GetComponentsInChildren<Transform>(true))
                if (bone.name == "HeadExpression") { headExpression = bone; break; }
        if (!face || !headExpression) { Debug.LogError("Sprite face or HeadExpression is missing.", this); enabled = false; return; }
        for (int i = 0; i < Names.Length; i++) indices[i] = face.sharedMesh.GetBlendShapeIndex(Names[i]);
        string[] helperNames = { "__BlinkSurprised", "__BlinkCurious", "__BlinkEncourage" };
        for (int i = 0; i < 3; i++) corrections[i] = face.sharedMesh.GetBlendShapeIndex(helperNames[i]);
        foreach (int index in indices) if (index < 0) { Debug.LogError("Missing required sprite expression.", this); enabled = false; return; }
        foreach (int index in corrections) if (index < 0) { Debug.LogError("Missing blink corrective target.", this); enabled = false; return; }
        restRotation = headExpression.localRotation;
        // Derive the axis from the imported model, independent of FBX bone-axis conversion.
        tiltAxis = headExpression.InverseTransformDirection(transform.forward).normalized;
        nextBlink = Time.time + 3.4f;
    }

    // Smile/Surprised/Curious/Encourage may blend; Blink is an independent override.
    public void SetExpression(string name, float weight01)
    {
        int index = Array.IndexOf(Names, name);
        if (index < 0) throw new ArgumentException("Unknown sprite expression: " + name);
        weights[index] = float.IsNaN(weight01) ? 0f : Mathf.Clamp01(weight01);
    }

    public void ClearExpressions() { Array.Clear(weights, 0, weights.Length); }

    void LateUpdate()
    {
        if (autoBlink && Time.time >= nextBlink) { blinkStart = Time.time; nextBlink = Time.time + 4.2f; }
        float phase = (Time.time - blinkStart) / .24f;
        float automatic = autoBlink && phase >= 0f && phase <= 1f ? 1f - Mathf.Abs(2f * phase - 1f) : 0f;
        float blink = Mathf.Max(weights[4], automatic);
        float sum = weights[0] + weights[1] + weights[2] + weights[3];
        float normalize = 1f / Mathf.Max(1f, sum);
        for (int i = 0; i < 4; i++) face.SetBlendShapeWeight(indices[i], weights[i] * normalize * 100f);
        face.SetBlendShapeWeight(indices[4], blink * 100f);
        for (int i = 0; i < 3; i++) face.SetBlendShapeWeight(corrections[i], weights[i + 1] * normalize * blink * 100f);
        // Always compose from the cached rest pose: repeated frames never accumulate roll.
        headExpression.localRotation = restRotation * Quaternion.AngleAxis(curiousTiltDegrees * weights[2] * normalize, tiltAxis);
    }

    void OnDisable()
    {
        if (face) for (int i = 0; i < face.sharedMesh.blendShapeCount; i++) face.SetBlendShapeWeight(i, 0f);
        if (headExpression && tiltAxis.sqrMagnitude > 0f) headExpression.localRotation = restRotation;
    }
}
