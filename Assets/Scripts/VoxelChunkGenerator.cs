using Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public enum RenderMode
{
	DrawProceduralIndirect = 0,
	RenderPrimitivesIndirect = 1,
	RenderPrimitivesIndexedIndirect = 2
}

public class VoxelChunkGenerator : MonoBehaviour
{
	#region Constants
	private const int ChunkSize = 80;
	private const int ChunkSize3 = ChunkSize * ChunkSize * ChunkSize;
	private const int WorkGroupSize = 8;
	private const int SubChunkSize = ChunkSize / WorkGroupSize;
	private const int VoxelCount = ChunkSize * ChunkSize * ChunkSize;
	#endregion

	#region Noise Settings
	[Header("Noise Settings")]
	public float Frequency = 0.1f;
	public float Amplitude = 1f;
	public Vector3 Position;
	public Vector3 MovementSpeed = new (2.5f, 2.5f, 2.5f);
	#endregion

	#region Compute Shaders
	[Header("Compute Shaders")]
	public ComputeShader generateVoxelsComputeShader;
	public ComputeShader voxelizerComputeShader;
	public ComputeShader feedbackComputeShader;
	#endregion

	#region Buffers
	// Shaders
	private ComputeBuffer voxelBuffer;
	private ComputeBuffer chunkFeedbackBuffer;
	private ComputeBuffer subChunkFeedbackBuffer;
	private ComputeBuffer vertexBuffer;
	private ComputeBuffer indexBuffer;
	// DrawProceduralIndirect
	private ComputeBuffer indirectBuffer;
	// RenderPrimitivesIndirect
	private GraphicsBuffer commandsBuffer;
	private GraphicsBuffer.IndirectDrawArgs[] indirectDrawArgs;
	// RenderPrimitivesIndexedIndirect
	private GraphicsBuffer indexedCommandBuffer;
	private GraphicsBuffer indexedBuffer;
	private GraphicsBuffer.IndirectDrawIndexedArgs[] indexedIndirectDrawArgs;
	#endregion

	#region Kernel Ids
	private int _generateVoxelsKernelId;
	private int _voxelizerKernelId;
	private int _feedbackKernelId;
	#endregion

	#region Variables
	[Header("Rendering Settings")]
	public RenderMode RenderingMode = RenderMode.DrawProceduralIndirect;
	public TMP_Dropdown RenderDropdown;
	public Material drawMaterial;
	private Camera _cam;
	// for more chunks Bounds(Vector3.one * ChunkSize * ChunksAmount / 2f, Vector3.one * ChunkSize * ChunksAmount);
	private Bounds bounds = new Bounds(Vector3.one * ChunkSize / 2f, Vector3.one * ChunkSize);
	private RenderParams renderParams;
	private readonly ChunkFeedback[] _chunkFeedback = new ChunkFeedback[1];
	#endregion

	private unsafe void Start()
	{
		_cam = Camera.main;

		GetKernelsIDs();

		InitializeBuffers();

		SetupDrawProceduralIndirect();
		SetupRenderPrimitivesIndirect();
		SetupRenderPrimitivesIndexedIndirect();

		BindBuffer();

		// copy material
		drawMaterial = new Material(drawMaterial);
		drawMaterial.SetBuffer("Vertices", vertexBuffer);
		drawMaterial.SetBuffer("Indices", indexBuffer);

		SetupRenderParams();
	}

	private void Update()
	{
		MoveNoiseOrigin();

		DispatchShaders();

		RenderingMode = (RenderMode)RenderDropdown.value;
		ChangeRenderMode(RenderingMode);

		Reset();
	}

	private unsafe void GetKernelsIDs()
	{
		// use name of function that needs to be run
		_generateVoxelsKernelId = generateVoxelsComputeShader.FindKernel("CSMain");
		_voxelizerKernelId = voxelizerComputeShader.FindKernel("CSMain");
		_feedbackKernelId = feedbackComputeShader.FindKernel("CSMain");
	}

	private unsafe void InitializeBuffers()
	{
		voxelBuffer = new ComputeBuffer(ChunkSize3, sizeof(int));

		chunkFeedbackBuffer = new ComputeBuffer(1, sizeof(ChunkFeedback));
		chunkFeedbackBuffer.SetData(new[] { new ChunkFeedback() });

		subChunkFeedbackBuffer = new ComputeBuffer(SubChunkSize * SubChunkSize * SubChunkSize, sizeof(SubChunkFeedback));
		subChunkFeedbackBuffer.SetData(new SubChunkFeedback[SubChunkSize * SubChunkSize * SubChunkSize]);

		// max voxels * 4 vertices per face * 6 faces per voxel, assuming voxels are only Cube-like
		vertexBuffer = new ComputeBuffer(VoxelCount * 4 * 6, sizeof(Vertex));
		// max voxels * 6 faces per voxel * 6 indices per face, assuming voxels are only Cube-like
		indexBuffer = new ComputeBuffer(VoxelCount * 6 * 6, sizeof(int));
	}

	private unsafe void SetupDrawProceduralIndirect()
	{
		indirectBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
		indirectBuffer.SetData(new uint[] { 0, 1, 0, 0, 0 });
	}

	private unsafe void SetupRenderPrimitivesIndirect()
	{
		commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
		
		indirectDrawArgs = new GraphicsBuffer.IndirectDrawArgs[1];
		indirectDrawArgs[0].vertexCountPerInstance = (uint)vertexBuffer.count;
		indirectDrawArgs[0].instanceCount = 1;

		commandsBuffer.SetData(indirectDrawArgs);
	}

	private unsafe void SetupRenderPrimitivesIndexedIndirect()
	{
		indexedCommandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
		indexedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexBuffer.count, sizeof(int));

		indexedIndirectDrawArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
		indexedIndirectDrawArgs[0].indexCountPerInstance = (uint)indexBuffer.count;
		indexedIndirectDrawArgs[0].instanceCount = 1;
		indexedIndirectDrawArgs[0].baseVertexIndex = 0;
		indexedIndirectDrawArgs[0].startIndex = 0;

		indexedCommandBuffer.SetData(indexedIndirectDrawArgs);
	}

	private unsafe void BindBuffer()
	{
		// ComputeShader kernelID + Name of shader buffer to bind, + buffer
		generateVoxelsComputeShader.SetBuffer(_generateVoxelsKernelId, "uVoxels", voxelBuffer);

		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uVertices", vertexBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uIndices", indexBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uVoxels", voxelBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uChunkFeedback", subChunkFeedbackBuffer);

		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uVoxels", voxelBuffer);
		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uFeedback", chunkFeedbackBuffer);
		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uChunkFeedback", subChunkFeedbackBuffer);
		feedbackComputeShader.SetBuffer(_voxelizerKernelId, "uIndirectArgs", indirectBuffer);
	}

	private unsafe void SetupRenderParams()
	{
		renderParams = new(drawMaterial)
		{
			layer = 0,
			renderingLayerMask = GraphicsSettings.defaultRenderingLayerMask,
			rendererPriority = 0,
			worldBounds = bounds,
			camera = _cam,
			motionVectorMode = MotionVectorGenerationMode.Camera,
			reflectionProbeUsage = ReflectionProbeUsage.Off,
			shadowCastingMode = ShadowCastingMode.On,
			receiveShadows = true,
			lightProbeUsage = LightProbeUsage.Off,
			lightProbeProxyVolume = null
		};
	}
	private void MoveNoiseOrigin()
	{
		Position += MovementSpeed * Time.deltaTime;

		generateVoxelsComputeShader.SetFloat("uFrequency", Frequency);
		generateVoxelsComputeShader.SetFloat("uAmplitude", Amplitude);
		generateVoxelsComputeShader.SetVector("uPosition", Position);
	}

	private void DispatchShaders()
	{
		// generate voxels
		generateVoxelsComputeShader.Dispatch(_generateVoxelsKernelId, SubChunkSize, SubChunkSize, SubChunkSize);
		// gather index and vertex count
		feedbackComputeShader.Dispatch(_feedbackKernelId, SubChunkSize, SubChunkSize, SubChunkSize);
		// generate mesh
		voxelizerComputeShader.Dispatch(_voxelizerKernelId, SubChunkSize, SubChunkSize, SubChunkSize);
	}

	private void DrawProceduralIndirect()
	{
		Graphics.DrawProceduralIndirect(drawMaterial, bounds, MeshTopology.Triangles, indirectBuffer, camera: _cam);
	}

	private void RenderPrimitivesIndirect() 
	{
		Graphics.RenderPrimitivesIndirect(in renderParams, MeshTopology.Triangles, commandsBuffer);
	}

	private void RenderPrimitivesIndexedIndirect()
	{
		Graphics.RenderPrimitivesIndexedIndirect(in renderParams, MeshTopology.Triangles, indexedBuffer, indexedCommandBuffer);
	}

	public void ChangeRenderMode(RenderMode renderMode)
	{
		switch (RenderingMode)
		{
			case RenderMode.DrawProceduralIndirect:
				DrawProceduralIndirect();
				break;
			case RenderMode.RenderPrimitivesIndirect:
				RenderPrimitivesIndirect();
				break;
			case RenderMode.RenderPrimitivesIndexedIndirect:
				RenderPrimitivesIndexedIndirect(); 
				break;
			default:
				DrawProceduralIndirect();
				break;
		}
	}
	private void Reset()
	{
		_chunkFeedback[0] = new ChunkFeedback();
		chunkFeedbackBuffer.SetData(_chunkFeedback);
	}

	private void OnDestroy()
	{
		voxelBuffer.Release();
		chunkFeedbackBuffer.Release();
		subChunkFeedbackBuffer.Release();
		vertexBuffer.Release();
		indexBuffer.Release();
		indirectBuffer.Release();

		commandsBuffer.Release();
		indexedBuffer.Release();
		indexedCommandBuffer.Release();
	}
}