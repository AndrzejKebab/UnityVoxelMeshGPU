#ifndef UNITY_INDIRECT_DRAW_ARGS
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
#include "UnityIndirect.cginc"
#endif

#ifndef RENDER_PRIMITIVES_INDIRECT_EXAMPLE
#define RENDER_PRIMITIVES_INDIRECT_EXAMPLE

#include <Assets/Shaders/Compute/Voxels.hlsl>
StructuredBuffer<int> Indices;
StructuredBuffer<Vertex> Vertices;

StructuredBuffer<int> _Triangles;
StructuredBuffer<float3> _Positions;
uniform uint _StartIndex;
uniform uint _BaseVertexIndex;

void Position_float(uint vertexID, out float3 Out)
{
    //Out = _Positions[_Triangles[vertexID + _StartIndex] + _BaseVertexIndex];
    Out = Vertices[Indices[vertexID]].position;
}

void CommandID_IndirectInstanceCount_float(out uint CommandID, out uint IndirectInstanceCount)
{
#ifndef SHADERGRAPH_PREVIEW
    InitIndirectDrawArgs(0);
    CommandID = GetCommandID(0);
    IndirectInstanceCount = GetIndirectInstanceCount();
#else
    CommandID = 0;
    IndirectInstanceCount = 1;
#endif
}

#endif