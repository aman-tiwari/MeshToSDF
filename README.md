# MeshToSDF 
Convert a mesh to an signed distance field for the VFX Graph in realtime.
See the `MeshToSDF/Demo.unity` scene to see how to use.

# How to use
1. Drag the MeshToSDF prefab into your scene.
2. Either set a mesh for the `Mesh` field or set a `SkinnedMeshRenderer` for the `Skinned Mesh Renderer` field
3. Enter play-mode and set the offset and scale such that the mesh is placed within the SDF where you want it to be. Copy these values into edit-mode
4. Outputs:
    1. **VFX graph output** - set the `Vfx Output` field to a VFX graph and the `Vfx Property` to an exposed Texture3D parameter of the VFX graph
    2. **Material output** - same as vfx graph output, but with a material. There's a `Slice Texture 3D` material in the `Editor` folder that can be used to debug the SDF. Put it on a plane and put it in to the Material output property to see a slice of the SDF.
    3. **Script output** - the SDF is available on the `outputRenderTexture` field of the component. The distance is stored in a RGBAFloat texture, in the RGB channels. Note that if you update the `offset` or `scale` or `sdfResolution` fields in a build, you also have to set `meshToSdfComponent.fieldsChanged = true`


# How it works
1. Convert the triangle mesh into voxels
  1. There are many "correct" ways to do this, for instance by iterating over the voxels that each triangle might intersect with and testing if it does intersect with any of them.
  2. But it's faster to just sample a bunch of quasi-randomly distributed points on each triangle and marking them as filled in the voxels texture, hoping we sample enough to get a good surface. I use the [R_2 sequence](http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/) for this.
   1. This would also be possible with a geometery or tesselation shader to split the triangles to be below the voxel resolution followed by marking the location of each vertex in the voxel texture as filled
2. Flood fill the voxel texture using [Jump Flood Assignment](https://blog.demofox.org/2016/02/29/fast-voronoi-diagrams-and-distance-dield-textures-on-the-gpu-with-the-jump-flooding-algorithm/). This creates a voroni diagram with each voxel acting as a seed, AKA an unsigned distance field of the voxels.
3. Subtract some constant from the unsigned distance field to thicken the surface a little bit. 

# Limitations & Improvements to make
* Currently SDFs are hollow, however the VFX treats all SDFs as hollow anyway.
* The same sample count is used for all triangles, but smaller triangles can get away with fewer samples.
  * Should benchmark to see if a dynamic length loop that samples & writes to fewer locations is faster than fixed length, unrolled by the compiler loop that samples and writes to too many.
* Since we know the vertex normals, we could write this into the voxels and have the vfx graph use the flood-filled SDF normals, rather than recomputing them per particle per timestep. However, this would require reimplementing the Conform To SDF block in the VFX graph to sample the sdf gradient, rather than computing it with a 3-tap approximation.
* Instead of each thread writing `numSamples` times into the voxel array, spawn `numTriangles * numSamples` threads and each thread writes 1 sample into the array. Maybe this is faster?
* Try the geometry shader technique. It used to be annoying to do this, but apparently is easier in HDRP.
* There's no need for the dependency on the VFX graph except for the demo scene.
*
