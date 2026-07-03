Shader "Hidden/RewriteReality/CornerPin"
{
    // カメラ映像を四隅クアッドへ「射影正しく」貼るシェーダ。
    // 単純な2三角形のアフィン補間だと台形でテクスチャが折れるため、
    // 各頂点に同次座標 q(=w) を持たせ、フラグメントで uv/q として射影補間する（古典手法）。
    Properties
    {
        _MainTex ("Camera", 2D) = "black" {}
        _Feather ("Edge Feather", Range(0, 0.5)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 uvq    : TEXCOORD0; // (u*q, v*q, q) — 射影補間用
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uvq : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _Feather;
            float _Opacity;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvq = v.uvq;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uvq.xy / i.uvq.z;   // perspective-correct UV
                fixed4 col = tex2D(_MainTex, uv);

                // 矩形フェザリング（端のソフトエッジ）。_Feather=0 ならハードエッジ。
                float e = max(_Feather, 1e-5);
                float fx = smoothstep(0.0, e, uv.x) * smoothstep(0.0, e, 1.0 - uv.x);
                float fy = smoothstep(0.0, e, uv.y) * smoothstep(0.0, e, 1.0 - uv.y);
                col.a *= fx * fy * _Opacity;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
