#include <Assets/Shaders/RenderPrimitivesIndirectGraphNode.hlsl>

void GetVertexUV_float(in float vertexId, out float2 Out)
{
    Out = Vertices[Indices[vertexId]].texcoord;
}