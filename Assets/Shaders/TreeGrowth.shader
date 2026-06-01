Shader "GlimmerDiary/TreeGrowth"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.4, 0.26, 0.13, 1)
        _GrowthProgress ("Growth", Range(0,1)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.2)) = 0.05
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;  // Growth encoding
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float growth : TEXCOORD1;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _GrowthProgress;
                float _EdgeSoftness;
            CBUFFER_END
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float vertexGrowth = IN.uv2.y;
                float mask = smoothstep(
                    _GrowthProgress - _EdgeSoftness,
                    _GrowthProgress + _EdgeSoftness,
                    vertexGrowth
                );
                
                // Scale down vertices that haven't "grown" yet
                float3 scaledPos = IN.positionOS.xyz * lerp(0.001, 1.0, 1.0 - mask);
                
                OUT.positionCS = TransformObjectToHClip(scaledPos);
                OUT.uv = IN.uv;
                OUT.growth = mask;
                
                return OUT;
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                // Clip pixels that haven't grown
                clip(0.5 - IN.growth);
                
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}