// An outline around a note: the note's own mesh pushed out along its normals with only its back faces drawn, so it
// shows just past the note's edges in one flat color. Used for Star Power open notes.
Shader "Hidden/NoteOutline"
{
    Properties
    {
        _Color ("Color", Color) = (0.22, 0.92, 1, 1)
        _Width ("Width (world units)", Float) = 0.035
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Width;

            float4 vert(appdata_base v) : SV_POSITION
            {
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += normalize(UnityObjectToWorldNormal(v.normal)) * _Width;
                return mul(UNITY_MATRIX_VP, float4(world, 1));
            }

            fixed4 frag() : SV_Target
            {
                return fixed4(_Color.rgb, 1);
            }
            ENDCG
        }
    }
}
