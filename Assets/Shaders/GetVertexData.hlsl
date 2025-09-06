#ifndef UNITY_INDIRECT_DRAW_ARGS
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
#include "UnityIndirect.cginc"
#endif

#ifndef VOXEL_MESH_INFO
#define VOXEL_MESH_INFO
#define GETVERTEXDATA_HLSL

#include <Assets/Shaders/Compute/Voxels.hlsl>

StructuredBuffer<int> Indices;
StructuredBuffer<Vertex> Vertices;
 
void GetVertexData_float(float vertexId, out float3 position, out float2 texcoord, out float3 normal)
{
    const int index = Indices[round(vertexId)];
    position = Vertices[index].position;
    texcoord = Vertices[index].texcoord;
    normal = Vertices[index].normal;
}
#endif