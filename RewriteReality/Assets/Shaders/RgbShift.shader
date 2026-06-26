Shader "Hidden/RewriteReality/RgbShift"
{
    // RGB チャンネルを別オフセットでサンプルして色ズレ（色収差/グリッチ）を作る。
    // R を +Offset、B を -Offset 方向へずらし、G は中央。Offset=0 で完全素通し。
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Offset  ("RGB Offset (UV)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _Offset;

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 o = _Offset;
                fixed  r = tex2D(_MainTex, i.uv + o).r;
                fixed4 g = tex2D(_MainTex, i.uv);        // 中央（G とアルファ）
                fixed  b = tex2D(_MainTex, i.uv - o).b;
                return fixed4(r, g.g, b, g.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
