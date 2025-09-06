#include <Assets/Shaders/RenderPrimitivesIndirectGraphNode.hlsl>

StructuredBuffer<float3> _Normals;

void Normal_float(in float vertexID, out float3 Out)
{
    //Out = _Normals[vertexID];
    Out = Vertices[Indices[vertexID]].normal;
}
