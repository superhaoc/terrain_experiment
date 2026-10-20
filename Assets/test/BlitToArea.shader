Shader "VirtualTexture/BlitToPhysicalPageAtlas"
{
    Properties
    {
        _MainTex ("Source Texture", 2D) = "white" {}
        _AreaRect ("Area Rect (XY as Min, ZW as Max)", Vector) = (0,0,1,1) 
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        ZWrite Off
        ZTest Always
        Cull Back
        Blend Off // 默认不混合，直接覆盖
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vertBlit
            #pragma fragment fragBlit
            #pragma target 3.5

            #pragma shader_feature NoScaleToFitRegion
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;

            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _AreaRect; 
            float2 renderTargetWH;  //Rendertexture 的高和宽(pixel)

            float4 _MainTex_TexelSize;

            Varyings vertBlit (Attributes input)
{
                Varyings output = (Varyings)0;

                const uint packedUVs = 3492u; // 逆时针顺序
                uint code = (packedUVs >> (input.vertexID * 2)) & 3;
                uint u = code & 1;
                uint v = (code >> 1) & 1;
                float2 uv = float2(u, v);
                
                output.texcoord = float2(u, 1.0 - v);
                
                float2 ndc = uv * 2.0 - 1.0;
                float4 pos = float4(ndc, UNITY_NEAR_CLIP_VALUE, 1.0);


                  //BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(2,1024,1024,512));


                float widthInNDC = 2.0 * _AreaRect.z / renderTargetWH.x; //1
                float heightInNDC = 2.0 * _AreaRect.w / renderTargetWH.y;
                float2 originInNDC = (_AreaRect.xy / renderTargetWH)  * 2.0 - 1.0;

                #ifdef NoScaleToFitRegion
                    float2 rectMaxNDC = ((_AreaRect.xy + _MainTex_TexelSize.zw) / renderTargetWH) * 2.0 - 1.0;
                    widthInNDC = rectMaxNDC.x - originInNDC.x;
                    heightInNDC = rectMaxNDC.y - originInNDC.y;
                #endif
                
                //ndc.x * 0.5 * rectSizeNDC.x + rectMinNDC.x +  0.5 * rectSizeNDC.x

                pos.x = (0.5 * widthInNDC) * pos.x + originInNDC.x + (0.5 * widthInNDC);
                pos.y = (0.5 * heightInNDC) * pos.y + originInNDC.y + (0.5 * heightInNDC);

                output.positionCS = pos;
                return output;
            }
            
            float4 fragBlit (Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
            }
            ENDHLSL
        }
    }
}