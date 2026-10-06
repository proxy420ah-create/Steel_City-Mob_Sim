// Directional flow-field stub: lerps _ColdColor (at the node end, local -Z) to
// _HotColor (at the next-hop end, local +Z) so the hot tip points toward the goal.
Shader "Unlit/FlowStub"
{
    Properties
    {
        _ColdColor ("Cold Color", Color) = (0.05, 0.15, 0.6, 0.15)
        _HotColor  ("Hot Color",  Color) = (1.0, 1.0, 1.0, 0.95)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _ColdColor;
            fixed4 _HotColor;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float t : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.t = saturate(v.vertex.z + 0.5);   // beam local: -Z = node, +Z = next hop
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return lerp(_ColdColor, _HotColor, i.t);
            }
            ENDCG
        }
    }
}
