namespace NeuroTune.Tests;

[TestClass]
public sealed class WebSearchTests
{
    private static string Body(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    [TestMethod]
    public void Native_search_is_requested_only_where_the_provider_supports_it_and_never_with_a_Tavily_key()
    {
        var anthropic = LlmClient.Defaults(LlmProvider.Anthropic); anthropic.Model = "claude";
        var openRouter = LlmClient.Defaults(LlmProvider.OpenRouter);
        var deepSeek = LlmClient.Defaults(LlmProvider.DeepSeek);
        Assert.IsTrue(WebSearch.Native(anthropic, tavily: false));
        Assert.IsTrue(WebSearch.Native(openRouter, tavily: false));
        Assert.IsFalse(WebSearch.Native(openRouter, tavily: true));
        Assert.IsFalse(WebSearch.Native(deepSeek, tavily: false));

        StringAssert.Contains(Body(LlmClient.CreateAnthropicRequest(anthropic, "key", "prompt", webSearch: true)), "web_search_20250305");
        Assert.IsFalse(Body(LlmClient.CreateAnthropicRequest(anthropic, "key", "prompt")).Contains("web_search", StringComparison.Ordinal));
        StringAssert.Contains(Body(LlmClient.CreateOpenAiRequest(openRouter, "key", "prompt", webSearch: true)), "\"plugins\":[{\"id\":\"web\"");
        Assert.IsFalse(Body(LlmClient.CreateOpenAiRequest(deepSeek, "key", "prompt", webSearch: true)).Contains("plugins", StringComparison.Ordinal));
    }

    [TestMethod]
    public void The_envelope_is_the_text_after_the_last_search_block()
    {
        const string body = """
            {"content":[{"type":"text","text":"Let me search."},{"type":"server_tool_use","id":"s1","name":"web_search","input":{"query":"q"}},
            {"type":"web_search_tool_result","tool_use_id":"s1","content":[]},{"type":"text","text":"{\"kind\":"},{"type":"text","text":"\"diagnosis\"}"}]}
            """;
        Assert.AreEqual("{\"kind\":\"diagnosis\"}", LlmClient.ExtractContent(ApiProtocol.Anthropic, body));
        Assert.AreEqual("plain", LlmClient.ExtractContent(ApiProtocol.Anthropic, """{"content":[{"type":"text","text":"plain"}]}"""));
    }

    [TestMethod]
    public void Tavily_results_are_bounded_and_marked_untrusted()
    {
        var parsed = System.Text.Json.JsonSerializer.Serialize(WebSearch.ParseTavily($$"""{"results":[{"title":"T","url":"https://e.example","content":"{{new string('x', 5000)}}"}]}"""));
        StringAssert.Contains(parsed, "untrusted");
        Assert.IsLessThan(2_500, parsed.Length);
    }
}
