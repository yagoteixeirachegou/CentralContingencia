param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath,
    [switch]$Capture,
    [string[]]$ExcludeTables = @(),
    [string]$StructureSnapshotPath,
    [string]$ExpectedStructurePath,
    [string]$CanonicalStructurePath,
    [string[]]$ExpectedNewTables = @(),
    [string[]]$ApprovedMigrations = @()
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DatabaseSnapshot.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$localRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.local')) + [IO.Path]::DirectorySeparatorChar
$snapshot = [IO.Path]::GetFullPath($SnapshotPath)
if (-not $snapshot.StartsWith($localRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Snapshots devem permanecer no diretório local ignorado pelo Git.'
}
if (-not $StructureSnapshotPath) { $StructureSnapshotPath = "$snapshot.structure.txt" }
foreach ($artifact in @($StructureSnapshotPath,$ExpectedStructurePath,$CanonicalStructurePath) | Where-Object { $_ }) {
    if (-not [IO.Path]::GetFullPath($artifact).StartsWith($localRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Artefatos de estrutura devem permanecer no diretório local.' }
}
foreach ($migration in $ApprovedMigrations) { if ($migration -notmatch '^[0-9]{14}_[A-Za-z0-9]+$') { throw 'Identificador de migration inválido.' } }
if ($Capture -and ($ApprovedMigrations.Count -or $ExpectedNewTables.Count)) { throw 'Capture não admite adições autorizadas.' }
$migrationFilter = ''
if ($ApprovedMigrations.Count) {
    $quoted = ($ApprovedMigrations | ForEach-Object { "''$_''" }) -join ','
    $migrationFilter = "WHERE ""MigrationId"" NOT IN ($quoted)"
}
$sql = @'
SELECT format('SELECT %L, count(*), md5(coalesce(string_agg(row_to_json(t)::text, '''' ORDER BY row_to_json(t)::text), '''')) FROM %I.%I t %s;', schemaname||'.'||tablename, schemaname, tablename, CASE WHEN schemaname='public' AND tablename='__EFMigrationsHistory' THEN '__FILTER__' ELSE '' END) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema') ORDER BY schemaname,tablename;
\gexec
'@
$sql = $sql.Replace('__FILTER__', $migrationFilter)
Push-Location $projectRoot
try {
    $output = @($sql | docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At')
    if ($LASTEXITCODE -ne 0) { throw 'Consulta de snapshot falhou.' }
    # psql imprime também o SQL gerado: comparar somente resultados tipados.
    $pattern = '^[^|]+\|[0-9]+\|[0-9a-f]{32}$'
    $current = @($output | Where-Object { $_ -match $pattern })
    if ($current.Count -eq 0) { throw 'Snapshot vazio ou inesperado.' }
    $structure = @((Get-DatabaseStructureSql) | docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At')
    if ($LASTEXITCODE -ne 0) { throw 'Consulta de estruturas falhou.' }
    # For a legacy baseline reconstructed from an archive, both schemas must have
    # passed the same PostgreSQL dump/restore parsing, not textual regex rewriting.
    if ($CanonicalStructurePath) {
        Assert-SchemaSource -CurrentRaw $structure -SourceRaw @(Get-Content -LiteralPath "$CanonicalStructurePath.source.txt" -Encoding UTF8)
        $canonical = @(Get-Content -LiteralPath $CanonicalStructurePath -Encoding UTF8)
        $rawMap = ConvertTo-SnapshotMap $structure
        $canonicalMap = ConvertTo-SnapshotMap $canonical
        if (Compare-Object @($rawMap.Keys) @($canonicalMap.Keys)) { throw 'Conjunto de schema canônico divergente.' }
        $structure = $canonical
    }
    if ($ApprovedMigrations.Count) {
        $ids = ($ApprovedMigrations | ForEach-Object { "'$_'" }) -join ','
        $history = @("SELECT ""MigrationId"", ""ProductVersion"" FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" IN ($ids) ORDER BY ""MigrationId"";" | docker compose exec -T postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At')
        if ($LASTEXITCODE -ne 0 -or (Compare-Object @($ApprovedMigrations | ForEach-Object { "$_|10.0.8" }) $history)) { throw 'Registros das migrations autorizadas divergentes.' }
    }
    if ($Capture) {
        $null = ConvertTo-SnapshotMap $current
        $null = ConvertTo-SnapshotMap $structure
        $current | Set-Content -LiteralPath $snapshot -Encoding UTF8
        $structure | Set-Content -LiteralPath $StructureSnapshotPath -Encoding UTF8
        Write-Output 'Snapshot completo de dados e estruturas capturado; nenhum conteúdo de registro exposto.'
    } else {
        $previous = @(Get-Content -LiteralPath $snapshot -Encoding UTF8)
        $oldStructure = @(Get-Content -LiteralPath $StructureSnapshotPath -Encoding UTF8)
        $expected = if ($ExpectedStructurePath) { @(Get-Content -LiteralPath $ExpectedStructurePath -Encoding UTF8) } else { @() }
        Assert-DatabaseSnapshot -Previous $previous -Current $current -PreviousStructure $oldStructure -CurrentStructure $structure -ExpectedStructure $expected -ExpectedNewTables $ExpectedNewTables -ExcludeTables $ExcludeTables
        Write-Output 'Snapshot confirmado: dados e estruturas anteriores preservados; todas as adições autorizadas validadas.'
    }
} finally { Pop-Location }
