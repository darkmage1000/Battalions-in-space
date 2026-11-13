// BIS/Lit for the land itself (terrain, rocks, buildings): the same vertex colours, plus a greyscale detail map laid on
// by world position - its red channel on ground that faces up, its green channel on walls and cliffs - so flat colour
// gets grain (grass, sand, rock, plaster) without any mesh needing texture coordinates. Vertex alpha < 0.5 still glows.
Shader "BIS/LitWorld" {
  Properties {
    _Color ("Tint", Color) = (1,1,1,1) _Glow ("Glow", Float) = 1.6
    _Detail ("Detail (R top, G sides)", 2D) = "gray" {}
    _TopScale ("Top: tiles per metre", Float) = 0.16 _SideScale ("Sides: tiles per metre", Float) = 0.2 _Strength ("Strength", Float) = 0.7
  }
  SubShader {
    Tags { "RenderType"="Opaque" }
    LOD 200
    CGPROGRAM
    #pragma surface surf Lambert fullforwardshadows addshadow
    #pragma multi_compile_instancing
    #pragma target 3.0
    sampler2D _Detail;
    struct Input { float4 color : COLOR; float3 worldPos; float3 worldNormal; };
    fixed4 _Color; float _Glow, _TopScale, _SideScale, _Strength;
    void surf (Input IN, inout SurfaceOutput o) {
      float3 c = IN.color.rgb * _Color.rgb;
      #ifndef UNITY_COLORSPACE_GAMMA
      c = GammaToLinearSpace(c);
      #endif
      float3 an = abs(IN.worldNormal);
      float top = tex2D(_Detail, IN.worldPos.xz * _TopScale).r;
      float2 suv = an.x > an.z ? IN.worldPos.zy : IN.worldPos.xy;
      float side = tex2D(_Detail, suv * _SideScale).g;
      float d = lerp(side, top, smoothstep(0.55, 0.8, an.y));
      float g = saturate((0.5 - IN.color.a) * 2);
      c *= 1 + (d - 0.5) * 2 * _Strength * (1 - g);
      o.Albedo = c * (1 - g * 0.6);
      o.Emission = c * g * _Glow;
      o.Alpha = 1;
    }
    ENDCG
  }
  Fallback "Diffuse"
}
