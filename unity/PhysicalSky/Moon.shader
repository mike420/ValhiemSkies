// Moon for ValheimSkies (Hidden/ValheimSkies/Moon in the valheim_physical_sky bundle): MagicMike's moon picture drawn
// on the sky opposite the sun, right after the skybox (so the game's clouds pass in front of it). One full-screen
// triangle at the far plane, shaded only where the depth buffer shows sky. Our own code.
Shader "Hidden/ValheimSkies/Moon"
{
    Properties
    {
        _MainTex ("Moon", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Pass
        {
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            // View rays at view depth 1 to the frustum's corners (PhysicalSky.cs)
            float4 _PSRayBL, _PSRayTL, _PSRayTR, _PSRayBR;
            float4 _MoonDir;    // xyz: the moon's direction
            float4 _MoonShape;  // x: tangent of the moon's radius, y: brightness, z: glow, w: how much shows (night, weather)

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

            float4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.ray);
                float3 m = normalize(_MoonDir.xyz);
                float c = dot(d, m);
                if (c <= 0.0)
                {
                    return 0;
                }

                // The moon's face, kept upright against the world's up
                float3 up = abs(m.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 right = normalize(cross(up, m));
                float3 faceUp = cross(m, right);
                float2 local = float2(dot(d, right), dot(d, faceUp)) / c / _MoonShape.x;
                float inside = all(abs(local) <= 1.0) ? 1.0 : 0.0;
                float4 tex = tex2D(_MainTex, saturate(local * 0.5 + 0.5)) * inside;

                // A soft glow around it, in the moon's own colour
                float r = length(local);
                float glow = _MoonShape.z * 0.35 * exp(-max(r - 0.9, 0.0) * 2.5) * saturate(1.0 - tex.a);
                float3 glowColour = float3(0.95, 0.9, 0.8);

                float show = _MoonShape.w;
                float3 rgb = (tex.rgb * tex.a * _MoonShape.y + glowColour * glow) * show;
                return float4(rgb, tex.a * show);
            }
            ENDCG
        }
    }
}
