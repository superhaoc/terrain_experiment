// PSSMLit.shader
Shader "Custom/Terrain"
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
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            
            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma shader_feature _ENABLE_VSM
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "./Common.hlsl"
            #include "./TerrainCommon.hlsl"
            #include "./VirtualTexture.hlsl"
    
            

        


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;

                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 debug : TEXCOORD3;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END
            
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

        


            
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                

                // RenderPatch patch = PatchList[input.instanceID];


                // FixLODConnectSeam(input.positionOS,input.uv,patch);


                // uint lod = patch.lod;
                // float scale = pow(2,_WorldMaxLod-lod);
                // input.positionOS.xz = input.positionOS.xz * scale;
                // input.positionOS.xz += patch.position;


                // //input.positionOS.xyz=ApplyNodeDebug(patch,input.positionOS);
  


                // float2 heightUV = (input.positionOS.xz + (_WorldSize * 0.5) + 0.5) / (_WorldSize + 1);
                // float height = tex2Dlod(_HeightMap,float4(heightUV,0,0)).r;
                // input.positionOS.y = (height + 0.001) * _WorldHeightSize;
                  
                GenTerrainVertex(input.instanceID,input.uv,input.positionOS.xyz);

  

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

      

                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {

                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, frac(input.uv*1000)) * _BaseColor;
                
                
                //  if(floor(input.debug.x) == 5.0)
                //      return float4(1,0,0,1);
                //  else    if(floor(input.debug.x) == 4.0)
                //     return float4(1,1,0,1);
                //   else    if(floor(input.debug.x) == 3.0)
                //     return float4(1,1,1,1);
                
                //  return float4(0,0,0,1);
                return baseColor;
            }
            ENDHLSL
        }
        
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
            // ColorMask 0  
    
            HLSLPROGRAM
            #pragma target 2.0
            #pragma enable_d3d11_debug_symbols

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex22
            #pragma fragment DepthOnlyFragment22



            

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
        #include "./Common.hlsl"
        #include "./TerrainCommon.hlsl"
        #include "./VirtualTexture.hlsl"



        struct Attributes2
        {
              float4 position     : POSITION;
             float2 texcoord     : TEXCOORD0;
            uint instanceID : SV_InstanceID;
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

            
            GenTerrainVertex(input.instanceID,input.texcoord,input.position.xyz);

            
            output.positionCS = TransformObjectToHClip(input.position.xyz);
            output.uv = input.texcoord;
            
            return output;
        }


        float4 DepthOnlyFragment22(Varyings2 input,out float depth : SV_Depth) :SV_TARGET
        {
        
            float4 result;
            //开始以为类似 A*zeye+B,实际是1/zeye(SV_POSITION决定了的) 做hiz culling 需要注意
            depth = input.positionCS.z;

            float2 mipLevel2 = log2(_VirtualTextureSize / PAGE_SIZE);
            float max_miplevel = max(mipLevel2.x,mipLevel2.y);
            
            float mip = floor(tex_mip_level(input.uv, _VirtualTextureSize));

            /* clamp on the max we can store using the page-table */
            mip = clamp(mip, 0.f, max_miplevel);


            float2 requested_pixel = floor(input.uv * _VirtualTextureSize) / exp2(mip);
            float2 requested_page = floor(requested_pixel / PAGE_SIZE) / 256.0;
       

            float2 page_low  = frac(requested_page);
            float2 page_high = floor(requested_page);// - page_low + 0.5);

            result.rg = floor(page_low * 256.0 + 0.5) / 255.0;
            result.b = (page_high.x + page_high.y * 16.0) / 255.0;
            result.a = (128.0 + mip) / 255.0;

  

            return result;
        }


            ENDHLSL
        }
       
    }
}