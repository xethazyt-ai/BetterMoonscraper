// The note flames: an animated flame (one frame of a strip) drawn additively in one color, white-hot at its core,
// with a flash of light at its base on a hit. Always on top of the highway.
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
        Blend One One

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
                color += lerp(_Color.rgb, fixed3(1, 1, 1), 0.5) * shape.b * _Flash;
                return fixed4(color * _Color.a, 1);
            }
            ENDCG
        }
    }
}
