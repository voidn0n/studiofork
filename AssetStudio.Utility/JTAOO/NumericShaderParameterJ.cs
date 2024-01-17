using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudioUtility
{
    public enum ShaderParamType
    {
        Float = 0x0,
        Int = 0x1,
        Bool = 0x2,
        Half = 0x3,
        Short = 0x4,
        UInt = 0x5,
        TypeCount = 0x6,
    }

    public class NumericShaderParameterJ
    {
        public string Name { get; set; }
        public int NameIndex { get; set; }
        public int Index { get; set; }
        public int ArraySize { get; set; }
        public ShaderParamType Type { get; set; }
        public byte RowCount { get; set; }
        public byte ColumnCount { get; set; }
        public bool IsMatrix { get; set; }
    }
}
