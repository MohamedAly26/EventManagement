using Markdig;
using Microsoft.AspNetCore.Components;

namespace EventManagement.Shared;

public static class Md
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .DisableHtml()              // niente HTML inline → evita XSS
            .UseAdvancedExtensions()
            .Build();

    public static MarkupString ToHtml(string? text)
        => new(Markdown.ToHtml(text ?? string.Empty, Pipeline));
}