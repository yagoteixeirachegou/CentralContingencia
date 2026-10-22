using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class CsvImportEndpointTests(AuthFixture fixture)
{
    private const string PreviewPath="/api/v1/proxies/import/preview";
    private const string CommitPath="/api/v1/proxies/import/commit";
    private const string Header="PERFIL;PLATAFORMA;PROXY;PORTA;USUARIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIAO";
    private static string Csv(string? host=null)=>Header+"\n"+$"perfil;plataforma;{host??$"import-{Guid.NewGuid():N}.example.test"};8080;import-synthetic-user;import-synthetic-secret;ATIVO;2099-12-31;BR";
    private WebApplicationFactory<Program> KeyFactory(ILoggerProvider? logs=null)=>fixture.Factory.WithWebHostBuilder(builder=>
    {
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{
            ["ProxyEncryption:ActiveKeyId"]="import-test-v1",["ProxyEncryption:Keys:import-test-v1"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}));
        if(logs is not null)builder.ConfigureLogging(logging=>logging.AddProvider(logs));
    });
    private static HttpClient Client(WebApplicationFactory<Program> factory)=>factory.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
    private static async Task Csrf(HttpClient client)
    {
        var value=await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",value.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client,string? email=null)
    {
        await Csrf(client);Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/v1/auth/login",new{email=email??fixture.AdminEmail,password=fixture.Password})).StatusCode);await Csrf(client);
    }
    private static MultipartFormDataContent File(string csv)
    {var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),"file","synthetic.csv");return content;}
    private static string Query(string? region)=>region is null?"":"?defaultRegion="+Uri.EscapeDataString(region);
    private static async Task<string> Preview(HttpClient client,string csv,string? region=null)
    {var response=await client.PostAsync(PreviewPath+Query(region),File(csv));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<CsvPreviewResponse>())!.ConfirmationToken!;}
    private static async Task<HttpResponseMessage> Commit(HttpClient client,string csv,string proof,Guid key,string? region=null)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,CommitPath+Query(region)){Content=File(csv)};
        request.Headers.Add("X-Import-Confirmation",proof);request.Headers.Add("Idempotency-Key",key.ToString());return await client.SendAsync(request);
    }
    [PostgresFact]
    public async Task ReferenceYearProofCalendarPersistenceAndReplayUseSameInterpretation()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var csv = Csv().Replace("2099-12-31", "29/02");
        using var previewResponse = await client.PostAsync(PreviewPath + "?referenceYear=2096", File(csv));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Equal(new DateTimeOffset(2096, 2, 29, 0, 0, 0, TimeSpan.Zero), preview.Items[0].ExpiresAt);
        var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Send(string query)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CommitPath + query) { Content = File(csv) };
            request.Headers.Add("X-Import-Confirmation", preview.ConfirmationToken); request.Headers.Add("Idempotency-Key", key.ToString());
            return await client.SendAsync(request);
        }
        foreach (var query in new[] { "", "?referenceYear=2099" })
        {
            using var mismatch = await Send(query); Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.DoesNotContain("import-synthetic-secret", await mismatch.Content.ReadAsStringAsync());
        }
        using var response = await Send("?referenceYear=2096"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<CsvImportReport>())!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var proxy = await db.Proxies.SingleAsync(p => p.Id == report.Items[0].ProxyId);
        Assert.Equal(preview.Items[0].ExpiresAt, proxy.ExpiresAt);
        Assert.Equal("import-synthetic-secret", scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>().Decrypt(proxy.Id, proxy.Password));
        using var replay = await Send("?referenceYear=2096"); Assert.True((await replay.Content.ReadFromJsonAsync<CsvImportReport>())!.Replayed);
        using var invalidDate = await client.PostAsync(PreviewPath + "?referenceYear=2099", File(Csv().Replace("2099-12-31", "29/02")));
        var invalid = (await invalidDate.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Equal(1, invalid.Summary.Invalid); Assert.Null(invalid.Items[0].ExpiresAt);
        Assert.DoesNotContain("import-synthetic-secret", await previewResponse.Content.ReadAsStringAsync());
    }
    [PostgresFact]
    public async Task InvalidReferenceYearQueriesAreRejectedWithoutWrites()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        foreach (var query in new[] { "0", "10000", "-1", "2099.5", "", "word", "2099&referenceYear=2098" })
        {
            using var response = await client.PostAsync(PreviewPath + "?referenceYear=" + query, File(Csv()));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("import-synthetic-secret", await response.Content.ReadAsStringAsync());
        }
    }
    [PostgresFact]
    public async Task CompatibilityOptionIsBoundToProofAndPersistsIgnoredLineWithReplay()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var csv = Csv() + "\nperfil;plataforma;;;;;ATIVO;31/12;BR";
        var query = "?skipRowsWithoutConnection=true";
        using var previewResponse = await client.PostAsync(PreviewPath + query, File(csv));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<CsvPreviewResponse>())!;
        Assert.Contains(preview.Items.Last().Warnings, i => i.Code == "NoConnectionRowSkipped");
        var key = Guid.NewGuid();
        using var mismatch = await Commit(client, csv, preview.ConfirmationToken!, key);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        async Task<CsvImportReport> Send()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CommitPath + query) { Content = File(csv) };
            request.Headers.Add("X-Import-Confirmation", preview.ConfirmationToken); request.Headers.Add("Idempotency-Key", key.ToString());
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("import-synthetic-secret", text);
            return (await response.Content.ReadFromJsonAsync<CsvImportReport>())!;
        }
        var report = await Send(); Assert.Equal(1, report.Imported); Assert.Equal(1, report.Ignored); Assert.Equal("Empty", report.Items.Last().Outcome);
        var replay = await Send(); Assert.True(replay.Replayed); Assert.Equal(report.ImportId, replay.ImportId);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var batch = await db.ProxyImportBatches.Include(b => b.Lines).SingleAsync(b => b.Id == report.ImportId);
        Assert.Equal(ProxyImportOutcome.Empty, batch.Lines.Single(line => line.Line == 3).Outcome);
        var proxy = await db.Proxies.SingleAsync(p => p.Id == report.Items.First().ProxyId);
        Assert.Equal("import-synthetic-secret", scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>().Decrypt(proxy.Id, proxy.Password));
        using var invalidQuery = await client.PostAsync(PreviewPath + "?skipRowsWithoutConnection=invalid", File(csv));
        Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
    }
    [PostgresFact]
    public async Task ValidImportEncryptsAuditsAndReplaysOriginalReceiptWithoutAdditionalWrites()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var key=Guid.NewGuid();var proof=await Preview(client,csv);
        var response=await Commit(client,csv,proof,key);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var report=(await response.Content.ReadFromJsonAsync<CsvImportReport>())!;
        Assert.Equal(1,report.Imported);Assert.False(report.Replayed);Assert.NotNull(report.ImportId);Assert.True(response.Headers.CacheControl!.NoStore);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var batch=await db.ProxyImportBatches.Include(b=>b.Lines).SingleAsync(b=>b.Id==report.ImportId);Assert.Equal(key,batch.IdempotencyKey);Assert.Equal(report.Total,batch.Total);
        var row=Assert.Single(batch.Lines);var proxy=await db.Proxies.AsNoTracking().SingleAsync(p=>p.Id==row.ProxyId);
        Assert.Equal("import-synthetic-secret",scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>().Decrypt(proxy.Id,proxy.Password));
        Assert.Equal(12,proxy.Password.Nonce.Length);Assert.Equal(16,proxy.Password.Tag.Length);Assert.Single(await db.ProxyAuditEvents.Where(e=>e.ProxyId==proxy.Id).ToListAsync());
        var before=(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.ProxyImportBatches.CountAsync());
        var replay=await Commit(client,csv,proof,key);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);var replayReport=(await replay.Content.ReadFromJsonAsync<CsvImportReport>())!;
        Assert.True(replayReport.Replayed);Assert.Equal(report.ImportId,replayReport.ImportId);Assert.Equal(report.Items,replayReport.Items);
        Assert.Equal(before,(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.ProxyImportBatches.CountAsync()));
        foreach(var text in new[]{await response.Content.ReadAsStringAsync(),JsonSerializer.Serialize(batch),JsonSerializer.Serialize(await db.ProxyAuditEvents.Where(e=>e.ProxyId==proxy.Id).ToListAsync())})
        {Assert.DoesNotContain("import-synthetic-secret",text);Assert.DoesNotContain("import-synthetic-user",text);Assert.DoesNotContain(proxy.Host.Value,text);}
    }
    [PostgresFact]
    public async Task FreshKeyForSameFileSkipsExistingProxyRatherThanDuplicatingOrOverwriting()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var proof=await Preview(client,csv);
        var first=(await (await Commit(client,csv,proof,Guid.NewGuid())).Content.ReadFromJsonAsync<CsvImportReport>())!;
        var changed=csv.Replace("import-synthetic-secret","changed-synthetic-secret");
        var next=await Commit(client,changed,await Preview(client,changed),Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,next.StatusCode);var report=(await next.Content.ReadFromJsonAsync<CsvImportReport>())!;
        Assert.Equal(0,report.Imported);Assert.Equal(1,report.Conflicting);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Single(await db.ProxyAuditEvents.Where(e=>e.ProxyId==first.Items[0].ProxyId).ToListAsync());
        var original=await db.Proxies.SingleAsync(p=>p.Id==first.Items[0].ProxyId);
        Assert.Equal("import-synthetic-secret",scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>().Decrypt(original.Id,original.Password));
    }
    [PostgresFact]
    public async Task ConflictIntroducedAfterPreviewIsRecheckedAndUnrelatedEligibleRowIsImported()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var first=Csv();var csv=first+"\n"+first.Split('\n')[1].Replace("8080","3128");var proof=await Preview(client,csv);
        Assert.Equal(HttpStatusCode.OK,(await Commit(client,first,await Preview(client,first),Guid.NewGuid())).StatusCode);
        var response=await Commit(client,csv,proof,Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var report=(await response.Content.ReadFromJsonAsync<CsvImportReport>())!;Assert.Equal(1,report.Imported);Assert.Equal(1,report.Conflicting);
    }
    [PostgresTheory]
    [InlineData("csv")][InlineData("region")][InlineData("token")]
    public async Task ModifiedFileRegionOrTokenIsRejected(string change)
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var proof=await Preview(client,csv);
        var response=await Commit(client,change=="csv"?csv.Replace("8080","3128"):csv,change=="token"?"tampered":proof,Guid.NewGuid(),change=="region"?"AR":null);
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);Assert.DoesNotContain("import-synthetic-secret",await response.Content.ReadAsStringAsync());
    }
    [PostgresFact]
    public async Task SameKeyCannotBeReusedWithAnotherValidatedFile()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var key=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Commit(client,csv,await Preview(client,csv),key)).StatusCode);
        var changed=csv.Replace("8080","3128");Assert.Equal(HttpStatusCode.Conflict,(await Commit(client,changed,await Preview(client,changed),key)).StatusCode);
    }
    [PostgresFact]
    public async Task ExplicitMissingRegionSolutionIsValidatedAndBoundToPreview()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();csv=csv.Replace(";REGIAO","");csv=csv[..^3];
        var invalid=await Commit(client,csv,await Preview(client,csv),Guid.NewGuid());Assert.Equal(HttpStatusCode.UnprocessableEntity,invalid.StatusCode);
        var response=await Commit(client,csv,await Preview(client,csv,"BR"),Guid.NewGuid(),"BR");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var report=(await response.Content.ReadFromJsonAsync<CsvImportReport>())!;Assert.Equal(1,report.Imported);
        using var scope=factory.Services.CreateScope();Assert.Equal("BR",(await scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>().Proxies.SingleAsync(p=>p.Id==report.Items[0].ProxyId)).Region);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(PreviewPath+Query(" "),File(csv))).StatusCode);
    }
    [PostgresFact]
    public async Task InvalidRowsAbortEntireFileButEmptyAndDuplicateRowsCanBeIgnored()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var invalid=csv+"\n"+csv.Split('\n')[1].Replace("8080","0");
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();var before=(await db.Proxies.CountAsync(),await db.ProxyImportBatches.CountAsync());
        var response=await Commit(client,invalid,await Preview(client,invalid),Guid.NewGuid());Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
        Assert.Equal(before,(await db.Proxies.CountAsync(),await db.ProxyImportBatches.CountAsync()));
        var repeated=csv+"\n\n"+csv.Split('\n')[1];var report=(await (await Commit(client,repeated,await Preview(client,repeated),Guid.NewGuid())).Content.ReadFromJsonAsync<CsvImportReport>())!;
        Assert.Equal(1,report.Imported);Assert.Equal(2,report.Ignored);Assert.Equal(0,report.Invalid);
    }
    [PostgresTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ConcurrentImportsHaveOneWriterAndStableIdempotency(bool sameKey)
    {
        await using var factory=KeyFactory();using var first=Client(factory);using var second=Client(factory);await Login(first);await Login(second);
        var csv=Csv();var proof=await Preview(first,csv);var key=Guid.NewGuid();
        var responses=await Task.WhenAll(Commit(first,csv,proof,key),Commit(second,csv,proof,sameKey?key:Guid.NewGuid()));
        Assert.All(responses,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));var reports=await Task.WhenAll(responses.Select(r=>r.Content.ReadFromJsonAsync<CsvImportReport>()));
        if(sameKey){Assert.Equal(reports[0]!.ImportId,reports[1]!.ImportId);Assert.Single(reports,r=>r!.Replayed);}
        else{Assert.Equal(1,reports.Sum(r=>r!.Imported));Assert.Equal(1,reports.Sum(r=>r!.Conflicting));}
    }
    [PostgresFact]
    public async Task ForcedDatabaseFailureRollsBackProxiesEventsAndReceipt()
    {
        await using var factory=KeyFactory();using var client=Client(factory);await Login(client);var csv=Csv();var proof=await Preview(client,csv);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();var before=(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.ProxyImportBatches.CountAsync());
        // FK garante inserção de proxy antes da linha. Sequence confirma que o proxy
        // estava visível dentro da transação antes da falha e sobrevive ao rollback.
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE audit.import_seen_test; CREATE FUNCTION audit.reject_import_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS (SELECT 1 FROM proxies.\"Proxies\" WHERE \"Id\"=NEW.\"ProxyId\") THEN PERFORM nextval('audit.import_seen_test'); END IF; RAISE EXCEPTION 'synthetic import failure'; END $$; CREATE TRIGGER reject_import_test BEFORE INSERT ON audit.\"ProxyImportLines\" FOR EACH ROW EXECUTE FUNCTION audit.reject_import_test();");
        var key=Guid.NewGuid();
        try {
            var response=await Commit(client,csv,proof,key);Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain("import-synthetic-secret",await response.Content.ReadAsStringAsync());
            Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT is_called AS \"Value\" FROM audit.import_seen_test").SingleAsync());
        }
        finally {await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_import_test ON audit.\"ProxyImportLines\"; DROP FUNCTION audit.reject_import_test(); DROP SEQUENCE audit.import_seen_test;");}
        Assert.Equal(before,(await db.Proxies.CountAsync(),await db.ProxyAuditEvents.CountAsync(),await db.ProxyImportBatches.CountAsync()));
        Assert.Equal(HttpStatusCode.OK,(await Commit(client,csv,proof,key)).StatusCode);
    }
    [PostgresFact]
    public async Task CrudWriterWaitsForImportTransactionLock()
    {
        await using var factory=KeyFactory();using var importScope=factory.Services.CreateScope();using var writerScope=factory.Services.CreateScope();
        var importStore=importScope.ServiceProvider.GetRequiredService<IProxyImportStore>();var db=writerScope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var protector=writerScope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>();
        db.Proxies.Add(Proxy.Create(id,ProxyHost.Create($"crud-lock-{Guid.NewGuid():N}.test"),ProxyPort.Create(80),"synthetic-user",protector.Encrypt(id,"synthetic-secret"),"BR",now.AddDays(1),now));
        var transaction=await importStore.BeginAsync(default);var write=db.SaveChangesAsync();
        try
        {
            var lockedContext=importScope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();var waiting=false;
            for(var i=0;i<40&&!waiting;i++)
            {
                waiting=await lockedContext.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='relation' AND relation='proxies.\"Proxies\"'::regclass AND NOT granted) AS \"Value\"").SingleAsync();
                if(!waiting)await Task.Delay(25);
            }
            Assert.True(waiting);Assert.False(write.IsCompleted);
        }
        finally {await transaction.DisposeAsync();}
        await write;Assert.True(await db.Proxies.AnyAsync(p=>p.Id==id));
    }
    [PostgresFact]
    public async Task MissingEncryptionKeyPreventsAllWrites()
    {
        using var client=fixture.Client();await Login(client);var csv=Csv();var proof=await Preview(client,csv);
        var response=await Commit(client,csv,proof,Guid.NewGuid());Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
    }
    [PostgresFact]
    public async Task AnonymousRolesAndCsrfProtectConfirmation()
    {
        await using var factory=KeyFactory();using var admin=Client(factory);await Login(admin);var csv=Csv();var proof=await Preview(admin,csv);
        using var anonymous=Client(factory);Assert.Equal(HttpStatusCode.Unauthorized,(await Commit(anonymous,csv,proof,Guid.NewGuid())).StatusCode);
        foreach(var role in new[]{"Manager","Operator"})
        {
            var email=$"import-role-{Guid.NewGuid():N}@example.test";Assert.Equal(HttpStatusCode.Created,(await admin.PostAsJsonAsync("/api/v1/users",new{email,password=fixture.Password,role})).StatusCode);
            using var other=Client(factory);await Login(other,email);Assert.Equal(HttpStatusCode.Forbidden,(await Commit(other,csv,proof,Guid.NewGuid())).StatusCode);
        }
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await Commit(admin,csv,proof,Guid.NewGuid())).StatusCode);
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN","tampered");Assert.Equal(HttpStatusCode.BadRequest,(await Commit(admin,csv,proof,Guid.NewGuid())).StatusCode);
    }
    [PostgresFact]
    public async Task ConfirmationCryptographyBindsActorExpiryPurposeAndFingerprint()
    {
        var protector=new DataProtectionImportConfirmation(new EphemeralDataProtectionProvider());var fingerprint=SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-file"));var actor=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        var proof=protector.Issue(actor,fingerprint,now.AddMinutes(15));Assert.True(protector.Validate(proof,actor,fingerprint,now));
        Assert.False(protector.Validate(proof,Guid.NewGuid(),fingerprint,now));Assert.False(protector.Validate(proof,actor,fingerprint,now.AddMinutes(16)));
        Assert.False(protector.Validate(proof,actor,SHA256.HashData([1]),now));Assert.False(protector.Validate("tampered",actor,fingerprint,now));
        var persisted=protector.ProtectFingerprint(fingerprint);Assert.True(protector.MatchesFingerprint(persisted,fingerprint));Assert.False(protector.Validate(persisted,actor,fingerprint,now));
        Assert.False(protector.MatchesFingerprint(proof,fingerprint));
    }
    [PostgresFact]
    public async Task LogsAndHttpNeverExposeCredentialsEvenOnFailure()
    {
        var logs=new CapturedLogs();await using var factory=KeyFactory(logs);using var client=Client(factory);await Login(client);logs.Lines.Clear();var csv=Csv();var proof=await Preview(client,csv);
        var response=await Commit(client,csv,proof,Guid.NewGuid());Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.DoesNotContain("import-synthetic-secret",await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict,(await Commit(client,csv.Replace("8080","3128"),proof,Guid.NewGuid())).StatusCode);
        Assert.All(logs.Lines,line=>{Assert.DoesNotContain("import-synthetic-secret",line);Assert.DoesNotContain("import-synthetic-user",line);Assert.DoesNotContain(proof,line);});
    }
    private sealed class CapturedLogs:ILoggerProvider
    {
        public ConcurrentQueue<string> Lines {get;}=new();public ILogger CreateLogger(string category)=>new Capture(Lines);public void Dispose(){}
        private sealed class Capture(ConcurrentQueue<string> lines):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>lines.Enqueue(formatter(state,exception));
        }
    }
}
