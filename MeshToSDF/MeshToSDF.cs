using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
            
// Creates a custom Label on the inspector for all the scripts named ScriptName
// Make sure you have a ScriptName script in your
// project, else this will not work.
[CustomEditor(typeof(MeshToSDF))]
public class MeshToSDFInspector : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();
        var m2s = target as MeshToSDF;
        if(m2s.compute == null) {
            GUILayout.Label("Error: No compute shader set. Please set the Compute property");
        } else {
            if(GUILayout.Button("Make SDF")) {
                m2s.Make();
            }
        }   
    }       
}           
            

public class MeshToSDF : MonoBehaviour
{
    [System.Serializable]
    public enum MeshToSDFMode {
        Hollow, Solid, Lines
    }

    [Tooltip("Note that this will be rounded up to be a multiple of the compute kernel thread group sizes")]
    public Vector3Int textureSize = new Vector3Int(32, 32, 32);
    public Mesh mesh;
    public MeshToSDFMode mode;
    public float scale;
    public float lineRadius = 0.05f;
    public string resultAssetName = "Assets/testSDF.asset";
    public ComputeShader compute;
    public ComputeShader slicer;
    public Texture3D debug;
    // Start is called before the first frame update
    void OnValidate() {
        if (compute == null) return;
        // round up to thread group size
        var sdfCompute = compute.FindKernel("SphereSDF");
        compute.GetKernelThreadGroupSizes(sdfCompute, out uint x, out uint y, out uint z);

        if ((textureSize.x % x) != 0) {
            textureSize.x = (1 + (textureSize.x / (int)x)) * (int)x;
        }

        if ((textureSize.y % y) != 0) {
            textureSize.y = (1 + (textureSize.y / (int)y)) * (int)y;
        }

        if ((textureSize.z % z) != 0) {
            textureSize.z = (1 + (textureSize.z / (int)z)) * (int)z;
        }
        textureSize.z = textureSize.y = textureSize.x;
    }

    Vector3 Div(Vector3 x, Vector3 y) {
        return new Vector3(x.x / y.x, x.y / y.y, x.z / y.z);
    }
    
    public void Make() {
        if(mesh == null) { MakeSphere(); return; }

        var tex = new RenderTexture(textureSize.x, textureSize.y, 0, RenderTextureFormat.RHalf);
        tex.enableRandomWrite = true;
        tex.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        tex.volumeDepth = textureSize.z;
        tex.anisoLevel = 1;
        tex.filterMode = FilterMode.Bilinear;
        tex.autoGenerateMips = false;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Create();

        var vertexBuffer = new ComputeBuffer(mesh.vertexCount, sizeof(float) * 3);
        var idxes = mesh.triangles;
        var indexBuffer = new ComputeBuffer(idxes.Length, sizeof(int));
        indexBuffer.SetData(idxes);

        var vertices = mesh.vertices;
        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        foreach(var vert in vertices) {
            bounds.Encapsulate(vert);
        }
        for(uint i = 0; i < vertices.Length; i++) {
            vertices[i] = Div(vertices[i] - bounds.center, bounds.size);
        }

        int sdfCompute = compute.FindKernel("MeshToSDFHollow");

        if (mode == MeshToSDFMode.Hollow) {
            sdfCompute = compute.FindKernel("MeshToSDFHollow");
        } else if(mode == MeshToSDFMode.Solid) {
            sdfCompute = compute.FindKernel("MeshToSDFSolid");
        } else if(mode == MeshToSDFMode.Lines) {
            sdfCompute = compute.FindKernel("MeshToSDFLines");
        }

        vertexBuffer.SetData(vertices);
        compute.SetBuffer(sdfCompute, "VertexBuffer", vertexBuffer);
        compute.SetBuffer(sdfCompute, "IndexBuffer", indexBuffer);
        compute.SetInt("tris", idxes.Length);
        compute.SetInts("dim", textureSize.x, textureSize.y, textureSize.z);
        compute.SetTexture(sdfCompute, "Result", tex);
        compute.SetFloat("scale", scale);
        compute.SetFloat("lineRadius", lineRadius);
        compute.GetKernelThreadGroupSizes(sdfCompute, out uint x, out uint y, out uint z);
        compute.Dispatch(sdfCompute, 
            textureSize.x / (int)x, textureSize.y / (int)y, textureSize.z / (int)z);

        Save(tex);
        vertexBuffer.Release();
        indexBuffer.Release();
        return;
    }

    public void MakeSphere() {
        //Create 3D Render Texture 1
        var tex = new RenderTexture(textureSize.x, textureSize.y, 0, RenderTextureFormat.RHalf);
        tex.enableRandomWrite = true;
        tex.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        tex.volumeDepth = textureSize.z;
        tex.anisoLevel = 1;
        tex.filterMode = FilterMode.Bilinear;
        tex.autoGenerateMips = false;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Create();

        var sdfCompute = compute.FindKernel("SphereSDF");
        compute.SetTexture(sdfCompute, "Result", tex);
        compute.SetInts("dim", textureSize.x, textureSize.y, textureSize.z);

        compute.GetKernelThreadGroupSizes(sdfCompute, out uint x, out uint y, out uint z);
        compute.Dispatch(sdfCompute, 
            textureSize.x / (int)x, textureSize.y / (int)y, textureSize.z / (int)z);

        Save(tex);
        return;
    }

    // following code from: 
    // https://answers.unity.com/questions/840983/how-do-i-copy-a-3d-rendertexture-isvolume-true-to.html

    RenderTexture Copy3DSliceToRenderTexture(RenderTexture source, int layer) {
        int voxelSize = textureSize.x;
        RenderTexture render = new RenderTexture(voxelSize, voxelSize, 0, RenderTextureFormat.ARGB32);
        render.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        render.enableRandomWrite = true;
        render.wrapMode = TextureWrapMode.Clamp;
        render.Create();

        int kernelIndex = slicer.FindKernel("CSMain");
        slicer.SetTexture(kernelIndex, "voxels", source);
        slicer.SetInt("layer", layer);
        slicer.SetTexture(kernelIndex, "Result", render);
        slicer.Dispatch(kernelIndex, voxelSize, voxelSize, 1);

        return render;
    }

    Texture2D ConvertFromRenderTexture(RenderTexture rt) {
        int voxelSize = textureSize.x;
        Texture2D output = new Texture2D(voxelSize, voxelSize);
        RenderTexture.active = rt;
        output.ReadPixels(new Rect(0, 0, voxelSize, voxelSize), 0, 0);
        output.Apply();
        return output;
    }

    void Save(RenderTexture selectedRenderTexture) {
        int voxelSize = textureSize.x;

        RenderTexture[] layers = new RenderTexture[voxelSize];
        for (int i = 0; i < voxelSize; i++)
            layers[i] = Copy3DSliceToRenderTexture(selectedRenderTexture, i);

        Texture2D[] finalSlices = new Texture2D[voxelSize];
        for (int i = 0; i < voxelSize; i++)
            finalSlices[i] = ConvertFromRenderTexture(layers[i]);

        Texture3D output = new Texture3D(voxelSize, voxelSize, voxelSize, TextureFormat.RHalf, true);
        output.filterMode = FilterMode.Trilinear;
        Color[] outputPixels = output.GetPixels();
        int i_flat = 0;
        for (int k = 0; k < voxelSize; k++) {
            Color[] layerPixels = finalSlices[k].GetPixels();
            for (int i = 0; i < voxelSize; i++)
                for (int j = 0; j < voxelSize; j++) {
                    var col = layerPixels[i + j * voxelSize];
                    //if (i_flat % 100 == 0) Debug.Log(col);
                    outputPixels[i + j * voxelSize + k * voxelSize * voxelSize] = col;
                    i_flat++;
                }
        }

        output.SetPixels(outputPixels);
        output.Apply();

        AssetDatabase.CreateAsset(output, resultAssetName);
    }

}