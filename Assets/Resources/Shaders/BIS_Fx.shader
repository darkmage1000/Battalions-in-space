// Particles, bolts, holograms, shields. Blend mode set per material (_Src/_Dst).
Shader "BIS/Fx" {
  Properties {
    _MainTex ("Tex", 2D) = "white" {}
    _Color ("Color", Color) = (1,1,1,1)
    [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src", Float) = 5
    [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst", Float) = 1
    [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    _Boost ("Boost", Float) = 1
  }
  SubShader {
    Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
    Blend [_Src] [_Dst]
    ZWrite Off
    Cull [_Cull]
    Pass {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma multi_compile_instancing
      #pragma multi_compile_fog
      #include "UnityCG.cginc"
      sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Boost;
      struct a2v { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
      struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
      v2f vert (a2v v) {
        v2f o; UNITY_SETUP_INSTANCE_ID(v);
        o.pos = UnityObjectToClipPos(v.vertex);
        o.color = v.color * _Color; o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        UNITY_TRANSFER_FOG(o, o.pos);
        return o;
      }
      fixed4 frag (v2f i) : SV_Target {
        fixed4 c = tex2D(_MainTex, i.uv) * i.color;
        c.rgb *= _Boost;
        UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0,0,0,0));
        return c;
      }
      ENDCG
    }
  }
}
