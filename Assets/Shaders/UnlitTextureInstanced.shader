// A drop in replacement for Unity's built in Unlit/Texture that can be GPU instanced.
//
// The stock Unlit/Texture has no "#pragma multi_compile_instancing", so its materials never even
// show the Enable GPU Instancing checkbox. The tunnel draws the same two ring meshes 25 times and
// the same rail mesh 54 times every frame, so that checkbox is the difference between ~112 draw
// calls and ~4.
//
// Fog is not optional here. The cave's sight limit IS linear fog (CaveAtmosphere), so a tunnel
// shader that dropped UNITY_APPLY_FOG would render the far end of the mine in full brightness.
Shader "CrystalCatch/Unlit Texture Instanced"
{
    // Deliberately the SAME property set as Unlit/Texture, no tint. The mine materials carry a
    // stale "_Color" of (1,1,1,0) left over from an earlier shader, and adding a _Color property
    // here would suddenly give that saved alpha of 0 a meaning.
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 3.0, not the stock Unlit/Texture's 2.0 - instancing variants need SM3.0, and the
            // Quest is GLES3 / Vulkan so nothing is lost by asking for it
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o);

                // Must run before UnityObjectToClipPos, which reads the instanced unity_ObjectToWorld
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    // If instancing is unavailable the stock shader still draws the right picture, just unbatched
    Fallback "Unlit/Texture"
}
