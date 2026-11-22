Shader "Hidden/RewriteReality/LayerBlend"
{
    // 映像レイヤー合成（#41）。_BaseTex（下地）の上へ _MainTex（重ねる層）を _Mode で混ぜ、
    // out = lerp(base, blend(base, layer), _Opacity * layer.a)。
    // _Mode: 0=Normal / 1=Add / 2=Screen / 3=Multiply（ShowTimeline.LayerBlend と同順）。
    Properties
    {
        _MainTex ("Layer", 2D) = "black" {}
        _BaseTex ("Base", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Mode ("Blend Mode", Float) = 0
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
            sampler2D _BaseTex;
            float _Opacity;
            float _Mode;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 layer = tex2D(_MainTex, i.uv);
                fixed4 b = tex2D(_BaseTex, i.uv);

                float3 blended = layer.rgb;                                              // Normal
                if (_Mode > 2.5)      blended = b.rgb * layer.rgb;                       // Multiply
                else if (_Mode > 1.5) blended = 1.0 - (1.0 - b.rgb) * (1.0 - layer.rgb); // Screen
                else if (_Mode > 0.5) blended = b.rgb + layer.rgb;                       // Add

                float w = saturate(_Opacity) * layer.a;
                float3 rgb = lerp(b.rgb, saturate(blended), w);
                return fixed4(rgb, max(b.a, w));
            }
            ENDCG
        }
    }
    Fallback Off
}
