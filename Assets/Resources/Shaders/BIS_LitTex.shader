// BIS/Lit with a texture: for imported models that carry their own painted texture (Quaternius ships).
// Vertex colour still multiplies in, and vertex alpha < 0.5 still marks glowing parts.
Shader "BIS/LitTex" {
  Properties { _MainTex ("Texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) _Glow ("Glow", Float) = 1.6 }
  SubShader {
    Tags { "RenderType"="Opaque" }
    LOD 200
    CGPROGRAM
    #pragma surface surf Lambert fullforwardshadows addshadow
    #pragma multi_compile_instancing
    #pragma target 3.0
    sampler2D _MainTex;
    struct Input { float2 uv_MainTex; float4 color : COLOR; };
    fixed4 _Color; float _Glow;
    void surf (Input IN, inout SurfaceOutput o) {
      float3 t = tex2D(_MainTex, IN.uv_MainTex).rgb;
      float3 c = IN.color.rgb * _Color.rgb;
      #ifndef UNITY_COLORSPACE_GAMMA
      c = GammaToLinearSpace(c);
      #endif
      c *= t;
      float g = saturate((0.5 - IN.color.a) * 2);
      o.Albedo = c * (1 - g * 0.6);
      o.Emission = c * g * _Glow;
      o.Alpha = 1;
    }
    ENDCG
  }
  Fallback "Diffuse"
}
