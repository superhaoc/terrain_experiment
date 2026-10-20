using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class DataFromRenderFeature : MonoBehaviour
{
public static Vector3 ScreenUVToWorldPointManual(Vector2 screenUV, float depth01, Camera cam = null)
{
    if (cam == null) cam = Camera.main;
    
    // 1. 获取 GPU 实际使用的投影矩阵和视图矩阵
    Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
    Matrix4x4 view = cam.worldToCameraMatrix;
    Matrix4x4 vp = gpuProj * view;
    Matrix4x4 vpInv = vp.inverse;
    
    // 2. 确定当前平台的 NDC 近平面和远平面对应的 Z 值
    float nearZ, farZ;
    GetPlatformNDCZRange(cam, out nearZ, out farZ);
    
    // 3. 构建 NDC 坐标
    float ndcX = screenUV.x * 2f - 1f;
    float ndcY = screenUV.y * 2f - 1f;
    
    Vector4 nearPointNDC = new Vector4(ndcX, ndcY, nearZ, 1f);
    Vector4 farPointNDC  = new Vector4(ndcX, ndcY, farZ,  1f);
    
    // 4. 变换到世界空间
    Vector4 worldNear = vpInv * nearPointNDC;
    worldNear /= worldNear.w;
    Vector4 worldFar  = vpInv * farPointNDC;
    worldFar  /= worldFar.w;
    
    // 5. 射线方向
    Vector3 rayDir = (worldFar - worldNear).normalized;
    
    // 6. 线性深度转换
    float linearEyeDepth = 0;// Depth01ToLinearEyeDepth(depth01, cam);
    
    return (Vector3)worldNear + rayDir * linearEyeDepth;
}

// 获取当前平台的 NDC 近平面和远平面 Z 值
private static void GetPlatformNDCZRange(Camera cam, out float nearZ, out float farZ)
{
    // 判断是否使用 Reversed?Z
    // Unity 中，当 SystemInfo.usesReversedZBuffer 为 true 时，深度缓冲使用 Reversed?Z
    if (SystemInfo.usesReversedZBuffer)
    {
        // 根据不同图形 API 确定 NDC 范围
        var api = SystemInfo.graphicsDeviceType;
        if (api == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
            api == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 ||
            api == UnityEngine.Rendering.GraphicsDeviceType.Vulkan)
        {
            // D3D/Vulkan: NDC Z 范围 [0,1]，Reversed 后 near=1, far=0
            nearZ = 1f;
            farZ = 0f;
        }
        else if (api == UnityEngine.Rendering.GraphicsDeviceType.Metal)
        {
            // Metal: NDC Z 范围 [-1,1]，Reversed 后 near=1, far=-1
            nearZ = 1f;
            farZ = -1f;
        }
        else // OpenGL 等其他平台通常不支持 Reversed?Z
        {
            nearZ = -1f;
            farZ = 1f;
        }
    }
    else
    {
        // 传统非 Reversed?Z
        var api = SystemInfo.graphicsDeviceType;
        if (api == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
            api == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 ||
            api == UnityEngine.Rendering.GraphicsDeviceType.Vulkan)
        {
            nearZ = 0f;
            farZ = 1f;
        }
        else
        {
            nearZ = -1f;
            farZ = 1f;
        }
    }
}
    public static NativeArray<float> depthData;


    
    // Start is called before the first frame update
    void Start()
    {
        //ScreenUVToWorldPointManual(Vector2.zero,0,Camera.main);
    }

    //UV[0,1],D[1,0]

    // Z buffer to linear view space (eye) depth.
// Does NOT correctly handle oblique view frustums.
// Does NOT work with orthographic projection.
// zBufferParam (UNITY_REVERSED_Z) = { f/n - 1,   1, (1/n - 1/f), 1/f }
// zBufferParam                    = { 1 - f/n, f/n, (1/f - 1/n), 1/n }
// float LinearEyeDepth(float depth, float4 zBufferParam)
// {
//     return 1.0 / (zBufferParam.z * depth + zBufferParam.w);
// }

    Ray GetRay(Vector3 UVD,out float eyeDepthRange)
    {
        var projReversed_Z = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false);
        var projviewInverse = (projReversed_Z * Camera.main.worldToCameraMatrix).inverse;

        float ndcx = UVD.x * 2.0f - 1.0f;
        float ndcy = UVD.y * 2.0f - 1.0f;
        float ndcz = 1.0f;

        Vector4 worldposnear = projviewInverse * new Vector4(ndcx, ndcy, ndcz,1.0f);
        worldposnear /= worldposnear.w; 

        ndcz = 0;
        Vector4 worldposfar = projviewInverse * new Vector4(ndcx, ndcy, ndcz,1.0f);
        worldposfar /= worldposfar.w; 

        Ray resultRay = new Ray();
        resultRay.direction = ((Vector3)worldposfar - (Vector3)worldposnear).normalized;
        resultRay.origin =  worldposnear;
    
        eyeDepthRange = 1.0f / ((1.0f/Camera.main.nearClipPlane - 1.0f/Camera.main.farClipPlane) * UVD.z + (1.0f/Camera.main.farClipPlane) );
        eyeDepthRange -= Camera.main.nearClipPlane;

        
        eyeDepthRange = eyeDepthRange / Mathf.Clamp(Vector3.Dot(resultRay.direction, Camera.main.transform.forward),-1,1);
    
        return resultRay;
    
    }

    // Update is called once per frame
    void Update()
    {
        float eyeDepthRange = 0.0f;
        Ray  testray = GetRay(new Vector3(0.0f, 0.0f, 

0.020413054f),out  eyeDepthRange);
        Debug.DrawLine(testray.origin,testray.origin+testray.direction*eyeDepthRange,Color.red);


        // testray = GetRay(new Vector3(1.0f, 0.0f, 0.0f),out  eyeDepthRange);
        // Debug.DrawLine(testray.origin,testray.origin+testray.direction*eyeDepthRange,Color.red);
        //  //Debug.DrawLine(testray.origin,testray.origin+Vector3.forward*range,Color.red);


         
        //  testray = GetRay(new Vector3(0.0f, 1.0f, 0.0f),out  eyeDepthRange);
        //  Debug.DrawLine(testray.origin,testray.origin+testray.direction*eyeDepthRange,Color.red);


        // testray = GetRay(new Vector3(1.0f, 1.0f, 0.0f),out  eyeDepthRange);
        // Debug.DrawLine(testray.origin,testray.origin+testray.direction*eyeDepthRange,Color.red);


        //   testray = GetRay(new Vector3(0.5f, 0.5f, 0.0f),out  eyeDepthRange);
        // Debug.DrawLine(testray.origin,testray.origin+testray.direction*eyeDepthRange,Color.red);
    }
}




