// Map surfaces for the code-built levels: Standard lighting plus detail that
// needs no UVs or textures, computed from the world position so it never
// stretches on scaled boxes:
//  - broad blotches + mid grain + fine speckle (3D value noise) on albedo,
//  - smoothness breakup from the same grain,
//  - darker wall bases near the floor (cheap contact occlusion).
Shader "Sundown/WorldSurface"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Grain ("Grain strength", Range(0,1)) = 0.32
        _GrainScale ("Grain per metre", Float) = 2.2
        _Grime ("Wall base darkening", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half4 _EmissionColor;
        half _Grain;
        float _GrainScale;
        half _Grime;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                             lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                             lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 p = IN.worldPos;
            float broad = Noise(p * 0.17);
            float mid = Noise(p * _GrainScale);
            float fine = Noise(p * _GrainScale * 7.3);
            float grain = broad * 0.5 + mid * 0.32 + fine * 0.18; // ~0..1 around 0.5

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half shade = 1.0 + (grain - 0.5) * 2.0 * _Grain;

            // Walls (not floors/ceilings) get darker over their bottom ~60 cm.
            half wall = 1.0 - abs(IN.worldNormal.y);
            half grime = lerp(1.0, saturate(0.62 + p.y * 0.62), wall * _Grime);

            o.Albedo = c.rgb * shade * grime;
            o.Metallic = _Metallic;
            o.Smoothness = saturate(_Glossiness * (0.75 + (mid - 0.5) * 0.9 + fine * 0.25));
            o.Emission = _EmissionColor.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
