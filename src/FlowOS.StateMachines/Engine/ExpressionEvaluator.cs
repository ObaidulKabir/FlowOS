using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowOS.StateMachines.Engine;

/// <summary>
/// A utility to evaluate string expressions (e.g., "Amount > 100") against a dynamic dictionary payload,
/// evaluate arbitrary value expressions for payload transformations, and interpolate template strings.
/// Values are normalized from JsonElement to .NET primitives before evaluation.
/// </summary>
public static class ExpressionEvaluator
{
    private static readonly Regex TemplateRegex = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    public static bool Evaluate(string expression, Dictionary<string, object> payload)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var cleanExpr = Regex.Replace(expression.Trim(), @"\b[pP]ayload\.", "");

        if (payload == null || payload.Count == 0)
        {
            try 
            {
                var emptyLambda = DynamicExpressionParser.ParseLambda(new ParameterExpression[0], typeof(bool), cleanExpr);
                var emptyResult = emptyLambda.Compile().DynamicInvoke();
                return emptyResult is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            var normalized = NormalizePayload(payload);

            var properties = normalized.Select(kvp => new DynamicProperty(kvp.Key, kvp.Value?.GetType() ?? typeof(object))).ToArray();
            var type = DynamicClassFactory.CreateType(properties);
            
            var obj = (DynamicClass)Activator.CreateInstance(type)!;
            foreach (var kvp in normalized)
            {
                type.GetProperty(kvp.Key)?.SetValue(obj, kvp.Value);
            }

            var parameter = Expression.Parameter(type, "it");
            var lambda = DynamicExpressionParser.ParseLambda(new[] { parameter }, typeof(bool), cleanExpr);
            var del = lambda.Compile();
            
            var result = del.DynamicInvoke(obj);
            return result is bool b && b;
        }
        catch (ParseException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates an expression against the context payload and returns the computed value.
    /// Can return primitives (numbers, booleans, strings) or mapped values.
    /// </summary>
    public static object? EvaluateValue(string expression, Dictionary<string, object>? payload)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var trimmed = expression.Trim();
        var cleanExpr = Regex.Replace(trimmed, @"\b[pP]ayload\.", "");

        if (payload == null || payload.Count == 0)
        {
            try
            {
                var emptyLambda = DynamicExpressionParser.ParseLambda(new ParameterExpression[0], typeof(object), cleanExpr);
                return emptyLambda.Compile().DynamicInvoke();
            }
            catch
            {
                return trimmed;
            }
        }

        try
        {
            var normalized = NormalizePayload(payload);

            // Direct key match shortcut
            if (normalized.TryGetValue(cleanExpr, out var directVal))
            {
                return directVal;
            }

            var properties = normalized.Select(kvp => new DynamicProperty(kvp.Key, kvp.Value?.GetType() ?? typeof(object))).ToArray();
            var type = DynamicClassFactory.CreateType(properties);

            var obj = (DynamicClass)Activator.CreateInstance(type)!;
            foreach (var kvp in normalized)
            {
                type.GetProperty(kvp.Key)?.SetValue(obj, kvp.Value);
            }

            var parameter = Expression.Parameter(type, "it");
            var lambda = DynamicExpressionParser.ParseLambda(new[] { parameter }, typeof(object), cleanExpr);
            var del = lambda.Compile();

            return del.DynamicInvoke(obj);
        }
        catch (ParseException)
        {
            return trimmed;
        }
        catch (Exception)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Interpolates {{Expression}} placeholders within a template string using the provided payload.
    /// E.g. "Order {{OrderId}} for {{Customer}} approved for ${{Amount * 1.15}}"
    /// </summary>
    public static string InterpolateTemplate(string? template, Dictionary<string, object>? payload)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (!template.Contains("{{"))
            return template;

        var cleanPayload = payload ?? new Dictionary<string, object>();
        var normalized = NormalizePayload(cleanPayload);

        return TemplateRegex.Replace(template, match =>
        {
            var expr = match.Groups[1].Value.Trim();
            var cleanExpr = Regex.Replace(expr, @"\b[pP]ayload\.", "");

            // If it's a single identifier and not present in payload, treat as empty
            if (IsValidIdentifier(cleanExpr) && !normalized.ContainsKey(cleanExpr))
            {
                return string.Empty;
            }

            var val = EvaluateValue(cleanExpr, cleanPayload);
            return val?.ToString() ?? string.Empty;
        });
    }

    private static bool IsValidIdentifier(string s) =>
        !string.IsNullOrEmpty(s) && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Converts JsonElement values (from System.Text.Json deserialization) to .NET primitives.
    /// Without this, expressions like "Amount > 100" fail because the property type is JsonElement, not double.
    /// </summary>
    private static Dictionary<string, object> NormalizePayload(Dictionary<string, object> payload)
    {
        var result = new Dictionary<string, object>(payload.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in payload)
        {
            result[kvp.Key] = NormalizeValue(kvp.Value);
        }
        return result;
    }

    private static object NormalizeValue(object value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt64(out var l) ? (object)l : je.GetDouble(),
                JsonValueKind.String => je.GetString() ?? string.Empty,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null!,
                _ => je.GetRawText()
            };
        }
        return value;
    }
}
