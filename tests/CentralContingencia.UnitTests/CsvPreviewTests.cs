using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;

namespace CentralContingencia.UnitTests;

public class CsvPreviewTests
{
    private const string Header = "PERFIL;PLATAFORMA;PROXY;PORTA;USUÁRIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIÃO";
    private const string Row = "perfil-sintetico;plataforma-sintetica;proxy.example.test;8080;synthetic-user;synthetic-password;ATIVO;2099-12-31;BR";
    private static readonly ProxyActor Admin = new(Guid.NewGuid(), ["Admin"]);
    private sealed class Conflicts : IProxyImportConflictReader
    {
        public IReadOnlyCollection<ProxyEndpointKey> Received { get; private set; } = [];
        public IReadOnlySet<ProxyEndpointKey> Existing { get; init; } = new HashSet<ProxyEndpointKey>();
        public Task<IReadOnlySet<ProxyEndpointKey>> FindExistingAsync(IReadOnlyCollection<ProxyEndpointKey> endpoints, CancellationToken token)
        { Received = endpoints; return Task.FromResult(Existing); }
    }
    private static Task<CsvPreviewResponse> Preview(string csv, CsvPreviewLimits? limits = null, int page = 1, int pageSize = 20, Conflicts? conflicts = null) =>
        new ProxyCsvPreviewService(conflicts ?? new(), limits ?? new(), TimeProvider.System).PreviewAsync(Admin, Encoding.UTF8.GetBytes(csv), page, pageSize, default);
    [Theory]
    [InlineData(";",false)][InlineData(",",false)][InlineData(";",true)][InlineData(",",true)]
    public async Task ValidCsvSupportsDelimiterAndBom(string delimiter, bool bom)
    {
        var result = await Preview((bom ? "\uFEFF" : "") + Header.Replace(";",delimiter) + "\r\n" + Row.Replace(";",delimiter));
        Assert.Equal(delimiter,result.Delimiter); Assert.Equal(1,result.Summary.Valid); Assert.Equal(0,result.Summary.Invalid);
        Assert.Equal("Active",Assert.Single(result.Items).Status); Assert.Equal(2,result.Items[0].Line);
    }
    [Theory]
    [InlineData("")][InlineData("\uFEFF")][InlineData(" \r\n\t")]
    [InlineData("PERFIL;PLATAFORMA\n\"unterminated")]
    [InlineData("PERFIL;PLATAFORMA\nabc\"def;x")]
    [InlineData("PERFIL;PLATAFORMA\n\"abc\"suffix;x")]
    [InlineData("PERFIL,PLATAFORMA;PROXY\nabc,def;ghi")]
    [InlineData("PERFIL;PLATAFORMA\n\0;x")]
    public async Task EmptyOrMalformedFilesAreSanitized(string csv)
    {
        var error = await Assert.ThrowsAsync<CsvPreviewException>(()=>Preview(csv));
        Assert.Equal(CsvPreviewFailure.InvalidFile,error.Failure); Assert.Equal("Pré-visualização CSV recusada.",error.Message);
    }
    [Fact]
    public async Task InvalidUtf8IsRejected()
    {
        var service = new ProxyCsvPreviewService(new Conflicts(),new(),TimeProvider.System);
        Assert.Equal(CsvPreviewFailure.InvalidFile,(await Assert.ThrowsAsync<CsvPreviewException>(()=>service.PreviewAsync(Admin,new byte[]{0xff,0xfe,0},1,20,default))).Failure);
    }
    [Fact]
    public async Task QuotesDelimitersEscapesAndOriginalLinesAreRespected()
    {
        var first=Row.Replace("perfil-sintetico","\"perfil;\"\"sintetico\"\"\"");
        var second=Row.Replace("perfil-sintetico","\"perfil\nmultilinha\"").Replace("8080","3128");
        var result=await Preview(Header+"\n"+first+"\n"+second+"\n"+Row.Replace("8080","80"));
        Assert.Equal(1,result.Summary.Invalid); Assert.Equal(2,result.Summary.Valid);
        Assert.Equal(new[]{2,3,5},result.Items.Select(r=>r.Line)); Assert.Equal(4,result.Items[1].EndLine);
    }
    [Theory]
    [InlineData("PORTA","OTHER","UnknownHeader")]
    [InlineData("PORTA","PROXY","DuplicateHeader")]
    [InlineData("USUÁRIO","USUARIO","none")]
    [InlineData("REGIÃO","OTHER","RegionRequiredByModel")]
    public async Task HeaderMappingIsExplicit(string field,string replacement,string code)
    {
        var result=await Preview(Header.Replace(field,replacement)+"\n"+Row);
        if(code=="none") Assert.Equal(1,result.Summary.Valid);
        else Assert.Contains(result.HeaderIssues,i=>i.Code==code);
        if(field is "PORTA" or "REGIÃO") Assert.Equal(1,result.Summary.Invalid);
    }
    [Fact]
    public async Task UnknownAndDuplicateHeadersNeverEchoSecrets()
    {
        var result=await Preview(Header+";synthetic-password;synthetic-password\n"+Row+";ignored;ignored");
        var json=JsonSerializer.Serialize(result);
        Assert.DoesNotContain("synthetic-password",json); Assert.DoesNotContain("synthetic-user",json);
        Assert.Contains(result.HeaderIssues,i=>i.Code=="DuplicateHeader");
    }
    [Theory]
    [InlineData("8080","0")][InlineData("8080","65536")][InlineData("8080","abc")]
    [InlineData("8080","-1")][InlineData("proxy.example.test","https://invalid.test")]
    [InlineData("2099-12-31","31/02/2099")][InlineData("2099-12-31","2020-01-01")]
    [InlineData("2099-12-31","2099-12-31T12:00:00")][InlineData("ATIVO","DISPONIVEL")]
    [InlineData("synthetic-user","")][InlineData("synthetic-password","")][InlineData(";BR",";")]
    public async Task DomainAndCreationRulesRejectInvalidFields(string original,string replacement)
    {
        var result=await Preview(Header+"\n"+Row.Replace(original,replacement));
        Assert.Equal(1,result.Summary.Invalid); Assert.All(result.Items[0].Errors,e=>Assert.Equal(2,e.Line));
    }
    [Theory]
    [InlineData("31/12/2099")][InlineData("2099-12-31T12:00:00Z")][InlineData("2099-12-31T12:00:00-03:00")]
    public async Task ExplicitDatesAreAccepted(string date)
    {
        var result=await Preview(Header+"\n"+Row.Replace("2099-12-31",date));
        Assert.Equal(1,result.Summary.Valid); Assert.Equal(TimeSpan.Zero,result.Items[0].ExpiresAt!.Value.Offset);
    }
    [Fact]
    public async Task DuplicateIdentityUsesNormalizedEndpointAndUsernameNotProfileOrPassword()
    {
        var result=await Preview(Header+"\n"+Row+"\n"+Row.Replace("perfil-sintetico","different-profile").Replace("proxy.example.test","PROXY.EXAMPLE.TEST").Replace("synthetic-password","different-secret")+"\n"+Row.Replace("synthetic-user","different-user"));
        Assert.Equal(1,result.Summary.Duplicates); Assert.Equal(2,result.Summary.Valid); Assert.Equal(3,result.Items[1].Line);
    }
    [Fact]
    public async Task ExistingEndpointIsWarningRatherThanAssumedUniqueIdentity()
    {
        var reader=new Conflicts {Existing=new HashSet<ProxyEndpointKey>{new("proxy.example.test",8080)}};
        var result=await Preview(Header+"\n"+Row,conflicts:reader);
        Assert.Equal(1,result.Summary.Conflicts); Assert.Equal(1,result.Summary.Valid);
        Assert.Contains(result.Items[0].Warnings,i=>i.Code=="ExistingEndpoint"); Assert.Single(reader.Received);
    }
    [Fact]
    public async Task EmptyRowsCountAndPaginationDoesNotChangeSummary()
    {
        var csv=Header+"\n"+Row+"\n\n"+Row.Replace("8080","3128")+"\n";
        var first=await Preview(csv,pageSize:1); var third=await Preview(csv,page:3,pageSize:1);
        Assert.Equal(first.Summary,third.Summary); Assert.Equal(3,first.Summary.Total); Assert.Equal(1,first.Summary.Empty);
        Assert.Equal(4,Assert.Single(third.Items).Line);
    }
    [Fact]
    public async Task FormulaTextIsNotExecutedAndCredentialsAreAlwaysMasked()
    {
        var result=await Preview(Header+";ADsPower Lote\n"+Row.Replace("perfil-sintetico","=1+1")+";sintetico");
        Assert.Contains(result.Items[0].Warnings,i=>i.Code=="FormulaLikeText"); Assert.True(result.Items[0].AdsPowerLotPresent);
        var json=JsonSerializer.Serialize(result); Assert.DoesNotContain("synthetic-password",json); Assert.DoesNotContain("synthetic-user",json); Assert.DoesNotContain("=1+1",json);
    }
    [Theory]
    [InlineData("bytes")][InlineData("rows")][InlineData("field")][InlineData("columns")][InlineData("physical")]
    public async Task AllResourceLimitsAreEnforced(string kind)
    {
        var limits=kind switch {
            "bytes"=>new CsvPreviewLimits{MaxFileBytes=1024}, "rows"=>new CsvPreviewLimits{MaxRows=1},
            "field"=>new CsvPreviewLimits{MaxFieldCharacters=1024}, "columns"=>new CsvPreviewLimits{MaxColumns=10},
            _=>new CsvPreviewLimits{MaxRows=1,MaxPhysicalLines=1}};
        var csv=kind switch {
            "bytes"=>Header+"\n"+new string('x',1025), "field"=>Header+"\n"+new string('x',1025),
            "columns"=>Header+";EXTRA;EXTRA2", _=>Header+"\n"+Row+"\n"+Row};
        Assert.Equal(CsvPreviewFailure.LimitExceeded,(await Assert.ThrowsAsync<CsvPreviewException>(()=>Preview(csv,limits))).Failure);
    }
    [Theory]
    [InlineData("Manager")][InlineData("Operator")][InlineData("")]
    public async Task ApplicationAlsoRequiresAdmin(string role)
    {
        var service=new ProxyCsvPreviewService(new Conflicts(),new(),TimeProvider.System);
        Assert.Equal(CsvPreviewFailure.Forbidden,(await Assert.ThrowsAsync<CsvPreviewException>(()=>service.PreviewAsync(new(Guid.NewGuid(),[role]),Encoding.UTF8.GetBytes(Header+"\n"+Row),1,20,default))).Failure);
    }
    [Theory]
    [InlineData(0,20)][InlineData(1,101)][InlineData(10001,20)]
    public async Task PaginationIsBounded(int page,int pageSize)
    { Assert.Equal(CsvPreviewFailure.InvalidPage,(await Assert.ThrowsAsync<CsvPreviewException>(()=>Preview(Header+"\n"+Row,page:page,pageSize:pageSize))).Failure); }
    [Fact]
    public async Task InvalidPasswordBytesAndInvalidLimitConfigurationFailClosed()
    {
        var result=await Preview(Header+"\n"+Row.Replace("synthetic-password",new string('é',513)));
        Assert.Equal(1,result.Summary.Invalid);Assert.Contains(result.Items[0].Errors,e=>e.Column==6);
        Assert.Throws<InvalidOperationException>(()=>new CsvPreviewLimits{MaxFileBytes=1024*1024*100}.Validate());
    }
    [Fact]
    public async Task CancellationIsRespected()
    {
        var service=new ProxyCsvPreviewService(new Conflicts(),new(),TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.PreviewAsync(Admin,Encoding.UTF8.GetBytes(Header+"\n"+Row),1,20,new CancellationToken(true)));
    }
}
