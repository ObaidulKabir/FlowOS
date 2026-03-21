$files = Get-ChildItem -Path tests -Recurse -Include *.cs
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $changed = $false
    
    for ($i=0; $i -lt $content.Length; $i++) {
        if ($content[$i] -match "\(\) => new FlowOS.Domain.Services.WorkflowClassManager\(\)\.(\w+)\(([^)]+)\);\)") {
            $content[$i] = $content[$i] -replace "\(\) => new FlowOS.Domain.Services.WorkflowClassManager\(\)\.(\w+)\(([^)]+)\);\)", "() => new FlowOS.Domain.Services.WorkflowClassManager().`$1(`$2))"
            $changed = $true
        }
    }
    
    if ($changed) {
        Set-Content -Path $file.FullName -Value $content
        Write-Host "Updated $($file.Name)"
    }
}