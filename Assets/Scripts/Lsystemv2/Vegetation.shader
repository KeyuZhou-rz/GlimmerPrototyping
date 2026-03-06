// GlimmerDiary Vegetation Shader
// Supports: Wind animation, growth, bark/foliage modes, subsurface scattering
// Compatible with URP

Shader "GlimmerDiary/Vegetation"
{
    Properties
    {
        [Header(Base)]
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        
        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
        
        [Header(Growth Animation)]
        _GrowthProgress ("Growth Progress", Range(0, 1)) = 1
        _GrowthEdge ("Growth Edge Softness", Range(0.01, 0.3)) = 0.05
        
        [Header(Wind)]
        [Toggle] _EnableWind ("Enable Wind", Float) = 1
        _WindStrength ("Wind Strength", Range(0, 2)) = 1
        _WindFrequency ("Wind Frequency", Range(0.1, 5)) = 1
        _TrunkStiffness ("Trunk Stiffness", Range(0, 1)) = 0.8
        
        [Header(Subsurface Scattering)]
        [Toggle] _EnableSSS ("Enable SSS (for leaves)", Float) = 0
        _SSSColor ("SSS Color", Color) = (0.5, 0.8, 0.3, 1)
        _SSSStrength ("SSS Strength", Range(0, 1)) = 0.5
        _Translucency ("Translucency", Range(0, 1)) = 0.3
        
        [Header(Alpha)]
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        
        [Header(Vertex Colors)]
        [Toggle] _UseVertexColor ("Use Vertex Color for Wind Mask", Float) = 1
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        
        // Global wind properties (set by WindSystem.cs)
        float4 _GlobalWind;
        float _GlobalWindStrength;
        float _GlobalWindFrequency;
        float _GlobalWindGust;
        float _GlobalTime;
        
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _NormalStrength;
            float _Smoothness;
            float _Metallic;
            float _GrowthProgress;
            float _GrowthEdge;
            float _EnableWind;
            float _WindStrength;
            float _WindFrequency;
            float _TrunkStiffness;
            float _EnableSSS;
            float4 _SSSColor;
            float _SSSStrength;
            float _Translucency;
            float _AlphaClip;
            float _Cutoff;
            float _UseVertexColor;
        CBUFFER_END
        
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);
        
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 uv2 : TEXCOORD1;  // Growth data (x = branch depth, y = normalized position)
            float4 color : COLOR;     // Wind mask in R channel
        };
        
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
            float4 tangentWS : TEXCOORD3;
            float3 viewDirWS : TEXCOORD4;
            float growth : TEXCOORD5;
            float4 vertexColor : COLOR;
        };
        
        // Simple noise function
        float hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }
        
        float noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            
            float a = hash(i);
            float b = hash(i + float2(1.0, 0.0));
            float c = hash(i + float2(0.0, 1.0));
            float d = hash(i + float2(1.0, 1.0));
            
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }
        
        // Wind displacement calculation
        float3 CalculateWind(float3 positionOS, float3 positionWS, float windMask, float height)
        {
            if (_EnableWind < 0.5) return float3(0, 0, 0);
            
            float time = _GlobalTime * _WindFrequency * _GlobalWindFrequency;
            
            // Get wind direction and strength
            float3 windDir = normalize(_GlobalWind.xyz + float3(0.001, 0, 0.001));
            float windStr = _GlobalWindStrength * _WindStrength;
            
            // Height-based influence (more movement at top)
            float heightFactor = saturate(height) * windMask;
            
            // Trunk stiffness reduces base movement
            heightFactor *= lerp(1.0, 0.3, _TrunkStiffness * (1.0 - height));
            
            // Primary sway
            float primaryWave = sin(time + positionWS.x * 0.5 + positionWS.z * 0.3);
            
            // Secondary detail movement
            float detailWave = sin(time * 2.3 + positionWS.x * 1.5) * 0.3;
            detailWave += cos(time * 1.7 + positionWS.z * 1.2) * 0.2;
            
            // Gust influence
            float gustInfluence = _GlobalWindGust * 2.0;
            
            // Combine waves
            float totalWave = (primaryWave + detailWave) * heightFactor * windStr;
            totalWave += gustInfluence * heightFactor;
            
            // Apply displacement
            float3 displacement = windDir * totalWave;
            
            // Add some vertical bounce
            displacement.y = -abs(totalWave) * 0.2 * heightFactor;
            
            return displacement;
        }
        
        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            
            // Get growth data from UV2
            float growthValue = IN.uv2.y;
            float branchDepth = IN.uv2.x;
            
            // Growth animation - vertices beyond growth progress collapse to origin
            float growthMask = smoothstep(
                _GrowthProgress - _GrowthEdge,
                _GrowthProgress + _GrowthEdge,
                growthValue
            );
            // When growth is fully complete, disable collapsing so tips fully expand
            growthMask *= (1.0 - step(1.0, _GrowthProgress));

            float3 positionOS = IN.positionOS.xyz;

            // Phase 1: Y-scale — sapling grows up from ground (covers 0→0.2 of progress)
            float heightPhase = smoothstep(0.0, 0.2, _GrowthProgress);
            positionOS.y *= max(0.001, heightPhase);

            // Phase 2: XZ shrink to trunk axis — ungrown branches stay on the trunk axis
            float3 trunkPoint = float3(0.0, positionOS.y, 0.0);
            positionOS = lerp(positionOS, trunkPoint, growthMask);
            
            // Wind mask from vertex color (R channel) or UV2
            float windMask = _UseVertexColor > 0.5 ? IN.color.r : branchDepth;
            
            // Get world position for wind calculation
            float3 positionWS = TransformObjectToWorld(positionOS);
            
            // Calculate and apply wind
            float height = IN.uv2.y;  // Use growth position as height
            float3 windOffset = CalculateWind(positionOS, positionWS, windMask, height);
            positionWS += windOffset;
            
            // Transform to clip space
            OUT.positionCS = TransformWorldToHClip(positionWS);
            OUT.positionWS = positionWS;
            
            // Transform normal and tangent
            VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
            OUT.normalWS = normalInputs.normalWS;
            OUT.tangentWS = float4(normalInputs.tangentWS, IN.tangentOS.w);
            
            OUT.viewDirWS = GetWorldSpaceViewDir(positionWS);
            OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
            OUT.growth = growthMask;
            OUT.vertexColor = IN.color;
            
            return OUT;
        }
        
        // Subsurface scattering approximation
        float3 CalculateSSS(float3 normalWS, float3 viewDirWS, Light light)
        {
            if (_EnableSSS < 0.5) return float3(0, 0, 0);
            
            // Wrap lighting for soft transmission
            float NdotL = dot(normalWS, light.direction);
            float wrap = saturate((NdotL + _Translucency) / (1.0 + _Translucency));
            
            // View-dependent transmission
            float3 H = normalize(light.direction + normalWS * 0.5);
            float VdotH = pow(saturate(dot(viewDirWS, -H)), 3.0);
            
            float3 sss = _SSSColor.rgb * (wrap + VdotH * _Translucency) * _SSSStrength;
            sss *= light.color * light.shadowAttenuation;
            
            return sss;
        }
        
        ENDHLSL
        
        // Main forward pass
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back  // Geometry generates explicit back faces with correct normals
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            half4 frag(Varyings IN) : SV_Target
            {
                // Alpha clip for growth animation
                if (_AlphaClip > 0.5)
                {
                    clip(0.5 - IN.growth);
                }
                
                // Sample textures
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                
                // Apply vertex color tint if present
                albedo.rgb *= lerp(float3(1,1,1), IN.vertexColor.rgb, IN.vertexColor.a > 0.01 ? 0.5 : 0.0);
                
                // Alpha cutoff
                if (_AlphaClip > 0.5)
                {
                    clip(albedo.a - _Cutoff);
                }
                
                // Normal mapping
                float3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv),
                    _NormalStrength
                );
                
                float3 bitangent = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
                float3x3 TBN = float3x3(IN.tangentWS.xyz, bitangent, IN.normalWS);
                float3 normalWS = normalize(mul(normalTS, TBN));
                
                // Handle double-sided normals
                float3 viewDirWS = normalize(IN.viewDirWS);
                float facingSign = sign(dot(viewDirWS, normalWS));
                normalWS *= facingSign;
                
                // Setup surface data
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = albedo.a;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = 1.0;
                
                // Setup input data
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = ComputeFogFactor(IN.positionCS.z);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                
                // Calculate lighting
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                
                // Add subsurface scattering for foliage
                Light mainLight = GetMainLight(inputData.shadowCoord);
                color.rgb += CalculateSSS(normalWS, viewDirWS, mainLight);
                
                // Apply fog
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                
                return color;
            }
            ENDHLSL
        }
        
        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            float3 _LightDirection;
            
            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                
                // Growth animation
                float growthValue = IN.uv2.y;
                float growthMask = smoothstep(
                    _GrowthProgress - _GrowthEdge,
                    _GrowthProgress + _GrowthEdge,
                    growthValue
                );
                // When growth is fully complete, disable collapsing so tips fully expand
                growthMask *= (1.0 - step(1.0, _GrowthProgress));

                float3 positionOS = IN.positionOS.xyz;

                // Phase 1: Y-scale — sapling grows up from ground
                float heightPhase = smoothstep(0.0, 0.2, _GrowthProgress);
                positionOS.y *= max(0.001, heightPhase);

                // Phase 2: XZ shrink to trunk axis — ungrown branches stay on the trunk axis
                float3 trunkPoint = float3(0.0, positionOS.y, 0.0);
                positionOS = lerp(positionOS, trunkPoint, growthMask);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                
                // Apply shadow bias
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.growth = growthMask;
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.tangentWS = float4(0,0,0,0);
                OUT.viewDirWS = float3(0,0,0);
                OUT.vertexColor = IN.color;
                
                return OUT;
            }
            
            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                // Clip for growth
                if (_AlphaClip > 0.5)
                {
                    clip(0.5 - IN.growth);
                }
                
                // Alpha clip for leaf shapes
                if (_AlphaClip > 0.5)
                {
                    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                    clip(alpha - _Cutoff);
                }
                
                return 0;
            }
            ENDHLSL
        }
        
        // Depth pass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            
            ZWrite On
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DepthFrag
            
            half4 DepthFrag(Varyings IN) : SV_Target
            {
                if (_AlphaClip > 0.5)
                {
                    clip(0.5 - IN.growth);
                    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a;
                    clip(alpha - _Cutoff);
                }
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
