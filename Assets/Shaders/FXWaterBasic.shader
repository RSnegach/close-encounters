// Unity Standard Assets "FX/Water (Basic)" shader — ported for Unity 6 (6000.x).
//
// Original source: Unity Technologies Standard Assets / Environment / Water (Basic).
// Modifications for Unity 6 compatibility:
//   - Replaced fixed-function texture combiners with a fragment shader.
//   - Uses URP/Built-in compatible syntax (no fixed-function TexGen, no SetTexture blocks).
//   - Kept the same property names and visual intent as the original.
//
// Shader path: FX/Water (Basic)
// Used by: ArenaBase.AddWaterSurface via WaterBasicAnimator for scrolling normals.

Shader "FX/Water (Basic)"
{
    Properties
    {
        _horizonColor ("Horizon Color", Color) = (0.172, 0.463, 0.435, 1.0)
        _waterColor   ("Water Color",   Color) = (0.172, 0.463, 0.435, 1.0)
        [NoScaleOffset]
        _WaveNormal   ("Normals",  2D) = "bump" {}
        _WaveScale    ("Wave Scale", Range(0.02, 0.5)) = 0.07
        _WaveSpeed    ("Wave Speed", Vector) = (5.0, 5.0, -4.0, 0.0)
        _ReflDistort  ("Reflection Distort", Range(0.0, 1.5)) = 0.44
        _RefrDistort  ("Refraction Distort", Range(0.0, 1.5)) = 0.40
        _RefrColor    ("Refraction Color",   Color) = (0.34, 0.85, 0.92, 1.0)
        _SpecColor    ("Specular Color",     Color) = (0.72, 0.72, 0.72, 1.0)
        _Shininess    ("Shininess",          Range(2.0, 500.0)) = 200.0
        _FresnelBias  ("Fresnel Bias",   Range(0.0, 1.0)) = 0.2
        _FresnelPow   ("Fresnel Power",  Range(0.5, 8.0)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue"      = "Transparent"
        }

        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "WATER_BASIC"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            // ---- Properties ----
            fixed4 _horizonColor;
            fixed4 _waterColor;
            sampler2D _WaveNormal;
            float  _WaveScale;
            float4 _WaveSpeed;       // xy = layer1 scroll, zw = layer2 scroll
            float  _ReflDistort;
            float  _RefrDistort;
            fixed4 _RefrColor;
            fixed4 _SpecColor;
            float  _Shininess;
            float  _FresnelBias;
            float  _FresnelPow;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float2 uv0       : TEXCOORD0;  // normal-map layer 1
                float2 uv1       : TEXCOORD1;  // normal-map layer 2
                float3 worldPos  : TEXCOORD2;
                float3 worldNorm : TEXCOORD3;
                float3 viewDir   : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos       = UnityObjectToClipPos(v.vertex);
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.viewDir   = normalize(UnityWorldSpaceViewDir(o.worldPos));

                // Two scrolling UV layers for the normal map
                float2 baseUV = o.worldPos.xz * _WaveScale;
                float time = _Time.x; // _Time.x = t/20, gives slow scroll

                o.uv0 = baseUV + _WaveSpeed.xy * time;
                o.uv1 = baseUV + _WaveSpeed.zw * time;

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample two scrolling normal layers and blend
                fixed3 n0 = UnpackNormal(tex2D(_WaveNormal, i.uv0));
                fixed3 n1 = UnpackNormal(tex2D(_WaveNormal, i.uv1));
                fixed3 normalTS = normalize(n0 + n1);

                // Perturb the world normal
                float3 worldNorm = normalize(i.worldNorm + float3(normalTS.x * _ReflDistort,
                                                                   0,
                                                                   normalTS.y * _ReflDistort));

                // Fresnel: view-dependent blend between water color and horizon color
                float fresnel = _FresnelBias + (1.0 - _FresnelBias) *
                                pow(1.0 - saturate(dot(i.viewDir, worldNorm)), _FresnelPow);
                fresnel = saturate(fresnel);

                fixed4 col;
                col.rgb = lerp(_waterColor.rgb, _horizonColor.rgb, fresnel);

                // Blend with refraction tint
                col.rgb = lerp(col.rgb, _RefrColor.rgb, 0.15);

                // Simple specular highlight from main directional light
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 halfVec  = normalize(i.viewDir + lightDir);
                float  spec     = pow(saturate(dot(worldNorm, halfVec)), _Shininess);
                col.rgb += _SpecColor.rgb * spec * 0.6;

                // Alpha: mostly opaque with slight transparency at edges
                col.a = lerp(0.85, 0.95, fresnel);

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
