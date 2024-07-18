using SpirV;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AssetStudioUtility;
using ShaderLabConvert;

namespace AssetStudio
{
    public static class ShaderConverter
    {
        public static string Convert(this Shader shader)
        {
            if (shader.m_SubProgramBlob != null) //5.3 - 5.4
            {
                var decompressedBytes = new byte[shader.decompressedSize];
                var numWrite = LZ4.Decompress(shader.m_SubProgramBlob, decompressedBytes);
                if (numWrite != shader.decompressedSize)
                {
                    throw new IOException($"Lz4 decompression error, write {numWrite} bytes but expected {shader.decompressedSize} bytes");
                }
                using (var blobReader = new EndianBinaryReader(new MemoryStream(decompressedBytes), EndianType.LittleEndian))
                {
                    var program = new ShaderProgram(blobReader, shader);
                    program.Read(blobReader, 0);
                    return header + program.Export(Encoding.UTF8.GetString(shader.m_Script));
                }
            }

            if (shader.compressedBlob != null) //5.5 and up
            {
                return header + ConvertSerializedShader(shader);
            }

            return header + Encoding.UTF8.GetString(shader.m_Script);
        }

        private static string ConvertSerializedShader(Shader shader)
        {
            var length = shader.platforms.Length;
            var shaderPrograms = new ShaderProgram[length];
            for (var i = 0; i < length; i++)
            {
                for (var j = 0; j < shader.offsets[i].Length; j++)
                {
                    var offset = shader.offsets[i][j];
                    var compressedLength = shader.compressedLengths[i][j];
                    var decompressedLength = shader.decompressedLengths[i][j];
                    var decompressedBytes = new byte[decompressedLength];
                    if (shader.assetsFile.game.Type.IsGISubGroup())
                    {
                        Buffer.BlockCopy(shader.compressedBlob, (int)offset, decompressedBytes, 0, (int)decompressedLength);
                    }
                    else
                    {
                        var numWrite = LZ4.Decompress(shader.compressedBlob.AsSpan().Slice((int)offset, (int)compressedLength), decompressedBytes.AsSpan().Slice(0, (int)decompressedLength));
                        if (numWrite != decompressedLength)
                        {
                            throw new IOException($"Lz4 decompression error, write {numWrite} bytes but expected {decompressedLength} bytes");
                        }
                    }
                    using (var blobReader = new EndianBinaryReader(new MemoryStream(decompressedBytes), EndianType.LittleEndian))
                    {
                        if (j == 0)
                        {
                            shaderPrograms[i] = new ShaderProgram(blobReader, shader);
                        }
                        shaderPrograms[i].Read(blobReader, j);
                    }
                }
            }

            return ConvertSerializedShader(shader.m_ParsedForm, shader.platforms, shaderPrograms);
        }

        private static string ConvertSerializedShader(SerializedShader m_ParsedForm, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms)
        {
            var sb = new StringBuilder();
            sb.Append($"Shader \"{m_ParsedForm.m_Name}\" {{\n");

            sb.Append(ConvertSerializedProperties(m_ParsedForm.m_PropInfo));

            foreach (var m_SubShader in m_ParsedForm.m_SubShaders)
            {
                sb.Append(ConvertSerializedSubShader(m_SubShader, platforms, shaderPrograms));
            }

            if (!string.IsNullOrEmpty(m_ParsedForm.m_FallbackName))
            {
                sb.Append($"Fallback \"{m_ParsedForm.m_FallbackName}\"\n");
            }

            if (!string.IsNullOrEmpty(m_ParsedForm.m_CustomEditorName))
            {
                sb.Append($"CustomEditor \"{m_ParsedForm.m_CustomEditorName}\"\n");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string ConvertSerializedSubShader(SerializedSubShader m_SubShader, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms)
        {
            var sb = new StringBuilder();
            sb.Append("SubShader {\n");
            if (m_SubShader.m_LOD != 0)
            {
                sb.Append($" LOD {m_SubShader.m_LOD}\n");
            }

            sb.Append(ConvertSerializedTagMap(m_SubShader.m_Tags, 1));

            foreach (var m_Passe in m_SubShader.m_Passes)
            {
                sb.Append(ConvertSerializedPass(m_Passe, platforms, shaderPrograms));
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string ConvertSerializedPass(SerializedPass m_Passe, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms)
        {
            var sb = new StringBuilder();
            switch (m_Passe.m_Type)
            {
                case PassType.Normal:
                    sb.Append(" Pass ");
                    break;
                case PassType.Use:
                    sb.Append(" UsePass ");
                    break;
                case PassType.Grab:
                    sb.Append(" GrabPass ");
                    break;
            }
            if (m_Passe.m_Type == PassType.Use)
            {
                sb.Append($"\"{m_Passe.m_UseName}\"\n");
            }
            else
            {
                sb.Append("{\n");

                if (m_Passe.m_Type == PassType.Grab)
                {
                    if (!string.IsNullOrEmpty(m_Passe.m_TextureName))
                    {
                        sb.Append($"  \"{m_Passe.m_TextureName}\"\n");
                    }
                }
                else
                {
                    sb.Append(ConvertSerializedShaderState(m_Passe.m_State));

                    if (m_Passe.progVertex.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"vp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progVertex.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }

                    if (m_Passe.progFragment.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"fp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progFragment.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }

                    if (m_Passe.progGeometry.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"gp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progGeometry.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }

                    if (m_Passe.progHull.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"hp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progHull.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }

                    if (m_Passe.progDomain.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"dp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progDomain.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }

                    if (m_Passe.progRayTracing?.m_SubPrograms.Count > 0)
                    {
                        sb.Append("Program \"rtp\" {\n");
                        sb.Append(ConvertSerializedSubPrograms(m_Passe.progRayTracing.m_SubPrograms, platforms, shaderPrograms));
                        sb.Append("}\n");
                    }
                }
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        private static string ConvertSerializedSubPrograms(List<SerializedSubProgram> m_SubPrograms, ShaderCompilerPlatform[] platforms, ShaderProgram[] shaderPrograms)
        {
            var sb = new StringBuilder();
            var groups = m_SubPrograms.GroupBy(x => x.m_BlobIndex);
            foreach (var group in groups)
            {
                var programs = group.GroupBy(x => x.m_GpuProgramType);
                foreach (var program in programs)
                {
                    for (int i = 0; i < platforms.Length; i++)
                    {
                        var platform = platforms[i];
                        if (CheckGpuProgramUsable(platform, program.Key))
                        {
                            var subPrograms = program.ToList();
                            var isTier = subPrograms.Count > 1;
                            foreach (var subProgram in subPrograms)
                            {
                                sb.Append($"SubProgram \"{GetPlatformString(platform)} ");
                                if (isTier)
                                {
                                    sb.Append($"hw_tier{subProgram.m_ShaderHardwareTier:00} ");
                                }
                                sb.Append("\" {\n");
                                sb.Append(shaderPrograms[i].m_SubPrograms[subProgram.m_BlobIndex].Export());
                                sb.Append("\n}\n");
                            }
                            break;
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private static string ConvertSerializedShaderState(SerializedShaderState m_State)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(m_State.m_Name))
            {
                sb.Append($"  Name \"{m_State.m_Name}\"\n");
            }
            if (m_State.m_LOD != 0)
            {
                sb.Append($"  LOD {m_State.m_LOD}\n");
            }

            sb.Append(ConvertSerializedTagMap(m_State.m_Tags, 2));

            sb.Append(ConvertSerializedShaderRTBlendState(m_State.rtBlend, m_State.rtSeparateBlend));

            if (m_State.alphaToMask.val > 0f)
            {
                sb.Append("  AlphaToMask On\n");
            }

            if (m_State.zClip?.val != 1f) //ZClip On
            {
                sb.Append("  ZClip Off\n");
            }

            if (m_State.zTest.val != 4f) //ZTest LEqual
            {
                sb.Append("  ZTest ");
                switch (m_State.zTest.val) //enum CompareFunction
                {
                    case 0f: //kFuncDisabled
                        sb.Append("Off");
                        break;
                    case 1f: //kFuncNever
                        sb.Append("Never");
                        break;
                    case 2f: //kFuncLess
                        sb.Append("Less");
                        break;
                    case 3f: //kFuncEqual
                        sb.Append("Equal");
                        break;
                    case 5f: //kFuncGreater
                        sb.Append("Greater");
                        break;
                    case 6f: //kFuncNotEqual
                        sb.Append("NotEqual");
                        break;
                    case 7f: //kFuncGEqual
                        sb.Append("GEqual");
                        break;
                    case 8f: //kFuncAlways
                        sb.Append("Always");
                        break;
                }

                sb.Append("\n");
            }

            if (m_State.zWrite.val != 1f) //ZWrite On
            {
                sb.Append("  ZWrite Off\n");
            }

            if (m_State.culling.val != 2f) //Cull Back
            {
                sb.Append("  Cull ");
                switch (m_State.culling.val) //enum CullMode
                {
                    case 0f: //kCullOff
                        sb.Append("Off");
                        break;
                    case 1f: //kCullFront
                        sb.Append("Front");
                        break;
                }
                sb.Append("\n");
            }

            if (m_State.offsetFactor.val != 0f || m_State.offsetUnits.val != 0f)
            {
                sb.Append($"  Offset {m_State.offsetFactor.val}, {m_State.offsetUnits.val}\n");
            }

            if (m_State.stencilRef.val != 0f ||
                m_State.stencilReadMask.val != 255f ||
                m_State.stencilWriteMask.val != 255f ||
                m_State.stencilOp.pass.val != 0f ||
                m_State.stencilOp.fail.val != 0f ||
                m_State.stencilOp.zFail.val != 0f ||
                m_State.stencilOp.comp.val != 8f ||
                m_State.stencilOpFront.pass.val != 0f ||
                m_State.stencilOpFront.fail.val != 0f ||
                m_State.stencilOpFront.zFail.val != 0f ||
                m_State.stencilOpFront.comp.val != 8f ||
                m_State.stencilOpBack.pass.val != 0f ||
                m_State.stencilOpBack.fail.val != 0f ||
                m_State.stencilOpBack.zFail.val != 0f ||
                m_State.stencilOpBack.comp.val != 8f)
            {
                sb.Append("  Stencil {\n");
                if (m_State.stencilRef.val != 0f)
                {
                    sb.Append($"   Ref {m_State.stencilRef.val}\n");
                }
                if (m_State.stencilReadMask.val != 255f)
                {
                    sb.Append($"   ReadMask {m_State.stencilReadMask.val}\n");
                }
                if (m_State.stencilWriteMask.val != 255f)
                {
                    sb.Append($"   WriteMask {m_State.stencilWriteMask.val}\n");
                }
                if (m_State.stencilOp.pass.val != 0f ||
                    m_State.stencilOp.fail.val != 0f ||
                    m_State.stencilOp.zFail.val != 0f ||
                    m_State.stencilOp.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOp, ""));
                }
                if (m_State.stencilOpFront.pass.val != 0f ||
                    m_State.stencilOpFront.fail.val != 0f ||
                    m_State.stencilOpFront.zFail.val != 0f ||
                    m_State.stencilOpFront.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOpFront, "Front"));
                }
                if (m_State.stencilOpBack.pass.val != 0f ||
                    m_State.stencilOpBack.fail.val != 0f ||
                    m_State.stencilOpBack.zFail.val != 0f ||
                    m_State.stencilOpBack.comp.val != 8f)
                {
                    sb.Append(ConvertSerializedStencilOp(m_State.stencilOpBack, "Back"));
                }
                sb.Append("  }\n");
            }

            if (m_State.fogMode != FogMode.Unknown ||
                m_State.fogColor.x.val != 0f ||
                m_State.fogColor.y.val != 0f ||
                m_State.fogColor.z.val != 0f ||
                m_State.fogColor.w.val != 0f ||
                m_State.fogDensity.val != 0f ||
                m_State.fogStart.val != 0f ||
                m_State.fogEnd.val != 0f)
            {
                sb.Append("  Fog {\n");
                if (m_State.fogMode != FogMode.Unknown)
                {
                    sb.Append("   Mode ");
                    switch (m_State.fogMode)
                    {
                        case FogMode.Disabled:
                            sb.Append("Off");
                            break;
                        case FogMode.Linear:
                            sb.Append("Linear");
                            break;
                        case FogMode.Exp:
                            sb.Append("Exp");
                            break;
                        case FogMode.Exp2:
                            sb.Append("Exp2");
                            break;
                    }
                    sb.Append("\n");
                }
                if (m_State.fogColor.x.val != 0f ||
                    m_State.fogColor.y.val != 0f ||
                    m_State.fogColor.z.val != 0f ||
                    m_State.fogColor.w.val != 0f)
                {
                    sb.AppendFormat("   Color ({0},{1},{2},{3})\n",
                        m_State.fogColor.x.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.y.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.z.val.ToString(CultureInfo.InvariantCulture),
                        m_State.fogColor.w.val.ToString(CultureInfo.InvariantCulture));
                }
                if (m_State.fogDensity.val != 0f)
                {
                    sb.Append($"   Density {m_State.fogDensity.val.ToString(CultureInfo.InvariantCulture)}\n");
                }
                if (m_State.fogStart.val != 0f ||
                    m_State.fogEnd.val != 0f)
                {
                    sb.Append($"   Range {m_State.fogStart.val.ToString(CultureInfo.InvariantCulture)}, {m_State.fogEnd.val.ToString(CultureInfo.InvariantCulture)}\n");
                }
                sb.Append("  }\n");
            }

            if (m_State.lighting)
            {
                sb.Append($"  Lighting {(m_State.lighting ? "On" : "Off")}\n");
            }

            sb.Append($"  GpuProgramID {m_State.gpuProgramID}\n");

            return sb.ToString();
        }

        private static string ConvertSerializedStencilOp(SerializedStencilOp stencilOp, string suffix)
        {
            var sb = new StringBuilder();
            sb.Append($"   Comp{suffix} {ConvertStencilComp(stencilOp.comp)}\n");
            sb.Append($"   Pass{suffix} {ConvertStencilOp(stencilOp.pass)}\n");
            sb.Append($"   Fail{suffix} {ConvertStencilOp(stencilOp.fail)}\n");
            sb.Append($"   ZFail{suffix} {ConvertStencilOp(stencilOp.zFail)}\n");
            return sb.ToString();
        }

        private static string ConvertStencilOp(SerializedShaderFloatValue op)
        {
            switch (op.val)
            {
                case 0f:
                default:
                    return "Keep";
                case 1f:
                    return "Zero";
                case 2f:
                    return "Replace";
                case 3f:
                    return "IncrSat";
                case 4f:
                    return "DecrSat";
                case 5f:
                    return "Invert";
                case 6f:
                    return "IncrWrap";
                case 7f:
                    return "DecrWrap";
            }
        }

        private static string ConvertStencilComp(SerializedShaderFloatValue comp)
        {
            switch (comp.val)
            {
                case 0f:
                    return "Disabled";
                case 1f:
                    return "Never";
                case 2f:
                    return "Less";
                case 3f:
                    return "Equal";
                case 4f:
                    return "LEqual";
                case 5f:
                    return "Greater";
                case 6f:
                    return "NotEqual";
                case 7f:
                    return "GEqual";
                case 8f:
                default:
                    return "Always";
            }
        }

        private static string ConvertSerializedShaderRTBlendState(List<SerializedShaderRTBlendState> rtBlend, bool rtSeparateBlend)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < rtBlend.Count; i++)
            {
                var blend = rtBlend[i];
                if (blend.srcBlend.val != 1f ||
                    blend.destBlend.val != 0f ||
                    blend.srcBlendAlpha.val != 1f ||
                    blend.destBlendAlpha.val != 0f)
                {
                    sb.Append("  Blend ");
                    if (i != 0 || rtSeparateBlend)
                    {
                        sb.Append($"{i} ");
                    }
                    sb.Append($"{ConvertBlendFactor(blend.srcBlend)} {ConvertBlendFactor(blend.destBlend)}");
                    if (blend.srcBlendAlpha.val != 1f ||
                        blend.destBlendAlpha.val != 0f)
                    {
                        sb.Append($", {ConvertBlendFactor(blend.srcBlendAlpha)} {ConvertBlendFactor(blend.destBlendAlpha)}");
                    }
                    sb.Append("\n");
                }

                if (blend.blendOp.val != 0f ||
                    blend.blendOpAlpha.val != 0f)
                {
                    sb.Append("  BlendOp ");
                    if (i != 0 || rtSeparateBlend)
                    {
                        sb.Append($"{i} ");
                    }
                    sb.Append(ConvertBlendOp(blend.blendOp));
                    if (blend.blendOpAlpha.val != 0f)
                    {
                        sb.Append($", {ConvertBlendOp(blend.blendOpAlpha)}");
                    }
                    sb.Append("\n");
                }

                var val = (int)blend.colMask.val;
                if (val != 0xf)
                {
                    sb.Append("  ColorMask ");
                    if (val == 0)
                    {
                        sb.Append(0);
                    }
                    else
                    {
                        if ((val & 0x2) != 0)
                        {
                            sb.Append("R");
                        }
                        if ((val & 0x4) != 0)
                        {
                            sb.Append("G");
                        }
                        if ((val & 0x8) != 0)
                        {
                            sb.Append("B");
                        }
                        if ((val & 0x1) != 0)
                        {
                            sb.Append("A");
                        }
                    }
                    sb.Append($" {i}\n");
                }
            }
            return sb.ToString();
        }

        private static string ConvertBlendOp(SerializedShaderFloatValue op)
        {
            switch (op.val)
            {
                case 0f:
                default:
                    return "Add";
                case 1f:
                    return "Sub";
                case 2f:
                    return "RevSub";
                case 3f:
                    return "Min";
                case 4f:
                    return "Max";
                case 5f:
                    return "LogicalClear";
                case 6f:
                    return "LogicalSet";
                case 7f:
                    return "LogicalCopy";
                case 8f:
                    return "LogicalCopyInverted";
                case 9f:
                    return "LogicalNoop";
                case 10f:
                    return "LogicalInvert";
                case 11f:
                    return "LogicalAnd";
                case 12f:
                    return "LogicalNand";
                case 13f:
                    return "LogicalOr";
                case 14f:
                    return "LogicalNor";
                case 15f:
                    return "LogicalXor";
                case 16f:
                    return "LogicalEquiv";
                case 17f:
                    return "LogicalAndReverse";
                case 18f:
                    return "LogicalAndInverted";
                case 19f:
                    return "LogicalOrReverse";
                case 20f:
                    return "LogicalOrInverted";
            }
        }

        private static string ConvertBlendFactor(SerializedShaderFloatValue factor)
        {
            switch (factor.val)
            {
                case 0f:
                    return "Zero";
                case 1f:
                default:
                    return "One";
                case 2f:
                    return "DstColor";
                case 3f:
                    return "SrcColor";
                case 4f:
                    return "OneMinusDstColor";
                case 5f:
                    return "SrcAlpha";
                case 6f:
                    return "OneMinusSrcColor";
                case 7f:
                    return "DstAlpha";
                case 8f:
                    return "OneMinusDstAlpha";
                case 9f:
                    return "SrcAlphaSaturate";
                case 10f:
                    return "OneMinusSrcAlpha";
            }
        }

        private static string ConvertSerializedTagMap(SerializedTagMap m_Tags, int intent)
        {
            var sb = new StringBuilder();
            if (m_Tags.tags.Count > 0)
            {
                sb.Append(new string(' ', intent));
                sb.Append("Tags { ");
                foreach (var pair in m_Tags.tags)
                {
                    sb.Append($"\"{pair.Key}\" = \"{pair.Value}\" ");
                }
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        private static string ConvertSerializedProperties(SerializedProperties m_PropInfo)
        {
            var sb = new StringBuilder();
            sb.Append("Properties {\n");
            foreach (var m_Prop in m_PropInfo.m_Props)
            {
                sb.Append(ConvertSerializedProperty(m_Prop));
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string ConvertSerializedProperty(SerializedProperty m_Prop)
        {
            var sb = new StringBuilder();
            foreach (var m_Attribute in m_Prop.m_Attributes)
            {
                sb.Append($"[{m_Attribute}] ");
            }
            //TODO Flag
            sb.Append($"{m_Prop.m_Name} (\"{m_Prop.m_Description}\", ");
            switch (m_Prop.m_Type)
            {
                case SerializedPropertyType.Color:
                    sb.Append("Color");
                    break;
                case SerializedPropertyType.Vector:
                    sb.Append("Vector");
                    break;
                case SerializedPropertyType.Float:
                    sb.Append("Float");
                    break;
                case SerializedPropertyType.Range:
                    sb.Append($"Range({m_Prop.m_DefValue[1]}, {m_Prop.m_DefValue[2]})");
                    break;
                case SerializedPropertyType.Texture:
                    switch (m_Prop.m_DefTexture.m_TexDim)
                    {
                        case TextureDimension.Any:
                            sb.Append("any");
                            break;
                        case TextureDimension.Tex2D:
                            sb.Append("2D");
                            break;
                        case TextureDimension.Tex3D:
                            sb.Append("3D");
                            break;
                        case TextureDimension.Cube:
                            sb.Append("Cube");
                            break;
                        case TextureDimension.Tex2DArray:
                            sb.Append("2DArray");
                            break;
                        case TextureDimension.CubeArray:
                            sb.Append("CubeArray");
                            break;
                    }
                    break;
            }
            sb.Append(") = ");
            switch (m_Prop.m_Type)
            {
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector:
                    sb.Append($"({m_Prop.m_DefValue[0]},{m_Prop.m_DefValue[1]},{m_Prop.m_DefValue[2]},{m_Prop.m_DefValue[3]})");
                    break;
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Range:
                    sb.Append(m_Prop.m_DefValue[0]);
                    break;
                case SerializedPropertyType.Texture:
                    sb.Append($"\"{m_Prop.m_DefTexture.m_DefaultName}\" {{ }}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            sb.Append("\n");
            return sb.ToString();
        }

        private static bool CheckGpuProgramUsable(ShaderCompilerPlatform platform, ShaderGpuProgramType programType)
        {
            switch (platform)
            {
                case ShaderCompilerPlatform.GL:
                    return programType == ShaderGpuProgramType.GLLegacy;
                case ShaderCompilerPlatform.D3D9:
                    return programType == ShaderGpuProgramType.DX9VertexSM20
                        || programType == ShaderGpuProgramType.DX9VertexSM30
                        || programType == ShaderGpuProgramType.DX9PixelSM20
                        || programType == ShaderGpuProgramType.DX9PixelSM30;
                case ShaderCompilerPlatform.Xbox360:
                case ShaderCompilerPlatform.PS3:
                case ShaderCompilerPlatform.PSP2:
                case ShaderCompilerPlatform.PS4:
                case ShaderCompilerPlatform.XboxOne:
                case ShaderCompilerPlatform.N3DS:
                case ShaderCompilerPlatform.WiiU:
                case ShaderCompilerPlatform.Switch:
                case ShaderCompilerPlatform.XboxOneD3D12:
                case ShaderCompilerPlatform.GameCoreXboxOne:
                case ShaderCompilerPlatform.GameCoreScarlett:
                case ShaderCompilerPlatform.PS5:
                    return programType == ShaderGpuProgramType.ConsoleVS
                        || programType == ShaderGpuProgramType.ConsoleFS
                        || programType == ShaderGpuProgramType.ConsoleHS
                        || programType == ShaderGpuProgramType.ConsoleDS
                        || programType == ShaderGpuProgramType.ConsoleGS;
                case ShaderCompilerPlatform.PS5NGGC:
                    return programType == ShaderGpuProgramType.PS5NGGC;
                case ShaderCompilerPlatform.D3D11:
                    return programType == ShaderGpuProgramType.DX11VertexSM40
                        || programType == ShaderGpuProgramType.DX11VertexSM50
                        || programType == ShaderGpuProgramType.DX11PixelSM40
                        || programType == ShaderGpuProgramType.DX11PixelSM50
                        || programType == ShaderGpuProgramType.DX11GeometrySM40
                        || programType == ShaderGpuProgramType.DX11GeometrySM50
                        || programType == ShaderGpuProgramType.DX11HullSM50
                        || programType == ShaderGpuProgramType.DX11DomainSM50;
                case ShaderCompilerPlatform.GLES20:
                    return programType == ShaderGpuProgramType.GLES;
                case ShaderCompilerPlatform.NaCl: //Obsolete
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.Flash: //Obsolete
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.D3D11_9x:
                    return programType == ShaderGpuProgramType.DX10Level9Vertex
                        || programType == ShaderGpuProgramType.DX10Level9Pixel;
                case ShaderCompilerPlatform.GLES3Plus:
                    return programType == ShaderGpuProgramType.GLES31AEP
                        || programType == ShaderGpuProgramType.GLES31
                        || programType == ShaderGpuProgramType.GLES3;
                case ShaderCompilerPlatform.PSM: //Unknown
                    throw new NotSupportedException();
                case ShaderCompilerPlatform.Metal:
                    return programType == ShaderGpuProgramType.MetalVS
                        || programType == ShaderGpuProgramType.MetalFS;
                case ShaderCompilerPlatform.OpenGLCore:
                    return programType == ShaderGpuProgramType.GLCore32
                        || programType == ShaderGpuProgramType.GLCore41
                        || programType == ShaderGpuProgramType.GLCore43;
                case ShaderCompilerPlatform.Vulkan:
                    return programType == ShaderGpuProgramType.SPIRV;
                default:
                    throw new NotSupportedException();
            }
        }

        public static string GetPlatformString(ShaderCompilerPlatform platform)
        {
            switch (platform)
            {
                case ShaderCompilerPlatform.GL:
                    return "openGL";
                case ShaderCompilerPlatform.D3D9:
                    return "d3d9";
                case ShaderCompilerPlatform.Xbox360:
                    return "xbox360";
                case ShaderCompilerPlatform.PS3:
                    return "ps3";
                case ShaderCompilerPlatform.D3D11:
                    return "d3d11";
                case ShaderCompilerPlatform.GLES20:
                    return "gles";
                case ShaderCompilerPlatform.NaCl:
                    return "glesdesktop";
                case ShaderCompilerPlatform.Flash:
                    return "flash";
                case ShaderCompilerPlatform.D3D11_9x:
                    return "d3d11_9x";
                case ShaderCompilerPlatform.GLES3Plus:
                    return "gles3";
                case ShaderCompilerPlatform.PSP2:
                    return "psp2";
                case ShaderCompilerPlatform.PS4:
                    return "ps4";
                case ShaderCompilerPlatform.XboxOne:
                    return "xboxone";
                case ShaderCompilerPlatform.PSM:
                    return "psm";
                case ShaderCompilerPlatform.Metal:
                    return "metal";
                case ShaderCompilerPlatform.OpenGLCore:
                    return "glcore";
                case ShaderCompilerPlatform.N3DS:
                    return "n3ds";
                case ShaderCompilerPlatform.WiiU:
                    return "wiiu";
                case ShaderCompilerPlatform.Vulkan:
                    return "vulkan";
                case ShaderCompilerPlatform.Switch:
                    return "switch";
                case ShaderCompilerPlatform.XboxOneD3D12:
                    return "xboxone_d3d12";
                case ShaderCompilerPlatform.GameCoreXboxOne:
                    return "xboxone";
                case ShaderCompilerPlatform.GameCoreScarlett:
                    return "xbox_scarlett";
                case ShaderCompilerPlatform.PS5:
                    return "ps5";
                case ShaderCompilerPlatform.PS5NGGC:
                    return "ps5_nggc";
                default:
                    return "unknown";
            }
        }

        private static string header = "//////////////////////////////////////////\n" +
                                      "//\n" +
                                      "// NOTE: This is *not* a valid shader file\n" +
                                      "//\n" +
                                      "///////////////////////////////////////////\n";
    }

    public class ShaderSubProgramEntry
    {
        public int Offset;
        public int Length;
        public int Segment;

        public ShaderSubProgramEntry(EndianBinaryReader reader, int[] version)
        {
            Offset = reader.ReadInt32();
            Length = reader.ReadInt32();
            if (version[0] > 2019 || (version[0] == 2019 && version[1] >= 3)) //2019.3 and up
            {
                Segment = reader.ReadInt32();
            }
        }
    }

    public class ShaderProgram
    {
        public ShaderSubProgramEntry[] entries;
        public ShaderSubProgram[] m_SubPrograms;

        private bool hasUpdatedGpuProgram = false;

        public ShaderProgram(EndianBinaryReader reader, Shader shader)
        {
            var subProgramsCapacity = reader.ReadInt32();
            entries = new ShaderSubProgramEntry[subProgramsCapacity];
            for (int i = 0; i < subProgramsCapacity; i++)
            {
                entries[i] = new ShaderSubProgramEntry(reader, shader.version);
            }
            m_SubPrograms = new ShaderSubProgram[subProgramsCapacity];
            if (shader.assetsFile.game.Type.IsGI())
            {
                hasUpdatedGpuProgram = SerializedSubProgram.HasInstancedStructuredBuffers(shader.serializedType) || SerializedSubProgram.HasGlobalLocalKeywordIndices(shader.serializedType);
            }
        }

        public void Read(EndianBinaryReader reader, int segment)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Segment == segment)
                {
                    reader.BaseStream.Position = entry.Offset;
                    m_SubPrograms[i] = new ShaderSubProgram(reader, hasUpdatedGpuProgram);
                }
            }
        }

        public string Export(string shader)
        {
            var evaluator = new MatchEvaluator(match =>
            {
                var index = int.Parse(match.Groups[1].Value);
                return m_SubPrograms[index].Export();
            });
            shader = Regex.Replace(shader, "GpuProgramIndex (.+)", evaluator);
            return shader;
        }
    }

    public class ShaderSubProgram
    {
        private int m_Version;
        public ShaderGpuProgramType m_ProgramType;
        public string[] m_Keywords;
        public string[] m_LocalKeywords;
        public byte[] m_ProgramCode;


        // JTAOO here
        public VectorParameterJ[] VectorParameters { get; set; } = Array.Empty<VectorParameterJ>();
        public MatrixParameterJ[] MatrixParameters { get; set; } = Array.Empty<MatrixParameterJ>();
        public TextureParameterJ[] TextureParameters { get; set; } = Array.Empty<TextureParameterJ>();
        public BufferBindingJ[] BufferParameters { get; set; } = Array.Empty<BufferBindingJ>();
        public UAVParameterJ[] UAVParameters { get; set; } = Array.Empty<UAVParameterJ>();
        public SamplerParameterJ[] SamplerParameters { get; set; } = Array.Empty<SamplerParameterJ>();
        public ConstantBufferJ[] ConstantBuffers { get; set; } = Array.Empty<ConstantBufferJ>();
        public BufferBindingJ[] ConstantBufferBindings { get; set; } = Array.Empty<BufferBindingJ>();
        public StructParameterJ[] StructParameters { get; set; } = Array.Empty<StructParameterJ>();
        public ParserBindChannelsJ BindChannels { get; set; } = new();


        private string ReadStringJ(EndianBinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == 0)
            {
                return string.Empty;
            }
            byte[] buffer = new byte[length];
            int offset = 0;
            int count = length;
            while (count > 0)
            {
                int read = reader.Read(buffer, offset, count);
                if (read == 0)
                {
                    throw new Exception($"End of stream. Read {offset}, expected {length} bytes");
                }
                offset += read;
                count -= read;
            }
            reader.AlignStream();
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        public ShaderSubProgram(EndianBinaryReader reader, bool hasUpdatedGpuProgram)
        {
            //LoadGpuProgramFromData
            //201509030 - Unity 5.3
            //201510240 - Unity 5.4
            //201608170 - Unity 5.5
            //201609010 - Unity 5.6, 2017.1 & 2017.2
            //201708220 - Unity 2017.3, Unity 2017.4 & Unity 2018.1
            //201802150 - Unity 2018.2 & Unity 2018.3
            //201806140 - Unity 2019.1~2021.1
            //202012090 - Unity 2021.2
            m_Version = reader.ReadInt32();
            // 202211230
            if (hasUpdatedGpuProgram && m_Version > 201806140)
            {
                m_Version = 201806140;
            }
            m_ProgramType = (ShaderGpuProgramType)reader.ReadInt32();
            reader.BaseStream.Position += 12;
            if (m_Version >= 201608170)
            {
                reader.BaseStream.Position += 4;
            }
            var m_KeywordsSize = reader.ReadInt32();
            m_Keywords = new string[m_KeywordsSize];
            for (int i = 0; i < m_KeywordsSize; i++)
            {
                m_Keywords[i] = reader.ReadAlignedString();
            }
            if (m_Version >= 201806140 && m_Version < 202012090)
            {
                var m_LocalKeywordsSize = reader.ReadInt32();
                m_LocalKeywords = new string[m_LocalKeywordsSize];
                for (int i = 0; i < m_LocalKeywordsSize; i++)
                {
                    m_LocalKeywords[i] = reader.ReadAlignedString();
                }
            }
            m_ProgramCode = reader.ReadUInt8Array();
            reader.AlignStream();

            //TODO

            // JTAOO Do here
            int sourceMap = reader.ReadInt32();
            int bindCount = reader.ReadInt32();
            ShaderBindChannelJ[] channels = new ShaderBindChannelJ[bindCount];
            for (int i = 0; i < bindCount; i++)
            {
                uint source = reader.ReadUInt32();
                VertexComponent target = (VertexComponent)reader.ReadUInt32();
                ShaderBindChannelJ channel = new ShaderBindChannelJ(source, target);
                channels[i] = channel;
                sourceMap |= 1 << (int)source;
            }
            BindChannels = new ParserBindChannelsJ(channels, sourceMap);

            List<VectorParameterJ> vectors = new List<VectorParameterJ>();
            List<MatrixParameterJ> matrices = new List<MatrixParameterJ>();
            List<TextureParameterJ> textures = new List<TextureParameterJ>();
            List<VectorParameterJ> structVectors = new List<VectorParameterJ>();
            List<MatrixParameterJ> structMatrices = new List<MatrixParameterJ>();
            List<BufferBindingJ> buffers = new List<BufferBindingJ>();
            List<UAVParameterJ>? uavs = new List<UAVParameterJ>();
            List<SamplerParameterJ>? samplers = new List<SamplerParameterJ>();
            List<BufferBindingJ> constBindings = new List<BufferBindingJ>();
            List<StructParameterJ> structs = new List<StructParameterJ>();

            int paramGroupCount = reader.ReadInt32();
            ConstantBuffers = new ConstantBufferJ[paramGroupCount - 1];
            for (int i = 0; i < paramGroupCount; i++)
            {
                vectors.Clear();
                matrices.Clear();
                structs.Clear();

                string groupName = ReadStringJ(reader);
                //string name = reader.ReadString();
                int usedSize = reader.ReadInt32();
                int paramCount = reader.ReadInt32();
                for (int j = 0; j < paramCount; j++)
                {
                    string paramName = ReadStringJ(reader);
                    //string paramName = reader.ReadString();
                    ShaderParamType paramType = (ShaderParamType)reader.ReadInt32();
                    int rows = reader.ReadInt32();
                    int columns = reader.ReadInt32();
                    bool isMatrix = reader.ReadInt32() > 0;
                    int arraySize = reader.ReadInt32();
                    int index = reader.ReadInt32();

                    if (isMatrix)
                    {
                        MatrixParameterJ matrix = new MatrixParameterJ(paramName, paramType, index, arraySize, rows, columns);
                        matrices.Add(matrix);
                    }
                    else
                    {
                        VectorParameterJ vector = new VectorParameterJ(paramName, paramType, index, arraySize, columns);
                        vectors.Add(vector);
                    }
                }
                // if (HasStructParameters(reader_Version))
                int structCount = reader.ReadInt32();
                for (int j = 0; j < structCount; j++)
                {
                    structVectors.Clear();
                    structMatrices.Clear();

                    string structName = ReadStringJ(reader);
                    //string structName = reader.ReadString();
                    int index = reader.ReadInt32();
                    int arraySize = reader.ReadInt32();
                    int structSize = reader.ReadInt32();

                    int strucParamCount = reader.ReadInt32();
                    for (int k = 0; k < strucParamCount; k++)
                    {
                        string paramName = ReadStringJ(reader);
                        //string paramName = reader.ReadString();
                        paramName = $"{structName}.{paramName}";
                        ShaderParamType paramType = (ShaderParamType)reader.ReadInt32();
                        int rows = reader.ReadInt32();
                        int columns = reader.ReadInt32();
                        bool isMatrix = reader.ReadInt32() > 0;
                        int vectorArraySize = reader.ReadInt32();
                        int paramIndex = reader.ReadInt32();

                        if (isMatrix)
                        {
                            MatrixParameterJ matrix = new MatrixParameterJ(paramName, paramType, paramIndex, vectorArraySize, rows, columns);
                            structMatrices.Add(matrix);
                        }
                        else
                        {
                            VectorParameterJ vector = new VectorParameterJ(paramName, paramType, paramIndex, vectorArraySize, columns);
                            structVectors.Add(vector);
                        }
                    }

                    StructParameterJ @struct = new StructParameterJ(structName, index, arraySize, structSize, structVectors.ToArray(), structMatrices.ToArray());
                    structs.Add(@struct);
                }
                if (i == 0)
                {
                    VectorParameters = vectors.ToArray();
                    MatrixParameters = matrices.ToArray();
                    StructParameters = structs.ToArray();
                }
                else
                {
                    ConstantBufferJ constBuffer = new ConstantBufferJ(groupName, matrices.ToArray(), vectors.ToArray(), structs.ToArray(), usedSize);
                    ConstantBuffers[i - 1] = constBuffer;
                }


            } // ... 


            //=================
            int paramGroup2Count = reader.ReadInt32(); 

            for (int i = 0; i < paramGroup2Count; i++)
            {
                string name = ReadStringJ(reader);
                //string name = reader.ReadString();
                int type = reader.ReadInt32();
                int index = reader.ReadInt32();
                int extraValue = reader.ReadInt32();

                if (type == 0)
                {
                    TextureParameterJ texture;
                    if (false)// HasNewTextureParams(reader_Version)
                    {
                        uint textureExtraValue = reader.ReadUInt32();
                        bool isMultiSampled = (textureExtraValue & 1) == 1;
                        byte dimension = (byte)(textureExtraValue >> 1);
                        int samplerIndex = extraValue;
                        texture = new TextureParameterJ(name, index, dimension, samplerIndex, isMultiSampled);
                    }
                    else if (true)//HasMultiSampled(reader_Version)
                    {
                        uint textureExtraValue = reader.ReadUInt32();
                        bool isMultiSampled = textureExtraValue == 1;
                        byte dimension = unchecked((byte)extraValue);
                        int samplerIndex = extraValue >> 8;
                        if (samplerIndex == 0xFFFFFF)
                        {
                            samplerIndex = -1;
                        }

                        texture = new TextureParameterJ(name, index, dimension, samplerIndex, isMultiSampled);
                    }
                    else
                    {
                        byte dimension = unchecked((byte)extraValue);
                        int samplerIndex = extraValue >> 8;
                        if (samplerIndex == 0xFFFFFF)
                        {
                            samplerIndex = -1;
                        }

                        texture = new TextureParameterJ(name, index, dimension, samplerIndex);
                    }
                    textures.Add(texture);
                }
                else if (type == 1)
                {
                    BufferBindingJ binding = new BufferBindingJ(name, index);
                    constBindings.Add(binding);
                }
                else if (type == 2)
                {
                    BufferBindingJ buffer = new BufferBindingJ(name, index);
                    buffers.Add(buffer);
                }
                else if (type == 3 && true)//HasUAVParameters(reader_Version)
                {
                    UAVParameterJ uav = new UAVParameterJ(name, index, extraValue);
                    uavs.Add(uav);
                }
                else if (type == 4 && true)//HasSamplerParameters(reader_Version)
                {
                    SamplerParameterJ sampler = new SamplerParameterJ((uint)extraValue, index);
                    samplers.Add(sampler);
                }
                else
                {
                    throw new Exception($"Unupported parameter type {type}");
                }


            }

            TextureParameters = textures.ToArray();
            BufferParameters = buffers.ToArray();
            if (true)//HasUAVParameters(reader_Version)
            {
                UAVParameters = uavs.ToArray();
            }

            if (true)//HasSamplerParameters(reader_Version)
            {
                SamplerParameters = samplers.ToArray();
            }

            ConstantBufferBindings = constBindings.ToArray();
            if (true)//HasStructParameters(reader_Version)
            {
                StructParameters = structs.ToArray();
            }
        }

        private static byte[] GetRelevantData(byte[] bytes, int offset)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            int size = bytes.Length - offset;
            byte[] result = new byte[size];
            for (int i = 0; i < size; i++)
            {
                result[i] = bytes[i + offset];
            }
            return result;
        }

        private static void ExportPassConstantBufferDefinitions(StringBuilder sb, HashSet<string> declaredBufs,
           ConstantBufferJ cbuffer, int depth)
        {
            if (cbuffer != null)
            {
                bool nonGlobalCbuffer = cbuffer.Name.Contains("Global") == false;

                if (nonGlobalCbuffer)
                {
                    sb.AppendIndent(depth);
                    sb.AppendLine($"CBUFFER_START({cbuffer.Name})");
                    depth++;
                }

                NumericShaderParameterJ[] allParams = cbuffer.AllNumericParams;
                foreach (NumericShaderParameterJ param in allParams)
                {
                    string typeName = DXShaderNamingUtilsJ.GetConstantBufferParamTypeName(param);
                    string name = param.Name;

                    // skip things like unity_MatrixVP if they show up in $Globals
                    //if (UnityShaderConstants.INCLUDED_UNITY_PROP_NAMES.Contains(name))
                    {
                        //continue;
                    }

                    if (!declaredBufs.Contains(name))
                    {
                        if (param.ArraySize > 0)
                        {
                            sb.AppendIndent(depth);
                            sb.AppendLine($"{typeName} {name}[{param.ArraySize}];");
                        }
                        else
                        {
                            sb.AppendIndent(depth);
                            sb.AppendLine($"{typeName} {name};");
                        }
                        declaredBufs.Add(name);
                    }
                }

                if (nonGlobalCbuffer)
                {
                    depth--;
                    sb.AppendIndent(depth);
                    sb.AppendLine("CBUFFER_END");
                }
            }
        }

        private void ExportPassTextureParamDefinitions(StringBuilder sb, HashSet<string> declaredBufs, int depth)
        {
            foreach (TextureParameterJ param in TextureParameters)
            {
                string name = param.Name;
                if (!declaredBufs.Contains(name) && true)//!UnityShaderConstants.BUILTIN_TEXTURE_NAMES.Contains(name)
                {
                    sb.AppendIndent(depth);
                    if (param.Dim == 2)
                    {
                        sb.AppendLine($"sampler2D {name};");
                    }
                    else if (param.Dim == 3)
                    {
                        sb.AppendLine($"sampler3D {name};");
                    }
                    else if (param.Dim == 4)
                    {
                        sb.AppendLine($"samplerCUBE {name};");
                    }
                    else if (param.Dim == 5)
                    {
                        sb.AppendLine($"UNITY_DECLARE_TEX2DARRAY({name});");
                    }
                    else if (param.Dim == 6)
                    {
                        sb.AppendLine($"UNITY_DECLARE_TEXCUBEARRAY({name});");
                    }
                    else
                    {
                        sb.AppendLine($"sampler2D {name}; // Unsure of real type ({param.Dim})");
                    }
                    declaredBufs.Add(name);
                }
            }
        }

        public string Export()
        {
            var sb = new StringBuilder();
            if (m_Keywords.Length > 0)
            {
                sb.Append("Keywords { ");
                foreach (string keyword in m_Keywords)
                {
                    sb.Append($"\"{keyword}\" ");
                }
                sb.Append("}\n");
            }
            if (m_LocalKeywords != null && m_LocalKeywords.Length > 0)
            {
                sb.Append("Local Keywords { ");
                foreach (string keyword in m_LocalKeywords)
                {
                    sb.Append($"\"{keyword}\" ");
                }
                sb.Append("}\n");
            }

            sb.Append("\"");
            if (m_ProgramCode.Length > 0)
            {
                switch (m_ProgramType)
                {
                    case ShaderGpuProgramType.GLLegacy:
                    case ShaderGpuProgramType.GLES31AEP:
                    case ShaderGpuProgramType.GLES31:
                    case ShaderGpuProgramType.GLES3:
                    case ShaderGpuProgramType.GLES:
                    case ShaderGpuProgramType.GLCore32:
                    case ShaderGpuProgramType.GLCore41:
                    case ShaderGpuProgramType.GLCore43:
                        sb.Append(Encoding.UTF8.GetString(m_ProgramCode));
                        break;
                    case ShaderGpuProgramType.DX9VertexSM20:
                    case ShaderGpuProgramType.DX9VertexSM30:
                    case ShaderGpuProgramType.DX9PixelSM20:
                    case ShaderGpuProgramType.DX9PixelSM30:
                        {
                            /*var shaderBytecode = new ShaderBytecode(m_ProgramCode);
                            sb.Append(shaderBytecode.Disassemble());*/
                            sb.Append("// shader disassembly not supported on DXBC");
                            break;
                        }
                    case ShaderGpuProgramType.DX10Level9Vertex:
                    case ShaderGpuProgramType.DX10Level9Pixel:
                    case ShaderGpuProgramType.DX11VertexSM40:
                    case ShaderGpuProgramType.DX11VertexSM50:
                        {
                            sb.Append("\"");
                            sb.Append("// DX11VertexSM50");
                            sb.Append("\n// only for genshin, by JTAOO");
                            sb.Append("\n  ");

                            int headerVersion = m_ProgramCode[0];
                            int dataOffset = 6;
                            if (headerVersion >= 2)
                            {
                                dataOffset += 0x20;
                            }
                            byte[] trimmedProgramData = GetRelevantData(m_ProgramCode, dataOffset);

                            USCShaderConverterJ vertexConverter = new USCShaderConverterJ();
                            vertexConverter.LoadDirectXCompiledShader(new MemoryStream(trimmedProgramData));

                            sb.AppendIndent(3);
                            sb.AppendLine("#pragma vertex vert");
                            DirectXCompiledShaderJ dxShader_vertex = vertexConverter.DxShader;

                            sb.AppendIndent(3);
                            sb.AppendLine("struct appdata_full");
                            sb.AppendIndent(3);
                            sb.AppendLine("{");
                            foreach (ISGN.Input input in dxShader_vertex.Isgn.inputs)
                            {
                                string type = input.name + input.index;
                                string name = DXShaderNamingUtilsJ.GetISGNInputName(input);
                                sb.AppendIndent(4);
                                sb.AppendLine($" {name} : {type};");
                            }
                            sb.AppendIndent(3);
                            sb.AppendLine("};");

                            sb.AppendIndent(3);
                            sb.AppendLine("struct v2f");
                            sb.AppendIndent(3);
                            sb.AppendLine("{");

                            foreach (OSGN.Output output in dxShader_vertex.Osgn.outputs)
                            {
                                string format = DXShaderNamingUtilsJ.GetOSGNFormatName(output);
                                string type = output.name + output.index;
                                string name = DXShaderNamingUtilsJ.GetOSGNOutputName(output);
                                sb.AppendIndent(4);
                                sb.AppendLine($"{format} {name} : {type};");
                            }
                            sb.AppendIndent(3);
                            sb.AppendLine("};");

                            HashSet<string> declaredBufs = new HashSet<string>();
                            sb.AppendIndent(3);
                            sb.AppendLine("// $Globals ConstantBuffers for Vertex Shader");
                            ConstantBufferJ cbuffer = ConstantBuffers.FirstOrDefault(cb => cb.Name.Contains("Global"));
                            ExportPassConstantBufferDefinitions(sb, declaredBufs, cbuffer, 3);

                            sb.AppendIndent(3);
                            sb.AppendLine("// Custom ConstantBuffers for Vertex Shader");
                            foreach (ConstantBufferJ cbuffer1 in ConstantBuffers)
                            {
                                //if (UnityShaderConstants.BUILTIN_CBUFFER_NAMES.Contains(cbuffer1.Name))
                                {
                                    //continue;
                                }
                                sb.AppendIndent(3);
                                sb.AppendLine("// groupName: " + cbuffer1.Name);
                                ExportPassConstantBufferDefinitions(sb, declaredBufs, cbuffer1, 3);
                            }

                            sb.AppendIndent(3);
                            sb.AppendLine("// Texture params for Vertex Shader");
                            ExportPassTextureParamDefinitions(sb, declaredBufs, 3);

                            sb.AppendIndent(3);
                            sb.AppendLine("");

                            string keywordsList = "";
                            if (m_Keywords != null)
                            {
                                foreach (string keyword1 in m_Keywords)
                                {
                                    keywordsList = keywordsList + " " + keyword1;
                                }
                            }
                            if (m_LocalKeywords != null)
                            {
                                foreach (string keyword1 in m_LocalKeywords)
                                {
                                    keywordsList = keywordsList + " " + keyword1;
                                }
                            }
                            sb.AppendIndent(3);
                            sb.AppendLine($"// Keywords: {keywordsList}");

                            sb.AppendIndent(3);
                            sb.AppendLine($"{USILConstants.VERT_TO_FRAG_STRUCT_NAME} vert(appdata_full {USILConstants.VERT_INPUT_NAME})");
                            sb.AppendIndent(3);
                            sb.AppendLine("{");

                            vertexConverter.ConvertShaderToUShaderProgram();
                            // gs4.0取不到bind信息, 再看看
                            if(ConstantBufferBindings.Length > 0)
                                vertexConverter.ApplyMetadataToProgram_Vertex(this);
                            string progamText = vertexConverter.CovnertUShaderProgramToHLSL(4);
                            sb.Append(progamText);
                            sb.AppendIndent(3);
                            sb.AppendLine("}");

                            sb.AppendIndent(3);
                            sb.AppendLine("ENDCG");

                            sb.Append("\"");
                            break;
                        }
                    case ShaderGpuProgramType.DX11PixelSM40: 
                    case ShaderGpuProgramType.DX11PixelSM50:
                        {
                            sb.Append("\"");
                            sb.Append("// DX11PixelSM50");
                            sb.Append("\n// only for genshin, by JTAOO");
                            sb.Append("\n  ");

                            int headerVersion = m_ProgramCode[0];
                            int dataOffset = 6;
                            if (headerVersion >= 2)
                            {
                                dataOffset += 0x20;
                            }
                            byte[] trimmedProgramData = GetRelevantData(m_ProgramCode, dataOffset);

                            USCShaderConverterJ fragmentConverter = new USCShaderConverterJ();
                            fragmentConverter.LoadDirectXCompiledShader(new MemoryStream(trimmedProgramData));

                            sb.AppendIndent(3);
                            sb.AppendLine("#pragma fragment frag");
                            sb.AppendIndent(3);
                            sb.AppendLine("struct fout");
                            sb.AppendIndent(3);
                            sb.AppendLine("{");
                            DirectXCompiledShaderJ dxShader_frag = fragmentConverter.DxShader;
                            foreach (OSGN.Output output in dxShader_frag.Osgn.outputs)
                            {
                                string format = DXShaderNamingUtilsJ.GetOSGNFormatName(output);
                                string type = output.name + output.index;
                                string name = DXShaderNamingUtilsJ.GetOSGNOutputName(output);
                                sb.AppendIndent(4);
                                sb.AppendLine($"{format} {name} : {type};");
                            }
                            sb.AppendIndent(3);
                            sb.AppendLine("};");

                            HashSet<string> declaredBufs = new HashSet<string>();
                            sb.AppendIndent(3);
                            sb.AppendLine("// $Globals ConstantBuffers for Fragment Shader");
                            ConstantBufferJ cbuffer = ConstantBuffers.FirstOrDefault(cb => cb.Name.Contains("Global"));
                            ExportPassConstantBufferDefinitions(sb, declaredBufs, cbuffer, 3);

                            sb.AppendIndent(3);
                            sb.AppendLine("// Custom ConstantBuffers for Fragment Shader");
                            foreach (ConstantBufferJ cbuffer1 in ConstantBuffers)
                            {
                                //if (UnityShaderConstants.BUILTIN_CBUFFER_NAMES.Contains(cbuffer1.Name))
                                {
                                    //continue;
                                }
                                sb.AppendIndent(3);
                                sb.AppendLine("// groupName: " + cbuffer1.Name);
                                ExportPassConstantBufferDefinitions(sb, declaredBufs, cbuffer1, 3);
                            }

                            sb.AppendIndent(3);
                            sb.AppendLine("// Texture params for Fragment Shader");
                            ExportPassTextureParamDefinitions(sb, declaredBufs, 3);

                            sb.AppendIndent(3);
                            sb.AppendLine("");

                            string keywordsList = "";
                            if (m_Keywords != null)
                            {
                                foreach (string keyword1 in m_Keywords)
                                {
                                    keywordsList = keywordsList + " " + keyword1;
                                }
                            }
                            if (m_LocalKeywords != null)
                            {
                                foreach (string keyword1 in m_LocalKeywords)
                                {
                                    keywordsList = keywordsList + " " + keyword1;
                                }
                            }
                            sb.AppendIndent(3);
                            sb.AppendLine($"// Keywords: {keywordsList}");

                            DirectXCompiledShaderJ dxShader1 = fragmentConverter.DxShader;
                            bool hasFrontFace = dxShader1.Isgn.inputs.Any(i => i.name == "SV_IsFrontFace");

                            sb.AppendIndent(3);
                            string args = $"{USILConstants.VERT_TO_FRAG_STRUCT_NAME} {USILConstants.FRAG_INPUT_NAME}";
                            if (hasFrontFace)
                            {
                                // not part of v2f
                                args += $", float facing: VFACE";
                            }
                            sb.AppendLine($"{USILConstants.FRAG_OUTPUT_STRUCT_NAME} frag({args})");
                            sb.AppendIndent(3);
                            sb.AppendLine("{");

                            fragmentConverter.ConvertShaderToUShaderProgram();
                            fragmentConverter.ApplyMetadataToProgram_Frag(this);
                            string progamText = fragmentConverter.CovnertUShaderProgramToHLSL(4);
                            sb.Append(progamText);
                            sb.AppendIndent(3);
                            sb.AppendLine("}");

                            sb.AppendIndent(3);
                            sb.AppendLine("ENDCG");

                            sb.Append("\"");
                            break;
                        }
                    case ShaderGpuProgramType.DX11GeometrySM40:
                    case ShaderGpuProgramType.DX11GeometrySM50:
                    case ShaderGpuProgramType.DX11HullSM50:
                    case ShaderGpuProgramType.DX11DomainSM50:
                        {
                            /*int start = 6;
                            if (m_Version == 201509030) // 5.3
                            {
                                start = 5;
                            }
                            var buff = new byte[m_ProgramCode.Length - start];
                            Buffer.BlockCopy(m_ProgramCode, start, buff, 0, buff.Length);
                            var shaderBytecode = new ShaderBytecode(buff);
                            sb.Append(shaderBytecode.Disassemble());*/
                            sb.Append("// shader disassembly not supported on DXBC");
                            break;
                        }
                    case ShaderGpuProgramType.MetalVS:
                    case ShaderGpuProgramType.MetalFS:
                        using (var reader = new EndianBinaryReader(new MemoryStream(m_ProgramCode), EndianType.LittleEndian))
                        {
                            var fourCC = reader.ReadUInt32();
                            if (fourCC == 0xf00dcafe)
                            {
                                int offset = reader.ReadInt32();
                                reader.BaseStream.Position = offset;
                            }
                            var entryName = reader.ReadStringToNull();
                            var buff = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
                            sb.Append(Encoding.UTF8.GetString(buff));
                        }
                        break;
                    case ShaderGpuProgramType.SPIRV:
                        try
                        {
                            // sb.Append(SpirVShaderConverter.Convert(m_ProgramCode)); 
                            sb.AppendIndent(3);
                            sb.AppendLine("SPIRV Hided by JTAOO");
                        }
                        catch (Exception e)
                        {
                            sb.Append($"// disassembly error {e.Message}\n");
                        }
                        break;
                    case ShaderGpuProgramType.ConsoleVS:
                    case ShaderGpuProgramType.ConsoleFS:
                    case ShaderGpuProgramType.ConsoleHS:
                    case ShaderGpuProgramType.ConsoleDS:
                    case ShaderGpuProgramType.ConsoleGS:
                        sb.Append(Encoding.UTF8.GetString(m_ProgramCode));
                        break;
                    default:
                        sb.Append($"//shader disassembly not supported on {m_ProgramType}");
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
