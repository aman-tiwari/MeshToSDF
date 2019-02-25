# MeshToSDF 
Convert a mesh to an signed distance field for the VFX Graph in realtime.
See the `MeshToSDF/Demo.unity` scene to see how to use.

# Limitations & Improvements to make
* Currently SDFs are hollow, however the VFX treats all SDFs as hollow anyway.
* The same sample count is used for all triangles, but smaller triangles can get away with fewer samples.
  * Should benchmark to see if a dynamic length loop that samples & writes to fewer locations is faster than fixed length, unrolled by the compiler loop that samples and writes to too many.
* Since we know the vertex normals, we could write this into the voxels and have the vfx graph use the flood-filled SDF normals, rather than recomputing them per particle per timestep. However, this would require reimplementing the Conform To SDF block in the VFX graph to sample the sdf gradient, rather than computing it with a 3-tap approximation.
