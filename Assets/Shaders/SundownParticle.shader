// Code-spawned effects (sparks, dust, smoke, fire, blood, muzzle flashes) and
// decals (bullet holes, scorch marks). No texture: a soft round sprite drawn
// from the quad UVs, tinted by the vertex/particle colour. Premultiplied
// output, so the same shader is additive (Blend One One) or alpha-blended
// (Blend One OneMinusSrcAlpha) depending on the material's blend factors.
Shader "Sundown/Particle"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("HDR intensity", Float) = 1
        _Softness ("Edge softness", Range(0.2, 4)) = 1.5
        _Ring ("Hole core (decals)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _Softness;
            float _Ring;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float r = length(d);
                // Ragged edge: the radius wobbles with the angle.
                float angle = atan2(d.y, d.x);
                r *= 1.0 + (Hash(floor(float2(angle * 2.5, 0.0) + 7.0)) - 0.5) * 0.25 * _Ring;
                float soft = pow(saturate(1.0 - r), _Softness);
                float core = smoothstep(0.42, 0.3, r);
                float a = lerp(soft, max(core, soft * 0.6), _Ring) * i.color.a;
                return fixed4(i.color.rgb * _Intensity * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
