// Leaves: a cut-out texture tinted by vertex colour, drawn two-sided. Vertex alpha is how far the vertex sways in the
// wind (0 = still), so a crown moves more at the top than where it meets the trunk.
Shader "BIS/LitCutout" {
  Properties { _MainTex ("Texture", 2D) = "white" {} _Cutoff ("Alpha cutoff", Range(0,1)) = 0.4 _Sway ("Sway (m)", Float) = 0.22 }
  SubShader {
    Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
    LOD 200
    Cull Off
    CGPROGRAM
    #pragma surface surf Lambert alphatest:_Cutoff addshadow vertex:vert
    #pragma target 3.0
    sampler2D _MainTex; float _Sway;
    struct Input { float2 uv_MainTex; float4 color : COLOR; };
    void vert (inout appdata_full v) {
      float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
      float k = v.color.a * _Sway;
      v.vertex.x += sin(_Time.y * 1.3 + w.x * 0.35 + w.z * 0.27) * k;
      v.vertex.z += cos(_Time.y * 1.1 + w.z * 0.31 + w.y * 0.2) * k * 0.7;
    }
    void surf (Input IN, inout SurfaceOutput o) {
      fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
      float3 c = IN.color.rgb;
      #ifndef UNITY_COLORSPACE_GAMMA
      c = GammaToLinearSpace(c);
      #endif
      o.Albedo = c * t.rgb;
      o.Alpha = t.a;
    }
    ENDCG
  }
  Fallback "Transparent/Cutout/Diffuse"
}
