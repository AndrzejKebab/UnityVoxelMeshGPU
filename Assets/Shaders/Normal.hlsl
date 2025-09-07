#include <Assets/Shaders/Compute/Voxels.hlsl>

StructuredBuffer<int> Indices;
StructuredBuffer<Vertex> Vertices;

void Normal_float(in float vertexID, out float3 Out)
{
    #if defined(RENDER_INDEXED)
    Out = Vertices[vertexID].normal;
    #else
    Out = Vertices[Indices[vertexID]].normal;
    #endif
}