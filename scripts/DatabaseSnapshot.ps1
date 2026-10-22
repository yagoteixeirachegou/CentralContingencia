function ConvertTo-SnapshotMap([string[]]$Lines) {
    $map = @{}
    foreach ($line in $Lines) {
        if ($line -notmatch '^([^|]+)\|([0-9]+)\|([0-9a-f]{32})$' -or $map.ContainsKey($Matches[1])) { throw 'Snapshot malformado ou com tabela duplicada.' }
        $map[$Matches[1]] = $line
    }
    if ($map.Count -eq 0) { throw 'Snapshot vazio.' }
    return $map
}
function Assert-DatabaseSnapshot {
    param([string[]]$Previous, [string[]]$Current, [string[]]$PreviousStructure,
        [string[]]$CurrentStructure, [string[]]$ExpectedStructure = @(),
        [string[]]$ExpectedNewTables = @(), [string[]]$ExcludeTables = @())
    $before = ConvertTo-SnapshotMap $Previous
    $after = ConvertTo-SnapshotMap $Current
    $oldSchema = ConvertTo-SnapshotMap $PreviousStructure
    $newSchema = ConvertTo-SnapshotMap $CurrentStructure
    $approvedSchema = if ($ExpectedStructure.Count) { ConvertTo-SnapshotMap $ExpectedStructure } else { @{} }
    foreach ($name in $ExcludeTables) { if (-not $before.ContainsKey($name)) { throw 'Exclusão não corresponde a tabela do baseline.' } }
    foreach ($name in $before.Keys) {
        if (-not $after.ContainsKey($name)) { throw "Tabela anterior ausente: $name." }
        if ($name -notin $ExcludeTables -and $before[$name] -ne $after[$name]) { throw "Dados anteriores divergentes: $name." }
        if (-not $oldSchema.ContainsKey($name) -or -not $newSchema.ContainsKey($name) -or $oldSchema[$name] -ne $newSchema[$name]) { throw "Estrutura anterior divergente: $name." }
    }
    foreach ($name in $after.Keys) { if (-not $before.ContainsKey($name) -and $name -notin $ExpectedNewTables) { throw "Tabela nova não autorizada: $name." } }
    foreach ($name in $ExpectedNewTables) {
        if ($before.ContainsKey($name) -or -not $after.ContainsKey($name)) { throw "Adição autorizada ausente ou inválida: $name." }
        if ($after[$name] -ne "$name|0|d41d8cd98f00b204e9800998ecf8427e") { throw "Tabela nova deveria estar vazia: $name." }
        if (-not $approvedSchema.ContainsKey($name) -or -not $newSchema.ContainsKey($name) -or $approvedSchema[$name] -ne $newSchema[$name]) { throw "Estrutura nova incompatível: $name." }
    }
    if ($newSchema.Count -ne $after.Count -or $oldSchema.Count -ne $before.Count) { throw 'Conjunto de estruturas divergente do conjunto de tabelas.' }
}
function Assert-SchemaSource([string[]]$CurrentRaw, [string[]]$SourceRaw) {
    $null = ConvertTo-SnapshotMap $CurrentRaw
    $null = ConvertTo-SnapshotMap $SourceRaw
    if (Compare-Object $CurrentRaw $SourceRaw) { throw 'Referência canônica não corresponde ao schema atual.' }
}
function Get-DatabaseStructureSql {
    return @'
SELECT n.nspname||'.'||c.relname, 0,
md5(jsonb_build_object('kind',c.relkind,'persistence',c.relpersistence,
'columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attname,format_type(a.atttypid,a.atttypmod),a.attnotnull,a.attidentity,a.attgenerated,pg_get_expr(d.adbin,d.adrelid)) ORDER BY a.attnum) FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
'constraints',(SELECT jsonb_agg(jsonb_build_array(x.conname,x.contype,x.convalidated,pg_get_constraintdef(x.oid)) ORDER BY x.conname) FROM pg_constraint x WHERE x.conrelid=c.oid),
'indexes',(SELECT jsonb_agg(jsonb_build_array(i.relname,x.indisvalid,x.indisready,pg_get_indexdef(x.indexrelid)) ORDER BY i.relname) FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid WHERE x.indrelid=c.oid))::text)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema') ORDER BY 1;
'@
}
