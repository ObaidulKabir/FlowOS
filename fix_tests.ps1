$files = Get-ChildItem -Path tests -Recurse -Include *.cs
foreach ($file in $files) {
    $content = Get-Content $file.FullName
    $changed = $false
    
    for ($i=0; $i -lt $content.Length; $i++) {
        if ($content[$i] -match "wc\.Publish\(\)") {
            $content[$i] = $content[$i] -replace "wc\.Publish\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().Publish(wc);"
            $changed = $true
        }
        if ($content[$i] -match "workflowClass\.Publish\(\)") {
            $content[$i] = $content[$i] -replace "workflowClass\.Publish\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().Publish(workflowClass);"
            $changed = $true
        }
        if ($content[$i] -match "publicWc\.Publish\(\)") {
            $content[$i] = $content[$i] -replace "publicWc\.Publish\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().Publish(publicWc);"
            $changed = $true
        }
        if ($content[$i] -match "publicWc\.SubmitForReview\(\)") {
            $content[$i] = $content[$i] -replace "publicWc\.SubmitForReview\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().SubmitForReview(publicWc);"
            $changed = $true
        }
        if ($content[$i] -match "publicWc\.ApproveAsPublic\(\)") {
            $content[$i] = $content[$i] -replace "publicWc\.ApproveAsPublic\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().ApproveAsPublic(publicWc);"
            $changed = $true
        }
        if ($content[$i] -match "wc\.SubmitForReview\(\)") {
            $content[$i] = $content[$i] -replace "wc\.SubmitForReview\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().SubmitForReview(wc);"
            $changed = $true
        }
        if ($content[$i] -match "wc\.WithdrawSubmission\(\)") {
            $content[$i] = $content[$i] -replace "wc\.WithdrawSubmission\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().WithdrawSubmission(wc);"
            $changed = $true
        }
        if ($content[$i] -match "wc\.ApproveAsPublic\(\)") {
            $content[$i] = $content[$i] -replace "wc\.ApproveAsPublic\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().ApproveAsPublic(wc);"
            $changed = $true
        }
        if ($content[$i] -match "wc\.Deprecate\(\)") {
            $content[$i] = $content[$i] -replace "wc\.Deprecate\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().Deprecate(wc);"
            $changed = $true
        }
        if ($content[$i] -match "copy\.Publish\(\)") {
            $content[$i] = $content[$i] -replace "copy\.Publish\(\)", "new FlowOS.Domain.Services.WorkflowClassManager().Publish(copy);"
            $changed = $true
        }
    }
    
    if ($changed) {
        Set-Content -Path $file.FullName -Value $content
        Write-Host "Updated $($file.Name)"
    }
}