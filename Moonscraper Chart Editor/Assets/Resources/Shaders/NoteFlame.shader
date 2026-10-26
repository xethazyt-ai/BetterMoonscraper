// The note flames: an animated flame (one frame of a strip) in one color, white-hot at its core,
// with a flash of light at its base on a hit. Always on top of the highway, and solid
// (it covers what's behind it rather than adding light), so a see-through highway doesn't make the flames see-through.
Shader "Hidden/NoteFlame"
{
    Properties
    {
        _MainTex ("Frames (R glow, G core, B base flash)", 2D) = "black" {}
        _Flash ("Flash", Float) = 0
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend One OneMinusSrcAlpha    // Premultiplied: the color is already scaled by how much flame covers the pixel

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;     // Picks the frame
            fixed4 _Color;
            float _Flash;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 shape = tex2D(_MainTex, i.uv);
                // Like fire: deeper orange at the faint edges, the flame color in the body and white-hot at the core
                fixed3 edge = _Color.rgb * fixed3(1, 0.55, 0.25);
                fixed3 body = lerp(edge, _Color.rgb, shape.r);
                fixed3 color = lerp(body, fixed3(1, 0.97, 0.85), shape.g * 0.9) * shape.r;
                float flash = shape.b * _Flash;
                color += lerp(_Color.rgb, fixed3(1, 1, 1), 0.5) * flash;
                fixed cover = saturate(max(shape.r, flash));
                return fixed4(color, cover) * _Color.a;
            }
            ENDCG
        }
    }
}
