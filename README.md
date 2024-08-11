# GPU Voxel Mesh Generation and Drawing in Unity HDRP

This project focuses on efficiently generating a cubic voxel chunk mesh using GPU. The meshing and drawing operations achieve a high performance, running at approximately 650 FPS on an RTX 4090. The mesh generation itself is even faster, exceeding 1000 FPS when the camera is disabled.

On HDRP with RTX 270 its around 300fps +- 20fps<br>
On URP with RTX 2070 its around 500fps +- 75fps,

[Watch the Mesh Generation in Action!](https://user-images.githubusercontent.com/14143603/231909731-d0047d10-7ccd-440d-8c25-6b64d07315ad.mp4)

### Overview
This example utilizes a 3D noise library to generate voxel data. Users can adjust parameters like frequency, amplitude, offset, and speed of the noise through the Unity inspector.

### Key Learning Points
- Creating a mesh on the GPU
- Utilizing Shader Graph with a custom function to draw a mesh using Graphics.DrawProceduralIndirect or Graphics.RenderPrimitivesIndirect. This functionality became achievable with Unity's recent addition of the VertexId node to Shader Graph.

### Workflow
1. **Generate Voxels Compute:** Generates voxel data (0/1) using 3D noise.
2. **Feedback Compute:** Iterates all voxels to calculate the count of vertices and indices required for the mesh.
3. **Voxelizer Compute:** Iterates all voxels to write vertex and index data into the buffers.
4. **Drawing the Mesh:** Utilizes Graphics.DrawProceduralIndirect or Graphics.RenderPrimitivesIndirect with data from index and vertex buffers.

The mesh is exclusively generated on the GPU and avoids CPU readback to create a Mesh object, which would be significantly slower (5 FPS).
### Warnings
UVs would require to write custom shader (not in Shader Graph) as Shader Graph don't allow to use Vertex ID node with fragment node, or some magic workaround to somehow use vertex id to get UVs.

### Credits
- [OpenGL Voxelizer Compute Shaders by Demurzasty](https://github.com/demurzasty/HolyGrail)
- [GPU Noise by keijiro](https://github.com/keijiro/NoiseShader)
