#ifndef UNITY_INDIRECT_DRAW_ARGS
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#endif

#include <Assets/Shaders/Compute/Voxels.hlsl>
StructuredBuffer<Vertex> Vertices;


void Position_float(uint vertexID, out float3 Out)
{
    Out = Vertices[vertexID].position;
}

void CommandID_IndirectInstanceCount_float(out uint CommandID, out uint IndirectInstanceCount)
{
    InitIndirectDrawArgs(0);
    CommandID = GetCommandID(0);
    IndirectInstanceCount = GetIndirectInstanceCount();
}