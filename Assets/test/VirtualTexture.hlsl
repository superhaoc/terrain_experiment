#ifndef __VirtualTexture__
#define __VirtualTexture__

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define PAGE_SIZE 64        // 单位页的大小

uint2 _VirtualTextureSize;                  



struct PageTableReq{
    uint mip;
    uint2 pageCoord;
    uint2 phyPageCoord;
};







Texture2D PageTable;
SamplerState PointSampler : register(s0);   // 用于页表（最近邻，无滤波）




float tex_mip_level(float2 coord, float2 tex_size)
{
   float2 dx_scaled, dy_scaled;
   float2 coord_scaled = coord * tex_size;

   dx_scaled = ddx(coord_scaled);
   dy_scaled = ddy(coord_scaled);

   float2 dtex = dx_scaled*dx_scaled + dy_scaled*dy_scaled;
   float min_delta = max(dtex.x,dtex.y);
   float miplevel = max(0.5 * log2(min_delta), 0.0);

   return miplevel;
}

void decodePageIDMip(float4 rgba, out uint mip, out uint2 pageID){
    // 解码 mip
    mip = uint(rgba.a * 255.0 + 0.5);

    // 解码 b 通道（高位页面索引）
    uint highCode = uint(rgba.b * 255.0 + 0.5);
    uint2 pageHigh = uint2(highCode % 16, highCode / 16);  // x = 低位, y = 高位*16

    // 解码 rg 通道（低位页面索引）
    uint2 lowCode = uint2(rgba.rg * 255.0 + 0.5);
    //float2 pageLow = float2(lowCode) / 256.0;

    // 合成归一化的页面表 UV
    pageID = pageHigh * 256 + lowCode;
}

float4 encodePageTableEntry(uint mip,uint2 physCoord){
   float4 res;
    res.r = physCoord.x / 255.0;
    res.g = physCoord.y / 255.0;
    res.b = mip / 255.0;
    res.a = 1.0;
    return res;

}

void decodePageTableEntry(float4 page, out uint2 physCoord, out uint mipLevel)
{
    physCoord.x = int(floor(page.r * 255.0 + 0.5));
    physCoord.y = int(floor(page.g * 255.0 + 0.5));
    mipLevel = int(floor(page.b * 255.0 + 0.5));
}


float2 vtex_coord(float2 address,float2 page_table_size, float2 phys_size_recip)
{
   // use the top bits to get the page info; bias to get page mipmaps
   float3 page = SAMPLE_TEXTURE2D(PageTable,PointSampler, address).xyz;

   // compute a scale factor based on the mip level
   // we don't store this pre-exp'ed in case we want more than 8 levels
   // but is it slow? must test; alt would be to use 'page.b*255 + 1'
   // we don't use A, so we could use page.b * 255 + page.a * 255 * 256
   float mip2 = floor(exp2(page.b * 255.0) + 0.5);

   // extract the bits that are below the page table index
   // but! also extract some of the page table bits based on the mip level;
   // this lets us do the right thing for the mip levels automatically
   float2 vtc_to_page_unit = page_table_size / mip2;
   float2 coord_low = frac(address * vtc_to_page_unit);

   // compute the page's physical address as integers
   float2 page_pos = floor(page.rg * 255.0 + 0.5);

   // combine the integral page address with the fractional coord
   float2 final_coord = page_pos + coord_low * 60.0/64.0 + 2.0/64.0;
   // not sure why this isn't +0.5/64

   // note that we pad around on all sides, rather than just padding
   // on the right. if we pad on the right, then we can just add +0.5/64
   // so that the samples fall in exactly the right region of mip level 0,
   // but this doesn't work for mip level 1 and it starts too far to the
   // right. so we need to add at least +1/64 to keep the parent mip level
   // from sampling out too far, and then +2/64 makes things align again.
   // note that this totally hoses using S3TC: we need both mip levels
   // to start on a block boundary, but we also want to make mip level 1
   // match other surfaces, so we need mip level 1 to start on a world-aligned
   // 4x4 block, which means it must be world-aligned 8x8 at mip level 0.
   // that means we need to pad mip-level 0 with an 8x8 block on all sides,
   // so e.g. use 112x112 out of 128x128! (And I'm not sure that's even
   // workable as you recurse down... I think you have to use 64x64 out
   // of 128x128 to make everything line up top to bottom.)

   // convert back to sampling coordinates
   final_coord = final_coord * phys_size_recip;



   return final_coord;
}


#endif