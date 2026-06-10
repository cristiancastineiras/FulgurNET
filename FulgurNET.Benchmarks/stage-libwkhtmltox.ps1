# stages libwkhtmltox.dll next to the benchmark output.
#
# Why: DinkToPdf's native P/Invoke looks for "libwkhtmltox.dll" (the
# Unix-style name). On Windows, the wkhtmltopdf installer drops the
# file as "wkhtmltox.dll" instead. We find the source file in a few
# common install locations and copy it to the requested destination
# with the correct name. First existing source wins.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$candidates = @(
    'C:\wkhtmltopdf\wkhtmltox.dll',
    'C:\Program Files\wkhtmltopdf\bin\wkhtmltox.dll',
    'C:\Program Files (x86)\wkhtmltopdf\bin\wkhtmltox.dll'
)

$src = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($src) {
    Copy-Item -LiteralPath $src -Destination $Destination -Force
    Write-Host "stage-libwkhtmltox: copied $src -> $Destination"
} else {
    Write-Host "stage-libwkhtmltox: no wkhtmltox.dll found in candidate paths (DinkToPdf benchmark will fail at runtime)"
    foreach ($c in $candidates) { Write-Host "  - tried: $c" }
    exit 0  # do not fail the build; the benchmark reports the issue at runtime
}
