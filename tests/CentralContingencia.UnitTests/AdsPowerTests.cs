using System.Net;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.AdsPower;
using CentralContingencia.Infrastructure.AdsPower;
using Microsoft.Extensions.Options;

namespace CentralContingencia.UnitTests;

public sealed class AdsPowerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Current;
    }
    private sealed class FakeSource(AdsPowerSnapshot snapshot) : IAdsPowerProfileSource
    {
        public int Reads { get; private set; }
        public Task<AdsPowerSnapshot> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult(snapshot); }
    }
    private static AdsPowerProfile Profile(string id, string groupId, string groupName, string[]? tags = null, int? createdDaysAgo = null) =>
        new(id, null, "Perfil " + id, groupId, groupName, tags ?? [], createdDaysAgo is { } days ? Now.AddDays(-days) : null);

    [Fact]
    public async Task Counts_profiles_per_group_including_empty_and_unlisted_groups()
    {
        var snapshot = new AdsPowerSnapshot([new("1", "ESTEIRA DE PERFIL"), new("2", "BM"), new("3", "VAZIO")],
            [Profile("a", "1", "ESTEIRA DE PERFIL"), Profile("b", "1", "ESTEIRA DE PERFIL"), Profile("c", "1", "ESTEIRA DE PERFIL"),
             Profile("d", "2", "BM"), Profile("e", "9", "SEM CADASTRO")]);
        var dashboard = await new AdsPowerDashboardService(new FakeSource(snapshot), new AdsPowerOptions(), new Clock(Now)).GetAsync(false, default);
        Assert.Equal(5, dashboard.Total);
        Assert.Equal([("ESTEIRA DE PERFIL", 3), ("BM", 1), ("SEM CADASTRO", 1), ("VAZIO", 0)], dashboard.Groups.Select(g => (g.GroupName, g.Total)));
        Assert.Equal(dashboard.Total, dashboard.Groups.Sum(g => g.Total)); Assert.Equal(5, dashboard.Profiles.Count);
    }

    [Fact]
    public async Task Aggregates_tags_ignoring_case_and_exposes_group_age_from_oldest_and_newest_profile()
    {
        var snapshot = new AdsPowerSnapshot([new("1", "FB | FLAG"), new("2", "VAZIO")],
            [Profile("a", "1", "FB | FLAG", ["ESTEIRA DE PERFIL", " flag "], 400), Profile("b", "1", "FB | FLAG", ["esteira de perfil", "ESTEIRA DE PERFIL"], 10),
             Profile("c", "1", "FB | FLAG", [], null), Profile("d", "1", "FB | FLAG", ["FLAG", ""], 90)]);
        var dashboard = await new AdsPowerDashboardService(new FakeSource(snapshot), new AdsPowerOptions(), new Clock(Now)).GetAsync(false, default);
        Assert.Equal([("ESTEIRA DE PERFIL", 2), ("flag", 2)], dashboard.Tags.Select(t => (t.Name, t.Total)));
        Assert.Equal(["ESTEIRA DE PERFIL"], dashboard.Profiles.Single(p => p.ProfileId == "b").Tags); // duplicadas colapsam
        Assert.Equal(["flag"], dashboard.Profiles.Single(p => p.ProfileId == "d").Tags); // grafia canônica, vazias descartadas
        var group = dashboard.Groups.Single(g => g.GroupId == "1");
        Assert.Equal(Now.AddDays(-400), group.OldestCreatedAt); Assert.Equal(Now.AddDays(-10), group.NewestCreatedAt);
        var empty = dashboard.Groups.Single(g => g.GroupId == "2"); Assert.Null(empty.OldestCreatedAt); Assert.Null(empty.NewestCreatedAt);
    }

    [Fact]
    public async Task Serves_cache_until_it_expires_and_refresh_bypasses_it()
    {
        var source = new FakeSource(new([], [Profile("a", "1", "G")])); var clock = new Clock(Now);
        var service = new AdsPowerDashboardService(source, new AdsPowerOptions { CacheMinutes = 5 }, clock);
        Assert.False((await service.GetAsync(false, default)).FromCache);
        clock.Current = Now.AddMinutes(4); Assert.True((await service.GetAsync(false, default)).FromCache); Assert.Equal(1, source.Reads);
        Assert.False((await service.GetAsync(true, default)).FromCache); Assert.Equal(2, source.Reads);
        clock.Current = Now.AddMinutes(10); Assert.False((await service.GetAsync(false, default)).FromCache); Assert.Equal(3, source.Reads);
    }

    [Fact]
    public void Options_reject_invalid_values() =>
        Assert.Throws<InvalidOperationException>(() => new AdsPowerOptions { CacheMinutes = -1 }.Validate());

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Requests.Add(request); return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static AdsPowerLocalApiClient Client(Handler handler, Action<AdsPowerConnectionOptions>? configure = null)
    {
        var options = new AdsPowerConnectionOptions { RequestIntervalMs = 0 }; configure?.Invoke(options);
        return new(new HttpClient(handler), Options.Create(options));
    }

    [Fact]
    public async Task Reads_all_pages_sends_api_key_and_never_exposes_account_credentials()
    {
        const string secretPassword = "synthetic-account-password"; const string secretUser = "synthetic-account-user"; const string apiKey = "synthetic-api-key";
        var handler = new Handler(request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (request.RequestUri.AbsolutePath.EndsWith("group/list"))
                return Json(new { code = 0, data = new { list = new[] { new { group_id = "1", group_name = "ESTEIRA DE PERFIL", remark = "synthetic-remark" } } } });
            var page = int.Parse(query["page"]!);
            var items = Enumerable.Range(0, page == 1 ? 2 : 1).Select(i => new Dictionary<string, object>
            {
                ["user_id"] = $"u{page}{i}", ["serial_number"] = 100 + i, ["name"] = $"Perfil {page}{i}", ["group_id"] = "1", ["group_name"] = "ESTEIRA DE PERFIL",
                ["created_time"] = "1612520997", ["last_open_time"] = "1621333030", ["fbcc_user_tag"] = new[] { new { name = "BACKUP", color = "red", id = "1" }, new { name = "TTX", color = "blue", id = "2" } }, ["password"] = secretPassword, ["username"] = secretUser, ["ip"] = "203.0.113.9", ["remark"] = "synthetic-remark",
            }).ToArray();
            return Json(new { code = 0, data = new { list = items, page, page_size = 2 } });
        });
        var snapshot = await Client(handler, o => { o.ApiKey = apiKey; o.ProfilePageSize = 2; }).ReadAsync(default);
        Assert.Equal(3, snapshot.Profiles.Count); Assert.Single(snapshot.Groups);
        Assert.Equal("100", snapshot.Profiles[0].SerialNumber);
        Assert.All(snapshot.Profiles, p => { Assert.Equal(["BACKUP", "TTX"], p.Tags); Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1612520997), p.CreatedAt); });
        Assert.All(handler.Requests, r => Assert.Equal("Bearer " + apiKey, r.Headers.Authorization!.ToString()));
        Assert.Equal(["page=1&page_size=200", "page=1&page_size=2", "page=2&page_size=2"], handler.Requests.Select(r => r.RequestUri!.Query.TrimStart('?')));
        var dump = JsonSerializer.Serialize(snapshot) + string.Join(' ', snapshot.Profiles.Select(p => p.ToString()));
        foreach (var secret in new[] { secretPassword, secretUser, "203.0.113.9", "synthetic-remark", apiKey }) Assert.DoesNotContain(secret, dump);
    }

    [Fact]
    public async Task Reads_every_profile_across_many_pages()
    {
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("group/list")) return Json(new { code = 0, data = new { list = Array.Empty<object>() } });
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"]!);
            var items = Enumerable.Range((page - 1) * 100, Math.Max(0, Math.Min(100, 297 - (page - 1) * 100))).Select(i => new { user_id = "u" + i, name = "Perfil " + i, group_id = "1", group_name = "G" }).ToArray();
            return Json(new { code = 0, data = new { list = items } });
        });
        var snapshot = await Client(handler).ReadAsync(default);
        Assert.Equal(297, snapshot.Profiles.Count); Assert.Equal(297, snapshot.Profiles.Select(p => p.UserId).Distinct().Count());
        Assert.Equal(3, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("user/list")));
    }

    private static Handler GroupHandler(Func<string, object[]> profilesOf, params (string Id, string Name)[] groups) => new(request =>
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        if (request.RequestUri.AbsolutePath.EndsWith("group/list"))
            return Json(new { code = 0, data = new { list = groups.Select(g => new { group_id = g.Id, group_name = g.Name }).ToArray() } });
        var page = int.Parse(query["page"]!); var all = profilesOf(query["group_id"] ?? "");
        return Json(new { code = 0, data = new { list = all.Skip((page - 1) * 100).Take(100).ToArray() } });
    });

    [Fact]
    public async Task Reads_the_highest_profile_number_of_one_group_only_with_tolerant_names_and_without_credentials()
    {
        var handler = GroupHandler(groupId => groupId == "10477650"
            ? [new { user_id = "a", name = "Perfil 1243", password = "synthetic-secret" }, new { user_id = "b", name = "Perfil 1202" }, new { user_id = "c", name = "Perfil 1250 (revisar)", username = "synthetic-user" }, new { user_id = "d", name = "Sem numero" }]
            : [new { user_id = "z", name = "Perfil 9999", password = "x" }], ("1", "GG | ESTEIRA DE PERFIL"), ("10477650", "03 ESTEIRA DE PERFIL"));
        var snapshot = await Client(handler).ReadGroupAsync("  03 esteira de perfil ", default);
        Assert.Equal(("10477650", "03 ESTEIRA DE PERFIL", 4, 1250), (snapshot.GroupId, snapshot.GroupName, snapshot.ProfileCount, snapshot.HighestProfileNumber)); // 9999 é de outro grupo
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("user/list") && r.RequestUri.Query.Contains("group_id=10477650"));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Query.Contains("group_id=1&"));
        var dump = JsonSerializer.Serialize(snapshot); Assert.DoesNotContain("synthetic", dump);
    }

    [Fact]
    public async Task Counts_every_page_of_a_large_group_and_handles_an_empty_one()
    {
        var many = Enumerable.Range(1, 250).Select(i => (object)new { user_id = "u" + i, name = "Perfil " + (1000 + i) }).ToArray();
        var big = await Client(GroupHandler(_ => many, ("7", "GRANDE"))).ReadGroupAsync("GRANDE", default);
        Assert.Equal((250, 1250), (big.ProfileCount, big.HighestProfileNumber));
        var empty = await Client(GroupHandler(_ => [], ("8", "VAZIO"))).ReadGroupAsync("VAZIO", default);
        Assert.Equal((0, 0), (empty.ProfileCount, empty.HighestProfileNumber));
    }

    [Fact]
    public async Task Refuses_a_missing_or_ambiguous_group_name()
    {
        var missing = await Assert.ThrowsAsync<AdsPowerException>(() => Client(GroupHandler(_ => [], ("1", "OUTRO"))).ReadGroupAsync("03 ESTEIRA DE PERFIL", default));
        Assert.Equal(AdsPowerFailure.GroupNotFound, missing.Failure);
        var ambiguous = await Assert.ThrowsAsync<AdsPowerException>(() => Client(GroupHandler(_ => [], ("1", "03 ESTEIRA DE PERFIL"), ("2", "03 esteira de perfil"))).ReadGroupAsync("03 ESTEIRA DE PERFIL", default));
        Assert.Equal(AdsPowerFailure.Failed, ambiguous.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AdsPowerFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AdsPowerFailure.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, AdsPowerFailure.Failed)]
    public async Task Maps_http_failures(HttpStatusCode status, AdsPowerFailure expected)
    {
        var error = await Assert.ThrowsAsync<AdsPowerException>(() => Client(new Handler(_ => new HttpResponseMessage(status))).ReadAsync(default));
        Assert.Equal(expected, error.Failure);
    }

    [Theory]
    [InlineData("Invalid API key", AdsPowerFailure.Unauthorized)]
    [InlineData("Require api-key", AdsPowerFailure.Unauthorized)]
    [InlineData("something broke", AdsPowerFailure.Failed)]
    public async Task Maps_adspower_error_codes(string message, AdsPowerFailure expected)
    {
        var error = await Assert.ThrowsAsync<AdsPowerException>(() => Client(new Handler(_ => Json(new { code = -1, msg = message, data = new { } }))).ReadAsync(default));
        Assert.Equal(expected, error.Failure);
    }

    [Fact]
    public async Task Retries_once_when_rate_limited()
    {
        var calls = 0;
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("user/list")) return Json(new { code = 0, data = new { list = Array.Empty<object>() } });
            return ++calls == 1 ? Json(new { code = -1, msg = "Too many request per second" }) : Json(new { code = 0, data = new { list = Array.Empty<object>() } });
        });
        var snapshot = await Client(handler, o => o.RequestIntervalMs = 0).ReadAsync(default);
        Assert.Empty(snapshot.Groups); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("https://127.0.0.1:50325")]
    [InlineData("http://example.com:50325")]
    [InlineData("not a url")]
    public async Task Rejects_non_local_or_invalid_base_urls(string baseUrl)
    {
        var handler = new Handler(_ => Json(new { code = 0, data = new { list = Array.Empty<object>() } }));
        var error = await Assert.ThrowsAsync<AdsPowerException>(() => Client(handler, o => o.BaseUrl = baseUrl).ReadAsync(default));
        Assert.Equal(AdsPowerFailure.NotConfigured, error.Failure); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Connection_failure_is_reported_as_unreachable()
    {
        var client = new AdsPowerLocalApiClient(new HttpClient(new ThrowingHandler()), Options.Create(new AdsPowerConnectionOptions { RequestIntervalMs = 0 }));
        Assert.Equal(AdsPowerFailure.Unreachable, (await Assert.ThrowsAsync<AdsPowerException>(() => client.ReadAsync(default))).Failure);
    }
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new HttpRequestException("refused");
    }
}
