using AssetStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class StructParameterJ
    {
        public StructParameterJ() { }

        public StructParameterJ(string name, int index, int arraySize, int structSize, VectorParameterJ[] vectors, MatrixParameterJ[] matrices)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            ArraySize = arraySize;
            StructSize = structSize;
            VectorMembers = vectors;
            MatrixMembers = matrices;
        }

        public string Name { get; set; } = string.Empty;
        public int NameIndex { get; set; }
        public int Index { get; set; }
        public int ArraySize { get; set; }
        public int StructSize { get; set; }
        public VectorParameterJ[] VectorMembers { get; set; } = Array.Empty<VectorParameterJ>();
        public MatrixParameterJ[] MatrixMembers { get; set; } = Array.Empty<MatrixParameterJ>();

        public NumericShaderParameterJ[] AllNumericMembers
        {
            get
            {
                NumericShaderParameterJ[] shaderParams = new NumericShaderParameterJ[MatrixMembers.Length + VectorMembers.Length];
                MatrixMembers.CopyTo(shaderParams, 0);
                VectorMembers.CopyTo(shaderParams, MatrixMembers.Length);
                return shaderParams;
            }
        }
    }
}
