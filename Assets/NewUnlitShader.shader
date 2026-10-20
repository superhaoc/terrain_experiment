Shader "Custom/TestOcclusion"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _HeightMap ("Texture", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
       
        
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
  
    
            HLSLPROGRAM
            #pragma target 2.0
            #pragma enable_d3d11_debug_symbols

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex22
            #pragma fragment DepthOnlyFragment22



            

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"



        struct Attributes2
        {
              float4 position     : POSITION;
             float2 texcoord     : TEXCOORD0;

        };

        struct Varyings2
        {

            float2 uv       : TEXCOORD0;
            float4 positionCS   : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
            

         Varyings2 DepthOnlyVertex22(Attributes2 input)
        {
            Varyings2 output = (Varyings2)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            


            
            output.positionCS = TransformObjectToHClip(input.position.xyz);
            output.uv = input.texcoord;
            
            return output;
        }

        

        float4 DepthOnlyFragment22(Varyings2 input,out float depth : SV_Depth) :SV_TARGET
        {
        

            //开始以为类似 A*zeye+B,实际是1/zeye(SV_POSITION决定了的) 做hiz culling 需要注意
            depth = input.positionCS.z ;



            return float4(0,0,0,1);
        }


            ENDHLSL
        }
       
    }
}