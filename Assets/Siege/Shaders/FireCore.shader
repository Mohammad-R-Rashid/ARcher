// Molten fireball surface: scrolling noise, hot fresnel rim, HDR so it blooms.
Shader "Siege/FireCore"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
        [HDR] _Hot ("Hot", Color) = (6, 4.2, 1.2, 1)
        [HDR] _Mid ("Mid", Color) = (4, 1.2, 0.15, 1)
        [HDR] _Dark ("Crust", Color) = (0.5, 0.06, 0.01, 1)
        _Speed ("Speed", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
            float4 _Noise_ST; half4 _Hot; half4 _Mid; half4 _Dark; float _Speed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float3 p : TEXCOORD2; };
            Varyings vert(Attributes a)
            {
                Varyings o;
                float t = _Time.y * _Speed;
                float wobble = sin(a.positionOS.y * 9 + t * 7) * 0.035 + sin(a.positionOS.x * 11 - t * 5) * 0.03;
                float3 pos = a.positionOS.xyz * (1 + wobble);
                float3 ws = TransformObjectToWorld(pos);
                o.positionCS = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(a.normalOS);
                o.v = GetWorldSpaceNormalizeViewDir(ws);
                o.p = a.positionOS.xyz;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 uv1 = i.p.xy * 2.6 + float2(0, -t * 0.6);
                float2 uv2 = i.p.zy * 3.4 + float2(t * 0.25, -t * 0.9);
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv1).r * 0.6 + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv2).r * 0.6;
                float fres = pow(1 - saturate(dot(normalize(i.n), i.v)), 2);
                half3 col = lerp(_Dark.rgb, _Mid.rgb, smoothstep(0.35, 0.65, n));
                col = lerp(col, _Hot.rgb, smoothstep(0.7, 1.0, n + fres * 0.8));
                col *= 0.85 + 0.3 * sin(t * 9 + n * 6);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
