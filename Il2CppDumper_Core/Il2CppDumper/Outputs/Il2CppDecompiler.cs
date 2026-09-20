using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class Il2CppDecompiler
    {
        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;
        private readonly Dictionary<Il2CppMethodDefinition, string> methodModifiers;

        public Il2CppDecompiler(Il2CppExecutor il2CppExecutor)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
            methodModifiers = new();
        }

        public void Decompile(Config config, string outputDir)
        {
            var stream = new FileStream(outputDir + "dump.cs", FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 128 * 1024);
            //dump image
            for (var imageIndex = 0; imageIndex < metadata.imageDefs.Length; imageIndex++)
            {
                var imageDef = metadata.imageDefs[imageIndex];
                writer.Write($"// Image {imageIndex}: {metadata.GetStringFromIndex(imageDef.nameIndex)} - {imageDef.typeStart}\n");
            }
            //dump type
            int totalTypes = 0;
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                if (config.DumpOnlyAssemblyCSharp && !imageName.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                totalTypes += (int)Math.Min((long)imageDef.typeStart + imageDef.typeCount, (long)metadata.typeDefs.Length) - imageDef.typeStart;
            }

            int processedTypes = 0;
            int lastPercent = -1;

            foreach (var imageDef in metadata.imageDefs)
            {
                try
                {
                    var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                    if (config.DumpOnlyAssemblyCSharp && !imageName.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var typeEnd = Math.Min(imageDef.typeStart + imageDef.typeCount, metadata.typeDefs.Length);
                    for (int typeDefIndex = imageDef.typeStart; typeDefIndex < typeEnd; typeDefIndex++)
                    {
                        processedTypes++;
                        if (totalTypes > 0)
                        {
                            int percent = (int)((long)processedTypes * 100 / totalTypes);
                            if (percent != lastPercent && (percent % 2 == 0 || percent == 100))
                            {
                                lastPercent = percent;
                                DrawProgressBar("Descompilando classes", percent);
                            }
                        }
                      try
                      {
                        var typeDef = metadata.typeDefs[typeDefIndex];
                        var extends = new List<string>();
                        if (typeDef.parentIndex >= 0)
                        {
                            var parent = il2Cpp.SafeGetType(typeDef.parentIndex);
                            var parentName = executor.GetTypeName(parent, false, false);
                            if (!typeDef.IsValueType && !typeDef.IsEnum && parentName != "object")
                            {
                                extends.Add(parentName);
                            }
                        }
                        if (typeDef.interfaces_count > 0 && metadata.interfaceIndices != null)
                        {
                            for (int i = 0; i < typeDef.interfaces_count; i++)
                            {
                                int idx = typeDef.interfacesStart + i;
                                if (idx >= 0 && idx < metadata.interfaceIndices.Length)
                                {
                                    var @interface = il2Cpp.SafeGetType(metadata.interfaceIndices[idx]);
                                    extends.Add(executor.GetTypeName(@interface, false, false));
                                }
                            }
                        }
                        writer.Write($"\n// Dll : {imageName}\n// Namespace: {metadata.GetStringFromIndex(typeDef.namespaceIndex)}\n");
                        if (config.DumpAttribute)
                        {
                            writer.Write(GetCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token));
                        }
                        if (config.DumpAttribute && (typeDef.flags & TYPE_ATTRIBUTE_SERIALIZABLE) != 0)
                            writer.Write("[Serializable]\n");
                        var visibility = typeDef.flags & TYPE_ATTRIBUTE_VISIBILITY_MASK;
                        switch (visibility)
                        {
                            case TYPE_ATTRIBUTE_PUBLIC:
                            case TYPE_ATTRIBUTE_NESTED_PUBLIC:
                                writer.Write("public ");
                                break;
                            case TYPE_ATTRIBUTE_NOT_PUBLIC:
                            case TYPE_ATTRIBUTE_NESTED_FAM_AND_ASSEM:
                            case TYPE_ATTRIBUTE_NESTED_ASSEMBLY:
                                writer.Write("internal ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_PRIVATE:
                                writer.Write("private ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_FAMILY:
                                writer.Write("protected ");
                                break;
                            case TYPE_ATTRIBUTE_NESTED_FAM_OR_ASSEM:
                                writer.Write("protected internal ");
                                break;
                        }
                        if ((typeDef.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0 && (typeDef.flags & TYPE_ATTRIBUTE_SEALED) != 0)
                            writer.Write("static ");
                        else if ((typeDef.flags & TYPE_ATTRIBUTE_INTERFACE) == 0 && (typeDef.flags & TYPE_ATTRIBUTE_ABSTRACT) != 0)
                            writer.Write("abstract ");
                        else if (!typeDef.IsValueType && !typeDef.IsEnum && (typeDef.flags & TYPE_ATTRIBUTE_SEALED) != 0)
                            writer.Write("sealed ");
                        if ((typeDef.flags & TYPE_ATTRIBUTE_INTERFACE) != 0)
                            writer.Write("interface ");
                        else if (typeDef.IsEnum)
                            writer.Write("enum ");
                        else if (typeDef.IsValueType)
                            writer.Write("struct ");
                        else
                            writer.Write("class ");
                        var typeName = executor.GetTypeDefName(typeDef, false, true);
                        writer.Write($"{typeName}");
                        if (extends.Count > 0)
                            writer.Write($" : {string.Join(", ", extends)}");
                        writer.Write("\n{\n");
                        //dump field
                        if (config.DumpField && typeDef.field_count > 0 && metadata.fieldDefs != null)
                        {
                            writer.Write("\n\t// Fields\n");
                            var fieldEnd = Math.Min(typeDef.fieldStart + typeDef.field_count, metadata.fieldDefs.Length);
                            for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                            {
                                try
                                {
                                    var fieldDef = metadata.fieldDefs[i];
                                    var fieldType = il2Cpp.SafeGetType(fieldDef.typeIndex);
                                var isStatic = false;
                                var isConst = false;
                                if (config.DumpAttribute)
                                {
                                    writer.Write(GetCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, "\t"));
                                }
                                writer.Write("\t");
                                var access = fieldType.attrs & FIELD_ATTRIBUTE_FIELD_ACCESS_MASK;
                                switch (access)
                                {
                                    case FIELD_ATTRIBUTE_PRIVATE:
                                        writer.Write("private ");
                                        break;
                                    case FIELD_ATTRIBUTE_PUBLIC:
                                        writer.Write("public ");
                                        break;
                                    case FIELD_ATTRIBUTE_FAMILY:
                                        writer.Write("protected ");
                                        break;
                                    case FIELD_ATTRIBUTE_ASSEMBLY:
                                    case FIELD_ATTRIBUTE_FAM_AND_ASSEM:
                                        writer.Write("internal ");
                                        break;
                                    case FIELD_ATTRIBUTE_FAM_OR_ASSEM:
                                        writer.Write("protected internal ");
                                        break;
                                }
                                if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                                {
                                    isConst = true;
                                    writer.Write("const ");
                                }
                                else
                                {
                                    if ((fieldType.attrs & FIELD_ATTRIBUTE_STATIC) != 0)
                                    {
                                        isStatic = true;
                                        writer.Write("static ");
                                    }
                                    if ((fieldType.attrs & FIELD_ATTRIBUTE_INIT_ONLY) != 0)
                                    {
                                        writer.Write("readonly ");
                                    }
                                }
                                writer.Write($"{executor.GetTypeName(fieldType, false, false)} {metadata.GetStringFromIndex(fieldDef.nameIndex)}");
                                if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefaultValue) && fieldDefaultValue.dataIndex != -1)
                                {
                                    if (executor.TryGetDefaultValue(fieldDefaultValue.typeIndex, fieldDefaultValue.dataIndex, out var value))
                                    {
                                        writer.Write($" = ");
                                        if (value is string str)
                                        {
                                            writer.Write($"\"{str.ToEscapedString()}\"");
                                        }
                                        else if (value is char c)
                                        {
                                            var v = (int)c;
                                            writer.Write($"'\\x{v:x}'");
                                        }
                                        else if (value != null)
                                        {
                                            writer.Write($"{value}");
                                        }
                                        else
                                        {
                                            writer.Write("null");
                                        }
                                    }
                                    else
                                    {
                                        writer.Write($" /*Metadata offset 0x{value:X}*/");
                                    }
                                }
                                if (config.DumpFieldOffset && !isConst)
                                    writer.Write("; // 0x{0:X}\n", il2Cpp.GetFieldOffsetFromIndex(typeDefIndex, i - typeDef.fieldStart, i, typeDef.IsValueType, isStatic));
                                else
                                    writer.Write(";\n");
                                }
                                catch (Exception ex)
                                {
                                    writer.Write($"\t/* Field error: {ex.Message} */\n");
                                }
                            }
                        }
                        //dump property
                        if (config.DumpProperty && typeDef.property_count > 0 && metadata.propertyDefs != null)
                        {
                            writer.Write("\n\t// Properties\n");
                            var propertyEnd = Math.Min(typeDef.propertyStart + typeDef.property_count, metadata.propertyDefs.Length);
                            for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                            {
                                try
                                {
                                    var propertyDef = metadata.propertyDefs[i];
                                    if (config.DumpAttribute)
                                    {
                                        writer.Write(GetCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, "\t"));
                                    }
                                    writer.Write("\t");
                                    if (propertyDef.get >= 0 && (typeDef.methodStart + propertyDef.get) < metadata.methodDefs.Length)
                                    {
                                        var methodDef = metadata.methodDefs[typeDef.methodStart + propertyDef.get];
                                        writer.Write(GetModifiers(methodDef));
                                        var propertyType = il2Cpp.SafeGetType(methodDef.returnType);
                                        writer.Write($"{executor.GetTypeName(propertyType, false, false)} {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                    }
                                    else if (propertyDef.set >= 0 && (typeDef.methodStart + propertyDef.set) < metadata.methodDefs.Length)
                                    {
                                        var methodDef = metadata.methodDefs[typeDef.methodStart + propertyDef.set];
                                        writer.Write(GetModifiers(methodDef));
                                        if (methodDef.parameterStart >= 0 && methodDef.parameterStart < metadata.parameterDefs.Length)
                                        {
                                            var parameterDef = metadata.parameterDefs[methodDef.parameterStart];
                                            var propertyType = il2Cpp.SafeGetType(parameterDef.typeIndex);
                                            writer.Write($"{executor.GetTypeName(propertyType, false, false)} {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                        }
                                        else
                                        {
                                            writer.Write($"object {metadata.GetStringFromIndex(propertyDef.nameIndex)} {{ ");
                                        }
                                    }
                                    if (propertyDef.get >= 0)
                                        writer.Write("get; ");
                                    if (propertyDef.set >= 0)
                                        writer.Write("set; ");
                                    writer.Write("}");
                                    writer.Write("\n");
                                }
                                catch (Exception ex)
                                {
                                    writer.Write($"\t/* Property error: {ex.Message} */\n");
                                }
                            }
                        }
                        //dump method
                        writer.Write("\n\t// Methods\n");
                        if (config.DumpMethod && typeDef.method_count > 0 && metadata.methodDefs != null)
                        {
                            var methodEnd = Math.Min(typeDef.methodStart + typeDef.method_count, metadata.methodDefs.Length);
                            for (var i = typeDef.methodStart; i < methodEnd; ++i)
                            {
                                try
                                {
                                    var methodDef = metadata.methodDefs[i];
                                    var isAbstract = (methodDef.flags & METHOD_ATTRIBUTE_ABSTRACT) != 0;
                                    if (config.DumpAttribute)
                                    {
                                        writer.Write(GetCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, "\t"));
                                    }
                                    if (config.DumpMethodOffset)
                                    {
                                        var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                                        if (!isAbstract && methodPointer > 0)
                                        {
                                            var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                            writer.Write("\t// RVA: 0x{0:x} Offset: 0x{1:x} VA: 0x{2:x}\n", fixedMethodPointer, il2Cpp.MapVATR(methodPointer), methodPointer);
                                        }
                                    }
                                    writer.Write("\t");
                                    writer.Write(GetModifiers(methodDef));
                                    var methodReturnType = il2Cpp.SafeGetType(methodDef.returnType);
                                    var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                                    if (methodDef.genericContainerIndex >= 0 && metadata.genericContainers != null && methodDef.genericContainerIndex < metadata.genericContainers.Length)
                                    {
                                        var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                                        methodName += executor.GetGenericContainerParams(genericContainer);
                                    }
                                    if (methodReturnType.byref == 1)
                                    {
                                        writer.Write("ref ");
                                    }
                                    writer.Write($"{executor.GetTypeName(methodReturnType, false, false)} {methodName}(");
                                    var parameterStrs = new List<string>();
                                    int safeParamCount = Math.Min(Math.Max(0, (int)methodDef.parameterCount), 64);
                                    for (var j = 0; j < safeParamCount; ++j)
                                    {
                                        var paramIdx = methodDef.parameterStart + j;
                                        if (metadata.parameterDefs != null && paramIdx >= 0 && paramIdx < metadata.parameterDefs.Length)
                                        {
                                            var parameterStr = "";
                                            var parameterDef = metadata.parameterDefs[paramIdx];
                                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                                            var parameterType = il2Cpp.SafeGetType(parameterDef.typeIndex);
                                            var parameterTypeName = executor.GetTypeName(parameterType, false, false);
                                            if (parameterType.byref == 1)
                                            {
                                                if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) == 0)
                                                {
                                                    parameterStr += "out ";
                                                }
                                                else if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) == 0 && (parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                                {
                                                    parameterStr += "in ";
                                                }
                                                else
                                                {
                                                    parameterStr += "ref ";
                                                }
                                            }
                                            else
                                            {
                                                if ((parameterType.attrs & PARAM_ATTRIBUTE_IN) != 0)
                                                {
                                                    parameterStr += "[In] ";
                                                }
                                                if ((parameterType.attrs & PARAM_ATTRIBUTE_OUT) != 0)
                                                {
                                                    parameterStr += "[Out] ";
                                                }
                                            }
                                            parameterStr += $"{parameterTypeName} {parameterName}";
                                            if (metadata.GetParameterDefaultValueFromIndex(paramIdx, out var parameterDefault) && parameterDefault.dataIndex != -1)
                                            {
                                                if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var value))
                                                {
                                                    parameterStr += " = ";
                                                    if (value is string str)
                                                    {
                                                        parameterStr += $"\"{str.ToEscapedString()}\"";
                                                    }
                                                    else if (value is char c)
                                                    {
                                                        var v = (int)c;
                                                        parameterStr += $"'\\x{v:x}'";
                                                    }
                                                    else if (value != null)
                                                    {
                                                        parameterStr += $"{value}";
                                                    }
                                                    else
                                                    {
                                                        writer.Write("null");
                                                    }
                                                }
                                                else
                                                {
                                                    parameterStr += $" /*Metadata offset 0x{value:X}*/";
                                                }
                                            }
                                            parameterStrs.Add(parameterStr);
                                        }
                                    }
                                    writer.Write(string.Join(", ", parameterStrs));
                                    if (isAbstract)
                                    {
                                        writer.Write(");\n");
                                    }
                                    else
                                    {
                                        writer.Write(") { }\n");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    writer.Write($"\t/* Method error: {ex.Message} */\n");
                                }
                            }
                        }
                        writer.Write("}\n");
                      }
                      catch (Exception ex)
                      {
                          writer.Write($"\n/* Failed to dump typeDef {typeDefIndex}: {ex.Message} */\n}}\n");
                      }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Some errors in dumping");
                    writer.Write("/*");
                    writer.Write(e);
                    writer.Write("*/\n}\n");
                }
            }
            DrawProgressBar("Descompilando classes", 100);
            Console.WriteLine();
            writer.Close();
        }

        private static void DrawProgressBar(string taskName, int percent)
        {
            try
            {
                if (percent < 0) percent = 0;
                if (percent > 100) percent = 100;
                const int totalBlocks = 25;
                int filled = (int)Math.Round((double)percent * totalBlocks / 100);
                string bar = new string('#', filled) + new string('-', totalBlocks - filled);
                Console.Write($"\r [INFO] {taskName}: [{bar}] {percent,3}%   ");
            }
            catch { }
        }

        public string GetCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, string padding = "")
        {
            if (il2Cpp.Version < 21)
                return string.Empty;
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                if (il2Cpp.Version < 29)
                {
                    var methodPointer = executor.customAttributeGenerators[attributeIndex];
                    var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                    var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                    var sb = new StringBuilder();
                    for (var i = 0; i < attributeTypeRange.count; i++)
                    {
                        var typeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                        sb.AppendFormat("{0}[{1}] // RVA: 0x{2:X} Offset: 0x{3:X} VA: 0x{4:X}\n",
                            padding,
                            executor.GetTypeName(il2Cpp.types[typeIndex], false, false),
                            fixedMethodPointer,
                            il2Cpp.MapVATR(methodPointer),
                            methodPointer);
                    }
                    return sb.ToString();
                }
                else
                {
                    var startRange = metadata.attributeDataRanges[attributeIndex];
                    var endRange = metadata.attributeDataRanges[attributeIndex + 1];
                    metadata.Position = metadata.header.attributeDataOffset + startRange.startOffset;
                    var buff = metadata.ReadBytes((int)(endRange.startOffset - startRange.startOffset));
                    var reader = new CustomAttributeDataReader(executor, buff);
                    if (reader.Count == 0)
                    {
                        return string.Empty;
                    }
                    var sb = new StringBuilder();
                    for (var i = 0; i < reader.Count; i++)
                    {
                        sb.Append(padding);
                        sb.Append(reader.GetStringCustomAttributeData());
                        sb.Append('\n');
                    }
                    return sb.ToString();
                }
            }
            else
            {
                return string.Empty;
            }
        }

        public string GetModifiers(Il2CppMethodDefinition methodDef)
        {
            if (methodModifiers.TryGetValue(methodDef, out string str))
                return str;
            var access = methodDef.flags & METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK;
            switch (access)
            {
                case METHOD_ATTRIBUTE_PRIVATE:
                    str += "private ";
                    break;
                case METHOD_ATTRIBUTE_PUBLIC:
                    str += "public ";
                    break;
                case METHOD_ATTRIBUTE_FAMILY:
                    str += "protected ";
                    break;
                case METHOD_ATTRIBUTE_ASSEM:
                case METHOD_ATTRIBUTE_FAM_AND_ASSEM:
                    str += "internal ";
                    break;
                case METHOD_ATTRIBUTE_FAM_OR_ASSEM:
                    str += "protected internal ";
                    break;
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_STATIC) != 0)
                str += "static ";
            if ((methodDef.flags & METHOD_ATTRIBUTE_ABSTRACT) != 0)
            {
                str += "abstract ";
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_FINAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_REUSE_SLOT)
                    str += "sealed override ";
            }
            else if ((methodDef.flags & METHOD_ATTRIBUTE_VIRTUAL) != 0)
            {
                if ((methodDef.flags & METHOD_ATTRIBUTE_VTABLE_LAYOUT_MASK) == METHOD_ATTRIBUTE_NEW_SLOT)
                    str += "virtual ";
                else
                    str += "override ";
            }
            if ((methodDef.flags & METHOD_ATTRIBUTE_PINVOKE_IMPL) != 0)
                str += "extern ";
            methodModifiers.Add(methodDef, str);
            return str;
        }
    }
}
