// Assets/Shaders/GuideArrow.shader
// 引导箭头专用（2026-10-12 用户定调：像"路"一样蜿蜒、半透明、材质后续再调）：
//   · 顶点色 alpha 渐变（尾淡头浓）× _Color.a（整体透明度，运行时脉动）
//   · 半透明混合、不写深度、双面（Cull Off，无所谓顶点绕序）
//   · UnityCG 内置 idiom（与 MaskErase.shader 同套，构建内稳定）
Shader "ARAttention/GuideArrow"
{
    Properties
    {
        _Color ("整体颜色（含整体透明度）", Color) = (1, 1, 1, 1)
        _MainTex ("设计贴图（ArrowRibbon）", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // 标准透明混合（缺失会导致 alpha 全被忽略——2026-10-12 踩过：所有层实心绘制"糊成一片"）
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 贴图（sRGB 由采样硬件自动转 Linear）× 整体染色/淡出 × 顶点 alpha（头淡入）
                fixed4 t = tex2D(_MainTex, i.uv);
                return fixed4(_Color.rgb * t.rgb * i.color.rgb, _Color.a * t.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
