using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UCE.Application.Documents.Abstractions;
using UCE.Infrastructure.Chunking;
using Xunit;

namespace UCE.UnitTests;

public sealed class OllamaSemanticBoundaryDetectorTests
{
    [Fact]
    public async Task ParsesStructuredBoundaryResponse()
    {
        var handler = new QueueHandler("""{"splitAfter":[0,2]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectBoundariesAsync(Paragraphs(4));

        Assert.Equal(new HashSet<int> { 0, 2 }, result);
        Assert.Equal(1, handler.RequestCount);
        using var request = JsonDocument.Parse(handler.LastRequest);
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Contains("\"required\":[\"splitAfter\"]", handler.LastRequest);
    }

    [Fact]
    public async Task SendsPersianParagraphTextWithoutUnicodeEscaping()
    {
        var handler = new QueueHandler("""{"splitAfter":[]}""");
        var detector = CreateDetector(handler);

        await detector.DetectBoundariesAsync(Paragraphs(2));

        using var request = JsonDocument.Parse(handler.LastRequest);
        var userPrompt = request.RootElement
            .GetProperty("messages")[1]
            .GetProperty("content")
            .GetString();

        Assert.Contains("متن پاراگراف", userPrompt);
        Assert.Contains("sectionTitle", userPrompt);
        Assert.Contains("بخش", userPrompt);
        Assert.DoesNotContain("\\u", userPrompt);
    }

    [Fact]
    public async Task RetriesWhenModelOmitsSplitAfter()
    {
        var handler = new QueueHandler(
            """{"boundaries":[0]}""",
            """{"splitAfter":[1]}""");
        var detector = CreateDetector(handler);

        var result = await detector.DetectBoundariesAsync(Paragraphs(3));

        Assert.Equal(new HashSet<int> { 1 }, result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task AcceptsJsonWrappedInMarkdownFence()
    {
        var fence = new string((char)96, 3);
        var response = string.Concat(fence, "json\n{\"splitAfter\":[0]}\n", fence);
        var handler = new QueueHandler(response);
        var detector = CreateDetector(handler);

        var result = await detector.DetectBoundariesAsync(Paragraphs(2));

        Assert.Equal(new HashSet<int> { 0 }, result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ThrowsAfterRetryWhenBoundaryIndexIsOutOfRange()
    {
        var handler = new QueueHandler(
            """{"splitAfter":[9]}""",
            """{"splitAfter":[9]}""");
        var detector = CreateDetector(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => detector.DetectBoundariesAsync(Paragraphs(3)));

        Assert.Contains("after JSON-mode retry", exception.Message);
        Assert.Contains("boundary index 9", exception.Message);
        Assert.Equal(2, handler.RequestCount);
    }

    private static OllamaSemanticBoundaryDetector CreateDetector(QueueHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        return new OllamaSemanticBoundaryDetector(
            client,
            Options.Create(new SemanticChunkingOptions { Model = "gemma3:1b" }));
    }

    private static IReadOnlyList<SemanticParagraphInput> Paragraphs(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new SemanticParagraphInput(i, $"متن پاراگراف {i}", false, "بخش"))
            .ToArray();

    private sealed class QueueHandler(params string[] contents) : HttpMessageHandler
    {
        private readonly Queue<string> _contents = new(contents);
        public int RequestCount { get; private set; }
        public string LastRequest { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
            var content = _contents.Dequeue();
            var body = JsonSerializer.Serialize(new { message = new { content } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
