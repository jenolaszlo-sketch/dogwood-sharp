param(
    [Parameter(Mandatory)]
    [string] $MatrixResult
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedCells = @(
    'Package consumer / win-x64 / net8.0',
    'Package consumer / win-x64 / net10.0',
    'Package consumer / linux-x64 / net8.0',
    'Package consumer / linux-x64 / net10.0',
    'Package consumer / osx-arm64 / net8.0',
    'Package consumer / osx-arm64 / net10.0'
)

if ($MatrixResult -cne 'success') {
    throw "The package-consumer matrix result is '$MatrixResult'; all six cells must succeed."
}
if (!$env:GITHUB_TOKEN -or !$env:GITHUB_API_URL -or !$env:GITHUB_REPOSITORY -or !$env:GITHUB_RUN_ID) {
    throw 'GitHub Actions context is required to verify the six-cell matrix.'
}

$uri = "$env:GITHUB_API_URL/repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/jobs?per_page=100"
$response = Invoke-RestMethod -Method Get -Uri $uri -Headers @{
    Authorization = "Bearer $env:GITHUB_TOKEN"
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'dogwood-sharp-d3-package-summary'
}

$jobs = @($response.jobs)
$consumerJobs = @($jobs | Where-Object { [string] $_.name -like 'Package consumer / *' })
if ($consumerJobs.Count -ne $expectedCells.Count) {
    throw "Expected exactly $($expectedCells.Count) package-consumer jobs; found $($consumerJobs.Count)."
}

foreach ($expected in $expectedCells) {
    $matches = @($consumerJobs | Where-Object { [string] $_.name -ceq $expected })
    if ($matches.Count -ne 1) {
        throw "The required package-consumer cell is missing or duplicated: $expected"
    }
    $conclusion = [string] $matches[0].conclusion
    if ($conclusion -ceq '') {
        throw "The package-consumer cell is still pending: $expected"
    }
    if ($conclusion -cne 'success') {
        throw "The package-consumer cell did not succeed: $expected ($conclusion)"
    }
}

Write-Host 'D3_PACKAGE_CONSUMER_SUMMARY=PASS CELLS=6'
