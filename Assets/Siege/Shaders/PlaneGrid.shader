// Subtle dotted grid shown on detected AR surfaces while choosing where to place things.
Shader "Siege/PlaneGrid"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.92, 0.7, 0.55)
        _Spacing ("Dot spacing (units)", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Spacing;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 g = frac(i.ws.xz / _Spacing) - 0.5;
                float d = length(g);
                float dots = 1 - smoothstep(0.06, 0.1, d);
                float pulse = 0.75 + 0.25 * sin(_Time.y * 3 - length(i.ws.xz) * 0.08);
                return half4(_Color.rgb, _Color.a * dots * pulse);
            }
            ENDHLSL
        }
    }
}
