
            StructuredBuffer<RenderPatch> PatchList;
                 sampler2D _HeightMap;
             //修复接缝
        void FixLODConnectSeam(inout float3 vertex,inout float2 uv,RenderPatch patch){
                 uint4 lodTrans = patch.lodTrans;
                uint2 vertexIndex = floor((vertex.xz + PATCH_MESH_SIZE * 0.5 + 0.01) / PATCH_MESH_GRID_SIZE);

                float scale = exp2(_WorldMaxLod-patch.lod);
                
                //float uvGridStrip = 1.0/PATCH_MESH_GRID_COUNT;
                float uvGridStrip = 1.0/(_WorldSize * 2.0) * scale;

                //垂直
                uint lodDelta = lodTrans.x;
                if(lodDelta > 0 && vertexIndex.x == 0 ){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.y % gridStripCount;
                    if(modIndex != 0){
                        vertex.z -= PATCH_MESH_GRID_SIZE * modIndex;
                        uv.y -= uvGridStrip * modIndex;
                        return;
                    }
                }

                lodDelta = lodTrans.z;
                if(lodDelta > 0 && vertexIndex.x == PATCH_MESH_GRID_COUNT){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.y % gridStripCount;
                    if(modIndex != 0){
                        vertex.z += PATCH_MESH_GRID_SIZE * (gridStripCount - modIndex);
                        uv.y += uvGridStrip * (gridStripCount- modIndex);
                        return;
                    }
                }


                //水平
                lodDelta = lodTrans.y;
                if(lodDelta > 0 && vertexIndex.y == 0){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.x % gridStripCount;
                    if(modIndex != 0){
                        vertex.x -= PATCH_MESH_GRID_SIZE * modIndex;
                        uv.x -= uvGridStrip * modIndex;
                        return;
                    }
                }

               

                lodDelta = lodTrans.w;
                if(lodDelta > 0 && vertexIndex.y == PATCH_MESH_GRID_COUNT){
                    uint gridStripCount = pow(2,lodDelta);
                    uint modIndex = vertexIndex.x % gridStripCount;
                    if(modIndex != 0){
                        vertex.x += PATCH_MESH_GRID_SIZE * (gridStripCount- modIndex);
                        uv.x += uvGridStrip * (gridStripCount- modIndex);
                        return;
                    }
                }
            }
            

                  //在Node之间留出缝隙供Debug
            float3 ApplyNodeDebug(RenderPatch patch,float3 vertex){
                uint nodeCount = (uint)(5 * pow(2,patch.lod));
                float nodeSize = _WorldSize / nodeCount;
                uint2 nodeLoc = floor((patch.position + _WorldSize * 0.5) / nodeSize);
                float2 nodeCenterPosition = - _WorldSize * 0.5 + (nodeLoc + 0.5) * nodeSize ;
                vertex.xz = nodeCenterPosition + (vertex.xz - nodeCenterPosition) * 0.95;
                return vertex;
            }

            void GenTerrainVertex(int instanceID,inout float2 uv, inout float3 positionOS){

                RenderPatch patch = PatchList[instanceID];
                 uint lod = patch.lod;
                float scale = pow(2,_WorldMaxLod-lod);


                //程序化计算uv
                float2 newUV = (positionOS.xz * scale + patch.position + (_WorldSize * 0.5) + 0.5) / (_WorldSize + 1);
                uv = newUV;


                FixLODConnectSeam(positionOS,uv,patch);



                positionOS.xz = positionOS.xz * scale;
                positionOS.xz += patch.position;


                //input.positionOS.xyz=ApplyNodeDebug(patch,input.positionOS);

      


                float2 heightUV = (positionOS.xz + (_WorldSize * 0.5) + 0.5) / (_WorldSize + 1);
                float height = tex2Dlod(_HeightMap,float4(heightUV,0,0)).r * 2.0 - 1.0;
                positionOS.y = (height /*+ 0.001*/) * _WorldHeightSize;
                
            }



                