using UnityEngine;
using System.Runtime.InteropServices;

namespace GPUTerrain
{
    // 节点描述符（必须与 HLSL 严格对齐）
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NodeDescription
    {
        public Vector2 center;      // 节点中心 (x, z)
        public float halfSize;       // 半宽
        public float minHeight;      // 最小高度
        public float maxHeight;      // 最大高度
        public int lod;              // LOD 0~5
        public int childBaseIndex;   // 子节点起始索引，-1 表示叶子
        public uint heightmapAtlasIdx;   // 高度图纹理索引（暂时忽略）
        public uint normalmapAtlasIdx;   // 法线图纹理索引
        public uint albedoAtlasIdx;      // 反射率纹理索引
        public uint coneTexAtlasIdx;     // 法线锥纹理索引

        public uint branch;
    }

    // 补丁实例数据（用于实例化绘制）
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PatchInstance
    {
        public uint nodeIndex;       // 所属节点索引
        public uint patchX, patchY;  // 补丁在节点内的坐标 0~7
        public Vector3 worldPos;      // 补丁左下角世界坐标
        public float worldSize;       // 补丁大小
        public uint packedLOD;        // 打包的 LOD 差值
        public uint coneTexCoord;     // 法线锥纹理坐标（打包）
    }

    // 间接绘制参数
    [StructLayout(LayoutKind.Sequential)]
    public struct IndirectArgs
    {
        public uint indexCountPerInstance;
        public uint instanceCount;
        public uint startIndexLocation;
        public uint baseVertexLocation;
        public uint startInstanceLocation;
    }
}