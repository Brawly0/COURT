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
        // Defaults tuned for LARGE architectural surfaces. Affine warp and
        // coarse snapping are only tolerable on small triangles (PS1 geometry
        // was heavily subdivided); on a 45m floor quad they read as nauseating
        // swimming, so both are dialled right back. The look survives - it
        // lives in the dither, colour quantisation and texel crunch.
        _SnapAmount ("Vertex Snap (>=600 disables)", Range(8, 640)) = 640
        _AffineAmount ("Affine Warp", Range(0, 1)) = 0.0
        _ColorDepth ("Color Depth (levels)", Range(4, 64)) = 40
        _DitherAmount ("Dither (1 = one full step)", Range(0, 1.5)) = 1.0
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
            // Forward+ (the PC renderer's mode) replaces per-object light lists
            // with clustered iteration and NEVER sets _ADDITIONAL_LIGHTS -
            // without this keyword + LIGHT_LOOP macros, all 132 lamps are dead.
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
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
                float3 positionWS : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
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
                // TWO guards, both load-bearing:
                //  - w > 0.1: vertices BEHIND the camera have w near/below zero;
                //    dividing by it snaps them to garbage, and since a 45m wall
                //    always has off-screen corners, the whole wall visibly
                //    writhed as the camera moved. (The "walls keep moving" bug.)
                //  - _SnapAmount >= 600 means OFF: architecture materials opt
                //    out entirely; characters (small tris) keep the jitter.
                OUT.positionCS = positionCS;
                if (_SnapAmount < 600 && positionCS.w > 0.1)
                {
                    float2 grid = _SnapAmount.xx;
                    OUT.positionCS.xy = floor((positionCS.xy / positionCS.w) * grid) / grid * positionCS.w;
                }

                // NOTE: lighting is evaluated per-PIXEL in frag, not per-vertex.
                // True PS1 hardware lit per vertex, but PS1 geometry was heavily
                // subdivided; our courthouse is big flat slabs (a 45m wall has 4
                // corners), so per-vertex lighting left every interior black.
                // The PSX signature lives in snapping/affine/dither, not here.
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;

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

                float3 n = normalize(IN.normalWS);

                // ambient: spherical harmonics + a floor so nothing is ever pure black
                float3 lit = SampleSH(n) * _AmbientBoost + 0.045;

                // main light, WITH shadow sampling - the ShadowCaster pass is
                // pointless if nothing ever reads the shadow map; this is what
                // actually grounds the puppets in the room
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                lit += mainLight.color * mainLight.shadowAttenuation
                     * saturate(dot(n, mainLight.direction));

                // the lamps: LIGHT_LOOP handles BOTH paths - clustered (Forward+,
                // keyword _CLUSTER_LIGHT_LOOP, needs a local named `inputData`)
                // and classic per-object lists (_ADDITIONAL_LIGHTS)
                #if defined(_CLUSTER_LIGHT_LOOP) || defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, IN.positionWS);
                    lit += l.color * l.distanceAttenuation * saturate(dot(n, l.direction));
                LIGHT_LOOP_END
                #endif

                half3 col = tex.rgb * _BaseColor.rgb * lit;

                // ---- dither + colour quantisation (5-bit era palette) ----
                // Quantise in GAMMA space: the PS1 framebuffer was gamma-encoded
                // 15-bit. Doing it in linear crushes all the dark tones into a
                // handful of coarse bands (dim corridors turned to camo blotches).
                // Dither spans exactly ONE quantisation step at _DitherAmount=1 -
                // any less and banding shows through (0.18 hid only 18% of it).
                col = sqrt(max(col, 0));
                uint2 px = uint2(IN.positionCS.xy) % 4u;
                float d = (BAYER[px.y * 4u + px.x] - 0.5) * (_DitherAmount / _ColorDepth);
                col = floor((col + d) * _ColorDepth + 0.5) / _ColorDepth;
                col *= col;

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

        // depth prepass support so depth-reading effects see PSX geometry
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V depthVert (A IN)
            {
                V OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half depthFrag (V IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
