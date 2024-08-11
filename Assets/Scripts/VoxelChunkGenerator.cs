using Data;
using Native;
using TMPro;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;


public enum RenderMode
{
	DrawProceduralIndirect = 0,
	RenderPrimitivesIndirect = 1,
	RenderPrimitivesIndexedIndirect = 2
}

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VoxelChunkGenerator : MonoBehaviour
{
	private const int ChunkSize = 80;
	private const int ChunkSize3 = ChunkSize * ChunkSize * ChunkSize;
	private const int WorkGroupSize = 8;
	private const int SubChunkSize = ChunkSize / WorkGroupSize;
	private const int VoxelCount = ChunkSize * ChunkSize * ChunkSize;

	public RenderMode RenderingMode = RenderMode.DrawProceduralIndirect;
	public TMP_Dropdown dropdown;
	public float frequency = 0.1f;
	public float amplitude = 1f;
	public Vector3 position;
	public Vector3 movementSpeed = new Vector3(5, 5, 5);
	
	public ComputeShader generateVoxelsComputeShader;
	public ComputeShader voxelizerComputeShader;
	public ComputeShader feedbackComputeShader;
	public Material drawMaterial;
	private Camera _cam;

	private ComputeBuffer _voxelBuffer;
	private ComputeBuffer _chunkFeedbackBuffer;
	private ComputeBuffer _subChunkFeedbackBuffer;
	private ComputeBuffer _vertexBuffer;
	private ComputeBuffer _indexBuffer;
	private ComputeBuffer _indirectBuffer;
	private GraphicsBuffer commandsBuffer;
	private GraphicsBuffer.IndirectDrawArgs[] indirectDrawArgs;
	private GraphicsBuffer indexedCommandBuffer;
	private GraphicsBuffer indexedBuffer;
	private GraphicsBuffer.IndirectDrawIndexedArgs[] indexedIndirectDrawArgs;

	private int _generateVoxelsKernelId;
	private int _voxelizerKernelId;
	private int _feedbackKernelId;
	private Mesh _mesh;
	private MeshFilter _meshFilter;
	private Bounds bounds = new Bounds(Vector3.one * ChunkSize / 2f, Vector3.one * ChunkSize);
	RenderParams renderParams;

	private readonly ChunkFeedback[] _chunkFeedback = new ChunkFeedback[1];

	private unsafe void Start()
	{
		// get mesh filter
		_meshFilter = GetComponent<MeshFilter>();
		_cam = Camera.main;
		
		// create mesh
		_mesh = new Mesh
		{
			name = "Voxel Chunk",
			indexFormat = IndexFormat.UInt32
		};
		_meshFilter.sharedMesh = _mesh;

		// get kernel ids
		_generateVoxelsKernelId = generateVoxelsComputeShader.FindKernel("CSMain");
		_voxelizerKernelId = voxelizerComputeShader.FindKernel("CSMain");
		_feedbackKernelId = feedbackComputeShader.FindKernel("CSMain");
		
		// create buffers
		_voxelBuffer = new ComputeBuffer(ChunkSize3, sizeof(int));
		_chunkFeedbackBuffer = new ComputeBuffer(1, sizeof(ChunkFeedback));
		_chunkFeedbackBuffer.SetData(new []{ new ChunkFeedback() });
		_subChunkFeedbackBuffer = new ComputeBuffer(SubChunkSize * SubChunkSize * SubChunkSize, sizeof(SubChunkFeedback));
		_subChunkFeedbackBuffer.SetData(new SubChunkFeedback[SubChunkSize * SubChunkSize * SubChunkSize]);
		// max voxels * 4 vertices per face * 6 faces per voxel
		_vertexBuffer = new ComputeBuffer(VoxelCount * 4 * 6, sizeof(Vertex));
		// max voxels * 6 faces per voxel * 6 indices per face
		_indexBuffer = new ComputeBuffer(VoxelCount * 6 * 6, sizeof(int));
		
		_indirectBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
		_indirectBuffer.SetData(new uint[] { 0, 1, 0, 0, 0 });

		// setup for RenderPrimitivesIndirect
		indirectDrawArgs = new GraphicsBuffer.IndirectDrawArgs[1];
		commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawArgs.size);
		indirectDrawArgs[0].vertexCountPerInstance = (uint)_vertexBuffer.count;
		indirectDrawArgs[0].instanceCount = 1;
		commandsBuffer.SetData(indirectDrawArgs);

		// setup for RenderPrimitivesIndexedIndirect
		indexedCommandBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
		indexedIndirectDrawArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
		indexedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, _indexBuffer.count, sizeof(int));
		indexedIndirectDrawArgs[0].indexCountPerInstance = (uint)_indexBuffer.count;
		indexedIndirectDrawArgs[0].instanceCount = 1;
		indexedIndirectDrawArgs[0].baseVertexIndex = 0;
		indexedIndirectDrawArgs[0].startIndex = 0;
		indexedCommandBuffer.SetData(indexedIndirectDrawArgs);

		// bind buffers
		generateVoxelsComputeShader.SetBuffer(_generateVoxelsKernelId, "uVoxels", _voxelBuffer);
		
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uVertices", _vertexBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uIndices", _indexBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uVoxels", _voxelBuffer);
		voxelizerComputeShader.SetBuffer(_voxelizerKernelId, "uChunkFeedback", _subChunkFeedbackBuffer);
		
		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uVoxels", _voxelBuffer);
		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uFeedback", _chunkFeedbackBuffer);
		feedbackComputeShader.SetBuffer(_feedbackKernelId, "uChunkFeedback", _subChunkFeedbackBuffer);
		feedbackComputeShader.SetBuffer(_voxelizerKernelId, "uIndirectArgs", _indirectBuffer);

		// copy material
		drawMaterial = new Material(drawMaterial);
		drawMaterial.SetBuffer("Vertices", _vertexBuffer);
		drawMaterial.SetBuffer("Indices", _indexBuffer);

		renderParams = new(drawMaterial)
		{
			worldBounds = bounds,
			shadowCastingMode = ShadowCastingMode.On,
			receiveShadows = true,
			camera = _cam
		};
	}

	private void Update()
	{
		MoveNoiseOrigin();
		
		// generate voxels
		generateVoxelsComputeShader.Dispatch(_generateVoxelsKernelId, SubChunkSize, SubChunkSize, SubChunkSize);
		// gather index and vertex count
		feedbackComputeShader.Dispatch(_feedbackKernelId, SubChunkSize, SubChunkSize, SubChunkSize);
		// generate mesh
		voxelizerComputeShader.Dispatch(_voxelizerKernelId, SubChunkSize, SubChunkSize, SubChunkSize);

		RenderingMode = (RenderMode)dropdown.value;
		ChangeRenderMode(RenderingMode);

		Reset();
	}

	private void MoveNoiseOrigin()
	{
		position += movementSpeed * Time.deltaTime;
		
		generateVoxelsComputeShader.SetFloat("uFrequency", frequency);
		generateVoxelsComputeShader.SetFloat("uAmplitude", amplitude);
		generateVoxelsComputeShader.SetVector("uPosition", position);
	}

	private void DrawProceduralIndirect()
	{
		Graphics.DrawProceduralIndirect(drawMaterial, bounds, MeshTopology.Triangles, _indirectBuffer, camera: _cam);
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
		_chunkFeedbackBuffer.SetData(_chunkFeedback);
	}

	private void OnDestroy()
	{
		_voxelBuffer.Release();
		_chunkFeedbackBuffer.Release();
		_subChunkFeedbackBuffer.Release();
		_vertexBuffer.Release();
		_indexBuffer.Release();
		_indirectBuffer.Release();
		Destroy(_mesh);
	}

	#region Legacy examples
	
	/// <summary>
	/// Create a mesh in the usual way, read data from buffers and assign to mesh
	/// </summary>
	private void VisualiseChunkMesh()
	{
		var chunkFeedback = _chunkFeedback[0];

		// get vertices and indices
		var vertices = new Vertex[chunkFeedback.vertexCount];
		var indices = new int[chunkFeedback.indexCount];
		
		_vertexBuffer.GetData(vertices, 0, 0, (int) chunkFeedback.vertexCount);
		_indexBuffer.GetData(indices, 0, 0, (int) chunkFeedback.indexCount);
		
		// create mesh data arrays 
		var meshVertices = new Vector3[chunkFeedback.vertexCount];
		var meshUVs = new Vector2[chunkFeedback.vertexCount];
		var meshNormals = new Vector3[chunkFeedback.vertexCount];
		
		for (var i = 0; i < chunkFeedback.vertexCount; i++)
		{
			var vertex = vertices[i];
			meshVertices[i] = vertex.Position;
			meshUVs[i] = vertex.UV;
			meshNormals[i] = vertex.Normal;
		}
		
		// assign data to mesh
		_mesh.Clear();
		_mesh.vertices = meshVertices;
		_mesh.uv = meshUVs;
		_mesh.normals = meshNormals;
		_mesh.SetIndices(indices, MeshTopology.Triangles, 0);
	}
	
	/// <summary>
	/// Fill the voxel buffer with random values
	/// </summary>
	private void GenerateRandomVoxelsCpu()
	{
		var voxels = new NativeArray<int>(ChunkSize * ChunkSize * ChunkSize, Allocator.Temp);
	
		var generateRandomVoxelsJob = new GenerateRandomVoxelsJob
		{
			Voxels = voxels
		};
		
		generateRandomVoxelsJob.Schedule(voxels.Length, ChunkSize).Complete();
		
		// set buffer data
		_voxelBuffer.SetData(voxels);

		voxels.Dispose();
	}

	#endregion
}