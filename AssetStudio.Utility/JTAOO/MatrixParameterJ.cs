using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public sealed class MatrixParameterJ : NumericShaderParameterJ
    {
        public MatrixParameterJ() { }

        public MatrixParameterJ(string name, ShaderParamType type, int index, int rowCount, int columnCount)
        {
            Name = name;
            NameIndex = -1;
            Index = index;
            ArraySize = 0;
            Type = type;
            RowCount = (byte)rowCount;
            ColumnCount = (byte)columnCount;
            IsMatrix = true;
        }

        public MatrixParameterJ(string name, ShaderParamType type, int index, int arraySize, int rowCount, int columnCount) : this(name, type, index, rowCount, columnCount)
        {
            ArraySize = arraySize;
        }
    }
}
