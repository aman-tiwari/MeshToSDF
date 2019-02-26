# MeshToSDF 
Convert a mesh to an signed distance field for the VFX Graph in realtime.
See the `MeshToSDF/Demo.unity` scene to see how to use.

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
