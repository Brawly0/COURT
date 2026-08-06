Shader "CaseClosed/PSX"
{
    // The three things that actually make a PS1 look, none of them "low poly":
    //   1. Vertex snapping  - the PS1 GTE had no sub-pixel precision, so verts
    //                         quantised to a grid and geometry visibly jittered.
    //   2. Affine texture mapping - no perspective-correct interpolation, so
    //                         textures warp and swim across big triangles.
    //   3. Vertex lighting + dithering - lighting baked per-vertex, colour
    //                         quantised to 5-bit channels with a 4x4 Bayer dither.
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _SnapAmount ("Vertex Snap (higher = coarser)", Range(8, 320)) = 64
        _AffineAmount ("Affine Warp", Range(0, 1)) = 0.85
        _ColorDepth ("Color Depth (levels)", Range(4, 64)) = 32
        _DitherAmount ("Dither", Range(0, 1)) = 0.55
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "PSXForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                noperspective float2 uvAffine : TEXCOORD0;   // affine (warped)
                float2 uvCorrect  : TEXCOORD1;               // perspective-correct
                float3 vertexLit  : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _SnapAmount;
                float  _AffineAmount;
                float  _ColorDepth;
                float  _DitherAmount;
                float  _AmbientBoost;
            CBUFFER_END

            // 4x4 Bayer matrix - the classic PS1/PSX dither pattern
            static const float BAYER[16] = {
                 0.0/16,  8.0/16,  2.0/16, 10.0/16,
                12.0/16,  4.0/16, 14.0/16,  6.0/16,
                 3.0/16, 11.0/16,  1.0/16,  9.0/16,
                15.0/16,  7.0/16, 13.0/16,  5.0/16
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float4 positionCS = TransformWorldToHClip(positionWS);

                // ---- 1. vertex snapping in clip space ----
                float2 grid = _SnapAmount.xx;
                float4 snapped = positionCS;
                snapped.xy = floor((positionCS.xy / positionCS.w) * grid) / grid * positionCS.w;
                OUT.positionCS = snapped;

                // ---- 2. per-vertex lighting (the console had no per-pixel) ----
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 lit = mainLight.color * ndotl;
                lit += unity_AmbientSky.rgb * _AmbientBoost;

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light l = GetAdditionalLight(i, positionWS);
                    lit += l.color * l.distanceAttenuation * saturate(dot(normalWS, l.direction));
                }
                #endif
                OUT.vertexLit = lit;

                float2 uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.uvAffine  = uv;
                OUT.uvCorrect = uv;
                OUT.fogFactor = ComputeFogFactor(positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // ---- 3. blend between warped and correct UVs ----
                float2 uv = lerp(IN.uvCorrect, IN.uvAffine, _AffineAmount);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                half3 col = tex.rgb * _BaseColor.rgb * IN.vertexLit;

                // ---- dither + colour quantisation (5-bit era palette) ----
                uint2 px = uint2(IN.positionCS.xy) % 4u;
                float d = (BAYER[px.y * 4u + px.x] - 0.5) * (_DitherAmount / _ColorDepth);
                col += d;
                col = floor(col * _ColorDepth + 0.5) / _ColorDepth;

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // shadow casting so the puppets still ground themselves in the room
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V shadowVert (A IN)
            {
                V OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }
            half4 shadowFrag (V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
