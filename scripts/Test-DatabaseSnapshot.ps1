$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DatabaseSnapshot.ps1')
$empty = 'd41d8cd98f00b204e9800998ecf8427e'
$old = @("identity.Users|4|$empty", "public.__EFMigrationsHistory|4|$empty")
$oldSchema = @("identity.Users|0|$empty", "public.__EFMigrationsHistory|0|$empty")
$newNames = @('profiles.OperationalProfiles','allocations.ProxyAllocations','audit.OperationalEvents')
$new = @($newNames | ForEach-Object { "$_|0|$empty" })
$passed = 0
function Check([scriptblock]$Action, [bool]$Reject) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    if ($failed -ne $Reject) { throw 'Regressão do comparador falhou.' }
    $script:passed++
}
function CompareFixture([string[]]$Rows, [string[]]$Schema, [string[]]$Original = $old) {
    Assert-DatabaseSnapshot -Previous $Original -Current $Rows -PreviousStructure $oldSchema -CurrentStructure $Schema -ExpectedStructure $new -ExpectedNewTables $newNames
}
Check { CompareFixture ($old + $new) ($oldSchema + $new) } $false
Check { CompareFixture ($old + $new + "other.Unknown|0|$empty") ($oldSchema + $new + "other.Unknown|0|$empty") } $true
Check { CompareFixture ($old + $new[0..1]) ($oldSchema + $new[0..1]) } $true
Check { CompareFixture (@("identity.Users|5|$empty", $old[1]) + $new) ($oldSchema + $new) } $true
Check { CompareFixture (@('identity.Users|4|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', $old[1]) + $new) ($oldSchema + $new) } $true
Check { CompareFixture (@($old[1]) + $new) ($oldSchema + $new) } $true
Check { CompareFixture ($old + $new) (@('identity.Users|0|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', $oldSchema[1]) + $new) } $true
Check { CompareFixture ($old + $new) ($oldSchema + @($new[0],$new[1],'audit.OperationalEvents|0|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')) } $true
Check { CompareFixture ($old + @($new[0],$new[1],"audit.OperationalEvents|1|$empty")) ($oldSchema + $new) } $true
Check { CompareFixture (@($old[0],"public.__EFMigrationsHistory|5|$empty") + $new) ($oldSchema + $new) } $true
Check { CompareFixture ($old + $new + $new[0]) ($oldSchema + $new) } $true
Check { CompareFixture ($old + $new) ($oldSchema + $new) @('invalid') } $true
Check { Assert-SchemaSource $oldSchema $oldSchema } $false
Check { Assert-SchemaSource $oldSchema @('identity.Users|0|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',$oldSchema[1]) } $true
Write-Output "$passed testes de regressão do comparador aprovados."
