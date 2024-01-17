using AssetStudio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class ConstantBufferJ
    {
        public ConstantBufferJ() { }

        public ConstantBufferJ(string name, MatrixParameterJ[] matrices, VectorParameterJ[] vectors, StructParameterJ[] structs, int usedSize)
        {
            Name = name;
            NameIndex = -1;
            MatrixParams = matrices;
            VectorParams = vectors;
            StructParams = structs;
            Size = usedSize;
            IsPartialCB = false;
        }

        public string Name { get; set; } = string.Empty;
        public int NameIndex { get; set; }
        public MatrixParameterJ[] MatrixParams { get; set; } = Array.Empty<MatrixParameterJ>();
        public VectorParameterJ[] VectorParams { get; set; } = Array.Empty<VectorParameterJ>();
        public StructParameterJ[] StructParams { get; set; } = Array.Empty<StructParameterJ>();
        public int Size { get; set; }
        public bool IsPartialCB { get; set; }

        public NumericShaderParameterJ[] AllNumericParams
        {
            get
            {
                NumericShaderParameterJ[] shaderParams = new NumericShaderParameterJ[MatrixParams.Length + VectorParams.Length];
                MatrixParams.CopyTo(shaderParams, 0);
                VectorParams.CopyTo(shaderParams, MatrixParams.Length);
                return shaderParams;
            }
        }
    }
}
