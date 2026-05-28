using System;
using CardStatement.Core.Categorization;
using FluentAssertions;
using Xunit;

namespace CardStatement.Tests.Categorization;

public class OpenAiLlmClientTests
{
    [Fact]
    public void Constructor_throws_when_api_key_and_base_url_are_missing()
    {
        var options = new OpenAiOptions
        {
            ApiKey = "",
            BaseUrl = null
        };

        var action = () => new OpenAiLlmClient(options);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Categorization:OpenAi:ApiKey is not configured.");
    }

    [Fact]
    public void Constructor_succeeds_when_api_key_is_provided()
    {
        var options = new OpenAiOptions
        {
            ApiKey = "some-key",
            BaseUrl = null
        };

        var client = new OpenAiLlmClient(options);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_succeeds_and_defaults_api_key_when_only_base_url_is_provided()
    {
        var options = new OpenAiOptions
        {
            ApiKey = "",
            BaseUrl = "http://localhost:1234/v1"
        };

        var client = new OpenAiLlmClient(options);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_accepts_base_url_without_trailing_slash()
    {
        var options = new OpenAiOptions
        {
            ApiKey = "key",
            BaseUrl = "http://localhost:1234/api/v1"
        };

        var client = new OpenAiLlmClient(options);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_accepts_disabled_json_mode()
    {
        var options = new OpenAiOptions
        {
            ApiKey = "key",
            UseJsonMode = false
        };

        var client = new OpenAiLlmClient(options);
        client.Should().NotBeNull();
    }

    [Theory]
    [InlineData("```json\n{\n  \"items\": []\n}\n```", "{\n  \"items\": []\n}")]
    [InlineData("```\n{\n  \"items\": []\n}\n```", "{\n  \"items\": []\n}")]
    [InlineData("{\n  \"items\": []\n}", "{\n  \"items\": []\n}")]
    [InlineData("  ```json\n  { }  \n  ```  ", "{ }")]
    public void CleanJsonContent_strips_markdown_fences_correctly(string input, string expected)
    {
        var result = OpenAiLlmClient.CleanJsonContent(input);
        result.Should().Be(expected);
    }
}
