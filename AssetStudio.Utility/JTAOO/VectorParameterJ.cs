using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class VectorParameterJ : NumericShaderParameterJ
    {
        public VectorParameterJ() { }

        public VectorParameterJ(string name, ShaderParamType type, int index, int columns)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            ArraySize = 0;
            Type = type;
            Dim = (byte)columns;
            ColumnCount = 1;
            IsMatrix = false;
        }

        public VectorParameterJ(string name, ShaderParamType type, int index, int arraySize, int columns) : this(name, type, index, columns)
        {
            ArraySize = arraySize;
        }

        public byte Dim
        {
            get { return RowCount; }
            set { RowCount = value; }
        }
    }
}
