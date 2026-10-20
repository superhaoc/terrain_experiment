using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;

namespace GPUTerrain
{
    public class GPUTerrainRenderer : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public ComputeShader terrainCS;
            public ComputeShader hiZGenCS;
            public ComputeShader VTCS;
            public Material terrainMaterial;
            public Material boundsDebugMaterial;
            public Mesh patchMesh;
            public Camera camera;
            public int worldSizeSectors = 160;
            public int sectorSize = 64;
            public int maxLod = 5;
            public float lodDistanceFactor = 200f;
            public Vector3 worldCenter = new Vector3(5120f, 0f, 5120f); // 10km/2

            public Vector3 worldSize = new Vector3(10240, 512, 10240);



            public Vector4 nodeEvaluationC = new Vector4(1, 0, 0, 0);

            public Texture2D heightMap;
            public Texture2D minMaxHeightMap;


            public bool isBoundsBufferOn;
            public bool EnableFrustumCull;
            public bool EnableHizOcclusion;

            public Mesh unitCubeMesh;

            public GPUDrivenTerrainLearn.TerrainAsset _asset;

            public Material testmat;

            public Vector2 VtexSize;

            public Texture2D testTexture2d;

        }

        public Settings settings = new Settings();

        private TerrainRenderPass renderPass;

        public override void Create()
        {
            renderPass = new TerrainRenderPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.camera == null)
                settings.camera = Camera.main;
            renderer.EnqueuePass(renderPass);
        }


        protected override void Dispose(bool disposing)
        {
            renderPass?.Dispose();
        }

        class TerrainRenderPass : ScriptableRenderPass
        {
            private Settings settings;


            private ComputeBuffer _maxLODNodeList;
            private ComputeBuffer _nodeListA;
            private ComputeBuffer _nodeListB;
            private ComputeBuffer _finalNodeListBuffer;

            private ComputeBuffer _indirectArgsBuffer;


            private ComputeBuffer _culledPatchBuffer;
            private ComputeBuffer _patchIndirectArgs;


            private ComputeBuffer _patchBoundsBuffer;
            private ComputeBuffer _patchBoundsIndirectArgs;

            private RenderTexture _lodMap;
            private RenderTexture _hizDepthMap;

            private RenderTexture _hizDepthMap2;

            private ComputeBuffer _PackPageData;
            private ComputeBuffer _uniqueBuffer;
            private ComputeBuffer _counterBuffer;
            private RTHandle _physicsTexture;
            private RTHandle _pageTableTexture;


            private RTHandle DepthTexture;
            private RTHandle CustomColorTexture;

            private RenderTargetIdentifier DepthAttachment;


            // ������
            private ComputeBuffer nodeBuffer;
            private ComputeBuffer visibleNodeListAppendBuffer;
            private ComputeBuffer visibleNodeArrayBuffer;
            private ComputeBuffer visibleNodeCounterBuffer;
            private ComputeBuffer patchInstanceBuffer;
            private ComputeBuffer indirectArgsBuffer;



            private int kernelTraverse;

            private int kernelHizGenInit;
            private int kernelHiZReduce;

            private int kernelResolvePage;
            private int kernelDeduplicate;
            private int kernel_ParallelBitonic_B16;
            private int kernel_ParallelBitonic_B8;
            private int kernel_ParallelBitonic_B4;
            private int kernel_ParallelBitonic_B2;
            private int kernel_ParallelBitonic_C4;
            private int kernel_ParallelBitonic_C2;
            private int kernel_ParallelBitonic_B32;
            private int kernel_ClearBuffer;

            private int kernelBuildPatches;
            private int kernelBuildLODMap;
            private int kernelCull;

            // ����
            private uint[] indirectData = new uint[5];
            private int[] mipSizes = new int[6] { 160, 80, 40, 20, 10, 5 };
            private NodeDescription[] nodeArray;
            private int nodeCount;
            private Vector4[] worldLODParams = null;

            private int[] lodoffsets = null;

            private int _tempNodeBufferSize = 1024;
            private int _maxNodeBufferSize = 200;

            private const int PatchStripSize = 9 * 4;
            private int DataBufferSize;
            private Material m_blitMaterial;


            public TerrainRenderPass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

            }

            public void Init(RenderTextureDescriptor cameraTextureDescriptor)
            {
                Dispose();
                InitializeData();
                InitializeBuffers(cameraTextureDescriptor);
                InitializeKernels();
                InitializeShaders();
            }

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {

                Init(cameraTextureDescriptor);
                //ConfigureInput(ScriptableRenderPassInput.Depth);
            }




            private void InitializeData()
            {
                // �����Ĳ����ڵ�
                nodeArray = TerrainQuadTreeGenerator.GenerateQuadTree(
                    settings.worldSizeSectors, settings.sectorSize, settings.maxLod, out lodoffsets);
                nodeCount = nodeArray.Length;

                float wSize = settings.worldSize.x;
                int curNodeCount = settings.maxLod;
                worldLODParams = new Vector4[settings.maxLod + 1];
                for (var lod = settings.maxLod; lod >= 0; lod--)
                {
                    var nodeSize = wSize / curNodeCount;
                    var patchExtent = nodeSize / 16;
                    var sectorCountPerNode = (int)Mathf.Pow(2, lod);
                    worldLODParams[settings.maxLod - lod] = new Vector4(nodeSize, patchExtent, curNodeCount, sectorCountPerNode);
                    //worldLODParams[lod] = new Vector4(nodeSize,patchExtent,curNodeCount,sectorCountPerNode);
                    curNodeCount *= 2;
                }


                // _computeShader.SetVectorArray(ShaderConstants.WorldLodParams,worldLODParams);

            }


            private void InitMaxLODNodeListDatas()
            {
                var maxLODNodeCount = settings.maxLod;
                uint2[] datas = new uint2[maxLODNodeCount * maxLODNodeCount];
                var index = 0;
                for (uint i = 0; i < maxLODNodeCount; i++)
                {
                    for (uint j = 0; j < maxLODNodeCount; j++)
                    {
                        datas[index] = new uint2(i, j);
                        index++;
                    }
                }
                _maxLODNodeList.SetData(datas);
            }
            private void InitializeBuffers(RenderTextureDescriptor cameraTextureDescriptor)
            {


                _culledPatchBuffer = new ComputeBuffer(_maxNodeBufferSize * 64, PatchStripSize, ComputeBufferType.Append);
                _patchIndirectArgs = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
                _patchIndirectArgs.SetData(new uint[] { settings.patchMesh.GetIndexCount(0), 0, 0, 0, 0 });


                _patchBoundsIndirectArgs = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
                _patchBoundsIndirectArgs.SetData(new uint[] { settings.unitCubeMesh.GetIndexCount(0), 0, 0, 0, 0 });


                // �ڵ㻺����
                int nodeStride = Marshal.SizeOf<NodeDescription>();
                nodeBuffer = new ComputeBuffer(nodeCount, nodeStride);
                nodeBuffer.SetData(nodeArray);


                _maxLODNodeList = new ComputeBuffer(settings.maxLod * settings.maxLod, 8, ComputeBufferType.Append);
                InitMaxLODNodeListDatas();

                _nodeListA = new ComputeBuffer(_tempNodeBufferSize, 8, ComputeBufferType.Append);
                _nodeListB = new ComputeBuffer(_tempNodeBufferSize, 8, ComputeBufferType.Append);

                _indirectArgsBuffer = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
                _indirectArgsBuffer.SetData(new uint[] { 1, 1, 1 });

                _finalNodeListBuffer = new ComputeBuffer(_maxNodeBufferSize, 12, ComputeBufferType.Append);

                _patchBoundsBuffer = new ComputeBuffer(_maxNodeBufferSize * 64, 4 * 10, ComputeBufferType.Append);


                //10240m = 160 * 64m
                if (_lodMap == null)
                    _lodMap = TextureUtility.CreateLODMap(160);


                if (_hizDepthMap == null)
                    _hizDepthMap = TextureUtility.CreateHizDepthMap(cameraTextureDescriptor.width, cameraTextureDescriptor.height, "_hizDepthMap");

                if (_hizDepthMap2 == null)
                    _hizDepthMap2 = TextureUtility.CreateHizDepthMap(cameraTextureDescriptor.width, cameraTextureDescriptor.height, "_hizDepthMap2");

                // ��Ӳ���������?
                indirectArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                indirectData[0] = settings.patchMesh.GetIndexCount(0);
                indirectData[1] = 0; // ʵ������ʼΪ0
                indirectArgsBuffer.SetData(indirectData);

                DataBufferSize = cameraTextureDescriptor.width * cameraTextureDescriptor.height;
                int paddedDataBufferSize = Mathf.NextPowerOfTwo(DataBufferSize);
                _PackPageData = new ComputeBuffer(paddedDataBufferSize, Marshal.SizeOf(typeof(uint)));
                _PackPageData.SetData(new uint[paddedDataBufferSize]);
                _uniqueBuffer = new ComputeBuffer(10240, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);

                _counterBuffer?.Release();
                _counterBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);




                if (DepthTexture == null || DepthTexture.rt.width != cameraTextureDescriptor.width || DepthTexture.rt.height != cameraTextureDescriptor.height)
                {
                    DepthTexture?.Release();
                    RenderTextureDescriptor depthDescriptor = cameraTextureDescriptor;
                    depthDescriptor.graphicsFormat = GraphicsFormat.None;
                    depthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat;
                    depthDescriptor.depthBufferBits = 32;

                    depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
                    RenderingUtils.ReAllocateIfNeeded(ref DepthTexture, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_MyCameraDepthTexture");
                    DepthAttachment = new RenderTargetIdentifier(DepthTexture.nameID, 0, CubemapFace.Unknown, -1);

                }

                if (CustomColorTexture == null || CustomColorTexture.rt.width != cameraTextureDescriptor.width || CustomColorTexture.rt.height != cameraTextureDescriptor.height)
                {
                    CustomColorTexture?.Release();
                    RenderTextureDescriptor halfColorDescriptor = cameraTextureDescriptor;
                    halfColorDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                    halfColorDescriptor.depthStencilFormat = GraphicsFormat.None;
                    halfColorDescriptor.depthBufferBits = 0;
                    halfColorDescriptor.msaaSamples = 1;


                    RenderingUtils.ReAllocateIfNeeded(ref CustomColorTexture, halfColorDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_MyCameraColorTexture");

                }

                if (_physicsTexture == null)
                {
                    _physicsTexture?.Release();
                    RenderTextureDescriptor phyTextureDescriptor = new RenderTextureDescriptor(2048, 2048, GraphicsFormat.R8G8B8A8_UNorm, 0, 1);
                    phyTextureDescriptor.enableRandomWrite = true;
                    RenderingUtils.ReAllocateIfNeeded(ref _physicsTexture, phyTextureDescriptor, FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: "_MyPhyTex");
                }

                if (_pageTableTexture == null)
                {
                    _pageTableTexture?.Release();
                    const int PageSizeInPixel = 64;
                    int pageTableWidth = Mathf.FloorToInt(settings.VtexSize.x / PageSizeInPixel);
                    int pageTableHeight = Mathf.FloorToInt(settings.VtexSize.y / PageSizeInPixel);
                    int pageTableMipCount =  (int)Mathf.Log(pageTableWidth,2);

                    RenderTextureDescriptor tableDescriptor = new RenderTextureDescriptor(pageTableWidth, pageTableHeight, GraphicsFormat.R8G8B8A8_UNorm, 0, pageTableMipCount);
                    tableDescriptor.useMipMap = true;
                    tableDescriptor.autoGenerateMips = false;
                    tableDescriptor.enableRandomWrite = true;


                    RenderingUtils.ReAllocateIfNeeded(ref _pageTableTexture, tableDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "virtTableTex");

                }
            }

            private void InitializeShaders()
            {

                if (settings.isBoundsBufferOn)
                {
                    settings.terrainCS.EnableKeyword("BOUNDS_DEBUG");
                }
                else
                {
                    settings.terrainCS.DisableKeyword("BOUNDS_DEBUG");
                }

                if (settings.EnableFrustumCull)
                {
                    settings.terrainCS.EnableKeyword("ENABLE_FRUS_CULL");
                }
                else
                {
                    settings.terrainCS.DisableKeyword("ENABLE_FRUS_CULL");
                }

                if(m_blitMaterial==null)
                    m_blitMaterial = new Material(Shader.Find("VirtualTexture/BlitToPhysicalPageAtlas"));
            }

            private void InitializeKernels()
            {
                kernelTraverse = settings.terrainCS.FindKernel("TraverseQuadtree");
                kernelBuildPatches = settings.terrainCS.FindKernel("BuildPatches");
                kernelBuildLODMap = settings.terrainCS.FindKernel("BuildLodMap");


                kernelHizGenInit = settings.hiZGenCS.FindKernel("InitHiZ");
                kernelHiZReduce = settings.hiZGenCS.FindKernel("HiZReduce");

                kernelResolvePage = settings.VTCS.FindKernel("PackFeedbackIntoUint");
                kernelDeduplicate = settings.VTCS.FindKernel("Deduplicate");


                kernel_ParallelBitonic_B16 = settings.VTCS.FindKernel("ParallelBitonic_B16");
                kernel_ParallelBitonic_B8 = settings.VTCS.FindKernel("ParallelBitonic_B8");
                kernel_ParallelBitonic_B4 = settings.VTCS.FindKernel("ParallelBitonic_B4");
                kernel_ParallelBitonic_B2 = settings.VTCS.FindKernel("ParallelBitonic_B2");
                kernel_ParallelBitonic_C4 = settings.VTCS.FindKernel("ParallelBitonic_C4");
                kernel_ParallelBitonic_C2 = settings.VTCS.FindKernel("ParallelBitonic_C2");
                kernel_ParallelBitonic_B32 = settings.VTCS.FindKernel("ParallelBitonic_B32");
                kernel_ClearBuffer = settings.VTCS.FindKernel("ClearBuffer");



                // ���ó���
                settings.terrainCS.SetInt("_WorldSizeSectors", settings.worldSizeSectors);
                settings.terrainCS.SetFloat("_SectorSize", settings.sectorSize);
                settings.terrainCS.SetInt("_NodeCount", nodeCount);
                settings.terrainCS.SetInt("_MaxLod", settings.maxLod);
                settings.terrainCS.SetFloat("_LODDistanceFactor", settings.lodDistanceFactor);

                settings.terrainCS.SetVectorArray("WorldLodParams", worldLODParams);


                int[] nodeIDOffsetLOD = new int[(5 + 1) * 4];
                int nodeIdOffset = 0;
                for (int lod = 5; lod >= 0; lod--)
                {
                    nodeIDOffsetLOD[lod * 4] = nodeIdOffset;
                    nodeIdOffset += (int)(worldLODParams[5 - lod].z * worldLODParams[5 - lod].z);
                }

                settings.terrainCS.SetInts("NodeIDOffsetOfLOD", lodoffsets);

                BindComputeShader(settings.terrainCS, kernelTraverse);
                BindComputeShader(settings.terrainCS, kernelBuildPatches);
                BindComputeShader(settings.terrainCS, kernelBuildLODMap);
                BindComputeShader(settings.hiZGenCS, kernelHizGenInit);
                BindComputeShader(settings.VTCS, kernelResolvePage);

            }


            private void BindComputeShader(ComputeShader compute, int kernelIndex)
            {
                if (compute == settings.terrainCS)
                {
                    if (kernelIndex == kernelTraverse)
                    {
                        settings.terrainCS.SetBuffer(kernelIndex, "AppendFinalNodeList", _finalNodeListBuffer);
                        settings.terrainCS.SetBuffer(kernelIndex, "_Nodes", nodeBuffer);
                        settings.terrainCS.SetTexture(kernelIndex, "MinMaxHeightTexture", settings.minMaxHeightMap);
                        // settings.terrainCS.SetTexture(kernelIndex,"MinMaxHeightTexture",settings._asset.minMaxHeightMap);
                    }
                    else if (kernelIndex == kernelBuildLODMap)
                    {
                        settings.terrainCS.SetTexture(kernelIndex, "_LodMap", _lodMap);
                        settings.terrainCS.SetBuffer(kernelIndex, "_Nodes", nodeBuffer);

                    }
                    else if (kernelIndex == kernelBuildPatches)
                    {
                        settings.terrainCS.SetTexture(kernelIndex, "_LodMap", _lodMap);
                        settings.terrainCS.SetBuffer(kernelIndex, "FinalNodeList", _finalNodeListBuffer);
                        settings.terrainCS.SetBuffer(kernelIndex, "CulledPatchList", _culledPatchBuffer);

                        settings.terrainCS.SetBuffer(kernelIndex, "PatchBoundsList", _patchBoundsBuffer);
                        settings.terrainCS.SetTexture(kernelIndex, "MinMaxHeightTexture", settings.minMaxHeightMap);

                        // if (settings.EnableHizOcclusion)
                        {

                            // settings.terrainCS.SetTexture(kernelIndex, "HiZMipMap", _hizDepthMap);



                        }
                        //settings.terrainCS.SetTexture(kernelIndex,"MinMaxHeightTexture",settings._asset.minMaxHeightMap);
                    }
                }


                else if (compute == settings.hiZGenCS)
                {

                    if (kernelIndex == kernelHizGenInit)
                    {
                        // settings.hiZGenCS.SetTexture(kernelIndex, "_CameraDepthTexture", DepthTexture);
                        // settings.hiZGenCS.SetTexture(kernelIndex, "_HiZTexture", _hizDepthMap2);

                        // settings.hiZGenCS.SetInt("_HiZWidth", _hizDepthMap.width);
                        // settings.hiZGenCS.SetInt("_HiZHeight", _hizDepthMap.height);

                        settings.hiZGenCS.SetVector("_SrcSize", new Vector4(DepthTexture.rt.width, DepthTexture.rt.height, 0, 0));
                        settings.hiZGenCS.SetVector("_DstSize", new Vector4(_hizDepthMap2.width, _hizDepthMap2.height, 0, 0));
                    }

                }
                else if (compute == settings.VTCS)
                {
                    if (kernelIndex == kernelResolvePage)
                    {
                        settings.VTCS.SetTexture(kernelIndex, "RequestTexture", CustomColorTexture);
                    }
                }
            }

            private void ClearBufferCounter(CommandBuffer cmd)
            {
                cmd.SetBufferCounterValue(_maxLODNodeList, (uint)_maxLODNodeList.count);
                cmd.SetBufferCounterValue(_nodeListA, 0);
                cmd.SetBufferCounterValue(_nodeListB, 0);

                cmd.SetBufferCounterValue(_finalNodeListBuffer, 0);
                cmd.SetBufferCounterValue(_culledPatchBuffer, 0);

                cmd.SetBufferCounterValue(_patchBoundsBuffer, 0);
                cmd.SetBufferCounterValue(_uniqueBuffer, 0); ;

            }


            private void ApplyTerrainMaterial()
            {
                if (settings.terrainMaterial)
                {

                    settings.terrainMaterial.SetBuffer("PatchList", _culledPatchBuffer);
                    settings.terrainMaterial.SetTexture("_HeightMap", settings.heightMap);
                    settings.terrainMaterial.SetVector("_VirtualTextureSize", settings.VtexSize);
                }


                if (settings.boundsDebugMaterial && settings.isBoundsBufferOn)
                {
                    settings.boundsDebugMaterial.SetBuffer("BoundsList", _patchBoundsBuffer);
                }

            }

            Plane[] _cameraFrustumPlanes = new Plane[6];
            Vector4[] _cameraFrustumPlanesV4 = new Vector4[6];

            void UpdateCameraFrustumPlanes(Camera camera)
            {


                GeometryUtility.CalculateFrustumPlanes(camera, _cameraFrustumPlanes);

                for (var i = 0; i < _cameraFrustumPlanes.Length; i++)
                {
                    Vector4 v4 = (Vector4)_cameraFrustumPlanes[i].normal;
                    v4.w = _cameraFrustumPlanes[i].distance;
                    _cameraFrustumPlanesV4[i] = v4;
                }
                settings.terrainCS.SetVectorArray("_FrustumPlanes2", _cameraFrustumPlanesV4);
            }

            void BitonicSort_fastest(CommandBuffer cmd)
            {
                const int THREADNUM_X = 64;
                int n = _PackPageData.count;

                cmd.SetComputeBufferParam(settings.VTCS, kernel_ParallelBitonic_B32, "_Data", _PackPageData);
                cmd.SetComputeBufferParam(settings.VTCS, kernel_ParallelBitonic_B16, "_Data", _PackPageData);
                cmd.SetComputeBufferParam(settings.VTCS, kernel_ParallelBitonic_B8, "_Data", _PackPageData);
                cmd.SetComputeBufferParam(settings.VTCS, kernel_ParallelBitonic_C4, "_Data", _PackPageData);
                cmd.SetComputeBufferParam(settings.VTCS, kernel_ParallelBitonic_C2, "_Data", _PackPageData);

                int nlog = (int)(Mathf.Log(n, 2));
                int B_indx, inc;
                int kernel_id;

                for (int i = 0; i < nlog; i++)
                {
                    inc = 1 << i;
                    for (int j = 0; j < i + 1; j++)
                    {
                        if (inc <= 256) break;

                        if (inc >= 8192)
                        {
                            B_indx = 32;
                            kernel_id = kernel_ParallelBitonic_B32;
                        }
                        else if (inc >= 4096)
                        {
                            B_indx = 16;
                            kernel_id = kernel_ParallelBitonic_B16;
                        }
                        else
                        {
                            B_indx = 8;
                            kernel_id = kernel_ParallelBitonic_B8;
                        }

                        cmd.SetComputeIntParam(settings.VTCS, "inc", inc * 2 / B_indx);
                        cmd.SetComputeIntParam(settings.VTCS, "dir", 2 << i);
                        cmd.DispatchCompute(settings.VTCS, kernel_id, n / B_indx / THREADNUM_X, 1, 1);
                        inc /= B_indx;
                    }

                    cmd.SetComputeIntParam(settings.VTCS, "inc0", inc);
                    cmd.SetComputeIntParam(settings.VTCS, "dir", 2 << i);

                    // C4 works correctly only when log2(inc0) is odd: inc0 in {8, 32, 128}
                    if (inc == 8 || inc == 32 || inc == 128)
                    {
                        cmd.DispatchCompute(settings.VTCS, kernel_ParallelBitonic_C4, n / 4 / 128, 1, 1);
                    }
                    else
                    {
                        cmd.DispatchCompute(settings.VTCS, kernel_ParallelBitonic_C2, n / 2 / 256, 1, 1);
                    }
                }
            }



            void BlitSpecifiedArea(CommandBuffer cmd,Texture2D text2d,Rect area,bool fitToArea = false)
            {
                cmd.SetRenderTarget(_physicsTexture);
                cmd.SetViewport(new Rect( 0,0,_physicsTexture.rt.width,_physicsTexture.rt.height));
                //cmd.ClearRenderTarget(true, true, Color.red);


                 MaterialPropertyBlock matBlock = new MaterialPropertyBlock();
                if(!fitToArea)
                {
                 
                     Shader.EnableKeyword("NoScaleToFitRegion");
                }
                else
                {
                    Shader.DisableKeyword("NoScaleToFitRegion");
                }
                
                   matBlock.SetTexture("_MainTex",text2d);
                   matBlock.SetVector("_AreaRect", new Vector4(area.x,area.y,area.width,area.height));
                   matBlock.SetVector("renderTargetWH",new Vector2(_physicsTexture.rt.width,_physicsTexture.rt.height));
                //m_blitMaterial.SetTexture("_MainTex",text2d);
                //m_blitMaterial.SetVector("_AreaRect", new Vector4(area.x,area.y,area.width,area.height));
                //m_blitMaterial.SetVector("renderTargetWH",new Vector2(_physicsTexture.rt.width,_physicsTexture.rt.height));

                
                cmd.DrawProcedural(Matrix4x4.identity, m_blitMaterial, 0, MeshTopology.Triangles, 6,1,matBlock);
            }
            /// <summary>
            /// feedback pass ʵ��Ӧ����Ⱦ��һ�ŵͷֱ��ʵ�rendertarget
            /// ���ڼ���request resolve ����
            /// </summary>
            /// <param name="renderingData"></param>
            /// <param name="cmd"></param>
            
            bool testtag =false;
            void ResolvePageRequest(ref RenderingData renderingData, CommandBuffer cmd)
            {
                if(testtag)
                    return;
               
                //return;
                // Clear sort buffer to 0 before packing (0 entries are skipped by dedup)
                cmd.SetComputeIntParam(settings.VTCS, "_ClearCount", _PackPageData.count);
                cmd.SetComputeBufferParam(settings.VTCS, kernel_ClearBuffer, "_Data", _PackPageData);
                cmd.DispatchCompute(settings.VTCS, kernel_ClearBuffer, Mathf.CeilToInt((float)_PackPageData.count / 512), 1, 1);

                cmd.SetComputeBufferParam(settings.VTCS, kernelResolvePage, "_Data", _PackPageData);
                cmd.DispatchCompute(settings.VTCS, kernelResolvePage, Mathf.FloorToInt(CustomColorTexture.rt.width / 8), Mathf.FloorToInt(CustomColorTexture.rt.height / 8), 1);



                BitonicSort_fastest(cmd);


                cmd.SetComputeBufferParam(settings.VTCS, kernelDeduplicate, "_Data", _PackPageData);
                cmd.SetComputeBufferParam(settings.VTCS, kernelDeduplicate, "_UniqueKeys", _uniqueBuffer);
                //5 /3  = 1
                //5 %3 = 2
  
                //5 - 3*1 = 2

                //512 / 64 / 3
                
                const int BLOCK_SIZE = 4;
                cmd.SetComputeIntParam(settings.VTCS,"_DataLength",_PackPageData.count);
                cmd.DispatchCompute(settings.VTCS, kernelDeduplicate, Mathf.CeilToInt((float)_PackPageData.count / BLOCK_SIZE / 64), 1, 1);



                cmd.RequestAsyncReadback(_uniqueBuffer, (AsyncGPUReadbackRequest request) =>
                {
                    if (request.hasError)
                    {
                        Debug.LogError("Async GPU readback failed.");
                        return;
                    }

                    ComputeBuffer.CopyCount(_uniqueBuffer, _counterBuffer, 0);

                    int[] counterData = new int[1];
                    // 3. ��ȡ�������������� CPU �ɷ��ʵ�����
                    _counterBuffer.GetData(counterData);
                    // 4. ��ȡʵ�ʵ�Ԫ������
                    int appendedCount = counterData[0];


                    // 6. ��ȫ�ػ�ȡ�Ͳ�������
                    NativeArray<uint> data = request.GetData<uint>();

                    for (int i = 0; i < appendedCount; i++)
                    {
                        uint key = (uint)data[i];

                        uint mip = (uint)((key >> 24) & 0xFF);
                        uint pageX = (uint)((key >> 12) & 0xFFF);
                        uint pageY = (uint)(key & 0xFFF);
                        if (data.Length > 0)
                        {
                            Debug.Log($"mip {mip} pagex {pageX} pagey {pageY}");
                        }
                    }

                    data.Dispose();

                    testtag = true;
                });

            }
            static bool executeOnlyOnce = false;
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {

                CommandBuffer cmd = CommandBufferPool.Get("GPUTerrain");
                cmd.Clear();


                UpdateCameraFrustumPlanes(Camera.main);
                ClearBufferCounter(cmd);
                cmd.SetComputeVectorParam(settings.terrainCS, "_CameraPositionWS", Camera.main.transform.position);
                cmd.SetComputeVectorParam(settings.terrainCS, "_NodeEvaluationC", settings.nodeEvaluationC);

                cmd.CopyCounterValue(_maxLODNodeList, _indirectArgsBuffer, 0);
                ComputeBuffer consumeNodeList = _nodeListA;
                ComputeBuffer appendNodeList = _nodeListB;
                // for(var lod = TerrainAsset.MAX_LOD; lod >=0; lod --){

                for (var lod = 0; lod <= settings.maxLod; lod++)
                {
                    cmd.SetComputeIntParam(settings.terrainCS, "PassLOD", lod);
                    if (lod == 0)
                    {
                        cmd.SetComputeBufferParam(settings.terrainCS, kernelTraverse, "ConsumeNodeList", _maxLODNodeList);
                    }
                    else
                    {
                        cmd.SetComputeBufferParam(settings.terrainCS, kernelTraverse, "ConsumeNodeList", consumeNodeList);
                    }
                    cmd.SetComputeBufferParam(settings.terrainCS, kernelTraverse, "AppendNodeList", appendNodeList);
                    cmd.DispatchCompute(settings.terrainCS, kernelTraverse, _indirectArgsBuffer, 0);
                    cmd.CopyCounterValue(appendNodeList, _indirectArgsBuffer, 0);
                    var temp = consumeNodeList;
                    consumeNodeList = appendNodeList;
                    appendNodeList = temp;
                }


                //����LodMap
                cmd.DispatchCompute(settings.terrainCS, kernelBuildLODMap, 20, 20, 1);

                //build patch

                cmd.CopyCounterValue(_finalNodeListBuffer, _indirectArgsBuffer, 0);

                cmd.SetComputeTextureParam(settings.terrainCS, kernelBuildPatches, "HiZMipMap", _hizDepthMap);
                cmd.DispatchCompute(settings.terrainCS, kernelBuildPatches, _indirectArgsBuffer, 0);

                cmd.CopyCounterValue(_culledPatchBuffer, _patchIndirectArgs, 4);


                ApplyTerrainMaterial();
                cmd.DrawMeshInstancedIndirect(settings.patchMesh, 0, settings.terrainMaterial, 0, _patchIndirectArgs);

                if (settings.isBoundsBufferOn)
                {
                    cmd.CopyCounterValue(_patchBoundsBuffer, _patchBoundsIndirectArgs, 4);
                    cmd.DrawMeshInstancedIndirect(settings.unitCubeMesh, 0, settings.boundsDebugMaterial, 0, _patchBoundsIndirectArgs);
                }


                var ddd = (GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, true) * Camera.main.worldToCameraMatrix) * new Vector4(0, 0, 1097, 1);
                var camera = Camera.main;
                var matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix;
                cmd.SetComputeMatrixParam(settings.terrainCS, "_HizCameraMatrixVP", matrixVP);
                cmd.SetComputeVectorParam(settings.terrainCS, "_HizMapSize", new Vector4(_hizDepthMap2.width, _hizDepthMap2.height, _hizDepthMap2.mipmapCount));
                //executeOnlyOnce = false;
                if (settings.EnableHizOcclusion)
                {

                    cmd.BeginSample("hiz occlusion");


                    // ���������ͬʱ����Ϊ��ɫ�����Ŀ��


                    RenderTargetIdentifier oldColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    RenderTargetIdentifier oldDepthTarget = renderingData.cameraData.renderer.cameraDepthTargetHandle;

                    cmd.SetRenderTarget(CustomColorTexture, DepthTexture);
                    cmd.SetViewport(new Rect(0, 0, renderingData.cameraData.cameraTargetDescriptor.width, renderingData.cameraData.cameraTargetDescriptor.height));
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    cmd.DrawMeshInstancedIndirect(settings.patchMesh, 0, settings.terrainMaterial, 1, _patchIndirectArgs);
                    // cmd.DrawMesh(settings.unitCubeMesh, Matrix4x4.TRS(new Vector3(3220f,27f, 3881f), Quaternion.identity, new Vector3(1, 14, 13)), settings.testmat);
                    cmd.SetRenderTarget(oldColorTarget, oldDepthTarget);
                    //      cmd.RequestAsyncReadback(DepthTexture, 0, TextureFormat.RFloat, (request) => {
                    //     if (request.hasError) { Debug.LogError("Readback failed"); return; }
                    //         var tex = new Texture2D(nWidth, nHeight, TextureFormat.RFloat, false);
                    //         tex.LoadRawTextureData(request.GetData<float>());
                    //         tex.Apply();
                    //         File.WriteAllBytes("Assets/test_async.png", tex.EncodeToPNG());
                    //         Object.DestroyImmediate(tex);
                    // });

                    uint threadX, threadY, threadZ;
                    settings.hiZGenCS.GetKernelThreadGroupSizes(kernelHizGenInit, out threadX, out threadY, out threadZ);
                    int groupsX = Mathf.FloorToInt(_hizDepthMap2.width / threadX);
                    int groupsY = Mathf.FloorToInt(_hizDepthMap2.height / threadY);

                    cmd.SetComputeTextureParam(settings.hiZGenCS, kernelHizGenInit, "_CameraDepthTexture", DepthTexture);
                    cmd.SetComputeTextureParam(settings.hiZGenCS, kernelHizGenInit, "_HiZTexture", _hizDepthMap2, 0);
                    cmd.DispatchCompute(settings.hiZGenCS, kernelHizGenInit, groupsX, groupsY, 1);
                    // // //  cmd.SetComputeTextureParam(settings, "InitHiZ", "_CameraDepthTexture", BuiltinRenderTextureType.Depth);

                    //         cmd.RequestAsyncReadback(_hizDepthMap, 0, TextureFormat.RFloat, (request) => {
                    //     if (request.hasError) { Debug.LogError("Readback failed"); return; }
                    //         var tex = new Texture2D(_hizDepthMap.width, _hizDepthMap.height, TextureFormat.RFloat, false);
                    //         tex.LoadRawTextureData(request.GetData<float>());
                    //         tex.Apply();
                    //         File.WriteAllBytes("Assets/test_async.png", tex.EncodeToPNG());
                    //         Object.DestroyImmediate(tex);
                    // });


                    settings.hiZGenCS.GetKernelThreadGroupSizes(kernelHiZReduce, out threadX, out threadY, out threadZ);
                    for (int i = 1; i < _hizDepthMap2.mipmapCount; i++)
                    {
                        int width = _hizDepthMap2.width >> i;
                        int height = _hizDepthMap2.height >> i;

                        int srcWidth = Mathf.Max(1, _hizDepthMap2.width >> (i - 1));
                        int srcHeight = Mathf.Max(1, _hizDepthMap2.height >> (i - 1));
                        cmd.SetComputeIntParams(settings.hiZGenCS, "_SrcMipSize", srcWidth, srcHeight);
                        cmd.SetComputeTextureParam(settings.hiZGenCS, kernelHiZReduce, "_HiSrcZTexture", _hizDepthMap2, i - 1);
                        cmd.SetComputeTextureParam(settings.hiZGenCS, kernelHiZReduce, "_HiZDstTexture", _hizDepthMap2, i);
                        cmd.SetComputeVectorParam(settings.hiZGenCS, "_DstSize", new Vector4(width, height, 0, 0));

                        groupsX = Mathf.CeilToInt((float)width / threadX);
                        groupsY = Mathf.CeilToInt((float)height / threadY);
                        cmd.DispatchCompute(settings.hiZGenCS, kernelHiZReduce, groupsX, groupsY, 1);
                    }


                    //           cmd.RequestAsyncReadback(_hizDepthMap, 4, TextureFormat.RFloat, (request) => {
                    //     if (request.hasError) { Debug.LogError("Readback failed"); return; }
                    //         var tex = new Texture2D(request.width, request.height, TextureFormat.RFloat, false);
                    //         tex.LoadRawTextureData(request.GetData<float>());
                    //         tex.Apply();
                    //         File.WriteAllBytes("Assets/test_async.png", tex.EncodeToPNG());
                    //         Object.DestroyImmediate(tex);
                    // });
                    executeOnlyOnce = true;
                    cmd.EndSample("hiz occlusion");
                }





                ResolvePageRequest(ref renderingData, cmd);
                // if (!executeOnlyOnce)
                {
                    // cmd.RequestAsyncReadback(_hizDepthMap, 0, TextureFormat.RFloat, (request) => {
                    //     if (request.hasError) { Debug.LogError("Readback failed"); return; }
                    //         var tex = new Texture2D(request.width, request.height, TextureFormat.RFloat, false);
                    //         var nativeDepth = request.GetData<float>();
                    //         DataFromRenderFeature.depthData = nativeDepth;
                    //         executeOnlyOnce = true;

                    //         //Debug.LogError("sdfs");
                    // });
                }


                //test 

                BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(0,0,1024,512));
                BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(64,64,1024,512));
                BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(128,128,1024,512));
                BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(0,128,1024,512));
                BlitSpecifiedArea(cmd,settings.testTexture2d,new Rect(0,64,1024,512));

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                RenderTexture tmp = _hizDepthMap2;
                _hizDepthMap2 = _hizDepthMap;
                _hizDepthMap = tmp;
            }

            public void Dispose()
            {
                nodeBuffer?.Release();
                visibleNodeListAppendBuffer?.Release();
                visibleNodeArrayBuffer?.Release();
                visibleNodeCounterBuffer?.Release();
                patchInstanceBuffer?.Release();
                indirectArgsBuffer?.Release();
                //lodMapTexture?.Release();
                // DestroyImmediate(lodMapTexture);
                // lodMapTexture = null;

                DestroyImmediate(_lodMap);
                _lodMap = null;

                //RenderTexture.ReleaseTemporary(_hizDepthMap);
                //DestroyImmediate(_hizDepthMap);
                // _hizDepthMap = null;

                _maxLODNodeList?.Release();
                _nodeListA?.Release();
                _nodeListB?.Release();
                _indirectArgsBuffer?.Release();
                _finalNodeListBuffer?.Release();

                _culledPatchBuffer?.Release();
                _patchIndirectArgs?.Dispose();

                _patchBoundsBuffer?.Release();
                _patchBoundsIndirectArgs?.Release();
                _PackPageData?.Release();
                _uniqueBuffer?.Release();
                // DepthTexture?.Release();
                //_hizDepthMap?.Release();
                // DestroyImmediate(_hizDepthMap);
                // _hizDepthMap = null;
                // DestroyImmediate(DepthTexture);
                // DepthTexture = null;

                // TextureUtility.DestroyLODMap(_lodMap);
                // _lodMap = null;
            }
        }
    }
}