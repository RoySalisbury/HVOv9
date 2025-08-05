using Microsoft.CodeAnalysis;
using System;
using System.Linq;
using System.Text;

namespace HVO
{
    [Generator]
    public class NamedOneOfGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new NamedOneOfReceiver());

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not NamedOneOfReceiver receiver)
                return;

            foreach (var candidate in receiver.Candidates)
            {
                var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
                if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
                    continue;

                // Find attribute by fully qualified name
                var attr = symbol.GetAttributes()
                                 .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "HVO.NamedOneOfAttribute");
                if (attr == null) continue;

                // Extract arguments - handle both typeof() expressions and strings
                if (attr.ConstructorArguments.Length == 0) continue;
                
                var arrayArgs = attr.ConstructorArguments[0].Values;
                if (arrayArgs.Length % 2 != 0) continue;

                var cases = new (string Name, string TypeName)[arrayArgs.Length / 2];

                for (int i = 0; i < arrayArgs.Length; i += 2)
                {
                    var name = arrayArgs[i].Value?.ToString() ?? string.Empty;
                    
                    // Handle both typeof() and string type names
                    string typeName;
                    var typeArg = arrayArgs[i + 1];
                    
                    if (typeArg.Value is ITypeSymbol typeSymbol)
                    {
                        // Handle typeof() expressions
                        typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    else
                    {
                        // Handle string type names
                        typeName = typeArg.Value?.ToString() ?? string.Empty;
                    }
                    
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(typeName))
                        continue;

                    cases[i / 2] = (name, typeName);
                }

                var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
                var code = GenerateOneOfClass(symbol.Name, namespaceName, cases);
                context.AddSource($"{symbol.Name}_NamedOneOf.g.cs", code);
            }
        }

        private string GenerateOneOfClass(string className, string namespaceName, (string Name, string TypeName)[] cases)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            var indent = !string.IsNullOrEmpty(namespaceName) ? "    " : "";

            sb.AppendLine($"{indent}[JsonConverter(typeof({className}JsonConverter))]");
            sb.AppendLine($"{indent}public partial class {className} : global::HVO.IOneOf");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    private readonly object? _value;");
            sb.AppendLine($"{indent}    private readonly int _index;");
            sb.AppendLine();

            // Public default constructor for fallback cases
            sb.AppendLine($"{indent}    public {className}()");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        _value = null;");
            sb.AppendLine($"{indent}        _index = -1;");
            sb.AppendLine($"{indent}        RawJson = null;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine();

            // Constructors
            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                var cleanTypeName = c.TypeName.Replace("global::", "");
                sb.AppendLine($"{indent}    public {className}({cleanTypeName} value) => (_value, _index, RawJson) = (value!, {i}, null);");
            }

            sb.AppendLine();

            // Is{CaseName} properties
            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                sb.AppendLine($"{indent}    public bool Is{c.Name} => _index == {i};");
            }

            sb.AppendLine();

            // As{CaseName} properties
            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                var cleanTypeName = c.TypeName.Replace("global::", "");
                sb.AppendLine($"{indent}    public {cleanTypeName} As{c.Name} => _index == {i} ? ({cleanTypeName})_value! : throw new InvalidOperationException(\"Value is not {c.Name}\");");
            }

            sb.AppendLine();

            // IOneOf interface implementation
            sb.AppendLine($"{indent}    public object? Value => _value;");
            sb.AppendLine($"{indent}    public Type? ValueType => _value?.GetType();");
            sb.AppendLine($"{indent}    public JsonElement? RawJson {{ get; set; }}");

            sb.AppendLine();
            sb.AppendLine($"{indent}    public override string ToString() => _value?.ToString() ?? \"<null>\";");

            sb.AppendLine();

            // Implicit operators
            foreach (var c in cases)
            {
                var cleanTypeName = c.TypeName.Replace("global::", "");
                sb.AppendLine($"{indent}    public static implicit operator {className}({cleanTypeName} value) => new(value);");
            }

            sb.AppendLine($"{indent}}}");

            // JSON converter
            sb.AppendLine();
            sb.AppendLine($"{indent}internal class {className}JsonConverter : JsonConverter<{className}>");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    public override {className} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        using var doc = JsonDocument.ParseValue(ref reader);");
            sb.AppendLine($"{indent}        var element = doc.RootElement.Clone();");
            sb.AppendLine();

            for (int i = 0; i < cases.Length; i++)
            {
                var c = cases[i];
                var cleanTypeName = c.TypeName.Replace("global::", "");
                sb.AppendLine($"{indent}        try");
                sb.AppendLine($"{indent}        {{");
                
                // Handle value types vs reference types differently for null check
                if (IsValueType(cleanTypeName))
                {
                    sb.AppendLine($"{indent}            var val = element.Deserialize<{cleanTypeName}>(options);");
                    sb.AppendLine($"{indent}            return new {className}(val) {{ RawJson = element }};");
                }
                else
                {
                    sb.AppendLine($"{indent}            var val = element.Deserialize<{cleanTypeName}>(options);");
                    sb.AppendLine($"{indent}            if (val != null) return new {className}(val) {{ RawJson = element }};");
                }
                
                sb.AppendLine($"{indent}        }}");
                sb.AppendLine($"{indent}        catch {{ }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}        // Fallback: create empty instance with raw JSON");
            sb.AppendLine($"{indent}        var fallback = new {className}();");
            sb.AppendLine($"{indent}        fallback.RawJson = element;");
            sb.AppendLine($"{indent}        return fallback;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine();
            sb.AppendLine($"{indent}    public override void Write(Utf8JsonWriter writer, {className} value, JsonSerializerOptions options)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if (value.Value != null)");
            sb.AppendLine($"{indent}            JsonSerializer.Serialize(writer, value.Value, value.ValueType!, options);");
            sb.AppendLine($"{indent}        else if (value.RawJson.HasValue)");
            sb.AppendLine($"{indent}            value.RawJson.Value.WriteTo(writer);");
            sb.AppendLine($"{indent}        else");
            sb.AppendLine($"{indent}            writer.WriteNullValue();");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static bool IsValueType(string typeName)
        {
            // Clean up fully qualified names for comparison
            var cleanTypeName = typeName.Replace("global::", "").Replace("System.", "");
            
            // Common value types
            return cleanTypeName switch
            {
                "int" or "Int32" => true,
                "long" or "Int64" => true,
                "double" or "Double" => true,
                "float" or "Single" => true,
                "bool" or "Boolean" => true,
                "DateTime" => true,
                "DateTimeOffset" => true,
                "TimeSpan" => true,
                "Guid" => true,
                "decimal" or "Decimal" => true,
                _ => false
            };
        }
    }
}
