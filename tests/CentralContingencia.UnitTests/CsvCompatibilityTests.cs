using System.Text;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;

namespace CentralContingencia.UnitTests;
public class CsvCompatibilityTests
{
    private const string Header = "PERFIL;PLATAFORMA;PLATAFORMA;PROXY;PORTA;USUARIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIÃO;ADsPower Lote";
    private const string Row = "perfil;FACEBOOK;;synthetic.example.test;8080;synthetic-user;synthetic-secret;ATIVO;31/12/2099;BR;lote";
    private readonly ProxyActor actor = new(Guid.NewGuid(), ["Admin"]);
    private sealed class NoConflicts : IProxyImportConflictReader
    {
        public Task<IReadOnlySet<ProxyEndpointKey>> FindExistingAsync(IReadOnlyCollection<ProxyEndpointKey> endpoints, CancellationToken token) => Task.FromResult<IReadOnlySet<ProxyEndpointKey>>(new HashSet<ProxyEndpointKey>());
    }
    private Task<CsvAnalysis> Analyze(string csv, bool skip = false, string? region = null, int? year = null) => new ProxyCsvPreviewService(new NoConflicts(), new(), TimeProvider.System).AnalyzeAsync(actor, Encoding.UTF8.GetBytes(csv), 1, 100, default, region, skip, year);
    [Theory]
    [InlineData("31/12", 2099, true, "2099-12-31")]
    [InlineData("29/02", 2096, true, "2096-02-29")]
    [InlineData("29/02", 2099, false, null)]
    [InlineData("31/04", 2099, false, null)]
    [InlineData("31/12/2098", 2099, true, "2098-12-31")]
    [InlineData("2098-12-31", 2099, true, "2098-12-31")]
    [InlineData("31/12", 2020, false, null)]
    public async Task ExplicitYearUsesCalendarAndDomainWithoutChangingCompleteDates(string date, int year, bool valid, string? expected)
    {
        using var result = await Analyze(Header + "\n" + Row.Replace("31/12/2099", date), year: year);
        Assert.Equal(valid, result.Rows[0].Valid);
        if (expected is not null) Assert.Equal(expected, result.Rows[0].ExpiresAt!.Value.ToString("yyyy-MM-dd"));
        else Assert.Null(result.Rows[0].ExpiresAt);
        Assert.Equal(valid && date.Length == 5, result.Rows[0].Warnings.Any(issue => issue.Code == "ReferenceYearApplied"));
    }
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(10000)]
    public async Task InvalidReferenceYearsAreRejected(int year)
    {
        var error = await Assert.ThrowsAsync<CsvPreviewException>(() => Analyze(Header + "\n" + Row, year: year));
        Assert.Equal(CsvPreviewFailure.InvalidReferenceYear, error.Failure);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void AbsentYearPreservesFingerprintAndExplicitYearIsBound(bool skip)
    {
        var bytes = Encoding.UTF8.GetBytes(Header + "\n" + Row);
        Assert.Equal(CsvImportFingerprint.Compute(bytes, "BR", skip), CsvImportFingerprint.Compute(bytes, "BR", skip, null));
        Assert.NotEqual(CsvImportFingerprint.Compute(bytes, "BR", skip), CsvImportFingerprint.Compute(bytes, "BR", skip, 2099));
        Assert.NotEqual(CsvImportFingerprint.Compute(bytes, "BR", skip, 2098), CsvImportFingerprint.Compute(bytes, "BR", skip, 2099));
    }
    [Fact]
    public async Task FormatElevenColumns132RecordsPreservesRegionsAndLineNumbers()
    {
        var rows = Enumerable.Range(1, 131).Select(i => Row.Replace("synthetic.example.test", $"synthetic-{i}.example.test").Replace(";BR;", i <= 128 ? ";;" : ";BR;"));
        var csv = Header + "\n" + string.Join('\n', rows) + "\nperfil;FACEBOOK;;;;;;ATIVO;31/12;;lote";
        using var result = await Analyze(csv, true, "AR");
        Assert.Equal(132, result.Preview.Summary.Total); Assert.Equal(131, result.Preview.Summary.Valid); Assert.Equal(1, result.Preview.Summary.Empty);
        Assert.Equal(128, result.Candidates.Count(c => c.Region == "AR")); Assert.Equal(3, result.Candidates.Count(c => c.Region == "BR"));
        Assert.Contains(result.Preview.HeaderIssues, i => i.Code == "IgnoredEmptyDuplicateHeader" && i.Column == 3);
        Assert.Equal(133, result.Rows.Last().Line); Assert.Contains(result.Rows.Last().Warnings, i => i.Code == "NoConnectionRowSkipped");
        Assert.All(result.Candidates, c => Assert.Equal("Active", c.Status.ToString()));
    }
    [Theory]
    [InlineData("FACEBOOK;", true)][InlineData(";FACEBOOK", true)][InlineData("FACEBOOK;OTHER", false)][InlineData("FACEBOOK;FACEBOOK", false)]
    public async Task DuplicateColumnOnlyResolvedWhenUnambiguous(string platform, bool safe)
    {
        using var result = await Analyze(Header + "\n" + Row.Replace("FACEBOOK;", platform));
        Assert.Equal(!safe, result.Preview.HeaderIssues.Any(i => i.Code == "DuplicateHeader"));
        Assert.Equal(safe ? 1 : 0, result.Preview.Summary.Valid);
    }
    [Fact]
    public async Task DuplicatePopulatedOnlyOnLaterPageRemainsAmbiguous()
    {
        var rows = Enumerable.Range(1, 101).Select(i => Row.Replace("synthetic.example.test", $"s-{i}.example.test").Replace("FACEBOOK;", i == 101 ? "FACEBOOK;OTHER" : "FACEBOOK;"));
        using var result = await Analyze(Header + "\n" + string.Join('\n', rows));
        Assert.Contains(result.Preview.HeaderIssues, i => i.Code == "DuplicateHeader"); Assert.Equal(101, result.Preview.Summary.Invalid);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task YearlessDatesAlwaysRejected(bool skip)
    {
        using var result = await Analyze(Header + "\n" + Row.Replace("31/12/2099", "31/12"), skip);
        Assert.Contains(result.Rows[0].Errors, i => i.Code == "ExpiryYearRequired" && i.Line == 2 && i.Column == 9); Assert.Empty(result.Candidates);
    }
    [Theory]
    [InlineData("", true)][InlineData("synthetic.example.test", false)]
    public async Task ExplicitSkipNeverDiscardsPartiallyFilledConnection(string host, bool ignored)
    {
        var csv = Header + $"\nperfil;FACEBOOK;;{host};;;;ATIVO;31/12;;lote";
        using var result = await Analyze(csv, true);
        Assert.Equal(ignored, result.Rows[0].Warnings.Any(i => i.Code == "NoConnectionRowSkipped"));
        Assert.Equal(ignored ? 1 : 0, result.Preview.Summary.Empty);
    }
    [Fact]
    public async Task SkipIsOptInAndBoundToFingerprint()
    {
        var csv = Header + "\nperfil;FACEBOOK;;;;;;ATIVO;31/12;;lote";
        using var result = await Analyze(csv);
        Assert.DoesNotContain(result.Rows[0].Errors, i => i.Code == "EmptyRow"); Assert.Equal(1, result.Preview.Summary.Invalid);
        var bytes = Encoding.UTF8.GetBytes(csv);
        Assert.Equal(CsvImportFingerprint.Compute(bytes, null), CsvImportFingerprint.Compute(bytes, null, false));
        Assert.NotEqual(CsvImportFingerprint.Compute(bytes, null), CsvImportFingerprint.Compute(bytes, null, true));
    }
}
