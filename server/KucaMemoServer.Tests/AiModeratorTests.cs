using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KucaMemoServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KucaMemoServer.Tests;

/// <summary>AiModerator: 게이트웨이 요청 모양, 응답 해석, 장애 시 금지어 검사로 대체하는지</summary>
public class AiModeratorTests
{
    /// <summary>요청을 기록하고 정해 둔 응답을 돌려주는 가짜 HTTP</summary>
    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }

    static string ChatResponse(string content) =>
        new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } } },
        }.ToJsonString();

    static FakeHandler Replying(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(ChatResponse(content), Encoding.UTF8, "application/json") }));

    static AiModerator Create(HttpMessageHandler handler, string? apiKey = "test-key", int timeoutSeconds = 5)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = apiKey,
            ["Ai:TimeoutSeconds"] = timeoutSeconds.ToString(),
        }).Build();
        return new AiModerator(new HttpClient(handler), config, NullLogger<AiModerator>.Instance);
    }

    [Fact]
    public async Task 게이트웨이에_OpenAI_형식으로_글과_사진을_보낸다()
    {
        var handler = Replying("""{"verdict":"allow","reason":"none","note":"일상 글"}""");
        var image = new ModerationImage(new byte[] { 1, 2, 3 }, "image/png");

        var result = await Create(handler).CheckAsync("공학관 자판기", image);

        Assert.Equal(Verdict.Allow, result.Verdict);
        Assert.False(result.UsedFallback);
        Assert.Equal(AiModerator.DefaultEndpoint, handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.Request.Headers.Authorization.Parameter);

        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal(AiModerator.DefaultModel, body["model"]!.GetValue<string>());
        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        var parts = body["messages"]![1]!["content"]!.AsArray();
        Assert.Contains("<post>", parts[0]!["text"]!.GetValue<string>());
        Assert.Contains("공학관 자판기", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("image_url", parts[1]!["type"]!.GetValue<string>());
        Assert.Equal("data:image/png;base64,AQID", parts[1]!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task 사진이_없으면_글만_보낸다()
    {
        var handler = Replying("""{"verdict":"allow","reason":"none","note":""}""");
        await Create(handler).CheckAsync("글만", null);
        var parts = JsonNode.Parse(handler.Body!)!["messages"]![1]!["content"]!.AsArray();
        Assert.Single(parts);
    }

    [Theory]
    [InlineData("""{"verdict":"block","reason":"personal_info","note":"전화번호"}""", Verdict.Block, "personal_info")]
    [InlineData("""{"verdict":"review","reason":"advertising","note":"홍보일 수도"}""", Verdict.Review, "advertising")]
    [InlineData("```json\n{\"verdict\": \"BLOCK\", \"reason\": \"Abusive\", \"note\": \"욕설\"}\n```", Verdict.Block, "abusive")]
    [InlineData("""판단: {"verdict":"allow","reason":"none"} 입니다""", Verdict.Allow, null)]
    // 거절인데 이유를 모르면 확실하지 않은 것으로 보고 검토 대기
    [InlineData("""{"verdict":"block","reason":"spam","note":"?"}""", Verdict.Review, null)]
    public void 응답을_해석한다(string content, Verdict verdict, string? reason)
    {
        var result = AiModerator.ParseResponse(ChatResponse(content));
        Assert.NotNull(result);
        Assert.Equal(verdict, result!.Verdict);
        Assert.Equal(reason, result.Reason);
    }

    [Theory]
    [InlineData("검사할 수 없습니다")]
    [InlineData("""{"verdict":"maybe"}""")]
    [InlineData("""{"verdict": """)]
    public void 형식이_틀린_응답은_null(string content) => Assert.Null(AiModerator.ParseResponse(ChatResponse(content)));

    [Fact]
    public void 응답_구조가_다르면_null() => Assert.Null(AiModerator.ParseResponse("""{"error":"bad"}"""));

    [Fact]
    public async Task 키가_없으면_AI_를_부르지_않고_금지어_검사()
    {
        var handler = Replying("""{"verdict":"block","reason":"abusive"}""");
        var result = await Create(handler, apiKey: null).CheckAsync("010-1234-5678", null);

        Assert.Null(handler.Request);
        Assert.True(result.UsedFallback);
        Assert.Equal(ModerationReasons.PersonalInfo, result.Reason);
    }

    [Fact]
    public async Task 서버_오류면_금지어_검사로_대체()
    {
        var handler = Replying("", HttpStatusCode.InternalServerError);
        var result = await Create(handler).CheckAsync("평범한 글", null);
        Assert.True(result.UsedFallback);
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    [Fact]
    public async Task 형식이_틀린_응답이면_금지어_검사로_대체()
    {
        var result = await Create(Replying("모르겠어요")).CheckAsync("씨 발", null);
        Assert.True(result.UsedFallback);
        Assert.Equal(Verdict.Block, result.Verdict);
        Assert.Equal(ModerationReasons.Abusive, result.Reason);
    }

    [Fact]
    public async Task 시간이_초과되면_금지어_검사로_대체()
    {
        var slow = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var started = DateTime.UtcNow;
        var result = await Create(slow, timeoutSeconds: 1).CheckAsync("평범한 글", null);

        Assert.True(result.UsedFallback);
        Assert.Equal(Verdict.Allow, result.Verdict);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task 연결이_안_되면_금지어_검사로_대체()
    {
        var broken = new FakeHandler((_, _) => throw new HttpRequestException("no route"));
        var result = await Create(broken).CheckAsync("평범한 글", null);
        Assert.True(result.UsedFallback);
    }
}

public class KeywordModeratorTests
{
    [Theory]
    [InlineData("연락 주세요 010-1234-5678", "personal_info")]
    [InlineData("01012345678 로 문자", "personal_info")]
    [InlineData("+82 10-1234-5678", "personal_info")]
    [InlineData("메일 abc.def@khu.ac.kr", "personal_info")]
    [InlineData("씨 발 진짜", "abusive")]
    [InlineData("ㅅㅂ", "abusive")]
    [InlineData("오픈채팅 들어오세요", "advertising")]
    [InlineData("광고 문의는 DM", "advertising")]
    public void 걸리면_거절(string text, string reason)
    {
        var result = KeywordModerator.Check(text);
        Assert.Equal(Verdict.Block, result.Verdict);
        Assert.Equal(reason, result.Reason);
        Assert.True(result.UsedFallback);
    }

    [Theory]
    [InlineData("공학관 1층 자판기 고장났어요")]
    [InlineData("도서관 3층 불이 꺼져 있어요")]
    [InlineData("도서 대출은 2층에서")]
    [InlineData("아까 고양이 보지 못했어요?")]
    [InlineData("시험 2026년 10월 9일 3시")]
    public void 일상_글은_통과(string text) => Assert.Equal(Verdict.Allow, KeywordModerator.Check(text).Verdict);
}
