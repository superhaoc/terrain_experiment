using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using GPUTerrain;
using System.Collections.Generic;

public class QuadTreeTextureBaker : EditorWindow
{
    // 可配置参数
    private int worldSizeSectors = 160;
    private int sectorSize = 64;
    private int maxLod = 5;
    private ComputeShader terrainCS;
    private string savePath = "Assets/QuadTreeTexture.asset";

    // 新增：高度图输入
    private Texture2D heightmap;
    private string minMaxSavePath = "Assets/TerrainMinMaxMipmap.asset";

    [MenuItem("GPUTerrain/Bake QuadTree Texture")]
    public static void ShowWindow()
    {
        GetWindow<QuadTreeTextureBaker>("QuadTree Texture Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("QuadTree Texture Settings", EditorStyles.boldLabel);
        worldSizeSectors = EditorGUILayout.IntField("World Sectors", worldSizeSectors);
        sectorSize = EditorGUILayout.IntField("Sector Size (m)", sectorSize);
        maxLod = EditorGUILayout.IntSlider("Max LOD", maxLod, 1, 5);
        terrainCS = (ComputeShader)EditorGUILayout.ObjectField("Terrain Compute Shader", terrainCS, typeof(ComputeShader), false);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        GUILayout.Space(10);
        GUILayout.Label("MinMax Heightmap Settings", EditorStyles.boldLabel);
        heightmap = (Texture2D)EditorGUILayout.ObjectField("Heightmap (Input)", heightmap, typeof(Texture2D), false);
        minMaxSavePath = EditorGUILayout.TextField("MinMax Output Path", minMaxSavePath);

        if (GUILayout.Button("Bake QuadTree Texture"))
        {
            if (terrainCS == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a Compute Shader.", "OK");
                return;
            }
            BakeQuadTreeTexture();
        }

        if (GUILayout.Button("Bake MinMax Heightmap Mipmap"))
        {
            if (terrainCS == null || heightmap == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign Compute Shader and Heightmap texture.", "OK");
                return;
            }
            BakeMinMaxHeightmap();
        }
    }

    // 原有 QuadTree 纹理烘焙（稍作调整）
    private void BakeQuadTreeTexture()
    {
        // 1. 生成节点列表
        var nodes = TerrainQuadTreeGenerator.GenerateQuadTree(worldSizeSectors, sectorSize, maxLod, out var lodOffsets);
        int nodeCount = nodes.Length;
        Debug.Log($"Generated {nodeCount} nodes.");

        // 2. 创建节点缓冲区
        int nodeStride = Marshal.SizeOf<NodeDescription>();
        ComputeBuffer nodeBuffer = new ComputeBuffer(nodeCount, nodeStride);
        nodeBuffer.SetData(nodes);

        int baseSize = worldSizeSectors;
        RenderTexture quadTreeTex = new RenderTexture(baseSize, baseSize, 0, RenderTextureFormat.RInt);
        quadTreeTex.useMipMap = true;
        quadTreeTex.autoGenerateMips = false;
        quadTreeTex.enableRandomWrite = true;
        quadTreeTex.filterMode = FilterMode.Point;
        quadTreeTex.Create();

        // 获取内核索引（6个）
        int[] kernels = new int[6];
        for (int i = 0; i < 6; i++)
        {
            kernels[i] = terrainCS.FindKernel($"BuildQuadTreeTexture{i}");
            if (kernels[i] < 0)
            {
                EditorUtility.DisplayDialog("Error", $"Kernel BuildQuadTreeTexture{i} not found.", "OK");
                return;
            }
        }

        terrainCS.SetInt("_NodeCount", nodeCount);
        terrainCS.SetInt("_MaxLod", maxLod);
        int nodeGroups = Mathf.CeilToInt(nodeCount / 64.0f);
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "BakeQuadTree";

        for (int i = 0; i <= maxLod; i++)
        {
            cmd.SetComputeBufferParam(terrainCS, kernels[i], "_Nodes", nodeBuffer);
            cmd.SetComputeTextureParam(terrainCS, kernels[i], "_QuadTreeMip", quadTreeTex, i);
            cmd.DispatchCompute(terrainCS, kernels[i], nodeGroups, 1, 1);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        // 异步读取并保存（保持原逻辑）
        int[] mipSizes = new int[6];
        for (int i = 0; i <= maxLod; i++)
            mipSizes[i] = worldSizeSectors >> i;
        for (int i = 0; i < 6; i++) if (mipSizes[i] < 1) mipSizes[i] = 1;

        Texture2D targetTex = new Texture2D(mipSizes[0], mipSizes[0], TextureFormat.RGBA32, true, true);
        targetTex.name = "QuadTreeTexture";
        int remainingMips = 6;
        for (int mip = 0; mip < 6; mip++)
        {
            int currentMip = mip;
            AsyncGPUReadback.Request(quadTreeTex, mip, (res) =>
            {
                if (res.hasError) return;
                var data = res.GetData<uint>();
                int pixelCount = mipSizes[currentMip] * mipSizes[currentMip];
                Color32[] colors = new Color32[pixelCount];
                for (int i = 0; i < pixelCount; i++)
                {
                    uint val = data[i];
                    colors[i] = new Color32(
                        (byte)(val & 0xFF),
                        (byte)((val >> 8) & 0xFF),
                        (byte)((val >> 16) & 0xFF),
                        (byte)((val >> 24) & 0xFF)
                    );
                }
                targetTex.SetPixelData(colors, currentMip);
                remainingMips--;
                if (remainingMips == 0)
                {
                    targetTex.Apply(false, false);
                    AssetDatabase.CreateAsset(targetTex, savePath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    nodeBuffer.Release();
                    quadTreeTex.Release();
                    EditorUtility.DisplayDialog("Success", $"QuadTree Texture saved to {savePath}", "OK");
                }
            });
        }
    }


    // 在 QuadTreeTextureBaker 类中添加这两个辅助方法
    private RenderTexture CreateMinMaxHeightTexture(int texSize)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(texSize, texSize, RenderTextureFormat.RG32, 0, 1);
        desc.enableRandomWrite = true;
        desc.autoGenerateMips = false;
        var rt = RenderTexture.GetTemporary(desc);
        rt.filterMode = FilterMode.Point;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
        return rt;
    }

    public static Texture2D ConvertToTexture2D(RenderTexture renderTexture, TextureFormat format)
    {
        var original = RenderTexture.active;
        RenderTexture.active = renderTexture;
        var tex = new Texture2D(renderTexture.width, renderTexture.height, format, 0, false);
        tex.filterMode = renderTexture.filterMode;
        tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
        tex.Apply(false, false);
        RenderTexture.active = original;
        return tex;
    }


    

    // 修改后的 BakeMinMaxHeightmap 方法
    private void BakeMinMaxHeightmap()
    {
         if (terrainCS == null || heightmap == null)
    {
        EditorUtility.DisplayDialog("Error", "Terrain Compute Shader or Heightmap not assigned.", "OK");
        return;
    }

    int srcSize = 1280;
    

    int mipLevels = Mathf.FloorToInt(Mathf.Log(srcSize, 2)) + 1;
    if (mipLevels < 1) mipLevels = 1;

    RenderTexture[] mipRTs = new RenderTexture[mipLevels];

    // Kernel 0: 初始化
    int kernelInit = terrainCS.FindKernel("PatchMinMaxHeight");
    if (kernelInit < 0) { EditorUtility.DisplayDialog("Error", "Kernel PatchMinMaxHeight not found.", "OK"); return; }
    mipRTs[0] = CreateMinMaxHeightTexture(srcSize);
    terrainCS.SetTexture(kernelInit, "HeightTex", heightmap);
    terrainCS.SetTexture(kernelInit, "PatchMinMaxHeightTex", mipRTs[0]);
    int groups = Mathf.CeilToInt(srcSize / 8.0f);
    terrainCS.Dispatch(kernelInit, groups, groups, 1);


    int kernelReduce = terrainCS.FindKernel("PatchMinMaxHeightMapMip");
    if (kernelReduce < 0) { EditorUtility.DisplayDialog("Error", "Kernel PatchMinMaxHeightMapMip not found.", "OK"); return; }
    for (int level = 1; level < mipLevels; level++)
    {
        int curSize = Mathf.Max(1, srcSize >> level);
        mipRTs[level] = CreateMinMaxHeightTexture(curSize);
        terrainCS.SetTexture(kernelReduce, "InTex", mipRTs[level - 1]);
        terrainCS.SetTexture(kernelReduce, "ReduceTex", mipRTs[level]);
        int dispatchX = Mathf.CeilToInt(curSize / 8.0f);
        int dispatchY = Mathf.CeilToInt(curSize / 8.0f);
        terrainCS.Dispatch(kernelReduce, dispatchX, dispatchY, 1);
    }

    // 准备最终纹理（带 mipmap）
    Texture2D resultTex = new Texture2D(srcSize, srcSize, TextureFormat.RG32, true);
    resultTex.name = "TerrainMinMaxMipmap";
    resultTex.filterMode = FilterMode.Point;

    int remaining = mipLevels;
    bool hasError = false;

        for (int level = 0; level < mipLevels; level++)
        {
            int currentLevel = level;
            RenderTexture rt = mipRTs[currentLevel];
            // 异步读取当前 RT 的整个纹理数据（格式 RGBAHalf）
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RG32, (request) =>
            {
                if (request.hasError)
                {
                    hasError = true;
                    Debug.LogError($"Async readback failed at mip level {currentLevel}");
                    return;
                }



                var path = $"Assets/test2/test_{currentLevel}.png";
                var tex2D = ConvertToTexture2D(rt,TextureFormat.RG32);
                var bytes = tex2D.EncodeToPNG();
                System.IO.File.WriteAllBytes(path,bytes);
        
                AssetDatabase.Refresh();


                // 使用 ConvertToTexture2D 重载，从数据构造单级纹理
                Texture2D singleMip = ConvertToTexture2D(rt, TextureFormat.RG32);
                // 拷贝到最终纹理的对应 mip 级别
                Graphics.CopyTexture(singleMip, 0, 0, resultTex,  0,currentLevel);
                DestroyImmediate(singleMip);

                remaining--;
                if (remaining == 0 && !hasError)
                {
                    EditorApplication.delayCall += () =>
                    {
                        resultTex.Apply(false, false);
                        string dir = System.IO.Path.GetDirectoryName(minMaxSavePath);
                        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir);
                        AssetDatabase.CreateAsset(resultTex, minMaxSavePath);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                        // 释放临时 RT
                        for (int i = 0; i < mipLevels; i++)
                            RenderTexture.ReleaseTemporary(mipRTs[i]);
                        EditorUtility.DisplayDialog("Success", $"MinMax height mipmap saved to {minMaxSavePath}", "OK");
                    };
                }
                else if (hasError && remaining == 0)
                {
                    EditorApplication.delayCall += () =>
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to read back one or more mip levels.", "OK");
                        for (int i = 0; i < mipLevels; i++)
                            if (mipRTs[i] != null) RenderTexture.ReleaseTemporary(mipRTs[i]);
                    };
                }
            });
        }
    }
}