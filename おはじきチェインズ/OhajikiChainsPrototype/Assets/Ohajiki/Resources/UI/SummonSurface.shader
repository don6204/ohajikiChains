Shader "Ohajiki/Summon Surface"
{
    Properties { _BaseColor("Color",Color)=(1,1,1,1) _Glow("Glow",Range(0,2))=0 _MainTex("Surface",2D)="white"{} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; float2 uv:TEXCOORD2; };
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _Glow; float4 _MainTex_ST;
            CBUFFER_END
            Varyings vert(Attributes input)
            {Varyings o;o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.positionWS=TransformObjectToWorld(input.positionOS.xyz);o.normalWS=TransformObjectToWorldNormal(input.normalOS);o.uv=TRANSFORM_TEX(input.uv,_MainTex);return o;}
            half4 frag(Varyings i):SV_Target
            {
                half3 n=normalize(i.normalWS),v=normalize(_WorldSpaceCameraPos-i.positionWS),l=normalize(half3(-.45,.8,-.5));
                half diffuse=saturate(dot(n,l));half rim=pow(1-saturate(dot(n,v)),3);
                half spec=pow(saturate(dot(n,normalize(l+v))),48);
                half3 c=_BaseColor.rgb*SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb*(.42+.65*diffuse+_Glow)+half3(.72,.9,1)*rim*.24+spec*.7;
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
