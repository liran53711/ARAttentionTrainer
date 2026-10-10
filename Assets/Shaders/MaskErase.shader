// Assets/Shaders/MaskErase.shader
// A 项：屏幕空间"人物擦除"——让精灵/画线被真实人体轮廓遮挡（"躲到人身后"）。
// 原理：全屏四边形，在精灵之后渲染；用单应矩阵把屏幕坐标映射到摄像头掩码坐标，
//       取掩码值 m；混合模式 Blend Zero OneMinusSrcAlpha → 目标色与 alpha 都乘以 (1-m)
//       → 掩码覆盖处（真人轮廓内）精灵被擦除，透视实景透出 = 精灵"在真人身后"。
Shader "ARAttention/MaskErase"
{
    Properties
    {
        _MaskTex ("Mask (selfie seg)", 2D) = "black" {}
        _EdgeLow ("Edge low", Range(0,1)) = 0.35
        _EdgeHigh ("Edge high", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        // dst = dst * (1 - srcAlpha)：src 色无所谓（Factor=Zero）
        Blend Zero OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MaskTex;
            // 屏幕UV(左下原点) → 掩码UV(左上原点) 的单应矩阵，按三行显式传入（避开矩阵上传约定坑）
            float3 _Row0, _Row1, _Row2;
            float _EdgeLow;
            float _EdgeHigh;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;   // 屏幕 UV（0-1，左下原点）
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = ComputeScreenPos(o.pos).xy / max(1e-5, o.pos.w); // 屏幕UV(左下原点)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 屏幕UV(左下原点) → 掩码UV(左上原点)
                float2 s = float2(i.uv.x, 1.0 - i.uv.y);
                float3 sw = float3(s, 1.0);
                float3 h = float3(dot(_Row0, sw), dot(_Row1, sw), dot(_Row2, sw));
                float2 muv = h.xy / max(1e-5, abs(h.z));

                // 掩码视锥外（PCA 相机看不到的方向）不擦除
                float inside = step(0.0, muv.x) * step(muv.x, 1.0) * step(0.0, muv.y) * step(muv.y, 1.0);
                float m = tex2D(_MaskTex, saturate(muv)).r;
                float cut = smoothstep(_EdgeLow, _EdgeHigh, m) * inside;

                // 颜色任意（被 Zero 系数丢弃），alpha = 擦除强度
                return fixed4(1, 1, 1, cut);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
