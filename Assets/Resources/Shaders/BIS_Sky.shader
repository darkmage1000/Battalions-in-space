// Procedural sky. _Space = 0: gradient sky with sun; 1: star field with nebula.
Shader "BIS/Sky" {
  Properties {
    _Top ("Top", Color) = (0.2,0.4,0.8,1)
    _Horizon ("Horizon", Color) = (0.8,0.85,0.9,1)
    _Bottom ("Bottom", Color) = (0.5,0.5,0.5,1)
    _SunCol ("Sun", Color) = (1,0.95,0.8,1)
    _SunDir ("SunDir", Vector) = (0.3,0.5,0.2,0)
    _Neb1 ("Neb1", Color) = (0.3,0.1,0.4,1)
    _Neb2 ("Neb2", Color) = (0.05,0.2,0.4,1)
    _Space ("Space", Float) = 0
    _NebAmt ("NebAmt", Float) = 0.6
    _Seed ("Seed", Float) = 1
  }
  SubShader {
    Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
    Cull Off ZWrite Off
    Pass {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"
      fixed4 _Top, _Horizon, _Bottom, _SunCol, _Neb1, _Neb2; float4 _SunDir; float _Space, _NebAmt, _Seed;
      struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
      v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }
      float h31(float3 p) { p = frac(p * 0.3183099 + 0.1 + _Seed * 0.013); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
      float n3(float3 x) {
        float3 i = floor(x); float3 f = frac(x); f = f * f * (3 - 2 * f);
        return lerp(lerp(lerp(h31(i), h31(i + float3(1,0,0)), f.x), lerp(h31(i + float3(0,1,0)), h31(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(h31(i + float3(0,0,1)), h31(i + float3(1,0,1)), f.x), lerp(h31(i + float3(0,1,1)), h31(i + float3(1,1,1)), f.x), f.y), f.z);
      }
      float fbm(float3 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += a * n3(p); p *= 2.03; a *= 0.5; } return s; }
      fixed4 frag (v2f i) : SV_Target {
        float3 d = normalize(i.dir);
        float3 sd = normalize(_SunDir.xyz);
        float sun = saturate(dot(d, sd));
        if (_Space < 0.5) {
          float y = d.y;
          float3 c = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), 0.55)) : lerp(_Horizon.rgb, _Bottom.rgb, saturate(-y * 4));
          c += _SunCol.rgb * (pow(sun, 900) * 6 + pow(sun, 12) * 0.25);
          float cl = fbm(float3(d.x / (y + 0.25), _Seed, d.z / (y + 0.25)) * 2.2);
          c = lerp(c, lerp(_Horizon.rgb, float3(1,1,1), 0.6), saturate((cl - 0.55) * 2.2) * saturate(y * 5) * 0.55);
          return fixed4(c, 1);
        } else {
          float3 c = 0;
          float neb = fbm(d * 2.4 + _Seed);
          float neb2 = fbm(d * 5.1 - _Seed * 0.7);
          c += _Neb1.rgb * pow(saturate(neb * 1.25 - 0.25), 2.2) * _NebAmt * 1.6;
          c += _Neb2.rgb * pow(saturate(neb2 * 1.3 - 0.35), 2.0) * _NebAmt * 1.4;
          float3 g = d * 260; float3 cell = floor(g); float r = h31(cell);
          float3 sp = cell + 0.5 + (float3(h31(cell + 7), h31(cell + 13), h31(cell + 29)) - 0.5) * 0.7;
          float st = saturate(1 - length(g - sp) * 2.2);
          c += (r > 0.92 ? 1 : 0) * pow(st, 3) * lerp(float3(0.7,0.8,1), float3(1,0.85,0.7), h31(cell + 3)) * 2.2;
          float3 g2 = d * 90; float3 c2 = floor(g2);
          float st2 = saturate(1 - length(g2 - (c2 + 0.5)) * 2.6);
          c += (h31(c2 + 5) > 0.985 ? 1 : 0) * pow(st2, 2) * 3;
          c += _SunCol.rgb * (pow(sun, 2000) * 30 + pow(sun, 40) * 0.35);
          return fixed4(c, 1);
        }
      }
      ENDCG
    }
  }
}
