Shader "Hidden/RewriteReality/ColorGrade"
{
    // 露出/コントラスト/彩度/色相回転の色調整。Mix=0 で素通し。
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Exposure ("Exposure", Range(0, 3)) = 1
        _Contrast ("Contrast", Range(0, 3)) = 1
        _Saturation ("Saturation", Range(0, 3)) = 1
        _Hue ("Hue (rad)", Float) = 0
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
            float _Exposure, _Contrast, _Saturation, _Hue, _Mix;

            // (1,1,1) 軸まわりの回転＝色相回転（Rodrigues）
            float3 hueRotate(float3 col, float angle)
            {
                float3 k = float3(0.57735, 0.57735, 0.57735);
                float c = cos(angle);
                return col * c + cross(k, col) * sin(angle) + k * dot(k, col) * (1.0 - c);
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float3 c = src.rgb;

                c *= _Exposure;
                c = (c - 0.5) * _Contrast + 0.5;
                float l = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(float3(l, l, l), c, _Saturation);
                c = hueRotate(c, _Hue);
                c = saturate(c);

                return fixed4(lerp(src.rgb, c, _Mix), src.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
