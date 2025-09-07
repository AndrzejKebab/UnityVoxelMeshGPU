#include <Assets/Shaders/Compute/Voxels.hlsl>

StructuredBuffer<int> Indices;
StructuredBuffer<Vertex> Vertices;
 
void GetVertexData_float(float vertexId, out float3 position, out float2 texcoord, out float3 normal)
{
    uint index;
    #if defined(RENDER_INDEXED)
    index = round(vertexId);
    #else
    index = Indices[round(vertexId)];
    #endif
    position = Vertices[index].position;
    texcoord = Vertices[index].texcoord;
    normal = Vertices[index].normal;
}