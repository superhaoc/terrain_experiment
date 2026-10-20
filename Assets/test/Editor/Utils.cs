using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utils
{
    public static Mesh CreateTerrainPatchMesh()
    {
        Mesh mesh = new Mesh();

        int gridCount = TerrainConsts.GridNumPerPatch; // 16
        float patchSize = TerrainConsts.PerPatchSize; // 8
        float step = patchSize / gridCount; // 0.5
        int vertexCount = (gridCount + 1) * (gridCount + 1); // 17x17

        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uv = new Vector2[vertexCount];
        int[] triangles = new int[gridCount * gridCount * 6]; // 每个格子两个三角形，每个三角形三个索引

        int index = 0;
        for (int i = 0; i <= gridCount; i++)
        {
            for (int j = 0; j <= gridCount; j++)
            {
                float x = -patchSize / 2 + i * step;
                float z = -patchSize / 2 + j * step;
                vertices[index] = new Vector3(x, 0, z);
                // UV: 从左下角(0,0)到右上角(1,1)
                uv[index] = new Vector2((float)i / gridCount, (float)j / gridCount);
                index++;
            }
        }

        const int nStride = TerrainConsts.GridNumPerPatch;
        for (int j = 0; j < triangles.Length; j += 6)
        {
            int nGrid = j / 6;
            nGrid = nGrid + nGrid / nStride;    // nGrid + nGrid / 16; 
            triangles[j] = nGrid;               
            triangles[j + 1] = nGrid + 1;         
            triangles[j + 2] = nStride + nGrid + 1;
            triangles[j + 3] = nGrid + 1;           
            triangles[j + 4] = nStride + nGrid + 2;
            triangles[j + 5] = nStride + nGrid + 1;
        }


        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetIndices(triangles, MeshTopology.Triangles, 0);
        mesh.UploadMeshData(false);
        return mesh;
    }

}
