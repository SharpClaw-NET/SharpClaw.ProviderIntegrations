using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.Providers.Google;
using SharpClaw.Modules.Providers.Google.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class GoogleGeminiApiClientBehaviorTests
{
    [Test]
    public async Task ChatCompletionAsync_MovesTopLevelResponseMimeTypeIntoGenerationConfig()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["response_mime_type"] = JsonSerializer.SerializeToElement("application/json")
        };

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("response_mime_type", out _), Is.False);
            Assert.That(
                doc.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString(),
                Is.EqualTo("application/json"));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_MergesGenerationConfigAndNormalizesSnakeCaseKeys()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["generation_config"] = JsonSerializer.SerializeToElement(new
            {
                response_mime_type = "application/json",
                candidate_count = 2,
                temperature = 1.2
            })
        };

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters,
            completionParameters: new CompletionParameters { Temperature = 0.4f });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("generation_config", out _), Is.False);
            Assert.That(generationConfig.GetProperty("responseMimeType").GetString(), Is.EqualTo("application/json"));
            Assert.That(generationConfig.GetProperty("candidateCount").GetInt32(), Is.EqualTo(2));
            Assert.That(generationConfig.GetProperty("temperature").GetDouble(), Is.EqualTo(0.4).Within(0.000001));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_TypedResponseFormatTakesPrecedenceOverProviderParameter()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["response_mime_type"] = JsonSerializer.SerializeToElement("application/json")
        };

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters,
            completionParameters: new CompletionParameters
            {
                ResponseFormat = JsonSerializer.SerializeToElement(new { type = "text" })
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.That(
            doc.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString(),
            Is.EqualTo("text/plain"));
    }

    [Test]
    public async Task ChatCompletionAsync_MapsNativePenaltyParameters()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            completionParameters: new CompletionParameters
            {
                PresencePenalty = 0.25f,
                FrequencyPenalty = -0.5f
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        Assert.Multiple(() =>
        {
            Assert.That(generationConfig.GetProperty("presencePenalty").GetDouble(), Is.EqualTo(0.25).Within(0.000001));
            Assert.That(generationConfig.GetProperty("frequencyPenalty").GetDouble(), Is.EqualTo(-0.5).Within(0.000001));
        });
    }

    [Test]
    public async Task ChatCompletionWithToolsAsync_MapsToolChoiceToToolConfig()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);

        await client.ChatCompletionWithToolsAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ToolAwareMessage { Role = "user", Content = "Hello" }],
            tools: [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            completionParameters: new CompletionParameters
            {
                ToolChoice = ToolChoice.ForFunction("lookup")
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var functionCallingConfig = doc.RootElement
            .GetProperty("toolConfig")
            .GetProperty("functionCallingConfig");

        Assert.Multiple(() =>
        {
            Assert.That(functionCallingConfig.GetProperty("mode").GetString(), Is.EqualTo("ANY"));
            Assert.That(
                functionCallingConfig.GetProperty("allowedFunctionNames").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(new[] { "lookup" }));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_StillPassesUnknownNativeRootParameters()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["safetySettings"] = JsonSerializer.SerializeToElement(new[]
            {
                new
                {
                    category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                    threshold = "BLOCK_ONLY_HIGH"
                }
            })
        };

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.That(
            doc.RootElement.GetProperty("safetySettings").EnumerateArray().Single().GetProperty("threshold").GetString(),
            Is.EqualTo("BLOCK_ONLY_HIGH"));
    }

    [Test]
    public void ChatCompletionAsync_RejectsNonObjectGenerationConfigProviderParameter()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleGeminiApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["generation_config"] = JsonSerializer.SerializeToElement("application/json")
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters));

        Assert.That(
            exception?.Message,
            Is.EqualTo("Google Gemini provider parameter 'generation_config' must be a JSON object."));
    }

    private static JsonElement EmptyObjectSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>()
        });
}

[TestFixture]
public sealed class GoogleVertexAIApiClientBehaviorTests
{
    [Test]
    public async Task ChatCompletionAsync_UsesProjectLocationEndpointWithBearerAuth()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleVertexAIApiClient(
            "https://europe-west4-aiplatform.googleapis.com/v1/projects/test-project/locations/europe-west4",
            "test-token",
            httpClient);

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            [new ChatCompletionMessage("user", "Hello")],
            completionParameters: new CompletionParameters
            {
                PresencePenalty = 0.25f,
                FrequencyPenalty = -0.5f
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        Assert.Multiple(() =>
        {
            Assert.That(
                handler.LastRequestUri?.ToString(),
                Is.EqualTo("https://europe-west4-aiplatform.googleapis.com/v1/projects/test-project/locations/europe-west4/publishers/google/models/gemini-test:generateContent"));
            Assert.That(handler.LastAuthorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastAuthorization?.Parameter, Is.EqualTo("test-token"));
            Assert.That(generationConfig.GetProperty("presencePenalty").GetDouble(), Is.EqualTo(0.25).Within(0.000001));
            Assert.That(generationConfig.GetProperty("frequencyPenalty").GetDouble(), Is.EqualTo(-0.5).Within(0.000001));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_UsesFullyQualifiedModelWithDefaultVersionRoot()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleVertexAIApiClient(apiKey: "Bearer test-token", httpClient: httpClient);

        await client.ChatCompletionAsync(
            "projects/test-project/locations/us-central1/publishers/google/models/gemini-test",
            systemPrompt: null,
            [new ChatCompletionMessage("user", "Hello")]);

        Assert.Multiple(() =>
        {
            Assert.That(
                handler.LastRequestUri?.ToString(),
                Is.EqualTo("https://aiplatform.googleapis.com/v1/projects/test-project/locations/us-central1/publishers/google/models/gemini-test:generateContent"));
            Assert.That(handler.LastAuthorization?.Parameter, Is.EqualTo("test-token"));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_NormalizesProviderParametersToVertexRequestShape()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleVertexAIApiClient(
            "https://us-central1-aiplatform.googleapis.com/v1/projects/test-project/locations/us-central1",
            "test-token",
            httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["response_mime_type"] = JsonSerializer.SerializeToElement("application/json"),
            ["generation_config"] = JsonSerializer.SerializeToElement(new
            {
                audio_timestamp = true,
                routing_config = new
                {
                    autoMode = new
                    {
                        modelRoutingPreference = "BALANCED"
                    }
                }
            }),
            ["model_armor_config"] = JsonSerializer.SerializeToElement(new
            {
                someOption = true
            }),
            ["labels"] = JsonSerializer.SerializeToElement(new
            {
                workload = "sharpclaw"
            })
        };

        await client.ChatCompletionAsync(
            "gemini-test",
            systemPrompt: null,
            [new ChatCompletionMessage("user", "Hello")],
            providerParameters: providerParameters);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var generationConfig = doc.RootElement.GetProperty("generationConfig");
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.TryGetProperty("response_mime_type", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("generation_config", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("model_armor_config", out _), Is.False);
            Assert.That(doc.RootElement.GetProperty("modelArmorConfig").GetProperty("someOption").GetBoolean(), Is.True);
            Assert.That(doc.RootElement.GetProperty("labels").GetProperty("workload").GetString(), Is.EqualTo("sharpclaw"));
            Assert.That(generationConfig.GetProperty("responseMimeType").GetString(), Is.EqualTo("application/json"));
            Assert.That(generationConfig.GetProperty("audioTimestamp").GetBoolean(), Is.True);
            Assert.That(
                generationConfig.GetProperty("routingConfig").GetProperty("autoMode").GetProperty("modelRoutingPreference").GetString(),
                Is.EqualTo("BALANCED"));
        });
    }

    [Test]
    public async Task ChatCompletionWithToolsAsync_MapsToolChoiceToToolConfig()
    {
        using var handler = new GoogleCaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new GoogleVertexAIApiClient(
            "https://us-central1-aiplatform.googleapis.com/v1/projects/test-project/locations/us-central1",
            "test-token",
            httpClient);

        await client.ChatCompletionWithToolsAsync(
            "gemini-test",
            systemPrompt: null,
            [new ToolAwareMessage { Role = "user", Content = "Hello" }],
            [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            completionParameters: new CompletionParameters
            {
                ToolChoice = ToolChoice.Required
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.That(
            doc.RootElement.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString(),
            Is.EqualTo("ANY"));
    }

    [Test]
    public async Task ListModelIdsAsync_ListsProjectModelsFromEndpoint()
    {
        using var handler = new GoogleCaptureHandler("""
            {
              "models": [
                { "name": "projects/test-project/locations/us-central1/models/custom-model" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new GoogleVertexAIApiClient(
            "https://us-central1-aiplatform.googleapis.com/v1/projects/test-project/locations/us-central1",
            "test-token",
            httpClient);

        var models = await client.ListModelIdsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                handler.LastRequestUri?.ToString(),
                Is.EqualTo("https://us-central1-aiplatform.googleapis.com/v1/projects/test-project/locations/us-central1/models"));
            Assert.That(models, Is.EqualTo(new[] { "custom-model" }));
        });
    }

    [Test]
    public void ModuleRegistersImplementedNativeVertexProvider()
    {
        var services = new ServiceCollection();
        new GoogleProvidersModule().ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var plugin = serviceProvider.GetServices<IProviderPlugin>()
            .Single(provider => provider.ProviderKey == "google-vertex-ai");
        var client = ProviderCredentialBinding.CreateClient(
            plugin,
            new ProviderClientOptions("https://us-central1-aiplatform.googleapis.com/v1/projects/p/locations/us-central1"),
            "test-token");

        Assert.Multiple(() =>
        {
            Assert.That(plugin.SupportsAutomaticEndpointDiscovery, Is.True);
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.GoogleVertexAI));
            Assert.That(client, Is.TypeOf<GoogleVertexAIApiClient>());
            Assert.That(client.SupportsNativeToolCalling, Is.True);
        });
    }

    private static JsonElement EmptyObjectSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>()
        });
}

internal sealed class GoogleCaptureHandler(string? responseBody = null) : HttpMessageHandler
{
    private const string CompletionResponse = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "ok" }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ],
          "usageMetadata": {
            "promptTokenCount": 1,
            "candidatesTokenCount": 1
          }
        }
        """;

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public AuthenticationHeaderValue? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody ?? CompletionResponse, Encoding.UTF8, "application/json")
        };
    }
}
