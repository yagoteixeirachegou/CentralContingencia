using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Domain.Audit;

namespace CentralContingencia.UnitTests;

public class CsvImportTests
{
    private const string Header="PERFIL;PLATAFORMA;PROXY;PORTA;USUARIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIAO";
    private const string Row="perfil;plataforma;import.example.test;8080;synthetic-user;synthetic-secret;ATIVO;2099-12-31;BR";
    private static readonly ProxyActor Admin=new(Guid.NewGuid(),["Admin"]);
    private sealed class Proof:IImportConfirmationProtector
    {
        public string Issue(Guid actor,byte[] fingerprint,DateTimeOffset expires)=>actor.ToString()+Convert.ToHexString(fingerprint);
        public bool Validate(string proof,Guid actor,byte[] fingerprint,DateTimeOffset now)=>proof==Issue(actor,fingerprint,now);
        public string ProtectFingerprint(byte[] fingerprint)=>Convert.ToHexString(fingerprint);
        public bool MatchesFingerprint(string value,byte[] fingerprint)=>value==ProtectFingerprint(fingerprint);
    }
    private sealed class MemoryStore:IProxyImportStore,IProxyImportTransaction,IProxyImportConflictReader
    {
        public ProxyImportBatch? Batch;public List<Proxy> Proxies=[];public List<ProxyAuditEvent> Events=[];
        public bool Fail;public int Begins;public HashSet<ProxyEndpointKey> Existing=[];
        public Task<IProxyImportTransaction> BeginAsync(CancellationToken token){Begins++;return Task.FromResult<IProxyImportTransaction>(this);}
        public Task<ProxyImportBatch?> FindAsync(Guid actor,Guid key,CancellationToken token)=>Task.FromResult(Batch?.ActorId==actor&&Batch.IdempotencyKey==key?Batch:null);
        public Task SaveAsync(ProxyImportBatch batch,IReadOnlyList<Proxy> proxies,IReadOnlyList<ProxyAuditEvent> events,CancellationToken token)
        {
            if(Fail)throw new CsvImportException(CsvImportFailure.Persistence);
            Batch=batch;Proxies.AddRange(proxies);Events.AddRange(events);return Task.CompletedTask;
        }
        public Task<IReadOnlySet<ProxyEndpointKey>> FindExistingAsync(IReadOnlyCollection<ProxyEndpointKey> endpoints,CancellationToken token)=>Task.FromResult<IReadOnlySet<ProxyEndpointKey>>(Existing);
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Encryption:IProxyPasswordProtector
    {
        public int Calls;public bool Fail;
        public EncryptedProxyPassword Encrypt(Guid id,string password)
        {if(Fail)throw new InvalidOperationException("Encryption unavailable");Calls++;return EncryptedProxyPassword.Create("unit-v1",new byte[12],[1],new byte[16]);}
        public string Decrypt(Guid id,EncryptedProxyPassword password)=>throw new InvalidOperationException();
    }
    private static (ProxyCsvImportService Import,ProxyCsvPreviewService Preview,MemoryStore Store,Encryption Encryption) Setup(TimeProvider? time = null)
    {
        var store=new MemoryStore();var encryption=new Encryption();var proof=new Proof();var limits=new CsvPreviewLimits();
        var clock=time??TimeProvider.System;var preview=new ProxyCsvPreviewService(store,limits,clock,proof);
        return(new(preview,store,proof,()=>encryption,clock,limits),preview,store,encryption);
    }
    [Fact]
    public async Task ExplicitSkipIsAuditedReplayedAndCannotBeChangedAfterPreview()
    {
        var (import, preview, store, encryption) = Setup();
        var csv = Header + "\n" + Row + "\nperfil;plataforma;;;;;ATIVO;31/12;BR";
        var response = await preview.PreviewAsync(Admin, Bytes(csv), 1, 100, default, null, true);
        var key = Guid.NewGuid();
        var mismatch = await Assert.ThrowsAsync<CsvImportException>(() => import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, null, "unit", default));
        Assert.Equal(CsvImportFailure.Confirmation, mismatch.Failure); Assert.Equal(0, store.Begins);
        var result = await import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, null, "unit", default, true);
        Assert.Equal(1, result.Imported); Assert.Equal(1, result.Ignored); Assert.Equal("Empty", result.Items.Last().Outcome);
        Assert.Equal(ProxyImportOutcome.Empty, store.Batch!.Lines.Last().Outcome);
        var replay = await import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, null, "unit", default, true);
        Assert.True(replay.Replayed); Assert.Single(store.Proxies); Assert.Equal(1, encryption.Calls);
    }
    private static byte[] Bytes(string csv)=>Encoding.UTF8.GetBytes(csv);
    [Fact]
    public async Task ReferenceYearIsBoundRevalidatedPersistedAndReplayedWithOtherOptions()
    {
        var (import, preview, store, _) = Setup();
        var csv = Header + "\n" + Row.Replace("2099-12-31", "31/12")[..^2] + "\nperfil;plataforma;;;;;ATIVO;31/12;BR";
        var response = await preview.PreviewAsync(Admin, Bytes(csv), 1, 100, default, "AR", true, 2099);
        var key = Guid.NewGuid();
        foreach (var year in new int?[] { null, 2098 })
        {
            var error = await Assert.ThrowsAsync<CsvImportException>(() => import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, "AR", "unit", default, true, year));
            Assert.Equal(CsvImportFailure.Confirmation, error.Failure); Assert.Equal(0, store.Begins);
        }
        var first = await import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, "AR", "unit", default, true, 2099);
        Assert.Equal(1, first.Imported); Assert.Equal(1, first.Ignored);
        Assert.Equal(response.Items[0].ExpiresAt, store.Proxies[0].ExpiresAt); Assert.Equal("AR", store.Proxies[0].Region);
        var replay = await import.CommitAsync(Admin, Bytes(csv), response.ConfirmationToken!, key, "AR", "unit", default, true, 2099);
        Assert.True(replay.Replayed); Assert.Single(store.Proxies);
        var changed = await preview.PreviewAsync(Admin, Bytes(csv), 1, 100, default, "AR", true, 2098);
        Assert.Equal(CsvImportFailure.IdempotencyConflict, (await Assert.ThrowsAsync<CsvImportException>(() => import.CommitAsync(Admin, Bytes(csv), changed.ConfirmationToken!, key, "AR", "unit", default, true, 2098))).Failure);
    }
    private static async Task<string> Token(ProxyCsvPreviewService preview,string csv,string? region=null)=>(await preview.PreviewAsync(Admin,Bytes(csv),1,20,default,region)).ConfirmationToken!;
    [Fact]
    public async Task ValidFileIsEncryptedAndReceiptedAtomicallyThenReplayed()
    {
        var (import,preview,store,encryption)=Setup();var csv=Header+"\n"+Row;var key=Guid.NewGuid();var token=await Token(preview,csv);
        var first=await import.CommitAsync(Admin,Bytes(csv),token,key,null,"unit",default);
        var replay=await import.CommitAsync(Admin,Bytes(csv),token,key,null,"unit",default);
        Assert.Equal(first.ImportId,replay.ImportId);Assert.True(replay.Replayed);Assert.Single(store.Proxies);Assert.Single(store.Events);Assert.Equal(1,encryption.Calls);
        Assert.Equal(Admin.Id,store.Batch!.ActorId);Assert.Equal(1,store.Batch.Imported);Assert.DoesNotContain("synthetic-secret",JsonSerializer.Serialize(first));
    }
    [Theory]
    [InlineData("port")][InlineData("region")][InlineData("password")]
    public async Task AlteredFileOrOptionsAreRejectedBeforeTransaction(string change)
    {
        var (import,preview,store,_)=Setup();var csv=Header+"\n"+Row;var token=await Token(preview,csv);
        var changed=change=="port"?csv.Replace("8080","3128"):change=="password"?csv.Replace("synthetic-secret","other-secret"):csv;
        var error=await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(changed),token,Guid.NewGuid(),change=="region"?"AR":null,"unit",default));
        Assert.Equal(CsvImportFailure.Confirmation,error.Failure);Assert.Equal(0,store.Begins);Assert.Empty(store.Proxies);
    }
    [Fact]
    public async Task SameKeyWithAnotherValidatedFileConflicts()
    {
        var (import,preview,store,_)=Setup();var first=Header+"\n"+Row;var second=first.Replace("8080","3128");var key=Guid.NewGuid();
        await import.CommitAsync(Admin,Bytes(first),await Token(preview,first),key,null,"unit",default);
        var token=await Token(preview,second);
        Assert.Equal(CsvImportFailure.IdempotencyConflict,(await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(second),token,key,null,"unit",default))).Failure);
        Assert.Single(store.Proxies);
    }
    [Theory]
    [InlineData("Operator")][InlineData("Manager")]
    public async Task RolesAreEnforcedInApplication(string role)
    {
        var (import,_,store,_)=Setup();Assert.Equal(CsvPreviewFailure.Forbidden,(await Assert.ThrowsAsync<CsvPreviewException>(()=>import.CommitAsync(new(Guid.NewGuid(),[role]),Bytes(Header+"\n"+Row),"invalid",Guid.NewGuid(),null,"unit",default))).Failure);Assert.Equal(0,store.Begins);
    }
    [Fact]
    public async Task MissingRegionIsInvalidWithoutExplicitOption()
    {
        var (import,preview,store,_)=Setup();var csv=Header.Replace(";REGIAO","")+"\n"+Row[..^3];
        var token=await Token(preview,csv);
        var error=await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default));
        Assert.Equal(CsvImportFailure.Validation,error.Failure);Assert.Equal(1,error.Report!.Invalid);Assert.Empty(store.Proxies);
    }
    [Fact]
    public async Task ExplicitRegionFillsOnlyMissingCells()
    {
        var (import,preview,store,_)=Setup();var csv=Header+"\n"+Row[..^2]+"\n"+Row.Replace("8080","3128");
        var response=await preview.PreviewAsync(Admin,Bytes(csv),1,20,default,"AR");Assert.Equal(2,response.Summary.Valid);
        Assert.Contains(response.Items[0].Warnings,i=>i.Code=="ExplicitDefaultRegion");
        await import.CommitAsync(Admin,Bytes(csv),response.ConfirmationToken!,Guid.NewGuid(),"AR","unit",default);
        Assert.Equal(new[]{"AR","BR"},store.Proxies.Select(p=>p.Region));
    }
    [Fact]
    public async Task InvalidDuplicateDoesNotHideInvalidFields()
    {
        var (import,preview,store,_)=Setup();var csv=Header+"\n"+Row+"\n"+Row.Replace("2099-12-31","2020-01-01");
        var token=await Token(preview,csv);var error=await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default));
        Assert.Equal(1,error.Report!.Invalid);Assert.Empty(store.Proxies);
    }
    [Fact]
    public async Task EmptyAndValidDuplicateRowsAreIgnored()
    {
        var (import,preview,store,_)=Setup();var csv=Header+"\n"+Row+"\n\n"+Row;
        var token=await Token(preview,csv);var report=await import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default);
        Assert.Equal(1,report.Imported);Assert.Equal(2,report.Ignored);Assert.Single(store.Proxies);
    }
    [Fact]
    public async Task ConflictsAreReadAgainAfterPreview()
    {
        var (import,preview,store,encryption)=Setup();var csv=Header+"\n"+Row;var token=await Token(preview,csv);
        store.Existing.Add(new("import.example.test",8080));
        var report=await import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default);
        Assert.Equal(1,report.Conflicting);Assert.Equal(0,report.Imported);Assert.Equal(0,encryption.Calls);Assert.Empty(store.Proxies);
    }
    [Theory]
    [InlineData("persistence")][InlineData("encryption")]
    public async Task FailureDoesNotSavePartialState(string kind)
    {
        var (import,preview,store,encryption)=Setup();var csv=Header+"\n"+Row;var token=await Token(preview,csv);
        store.Fail=kind=="persistence";encryption.Fail=kind=="encryption";
        var error=await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default));
        Assert.Equal(kind=="persistence"?CsvImportFailure.Persistence:CsvImportFailure.EncryptionUnavailable,error.Failure);
        Assert.Empty(store.Proxies);Assert.Empty(store.Events);Assert.Null(store.Batch);
    }
    [Fact]
    public async Task ExpiryCrossedDuringConfirmationIsInvalidWithoutWriting()
    {
        var (import,preview,store,encryption)=Setup(new AdvancingClock());
        var csv=(Header+"\n"+Row).Replace("2099-12-31","2030-01-01T00:01:00Z");var token=await Token(preview,csv);
        var error=await Assert.ThrowsAsync<CsvImportException>(()=>import.CommitAsync(Admin,Bytes(csv),token,Guid.NewGuid(),null,"unit",default));
        Assert.Equal(CsvImportFailure.Validation,error.Failure);Assert.Equal(1,error.Report!.Invalid);Assert.Equal(0,encryption.Calls);Assert.Empty(store.Proxies);
    }
    private sealed class AdvancingClock:TimeProvider
    {
        private int calls;
        public override DateTimeOffset GetUtcNow()=>new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero).AddMinutes(++calls>=5?2:0);
    }
    [Fact]
    public void ReceiptRejectsInvalidStateAndMasksToString()
    {
        Assert.Throws<ArgumentException>(()=>ProxyImportBatch.Create(Admin.Id,Guid.NewGuid(),"protected",DateTimeOffset.UtcNow,"unit",[new(2,null,ProxyImportOutcome.Invalid)]));
        using var candidate=new CsvImportCandidate(2,ProxyHost.Create("import.test"),ProxyPort.Create(80),"synthetic-user","synthetic-secret","BR",ProxyStatus.Active,DateTimeOffset.UtcNow.AddDays(1));
        Assert.DoesNotContain("synthetic-secret",candidate.ToString());candidate.Dispose();Assert.Empty(candidate.Password);Assert.Empty(candidate.Username);
    }
}
