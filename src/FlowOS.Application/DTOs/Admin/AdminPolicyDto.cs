using System.Collections.Generic;

namespace FlowOS.Application.DTOs.Admin;

public class AdminPolicyDto
{
    public string PolicyName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Rules { get; set; } = new();
}
