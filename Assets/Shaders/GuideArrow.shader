// Assets/Shaders/GuideArrow.shader
// 引导箭头专用（2026-10-12 用户定调：像"路"一样蜿蜒、半透明、材质后续再调）：
//   · 顶点色 alpha 渐变（尾淡头浓）× _Color.a（整体透明度，运行时脉动）
//   · 半透明混合、不写深度、双面（Cull Off，无所谓顶点绕序）
//   · UnityCG 内置 idiom（与 MaskErase.shader 同套，构建内稳定）
Shader "ARAttention/GuideArrow"
{
    Properties
    {
        _Color ("整体颜色（含整体透明度）", Color) = (1, 0.84, 0.32, 0.85)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(_Color.rgb, _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
