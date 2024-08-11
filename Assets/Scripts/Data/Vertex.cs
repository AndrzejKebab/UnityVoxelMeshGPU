using System.Runtime.InteropServices;
using UnityEngine;

namespace Data
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Position; // 12 bytes
        public Vector2 UV; // 8 bytes, for Tex2D array use Vector3 and use Z as texture index
        public Vector3 Normal; // 12 bytes
    }
}