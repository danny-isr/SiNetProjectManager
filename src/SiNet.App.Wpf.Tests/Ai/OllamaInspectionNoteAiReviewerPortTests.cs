using SiNet.Application.Ai;
using SiNet.Infrastructure.Sql.Services.Ai;
using Xunit;

namespace SiNet.App.Wpf.Tests.Ai;

public sealed class OllamaInspectionNoteAiReviewerPortTests
{
    [Fact]
    public async Task Review_uses_Simple_then_QualityCheck_on_generic_port()
    {
        var completion = new RecordingCompletion();
        var sut = new OllamaInspectionNoteAiReviewer(completion);

        var result = await sut.ReviewAsync("טקסט לבדיקה");

        Assert.False(result.HasError);
        Assert.Equal("grammar", result.GrammarCorrected);
        Assert.Equal("rephrase", result.Rephrased);
        Assert.Equal([AiCompletionLevel.Simple, AiCompletionLevel.QualityCheck], completion.Levels);
    }

    [Fact]
    public async Task Review_fails_gracefully_when_completion_unavailable()
    {
        var completion = new RecordingCompletion
        {
            Next = AiCompletionResult.Fail(AiCompletionFailureKind.Unavailable, "שירות ה-AI אינו זמין כרגע."),
        };
        var sut = new OllamaInspectionNoteAiReviewer(completion);

        var result = await sut.ReviewAsync("טקסט לבדיקה");

        Assert.True(result.HasError);
        Assert.Equal("טקסט לבדיקה", result.OriginalText);
        Assert.Equal("שירות ה-AI אינו זמין כרגע.", result.ErrorMessage);
    }

    [Fact]
    public async Task Empty_text_fails_without_calling_completion()
    {
        var completion = new RecordingCompletion();
        var sut = new OllamaInspectionNoteAiReviewer(completion);

        var result = await sut.ReviewAsync("   ");

        Assert.True(result.HasError);
        Assert.Equal("טקסט ריק — אין מה לבדוק.", result.ErrorMessage);
        Assert.Empty(completion.Levels);
    }

    private sealed class RecordingCompletion : IAiCompletionService
    {
        public List<AiCompletionLevel> Levels { get; } = [];

        public AiCompletionResult? Next { get; set; }

        public Task<AiCompletionResult> CompleteAsync(
            AiCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Levels.Add(request.Level);
            if (Next is { } forced)
            {
                return Task.FromResult(forced);
            }

            var text = request.Level == AiCompletionLevel.Simple ? "grammar" : "rephrase";
            return Task.FromResult(AiCompletionResult.Ok(text, AiProviderNames.Ollama, "test-model"));
        }

        public Task<bool> IsAvailableAsync(
            AiCompletionLevel level = AiCompletionLevel.Simple,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
