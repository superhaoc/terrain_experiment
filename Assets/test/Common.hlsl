
#define _SectorSize 64          //64m x 64m 

#define _WorldSizeSectors 160           // 扇区数量 (160x160)

#define _WorldSize 10240.0f        // 世界边长 10km
#define _WorldHeightSize 4096u

//一个Node拆成8x8个Patch
#define PATCH_COUNT_PER_NODE 8

#define _WorldMaxLod 5u

#define PATCH_MESH_GRID_SIZE 0.5

#define PATCH_MESH_SIZE 8
#define PATCH_MESH_GRID_COUNT 16

int _NodeCount;                    // 节点总数

int _MaxLod;                        // 最大 LOD (5)


// ------------------------------------------------------------------
// 结构化缓冲区
// ------------------------------------------------------------------
struct NodeDescription
{
   float2 center;          // 中心点 (x, z)
    float halfSize;         // 半宽
    float minHeight;        // 最小高度
    float maxHeight;        // 最大高度
    int lod;                // LOD 0~5
    int childBaseIndex;     // 子节点起始索引，-1 表示叶子
    uint heightmapAtlasIdx; // 纹理索引（暂时无用）
    uint normalmapAtlasIdx;
    uint albedoAtlasIdx;
    uint coneTexAtlasIdx;

    uint branch;
};


struct RenderPatch{
    float2 position;
    float2 minMaxHeight;
    uint lod;
    uint4 lodTrans;
};

struct Bounds{
    float3 minPosition;
    float3 maxPosition;
};


struct BoundsDebug{
    Bounds bounds;
    float4 color;
};
