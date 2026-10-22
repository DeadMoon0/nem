# nem proxy: asks nem which copy of this tool to run here (installing the env
# first if it has to), then runs it itself - nem is not running while the tool
# does.
$toolName = [System.IO.Path]::GetFileNameWithoutExtension($MyInvocation.MyCommand.Name)
$resolved = @(& nem resolve $toolName --shell ps)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# NAME=value lines, split on the first '=' and never evaluated.
$tool = $null
$saved = @{}
foreach ($line in $resolved) {
    $name, $value = $line -split '=', 2
    if ($name -eq 'NEM_TOOL') { $tool = $value; continue }
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $value)
}

try {
    # Input piped into the proxy has to be handed on explicitly.
    if ($MyInvocation.ExpectingInput) { $input | & $tool @args } else { & $tool @args }
}
finally {
    # The environment belongs to the calling session; leave it as it was. A variable
    # that did not exist is removed - restoring $null would leave an empty one.
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
}
exit $LASTEXITCODE
