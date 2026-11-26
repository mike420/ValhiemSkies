// Physical Sky for ValheimSkies (Hidden/ValheimSkies/PhysicalSky in the valheim_physical_sky bundle): a cheap
// atmosphere laid over the game's sky right after the skybox. One full-screen triangle at the far plane, shaded only
// where the depth buffer shows sky; no look-up tables and no extra cameras. Our own code.
//
// The sky: the day colour overhead, paler toward the horizon; a soft glow around the sun; at sunrise and sunset the
// sunset colour along the horizon (strongest toward the sun) with the sky above darkening toward purple; at the horizon
// it meets the game's fog colour, so the land's fog joins it. At night (and in bad weather, and by the Strength setting)
// it gives way to the game's own sky: out = sky * cover + game * (1 - cover).
Shader "Hidden/ValheimSkies/PhysicalSky"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Pass
        {
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One SrcAlpha

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // View rays at view depth 1 to the frustum's corners (PhysicalSky.cs)
            float4 _PSRayBL, _PSRayTL, _PSRayTR, _PSRayBR;
            float4 _PSDay;      // day sky colour (linear)
            float4 _PSSunset;   // sunset colour (linear)
            float4 _PSSunDir;   // the sun's direction (also at night)
            float4 _PSCover;    // x: how much of our sky shows (weather x strength)

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 ray : TEXCOORD0;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                o.position.y = -o.position.y;
                #endif
                #if UNITY_REVERSED_Z
                o.position.z = 0.0;
                #else
                o.position.z = o.position.w;
                #endif
                float3 bottom = lerp(_PSRayBL.xyz, _PSRayBR.xyz, uv.x);
                float3 top = lerp(_PSRayTL.xyz, _PSRayTR.xyz, uv.x);
                o.ray = lerp(bottom, top, uv.y);
                return o;
            }

            float2 Flat(float3 v)
            {
                float l = dot(v.xz, v.xz);
                return l > 1e-8 ? v.xz * rsqrt(l) : float2(0.0, 0.0);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.ray);
                float3 s = normalize(_PSSunDir.xyz);
                float h = d.y;
                float hp = saturate(h);
                float sunH = s.y;

                // Daylight, and the twilight band (sun from about 12 degrees below to 14 above the horizon)
                float day = smoothstep(-0.12, 0.3, sunH);
                float tw = saturate(1.0 - abs(sunH - 0.03) / 0.24);
                tw = tw * tw * (3.0 - 2.0 * tw);

                // Overhead the day colour; toward the horizon paler and a little brighter
                float3 dayCol = _PSDay.rgb;
                float peak = max(dayCol.r, max(dayCol.g, dayCol.b));
                float3 horizonCol = lerp(dayCol, peak.xxx * 1.05, 0.45);
                float grad = pow(1.0 - hp, 8.0);
                float3 sky = lerp(dayCol, horizonCol, grad);

                // Twilight: the upper sky darkens toward purple
                float3 duskTop = lerp(dayCol, _PSSunset.rgb, 0.35) * 0.4;
                sky = lerp(sky, duskTop, tw * (1.0 - grad));

                // The sunset colour along the horizon, strongest toward the sun
                float az = saturate(0.5 + 0.5 * dot(Flat(d), Flat(s)));
                float glow = tw * exp(-hp * 5.0) * (0.25 + 0.75 * az * az);
                sky = lerp(sky, _PSSunset.rgb, saturate(glow));

                // Brightness with the sun's height
                sky *= lerp(0.08, 1.0, day);

                // A soft glow around the sun (Henyey-Greenstein, g 0.8), warmer at twilight
                float c = dot(d, s);
                float phase = 0.36 / pow(max(1.64 - 1.6 * c, 1e-4), 1.5) / 12.566;
                float3 sunTint = lerp(float3(1.0, 0.95, 0.85), _PSSunset.rgb * 1.5, tw);
                sky += sunTint * phase * 0.06 * saturate(day + tw) * step(-0.05, sunH);

                // At the horizon our sky meets the game's fog
                float fogBlend = 1.0 - smoothstep(-0.02, 0.12, h);
                sky = lerp(sky, unity_FogColor.rgb, fogBlend * 0.8);

                // How much of it shows: fades out at night (the game's stars and sky come through) and below the horizon
                float cover = _PSCover.x * smoothstep(-0.2, 0.02, sunH) * smoothstep(-0.1, 0.0, h);
                return float4(sky * cover, 1.0 - cover);
            }
            ENDCG
        }
    }
}
