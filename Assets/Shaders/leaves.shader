Shader "Custom/SimpleLeaf"
{
    Properties
    {
        // 1. 基础颜色 (绿色)
        _Color ("Main Color", Color) = (0.2, 0.8, 0.2, 1)
        
        // 2. 形状贴图 (你需要放一个圆形的白色图片，背景透明)
        _MainTex ("Leaf Texture (Alpha)", 2D) = "white" {}
        
        // 3. Alpha 阈值 (用于把方形剪成圆形)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        
        // 4. 生长进度 (从 C# 传过来的数据)
        _GrowthProgress ("Growth Progress", Range(0, 1)) = 1.0
    }

    SubShader
    {
        // 标签：告诉 Unity 这是一个透明镂空物体，需要按顺序渲染
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        
        // 🔥 核心指令：关闭背面剔除
        // 默认情况下 (Cull Back)，GPU 不画背面。
        // 因为你的叶子是十字面片，必须两面都画，否则转到背面叶子就没了。
        Cull Off 

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"

            // 对应 Properties 里的变量
            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST; // 贴图的缩放位移
            float _Cutoff;
            float _GrowthProgress;

            // 顶点着色器的输入 (从 Mesh 读数据)
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;  // UV0: 贴图坐标
                float2 uv2 : TEXCOORD1; // 🔥 UV2: 你的“私货” (生长数据)
            };

            // 顶点 -> 片元 的传递结构
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 growthInfo : TEXCOORD1; // 把生长数据传给片元
            };

            // --- 1. 顶点着色器 (处理形状) ---
            v2f vert (appdata v)
            {
                v2f o;
                
                // 简单的生长动画逻辑：
                // 你的 C# 代码里，叶子的 uv2.y 是 1.0 (或生长深度)。
                // 我们可以做一个简单的缩放：如果当前生长进度还没到我，我就缩成 0
                
                // 取出这个顶点的“出生时间” (存放在 uv2.y)
                float birthTime = v.uv2.y;
                
                // 如果当前进度 < 出生时间，把顶点归零 (叶子消失)
                // 这是一个硬切 (Hard Cut)，你也可以做平滑缩放
                if (_GrowthProgress < birthTime)
                {
                    v.vertex = 0; 
                }

                // 常规操作：把 3D 坐标转成 2D 屏幕坐标
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.growthInfo = v.uv2;
                
                return o;
            }

            // --- 2. 片元着色器 (处理像素颜色) ---
            fixed4 frag (v2f i) : SV_Target
            {
                // 采样贴图颜色
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // 叠加自定义绿色
                col *= _Color;
                
                // 🔥 剪裁 (Alpha Clipping)
                // 如果贴图的透明度 (Alpha) 小于阈值，丢弃这个像素！
                // 这就是为什么方形 Mesh 能显示成圆形叶子的原因。
                clip(col.a - _Cutoff);
                
                return col;
            }
            ENDCG
        }
    }
}