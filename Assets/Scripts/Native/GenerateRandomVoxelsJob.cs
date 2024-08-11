using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Native
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatPrecision = FloatPrecision.Low, FloatMode = FloatMode.Fast)]
    public struct GenerateRandomVoxelsJob : IJobParallelFor
    {
        [WriteOnly]
        public NativeArray<int> Voxels;

        public void Execute(int index)
        {
            Voxels[index] = Random.CreateFromIndex((uint)index).NextInt(0, 2);
        }
    }
}