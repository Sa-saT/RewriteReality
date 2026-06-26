Shader "Hidden/RewriteReality/Feedback"
{
    // 前フレーム(_HistoryTex)を減衰＋ズーム/回転して現フレームに重ね、残像/トレイルを作る。
    // Mix=0 で素通し（履歴は現フレームで上書きされ蓄積しない）。
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _HistoryTex ("History", 2D) = "black" {}
        _Decay ("Decay", Range(0, 0.99)) = 0.9
        _Zoom ("Zoom", Range(0.9, 1.1)) = 1.0
        _Rotate ("Rotate (rad)", Float) = 0
        _Mix ("Mix", Range(0, 1)) = 1
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
            sampler2D _HistoryTex;
            float _Decay, _Zoom, _Rotate, _Mix;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 cur = tex2D(_MainTex, i.uv);

                // history を中心基準でズーム/回転してトレイルを生む
                float2 c = i.uv - 0.5;
                float s = sin(_Rotate), co = cos(_Rotate);
                c = float2(c.x * co - c.y * s, c.x * s + c.y * co);
                c /= max(_Zoom, 1e-3);
                fixed4 hist = tex2D(_HistoryTex, c + 0.5) * _Decay;

                fixed4 trail = max(cur, hist);   // 明るい残像（スクリーン的）
                return lerp(cur, trail, _Mix);
            }
            ENDCG
        }
    }
    Fallback Off
}
