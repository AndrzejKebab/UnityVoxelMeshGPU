StructuredBuffer<float4x4> _Matrices;

void InstanceData_float(uint instanceID, out float4x4 localToWorld)
{
    localToWorld = _Matrices[instanceID];
}