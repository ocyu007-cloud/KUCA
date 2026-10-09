using System.Security.Cryptography;
using System.Text;
using KucaMemoServer.Services;
using static KucaMemoServer.Endpoints.MemoEndpoints;

namespace KucaMemoServer.Endpoints;

/// <summary>
/// 관리자 API: 내용 검사에서 검토 대기(pending)가 된 메모·댓글을 공개하거나 지운다 (docs/api.md 의 "관리자 API").
/// 헤더 X-Admin-Key 가 설정 Admin:Key 와 같아야 한다. 설정이 없으면 관리자 API 는 꺼진다(404).
/// Admin:Key 는 저장소에 넣지 않는다: Azure 는 앱 설정 Admin__Key, 로컬은 dotnet user-secrets.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin")
            .AddEndpointFilter(async (context, next) =>
            {
                string? expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:Key"];
                if (string.IsNullOrEmpty(expected))
                    return Results.NotFound();

                string given = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
                if (!SameKey(given, expected))
                    return Error(StatusCodes.Status403Forbidden, "관리자 키가 맞지 않습니다");

                return await next(context);
            });

        admin.MapGet("/pending", (MemoStore memos) =>
            Results.Ok(new { memos = memos.ListPendingMemos(), comments = memos.ListPendingComments() }));

        admin.MapPost("/memos/{memoId}/approve", (string memoId, MemoStore memos) =>
            memos.ApproveMemo(memoId) ? Results.Ok(memos.Find(memoId)) : MemoNotFound(memoId));

        // 거절: 사진·좋아요·댓글과 함께 지운다.
        admin.MapDelete("/memos/{memoId}", (string memoId, MemoStore memos, PhotoStore photos) =>
        {
            if (memos.Find(memoId) is not { } memo)
                return MemoNotFound(memoId);
            memos.Delete(memoId);
            photos.Delete(memo.PhotoUrl);
            return Results.NoContent();
        });

        admin.MapPost("/comments/{commentId}/approve", (string commentId, MemoStore memos) =>
            memos.ApproveComment(commentId) ? Results.Ok(memos.FindComment(commentId)) : CommentNotFound(commentId));

        admin.MapDelete("/comments/{commentId}", (string commentId, MemoStore memos) =>
            memos.DeleteComment(commentId) ? Results.NoContent() : CommentNotFound(commentId));
    }

    static IResult CommentNotFound(string id) => Error(StatusCodes.Status404NotFound, $"댓글을 찾을 수 없습니다: {id}");

    /// <summary>키 비교 시간이 내용에 따라 달라지지 않게 고정 시간으로 비교한다.</summary>
    static bool SameKey(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
