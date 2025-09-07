#ifndef UNITY_INDIRECT_DRAW_ARGS
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
#include "UnityIndirect.cginc"
#endif

#include <Assets/Shaders/Compute/Voxels.hlsl>
StructuredBuffer<int> Indices;
StructuredBuffer<Vertex> Vertices;
uniform uint _StartIndex;
uniform uint _BaseVertexIndex;

void Position_float(uint vertexID, out float3 Out)
{
    //Out = Vertices[Indices[vertexID + _StartIndex] + _BaseVertexIndex];
    Out = Vertices[Indices[vertexID]].position;
}

void CommandID_IndirectInstanceCount_float(out uint CommandID, out uint IndirectInstanceCount)
{
    InitIndirectDrawArgs(0);
    CommandID = GetCommandID(0);
    IndirectInstanceCount = GetIndirectInstanceCount();
}