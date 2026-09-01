using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;
using System.Text.Json;

namespace FlowOS.StateMachines.Engine;

/// <summary>
/// A utility to evaluate string expressions (e.g., "Amount > 100") against a dynamic dictionary payload.
/// Values are normalized from JsonElement to .NET primitives before evaluation.
/// </summary>
public static class ExpressionEvaluator
{
    public static bool Evaluate(string expression, Dictionary<string, object> payload)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        if (payload == null || payload.Count == 0)
        {
            try 
            {
                var emptyLambda = DynamicExpressionParser.ParseLambda(new ParameterExpression[0], typeof(bool), expression);
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
            var lambda = DynamicExpressionParser.ParseLambda(new[] { parameter }, typeof(bool), expression);
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
    /// Converts JsonElement values (from System.Text.Json deserialization) to .NET primitives.
    /// Without this, expressions like "Amount > 100" fail because the property type is JsonElement, not double.
    /// </summary>
    private static Dictionary<string, object> NormalizePayload(Dictionary<string, object> payload)
    {
        var result = new Dictionary<string, object>(payload.Count);
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
