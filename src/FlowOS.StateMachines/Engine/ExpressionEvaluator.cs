using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;

namespace FlowOS.StateMachines.Engine;

/// <summary>
/// A utility to evaluate string expressions (e.g., "Amount > 100") against a dynamic dictionary payload.
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
            var properties = payload.Select(kvp => new DynamicProperty(kvp.Key, kvp.Value?.GetType() ?? typeof(object))).ToArray();
            var type = DynamicClassFactory.CreateType(properties);
            
            var obj = (DynamicClass)Activator.CreateInstance(type)!;
            foreach (var kvp in payload)
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
}
