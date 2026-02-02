using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Enums;

namespace FlowOS.MCP.Tools
{
    public class GovernanceTools
    {
        private readonly FlowOSDbContext _dbContext;
        private readonly WorkflowClassValidator _validator;

        public GovernanceTools(FlowOSDbContext dbContext, WorkflowClassValidator validator)
        {
            _dbContext = dbContext;
            _validator = validator;
        }

        public async Task<CallToolResult> CreateDraft(JObject args)
        {
            try
            {
                var name = args["name"]?.ToString();
                var version = args["version"]?.ToString() ?? "0.1.0";
                var blueprintJson = args["blueprint"] as JObject;
                var tenantIdStr = args["tenantId"]?.ToString();

                if (string.IsNullOrEmpty(name)) return Error("Name is required");
                if (blueprintJson == null) return Error("Blueprint is required");
                
                // For MCP demo, we might generate a tenant ID or accept one.
                var tenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : Guid.NewGuid();

                var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
                if (blueprint == null) return Error("Invalid blueprint format");

                var workflowClass = new WorkflowClass(tenantId, name, version, blueprint);

                _dbContext.WorkflowClasses.Add(workflowClass);
                await _dbContext.SaveChangesAsync();

                return Success(new { id = workflowClass.Id, status = "Draft", message = "Draft created successfully" });
            }
            catch (Exception ex)
            {
                return Error($"Failed to create draft: {ex.Message}");
            }
        }

        public async Task<CallToolResult> UpdateDraft(JObject args)
        {
            try
            {
                var idStr = args["id"]?.ToString();
                var blueprintJson = args["blueprint"] as JObject;
                var name = args["name"]?.ToString(); // Optional update name

                if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id)) return Error("Valid ID is required");
                if (blueprintJson == null) return Error("Blueprint is required");

                var workflowClass = await _dbContext.WorkflowClasses.FindAsync(id);
                if (workflowClass == null) return Error("WorkflowClass not found");

                var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
                if (blueprint == null) return Error("Invalid blueprint format");

                workflowClass.UpdateDraft(name ?? workflowClass.Name, blueprint);
                await _dbContext.SaveChangesAsync();

                return Success(new { id = workflowClass.Id, status = workflowClass.Status.ToString(), message = "Draft updated successfully" });
            }
            catch (Exception ex)
            {
                return Error($"Failed to update draft: {ex.Message}");
            }
        }

        public async Task<CallToolResult> ValidateDraft(JObject args)
        {
            try
            {
                var idStr = args["id"]?.ToString();
                if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id)) return Error("Valid ID is required");

                var workflowClass = await _dbContext.WorkflowClasses.FindAsync(id);
                if (workflowClass == null) return Error("WorkflowClass not found");

                var result = _validator.Validate(workflowClass);

                return Success(new 
                { 
                    isValid = result.IsValid, 
                    errors = result.Errors.Select(e => new { code = e.Code, message = e.Message }) 
                });
            }
            catch (Exception ex)
            {
                return Error($"Validation failed: {ex.Message}");
            }
        }

        public async Task<CallToolResult> ForkPublic(JObject args)
        {
            try
            {
                var publicIdStr = args["publicId"]?.ToString();
                var tenantIdStr = args["tenantId"]?.ToString();

                if (string.IsNullOrEmpty(publicIdStr) || !Guid.TryParse(publicIdStr, out var publicId)) return Error("Valid Public ID is required");
                
                var tenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : Guid.NewGuid();

                var publicWc = await _dbContext.WorkflowClasses.FindAsync(publicId);
                if (publicWc == null) return Error("Public WorkflowClass not found");
                if (publicWc.Scope != WorkflowClassScope.Public) return Error("WorkflowClass is not Public");

                var copy = publicWc.CreateCopyForTenant(tenantId);
                
                _dbContext.WorkflowClasses.Add(copy);
                await _dbContext.SaveChangesAsync();

                return Success(new { id = copy.Id, status = "Draft", message = $"Forked from {publicWc.Name}" });
            }
            catch (Exception ex)
            {
                return Error($"Fork failed: {ex.Message}");
            }
        }

        private CallToolResult Success(object data)
        {
            return new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "json", Text = JObject.FromObject(data).ToString() }
                }
            };
        }

        private CallToolResult Error(string message)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = message }
                }
            };
        }
    }
}
