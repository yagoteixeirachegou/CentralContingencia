using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class CsvPreviewEndpointTests(AuthFixture fixture)
{
    private const string Path="/api/v1/proxies/import/preview";
    private const string Header="PERFIL;PLATAFORMA;PROXY;PORTA;USUARIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIÃO";
    private const string Row="perfil-sintetico;plataforma-sintetica;csv.example.test;8080;csv-synthetic-user;csv-synthetic-password;ATIVO;2099-12-31;BR";
    private static async Task Csrf(HttpClient client)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client,string? email=null)
    {
        await Csrf(client); Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/v1/auth/login",new {email=email??fixture.AdminEmail,password=fixture.Password})).StatusCode); await Csrf(client);
    }
    private static MultipartFormDataContent Upload(byte[] bytes,string name="file")
    {
        var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),name,"synthetic.csv");return content;
    }
    private static Task<HttpResponseMessage> Preview(HttpClient client,string csv,string query="") => client.PostAsync(Path+query,Upload(Encoding.UTF8.GetBytes(csv)));
    [PostgresTheory]
    [InlineData(";",false)][InlineData(",",true)]
    public async Task ValidPreviewDoesNotPersistAnythingAndMasksCredentials(string delimiter,bool bom)
    {
        using var client=fixture.Client(); await Login(client);
        using var scope=fixture.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var before=(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.AuthenticationAuditEvents.CountAsync());
        var csv=(bom?"\uFEFF":"")+Header.Replace(";",delimiter)+"\n"+Row.Replace(";",delimiter);
        var response=await Preview(client,csv);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("csv-synthetic-password",text);Assert.DoesNotContain("csv-synthetic-user",text);
        var result=(await response.Content.ReadFromJsonAsync<CsvPreviewResponse>())!; Assert.Equal(1,result.Summary.Valid);Assert.Equal("***",result.Items[0].PasswordMasked);
        Assert.Equal(before,(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.AuthenticationAuditEvents.CountAsync()));
    }
    [PostgresFact]
    public async Task AnonymousIs401AndManagerOperatorAre403()
    {
        using var anonymous=fixture.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await Preview(anonymous,Header+"\n"+Row)).StatusCode);
        foreach(var role in new[]{"Manager","Operator"})
        {
            using var admin=fixture.Client(); await Login(admin);
            var email=$"csv-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/v1/users",new{email,password=fixture.Password,role})).StatusCode);
            using var client=fixture.Client(); await Login(client,email);
            Assert.Equal(HttpStatusCode.Forbidden,(await Preview(client,Header+"\n"+Row)).StatusCode);
        }
    }
    [PostgresFact]
    public async Task MissingTamperedAndStaleCsrfAreRejected()
    {
        using var client=fixture.Client(); await Csrf(client);
        var stale=client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();await Login(client);
        foreach(var csrf in new string?[]{null,"tampered",stale})
        {
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");if(csrf is not null)client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);
            var response=await Preview(client,Header+"\n"+Row);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Equal("application/problem+json",response.Content.Headers.ContentType!.MediaType);
        }
    }
    [PostgresTheory]
    [InlineData("")][InlineData("\uFEFF")][InlineData("PERFIL;PLATAFORMA\n\"csv-synthetic-password")]
    public async Task MalformedFileErrorsNeverEchoContent(string csv)
    {
        using var client=fixture.Client(); await Login(client);var response=await Preview(client,csv);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.DoesNotContain("csv-synthetic-password",await response.Content.ReadAsStringAsync());
    }
    [PostgresFact]
    public async Task HeadersRowsErrorsDuplicatesAndPaginationUseOriginalLines()
    {
        using var client=fixture.Client();await Login(client);
        var response=await Preview(client,Header+"\n"+Row+"\n\n"+Row+"\n"+Row.Replace("8080","0"),"?page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=(await response.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Equal(4,result.Summary.Total);Assert.Equal(1,result.Summary.Valid);Assert.Equal(3,result.Summary.Invalid);Assert.Equal(1,result.Summary.Duplicates);
        Assert.Equal(new[]{4,5},result.Items.Select(r=>r.Line));
        var missing=(await (await Preview(client,Header.Replace("PORTA","OTHER")+"\n"+Row)).Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Contains(missing.HeaderIssues,i=>i.Code=="MissingHeader");
        var duplicate=(await (await Preview(client,Header.Replace("PORTA","PROXY")+"\n"+Row)).Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Contains(duplicate.HeaderIssues,i=>i.Code=="DuplicateHeader");
    }
    [PostgresFact]
    public async Task ConflictReaderSelectsExistingEndpointsWithoutModifyingThem()
    {
        using var scope=fixture.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var host=$"csv-{Guid.NewGuid():N}.example.test";var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        var envelope=EncryptedProxyPassword.Create("synthetic-v1",new byte[12],new byte[10],new byte[16]);
        db.Proxies.Add(Proxy.Create(id,ProxyHost.Create(host),ProxyPort.Create(8080),"synthetic-user",envelope,"BR",now.AddDays(1),now));await db.SaveChangesAsync();
        using var client=fixture.Client(); await Login(client);
        var response=await Preview(client,Header+"\n"+Row.Replace("csv.example.test",host));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=(await response.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;Assert.Equal(1,result.Summary.Conflicts);Assert.Equal(1,result.Summary.Valid);
        Assert.Equal(now,(await db.Proxies.SingleAsync(p=>p.Id==id)).UpdatedAt);
        var reader=scope.ServiceProvider.GetRequiredService<IProxyImportConflictReader>();
        var keys=Enumerable.Range(1,250).Select(i=>new ProxyEndpointKey($"other-{i}.example.test",8080)).Append(new(host,8080)).ToArray();
        Assert.Equal(new ProxyEndpointKey(host,8080),Assert.Single(await reader.FindExistingAsync(keys,default)));
    }
    [PostgresFact]
    public async Task LimitsMalformedMultipartAndMediaTypesAreEnforced()
    {
        await using var factory=fixture.Factory.WithWebHostBuilder(builder=>builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ProxyImportPreview:MaxFileBytes"]="1024",["ProxyImportPreview:MaxRows"]="1"})));
        using var client=factory.CreateClient(new(){BaseAddress=new("https://localhost")});await Login(client);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Preview(client,Header+"\n"+new string('a',1100))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Preview(client,Header+"\n"+Row+"\n"+Row)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(Path,Upload(Encoding.UTF8.GetBytes(Row),"wrong"))).StatusCode);
        using var multiple=Upload(Encoding.UTF8.GetBytes(Header+"\n"+Row));multiple.Add(new StringContent("extra"),"extra");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(Path,multiple)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await client.PostAsJsonAsync(Path,new{file="synthetic"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(Path,Upload(new byte[]{0xff,0xfe}))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Preview(client,Header+"\n"+Row,"?pageSize=101")).StatusCode);
    }
    [PostgresFact]
    public async Task TrailingSlashQuotesDatesAndInvalidFieldsUseSameSecurityAndRules()
    {
        using var client=fixture.Client();await Login(client);
        var csv=Header+"\n"+Row.Replace("perfil-sintetico","\"perfil;\"\"sintetico\"\"\"").Replace("2099-12-31","31/12/2099");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync(Path+"/",Upload(Encoding.UTF8.GetBytes(csv)))).StatusCode);
        foreach(var invalid in new[]{Row.Replace("8080","65536"),Row.Replace("2099-12-31","31/02/2099"),Row.Replace("ATIVO","DISPONIVEL"),Row.Replace("synthetic-password",new string('é',513))})
        {
            var response=await Preview(client,Header+"\n"+invalid);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var result=(await response.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;Assert.Equal(1,result.Summary.Invalid);
            Assert.All(result.Items[0].Errors,error=>Assert.Equal(2,error.Line));
        }
    }
    [PostgresFact]
    public async Task UnknownLengthUploadsStillRespectByteLimit()
    {
        await using var factory=fixture.Factory.WithWebHostBuilder(builder=>builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["ProxyImportPreview:MaxFileBytes"]="1024"})));
        using var client=factory.CreateClient(new(){BaseAddress=new("https://localhost")});await Login(client);
        using var multipart=new MultipartFormDataContent();multipart.Add(new UnknownLengthContent(Encoding.UTF8.GetBytes(Header+"\n"+new string('a',1100))),"file","synthetic.csv");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await client.PostAsync(Path,multipart)).StatusCode);
    }
    private sealed class UnknownLengthContent(byte[] bytes):HttpContent
    {
        protected override bool TryComputeLength(out long length) { length=0;return false; }
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(bytes).AsTask();
    }
    [PostgresFact]
    public async Task SensitiveFileContentNeverAppearsInCapturedLogs()
    {
        var logs=new CapturedLogs();await using var factory=fixture.Factory.WithWebHostBuilder(builder=>builder.ConfigureLogging(logging=>logging.AddProvider(logs)));
        using var client=factory.CreateClient(new(){BaseAddress=new("https://localhost")});await Login(client);logs.Lines.Clear();
        Assert.Equal(HttpStatusCode.OK,(await Preview(client,Header+"\n"+Row)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Preview(client,Header+"\n\"csv-synthetic-password")).StatusCode);
        Assert.All(logs.Lines,line=>{Assert.DoesNotContain("csv-synthetic-password",line);Assert.DoesNotContain("csv-synthetic-user",line);});
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines {get;}=new();
        public ILogger CreateLogger(string categoryName)=>new Capture(Lines);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> lines):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel logLevel)=>true;
            public void Log<TState>(LogLevel logLevel,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>lines.Enqueue(formatter(state,exception));
        }
    }
}
