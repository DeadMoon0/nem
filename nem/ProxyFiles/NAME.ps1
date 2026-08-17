# Call nem run with the script name (without extension) and all arguments
$toolName = [System.IO.Path]::GetFileNameWithoutExtension($MyInvocation.MyCommand.Name)
nem run $toolName @args

# Propagate exit code
exit $LASTEXITCODE
