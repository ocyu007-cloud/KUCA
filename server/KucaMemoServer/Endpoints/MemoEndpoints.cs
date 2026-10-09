using System.Globalization;
using KucaMemoServer.Models;
using KucaMemoServer.Services;

namespace KucaMemoServer.Endpoints;

/// <summary>
/// 메모 API. 주소와 요청/응답 모양은 docs/api.md 를 그대로 따른다.
/// </summary>
public static class MemoEndpoints
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxAuthorLength = 20;
    public const int MaxTextLength = 500;
    public const int MaxDeviceIdLength = 64;

    public static void MapMemoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Memos");

        // 건물의 메모 목록 (최신순). ?limit=1~50 (기본 20), ?before=시각 이면 그보다 이전 메모만.
        // X-Device-Id 헤더를 보내면 각 메모의 likedByMe 가 채워진다.
        group.MapGet("/buildings/{buildingId}/memos", (string buildingId, int? limit, string? before, HttpRequest request,
                                                       BuildingStore buildings, MemoStore memos) =>
        {
            if (buildings.Find(buildingId) is null)
                return BuildingNotFound(buildingId);

            DateTime? beforeTime = null;
            if (!string.IsNullOrWhiteSpace(before))
            {
                if (!DateTime.TryParse(before, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                    return Error(StatusCodes.Status400BadRequest, "before 는 ISO 8601 시각이어야 합니다 (예: 2026-10-06T05:12:30Z)");
                beforeTime = parsed;
            }

            int take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            return Results.Ok(memos.List(buildingId, take, beforeTime, ViewerDeviceId(request)));
        });

        // 메모 작성. multipart/form-data 로 author, text, deviceId, photo(선택)를 받는다.
        // 저장 전에 글과 사진을 내용 검사한다: 거절이면 422, 애매하면 검토 대기(pending)로 저장.
        group.MapPost("/buildings/{buildingId}/memos", async (string buildingId, HttpRequest request,
                                                              BuildingStore buildings, MemoStore memos, PhotoStore photos,
                                                              IContentModerator moderator) =>
        {
            if (buildings.Find(buildingId) is null)
                return BuildingNotFound(buildingId);

            if (!request.HasFormContentType)
                return Error(StatusCodes.Status400BadRequest, "본문은 multipart/form-data 여야 합니다");

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync();
            }
            catch (InvalidDataException)
            {
                // FormOptions 의 크기 제한을 넘은 경우
                return Error(StatusCodes.Status400BadRequest, "사진은 10MB 이하만 올릴 수 있습니다");
            }

            string author = form["author"].ToString().Trim();
            string text = form["text"].ToString().Trim();
            string deviceId = form["deviceId"].ToString().Trim();

            if (author.Length is 0 or > MaxAuthorLength)
                return Error(StatusCodes.Status400BadRequest, $"author 는 1~{MaxAuthorLength}자여야 합니다");
            if (text.Length is 0 or > MaxTextLength)
                return Error(StatusCodes.Status400BadRequest, $"text 는 1~{MaxTextLength}자여야 합니다");
            if (deviceId.Length is 0 or > MaxDeviceIdLength)
                return Error(StatusCodes.Status400BadRequest, $"deviceId 는 1~{MaxDeviceIdLength}자여야 합니다");

            IFormFile? photo = form.Files.GetFile("photo");
            string? extension = null;
            if (photo is { Length: > 0 })
            {
                if (photo.Length > PhotoStore.MaxBytes)
                    return Error(StatusCodes.Status400BadRequest, "photo 는 10MB 이하만 올릴 수 있습니다");
                extension = await PhotoStore.DetectExtensionAsync(photo);
                if (extension is null)
                    return Error(StatusCodes.Status400BadRequest, "photo 는 JPEG 또는 PNG 여야 합니다");
            }

            ModerationImage? image = null;
            if (photo is not null && extension is not null)
            {
                using var buffer = new MemoryStream();
                await using (var stream = photo.OpenReadStream())
                    await stream.CopyToAsync(buffer);
                image = new ModerationImage(buffer.ToArray(), extension == "png" ? "image/png" : "image/jpeg");
            }

            // 닉네임도 남에게 보이므로 함께 검사한다.
            ModerationResult check = await moderator.CheckAsync($"닉네임: {author}\n{text}", image, request.HttpContext.RequestAborted);
            if (check.Verdict == Verdict.Block)
                return Rejected(check);

            var memo = new Memo
            {
                Id = Guid.NewGuid().ToString(),
                BuildingId = buildingId,
                Author = author,
                Text = text,
                DeviceId = deviceId,
                // 밀리초 아래는 버린다. 응답의 createdAt 을 그대로 before 에 넣어도 같은 메모가 다시 오지 않게.
                CreatedAt = TruncateToMilliseconds(DateTime.UtcNow),
                Status = check.Verdict == Verdict.Review ? ContentStatus.Pending : ContentStatus.Visible,
                FlagNote = check.Verdict == Verdict.Review ? DescribeFlag(check) : null,
            };

            if (photo is not null && extension is not null)
                memo.PhotoUrl = await photos.SaveAsync(memo.Id, extension, photo);

            try
            {
                memos.Add(memo);
            }
            catch
            {
                photos.Delete(memo.PhotoUrl);
                throw;
            }

            memo.FlagNote = null; // 검토 메모는 관리자에게만 보인다
            return Results.Created($"/api/memos/{memo.Id}", memo);
        })
        .DisableAntiforgery();

        // 메모 하나. X-Device-Id 헤더를 보내면 likedByMe 가 채워진다.
        // 남이 쓴 검토 대기 메모는 없는 것처럼 404.
        group.MapGet("/memos/{memoId}", (string memoId, HttpRequest request, MemoStore memos) =>
        {
            string? viewer = ViewerDeviceId(request);
            return memos.Find(memoId, viewer) is { } memo && IsVisibleTo(memo.Status, memo.DeviceId, viewer)
                ? Results.Ok(memo)
                : MemoNotFound(memoId);
        });

        // 메모 삭제. X-Device-Id 헤더가 작성할 때의 deviceId 와 같을 때만. 사진 파일·좋아요·댓글도 함께 지운다.
        group.MapDelete("/memos/{memoId}", (string memoId, HttpRequest request, MemoStore memos, PhotoStore photos) =>
        {
            Memo? memo = memos.Find(memoId);
            if (memo is null)
                return MemoNotFound(memoId);

            string deviceId = request.Headers["X-Device-Id"].ToString().Trim();
            if (deviceId.Length == 0 || deviceId != memo.DeviceId)
                return Error(StatusCodes.Status403Forbidden, "이 기기에서 쓴 메모만 지울 수 있습니다");

            memos.Delete(memoId);
            photos.Delete(memo.PhotoUrl);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// X-Device-Id 헤더 값 (앞뒤 공백 제거). 없거나 64자를 넘으면 null.
    /// 조회에서는 likedByMe 계산에만 쓰므로 틀린 값이어도 오류 대신 "모르는 기기"로 본다.
    /// </summary>
    internal static string? ViewerDeviceId(HttpRequest request)
    {
        string id = request.Headers["X-Device-Id"].ToString().Trim();
        return id.Length is > 0 and <= MaxDeviceIdLength ? id : null;
    }

    /// <summary>공개 메모·댓글이거나, 검토 대기라도 작성한 기기가 보는 것이면 true.</summary>
    internal static bool IsVisibleTo(string status, string ownerDeviceId, string? viewer) =>
        status == ContentStatus.Visible || (viewer is not null && viewer == ownerDeviceId);

    /// <summary>내용 검사 거절 응답: 422 {error, reason}</summary>
    internal static IResult Rejected(ModerationResult check) =>
        Results.Json(new { error = check.UserMessage, reason = check.Reason ?? ModerationReasons.Abusive },
                     statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>관리자가 볼 검토 메모 (이유 코드 + AI 가 쓴 한 문장)</summary>
    internal static string DescribeFlag(ModerationResult check) =>
        string.Join(" · ", new[] { check.Reason, check.Note }.Where(s => !string.IsNullOrWhiteSpace(s)));

    internal static DateTime TruncateToMilliseconds(DateTime t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond, t.Kind);

    internal static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    static IResult BuildingNotFound(string id) => Error(StatusCodes.Status404NotFound, $"건물을 찾을 수 없습니다: {id}");

    internal static IResult MemoNotFound(string id) => Error(StatusCodes.Status404NotFound, $"메모를 찾을 수 없습니다: {id}");
}
