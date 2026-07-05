// Sundown Arena's post-processing, self-contained (no packages):
// pass 0 = bloom bright-pass, pass 1 = separable gaussian blur,
// pass 2 = composite (bloom + ACES tonemap + warm grade + vignette).
Shader "Hidden/SundownPost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        ENDCG

        // ---- Pass 0: bright pass (keeps only HDR highlights) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float _Threshold;

            float4 frag (v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float brightness = max(c.r, max(c.g, c.b));
                float contribution = max(0.0, brightness - _Threshold) / max(brightness, 1e-4);
                return float4(c * contribution, 1);
            }
            ENDCG
        }

        // ---- Pass 1: separable gaussian blur (direction in _BlurDir) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float2 _BlurDir;

            float4 frag (v2f_img i) : SV_Target
            {
                float2 stepUv = _MainTex_TexelSize.xy * _BlurDir;
                float3 c = tex2D(_MainTex, i.uv).rgb * 0.227027;
                c += tex2D(_MainTex, i.uv + stepUv * 1.3846).rgb * 0.3162162;
                c += tex2D(_MainTex, i.uv - stepUv * 1.3846).rgb * 0.3162162;
                c += tex2D(_MainTex, i.uv + stepUv * 3.2308).rgb * 0.0702703;
                c += tex2D(_MainTex, i.uv - stepUv * 3.2308).rgb * 0.0702703;
                return float4(c, 1);
            }
            ENDCG
        }

        // ---- Pass 2: composite ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            sampler2D _BloomTex;
            float _BloomIntensity;
            float _Saturation;
            float _Contrast;
            float _Vignette;

            // Narkowicz ACES filmic approximation.
            float3 TonemapACES(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                c += tex2D(_BloomTex, i.uv).rgb * _BloomIntensity;

                c *= float3(1.05, 1.0, 0.95); // warm sundown tint
                c = TonemapACES(c);

                float luma = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(luma.xxx, c, _Saturation);
                c = (c - 0.5) * _Contrast + 0.5;

                float2 d = i.uv - 0.5;
                c *= 1.0 - _Vignette * dot(d, d) * 2.5;

                return float4(saturate(c), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
