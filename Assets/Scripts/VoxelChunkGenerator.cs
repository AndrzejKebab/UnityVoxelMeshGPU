using Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public enum RenderMode
{
	DrawProceduralIndirect          = 0,
	RenderPrimitivesIndirect        = 1,
	RenderPrimitivesIndexedIndirect = 2
}

public enum DispatchMode
{
	Dispatch = 0,
	DispatchIndirect = 1,
	AsyncCompute = 2
}

public class VoxelChunkGenerator : MonoBehaviour
{
	#region Shader Properties

	private static readonly int vertices                      = Shader.PropertyToID("Vertices");
	private static readonly int indices                       = Shader.PropertyToID("Indices");
	private static readonly int uVoxels                       = Shader.PropertyToID("uVoxels");
	private static readonly int uVertices                     = Shader.PropertyToID("uVertices");
	private static readonly int uIndices                      = Shader.PropertyToID("uIndices");
	private static readonly int uChunkFeedback                = Shader.PropertyToID("uChunkFeedback");
	private static readonly int uFeedback                     = Shader.PropertyToID("uFeedback");
	private static readonly int uIndirectArgs                 = Shader.PropertyToID("uIndirectArgs");
	private static readonly int uFrequency                    = Shader.PropertyToID("uFrequency");
	private static readonly int uAmplitude                    = Shader.PropertyToID("uAmplitude");
	private static readonly int uPosition                     = Shader.PropertyToID("uPosition");
	private static readonly int uRenderPrimitivesIndirectArgs = Shader.PropertyToID("uRenderPrimitivesIndirectArgs");
	private static readonly int uRenderPrimitivesIndexedArgs  = Shader.PropertyToID("uRenderPrimitivesIndexedArgs");

	#endregion

	#region Constants

	private const int CHUNK_SIZE      = 80;
	private const int WORK_GROUP_SIZE = 8;
	private const int SUB_CHUNK_SIZE  = CHUNK_SIZE / WORK_GROUP_SIZE;
	private const int VOXEL_COUNT     = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;

	#endregion

	#region Noise Settings

	[Header("Noise Settings")] 
	public							  float   Frequency = 0.1f;
	public                            float   Amplitude = 1f;
	public                            Vector3 Position;
	public                            Vector3 MovementSpeed = new(2.5f, 2.5f, 2.5f);

	#endregion

	#region Compute Shaders

	[Header("Compute Shaders")] public ComputeShader GenerateVoxelsComputeShader;
	public                             ComputeShader VoxelizerComputeShader;
	public                             ComputeShader FeedbackComputeShader;

	#endregion

	#region Buffers

	// Shaders
	private ComputeBuffer voxelBuffer;
	private ComputeBuffer chunkFeedbackBuffer;
	private ComputeBuffer subChunkFeedbackBuffer;
	private ComputeBuffer verticesBuffer;
	private ComputeBuffer argsBuffer;
	private GraphicsBuffer indicesBuffer;

	// DrawProceduralIndirect
	private ComputeBuffer indirectBuffer;

	// RenderPrimitivesIndirect
	private GraphicsBuffer                    commandsBuffer;
	private GraphicsBuffer.IndirectDrawArgs[] indirectDrawArgs;

	// RenderPrimitivesIndexedIndirect
	private GraphicsBuffer                           indexedCommandBuffer;
	private GraphicsBuffer.IndirectDrawIndexedArgs[] indexedIndirectDrawArgs;

	// Async Compute
	private CommandBuffer asyncCommandBuffer;

	#endregion

	#region Kernel Ids

	private int generateVoxelsKernelId;
	private int voxelizerKernelId;
	private int feedbackKernelId;

	#endregion

	#region Variables

	[Header("Rendering Settings")] 
	[SerializeField] private  TMP_Dropdown renderDropdown;
	[SerializeField] private TMP_Dropdown dispatchDropdown;
	[SerializeField]     private Material     drawMaterial;
	private                                                             RenderMode   renderingMode   = RenderMode.RenderPrimitivesIndirect;
	private                                                             DispatchMode dispatchingMode = DispatchMode.DispatchIndirect;
	
	private Camera       cam;

	// for more chunks Bounds(Vector3.one * ChunkSize * ChunksAmount / 2f, Vector3.one * ChunkSize * ChunksAmount);
	private readonly Bounds          bounds = new(Vector3.one * CHUNK_SIZE / 2f, Vector3.one * CHUNK_SIZE);
	private          RenderParams    renderParams;
	private readonly ChunkFeedback[] chunkFeedback = new ChunkFeedback[1];

	#endregion

	private void OnEnable()
	{
		renderDropdown.onValueChanged.AddListener(SetRenderingMode);
		dispatchDropdown.onValueChanged.AddListener(SetDispatchMode);
	}

	private void OnDisable()
	{
		renderDropdown.onValueChanged.RemoveAllListeners();
		dispatchDropdown.onValueChanged.RemoveAllListeners();
	}

	private void Awake()
	{
		renderDropdown.options.Clear();
		for (var i = 0; i <= (int)RenderMode.RenderPrimitivesIndexedIndirect; i++)
		{
			var temp = (RenderMode)i;
			var item = new TMP_Dropdown.OptionData
			           {
				           text = temp.ToString()
			           };
			renderDropdown.options.Add(item);
		}
		renderDropdown.value = (int)renderingMode;
		
		dispatchDropdown.options.Clear();
		for (var i = 0; i <= (int)DispatchMode.AsyncCompute; i++)
		{
			var temp = (DispatchMode)i;
			var item = new TMP_Dropdown.OptionData
			           {
				           text = temp.ToString()
			           };
			dispatchDropdown.options.Add(item);
		}
		dispatchDropdown.value = (int)dispatchingMode;
	}

	private void Start()
	{
		cam = Camera.main;
		GetKernelsIDs();

		InitializeBuffers();

		SetupDrawProceduralIndirect();
		SetupRenderPrimitivesIndirect();
		SetupRenderPrimitivesIndexedIndirect();

		BindBuffer();

		drawMaterial.SetBuffer(vertices, verticesBuffer);
		drawMaterial.SetBuffer(indices, indicesBuffer);

		SetupAsynCompute();
		SetupRenderParams();
	}

	private void Update()
	{
		MoveNoiseOrigin();

		ChangeDispatchingMode();
		ChangeRenderMode();

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
		voxelBuffer = new ComputeBuffer(VOXEL_COUNT, sizeof(int));

		chunkFeedbackBuffer = new ComputeBuffer(1, sizeof(ChunkFeedback));
		chunkFeedbackBuffer.SetData(new[] { new ChunkFeedback() });

		subChunkFeedbackBuffer =
			new ComputeBuffer(SUB_CHUNK_SIZE * SUB_CHUNK_SIZE * SUB_CHUNK_SIZE, sizeof(SubChunkFeedback));
		subChunkFeedbackBuffer.SetData(new SubChunkFeedback[SUB_CHUNK_SIZE * SUB_CHUNK_SIZE * SUB_CHUNK_SIZE]);

		// max voxels * 4 vertices per face * 6 faces per voxel, assuming voxels are only Cube-like
		verticesBuffer = new ComputeBuffer(VOXEL_COUNT * 4 * 6, sizeof(Vertex));
		// max voxels * 6 faces per voxel * 6 indices per face, assuming voxels are only Cube-like
		indicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, VOXEL_COUNT * 6 * 6, sizeof(int));

		argsBuffer = new ComputeBuffer(1, sizeof(uint) * 3, ComputeBufferType.IndirectArguments, ComputeBufferMode.Immutable);
		argsBuffer.SetData(new uint[]{SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE});
	}

	private void SetupDrawProceduralIndirect()
	{
		indirectBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments, ComputeBufferMode.Immutable);
		indirectBuffer.SetData(new uint[] { 0, 1, 0, 0, 0 });
	}

	private void SetupRenderPrimitivesIndirect()
	{
		commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
		                                    GraphicsBuffer.IndirectDrawArgs.size);
	}

	private void SetupRenderPrimitivesIndexedIndirect()
	{
		indexedCommandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
		                                          GraphicsBuffer.IndirectDrawIndexedArgs.size);
	}

	private void SetupAsynCompute()
	{
		asyncCommandBuffer = new CommandBuffer();
		asyncCommandBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
		asyncCommandBuffer.DispatchCompute(GenerateVoxelsComputeShader, generateVoxelsKernelId, argsBuffer, 0);
		asyncCommandBuffer.DispatchCompute(FeedbackComputeShader, feedbackKernelId, argsBuffer, 0);
		asyncCommandBuffer.DispatchCompute(VoxelizerComputeShader, voxelizerKernelId, argsBuffer, 0);
	}

	private void AsyncComputeTest()
	{
		Graphics.ExecuteCommandBufferAsync(asyncCommandBuffer, ComputeQueueType.Urgent);
	}

	private void BindBuffer()
	{
		// ComputeShader kernelID + Name of shader buffer to bind, + buffer
		GenerateVoxelsComputeShader.SetBuffer(generateVoxelsKernelId, uVoxels, voxelBuffer);

		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVertices, verticesBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uIndices, indicesBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVoxels, voxelBuffer);
		VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uChunkFeedback, subChunkFeedbackBuffer);

		FeedbackComputeShader.SetBuffer(feedbackKernelId, uVoxels, voxelBuffer);
		FeedbackComputeShader.SetBuffer(feedbackKernelId, uFeedback, chunkFeedbackBuffer);
		FeedbackComputeShader.SetBuffer(feedbackKernelId, uChunkFeedback, subChunkFeedbackBuffer);
		FeedbackComputeShader.SetBuffer(voxelizerKernelId, uIndirectArgs, indirectBuffer);

		FeedbackComputeShader.SetBuffer(feedbackKernelId, uRenderPrimitivesIndirectArgs, commandsBuffer);
		FeedbackComputeShader.SetBuffer(feedbackKernelId, uRenderPrimitivesIndexedArgs, indexedCommandBuffer);
	}

	private void SetupRenderParams()
	{
		renderParams = new RenderParams(drawMaterial)
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
			               lightProbeProxyVolume = null,
			               matProps              = new MaterialPropertyBlock()
		               };
		renderParams.matProps.SetBuffer(vertices, verticesBuffer);
		renderParams.matProps.SetBuffer(indices, indicesBuffer);
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
		GenerateVoxelsComputeShader.Dispatch(generateVoxelsKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
		FeedbackComputeShader.Dispatch(feedbackKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
		VoxelizerComputeShader.Dispatch(voxelizerKernelId, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE, SUB_CHUNK_SIZE);
	}

	private void DispatchIndirectShaders()
	{
		GenerateVoxelsComputeShader.DispatchIndirect(generateVoxelsKernelId, argsBuffer);
		FeedbackComputeShader.DispatchIndirect(feedbackKernelId, argsBuffer);
		VoxelizerComputeShader.DispatchIndirect(voxelizerKernelId, argsBuffer);
	}

	private void DrawProceduralIndirect()
	{
		Graphics.DrawProceduralIndirect(drawMaterial, bounds, MeshTopology.Triangles, indirectBuffer, camera: cam);
	}

	private void RenderPrimitivesIndirect()
	{
		Graphics.RenderPrimitivesIndirect(in renderParams, MeshTopology.Triangles, commandsBuffer);
	}

	private void RenderPrimitivesIndexedIndirect()
	{
		Graphics.RenderPrimitivesIndexedIndirect(in renderParams, MeshTopology.Triangles, indicesBuffer,
		                                         indexedCommandBuffer);
	}

	private void ChangeRenderMode()
	{
		switch (renderingMode)
		{
			case RenderMode.DrawProceduralIndirect:
				DrawProceduralIndirect();
				if (drawMaterial.IsKeywordEnabled("RENDER_INDEXED"))
				{
					drawMaterial.DisableKeyword("RENDER_INDEXED");
				}
				break;
			case RenderMode.RenderPrimitivesIndirect:
				RenderPrimitivesIndirect();
				if (drawMaterial.IsKeywordEnabled("RENDER_INDEXED"))
				{
					drawMaterial.DisableKeyword("RENDER_INDEXED");
				}
				break;
			case RenderMode.RenderPrimitivesIndexedIndirect:
				if (!drawMaterial.IsKeywordEnabled("RENDER_INDEXED"))
				{
					drawMaterial.EnableKeyword("RENDER_INDEXED");
				}
				RenderPrimitivesIndexedIndirect();
				break;
			default:
				DrawProceduralIndirect();
				break;
		}
	}

	private void ChangeDispatchingMode()
	{
		switch (dispatchingMode)
		{
			case DispatchMode.Dispatch :
				DispatchShaders();
				break;
			case DispatchMode.DispatchIndirect :
				DispatchIndirectShaders();
				break;
			case DispatchMode.AsyncCompute :
				AsyncComputeTest();
				break;
			default:
				DispatchShaders();
				break;
		}
	}
	
	private void SetRenderingMode(int mode)
	{
		renderingMode = (RenderMode)mode;
	}

	private void SetDispatchMode(int mode)
	{
		dispatchingMode = (DispatchMode)mode;
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
		verticesBuffer.Release();
		indicesBuffer.Release();
		indirectBuffer.Release();
		argsBuffer.Release();

		commandsBuffer.Release();
		indexedCommandBuffer.Release();
		asyncCommandBuffer.Release();
	}
}