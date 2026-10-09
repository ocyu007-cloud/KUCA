using System.Text.Json.Serialization;

namespace KucaMemoServer.Models;

/// <summary>
/// 메모에 단 댓글 하나. API 응답의 JSON 모양과 같다 (docs/api.md 의 Comment).
/// </summary>
public class Comment
{
    /// <summary>댓글 ID (서버가 만든다, Guid 문자열)</summary>
    public string Id { get; set; } = "";

    /// <summary>댓글이 달린 메모 ID</summary>
    public string MemoId { get; set; } = "";

    /// <summary>작성자 닉네임 (1~20자)</summary>
    public string Author { get; set; } = "";

    /// <summary>댓글 내용 (1~200자)</summary>
    public string Text { get; set; } = "";

    /// <summary>작성 시각 (UTC)</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>"visible"(모두에게 보임) 또는 "pending"(검토 대기, 작성 기기에만 보임)</summary>
    public string Status { get; set; } = ContentStatus.Visible;

    /// <summary>검토 대기로 둔 이유 (관리자 API 에서만 채운다, 그 밖에는 응답에서 빠짐)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FlagNote { get; set; }

    /// <summary>작성한 기기 ID. 삭제 권한 확인용으로 서버에만 저장하고 API 응답에는 내보내지 않는다.</summary>
    [JsonIgnore]
    public string DeviceId { get; set; } = "";
}
