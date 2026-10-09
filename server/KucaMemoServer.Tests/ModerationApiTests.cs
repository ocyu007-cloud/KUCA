using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KucaMemoServer.Services;

namespace KucaMemoServer.Tests;

/// <summary>내용 검사(docs/api.md 의 "내용 검사", "관리자 API") 가 API 에 붙은 모습을 검증한다.</summary>
public class ModerationApiTests : IDisposable
{
    const string Engineering = "way-455725718";

    static readonly byte[] TinyJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9 };

    static readonly ModerationResult BlockPersonal = new(Verdict.Block, ModerationReasons.PersonalInfo, "번호 있음", false);
    static readonly ModerationResult Review = new(Verdict.Review, ModerationReasons.Advertising, "홍보일 수도", false);

    readonly TestServer server = new();
    readonly HttpClient client;

    public ModerationApiTests() => client = server.CreateClient();

    public void Dispose() => server.Dispose();

    static MultipartFormDataContent MemoForm(string text = "공학관 자판기 고장", string deviceId = "writer", byte[]? photo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("원준"), "author" },
            { new StringContent(text), "text" },
            { new StringContent(deviceId), "deviceId" },
        };
        if (photo != null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "photo", "photo.jpg");
        }
        return form;
    }

    static StringContent CommentJson(string text, string deviceId = "commenter") =>
        new(JsonSerializer.Serialize(new { author = "민지", text, deviceId }), Encoding.UTF8, "application/json");

    Task<HttpResponseMessage> PostMemoAsync(MultipartFormDataContent form) =>
        client.PostAsync($"/api/buildings/{Engineering}/memos", form);

    async Task<string> CreateVisibleMemoAsync()
    {
        server.Moderator.Next = null;
        var res = await PostMemoAsync(MemoForm());
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? deviceId = null, string? adminKey = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (deviceId != null) req.Headers.Add("X-Device-Id", deviceId);
        if (adminKey != null) req.Headers.Add("X-Admin-Key", adminKey);
        return client.SendAsync(req);
    }

    async Task<JsonElement[]> ListMemosAsync(string? deviceId = null)
    {
        var res = await SendAsync(HttpMethod.Get, $"/api/buildings/{Engineering}/memos", deviceId);
        return (await res.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }

    // ---------- 메모 ----------

    [Fact]
    public async Task 거절되면_422와_사용자_문구_이유를_주고_저장하지_않는다()
    {
        server.Moderator.Next = BlockPersonal;

        var res = await PostMemoAsync(MemoForm(photo: TinyJpeg));

        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("personal_info", body.GetProperty("reason").GetString());
        Assert.Contains("개인정보", body.GetProperty("error").GetString());
        Assert.Empty(await ListMemosAsync("writer"));
        // 사진도 남지 않는다
        Assert.False(Directory.Exists(server.PhotosDir) && Directory.EnumerateFiles(server.PhotosDir).Any());
    }

    [Fact]
    public async Task 글과_사진과_닉네임을_검사기에_넘긴다()
    {
        await PostMemoAsync(MemoForm(text: "사진 메모", photo: TinyJpeg));

        var call = server.Moderator.Calls.Single();
        Assert.Contains("사진 메모", call.Text);
        Assert.Contains("원준", call.Text);
        Assert.NotNull(call.Image);
        Assert.Equal("image/jpeg", call.Image!.MediaType);
        Assert.Equal(TinyJpeg, call.Image.Bytes);
    }

    [Fact]
    public async Task 애매하면_검토_대기로_저장되고_작성한_기기에만_보인다()
    {
        server.Moderator.Next = Review;

        var res = await PostMemoAsync(MemoForm(deviceId: "writer"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var memo = await res.Content.ReadFromJsonAsync<JsonElement>();
        string id = memo.GetProperty("id").GetString()!;
        Assert.Equal("pending", memo.GetProperty("status").GetString());
        Assert.False(memo.TryGetProperty("flagNote", out _)); // 검토 메모는 관리자만

        // 목록: 작성한 기기에만
        Assert.Single(await ListMemosAsync("writer"));
        Assert.Empty(await ListMemosAsync("other"));
        Assert.Empty(await ListMemosAsync());

        // 단건: 남에게는 404
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/memos/{id}", "writer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/memos/{id}", "other")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/memos/{id}")).StatusCode);

        // 웹 페이지에도 안 보인다
        Assert.DoesNotContain("메모 1", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task 검토_대기_메모에는_좋아요와_댓글을_달_수_없다()
    {
        server.Moderator.Next = Review;
        var res = await PostMemoAsync(MemoForm(deviceId: "writer"));
        string id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        server.Moderator.Next = null;

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/memos/{id}/like", "writer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/memos/{id}/comments", CommentJson("댓글"))).StatusCode);
    }

    [Fact]
    public async Task 작성자는_검토_대기_메모를_지울_수_있다()
    {
        server.Moderator.Next = Review;
        var res = await PostMemoAsync(MemoForm(deviceId: "writer"));
        string id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/memos/{id}", "writer")).StatusCode);
    }

    // ---------- 댓글 ----------

    [Fact]
    public async Task 댓글도_검사한다_거절이면_422_애매하면_검토_대기()
    {
        string memo = await CreateVisibleMemoAsync();

        server.Moderator.Next = new ModerationResult(Verdict.Block, ModerationReasons.Abusive, null, false);
        var rejected = await client.PostAsync($"/api/memos/{memo}/comments", CommentJson("나쁜 말"));
        Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
        Assert.Equal("abusive", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());

        server.Moderator.Next = Review;
        var pending = await client.PostAsync($"/api/memos/{memo}/comments", CommentJson("애매한 말", deviceId: "c1"));
        Assert.Equal(HttpStatusCode.Created, pending.StatusCode);
        Assert.Equal("pending", (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        // 작성 기기에만 보이고, commentCount 에는 세지 않는다
        var mine = await (await SendAsync(HttpMethod.Get, $"/api/memos/{memo}/comments", "c1")).Content.ReadFromJsonAsync<JsonElement[]>();
        var others = await (await SendAsync(HttpMethod.Get, $"/api/memos/{memo}/comments", "c2")).Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Single(mine!);
        Assert.Empty(others!);
        var m = await client.GetFromJsonAsync<JsonElement>($"/api/memos/{memo}");
        Assert.Equal(0, m.GetProperty("commentCount").GetInt32());
    }

    [Fact]
    public async Task AI_가_없을_때_금지어_검사로_전화번호와_욕설을_거른다()
    {
        // FakeModerator 의 기본값이 금지어 검사 (실제 서버에서 AI 키가 없거나 장애일 때와 같음)
        var phone = await PostMemoAsync(MemoForm(text: "분실물 찾으면 010-1234-5678 로 연락"));
        Assert.Equal((HttpStatusCode)422, phone.StatusCode);
        Assert.Equal("personal_info", (await phone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());

        var ok = await PostMemoAsync(MemoForm(text: "도서관 3층 불이 꺼져 있어요. 대출 데스크는 열려 있음"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    // ---------- 관리자 ----------

    [Fact]
    public async Task 관리자_키가_없거나_틀리면_403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/admin/pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/admin/pending", adminKey: "wrong")).StatusCode);
    }

    [Fact]
    public async Task 서버에_관리자_키가_설정되지_않으면_관리자_API_는_꺼진다()
    {
        using var noAdmin = server.WithWebHostBuilder(b => b.UseSetting("Admin:Key", ""));
        var res = await noAdmin.CreateClient().GetAsync("/api/admin/pending");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task 관리자는_검토_대기_목록을_보고_공개하거나_지운다()
    {
        string visibleMemo = await CreateVisibleMemoAsync();

        server.Moderator.Next = Review;
        var p1 = await PostMemoAsync(MemoForm(text: "검토1", deviceId: "w1", photo: TinyJpeg));
        var p2 = await PostMemoAsync(MemoForm(text: "검토2", deviceId: "w2"));
        var pc = await client.PostAsync($"/api/memos/{visibleMemo}/comments", CommentJson("검토 댓글"));
        string id1 = (await p1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        string id2 = (await p2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        string cid = (await pc.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        server.Moderator.Next = null;

        // 목록: 이유 메모(flagNote) 포함
        var pendingRes = await SendAsync(HttpMethod.Get, "/api/admin/pending", adminKey: TestServer.AdminKey);
        Assert.Equal(HttpStatusCode.OK, pendingRes.StatusCode);
        var pending = await pendingRes.Content.ReadFromJsonAsync<JsonElement>();
        var memos = pending.GetProperty("memos").EnumerateArray().ToList();
        Assert.Equal(new[] { "검토1", "검토2" }, memos.Select(m => m.GetProperty("text").GetString()));
        Assert.Contains("advertising", memos[0].GetProperty("flagNote").GetString());
        Assert.Contains("홍보일 수도", memos[0].GetProperty("flagNote").GetString());
        Assert.Equal("검토 댓글", pending.GetProperty("comments").EnumerateArray().Single().GetProperty("text").GetString());

        // 메모 공개 → 모두에게 보임, flagNote 는 사라짐
        var approved = await SendAsync(HttpMethod.Post, $"/api/admin/memos/{id1}/approve", adminKey: TestServer.AdminKey);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedMemo = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("visible", approvedMemo.GetProperty("status").GetString());
        Assert.False(approvedMemo.TryGetProperty("flagNote", out _));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/memos/{id1}", "other")).StatusCode);

        // 메모 거절 → 삭제
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/admin/memos/{id2}", adminKey: TestServer.AdminKey)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/memos/{id2}", "w2")).StatusCode);

        // 댓글 공개 → 다른 기기에도 보이고 commentCount 증가
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/comments/{cid}/approve", adminKey: TestServer.AdminKey)).StatusCode);
        var comments = await client.GetFromJsonAsync<JsonElement[]>($"/api/memos/{visibleMemo}/comments");
        Assert.Equal("visible", comments!.Single().GetProperty("status").GetString());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/memos/{visibleMemo}")).GetProperty("commentCount").GetInt32());

        // 남은 검토 대기 없음
        var after = await (await SendAsync(HttpMethod.Get, "/api/admin/pending", adminKey: TestServer.AdminKey)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(after.GetProperty("memos").EnumerateArray());
        Assert.Empty(after.GetProperty("comments").EnumerateArray());

        // 없는 것 → 404
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/admin/memos/{Guid.NewGuid()}/approve", adminKey: TestServer.AdminKey)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, $"/api/admin/comments/{Guid.NewGuid()}", adminKey: TestServer.AdminKey)).StatusCode);
    }
}
