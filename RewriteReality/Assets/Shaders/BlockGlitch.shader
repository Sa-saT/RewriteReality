Shader "Hidden/RewriteReality/BlockGlitch"
{
    // 画面をブロック格子に分割し、ブロック単位で UV をランダムにずらす/色を飛ばすグリッチ。
    // _Seed を更新すると柄が変わる（ビート同期は C# 側で onset 時に更新）。Intensity=0 で素通し。
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Blocks ("Block Count", Vector) = (24, 14, 0, 0)
        _Intensity ("Glitched Fraction", Range(0, 1)) = 0
        _Amount ("Max Shift (UV)", Range(0, 0.5)) = 0.12
        _Seed ("Seed", Float) = 0
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
            float4 _Blocks;
            float _Intensity, _Amount, _Seed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 blocks = max(_Blocks.xy, float2(1, 1));
                float2 cell = floor(i.uv * blocks);
                float r = hash21(cell + _Seed);
                bool glitch = r < _Intensity;

                float2 uv = i.uv;
                if (glitch)
                {
                    float rx = hash21(cell + _Seed + 7.0) - 0.5;
                    float ry = hash21(cell + _Seed + 19.0) - 0.5;
                    uv += float2(rx, ry * 0.3) * _Amount;
                }

                fixed4 col = tex2D(_MainTex, uv);

                // グリッチブロックは色チャンネルを軽くずらして“壊れ”感を出す
                if (glitch)
                {
                    float ch = hash21(cell + _Seed + 3.0);
                    if (ch < 0.5) col.r = tex2D(_MainTex, uv + float2(_Amount * 0.3, 0)).r;
                    else          col.b = tex2D(_MainTex, uv - float2(_Amount * 0.3, 0)).b;
                }
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
