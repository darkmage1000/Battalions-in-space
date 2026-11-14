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
    struct Input { float2 uv_MainTex; float4 color : COLOR; float3 viewDir; float3 worldNormal; };
    fixed4 _Color; float _Glow; float4 _BIS_Rim;
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
      // soft sky-coloured light along the silhouette (global _BIS_Rim, set per map)
      float rim = 1 - saturate(dot(normalize(IN.viewDir), normalize(IN.worldNormal)));
      o.Emission += _BIS_Rim.rgb * (rim * rim * rim) * (0.55 + 0.45 * saturate(IN.worldNormal.y + 0.5)) * (1 - g);
      o.Alpha = 1;
    }
    ENDCG
  }
  Fallback "Diffuse"
}
