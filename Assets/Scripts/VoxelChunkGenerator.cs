using Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public enum RenderMode
{
	DrawProceduralIndirect          = 0,
	RenderPrimitivesIndirect        = 1,
	RenderPrimitivesIndexedIndirect = 2
}

public class VoxelChunkGenerator : MonoBehaviour
{
	private static readonly int vertices       = Shader.PropertyToID("Vertices");
	private static readonly int indices        = Shader.PropertyToID("Indices");
	private static readonly int uVoxels        = Shader.PropertyToID("uVoxels");
	private static readonly int uVertices      = Shader.PropertyToID("uVertices");
	private static readonly int uIndices       = Shader.PropertyToID("uIndices");
	private static readonly int uChunkFeedback = Shader.PropertyToID("uChunkFeedback");
	private static readonly int uFeedback      = Shader.PropertyToID("uFeedback");
	private static readonly int uIndirectArgs  = Shader.PropertyToID("uIndirectArgs");
	private static readonly int uFrequency     = Shader.PropertyToID("uFrequency");
	private static readonly int uAmplitude     = Shader.PropertyToID("uAmplitude");
	private static readonly int uPosition      = Shader.PropertyToID("uPosition");

	#region Constants

	private const int CHUNK_SIZE      = 80;
	private const int CHUNK_SIZE3     = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;
	private const int WORK_GROUP_SIZE = 8;
	private const int SUB_CHUNK_SIZE  = CHUNK_SIZE / WORK_GROUP_SIZE;
	private const int VOXEL_COUNT     = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;

	#endregion

	#region Noise Settings

	[Header("Noise Settings")] public float   Frequency = 0.1f;
	public                            float   Amplitude = 1f;
	public                            Vector3 Position;
	public                            Vector3 MovementSpeed = new(2.5f, 2.5f, 2.5f);

	#endregion

	#region Compute Shaders

	[Header("Compute Shaders")] 
	[FormerlySerializedAs("generateVoxelsComputeShader")]
	public ComputeShader GenerateVoxelsComputeShader;

	[FormerlySerializedAs("voxelizerComputeShader")]
	public ComputeShader VoxelizerComputeShader;

	[FormerlySerializedAs("feedbackComputeShader")]
	public ComputeShader FeedbackComputeShader;

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
	private GraphicsBuffer                           indexedCommandBuffer;
	private GraphicsBuffer                           indexedBuffer;
	private GraphicsBuffer.IndirectDrawIndexedArgs[] indexedIndirectDrawArgs;

	#endregion

	#region Kernel Ids

	private int generateVoxelsKernelId;
	private int voxelizerKernelId;
	private int feedbackKernelId;

	#endregion

	#region Variables

	[Header("Rendering Settings")] public         RenderMode   RenderingMode = RenderMode.DrawProceduralIndirect;
	public                                        TMP_Dropdown RenderDropdown;
	[FormerlySerializedAs("drawMaterial")] public Material     DrawMaterial;

	private Camera cam;

	// for more chunks Bounds(Vector3.one * ChunkSize * ChunksAmount / 2f, Vector3.one * ChunkSize * ChunksAmount);
	private readonly Bounds          bounds = new(Vector3.one * CHUNK_SIZE / 2f, Vector3.one * CHUNK_SIZE);
	private          RenderParams    renderParams;
	private readonly ChunkFeedback[] chunkFeedback = new ChunkFeedback[1];

	#endregion

	private void Start()
	{
		cam = Camera.main;

		GetKernelsIDs();

		InitializeBuffers();

		SetupDrawProceduralIndirect();
		SetupRenderPrimitivesIndirect();
		SetupRenderPrimitivesIndexedIndirect();

		BindBuffer();

		// copy material
		DrawMaterial = new Material(DrawMaterial);
		DrawMaterial.SetBuffer(vertices, vertexBuffer);
		DrawMaterial.SetBuffer(indices, indexBuffer);

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

	private void GetKernelsIDs()
	{
		// use name of function that needs to be run
		generateVoxelsKernelId = GenerateVoxelsComputeShader.FindKernel("CSMain");
		voxelizerKernelId      = VoxelizerComputeShader.FindKernel("CSMain");
		feedbackKernelId       = FeedbackComputeShader.FindKernel("CSMain");
	}

	private unsafe void InitializeBuffers()
	{
		voxelBuffer = new ComputeBuffer(CHUNK_SIZE3, sizeof(int));

		chunkFeedbackBuffer = new ComputeBuffer(1, sizeof(ChunkFeedback));
		chunkFeedbackBuffer.SetData(new[] { new ChunkFeedback() });

		subChunkFeedbackBuffer =
			new ComputeBuffer(SUB_CHUNK_SIZE * SUB_CHUNK_SIZE * SUB_CHUNK_SIZE, sizeof(SubChunkFeedback));
		subChunkFeedbackBuffer.SetData(new SubChunkFeedback[SUB_CHUNK_SIZE * SUB_CHUNK_SIZE * SUB_CHUNK_SIZE]);

		// max voxels * 4 vertices per face * 6 faces per voxel, assuming voxels are only Cube-like
		vertexBuffer = new ComputeBuffer(VOXEL_COUNT * 4 * 6, sizeof(Vertex));
		// max voxels * 6 faces per voxel * 6 indices per face, assuming voxels are only Cube-like
		indexBuffer = new ComputeBuffer(VOXEL_COUNT * 6 * 6, sizeof(int));
	}

	private void SetupDrawProceduralIndirect()
	{
		indirectBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
		indirectBuffer.SetData(new uint[] { 0, 1, 0, 0, 0 });
	}

	private void SetupRenderPrimitivesIndirect()
	{
		commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
		                                    GraphicsBuffer.IndirectDrawArgs.size);

		indirectDrawArgs                           = new GraphicsBuffer.IndirectDrawArgs[1];
		indirectDrawArgs[0].vertexCountPerInstance = (uint)vertexBuffer.count;
		indirectDrawArgs[0].instanceCount          = 1;

		commandsBuffer.SetData(indirectDrawArgs);
	}

	private void SetupRenderPrimitivesIndexedIndirect()
	{
		indexedCommandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
		                                          GraphicsBuffer.IndirectDrawIndexedArgs.size);
		indexedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexBuffer.count, sizeof(int));

		indexedIndirectDrawArgs                          = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
		indexedIndirectDrawArgs[0].indexCountPerInstance = (uint)indexBuffer.count;
		indexedIndirectDrawArgs[0].instanceCount         = 1;
		indexedIndirectDrawArgs[0].baseVertexIndex       = 0;
		indexedIndirectDrawArgs[0].startIndex            = 0;

		indexedCommandBuffer.SetData(indexedIndirectDrawArgs);
	}

	private void BindBuffer()
	{
		// ComputeShader kernelID + Name of shader buffer to bind, + buffer
		GenerateVoxelsComputeShader.SetBuffer(generateVoxelsKernelId, uVoxels, voxelBuffer);

		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVertices, vertexBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uIndices, indexBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVoxels, voxelBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uChunkFeedback, subChunkFeedbackBuffer);

		FeedbackComputeShader.SetBuffer(feedbackKernelId, uVoxels, voxelBuffer);
		FeedbackComputeShader.SetBuffer(feedbackKernelId, uFeedback, chunkFeedbackBuffer);
		FeedbackComputeShader.SetBuffer(feedbackKernelId, uChunkFeedback, subChunkFeedbackBuffer);
		FeedbackComputeShader.SetBuffer(voxelizerKernelId, uIndirectArgs, indirectBuffer);
	}

	private void SetupRenderParams()
	{
		renderParams = new RenderParams(DrawMaterial)
		               {
			               layer                 = 0,
			               renderingLayerMask    = RenderingLayerMask.defaultRenderingLayerMask,
			               rendererPriority      = 0,
			               worldBounds           = bounds,
			               camera                = cam,
			               motionVectorMode      = MotionVectorGenerationMode.Camera,
			               reflectionProbeUsage  = ReflectionProbeUsage.Off,
			               shadowCastingMode     = ShadowCastingMode.On,
			               receiveShadows        = true,
			               lightProbeUsage       = LightProbeUsage.Off,
			               lightProbeProxyVolume = null
		               };
	}

	private void MoveNoiseOrigin()
	{
		Position += MovementSpeed * Time.deltaTime;

		GenerateVoxelsComputeShader.SetFloat(uFrequency, Frequency);
		GenerateVoxelsComputeShader.SetFloat(uAmplitude, Amplitude);
		GenerateVoxelsComputeShader.SetVector(uPosition, Position);
	}

	private void DispatchShaders()
	{
		// generate voxels
		GenerateVoxelsComputeShader.Dispatch(generateVoxelsKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
		// gather index and vertex count
		FeedbackComputeShader.Dispatch(feedbackKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
		// generate mesh
		VoxelizerComputeShader.Dispatch(voxelizerKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
	}

	private void DrawProceduralIndirect()
	{
		Graphics.DrawProceduralIndirect(DrawMaterial, bounds, MeshTopology.Triangles, indirectBuffer, camera: cam);
	}

	private void RenderPrimitivesIndirect()
	{
		Graphics.RenderPrimitivesIndirect(in renderParams, MeshTopology.Triangles, commandsBuffer);
	}

	private void RenderPrimitivesIndexedIndirect()
	{
		Graphics.RenderPrimitivesIndexedIndirect(in renderParams, MeshTopology.Triangles, indexedBuffer,
		                                         indexedCommandBuffer);
	}

	private void ChangeRenderMode(RenderMode renderMode)
	{
		switch (renderMode)
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
		chunkFeedback[0] = new ChunkFeedback();
		chunkFeedbackBuffer.SetData(chunkFeedback);
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