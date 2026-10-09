using KucaMemoServer.Services;

namespace KucaMemoServer.Tests;

/// <summary>
/// 테스트용 내용 검사기. 실제 AI 를 부르지 않는다.
/// Next 를 정하면 그 결과를 돌려주고, 정하지 않으면 금지어 검사(KeywordModerator)를 쓴다.
/// 받은 글과 사진을 Calls 에 기록한다.
/// </summary>
public sealed class FakeModerator : IContentModerator
{
    public ModerationResult? Next { get; set; }

    public List<(string Text, ModerationImage? Image)> Calls { get; } = new();

    public Task<ModerationResult> CheckAsync(string text, ModerationImage? image, CancellationToken cancellationToken = default)
    {
        lock (Calls)
            Calls.Add((text, image));
        return Task.FromResult(Next ?? KeywordModerator.Check(text));
    }
}
