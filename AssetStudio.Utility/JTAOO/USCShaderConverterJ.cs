using AssetStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShaderLabConvert;

namespace AssetStudioUtility
{
    public class USCShaderConverterJ
    {
        public DirectXCompiledShaderJ DxShader { get; set; }
        public UShaderProgram ShaderProgram { get; set; }

        public void LoadDirectXCompiledShader(Stream data)
        {
            DxShader = new DirectXCompiledShaderJ(data);
        }

        public void ConvertShaderToUShaderProgram()
        {
            if (DxShader == null)
            {
                throw new Exception("You need to call LoadDirectXCopmiledShader first!");
            }

            DirectXProgramToUSIL dx2UsilConverter = new DirectXProgramToUSIL(DxShader);
            dx2UsilConverter.Convert();

            ShaderProgram = dx2UsilConverter.shader;
        }
        public void ApplyMetadataToProgram_Frag(ShaderSubProgram subProgram)
        {
            if (ShaderProgram == null)
            {
                throw new Exception("You need to call ConvertShaderToUShaderProgram first!");
            }

            bool isVertex = false;
            bool isFragment = true;

            if (!isVertex && !isFragment)
            {
                throw new NotSupportedException("Only vertex and fragment shaders are supported at the moment!");
            }

            ShaderProgram.shaderFunctionType = isVertex ? UShaderFunctionType.Vertex : UShaderFunctionType.Fragment;

            USILOptimizerApplier.Apply(ShaderProgram, subProgram);
        }

        public void ApplyMetadataToProgram_Vertex(ShaderSubProgram subProgram)
        {
            if (ShaderProgram == null)
            {
                throw new Exception("You need to call ConvertShaderToUShaderProgram first!");
            }

            bool isVertex = true;
            bool isFragment = false;

            if (!isVertex && !isFragment)
            {
                throw new NotSupportedException("Only vertex and fragment shaders are supported at the moment!");
            }

            ShaderProgram.shaderFunctionType = isVertex ? UShaderFunctionType.Vertex : UShaderFunctionType.Fragment;

            USILOptimizerApplier.Apply(ShaderProgram, subProgram);
        }

        public string CovnertUShaderProgramToHLSL(int depth)
        {
            UShaderFunctionToHLSL hlslConverter = new UShaderFunctionToHLSL(ShaderProgram);
            return hlslConverter.Convert(depth);
        }
        
    }
}
