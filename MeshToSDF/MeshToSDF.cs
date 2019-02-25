using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.VFX;

public class MeshToSDF : MonoBehaviour
{
    public ComputeShader JFAImplementation;
    public ComputeShader MtVImplementation;

    [HideInInspector]
    public RenderTexture outputRenderTexture;

    [Tooltip("Mesh to convert to SDF. One of Mesh or Skinned Mesh Renderer must be set")]
    public Mesh mesh;

    [Tooltip("Skinned mesh renderer to bake mesh from before converting to SDF.")]
    public SkinnedMeshRenderer skinnedMeshRenderer;

    [Tooltip("Material whose property to set with the output SDF texture")]
    public Material materialOutput;
    public string materialProperty = "_Texture3D";

    [Tooltip("Visual effect whose property to set with the output SDF texture")]
    public VisualEffect vfxOutput;
    public string vfxProperty = "Texture3D";

    [Tooltip("Signed distance field resoluton")]
    public int sdfResolution = 64;

    [Tooltip("Offset the mesh before voxelization")]
    public Vector3 offset;
    [Tooltip("Scale the mesh before voxelization")]
    public float scaleBy = 1.0f;
    [Tooltip("Number of points to sample on each triangle when voxeling")]
    public uint samplesPerTriangle = 10;
    [Tooltip("Thicken the signed distance field by this amount")]
    public float postProcessThickness = 0.01f;

    // kernel ids
    int JFA;
    int Preprocess;
    int Postprocess;
    int DebugSphere;
    int MtV;
    int Zero;

    private void OnValidate() {
        JFA = JFAImplementation.FindKernel("JFA");
        Preprocess = JFAImplementation.FindKernel("Preprocess");
        Postprocess = JFAImplementation.FindKernel("Postprocess");
        DebugSphere = JFAImplementation.FindKernel("DebugSphere");

        MtV = MtVImplementation.FindKernel("MeshToVoxel");
        Zero = MtVImplementation.FindKernel("Zero");
        // set to nearest power of 2
        sdfResolution = Mathf.CeilToInt(Mathf.Pow(2, Mathf.Ceil(Mathf.Log(sdfResolution, 2))));
    }

    private void Start() {
        if(mesh == null) {
            mesh = new Mesh();
        }

        skinnedMeshRenderer = skinnedMeshRenderer ?? GetComponent<SkinnedMeshRenderer>();

    }

    private void Update() {
        float t = Time.time;

        Mesh mesh;
        if (skinnedMeshRenderer) {
            mesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(mesh);
        } else {
            mesh = this.mesh;
        }

        outputRenderTexture = MeshToVoxel(sdfResolution, mesh, offset, scaleBy, 
            samplesPerTriangle, outputRenderTexture);
    
        FloodFillToSDF(outputRenderTexture);
        
        if (materialOutput) {
            if(!materialOutput.HasProperty(materialProperty)) {
                Debug.LogError(string.Format("Material output doesn't have property {0}", materialProperty));
            } else {
                materialOutput.SetTexture(materialProperty, outputRenderTexture);
            }
        }

        if(vfxOutput) {
            if(!vfxOutput.HasTexture(vfxProperty)) {
                Debug.LogError(string.Format("Vfx Output doesn't have property {0}", vfxProperty));
            } else {
                vfxOutput.SetTexture(vfxProperty, outputRenderTexture);
            }
            
        }
    }

    private void OnDestroy() {
        if(outputRenderTexture != null) outputRenderTexture.Release();
        cachedBuffers[0]?.Dispose();
        cachedBuffers[1]?.Dispose();
    }

    public void FloodFillToSDF(RenderTexture voxels) {
        int dispatchCubeSize = voxels.width;
        JFAImplementation.SetInt("dispatchCubeSide", dispatchCubeSize);

        JFAImplementation.SetTexture(Preprocess, "Voxels", voxels);
        JFAImplementation.Dispatch(Preprocess, numGroups(voxels.width, 8),
                numGroups(voxels.height, 8), numGroups(voxels.volumeDepth, 8));

        JFAImplementation.SetTexture(JFA, "Voxels", voxels);
        for (int offset = voxels.width / 2; offset >= 1; offset /= 2) {
            JFAImplementation.SetInt("samplingOffset", offset);
            JFAImplementation.Dispatch(JFA, numGroups(voxels.width, 8),
                numGroups(voxels.height, 8), numGroups(voxels.volumeDepth, 8));
        }

        JFAImplementation.SetFloat("postProcessThickness", postProcessThickness);
        JFAImplementation.SetTexture(Postprocess, "Voxels", voxels);

        JFAImplementation.Dispatch(Postprocess, numGroups(voxels.width, 8),
            numGroups(voxels.height, 8), numGroups(voxels.volumeDepth, 8));
    }

    ComputeBuffer[] cachedBuffers = new ComputeBuffer[2];

    ComputeBuffer cachedComputeBuffer(int count, int stride, int cacheSlot) {
        cacheSlot = cacheSlot == 0 ? 0 : 1;
        var buffer = cachedBuffers[cacheSlot];
        if(buffer == null || (buffer.stride != stride || buffer.count != count)) {
            if(buffer != null) buffer.Dispose();
            buffer = new ComputeBuffer(count, stride);
            cachedBuffers[cacheSlot] = buffer;
            return buffer;
        } else {
            return buffer;
        }
    }

    public RenderTexture MeshToVoxel(int voxelResolution, Mesh mesh,
        Vector3 offset, float scaleMeshBy, uint numSamplesPerTriangle, 
        RenderTexture voxels = null) {
        var indicies = mesh.triangles;
        var numIdxes = indicies.Length;
        var numTris = numIdxes / 3;
        var indicesBuffer = cachedComputeBuffer(numIdxes, sizeof(uint), 0);
        indicesBuffer.SetData(indicies);

        var vertexBuffer = cachedComputeBuffer(mesh.vertexCount, sizeof(float) * 3, 1);
        vertexBuffer.SetData(mesh.vertices);

        MtVImplementation.SetBuffer(MtV, "IndexBuffer", indicesBuffer);
        MtVImplementation.SetBuffer(MtV, "VertexBuffer", vertexBuffer);
        MtVImplementation.SetInt("tris", numTris);
        MtVImplementation.SetFloats("offset", offset.x, offset.y, offset.z);
        MtVImplementation.SetInt("numSamples", (int)numSamplesPerTriangle);
        MtVImplementation.SetFloat("scale", scaleMeshBy);
        MtVImplementation.SetInt("voxelSide", (int)voxelResolution);

        if(voxels == null) {
            voxels = new RenderTexture(voxelResolution, voxelResolution,
                    0, RenderTextureFormat.ARGBHalf);
            voxels.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            voxels.enableRandomWrite = true;
            voxels.useMipMap = false;
            voxels.volumeDepth = voxelResolution;
            voxels.Create();
        }


        MtVImplementation.SetBuffer(Zero, "IndexBuffer", indicesBuffer);
        MtVImplementation.SetBuffer(Zero, "VertexBuffer", vertexBuffer);
        MtVImplementation.SetTexture(Zero, "Voxels", voxels);
        MtVImplementation.Dispatch(Zero, numGroups(voxelResolution, 8),
            numGroups(voxelResolution, 8), numGroups(voxelResolution, 8));

        MtVImplementation.SetTexture(MtV, "Voxels", voxels);
        MtVImplementation.Dispatch(MtV, numGroups(numTris, 512), 1, 1);

        return voxels;
    }

    RenderTexture MakeDebugTexture() {
        var voxels = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBHalf);
        voxels.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        voxels.enableRandomWrite = true;
        voxels.useMipMap = false;
        voxels.volumeDepth = 64;
        voxels.Create();
        int dispatchCubeSize = voxels.width;
        JFAImplementation.SetInt("dispatchCubeSide", dispatchCubeSize);
        JFAImplementation.SetTexture(DebugSphere, "Voxels", voxels);
        JFAImplementation.Dispatch(DebugSphere, numGroups(voxels.width, 8),
           numGroups(voxels.height, 8), numGroups(voxels.volumeDepth, 8));
        return voxels;
    }

    // number of groups for a dispatch with totalThreads and groups of size
    // numThreadsForDim
    int numGroups(int totalThreads, int groupSize) {
        return (totalThreads + (groupSize - 1)) / groupSize;
    }
}
