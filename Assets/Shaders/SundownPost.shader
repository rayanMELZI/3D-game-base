// Sundown Arena's post-processing, self-contained (no packages):
// pass 0 = bloom bright-pass (soft knee), pass 1 = separable gaussian blur,
// pass 2 = composite (bloom + AO + ACES tonemap + grade + vignette),
// pass 3 = ambient occlusion from the camera depth (half resolution),
// pass 4 = depth-aware blur of the occlusion, pass 5 = FXAA.
// Works in linear and gamma colour space: grading happens on a perceptual
// (gamma-encoded) signal either way.
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

        // Linear <-> perceptual, so grading behaves the same in both colour spaces.
        float3 ToPerceptual(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return LinearToGammaSpace(c);
        #endif
        }

        float3 FromPerceptual(float3 c)
        {
        #if defined(UNITY_COLORSPACE_GAMMA)
            return c;
        #else
            return GammaToLinearSpace(c);
        #endif
        }
        ENDCG

        // ---- Pass 0: bright pass (keeps only HDR highlights, soft knee) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float _Threshold;

            float4 frag (v2f_img i) : SV_Target
            {
                // 4 taps (box filter) kill single-pixel fireflies before the blur.
                float2 t = _MainTex_TexelSize.xy;
                float3 c = tex2D(_MainTex, i.uv + t * float2(-0.5, -0.5)).rgb
                         + tex2D(_MainTex, i.uv + t * float2(0.5, -0.5)).rgb
                         + tex2D(_MainTex, i.uv + t * float2(-0.5, 0.5)).rgb
                         + tex2D(_MainTex, i.uv + t * float2(0.5, 0.5)).rgb;
                c *= 0.25;
                float brightness = max(c.r, max(c.g, c.b));
                float knee = _Threshold * 0.5;
                float soft = clamp(brightness - _Threshold + knee, 0.0, 2.0 * knee);
                soft = soft * soft / (4.0 * knee + 1e-4);
                float contribution = max(soft, brightness - _Threshold) / max(brightness, 1e-4);
                return float4(min(c * contribution, 32.0), 1);
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
            #pragma multi_compile __ SUNDOWN_AO
            sampler2D _BloomTex;
            sampler2D _BloomTexWide;
            sampler2D _AOTex;
            float _BloomIntensity;
            float _Exposure;
            float _Saturation;
            float _Contrast;
            float _Vignette;
            float _AOIntensity;
            float3 _Lift;
            float3 _Gain;

            // Narkowicz ACES filmic approximation.
            float3 TonemapACES(float3 x)
            {
                return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;

            #if defined(SUNDOWN_AO)
                float ao = tex2D(_AOTex, i.uv).r;
                c *= lerp(1.0, ao, _AOIntensity);
            #endif

                float3 bloom = tex2D(_BloomTex, i.uv).rgb * 0.6 + tex2D(_BloomTexWide, i.uv).rgb * 0.4;
            #if defined(UNITY_COLORSPACE_GAMMA)
                // Gamma space: the scene is gamma-encoded, ACES expects linear light.
                c = GammaToLinearSpace(c);
                bloom = GammaToLinearSpace(bloom);
            #endif
                c += bloom * _BloomIntensity;

                c *= _Exposure;
                c = TonemapACES(c);

                // Grade on a perceptual signal: split-tone lift/gain, saturation, contrast.
                float3 p = LinearToGammaSpace(c);
                p = p * _Gain + _Lift * (1.0 - p);
                float luma = dot(p, float3(0.2126, 0.7152, 0.0722));
                p = lerp(luma.xxx, p, _Saturation);
                p = saturate((p - 0.5) * _Contrast + 0.5);

                float2 d = i.uv - 0.5;
                d.x *= _MainTex_TexelSize.y / _MainTex_TexelSize.x * 0.75 + 0.25;
                p *= 1.0 - _Vignette * smoothstep(0.15, 0.85, dot(d, d) * 2.2);

                // Output: perceptual alpha luma for FXAA, colour in the target space.
                float outLuma = dot(p, float3(0.299, 0.587, 0.114));
            #if defined(UNITY_COLORSPACE_GAMMA)
                return float4(saturate(p), outLuma);
            #else
                return float4(GammaToLinearSpace(saturate(p)), outLuma);
            #endif
            }
            ENDCG
        }

        // ---- Pass 3: ambient occlusion (normal-oriented hemisphere, from depth) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;
            float4 _AOParams;   // x = radius (m), y = bias, z = fade start (m), w = fade end (m)
            float2 _ProjScale;  // (1/P11, 1/P22): view-space reconstruction

            float Depth(float2 uv)
            {
                return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0)));
            }

            float3 ViewPos(float2 uv, float z)
            {
                return float3((uv * 2.0 - 1.0) * _ProjScale * z, z);
            }

            float2 ToUv(float3 v)
            {
                return v.xy / (v.z * _ProjScale) * 0.5 + 0.5;
            }

            float Ign(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
            #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    uv.y = 1.0 - uv.y;
            #endif
                float z = Depth(uv);
                // Nothing to occlude on the sky, the first-person gun or far away.
                float fade = smoothstep(0.7, 1.4, z) * (1.0 - smoothstep(_AOParams.z, _AOParams.w, z));
                if (fade <= 0.0)
                    return float4(1, z / _AOParams.w, 0, 1);

                float3 p = ViewPos(uv, z);

                // Normal from the neighbour with the smallest depth step (no seams on edges).
                float2 t = _CameraDepthTexture_TexelSize.xy;
                float3 pr = ViewPos(uv + float2(t.x, 0), Depth(uv + float2(t.x, 0)));
                float3 pl = ViewPos(uv - float2(t.x, 0), Depth(uv - float2(t.x, 0)));
                float3 pu = ViewPos(uv + float2(0, t.y), Depth(uv + float2(0, t.y)));
                float3 pd = ViewPos(uv - float2(0, t.y), Depth(uv - float2(0, t.y)));
                float3 dx = abs(pr.z - p.z) < abs(p.z - pl.z) ? pr - p : p - pl;
                float3 dy = abs(pu.z - p.z) < abs(p.z - pd.z) ? pu - p : p - pd;
                float3 n = normalize(cross(dy, dx));
                n *= -sign(dot(n, p) + 1e-6); // always facing the camera

                float radius = _AOParams.x;
                float noise = Ign(i.uv * _MainTex_TexelSize.zw) * 6.2831853;
                float occlusion = 0.0;
                const int samples = 12;
                [unroll]
                for (int s = 0; s < samples; s++)
                {
                    // Spiral of directions, rotated per pixel; lengths grow outward.
                    float a = noise + s * 2.3999632;
                    float h = (s + 0.5) / samples;
                    float zc = h * 2.0 - 1.0;
                    float r = sqrt(1.0 - zc * zc);
                    float3 dir = float3(cos(a) * r, sin(a) * r, zc);
                    dir *= sign(dot(dir, n));
                    float len = lerp(0.15, 1.0, (s + 1.0) / samples);
                    float3 q = p + (dir + n * 0.15) * radius * len;
                    float2 quv = ToUv(q);
                    float sceneZ = Depth(quv);
                    float range = saturate(radius / max(abs(p.z - sceneZ), 1e-3));
                    occlusion += (sceneZ < q.z - _AOParams.y ? 1.0 : 0.0) * range * range;
                }
                float ao = 1.0 - occlusion / samples * fade;
                return float4(saturate(ao), z / _AOParams.w, 0, 1);
            }
            ENDCG
        }

        // ---- Pass 4: depth-aware blur of the occlusion (direction in _BlurDir) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float2 _BlurDir;

            float4 frag (v2f_img i) : SV_Target
            {
                float2 stepUv = _MainTex_TexelSize.xy * _BlurDir;
                float2 center = tex2D(_MainTex, i.uv).rg;
                float sum = center.r;
                float weight = 1.0;
                [unroll]
                for (int k = -3; k <= 3; k++)
                {
                    if (k == 0)
                        continue;
                    float2 s = tex2D(_MainTex, i.uv + stepUv * k).rg;
                    float w = exp(-abs(k) * 0.35) * saturate(1.0 - abs(s.g - center.g) / max(center.g, 1e-3) * 8.0);
                    sum += s.r * w;
                    weight += w;
                }
                return float4(sum / weight, center.g, 0, 1);
            }
            ENDCG
        }

        // ---- Pass 5: FXAA (quality-lite, luma in alpha from the composite) ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            float Luma(float2 uv)
            {
                return tex2Dlod(_MainTex, float4(uv, 0, 0)).a;
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                float4 centre = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                float lM = centre.a;
                float lN = Luma(i.uv + float2(0, t.y));
                float lS = Luma(i.uv - float2(0, t.y));
                float lE = Luma(i.uv + float2(t.x, 0));
                float lW = Luma(i.uv - float2(t.x, 0));
                float lMin = min(lM, min(min(lN, lS), min(lE, lW)));
                float lMax = max(lM, max(max(lN, lS), max(lE, lW)));
                float range = lMax - lMin;
                if (range < max(0.0312, lMax * 0.125))
                    return centre;

                float lNE = Luma(i.uv + t);
                float lNW = Luma(i.uv + float2(-t.x, t.y));
                float lSE = Luma(i.uv + float2(t.x, -t.y));
                float lSW = Luma(i.uv - t);

                // Edge orientation and blend amount (subpixel aliasing).
                float edgeH = abs(lNW + lNE - 2.0 * lN) + 2.0 * abs(lW + lE - 2.0 * lM) + abs(lSW + lSE - 2.0 * lS);
                float edgeV = abs(lNW + lSW - 2.0 * lW) + 2.0 * abs(lN + lS - 2.0 * lM) + abs(lNE + lSE - 2.0 * lE);
                bool horizontal = edgeH >= edgeV;

                float lPos = horizontal ? lN : lE;
                float lNeg = horizontal ? lS : lW;
                float gPos = abs(lPos - lM);
                float gNeg = abs(lNeg - lM);
                float stepLen = horizontal ? t.y : t.x;
                if (gNeg > gPos)
                    stepLen = -stepLen;

                float avg = (2.0 * (lN + lS + lE + lW) + lNE + lNW + lSE + lSW) / 12.0;
                float sub = saturate(abs(avg - lM) / range);
                sub = smoothstep(0.0, 1.0, sub);
                float blend = sub * sub * 0.75;

                // Walk along the edge to find its ends.
                float2 edgeUv = i.uv + (horizontal ? float2(0, stepLen * 0.5) : float2(stepLen * 0.5, 0));
                float2 along = horizontal ? float2(t.x, 0) : float2(0, t.y);
                float edgeLuma = (lM + (gNeg > gPos ? lNeg : lPos)) * 0.5;
                float gradient = max(gPos, gNeg) * 0.25;
                float2 uvP = edgeUv + along;
                float2 uvN = edgeUv - along;
                float dP = Luma(uvP) - edgeLuma;
                float dN = Luma(uvN) - edgeLuma;
                bool doneP = abs(dP) >= gradient;
                bool doneN = abs(dN) >= gradient;
                [unroll]
                for (int s = 0; s < 8; s++)
                {
                    float stride = s < 3 ? 1.0 : (s < 6 ? 2.0 : 4.0);
                    if (!doneP) { uvP += along * stride; dP = Luma(uvP) - edgeLuma; doneP = abs(dP) >= gradient; }
                    if (!doneN) { uvN -= along * stride; dN = Luma(uvN) - edgeLuma; doneN = abs(dN) >= gradient; }
                }
                float distP = horizontal ? uvP.x - i.uv.x : uvP.y - i.uv.y;
                float distN = horizontal ? i.uv.x - uvN.x : i.uv.y - uvN.y;
                bool nearP = distP <= distN;
                float dist = min(distP, distN);
                float total = distP + distN;
                bool deltaSign = (nearP ? dP : dN) >= 0.0;
                float edgeBlend = deltaSign == (lM - edgeLuma >= 0.0) ? 0.0 : 0.5 - dist / max(total, 1e-5);
                blend = max(blend, edgeBlend);

                float2 uv = i.uv + (horizontal ? float2(0, stepLen * blend) : float2(stepLen * blend, 0));
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            ENDCG
        }
    }
    Fallback Off
}
