namespace KucaMemoServer.Services;

/// <summary>내용 검사 결과 (docs/api.md 의 "내용 검사")</summary>
public enum Verdict
{
    /// <summary>통과: 바로 공개</summary>
    Allow,
    /// <summary>거절: 저장하지 않고 422</summary>
    Block,
    /// <summary>애매함: 저장하되 검토 대기(pending)</summary>
    Review,
}

/// <param name="Verdict">통과 / 거절 / 검토 대기</param>
/// <param name="Reason">거절·검토 이유 코드: abusive, personal_info, advertising. 통과면 null.</param>
/// <param name="Note">관리자가 볼 판단 메모 (사용자에게는 보이지 않음)</param>
/// <param name="UsedFallback">AI 대신 금지어·패턴 검사로 판단했으면 true</param>
public record ModerationResult(Verdict Verdict, string? Reason, string? Note, bool UsedFallback)
{
    public static ModerationResult Allowed(bool usedFallback) => new(Verdict.Allow, null, null, usedFallback);

    /// <summary>거절할 때 사용자에게 보여 줄 문구</summary>
    public string UserMessage => Reason switch
    {
        ModerationReasons.PersonalInfo => "전화번호 같은 개인정보가 있어 올릴 수 없어요.",
        ModerationReasons.Advertising => "광고·홍보 내용은 올릴 수 없어요.",
        _ => "욕설이나 불쾌한 표현이 있어 올릴 수 없어요.",
    };
}

public static class ModerationReasons
{
    public const string Abusive = "abusive";
    public const string PersonalInfo = "personal_info";
    public const string Advertising = "advertising";

    public static bool IsKnown(string? reason) => reason is Abusive or PersonalInfo or Advertising;
}

/// <summary>사진 한 장 (검사용)</summary>
public record ModerationImage(byte[] Bytes, string MediaType);

/// <summary>쪽지·댓글 내용 검사기. 실제 구현은 AiModerator, 테스트에서는 가짜로 바꿔 끼운다.</summary>
public interface IContentModerator
{
    Task<ModerationResult> CheckAsync(string text, ModerationImage? image, CancellationToken cancellationToken = default);
}
