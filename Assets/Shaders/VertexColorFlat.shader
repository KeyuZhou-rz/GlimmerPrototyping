Shader "Custom/VertexColorFlat"
{
    Properties { }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // ← 读顶点色
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 简单的 Lambert 光照 + 顶点颜色
                InputData lightingInput = (InputData)0;
                lightingInput.normalWS        = normalize(IN.normalWS);
                lightingInput.positionWS      = IN.positionWS;
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                lightingInput.shadowCoord     = float4(0, 0, 0, 0);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo    = IN.color.rgb;   // ← 顶点色作为反照率
                surfaceData.alpha     = 1.0;
                surfaceData.smoothness = 0.0;           // 哑光，更适合低多边形风格

                return UniversalFragmentPBR(lightingInput, surfaceData);
            }
            ENDHLSL
        }
        
        // Shadow caster pass（让地形能投射阴影）
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}