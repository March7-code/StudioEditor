using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    internal static class KoikatsuFinalIkRuntime
    {
        private const string FullBodyTypeName =
            "RootMotion.FinalIK.FullBodyBipedIK";
        private const string ReferencesTypeName = "RootMotion.BipedReferences";
        private const string EffectorTypeName =
            "RootMotion.FinalIK.FullBodyBipedEffector";

        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, MemberInfo> Members =
            new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, MethodInfo> Methods =
            new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        private static Api api;
        private static bool resolved;
        private static string status;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return api != null;
            }
        }

        public static bool TryGetStatus(out string message)
        {
            EnsureResolved();
            message = status;
            return api != null;
        }

        public static void RefreshAvailability()
        {
            lock (Sync)
            {
                resolved = false;
                api = null;
                status = null;
                Members.Clear();
                Methods.Clear();
            }
        }

        public static bool TryGetOrAdd(
            GameObject host,
            out KoikatsuFinalIkComponent component,
            out bool created,
            out string error)
        {
            component = null;
            created = false;
            if (host == null)
            {
                error = "The Final IK host is missing.";
                return false;
            }

            if (!TryGetApi(out var current, out error))
            {
                return false;
            }

            try
            {
                var value = host.GetComponent(current.FullBodyType) as Component;
                if (value == null)
                {
                    value = host.AddComponent(current.FullBodyType);
                    created = true;
                }

                component = new KoikatsuFinalIkComponent(value);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = Unwrap(exception).Message;
                return false;
            }
        }

        public static bool TryAdd(
            GameObject host,
            out KoikatsuFinalIkComponent component,
            out string error)
        {
            component = null;
            if (host == null)
            {
                error = "The Final IK host is missing.";
                return false;
            }

            if (!TryGetApi(out var current, out error))
            {
                return false;
            }

            try
            {
                var value = host.AddComponent(current.FullBodyType);
                component = new KoikatsuFinalIkComponent(value);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = Unwrap(exception).Message;
                return false;
            }
        }

        public static KoikatsuFinalIkComponent[] GetComponentsInChildren(
            GameObject root)
        {
            if (root == null || !TryGetApi(out var current, out _))
            {
                return Array.Empty<KoikatsuFinalIkComponent>();
            }

            var values = root.GetComponentsInChildren(
                current.FullBodyType,
                true);
            var result = new KoikatsuFinalIkComponent[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                result[index] = new KoikatsuFinalIkComponent(values[index]);
            }

            return result;
        }

        public static object CreateReferences()
        {
            if (!TryGetApi(out var current, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return Activator.CreateInstance(current.ReferencesType);
        }

        public static object GetMember(object target, string name)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var member = FindMember(target.GetType(), name);
            if (member is FieldInfo field)
            {
                return field.GetValue(target);
            }

            return ((PropertyInfo)member).GetValue(target, null);
        }

        public static T GetMember<T>(object target, string name)
        {
            var value = GetMember(target, name);
            return value == null ? default : (T)value;
        }

        public static Array GetArray(object target, string name)
        {
            return GetMember(target, name) as Array ?? Array.Empty<object>();
        }

        public static void SetMember(
            object target,
            string name,
            object value)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var member = FindMember(target.GetType(), name);
            if (member is FieldInfo field)
            {
                field.SetValue(target, ConvertValue(value, field.FieldType));
                return;
            }

            var property = (PropertyInfo)member;
            property.SetValue(
                target,
                ConvertValue(value, property.PropertyType),
                null);
        }

        public static object Invoke(
            object target,
            string name,
            params object[] arguments)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            arguments ??= Array.Empty<object>();
            var method = FindMethod(target.GetType(), name, arguments);
            var parameters = method.GetParameters();
            var converted = new object[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
            {
                converted[index] = ConvertValue(
                    arguments[index],
                    parameters[index].ParameterType);
            }

            try
            {
                var result = method.Invoke(target, converted);
                for (var index = 0; index < arguments.Length; index++)
                {
                    if (parameters[index].ParameterType.IsByRef)
                    {
                        arguments[index] = converted[index];
                    }
                }

                return result;
            }
            catch (TargetInvocationException exception)
            {
                throw Unwrap(exception);
            }
        }

        public static object GetEffectorValue(string name)
        {
            if (!TryGetApi(out var current, out var error))
            {
                throw new InvalidOperationException(error);
            }

            return Enum.Parse(current.EffectorType, name, false);
        }

        private static bool TryGetApi(out Api value, out string error)
        {
            EnsureResolved();
            value = api;
            error = value == null ? status : string.Empty;
            return value != null;
        }

        private static void EnsureResolved()
        {
            lock (Sync)
            {
                if (resolved)
                {
                    return;
                }

                resolved = true;
                var fullBodyType = FindType(FullBodyTypeName);
                if (fullBodyType == null)
                {
                    status = "Final IK is not installed.";
                    return;
                }

                var referencesType = FindType(ReferencesTypeName);
                var effectorType = FindType(EffectorTypeName);
                if (!typeof(Component).IsAssignableFrom(fullBodyType) ||
                    referencesType == null ||
                    effectorType == null ||
                    !effectorType.IsEnum)
                {
                    status = "Final IK was found, but its public API is not " +
                             "compatible with this Studio Editor integration.";
                    return;
                }

                try
                {
                    FindMember(fullBodyType, "solver");
                    FindMember(fullBodyType, "fixTransforms");
                    FindMethod(
                        fullBodyType,
                        "SetReferences",
                        new object[]
                        {
                            Activator.CreateInstance(referencesType),
                            null,
                        });
                }
                catch (Exception exception)
                {
                    status = "Final IK was found, but API validation failed: " +
                             exception.Message;
                    return;
                }

                api = new Api(fullBodyType, referencesType, effectorType);
                status = "Final IK integration is available from assembly '" +
                         fullBodyType.Assembly.GetName().Name + "'.";
            }
        }

        private static Type FindType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var index = 0; index < assemblies.Length; index++)
            {
                try
                {
                    var type = assemblies[index].GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // A partially loadable optional assembly is not a match.
                }
            }

            return null;
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            var key = type.AssemblyQualifiedName + "|" + name;
            lock (Sync)
            {
                if (Members.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                MemberInfo member = type.GetField(name, InstanceMembers);
                member ??= type.GetProperty(name, InstanceMembers);
                if (member == null)
                {
                    throw new MissingMemberException(type.FullName, name);
                }

                Members.Add(key, member);
                return member;
            }
        }

        private static MethodInfo FindMethod(
            Type type,
            string name,
            IReadOnlyList<object> arguments)
        {
            var key = BuildMethodKey(type, name, arguments);
            lock (Sync)
            {
                if (Methods.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                MethodInfo best = null;
                var bestScore = int.MinValue;
                var methods = type.GetMethods(InstanceMembers);
                for (var index = 0; index < methods.Length; index++)
                {
                    var candidate = methods[index];
                    if (!string.Equals(
                            candidate.Name,
                            name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parameters = candidate.GetParameters();
                    if (parameters.Length != arguments.Count)
                    {
                        continue;
                    }

                    var score = 0;
                    var compatible = true;
                    for (var argumentIndex = 0;
                         argumentIndex < arguments.Count;
                         argumentIndex++)
                    {
                        if (!CanConvert(
                                arguments[argumentIndex],
                                parameters[argumentIndex].ParameterType,
                                out var argumentScore))
                        {
                            compatible = false;
                            break;
                        }

                        score += argumentScore;
                    }

                    if (compatible && score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (best == null)
                {
                    throw new MissingMethodException(
                        type.FullName,
                        name + "(" + arguments.Count + ")");
                }

                Methods.Add(key, best);
                return best;
            }
        }

        private static string BuildMethodKey(
            Type type,
            string name,
            IReadOnlyList<object> arguments)
        {
            var result = type.AssemblyQualifiedName + "|" + name;
            for (var index = 0; index < arguments.Count; index++)
            {
                result += "|" +
                          (arguments[index]?.GetType().AssemblyQualifiedName ??
                           "null");
            }

            return result;
        }

        private static bool CanConvert(
            object value,
            Type targetType,
            out int score)
        {
            targetType = UnwrapParameterType(targetType);
            if (value == null)
            {
                score = 0;
                return !targetType.IsValueType ||
                       Nullable.GetUnderlyingType(targetType) != null;
            }

            var sourceType = value.GetType();
            if (targetType == sourceType)
            {
                score = 4;
                return true;
            }

            if (targetType.IsAssignableFrom(sourceType))
            {
                score = 3;
                return true;
            }

            if (targetType.IsEnum &&
                (value is string || value is IConvertible))
            {
                score = 1;
                return true;
            }

            score = value is IConvertible &&
                    typeof(IConvertible).IsAssignableFrom(targetType)
                ? 1
                : int.MinValue;
            return score != int.MinValue;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            targetType = UnwrapParameterType(targetType);
            if (value == null)
            {
                return null;
            }

            var nullable = Nullable.GetUnderlyingType(targetType);
            if (nullable != null)
            {
                targetType = nullable;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            if (targetType.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(targetType, text, false)
                    : Enum.ToObject(
                        targetType,
                        Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }

            return Convert.ChangeType(
                value,
                targetType,
                CultureInfo.InvariantCulture);
        }

        private static Type UnwrapParameterType(Type type)
        {
            return type.IsByRef ? type.GetElementType() : type;
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException invocation &&
                   invocation.InnerException != null)
            {
                exception = invocation.InnerException;
            }

            return exception;
        }

        private sealed class Api
        {
            public Api(
                Type fullBodyType,
                Type referencesType,
                Type effectorType)
            {
                FullBodyType = fullBodyType;
                ReferencesType = referencesType;
                EffectorType = effectorType;
            }

            public Type FullBodyType { get; }

            public Type ReferencesType { get; }

            public Type EffectorType { get; }
        }
    }

    internal sealed class KoikatsuFinalIkComponent
    {
        public KoikatsuFinalIkComponent(Component value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public Component Value { get; }

        public bool IsAlive => Value != null;

        public Transform Transform => Value != null ? Value.transform : null;

        public bool Enabled
        {
            get => Value is Behaviour behaviour && behaviour.enabled;
            set
            {
                if (Value is Behaviour behaviour)
                {
                    behaviour.enabled = value;
                }
            }
        }

        public bool FixTransforms
        {
            get => KoikatsuFinalIkRuntime.GetMember<bool>(
                Value,
                "fixTransforms");
            set => KoikatsuFinalIkRuntime.SetMember(
                Value,
                "fixTransforms",
                value);
        }

        public object Solver =>
            KoikatsuFinalIkRuntime.GetMember(Value, "solver");

        public bool SolverInitiated =>
            IsAlive &&
            KoikatsuFinalIkRuntime.GetMember<bool>(Solver, "initiated");

        public void SetReferences(object references, Transform rootNode)
        {
            KoikatsuFinalIkRuntime.Invoke(
                Value,
                "SetReferences",
                references,
                rootNode);
        }

        public bool ReferencesError(ref string error)
        {
            object[] arguments = { error };
            var result = KoikatsuFinalIkRuntime.Invoke(
                Value,
                "ReferencesError",
                arguments);
            error = arguments[0] as string ?? string.Empty;
            return result is bool value && value;
        }

        public void Initiate()
        {
            KoikatsuFinalIkRuntime.Invoke(Solver, "Initiate", Transform);
        }

        public void FixSolverTransforms()
        {
            KoikatsuFinalIkRuntime.Invoke(Solver, "FixTransforms");
        }

        public void UpdateSolver()
        {
            KoikatsuFinalIkRuntime.Invoke(Solver, "Update");
        }
    }
}
