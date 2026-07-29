using Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

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

// Ensure ChunkFeedback matches the HLSL struct byte-for-byte
[StructLayout(LayoutKind.Sequential)]
public struct ChunkFeedback {
    public uint vertexOffset;
    public uint vertexCount;
    public uint indexOffset;
    public uint indexCount;
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
    
    // New Dynamic Properties
    private static readonly int uChunkGrid                    = Shader.PropertyToID("uChunkGrid");
    private static readonly int uChunkSize                    = Shader.PropertyToID("uChunkSize");
    private static readonly int uMaxVertices                  = Shader.PropertyToID("uMaxVertices");
    private static readonly int uMaxIndices                   = Shader.PropertyToID("uMaxIndices");
    #endregion

    #region Constants
    private const int WORK_GROUP_SIZE = 8;
    #endregion

    #region Grid Settings
    [Header("Grid Settings")]
    public Vector3Int ChunkGrid = new Vector3Int(33, 33, 33);
    public Vector3Int ChunkSize = new Vector3Int(32, 32, 32);
    
    [Header("Memory Limits (Prevents GPU Crash)")]
    public int MaxVertices = 5000000; // ~160 MB
    public int MaxIndices = 7500000;  // ~30 MB
    #endregion

    #region Noise Settings
    [Header("Noise Settings")] 
    public float   Frequency = 0.1f;
    public float   Amplitude = 1f;
    public Vector3 Position;
    public Vector3 MovementSpeed = new(2.5f, 2.5f, 2.5f);
    #endregion

    #region Compute Shaders
    [Header("Compute Shaders")] 
    public ComputeShader GenerateVoxelsComputeShader;
    public ComputeShader VoxelizerComputeShader;
    public ComputeShader FeedbackComputeShader;
    #endregion

    #region Buffers
    private ComputeBuffer voxelBuffer;
    private ComputeBuffer chunkFeedbackBuffer;
    private ComputeBuffer subChunkFeedbackBuffer;
    private ComputeBuffer verticesBuffer;
    private ComputeBuffer argsBuffer;
    private GraphicsBuffer indicesBuffer;

    private ComputeBuffer indirectBuffer;
    private GraphicsBuffer commandsBuffer;
    private GraphicsBuffer indexedCommandBuffer;
    private CommandBuffer asyncCommandBuffer;
    #endregion

    #region Kernel Ids
    private int generateVoxelsKernelId;
    private int voxelizerKernelId;
    private int feedbackKernelId;
    #endregion

    #region Variables
    [Header("Rendering Settings")] 
    [SerializeField] private TMP_Dropdown renderDropdown;
    [SerializeField] private TMP_Dropdown dispatchDropdown;
    [SerializeField] private Material     drawMaterial;
    
    private RenderMode   renderingMode   = RenderMode.RenderPrimitivesIndirect;
    private DispatchMode dispatchingMode = DispatchMode.DispatchIndirect;
    
    private Camera       cam;
    private Bounds       bounds;
    private RenderParams renderParams;
    private readonly ChunkFeedback[] chunkFeedback = new ChunkFeedback[1];

    private int dispatchX, dispatchY, dispatchZ;
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
        // Dropdown setup remains the same...
        renderDropdown.options.Clear();
        for (var i = 0; i <= (int)RenderMode.RenderPrimitivesIndexedIndirect; i++)
            renderDropdown.options.Add(new TMP_Dropdown.OptionData { text = ((RenderMode)i).ToString() });
        renderDropdown.value = (int)renderingMode;
        
        dispatchDropdown.options.Clear();
        for (var i = 0; i <= (int)DispatchMode.AsyncCompute; i++)
            dispatchDropdown.options.Add(new TMP_Dropdown.OptionData { text = ((DispatchMode)i).ToString() });
        dispatchDropdown.value = (int)dispatchingMode;
    }

    private void Start()
    {
        cam = Camera.main;
        GetKernelsIDs();

        CalculateDimensions();
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
        generateVoxelsKernelId = GenerateVoxelsComputeShader.FindKernel("CSMain");
        voxelizerKernelId      = VoxelizerComputeShader.FindKernel("CSMain");
        feedbackKernelId       = FeedbackComputeShader.FindKernel("CSMain");
    }

    private void CalculateDimensions()
    {
        int totalVoxelsX = ChunkGrid.x * ChunkSize.x;
        int totalVoxelsY = ChunkGrid.y * ChunkSize.y;
        int totalVoxelsZ = ChunkGrid.z * ChunkSize.z;
        
        Vector3 size = new Vector3(totalVoxelsX, totalVoxelsY, totalVoxelsZ);
        bounds = new Bounds(size / 2f, size);

        dispatchX = totalVoxelsX / WORK_GROUP_SIZE;
        dispatchY = totalVoxelsY / WORK_GROUP_SIZE;
        dispatchZ = totalVoxelsZ / WORK_GROUP_SIZE;
    }

    private unsafe void InitializeBuffers()
    {
        long totalVoxels = (long)ChunkGrid.x * ChunkSize.x * 
                           ChunkGrid.y * ChunkSize.y * 
                           ChunkGrid.z * ChunkSize.z;

        if (totalVoxels > 2147483647) {
            Debug.LogError("Voxel count exceeds Unity ComputeBuffer limits. Reduce Grid or Size.");
            return;
        }

        voxelBuffer = new ComputeBuffer((int)totalVoxels, sizeof(int));

        chunkFeedbackBuffer = new ComputeBuffer(1, sizeof(ChunkFeedback));
        chunkFeedbackBuffer.SetData(new[] { new ChunkFeedback() });

        int totalWorkGroups = dispatchX * dispatchY * dispatchZ;
        subChunkFeedbackBuffer = new ComputeBuffer(totalWorkGroups, sizeof(ChunkFeedback));
        
        // Safety cap applied here instead of allocating memory we don't have
        verticesBuffer = new ComputeBuffer(MaxVertices, 32); // Vertex struct is 32 bytes
        indicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, MaxIndices, sizeof(int));

        argsBuffer = new ComputeBuffer(1, sizeof(uint) * 3, ComputeBufferType.IndirectArguments, ComputeBufferMode.Immutable);
        argsBuffer.SetData(new uint[]{ (uint)dispatchX, (uint)dispatchY, (uint)dispatchZ });
    }

    private void SetupDrawProceduralIndirect()
    {
        indirectBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments, ComputeBufferMode.Immutable);
        indirectBuffer.SetData(new uint[] { 0, 1, 0, 0, 0 });
    }

    private void SetupRenderPrimitivesIndirect()
    {
        commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
    }

    private void SetupRenderPrimitivesIndexedIndirect()
    {
        indexedCommandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
    }

    private void SetupAsynCompute()
    {
        asyncCommandBuffer = new CommandBuffer();
        asyncCommandBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
        asyncCommandBuffer.DispatchCompute(GenerateVoxelsComputeShader, generateVoxelsKernelId, argsBuffer, 0);
        asyncCommandBuffer.DispatchCompute(FeedbackComputeShader, feedbackKernelId, argsBuffer, 0);
        asyncCommandBuffer.DispatchCompute(VoxelizerComputeShader, voxelizerKernelId, argsBuffer, 0);
    }

    private void AsyncComputeTest() { Graphics.ExecuteCommandBufferAsync(asyncCommandBuffer, ComputeQueueType.Background); }

    private void BindBuffer()
    {
        Vector4 gridParam = new Vector4(ChunkGrid.x, ChunkGrid.y, ChunkGrid.z, 0);
        Vector4 sizeParam = new Vector4(ChunkSize.x, ChunkSize.y, ChunkSize.z, 0);

        // Generate Voxels
        GenerateVoxelsComputeShader.SetVector(uChunkGrid, gridParam);
        GenerateVoxelsComputeShader.SetVector(uChunkSize, sizeParam);
        GenerateVoxelsComputeShader.SetBuffer(generateVoxelsKernelId, uVoxels, voxelBuffer);

        // Voxelizer
        VoxelizerComputeShader.SetVector(uChunkGrid, gridParam);
        VoxelizerComputeShader.SetVector(uChunkSize, sizeParam);
        VoxelizerComputeShader.SetInt(uMaxVertices, MaxVertices);
        VoxelizerComputeShader.SetInt(uMaxIndices, MaxIndices);
        VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVertices, verticesBuffer);
        VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uIndices, indicesBuffer);
        VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uVoxels, voxelBuffer);
        VoxelizerComputeShader.SetBuffer(voxelizerKernelId, uChunkFeedback, subChunkFeedbackBuffer);

        // Feedback
        FeedbackComputeShader.SetVector(uChunkGrid, gridParam);
        FeedbackComputeShader.SetVector(uChunkSize, sizeParam);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uVoxels, voxelBuffer);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uFeedback, chunkFeedbackBuffer);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uChunkFeedback, subChunkFeedbackBuffer);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uIndirectArgs, indirectBuffer);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uRenderPrimitivesIndirectArgs, commandsBuffer);
        FeedbackComputeShader.SetBuffer(feedbackKernelId, uRenderPrimitivesIndexedArgs, indexedCommandBuffer);
    }

    private void SetupRenderParams()
    {
        renderParams = new RenderParams(drawMaterial)
        {
            layer = 0,
            renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
            worldBounds = bounds,
            camera = cam,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
            matProps = new MaterialPropertyBlock()
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
        GenerateVoxelsComputeShader.Dispatch(generateVoxelsKernelId, dispatchX, dispatchY, dispatchZ);
        FeedbackComputeShader.Dispatch(feedbackKernelId, dispatchX, dispatchY, dispatchZ);
        VoxelizerComputeShader.Dispatch(voxelizerKernelId, dispatchX, dispatchY, dispatchZ);
    }

    private void DispatchIndirectShaders()
    {
        GenerateVoxelsComputeShader.DispatchIndirect(generateVoxelsKernelId, argsBuffer);
        FeedbackComputeShader.DispatchIndirect(feedbackKernelId, argsBuffer);
        VoxelizerComputeShader.DispatchIndirect(voxelizerKernelId, argsBuffer);
    }

    private void DrawProceduralIndirect() { Graphics.DrawProceduralIndirect(drawMaterial, bounds, MeshTopology.Triangles, indirectBuffer, camera: cam); }
    private void RenderPrimitivesIndirect() { Graphics.RenderPrimitivesIndirect(in renderParams, MeshTopology.Triangles, commandsBuffer); }
    private void RenderPrimitivesIndexedIndirect() { Graphics.RenderPrimitivesIndexedIndirect(in renderParams, MeshTopology.Triangles, indicesBuffer, indexedCommandBuffer); }

    private void ChangeRenderMode()
    {
        switch (renderingMode)
        {
            case RenderMode.DrawProceduralIndirect:
                DrawProceduralIndirect();
                if (drawMaterial.IsKeywordEnabled("RENDER_INDEXED")) drawMaterial.DisableKeyword("RENDER_INDEXED");
                break;
            case RenderMode.RenderPrimitivesIndirect:
                RenderPrimitivesIndirect();
                if (drawMaterial.IsKeywordEnabled("RENDER_INDEXED")) drawMaterial.DisableKeyword("RENDER_INDEXED");
                break;
            case RenderMode.RenderPrimitivesIndexedIndirect:
                if (!drawMaterial.IsKeywordEnabled("RENDER_INDEXED")) drawMaterial.EnableKeyword("RENDER_INDEXED");
                RenderPrimitivesIndexedIndirect();
                break;
        }
    }

    private void ChangeDispatchingMode()
    {
        switch (dispatchingMode)
        {
            case DispatchMode.Dispatch : DispatchShaders(); break;
            case DispatchMode.DispatchIndirect : DispatchIndirectShaders(); break;
            case DispatchMode.AsyncCompute : AsyncComputeTest(); break;
        }
    }
    
    private void SetRenderingMode(int mode) { renderingMode = (RenderMode)mode; }
    private void SetDispatchMode(int mode) { dispatchingMode = (DispatchMode)mode; }

    private void Reset()
    {
        chunkFeedback[0] = new ChunkFeedback();
        chunkFeedbackBuffer.SetData(chunkFeedback);
    }

    private void OnDestroy()
    {
        voxelBuffer?.Release();
        chunkFeedbackBuffer?.Release();
        subChunkFeedbackBuffer?.Release();
        verticesBuffer?.Release();
        indicesBuffer?.Release();
        indirectBuffer?.Release();
        argsBuffer?.Release();
        commandsBuffer?.Release();
        indexedCommandBuffer?.Release();
        asyncCommandBuffer?.Release();
    }
}