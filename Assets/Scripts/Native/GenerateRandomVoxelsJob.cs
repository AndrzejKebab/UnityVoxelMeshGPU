using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Random = Unity.Mathematics.Random;

namespace Native
{
	[BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatPrecision = FloatPrecision.Low, FloatMode = FloatMode.Fast)]
	public struct GenerateRandomVoxelsJob : IJobParallelFor
	{
		[WriteOnly] // The array size mapped here is now `voxelBufferSize`
		public NativeArray<uint> Voxels;

		public void Execute(int index)
		{
			uint packedData = 0;
			var  rand       = Random.CreateFromIndex((uint)index);
            
			// Build the bit structure iteratively for the 32 voxels inside this uint
			for (int i = 0; i < 32; i++)
			{
				if (rand.NextFloat() > 0.5f) {
					packedData |= (1u << i);
				}
			}
            
			Voxels[index] = packedData;
		}
	}
}