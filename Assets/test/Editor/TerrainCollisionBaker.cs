// TerrainCollisionBaker.cs
// 放在 Assets/Editor 文件夹下

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GPUTerrain.Baking
{
    public class TerrainCollisionBaker : EditorWindow
    {
        [MenuItem("GPUTerrain/Bake Terrain Collision Data")]
        public static void ShowWindow()
        {
            GetWindow<TerrainCollisionBaker>("地形碰撞烘焙工具");
        }

        // 参数
        private Texture2D heightmapTexture;      // 高度图，RGBA32 或 RFloat，取 R 通道为高度
        private string outputFolder = "Assets/TerrainCollisionData";
        private int tileSize = 64;              // 每个碰撞 tile 的采样点数量（建议 64 或 128）
        private float worldWidth = 10240f;      // 世界总宽度（米）
        private float worldHeight = 10240f;     // 世界总深度（米）
        private float heightmapScale = 1f;      // 高度图值到世界高度单位的转换系数（如果高度图存的是归一化值）
        private bool generateMips = true;       // 是否生成低精度 mip 级别
        private int[] mipLevels = { 1, 2, 4, 8 }; // 降采样系数（mip0为全分辨率，mip1采样步长2，以此类推）

        private void OnGUI()
        {
            GUILayout.Label("地形碰撞数据烘焙", EditorStyles.boldLabel);
            heightmapTexture = (Texture2D)EditorGUILayout.ObjectField("高度图纹理", heightmapTexture, typeof(Texture2D), false);
            outputFolder = EditorGUILayout.TextField("输出文件夹", outputFolder);
            tileSize = EditorGUILayout.IntField("Tile 采样点尺寸", tileSize);
            worldWidth = EditorGUILayout.FloatField("世界宽度（米）", worldWidth);
            worldHeight = EditorGUILayout.FloatField("世界深度（米）", worldHeight);
            heightmapScale = EditorGUILayout.FloatField("高度缩放系数", heightmapScale);
            generateMips = EditorGUILayout.Toggle("生成 Mip 级别", generateMips);
            if (generateMips)
            {
                GUILayout.Label("降采样系数（例如1,2,4,8 表示 全分辨率, 1/2, 1/4, 1/8）", EditorStyles.miniLabel);
                string mipStr = EditorGUILayout.TextField("Mip 系数 (逗号分隔)", string.Join(",", mipLevels));
                try
                {
                    var parts = mipStr.Split(',');
                    var newList = new List<int>();
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p.Trim(), out int v) && v > 0)
                            newList.Add(v);
                    }
                    if (newList.Count > 0)
                        mipLevels = newList.ToArray();
                }
                catch { }
            }

            if (GUILayout.Button("开始烘焙"))
            {
                if (heightmapTexture == null)
                {
                    EditorUtility.DisplayDialog("错误", "请指定高度图纹理", "OK");
                    return;
                }
                BakeCollisionData();
            }
        }

        private void BakeCollisionData()
        {
            string fullPath = Path.Combine(Application.dataPath, "../" + outputFolder);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            // 读取高度图原始数据
            int texWidth = heightmapTexture.width;
            int texHeight = heightmapTexture.height;
            // 支持多种纹理格式
            Color[] pixels = heightmapTexture.GetPixels();
            float[,] heightField = new float[texWidth, texHeight];
            float minGlobal = float.MaxValue, maxGlobal = float.MinValue;
            for (int y = 0; y < texHeight; y++)
            {
                for (int x = 0; x < texWidth; x++)
                {
                    Color c = pixels[y * texWidth + x];
                    float h = c.r * heightmapScale;   // 取 R 通道，乘以缩放系数
                    heightField[x, y] = h;
                    if (h < minGlobal) minGlobal = h;
                    if (h > maxGlobal) maxGlobal = h;
                }
            }

            // 世界尺寸对应纹理分辨率
            float metersPerTexelX = worldWidth / (texWidth - 1);
            float metersPerTexelZ = worldHeight / (texHeight - 1);
            Debug.Log($"世界范围 {worldWidth}x{worldHeight}，纹理分辨率 {texWidth}x{texHeight}，每个纹素代表 {metersPerTexelX}x{metersPerTexelZ} 米");

            // 计算总 tile 数量（世界被划分为 N x M 个 tile）
            int tilesX = Mathf.CeilToInt(texWidth / (float)tileSize);
            int tilesZ = Mathf.CeilToInt(texHeight / (float)tileSize);
            int totalTiles = tilesX * tilesZ;
            Debug.Log($"生成 {tilesX} x {tilesZ} = {totalTiles} 个碰撞 Tile (每个 {tileSize}x{tileSize} 采样点)");

            // 准备存储 Tile 元信息
            List<TileMetadata> allTiles = new List<TileMetadata>();
            // 在输出文件夹中创建子目录
            string collisionDataDir = Path.Combine(fullPath, "Tiles");
            if (!Directory.Exists(collisionDataDir))
                Directory.CreateDirectory(collisionDataDir);

            // 遍历所有 tile
            for (int tileZ = 0; tileZ < tilesZ; tileZ++)
            {
                for (int tileX = 0; tileX < tilesX; tileX++)
                {
                    int startX = tileX * tileSize;
                    int startY = tileZ * tileSize;
                    int width = Mathf.Min(tileSize, texWidth - startX);
                    int height = Mathf.Min(tileSize, texHeight - startY);
                    if (width <= 0 || height <= 0) continue;

                    // 提取当前 tile 的高度数组
                    float[,] tileHeights = new float[width, height];
                    float minH = float.MaxValue, maxH = float.MinValue;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            float h = heightField[startX + x, startY + y];
                            tileHeights[x, y] = h;
                            if (h < minH) minH = h;
                            if (h > maxH) maxH = h;
                        }
                    }

                    // 生成 mip 数据（如果启用）
                    List<float[]> mipHeights = null;
                    if (generateMips)
                    {
                        mipHeights = new List<float[]>();
                        foreach (int step in mipLevels)
                        {
                            if (step == 1)
                            {
                                // mip0 就是原始分辨率，不需要额外存储（但为了方便，可以保留一份指针）
                                continue;
                            }
                            int mipWidth = Mathf.Max(1, width / step);
                            int mipHeight = Mathf.Max(1, height / step);
                            float[] mip = new float[mipWidth * mipHeight];
                            for (int my = 0; my < mipHeight; my++)
                            {
                                for (int mx = 0; mx < mipWidth; mx++)
                                {
                                    // 采样区域
                                    int sampleX0 = Mathf.Clamp(mx * step, 0, width - 1);
                                    int sampleY0 = Mathf.Clamp(my * step, 0, height - 1);
                                    int sampleX1 = Mathf.Clamp(sampleX0 + step, 0, width - 1);
                                    int sampleY1 = Mathf.Clamp(sampleY0 + step, 0, height - 1);
                                    // 简单取四个角的最大值（保守）或平均值？为了碰撞检测通常取最大值更安全（防止穿透）
                                    // 但查询远距离高度时，平均值可能更好。这里实现为取最大值（保守碰撞）。
                                    float max = tileHeights[sampleX0, sampleY0];
                                    max = Mathf.Max(max, tileHeights[sampleX1, sampleY0]);
                                    max = Mathf.Max(max, tileHeights[sampleX0, sampleY1]);
                                    max = Mathf.Max(max, tileHeights[sampleX1, sampleY1]);
                                    mip[my * mipWidth + mx] = max;
                                }
                            }
                            mipHeights.Add(mip);
                        }
                    }

                    // 序列化 tile 数据到二进制文件
                    string tileFileName = $"tile_{tileX}_{tileZ}.bytes";
                    string tileFilePath = Path.Combine(collisionDataDir, tileFileName);
                    float originX,originZ;
                    using (BinaryWriter writer = new BinaryWriter(File.Open(tileFilePath, FileMode.Create)))
                    {
                        // 写 head 信息: 版本, 宽度, 高度, minH, maxH, 世界偏移量（米）
                        writer.Write(1); // version
                        writer.Write(width);
                        writer.Write(height);
                        writer.Write(minH);
                        writer.Write(maxH);
                        // tile 在世界中的左下角坐标（米）
                        originX = startX * metersPerTexelX;
                        originZ = startY * metersPerTexelZ;
                        writer.Write(originX);
                        writer.Write(originZ);
                        writer.Write(metersPerTexelX);
                        writer.Write(metersPerTexelZ);

                        // 写入原始高度数组
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                                writer.Write(tileHeights[x, y]);

                        // 写入 mip 级别数量
                        if (generateMips)
                        {
                            writer.Write(mipLevels.Length - 1); // 减去 mip0 因为 mip0 就是原始数组
                            for (int mi = 0; mi < mipHeights.Count; mi++)
                            {
                                int step = mipLevels[mi + 1]; // mipLevels[0] = 1 跳过
                                writer.Write(step);
                                float[] mipArr = mipHeights[mi];
                                writer.Write(mipArr.Length);
                                foreach (float v in mipArr)
                                    writer.Write(v);
                            }
                        }
                        else
                        {
                            writer.Write(0);
                        }
                    }

                    // 记录元信息
                    TileMetadata meta = new TileMetadata
                    {
                        tileX = tileX,
                        tileZ = tileZ,
                        minX = originX,
                        minZ = originZ,
                        maxX = originX + width * metersPerTexelX,
                        maxZ = originZ + height * metersPerTexelZ,
                        filename = tileFileName,
                        minHeight = minH,
                        maxHeight = maxH,
                        width = width,
                        height = height
                    };
                    allTiles.Add(meta);
                }
            }

            // 生成总的索引文件
            string indexFilePath = Path.Combine(fullPath, "collision_index.json");
            string json = JsonUtility.ToJson(new CollisionIndex { tiles = allTiles.ToArray(), worldWidth = worldWidth, worldHeight = worldHeight, metersPerTexelX = metersPerTexelX, metersPerTexelZ = metersPerTexelZ, heightmapScale = heightmapScale });
            File.WriteAllText(indexFilePath, json);

            // 刷新 AssetDatabase 以便在项目中看到
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完成", $"烘焙完成！\n生成 {allTiles.Count} 个 tile，索引文件保存在:\n{indexFilePath}", "OK");
        }

        [System.Serializable]
        public class TileMetadata
        {
            public int tileX, tileZ;
            public float minX, minZ, maxX, maxZ;
            public string filename;
            public float minHeight, maxHeight;
            public int width, height;
        }

        [System.Serializable]
        public class CollisionIndex
        {
            public TileMetadata[] tiles;
            public float worldWidth, worldHeight;
            public float metersPerTexelX, metersPerTexelZ;
            public float heightmapScale;
        }
    }
}