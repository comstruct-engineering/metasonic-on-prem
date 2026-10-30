#Requires -Version 3.0
<#
.SYNOPSIS
    Standalone PowerShell script for BRZ & MetaSonic inspection, OData calls, SQL queries, and Comstruct master data sync.
    Requires ZERO compilation or Visual Studio Build Tools — runs out-of-the-box on any Windows Server.

.EXAMPLE
    .\brz-sync.ps1 test
    .\brz-sync.ps1 tables
    .\brz-sync.ps1 query "SELECT TOP 20 * FROM Kreditoren"
    .\brz-sync.ps1 odata Kostenstellen
    .\brz-sync.ps1 odata-sync Kostenstellen
    .\brz-sync.ps1 sync
#>
param(
    [Parameter(Position = 0)]
    [string]$Command = "test",

    [Parameter(Position = 1)]
    [string]$Arg1 = "",

    [Parameter(Position = 2)]
    [string]$Arg2 = ""
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Load config.env if present in the script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigFile = Join-Path $ScriptDir "config.env"
if (Test-Path $ConfigFile) {
    Get-Content $ConfigFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith("#")) {
            $parts = $line -split "=", 2
            if ($parts.Length -eq 2) {
                $name = $parts[0].Trim()
                $val = $parts[1].Trim()
                if ($name) {
                    [Environment]::SetEnvironmentVariable($name, $val, "Process")
                }
            }
        }
    }
}

function Get-EnvOrDefault([string]$key, [string]$defaultVal = "") {
    $v = [Environment]::GetEnvironmentVariable($key)
    if ([string]::IsNullOrWhiteSpace($v)) { return $defaultVal }
    return $v.Trim()
}

function Open-BrzDbConnection {
    $provider = (Get-EnvOrDefault "BRZ_DB_PROVIDER" "odbc").ToLowerInvariant()
    $connStr = Get-EnvOrDefault "BRZ_DB_CONNECTION_STRING" ""
    if (-not $connStr) {
        throw "BRZ_DB_CONNECTION_STRING is not set in config.env."
    }

    switch ($provider) {
        "odbc" {
            $conn = New-Object System.Data.Odbc.OdbcConnection($connStr)
        }
        "oledb" {
            $conn = New-Object System.Data.OleDb.OleDbConnection($connStr)
        }
        default {
            $conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
        }
    }
    $conn.Open()
    return $conn
}

function Invoke-BrzSql([System.Data.Common.DbConnection]$conn, [string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = [int](Get-EnvOrDefault "BRZ_DB_TIMEOUT_SECONDS" "60")
    $cmd.CommandText = $sql
    $adapter = $null
    $table = New-Object System.Data.DataTable
    $reader = $cmd.ExecuteReader()
    try {
        $table.Load($reader)
    } finally {
        $reader.Close()
    }
    return ,$table
}

function Invoke-ComstructPost([string]$endpoint, $payload) {
    $baseUrl = (Get-EnvOrDefault "COMSTRUCT_NEST_URL" "https://api.comstruct.com").TrimEnd('/')
    $prefix = (Get-EnvOrDefault "COMSTRUCT_API_PREFIX" "v1").Trim('/')
    $apiKey = Get-EnvOrDefault "COMSTRUCT_API_KEY" ""
    if (-not $apiKey) {
        throw "COMSTRUCT_API_KEY is not set in config.env."
    }

    $url = "$baseUrl/$prefix/$($endpoint.TrimStart('/'))"
    $json = ConvertTo-Json -InputObject $payload -Depth 10 -Compress
    $headers = @{
        "x-api-key" = $apiKey
        "Accept"    = "application/json"
    }
    return Invoke-RestMethod -Method Post -Uri $url -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -ContentType "application/json; charset=utf-8"
}

switch ($Command.ToLowerInvariant()) {
    "test" {
        Write-Host "=== BRZ Database Connection Test (PowerShell) ===" -ForegroundColor Cyan
        $conn = Open-BrzDbConnection
        try {
            Write-Host "Connected to '$($conn.Database)' on '$($conn.DataSource)'" -ForegroundColor Green
            $dt = Invoke-BrzSql $conn "SELECT 1 AS TestValue"
            Write-Host "SELECT 1 returned: $($dt.Rows[0][0])" -ForegroundColor Green
        } finally {
            $conn.Close()
        }
    }

    "tables" {
        Write-Host "=== BRZ Database Tables & Views ===" -ForegroundColor Cyan
        $conn = Open-BrzDbConnection
        try {
            $schema = $conn.GetSchema("Tables")
            if ($Arg1) {
                $schema = $schema | Where-Object { $_.TABLE_NAME -match $Arg1 -or $_.TABLE_SCHEMA -match $Arg1 }
            }
            $schema | Select-Object TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE | Sort-Object TABLE_NAME | Format-Table -AutoSize
        } finally {
            $conn.Close()
        }
    }

    "query" {
        if (-not $Arg1) {
            throw "Usage: .\brz-sync.ps1 query `"SELECT TOP 10 * FROM ...`" [output.csv]"
        }
        Write-Host "=== BRZ SQL Query ===" -ForegroundColor Cyan
        $conn = Open-BrzDbConnection
        try {
            $dt = Invoke-BrzSql $conn $Arg1
            Write-Host "Rows returned: $($dt.Rows.Count)" -ForegroundColor Green
            $dt | Select-Object -First 25 | Format-Table -AutoSize
            if ($Arg2) {
                $dt | Export-Csv -Path $Arg2 -Delimiter ";" -NoTypeInformation -Encoding UTF8
                Write-Host "Exported to CSV: $Arg2" -ForegroundColor Green
            }
        } finally {
            $conn.Close()
        }
    }

    "odata" {
        $resource = if ($Arg1) { $Arg1 } else { "Kostenstellen" }
        $baseUrl = (Get-EnvOrDefault "BRZ_ODATA_URL" "http://127.0.0.1/api/stammdaten/").TrimEnd('/') + "/"
        $user = Get-EnvOrDefault "BRZ_ODATA_USER" ""
        $pass = Get-EnvOrDefault "BRZ_ODATA_PASSWORD" ""
        $parent = Get-EnvOrDefault "BRZ_ODATA_PARENT" ""

        $url = if ($resource -match "^https?://") { $resource } elseif ($resource -match "\?") { "$baseUrl$resource" } elseif ($parent) { "${baseUrl}${resource}?`$filter=Ident eq '$parent'" } else { "${baseUrl}${resource}" }
        Write-Host "GET $url" -ForegroundColor Cyan

        $headers = @{ "Accept" = "application/json" }
        if ($user) {
            $token = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${user}:${pass}"))
            $headers["Authorization"] = "Basic $token"
        }

        $resp = Invoke-RestMethod -Method Get -Uri $url -Headers $headers -TimeoutSec 30
        $rows = if ($null -ne $resp.value) { $resp.value } elseif ($null -ne $resp.data) { $resp.data } else { $resp }
        $rows | Select-Object -First 25 | Format-Table -AutoSize
        if ($Arg2) {
            $rows | Export-Csv -Path $Arg2 -Delimiter ";" -NoTypeInformation -Encoding UTF8
            Write-Host "Exported to CSV: $Arg2" -ForegroundColor Green
        }
    }

    "odata-sync" {
        $resource = if ($Arg1) { $Arg1 } else { "Kostenstellen" }
        $dry = (Get-EnvOrDefault "DMS_WORKER_DRY_RUN" "false") -match "^(1|true)$"
        $baseUrl = (Get-EnvOrDefault "BRZ_ODATA_URL" "http://127.0.0.1/api/stammdaten/").TrimEnd('/') + "/"
        $user = Get-EnvOrDefault "BRZ_ODATA_USER" ""
        $pass = Get-EnvOrDefault "BRZ_ODATA_PASSWORD" ""
        $parent = Get-EnvOrDefault "BRZ_ODATA_PARENT" ""

        $url = if ($parent) { "${baseUrl}${resource}?`$filter=Ident eq '$parent'" } else { "${baseUrl}${resource}" }
        Write-Host "=== BRZ OData -> Comstruct Sync ($url, dryRun=$dry) ===" -ForegroundColor Cyan

        $headers = @{ "Accept" = "application/json" }
        if ($user) {
            $token = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${user}:${pass}"))
            $headers["Authorization"] = "Basic $token"
        }

        $resp = Invoke-RestMethod -Method Get -Uri $url -Headers $headers -TimeoutSec 30
        $rows = if ($null -ne $resp.value) { $resp.value } elseif ($null -ne $resp.data) { $resp.data } else { @($resp) }
        $count = 0
        foreach ($r in $rows) {
            $kst = if ($r.KstNr) { "$($r.KstNr)" } elseif ($r.Kostenstelle) { "$($r.Kostenstelle)" } else { "$($r.project_number)" }
            $bez = if ($r.Bezeichnung) { "$($r.Bezeichnung)" } elseif ($r.Gruppename) { "$($r.Gruppename)" } else { $kst }
            if (-not $kst) { continue }
            $proj = @{
                external_id    = $kst
                project_number = $kst
                title          = $bez
                active         = $true
            }
            if ($dry) {
                Write-Host "  [dry-run] Project: $kst - $bez"
            } else {
                Invoke-ComstructPost "projects" $proj | Out-Null
            }
            $count++
        }
        Write-Host "Processed $count project(s)." -ForegroundColor Green
    }

    "sync" {
        $dry = (Get-EnvOrDefault "DMS_WORKER_DRY_RUN" "false") -match "^(1|true)$"
        Write-Host "=== BRZ DB -> Comstruct Master Data Sync (dryRun=$dry) ===" -ForegroundColor Cyan
        $conn = Open-BrzDbConnection
        try {
            $supSql = Get-EnvOrDefault "BRZ_SQL_SUPPLIERS_QUERY" ""
            if ($supSql) {
                $dt = Invoke-BrzSql $conn $supSql
                $suppliers = @()
                foreach ($row in $dt.Rows) {
                    $extId = "$($row['external_id'])".Trim()
                    $title = "$($row['title'])".Trim()
                    if (-not $extId -or -not $title) { continue }
                    $s = @{
                        external_id  = $extId
                        title        = $title
                        country_code = if ($dt.Columns.Contains("country_code") -and $row["country_code"]) { "$($row['country_code'])".Trim() } else { "DE" }
                        is_active    = $true
                    }
                    if ($dt.Columns.Contains("address") -and $row["address"]) { $s["address"] = "$($row['address'])".Trim() }
                    if ($dt.Columns.Contains("city") -and $row["city"]) { $s["city"] = "$($row['city'])".Trim() }
                    if ($dt.Columns.Contains("zip") -and $row["zip"]) { $s["zip"] = "$($row['zip'])".Trim() }
                    if ($dt.Columns.Contains("legal_uid") -and $row["legal_uid"]) { $s["legal_uid"] = "$($row['legal_uid'])".Trim() }
                    if ($dt.Columns.Contains("iban") -and $row["iban"]) {
                        $iban = ("$($row['iban'])" -replace "\s+", "")
                        if ($iban) { $s["supplier_bank_details"] = @(@{ iban = $iban; external_id = $iban }) }
                    }
                    $suppliers += $s
                }
                Write-Host "Suppliers mapped: $($suppliers.Count)" -ForegroundColor Green
                if (-not $dry -and $suppliers.Count -gt 0) {
                    Invoke-ComstructPost "suppliers" $suppliers | Out-Null
                    Write-Host "Suppliers synced to Comstruct." -ForegroundColor Green
                }
            }

            $projSql = Get-EnvOrDefault "BRZ_SQL_PROJECTS_QUERY" ""
            if ($projSql) {
                $dt = Invoke-BrzSql $conn $projSql
                $count = 0
                foreach ($row in $dt.Rows) {
                    $num = "$($row['project_number'])".Trim()
                    $title = "$($row['title'])".Trim()
                    if (-not $num) { continue }
                    $p = @{
                        external_id    = if ($dt.Columns.Contains("external_id") -and $row["external_id"]) { "$($row['external_id'])".Trim() } else { $num }
                        project_number = $num
                        title          = if ($title) { $title } else { $num }
                        active         = $true
                    }
                    if (-not $dry) {
                        Invoke-ComstructPost "projects" $p | Out-Null
                    }
                    $count++
                }
                Write-Host "Projects processed: $count" -ForegroundColor Green
            }
        } finally {
            $conn.Close()
        }
    }

    "dms-dbs" {
        $ep = Get-EnvOrDefault "METASONIC_ENDPOINT" "http://127.0.0.1:8089/ScanviewWebservice.svc"
        Write-Host "=== MetaSonic: GetLoginConfigurations ($ep) ===" -ForegroundColor Cyan
        $soap = '<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><GetLoginConfigurations xmlns="http://www.allgeier.com/scanview"/></s:Body></s:Envelope>'
        $resp = Invoke-WebRequest -Uri $ep -Method Post -ContentType "text/xml; charset=utf-8" -Headers @{ SOAPAction = '"http://www.allgeier.com/scanview/IScanviewWebservice/GetLoginConfigurations"' } -Body ([System.Text.Encoding]::UTF8.GetBytes($soap)) -UseBasicParsing
        [xml]$xml = $resp.Content
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace("a", "http://schemas.microsoft.com/2003/10/Serialization/Arrays")
        $dbs = $xml.SelectNodes("//a:string", $ns) | ForEach-Object { $_.InnerText }
        Write-Host "Available MetaSonic Databases (METASONIC_DB):" -ForegroundColor Green
        $dbs | ForEach-Object { Write-Host "  - $_" }
    }

    default {
        Write-Host "Commands: test | tables [filter] | query `"<SQL>`" [out.csv] | odata [endpoint] [out.csv] | odata-sync [endpoint] | sync | dms-dbs" -ForegroundColor Yellow
    }
}
