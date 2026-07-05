using System.Reflection;
using System.Text;
using System.Text.Json;
using LlamaSharp.ToolCallEnvelopes;
using NUnit.Framework;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.Providers.LlamaSharp.Clients;
using SharpClaw.Modules.Providers.LlamaSharp.LocalInference;
using SharpClaw.Providers.Common;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class GgufHeaderReaderBehaviorTests
{
    [Test]
    public async Task ReadArchitectureAsync_ValidGguf_ReturnsArchitecture()
    {
        var path = WriteGgufFile("llama");
        try
        {
            var result = await GgufHeaderReader.ReadArchitectureAsync(path);

            Assert.That(result, Is.EqualTo("llama"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadArchitectureAsync_NotGgufFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var result = await GgufHeaderReader.ReadArchitectureAsync(path);

            Assert.That(result, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadArchitectureAsync_EmptyFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, []);
        try
        {
            var result = await GgufHeaderReader.ReadArchitectureAsync(path);

            Assert.That(result, Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadArchitectureAsync_NonExistentFile_ReturnsNull()
    {
        var result = await GgufHeaderReader.ReadArchitectureAsync(
            Path.Combine(Path.GetTempPath(), "does_not_exist.gguf"));

        Assert.That(result, Is.Null);
    }

    private static string WriteGgufFile(string architectureValue)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".gguf");
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write("GGUF"u8.ToArray());
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)1);

        var key = Encoding.UTF8.GetBytes("general.architecture");
        var value = Encoding.UTF8.GetBytes(architectureValue);
        writer.Write((ulong)key.Length);
        writer.Write(key);
        writer.Write((uint)8);
        writer.Write((ulong)value.Length);
        writer.Write(value);

        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }
}

[TestFixture]
public sealed class LlamaSharpJsonSchemaConverterBehaviorTests
{
    [SetUp]
    public void ResetCache() => LlamaSharpJsonSchemaConverter.ResetCache();

    [Test]
    public void NonObjectRoot_IsRejected_WithPointerEntry()
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""[1, 2, 3]"""), out var gbnf, out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(gbnf, Is.Empty);
            Assert.That(unsupported, Has.Count.EqualTo(1));
            Assert.That(unsupported[0], Does.StartWith("/"));
        });
    }

    [Test]
    public void EmptyObjectSchema_ProducesGrammar()
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ }"""), out var gbnf, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("root ::= ws"));
            Assert.That(gbnf, Does.Contain("value"));
        });
    }

    [Test]
    public void ObjectSchema_WithProperties_EmitsNamedRules()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "age":  { "type": "integer" }
              },
              "required": ["name"]
            }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            schema, out var gbnf, out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("\\\"name\\\""));
            Assert.That(gbnf, Does.Contain("\\\"age\\\""));
            Assert.That(gbnf, Does.Contain("integer"));
            Assert.That(gbnf, Does.Contain("string"));
            Assert.That(unsupported, Is.Empty);
        });
    }

    [Test]
    public void ObjectSchema_WithAdditionalPropertiesFalse_ClosesObject()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "x": { "type": "string" } },
              "required": ["x"],
              "additionalProperties": false
            }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            schema, out var gbnf, out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("\\\"x\\\""));
            Assert.That(unsupported, Is.Empty);
        });
    }

    [Test]
    public void ObjectSchema_WithAdditionalPropertiesSubschema_EmitsExtraKvRule()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "x": { "type": "string" } },
              "required": ["x"],
              "additionalProperties": { "type": "number" }
            }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            schema, out var gbnf, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("number"));
        });
    }

    [Test]
    public void ArraySchema_HomogeneousItems()
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "array", "items": { "type": "string" } }"""),
            out var gbnf,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("string"));
            Assert.That(gbnf, Does.Contain("\"[\""));
        });
    }

    [Test]
    public void ArraySchema_TupleItems()
    {
        var schema = Parse("""
            {
              "type": "array",
              "items": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            schema, out var gbnf, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("string"));
            Assert.That(gbnf, Does.Contain("integer"));
        });
    }

    [Test]
    public void ArraySchema_MinItemsMaxItemsHonoured()
    {
        var schema = Parse("""
            { "type": "array", "items": { "type": "integer" },
              "minItems": 2, "maxItems": 3 }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            schema, out var gbnf, out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(unsupported, Is.Empty);
            Assert.That(gbnf, Does.Contain("integer"));
        });
    }

    [Test]
    public void ArraySchema_LargeMinItems_Relaxed()
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "array", "items": { "type": "integer" }, "minItems": 100 }"""),
            out _,
            out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(unsupported, Has.Some.Contains("minItems"));
        });
    }

    [Test]
    public void PrimitiveTypes_EmitExpectedRules()
    {
        Assert.Multiple(() =>
        {
            AssertConvertedGrammarContains("""{ "type": "string" }""", "string");
            AssertConvertedGrammarContains("""{ "type": "boolean" }""", "boolean");
            AssertConvertedGrammarContains("""{ "type": "null" }""", "null-lit");
            AssertConvertedGrammarContains("""{ "type": ["string", "null"] }""", "null-lit");
        });
    }

    [Test]
    public void StringPattern_SupportedAndUnsupportedPatternsAreTracked()
    {
        var supported = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "string", "pattern": "^foo$" }"""),
            out var gbnf,
            out var supportedUnsupported);
        var unsupported = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "string", "pattern": "^(a)\\1$" }"""),
            out _,
            out var unsupportedEntries);

        Assert.Multiple(() =>
        {
            Assert.That(supported, Is.True);
            Assert.That(supportedUnsupported, Is.Empty);
            Assert.That(gbnf, Does.Contain("\"f\" \"o\" \"o\""));
            Assert.That(unsupported, Is.True);
            Assert.That(unsupportedEntries, Has.Some.Contains("pattern"));
        });
    }

    [Test]
    public void IntegerRanges_EnforceSmallRangeAndRelaxWideRange()
    {
        var small = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "integer", "minimum": 1, "maximum": 5 }"""),
            out var smallGbnf,
            out var smallUnsupported);
        var wide = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "integer", "minimum": -100000, "maximum": 100000 }"""),
            out _,
            out var wideUnsupported);

        Assert.Multiple(() =>
        {
            Assert.That(small, Is.True);
            Assert.That(smallUnsupported, Is.Empty);
            Assert.That(smallGbnf, Does.Contain("\"1\""));
            Assert.That(smallGbnf, Does.Contain("\"5\""));
            Assert.That(wide, Is.True);
            Assert.That(wideUnsupported.Any(entry => entry.Contains("minimum", StringComparison.Ordinal) || entry.Contains("maximum", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void EnumAndConst_EmitLiteralAlternation()
    {
        Assert.Multiple(() =>
        {
            AssertConvertedGrammarContains("""{ "enum": ["red", "green", "blue"] }""", "\\\"red\\\"");
            AssertConvertedGrammarContains("""{ "enum": ["red", "green", "blue"] }""", "\\\"green\\\"");
            AssertConvertedGrammarContains("""{ "const": "fixed-value" }""", "\\\"fixed-value\\\"");
            AssertConvertedGrammarContains("""{ "enum": ["a", 1, true, null] }""", "null");
        });
    }

    [Test]
    public void CompositionKeywords_ConvertOrTrackDegradedSemantics()
    {
        AssertConvertedGrammarContains(
            """{ "anyOf": [ { "type": "string" }, { "type": "integer" } ] }""",
            "integer");

        var oneOf = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "oneOf": [ { "type": "string" }, { "type": "integer" } ] }"""),
            out _,
            out var oneOfUnsupported);

        var allOf = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""
                {
                  "allOf": [
                    { "type": "object", "properties": { "a": { "type": "string" } }, "required": ["a"] },
                    { "type": "object", "properties": { "b": { "type": "integer" } }, "required": ["b"] }
                  ]
                }
                """),
            out var allOfGbnf,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(oneOf, Is.True);
            Assert.That(oneOfUnsupported, Has.Some.Contains("oneOf"));
            Assert.That(allOf, Is.True);
            Assert.That(allOfGbnf, Does.Contain("\\\"a\\\""));
            Assert.That(allOfGbnf, Does.Contain("\\\"b\\\""));
        });
    }

    [Test]
    public void LocalRefsResolveAndNonLocalRefsAreTracked()
    {
        var local = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""
                {
                  "type": "object",
                  "properties": { "item": { "$ref": "#/$defs/Item" } },
                  "required": ["item"],
                  "$defs": {
                    "Item": { "type": "object", "properties": { "id": { "type": "integer" } }, "required": ["id"] }
                  }
                }
                """),
            out var localGbnf,
            out var localUnsupported);

        var nonLocal = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "$ref": "https://example.com/schema.json" }"""),
            out _,
            out var nonLocalUnsupported);

        Assert.Multiple(() =>
        {
            Assert.That(local, Is.True);
            Assert.That(localUnsupported, Is.Empty);
            Assert.That(localGbnf, Does.Contain("\\\"id\\\""));
            Assert.That(nonLocal, Is.True);
            Assert.That(nonLocalUnsupported, Has.Some.Contains("$ref"));
        });
    }

    [Test]
    public void RecursiveRef_CompilesWithoutStackOverflow()
    {
        var schema = Parse("""
            {
              "$ref": "#/$defs/Node",
              "$defs": {
                "Node": {
                  "type": "object",
                  "properties": {
                    "value": { "type": "integer" },
                    "child": { "$ref": "#/$defs/Node" }
                  },
                  "required": ["value"]
                }
              }
            }
            """);

        var ok = LlamaSharpJsonSchemaConverter.TryConvert(schema, out var gbnf, out _);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(gbnf, Does.Contain("\\\"value\\\""));
            Assert.That(gbnf, Does.Contain("\\\"child\\\""));
        });
    }

    [TestCase("""{ "type": "object", "patternProperties": { "^f": { "type": "string" } } }""", "patternProperties")]
    [TestCase("""{ "type": "object", "propertyNames": { "minLength": 1 } }""", "propertyNames")]
    [TestCase("""{ "type": "object", "minProperties": 1 }""", "minProperties")]
    [TestCase("""{ "type": "object", "maxProperties": 10 }""", "maxProperties")]
    [TestCase("""{ "type": "array", "uniqueItems": true }""", "uniqueItems")]
    [TestCase("""{ "type": "array", "contains": { "type": "integer" } }""", "contains")]
    [TestCase("""{ "not": { "type": "string" } }""", "not")]
    [TestCase("""{ "if": { "type": "string" }, "then": { "minLength": 1 } }""", "if")]
    public void TrackedUnsupportedKeywords_AppearInUnsupportedChannel(string json, string keyword)
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse(json), out _, out var unsupported);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(unsupported, Has.Some.Contains(keyword));
        });
    }

    [Test]
    public void EveryConversion_ProducesRootRuleAndPrimitiveFragment()
    {
        LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "object", "properties": { "x": { "type": "string" } } }"""),
            out var objectGbnf,
            out _);
        LlamaSharpJsonSchemaConverter.TryConvert(
            Parse("""{ "type": "boolean" }"""),
            out var booleanGbnf,
            out _);

        Assert.Multiple(() =>
        {
            Assert.That(objectGbnf, Does.StartWith("root ::="));
            Assert.That(booleanGbnf, Does.Contain("boolean  ::="));
            Assert.That(booleanGbnf, Does.Contain("ws       ::="));
        });
    }

    [Test]
    public void Cache_ReturnsIdenticalGrammarForSameSchema()
    {
        var schema = Parse("""{ "type": "object", "properties": { "x": { "type": "string" } } }""");

        LlamaSharpJsonSchemaConverter.TryConvert(schema, out var first, out _);
        LlamaSharpJsonSchemaConverter.TryConvert(schema, out var second, out _);

        Assert.That(second, Is.EqualTo(first));
    }

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static void AssertConvertedGrammarContains(string json, string expected)
    {
        var ok = LlamaSharpJsonSchemaConverter.TryConvert(
            Parse(json), out var gbnf, out _);

        Assert.That(ok, Is.True);
        Assert.That(gbnf, Does.Contain(expected));
    }
}

[TestFixture]
public sealed class LlamaSharpResponseFormatDispatchBehaviorTests
{
    private static readonly MethodInfo Resolve =
        typeof(LocalInferenceApiClient).GetMethod(
            "ResolveResponseFormatGrammar",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void Dispatch_JsonObject_ReturnsGrammar()
    {
        var grammar = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""{ "type": "json_object" }""")
        });

        Assert.That(grammar, Is.Not.Null);
    }

    [Test]
    public void Dispatch_JsonSchema_TrivialAndNonTrivialObjects_ReturnGrammar()
    {
        var trivial = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""
                {
                  "type": "json_schema",
                  "json_schema": {
                    "name": "trivial",
                    "schema": { "type": "object" }
                  }
                }
                """)
        });
        var nonTrivial = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""
                {
                  "type": "json_schema",
                  "json_schema": {
                    "name": "person",
                    "schema": {
                      "type": "object",
                      "properties": { "name": { "type": "string" } },
                      "required": ["name"]
                    }
                  }
                }
                """)
        });

        Assert.Multiple(() =>
        {
            Assert.That(trivial, Is.Not.Null);
            Assert.That(nonTrivial, Is.Not.Null);
        });
    }

    [Test]
    public void Dispatch_MalformedJsonSchema_FallsBackWithoutThrowing()
    {
        var missingSchema = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""
                {
                  "type": "json_schema",
                  "json_schema": { "name": "broken" }
                }
                """)
        });
        var missingWrapper = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""{ "type": "json_schema" }""")
        });

        Assert.Multiple(() =>
        {
            Assert.That(missingSchema, Is.Not.Null);
            Assert.That(missingWrapper, Is.Not.Null);
        });
    }

    [Test]
    public void Dispatch_NullOrUnknownResponseFormat_ReturnsNull()
    {
        var nullFormat = Invoke(new CompletionParameters { ResponseFormat = null });
        var unknownType = Invoke(new CompletionParameters
        {
            ResponseFormat = Parse("""{ "type": "text" }""")
        });

        Assert.Multiple(() =>
        {
            Assert.That(nullFormat, Is.Null);
            Assert.That(unknownType, Is.Null);
        });
    }

    private static object? Invoke(CompletionParameters parameters)
        => Resolve.Invoke(null, [parameters]);

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}

[TestFixture]
public sealed class LlamaSharpParameterSurfaceBehaviorTests
{
    [Test]
    public void Spec_LlamaSharp_ClaimsParityOnStopAndSeed()
    {
        var spec = ProviderParameterSpecs.LlamaSharp;

        Assert.Multiple(() =>
        {
            Assert.That(spec.SupportsStop, Is.True);
            Assert.That(spec.MaxStopSequences, Is.EqualTo(16));
            Assert.That(spec.SupportsSeed, Is.True);
        });
    }

    [Test]
    public void JsonObjectGrammar_IsCachedAndDeclaresCoreRules()
    {
        var first = LlamaSharpJsonGrammars.JsonObject();
        var second = LlamaSharpJsonGrammars.JsonObject();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Empty);
            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(first, Does.Contain("root"));
            Assert.That(first, Does.Contain("value"));
            Assert.That(first, Does.Contain("obj"));
            Assert.That(first, Does.Contain("arr"));
            Assert.That(first, Does.Contain("string"));
            Assert.That(first, Does.Contain("number"));
            Assert.That(first, Does.Contain("\"true\""));
            Assert.That(first, Does.Contain("\"false\""));
            Assert.That(first, Does.Contain("\"null\""));
        });
    }

    [Test]
    public void Spec_LlamaSharp_ResponseFormatAcceptsBothShapes()
    {
        var spec = ProviderParameterSpecs.LlamaSharp;

        Assert.Multiple(() =>
        {
            Assert.That(spec.SupportsResponseFormat, Is.True);
            Assert.That(spec.OnlyJsonObjectResponseFormat, Is.False);
        });
    }

    [Test]
    public void Spec_LlamaSharp_ReasoningEffortIsInformationalOnly()
    {
        var llamaSharp = ProviderParameterSpecs.LlamaSharp;
        var openAi = ProviderParameterSpecs.OpenAI;

        Assert.Multiple(() =>
        {
            Assert.That(llamaSharp.SupportsReasoningEffort, Is.True);
            Assert.That(llamaSharp.ReasoningEffortInformationalOnly, Is.True);
            Assert.That(openAi.ReasoningEffortInformationalOnly, Is.False);
        });
    }
}
