Shader "ithappy/WaterURP"
{
    Properties
    {
        [Header(Surface)]
        _MaskSurface ("Mask", 2D) = "black" {}
        _SurfaceOpacity ("Opacity", range(0, 1)) = 1
        _ColorSurface ("Color", color) = (0.9, 0.9, 0.9, 1)

        [Header(Color)]
        _ColorShallow ("Shallow", color) = (0.1, 0.1, 0.7, 1)
        _ColorDeep ("Deep", color) = (0.1, 0.2, 0.9, 1)
        _Depth ("Depth", float) = 1

        [Header(Normal)]
        _NormalMap ("Map", 2D) = "bump" {}
        _NormalStrength ("Srength", range(0, 1)) = 1

        [Header(Optics)]
        _Smoothness ("Smoothness", range(0, 1)) = 1
        _Refraction ("Refraction", float) = 0.03

        [Header(Ambient)]
        _AmbientFresnel ("Fresnel", float) = 1
        _ColorAmbient ("Color", color) = (0.9, 0.9, 1)

        [Header(Caustics)]
        [Toggle] _IsCaustics ("Enable", float) = 1
        _MaskCaustics ("Mask", 2D) = "black" {}

        [Header(Foam)]
        [Toggle] _IsFoam ("Enable", float) = 1
        _MaskFoam ("Mask", 2D) = "white" {}
        _FoamAmount ("Amount", float) = 0.5
        _FoamCutoff ("Cutoff", range(0, 1)) = 0.5
        _ColorFoam ("Color", color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags 
        { 
        "RenderPipline" = "Universal"
        "Queue" = "Transparent" 
        }

        Pass
        {
            Name "UniversalForward"

            HLSLPROGRAM

            #pragma vertex VertexFunction
            #pragma fragment FragmentFunction

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 position_os : POSITION;
            };

            struct Interpolators
            {
                float2 uv_ws : TEXCOORD0;
                float4 position_ss : TEXCOORD1;
                float3 viewVector_ws : TEXCOORD2;
                float4 position_cs : SV_POSITION;
            };

            void VertexFunction(Attributes attribs, out Interpolators varyings)
            {
	            float4 position_ws = mul(UNITY_MATRIX_M, attribs.position_os);
	            float4 position_cs = mul(UNITY_MATRIX_VP, position_ws);
	            float4 position_ss = ComputeScreenPos(position_cs);

	            varyings.uv_ws = position_ws.xz;
	            varyings.position_ss = position_ss;
	            varyings.viewVector_ws = _WorldSpaceCameraPos - position_ws.xyz;
	            varyings.position_cs = position_cs;
            }

            uniform sampler2D _MaskSurface;
            uniform float4 _MaskSurface_ST;
            uniform half _SurfaceOpacity;
            uniform half3 _ColorSurface;

            uniform half3 _ColorShallow;
            uniform half3 _ColorDeep;
            uniform half _Depth;

            uniform sampler2D _NormalMap;
            uniform float4 _NormalMap_ST;
            uniform half _NormalStrength;

            uniform half _Refraction;
            uniform half _Smoothness;

            uniform half _AmbientFresnel;
            uniform half3 _ColorAmbient;

            uniform bool _IsCaustics;
            uniform sampler2D _MaskCaustics;
            uniform float4 _MaskCaustics_ST;

            uniform bool _IsFoam;
            uniform sampler2D _MaskFoam;
            uniform float4 _MaskFoam_ST;
            uniform half _FoamAmount;
            uniform half _FoamCutoff;
            uniform half3 _ColorFoam;

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"


            float Fresnel(float3 normal, float3 viewDir, float power)
            {
                return pow((1.0 - saturate(dot(normalize(normal), normalize(viewDir)))), power);
            }

            // Packing
            half3 UnpackNormalAG(half4 packedNormal, half scale)
            {
                half3 normal;
                normal.xy = packedNormal.ag * 2.0 - 1.0;
                normal.z = max(1.0e-16, sqrt(1.0 - saturate(dot(normal.xy, normal.xy))));

                normal.xy *= scale;
                return normal;
            }

            half3 UnpackNormalmapRGorAG(half4 packedNormal, half scale)
            {
                packedNormal.a *= packedNormal.r;
                return UnpackNormalAG(packedNormal, scale);
            }

            // Operations
            half3 NormalBlend(half3 A, half3 B)
            {
                return normalize(half3(A.rg + B.rg, A.b * B.b));
            }

            half3 NormalStrength(half3 normal, half strength)
            {
                normal.xy *= strength;
                return normalize(normal);
            }

            half3 SampleNormalMap(sampler2D map, float2 uv)
            {
                half4 sampleResult = tex2D(map, uv);
                return UnpackNormalmapRGorAG(sampleResult, 1);
            }

            half3 TransformNormalToWS(half3 tangent, half3 normal, half3 bitangent, half3 normal_ts)
            {
                return normalize(mul(float3x3(tangent, bitangent, normal), normal_ts));
            }

            void FragmentFunction(Interpolators varyings, out half4 outColor : SV_Target)
            {
	            half3 viewDir = normalize(varyings.viewVector_ws);
	            float2 uv_ss = varyings.position_ss.xy / varyings.position_ss.w;

	            // Calculating Normal
	            half3 normal = SampleNormalMap(_NormalMap, varyings.uv_ws * _NormalMap_ST.xy + _Time * _NormalMap_ST.zw);
	            normal = NormalStrength(normal, _NormalStrength);
	            normal = TransformNormalToWS(half3(1, 0, 0), half3(0, 1, 0), half3(0, 0, 1), normal);

	            // Calculating Direct Depth
	            half depth = Linear01Depth(SampleSceneDepth(uv_ss), _ZBufferParams) * _ProjectionParams.z - (varyings.position_ss.w - 1);
	            half depthMask = saturate(depth - _Depth);

	            // Calculating Refracted Depth
	            half refDepth = Linear01Depth(SampleSceneDepth(uv_ss + normal.xz * _Refraction), _ZBufferParams) * _ProjectionParams.z - (varyings.position_ss.w - 1);
	            half refMask = saturate(refDepth - _Depth);

	            // Shallow-Deep Coloring
	            half3 waterColor = lerp(_ColorShallow, _ColorDeep, refMask);

	            // Specular Coloring
	            half3 halfVector = normalize(_MainLightPosition.xyz + viewDir);
	            half specMask = pow(saturate(dot(normal, halfVector)), _Smoothness * 1000) * sqrt(_Smoothness);
	            waterColor = lerp(waterColor, half3(1, 1, 1), specMask);

	            // Surface Mask Coloring
	            half surfaceMask = tex2D(_MaskSurface, varyings.uv_ws * _MaskSurface_ST.xy + _Time.y * _MaskSurface_ST.zw);
	            waterColor = lerp(waterColor, _ColorSurface, surfaceMask * _SurfaceOpacity);

	            // Fade Fresnel Coloring
	            half fresnel = saturate(Fresnel(normal, viewDir, _AmbientFresnel) + Fresnel(half3(0, 1, 0), viewDir, _AmbientFresnel));
	            waterColor = lerp(waterColor, _ColorAmbient, fresnel);

	            // Caustics Coloring
	            if(_IsCaustics)
	            {
		            half3 causticsMask = tex2D(_MaskCaustics, varyings.uv_ws * _MaskCaustics_ST.xy + _Time.y * _MaskCaustics_ST.zw).rgb;
		            waterColor = lerp(waterColor, half3(1, 1, 1), causticsMask * (1 - depthMask));
	            }

	            // Foam Coloring
	            if(_IsFoam)
	            {
		            half foamMask = tex2D(_MaskFoam, varyings.uv_ws * _MaskFoam_ST.xy + _Time.y * _MaskFoam_ST.zw).r * (1 - saturate(depth - _FoamAmount));
		            foamMask = step(_FoamCutoff, foamMask);
		            waterColor = lerp(waterColor, _ColorFoam, foamMask);
	            }

	            outColor = half4(waterColor.rgb, 1);
            }

            ENDHLSL
        }

        Pass 
        {
	        Name "DepthOnly"
	        Tags { "LightMode"="DepthOnly" }

	        ColorMask 0
	        ZWrite On
	        ZTest LEqual

	        HLSLPROGRAM
	        #pragma vertex DepthOnlyVertex
	        #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

	        ENDHLSL
        }

        Pass 
        {
	        Name "DepthNormals"
	        Tags { "LightMode"="DepthNormals" }

	        ZWrite On
	        ZTest LEqual

	        HLSLPROGRAM
	        #pragma vertex DepthNormalsVertex
	        #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
	
	        ENDHLSL
        }
    }
}