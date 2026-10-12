// Assets/Scripts/Editor/IpAnimatorBuilder.cs
// 生成组员精灵的 AnimatorController（2026-10-12）：
//   状态 = 交付包 8 个剪辑（名称逐字一致）；参数 = 项目侧 IPWorldView 驱动的一套：
//     Guiding(bool)   → IdleFly ↔ GuideFly
//     Companion(bool) → IdleFly ↔ CompanionIdle（与 Guiding 互斥，由驱动层保证）
//     Appear/Feedback/Peek(Trigger) → 一次性动画，播完回 IdleFly（bool 再接管）
//   循环剪辑（IdleFly/GuideFly/CompanionIdle）的循环标记已由 SpriteModelImporter 设好。
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class IpAnimatorBuilder
{
    const string Path = "Assets/Art/IP/CH_Sprite.controller";
    const string FbxPath = "Assets/Art/IP/CH_Sprite.fbx";

    [MenuItem("ARAttention/IP/Build Sprite Animator Controller")]
    public static AnimatorController Ensure()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(Path);
        if (existing != null) return existing;

        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        AnimationClip Clip(string n)
        {
            var c = clips.FirstOrDefault(x => x.name == n);
            if (c == null) Debug.LogWarning($"[IpAnim] 缺少剪辑 {n}——状态将为空");
            return c;
        }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Path);
        ctrl.AddParameter("Guiding", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Companion", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Appear", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Feedback", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Peek", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var idle = State(sm, "IdleFly", Clip("IdleFly"));
        var guide = State(sm, "GuideFly", Clip("GuideFly"));
        var comp = State(sm, "CompanionIdle", Clip("CompanionIdle"));
        var appear = State(sm, "AppearFly", Clip("AppearFly"));
        var feedback = State(sm, "HappyFeedback", Clip("HappyFeedback"));
        var peek = State(sm, "Peek", Clip("Peek"));
        sm.defaultState = idle;

        // 基础层互切（0.18s 交叉；bool 互斥由驱动层保证）
        Cond(sm, idle, guide, 0.18f, ("Guiding", true));
        Cond(sm, guide, idle, 0.18f, ("Guiding", false));
        Cond(sm, idle, comp, 0.18f, ("Companion", true));
        Cond(sm, comp, idle, 0.18f, ("Companion", false));
        Cond(sm, guide, comp, 0.18f, ("Companion", true));
        Cond(sm, comp, guide, 0.18f, ("Guiding", true));

        // 一次性动画（AnyState 触发，播完回 IdleFly → bool 条件自然接管）
        Any(sm, appear, "Appear", 0.12f);
        Any(sm, feedback, "Feedback", 0.12f);
        Any(sm, peek, "Peek", 0.12f);
        Back(sm, appear, idle, 0.9f, 0.15f);
        Back(sm, feedback, idle, 0.9f, 0.15f);
        Back(sm, peek, idle, 0.9f, 0.15f);

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log($"[IpAnim] 已生成 {Path}（状态 {sm.states.Length} 个）");
        return ctrl;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip)
    {
        var s = sm.AddState(name);
        s.motion = clip;
        s.writeDefaultValues = false;
        return s;
    }

    static void Cond(AnimatorStateMachine sm, AnimatorState from, AnimatorState to, float dur, (string, bool) c)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = dur;
        t.AddCondition(AnimatorConditionMode.If, 0f, c.Item1); // bool true
    }

    static void Any(AnimatorStateMachine sm, AnimatorState to, string trigger, float dur)
    {
        var t = sm.AddAnyStateTransition(to);
        t.hasExitTime = false;
        t.duration = dur;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    static void Back(AnimatorStateMachine sm, AnimatorState from, AnimatorState to, float exitTime, float dur)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = dur;
    }
}
