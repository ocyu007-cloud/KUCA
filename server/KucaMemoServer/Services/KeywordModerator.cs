using System.Text.RegularExpressions;

namespace KucaMemoServer.Services;

/// <summary>
/// AI 를 쓸 수 없을 때(키 없음, 장애, 시간 초과) 대신 쓰는 단순 검사.
/// 금지어 목록과 전화번호·이메일 패턴만 본다. 걸리면 거절, 아니면 통과 (애매함 판정은 없음).
/// 사진은 검사하지 않는다.
/// </summary>
public static partial class KeywordModerator
{
    // 공백·기호를 지운 뒤 비교하므로 "시 발", "시.발" 도 걸린다.
    // 캠퍼스 글에 흔한 말과 겹치는 단어는 넣지 않는다 (예: "보지 못했어요", "불이 꺼져", "도서 대출").
    static readonly string[] AbusiveWords =
    {
        "씨발", "시발", "씨빨", "ㅅㅂ", "병신", "ㅂㅅ", "개새끼", "새끼야", "좆", "지랄", "닥쳐",
        "미친놈", "미친년", "느금마", "니애미", "섹스",
    };

    static readonly string[] AdvertisingWords =
    {
        "광고문의", "홍보문의", "오픈채팅", "openkakao", "카톡문의", "대출상담", "수익보장",
        "텔레그램문의", "구매문의", "할인코드",
    };

    // 010-1234-5678, 01012345678, 010 1234 5678, +82 10-1234-5678
    [GeneratedRegex(@"(\+?82[-\s]?1[016789]|01[016789])[-\s.]?\d{3,4}[-\s.]?\d{4}")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[\s\p{P}\p{S}]")]
    private static partial Regex SeparatorRegex();

    public static ModerationResult Check(string text)
    {
        if (PhoneRegex().IsMatch(text) || EmailRegex().IsMatch(text))
            return new ModerationResult(Verdict.Block, ModerationReasons.PersonalInfo, "전화번호·이메일 패턴", UsedFallback: true);

        string compact = SeparatorRegex().Replace(text, "").ToLowerInvariant();
        if (AbusiveWords.Any(compact.Contains))
            return new ModerationResult(Verdict.Block, ModerationReasons.Abusive, "금지어", UsedFallback: true);
        if (AdvertisingWords.Any(compact.Contains))
            return new ModerationResult(Verdict.Block, ModerationReasons.Advertising, "광고 단어", UsedFallback: true);

        return ModerationResult.Allowed(usedFallback: true);
    }
}
