// The note flames: a flame shape drawn additively in one color, white-hot at its core. Always on top of the highway.
Shader "Hidden/NoteFlame"
{
    Properties
    {
        _MainTex ("Shape (R glow, G core)", 2D) = "black" {}
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
            fixed4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 shape = tex2D(_MainTex, i.uv);
                fixed3 color = lerp(_Color.rgb, fixed3(1, 1, 1), shape.g * 0.8);
                return fixed4(color * shape.r * _Color.a, 1);
            }
            ENDCG
        }
    }
}
