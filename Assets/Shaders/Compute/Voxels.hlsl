#ifndef VOXELS_HLSL
#define VOXELS_HLSL

cbuffer VoxelWorldParams
{
    uint3 uChunkGrid; // e.g. (33, 33, 33) chunks
    uint3 uChunkSize; // e.g. (32, 32, 32) voxels per chunk
};

struct Vertex {
    float3 position : POSITION;
    float2 texcoord : TEXCOORD0;
    float3 normal : NORMAL;
};

struct GeometryData {
    uint vertexCount;
    uint indexCount;
};

struct ChunkFeedback {
    uint vertexOffset;
    uint vertexCount;
    uint indexOffset;
    uint indexCount;
};

uint to1D(uint3 pos) {
    uint3 wSize = uChunkGrid * uChunkSize;
    return pos.x + wSize.x * (pos.y + wSize.y * pos.z);
}

int to1D(int3 pos) {
    uint3 wSize = uChunkGrid * uChunkSize;
    return pos.x + wSize.x * (pos.y + wSize.y * pos.z);
}

uint3 to3D(uint idx) {
    uint3 wSize = uChunkGrid * uChunkSize;
    uint x = idx % wSize.x;
    uint y = (idx / wSize.x) % wSize.y;
    uint z = idx / (wSize.x * wSize.y);
    
    return uint3(x, y, z);
}

#endif