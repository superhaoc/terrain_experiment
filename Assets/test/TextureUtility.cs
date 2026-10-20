using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public class TextureUtility
{

    public static RenderTexture CreateRenderTextureWithMipTextures(Texture2D[] mipmaps, RenderTextureFormat format)
    {
        var mip0 = mipmaps[0];
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(mip0.width, mip0.height, format, 0, mipmaps.Length);
        descriptor.autoGenerateMips = false;
        descriptor.useMipMap = true;
        RenderTexture rt = new RenderTexture(descriptor);
        rt.filterMode = mip0.filterMode;
        rt.Create();
        for (var i = 0; i < mipmaps.Length; i++)
        {
            Graphics.CopyTexture(mipmaps[i], 0, 0, rt, 0, i);
        }
        return rt;
    }

    public static RenderTexture CreateLODMap(int size)
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(size, size, RenderTextureFormat.R8, 0, 1);
        descriptor.autoGenerateMips = false;
        descriptor.enableRandomWrite = true;
        RenderTexture rt = new RenderTexture(descriptor);
        rt.filterMode = FilterMode.Point;
        rt.Create();
        return rt;
    }
    public static RenderTexture CreateHizDepthMap(int width, int height,string name)
    {
        int hizMapSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
        int hiZMipCount =  Mathf.FloorToInt(Mathf.Log(hizMapSize, 2)) + 1;
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(hizMapSize, hizMapSize, RenderTextureFormat.RFloat, 0, hiZMipCount);

        descriptor.useMipMap = true;
        descriptor.autoGenerateMips = false;
        descriptor.enableRandomWrite = true; // 强制关闭
        
        // RenderTexture rt = RenderTexture.GetTemporary(descriptor);
        // // RenderTexture rt = new RenderTexture(descriptor);
        //  rt.filterMode = FilterMode.Point;
        //  rt.wrapMode = TextureWrapMode.Clamp;
        //  rt.Create();
        RTHandle DepthTexture = null;
          RenderingUtils.ReAllocateIfNeeded(ref DepthTexture, descriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: name);
  
        return DepthTexture;
    }


    public static void DestroyLODMap(RenderTexture rt)
    {
        if (rt == null) return;
        if (rt.IsCreated())
            rt.Release();          // 立即释放 GPU 显存
        Object.DestroyImmediate(rt);        // 销毁 Unity 对象
    }

}

