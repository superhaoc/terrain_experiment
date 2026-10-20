using UnityEngine;
using System.Collections.Generic;

namespace GPUTerrain {
// 四叉树生成器
public static class TerrainQuadTreeGenerator
{

    public static NodeDescription[] GenerateQuadTree(int worldSizeSectors, int sectorSize, int maxLod, out int[] lodOffsetsAlignment)
    {
        int rootSize = sectorSize * (1 << maxLod);
        int rootsPerSide = worldSizeSectors / (1 << maxLod);
        if (rootsPerSide * (1 << maxLod) != worldSizeSectors)
            throw new System.ArgumentException("世界扇区数必须是2^maxLod的整数倍");

        // 计算每层节点数
        int[] nodesPerLod = new int[maxLod + 1];
        int totalNodes = 0;
        int rootCount = rootsPerSide * rootsPerSide;
        for (int lod = 0; lod <= maxLod; lod++)
        {
            nodesPerLod[lod] = rootCount * (1 << (2 * lod));
            totalNodes += nodesPerLod[lod];
        }

        // 分配最终数组和 lodOffsets
        NodeDescription[] nodes = new NodeDescription[totalNodes];
        lodOffsetsAlignment = new int[(maxLod + 2) * 4];

        int[] lodOffsets = new int[maxLod + 2];
        
        // 计算每层起始索引
        int offset = 0;
        for (int lod = 0; lod <= maxLod; lod++)
        {
            lodOffsetsAlignment[lod * 4] = offset;
            lodOffsets[lod] = offset;

            offset += nodesPerLod[lod];
        }
        lodOffsetsAlignment[(maxLod + 1)*4] = totalNodes;
        lodOffsets[maxLod + 1] = totalNodes;

        // 填充每个节点
        float worldSize = worldSizeSectors * sectorSize;
        for (int lod = 0; lod <= maxLod; lod++)
        {
            int startIdx = lodOffsets[lod];
            int count = nodesPerLod[lod];
            int nodesPerSide = rootsPerSide * (1 << lod);  // 该 LOD 每行/列的节点数
            float nodeSize = rootSize / (1 << lod);        // 节点边长（米）
            float halfSize = nodeSize * 0.5f;
            float delta = nodeSize;                        // 相邻节点中心距离

            for (int i = 0; i < count; i++)
            {
                // 计算节点在网格中的行列 (col, row)
                int col = i % nodesPerSide;
                int row = i / nodesPerSide;
                // 世界中心坐标（左下角为原点）
                float centerX = (col + 0.5f) * nodeSize;
                float centerY = (row + 0.5f) * nodeSize;

                NodeDescription node = new NodeDescription
                {
                    center = new Vector2(centerX, centerY),
                    halfSize = halfSize,
                    lod = lod,
                    minHeight = 0,
                    maxHeight = 100,
                    childBaseIndex = -1,
                    branch = 0xff
                };

                // 如果不是最底层，设置子节点起始索引
                if (lod < maxLod)
                {
                    // 子节点在下一层的起始索引 = lodOffsets[lod+1] + i * 4
                    node.childBaseIndex = lodOffsets[lod + 1] + i * 4;
                }

                nodes[startIdx + i] = node;
            }
        }

        return nodes;
    }

    public static NodeDescription[] GenerateQuadTree(int worldSizeSectors, int sectorSize, int maxLod)
    {
        int worldSize = worldSizeSectors * sectorSize;       // 10240
        int rootSize = sectorSize * (1 << maxLod);           // 64 * 32 = 2048
        int rootsPerSide = 10240 / rootSize;   // 160 / 32 = 5
        if (rootsPerSide * (1 << maxLod) != worldSizeSectors)
            throw new System.ArgumentException("世界扇区数必须是2^maxLod的整数倍");

        var nodes = new List<NodeDescription>();

        // 递归函数（与原代码风格一致）
        System.Func<Vector2, float, int, int> buildNode = null;
        buildNode = (center, size, lod) =>
        {
            var node = new NodeDescription
            {
                center = center,
                halfSize = size * 0.5f,
                lod = lod,
                minHeight = 0,
                maxHeight = 100,
                childBaseIndex = -1,
                branch = 0xffff
            };

            int index = nodes.Count;
            nodes.Add(node);

            if (lod < maxLod)
            {
                float childSize = size * 0.5f;
                int childLod = lod + 1;
                float quarter = childSize * 0.5f;
                Vector2[] offsets = new Vector2[]
                {
                    new Vector2(-quarter, -quarter), // 左下
                    new Vector2( quarter, -quarter), // 右下
                    new Vector2(-quarter,  quarter), // 左上
                    new Vector2( quarter,  quarter)  // 右上
                };

                int firstChild = nodes.Count;
                foreach (var off in offsets)
                {
                    buildNode(center + off, childSize, childLod);
                }

                node.childBaseIndex = firstChild;
                nodes[index] = node;
            }

            return index;
        };

        // 遍历每个根节点（LOD0，大小2048m）
        for (int ry = 0; ry < rootsPerSide; ry++)
        {
            for (int rx = 0; rx < rootsPerSide; rx++)
            {
                // 根节点中心，以世界左下角为原点 (0,0) 到 (worldSize, worldSize)
                float centerX = (rx + 0.5f) * rootSize;
                float centerY = (ry + 0.5f) * rootSize;
                Vector2 rootCenter = new Vector2(centerX, centerY);
                buildNode(rootCenter, rootSize, 0);
            }
        }

        return nodes.ToArray();
    }
}
}