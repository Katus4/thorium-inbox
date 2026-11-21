[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')] [string] $Repository
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function GitHub([string[]] $Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE) { throw "GitHub command failed ($($Arguments[0]), exit $LASTEXITCODE)." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $root
try {
    $owner = (GitHub @('api', 'user', '--jq', '.login')).Trim()
    if ($Repository.Split('/')[0] -ine $owner) { throw 'Use the authenticated personal account as the repository owner.' }
    foreach ($branch in @('main', 'develop')) {
        git show-ref --verify --quiet "refs/heads/$branch"
        if ($LASTEXITCODE) { throw "Create the local $branch branch first." }
    }

    $origin = git config --get remote.origin.url
    if (!$origin) {
        GitHub @('repo', 'create', $Repository, '--public', '--source', '.', '--remote', 'origin',
            '--description', 'A Windows inbox that turns PDFs into EPUBs and imports them into Thorium Reader.')
    } elseif ($origin -ine "https://github.com/$Repository.git" -and $origin -ine "https://github.com/$Repository" -and
        $origin -ine "git@github.com:$Repository.git") {
        throw 'The current origin points at a different repository.'
    }

    $visibility = (GitHub @('repo', 'view', $Repository, '--json', 'visibility', '--jq', '.visibility')).Trim()
    if ($visibility -ne 'PUBLIC') { throw 'The target repository must be public.' }
    git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin main develop
    if ($LASTEXITCODE) { throw 'Could not push main and develop.' }
    GitHub @('repo', 'edit', $Repository, '--default-branch', 'main', '--enable-issues=true')

    foreach ($branch in @('main', 'develop')) {
        $protection = @{
            required_status_checks = @{
                strict = $true
                contexts = @('Fork and branch rules', 'Windows build and tests')
            }
            enforce_admins = $false
            required_pull_request_reviews = @{
                dismiss_stale_reviews = $true
                required_approving_review_count = [int]($branch -eq 'develop')
            }
            restrictions = $null
            allow_force_pushes = $false
            allow_deletions = $false
            required_conversation_resolution = $true
        } | ConvertTo-Json -Depth 7 -Compress
        $protection | & gh api --method PUT "repos/$Repository/branches/$branch/protection" --input '-' --jq '.url'
        if ($LASTEXITCODE) { throw "Could not protect $branch." }
    }

    $forkApproval = @{ approval_policy = 'first_time_contributors' } | ConvertTo-Json -Compress
    $forkApproval | & gh api --method PUT "repos/$Repository/actions/permissions/fork-pr-contributor-approval" --input '-'
    if ($LASTEXITCODE) { throw 'Could not configure approvals for first-time fork contributors.' }

    $labels = [ordered]@{
        'conversion' = @('185c80', 'PDF rendering or EPUB packaging')
        'thorium' = @('3e4a82', 'Thorium detection or import')
        'installer' = @('8d4e23', 'Setup, uninstall, or Windows startup')
        'updates' = @('27704e', 'Update feed or installation of a new version')
        'documentation' = @('346682', 'README, instructions, or contributor documentation')
        'good first issue' = @('7057ff', 'A focused change suitable for a first contribution')
    }
    foreach ($label in $labels.GetEnumerator()) {
        GitHub @('label', 'create', $label.Key, '--repo', $Repository, '--color', $label.Value[0],
            '--description', $label.Value[1], '--force')
    }
    GitHub @('repo', 'view', $Repository, '--json', 'url,defaultBranchRef,visibility')
}
finally {
    Pop-Location
}
