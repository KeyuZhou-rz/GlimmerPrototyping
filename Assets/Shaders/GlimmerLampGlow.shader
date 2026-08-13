Shader "Glimmer/LampGlow"
{
    // 田鼠灯光晕（2026-08-13）：加色径向渐变片，由 VoleLampDriver 逐帧朝向相机并按夜相推 HDR 色。
    // 柔和晕染靠这张贴图的 alpha 衰减，不靠 Bloom——Bloom 只是锦上添花（阈值 1.0 之上再吃一层泛光）。
    Properties
    {
        [HDR] _Color ("Glow Color", Color) = (1, 0.7, 0.3, 1)
        _MainTex ("Glow (A)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One   // 加色：晕开的光，不遮背景
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            half4 _Color;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                // Blend SrcAlpha One：贡献 = rgb × alpha——alpha 即径向衰减，HDR rgb 喂 Bloom
                return half4(_Color.rgb, _Color.a * a);
            }
            ENDHLSL
        }
    }
}
