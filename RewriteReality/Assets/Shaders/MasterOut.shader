Shader "Hidden/RewriteReality/MasterOut"
{
    // 出力直前のマスター段。Master（明度倍率）と Fade to Black（黒へのフェード）を掛ける。
    // Master=1 / Fade=0 のときは素通しと同一結果（呼び出し側はその場合 Blit 自体を省く）。
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Master ("Master", Range(0, 1)) = 1
        _Fade ("Fade to Black", Range(0, 1)) = 0
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
            float _Master, _Fade;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float gain = saturate(_Master) * (1.0 - saturate(_Fade));
                return fixed4(src.rgb * gain, src.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
