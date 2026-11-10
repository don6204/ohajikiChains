Shader "Ohajiki/Summon Portrait"
{
    Properties { _MainTex("Portrait",2D)="white"{} _Color("Tint",Color)=(1,1,1,.6) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;half4 _Color;
            CBUFFER_END
            Varyings vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
            half4 frag(Varyings i):SV_Target
            {
                float d=length(i.uv-.5);clip(.5-d);
                half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,TRANSFORM_TEX(i.uv,_MainTex))*_Color;
                c.a*=saturate((.5-d)*12);return c;
            }
            ENDHLSL
        }
    }
}
