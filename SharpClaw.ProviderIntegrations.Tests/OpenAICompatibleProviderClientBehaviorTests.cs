using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SharpClaw.Contracts.Models;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.Providers.OpenAICompatible;
using SharpClaw.Modules.Providers.OpenAICompatible.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class DeepSeekApiClientBehaviorTests
{
    [Test]
    public async Task ChatCompletionAsync_DefaultsThinkingModeDisabled()
    {
        using var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);

        var result = await client.ChatCompletionAsync(
            "deepseek-v4-flash",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Hello")]);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Is.EqualTo("ok"));
            Assert.That(handler.LastRequestUri?.ToString(), Is.EqualTo("https://api.deepseek.com/chat/completions"));
            Assert.That(doc.RootElement.GetProperty("thinking").GetProperty("type").GetString(), Is.EqualTo("disabled"));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_PreservesExplicitThinkingMode()
    {
        using var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["thinking"] = JsonSerializer.SerializeToElement(new { type = "enabled" })
        };

        await client.ChatCompletionAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Think this through")],
            providerParameters: providerParameters);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.That(doc.RootElement.GetProperty("thinking").GetProperty("type").GetString(), Is.EqualTo("enabled"));
    }

    [Test]
    public async Task ChatCompletionAsync_EnablesThinkingWhenReasoningEffortIsSet()
    {
        using var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);

        await client.ChatCompletionAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Think this through")],
            completionParameters: new CompletionParameters { ReasoningEffort = "high" });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.GetProperty("thinking").GetProperty("type").GetString(), Is.EqualTo("enabled"));
            Assert.That(doc.RootElement.GetProperty("reasoning_effort").GetString(), Is.EqualTo("high"));
        });
    }

    [Test]
    public async Task ChatCompletionAsync_ReplaysReasoningContentFromHistory()
    {
        using var handler = new CaptureHandler(ReasonedCompletionResponse, """
            {
              "choices": [
                {
                  "message": { "role": "assistant", "content": "next" },
                  "finish_reason": "stop"
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["thinking"] = JsonSerializer.SerializeToElement(new { type = "enabled" })
        };

        var first = await client.ChatCompletionAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ChatCompletionMessage("user", "Think this through")],
            providerParameters: providerParameters);

        var second = await client.ChatCompletionAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages:
            [
                new ChatCompletionMessage("assistant", first.Content!)
                {
                    ProviderMetadataJson = first.ProviderMetadataJson
                },
                new ChatCompletionMessage("user", "Continue")
            ],
            providerParameters: providerParameters);

        using var doc = JsonDocument.Parse(handler.RequestBodies[1]);
        var assistantTurn = doc.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "assistant");

        Assert.Multiple(() =>
        {
            Assert.That(second.Content, Is.EqualTo("next"));
            Assert.That(assistantTurn.GetProperty("reasoning_content").GetString(), Is.EqualTo("final hidden reasoning"));
        });
    }

    [Test]
    public async Task ChatCompletionWithToolsAsync_ReplaysReasoningContentForThinkingToolTurns()
    {
        using var handler = new CaptureHandler(ToolCallResponse, ReasonedCompletionResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);
        var providerParameters = new Dictionary<string, JsonElement>
        {
            ["thinking"] = JsonSerializer.SerializeToElement(new { type = "enabled" })
        };

        var first = await client.ChatCompletionWithToolsAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ToolAwareMessage { Role = "user", Content = "Use a tool" }],
            tools: [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            providerParameters: providerParameters);

        var final = await client.ChatCompletionWithToolsAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages:
            [
                new ToolAwareMessage { Role = "user", Content = "Use a tool" },
                ToolAwareMessage.AssistantWithToolCalls(
                    first.ToolCalls,
                    first.Content,
                    first.ProviderMetadataJson),
                ToolAwareMessage.ToolResult(first.ToolCalls[0].Id, "lookup result")
            ],
            tools: [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            providerParameters: providerParameters);

        using var requestDoc = JsonDocument.Parse(handler.RequestBodies[1]);
        var assistantTurn = requestDoc.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "assistant");

        Assert.Multiple(() =>
        {
            Assert.That(first.HasToolCalls, Is.True);
            Assert.That(first.ToolCalls, Has.Count.EqualTo(1));
            Assert.That(first.ToolCalls[0].Name, Is.EqualTo("lookup"));
            Assert.That(ExtractReasoningContent(first.ProviderMetadataJson), Is.EqualTo("hidden tool reasoning"));
            Assert.That(final.Content, Is.EqualTo("ok"));
            Assert.That(ExtractReasoningContent(final.ProviderMetadataJson), Is.EqualTo("final hidden reasoning"));
            Assert.That(assistantTurn.GetProperty("reasoning_content").GetString(), Is.EqualTo("hidden tool reasoning"));
        });
    }

    [Test]
    public async Task ChatCompletionWithToolsAsync_ReasoningEffortWithToolsEnablesThinking()
    {
        using var handler = new CaptureHandler(ToolCallResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);

        var result = await client.ChatCompletionWithToolsAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ToolAwareMessage { Role = "user", Content = "Use a tool" }],
            tools: [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            completionParameters: new CompletionParameters { ReasoningEffort = "high" });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(result.HasToolCalls, Is.True);
            Assert.That(doc.RootElement.GetProperty("thinking").GetProperty("type").GetString(), Is.EqualTo("enabled"));
            Assert.That(doc.RootElement.GetProperty("reasoning_effort").GetString(), Is.EqualTo("high"));
        });
    }

    [Test]
    public async Task StreamChatCompletionWithToolsAsync_AccumulatesReasoningContent()
    {
        const string streamResponse = """
            data: {"choices":[{"index":0,"delta":{"reasoning_content":"hidden "}}]}

            data: {"choices":[{"index":0,"delta":{"reasoning_content":"stream reasoning"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}

            data: [DONE]

            """;

        using var handler = CaptureHandler.Stream(streamResponse);
        using var httpClient = new HttpClient(handler);
        var client = new DeepSeekApiClient("test-key", httpClient);
        var chunks = new List<ChatStreamChunk>();

        await foreach (var chunk in client.StreamChatCompletionWithToolsAsync(
            "deepseek-v4-pro",
            systemPrompt: null,
            messages: [new ToolAwareMessage { Role = "user", Content = "Think then answer" }],
            tools: [new ChatToolDefinition("lookup", "Lookup information", EmptyObjectSchema())],
            providerParameters: new Dictionary<string, JsonElement>
            {
                ["thinking"] = JsonSerializer.SerializeToElement(new { type = "enabled" })
            }))
        {
            chunks.Add(chunk);
        }

        var final = chunks.Single(chunk => chunk.IsFinished).Finished!;
        Assert.Multiple(() =>
        {
            Assert.That(chunks.Where(chunk => chunk.Delta is not null).Select(chunk => chunk.Delta), Is.EqualTo(new[] { "ok" }));
            Assert.That(final.Content, Is.EqualTo("ok"));
            Assert.That(final.Usage, Is.EqualTo(new TokenUsage(1, 2)));
            Assert.That(ExtractReasoningContent(final.ProviderMetadataJson), Is.EqualTo("hidden stream reasoning"));
        });
    }

    [Test]
    public void ModuleRegistersDeepSeekProvider()
    {
        var services = new ServiceCollection();
        new OpenAICompatibleProvidersModule().ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var plugin = serviceProvider.GetServices<IProviderPlugin>()
            .Single(provider => provider.ProviderKey == "deepseek");

        Assert.Multiple(() =>
        {
            Assert.That(plugin.DisplayName, Is.EqualTo("DeepSeek"));
            Assert.That(plugin.OwnerModuleId, Is.EqualTo("sharpclaw_providers_openai_compat"));
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.DeepSeek));
            Assert.That(
                ProviderCredentialBinding.CreateClient(plugin, new ProviderClientOptions(null), "test-key"),
                Is.TypeOf<DeepSeekApiClient>());
            Assert.That(plugin.Capabilities.Resolve("deepseek-v4-flash"), Is.EquivalentTo(new[] { WellKnownCapabilityKeys.Chat }));
        });
    }

    [Test]
    public void ParameterSpecMatchesDeepSeekSurface()
    {
        var spec = ProviderParameterSpecs.For("deepseek");

        Assert.Multiple(() =>
        {
            Assert.That(spec.ProviderName, Is.EqualTo("DeepSeek"));
            Assert.That(spec.SupportsResponseFormat, Is.True);
            Assert.That(spec.OnlyJsonObjectResponseFormat, Is.True);
            Assert.That(spec.SupportsReasoningEffort, Is.True);
            Assert.That(spec.ValidReasoningEffortValues, Is.EquivalentTo(new[] { "low", "medium", "high", "xhigh", "max" }));
            Assert.That(spec.SupportsFrequencyPenalty, Is.False);
            Assert.That(spec.SupportsPresencePenalty, Is.False);
            Assert.That(spec.SupportsStrictTools, Is.False);
        });
    }

    private const string ToolCallResponse = """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": null,
                "reasoning_content": "hidden tool reasoning",
                "tool_calls": [
                  {
                    "id": "call_1",
                    "type": "function",
                    "function": {
                      "name": "lookup",
                      "arguments": "{}"
                    }
                  }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ],
          "usage": {
            "prompt_tokens": 2,
            "completion_tokens": 3,
            "total_tokens": 5
          }
        }
        """;

    private const string ReasonedCompletionResponse = """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "ok",
                "reasoning_content": "final hidden reasoning"
              },
              "finish_reason": "stop"
            }
          ],
          "usage": {
            "prompt_tokens": 1,
            "completion_tokens": 1,
            "total_tokens": 2
          }
        }
        """;

    private static JsonElement EmptyObjectSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>()
        });

    private static string? ExtractReasoningContent(string? providerMetadataJson)
    {
        Assert.That(providerMetadataJson, Is.Not.Null);
        using var doc = JsonDocument.Parse(providerMetadataJson!);
        return doc.RootElement.GetProperty("reasoning_content").GetString();
    }
}

[TestFixture]
public sealed class EdenAIApiClientBehaviorTests
{
    [Test]
    public async Task ChatCompletionAsync_UsesEdenAiV3ChatCompletionsEndpoint()
    {
        using var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var client = new EdenAIApiClient("test-key", httpClient);

        var result = await client.ChatCompletionAsync(
            "openai/gpt-4o-mini",
            systemPrompt: null,
            [new ChatCompletionMessage("user", "Hello")],
            completionParameters: new CompletionParameters
            {
                Temperature = 0.2f,
                ReasoningEffort = "none"
            });

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(result.Content, Is.EqualTo("ok"));
            Assert.That(result.Usage, Is.EqualTo(new TokenUsage(1, 2)));
            Assert.That(handler.LastRequestUri?.ToString(), Is.EqualTo("https://api.edenai.run/v3/chat/completions"));
            Assert.That(handler.LastAuthorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastAuthorization?.Parameter, Is.EqualTo("test-key"));
            Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("openai/gpt-4o-mini"));
            Assert.That(doc.RootElement.GetProperty("temperature").GetDouble(), Is.EqualTo(0.2).Within(0.000001));
            Assert.That(doc.RootElement.GetProperty("reasoning_effort").GetString(), Is.EqualTo("none"));
        });
    }

    [Test]
    public async Task ListModelIdsAsync_UsesEdenAiV3ModelsEndpoint()
    {
        using var handler = new CaptureHandler("""
            {
              "object": "list",
              "data": [
                { "id": "openai/gpt-4o-mini", "object": "model" },
                { "id": "anthropic/claude-sonnet-4-5", "object": "model" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new EdenAIApiClient("test-key", httpClient);

        var ids = await client.ListModelIdsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.LastRequestUri?.ToString(), Is.EqualTo("https://api.edenai.run/v3/models"));
            Assert.That(handler.LastAuthorization?.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastAuthorization?.Parameter, Is.EqualTo("test-key"));
            Assert.That(ids, Is.EqualTo(new[] { "anthropic/claude-sonnet-4-5", "openai/gpt-4o-mini" }));
        });
    }

    [Test]
    public void ModuleRegistersEdenAiProvider()
    {
        var services = new ServiceCollection();
        new OpenAICompatibleProvidersModule().ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var plugin = serviceProvider.GetServices<IProviderPlugin>()
            .Single(provider => provider.ProviderKey == "eden-ai");

        Assert.Multiple(() =>
        {
            Assert.That(plugin.DisplayName, Is.EqualTo("Eden AI"));
            Assert.That(plugin.OwnerModuleId, Is.EqualTo("sharpclaw_providers_openai_compat"));
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.EdenAI));
            Assert.That(
                ProviderCredentialBinding.CreateClient(plugin, new ProviderClientOptions(null), "test-key"),
                Is.TypeOf<EdenAIApiClient>());
            Assert.That(
                plugin.Capabilities.Resolve("openai/gpt-4o-mini"),
                Is.EquivalentTo(new[] { WellKnownCapabilityKeys.Chat, WellKnownCapabilityKeys.Vision }));
            Assert.That(plugin.Capabilities.Resolve("@edenai"), Is.EquivalentTo(new[] { WellKnownCapabilityKeys.Chat }));
        });
    }

    [Test]
    public void ParameterSpecMatchesEdenAiSurface()
    {
        var spec = ProviderParameterSpecs.For("eden-ai");

        Assert.Multiple(() =>
        {
            Assert.That(spec.ProviderName, Is.EqualTo("Eden AI"));
            Assert.That(spec.SupportsResponseFormat, Is.True);
            Assert.That(spec.SupportsReasoningEffort, Is.True);
            Assert.That(spec.ValidReasoningEffortValues, Does.Contain("none"));
            Assert.That(spec.SupportsToolChoice, Is.True);
            Assert.That(spec.SupportsSeed, Is.True);
        });
    }

    [Test]
    public void ProviderTypesCanBeDerivedFromRegisteredPluginMetadata()
    {
        var services = new ServiceCollection();
        new OpenAICompatibleProvidersModule().ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var plugins = serviceProvider.GetServices<IProviderPlugin>().ToDictionary(
            provider => provider.ProviderKey,
            StringComparer.Ordinal);
        var edenAi = plugins["eden-ai"];

        Assert.Multiple(() =>
        {
            Assert.That(edenAi.ProviderKey, Is.EqualTo("eden-ai"));
            Assert.That(edenAi.DisplayName, Is.EqualTo("Eden AI"));
            Assert.That(edenAi.RequiresEndpoint, Is.False);
            Assert.That(edenAi.RequiresApiKey, Is.True);
            Assert.That(edenAi.DeviceCodeFlow, Is.Null);
            Assert.That(plugins.ContainsKey("google-gemini-openai"), Is.True);
        });
    }

    private sealed class CaptureHandler(string? responseBody = null) : HttpMessageHandler
    {
        private const string CompletionResponse = """
            {
              "choices": [
                {
                  "message": { "role": "assistant", "content": "ok" },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 1,
                "completion_tokens": 2,
                "total_tokens": 3
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
}

internal sealed class CaptureHandler : HttpMessageHandler
{
    private const string CompletionResponse = """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "ok" },
              "finish_reason": "stop"
            }
          ],
          "usage": {
            "prompt_tokens": 1,
            "completion_tokens": 1,
            "total_tokens": 2
          }
        }
        """;

    private readonly Queue<(string Body, string MediaType)> _responses;

    public CaptureHandler(params string[] responses)
        : this(responses.Length > 0
            ? responses.Select(response => (Body: response, MediaType: "application/json"))
            : [(Body: CompletionResponse, MediaType: "application/json")])
    {
    }

    private CaptureHandler(IEnumerable<(string Body, string MediaType)> responses)
    {
        _responses = new Queue<(string Body, string MediaType)>(responses);
    }

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public List<string> RequestBodies { get; } = [];

    public static CaptureHandler Stream(string response)
        => new([(Body: response, MediaType: "text/event-stream")]);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(LastRequestBody);
        }

        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : (Body: CompletionResponse, MediaType: "application/json");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, response.MediaType)
        };
    }
}
