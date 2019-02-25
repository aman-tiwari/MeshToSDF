# MeshToSDF 
Convert a mesh to an signed distance field for the VFX Graph in realtime.
See the `MeshToSDF/Demo.unity` scene to see how to use.

# Limitations & Improvements to make
* Currently SDFs are hollow, however the VFX treats all SDFs as hollow anyway.
* The same sample count is used for all triangles, but smaller triangles can get away with fewer samples.
  * Should benchmark to see if a dynamic length loop that samples & writes to fewer locations is faster than fixed length, unrolled by the compiler loop that samples and writes to too many.

