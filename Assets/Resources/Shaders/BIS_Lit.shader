// Vertex-coloured lit shader. Vertex alpha < 1 marks self-lit (glowing) parts.
Shader "BIS/Lit" {
  Properties { _Color ("Tint", Color) = (1,1,1,1) _Glow ("Glow", Float) = 1.6 }
  SubShader {
    Tags { "RenderType"="Opaque" }
    LOD 200
    CGPROGRAM
    #pragma surface surf Lambert fullforwardshadows addshadow
    #pragma multi_compile_instancing
    #pragma target 3.0
    struct Input { float4 color : COLOR; };
    fixed4 _Color; float _Glow;
    void surf (Input IN, inout SurfaceOutput o) {
      float3 c = IN.color.rgb * _Color.rgb;
      #ifndef UNITY_COLORSPACE_GAMMA
      c = GammaToLinearSpace(c);
      #endif
      float g = saturate((0.5 - IN.color.a) * 2);
      o.Albedo = c * (1 - g * 0.6);
      o.Emission = c * g * _Glow;
      o.Alpha = 1;
    }
    ENDCG
  }
  Fallback "Diffuse"
}
