using System.Collections.Concurrent;
using System.Reflection;

namespace AutoQuestConflictPatcher.Merging;

public static class DeepCopyHelper
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> DeepCopyMethods = new();

    public static object? CloneForAssignment(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        if (targetType.IsInstanceOfType(value) && IsDirectAssignmentSafe(value.GetType()))
        {
            return value;
        }

        if (TryDeepCopy(value, out var clone) && clone is not null && targetType.IsInstanceOfType(clone))
        {
            return clone;
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        return clone;
    }

    public static object DeepCopyObject(object value)
    {
        return TryDeepCopy(value, out var clone) && clone is not null ? clone : value;
    }

    public static bool TryDeepCopy(object value, out object? clone)
    {
        clone = null;

        if (IsDirectAssignmentSafe(value.GetType()))
        {
            clone = value;
            return true;
        }

        var method = DeepCopyMethods.GetOrAdd(value.GetType(), FindDeepCopyMethod);
        if (method is null)
        {
            return false;
        }

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = value;
        for (var i = 1; i < parameters.Length; i++)
        {
            args[i] = Type.Missing;
        }

        clone = method.Invoke(null, args);
        return clone is not null;
    }

    private static MethodInfo? FindDeepCopyMethod(Type valueType)
    {
        return valueType.Assembly
            .GetTypes()
            .Where(type => type.IsSealed && type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == "DeepCopy")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length >= 1
                    && parameters[0].ParameterType.IsAssignableFrom(valueType)
                    && parameters.Skip(1).All(parameter => parameter.IsOptional);
            })
            .OrderBy(method => method.GetParameters()[0].ParameterType == valueType ? 0 : 1)
            .FirstOrDefault();
    }

    private static bool IsDirectAssignmentSafe(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsValueType)
        {
            return true;
        }

        return type == typeof(string);
    }
}
