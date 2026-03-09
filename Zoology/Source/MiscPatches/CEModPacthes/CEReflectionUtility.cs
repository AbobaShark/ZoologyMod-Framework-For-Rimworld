using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class CEReflectionUtility
    {
        private static readonly string[] SharpPenetrationMemberNames = { "armorPenetrationSharp", "armorPenetration" };
        private static readonly string[] BluntPenetrationMemberNames = { "armorPenetrationBlunt", "armorPenetration" };
        private static readonly string[] CasterCandidateNames = { "CasterPawn", "casterPawn", "Caster", "caster", "owner", "CasterThing" };
        private static readonly Dictionary<TypeMemberKey, MethodInfo> methodCache = new Dictionary<TypeMemberKey, MethodInfo>();
        private static readonly Dictionary<TypeMemberKey, MemberInfo> memberCache = new Dictionary<TypeMemberKey, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> toolMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> skillMultiplierMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> equipmentSourceMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> casterMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> verbPropsMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> verbPropsOwnerMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MethodInfo> getStatValueWithIndexCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> getStatValueCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Tool, string> maneuverStringCache = new Dictionary<Tool, string>();
        private static readonly StatDef meleePenetrationFactorStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleePenetrationFactor");

        public static float InvokePrivateFloat(object instance, string methodName, object[] args, float fallback)
        {
            if (instance == null) return fallback;

            try
            {
                var method = GetCachedMethod(instance.GetType(), methodName);
                if (method == null)
                {
                    return fallback;
                }

                var value = method.Invoke(instance, args);
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value != null) return Convert.ToSingle(value);
            }
            catch
            {
            }

            return fallback;
        }

        public static bool TryGetVerbTool(object verbInstance, out object toolObj)
        {
            toolObj = null;
            if (verbInstance == null)
            {
                return false;
            }

            try
            {
                var member = GetCachedSpecialMember(toolMemberCache, verbInstance.GetType(), FindToolMember);
                toolObj = ReadMemberValue(member, verbInstance);
                return toolObj != null;
            }
            catch
            {
                return false;
            }
        }

        public static float GetPenetrationSkillMultiplier(object verbInstance)
        {
            if (verbInstance == null)
            {
                return 1f;
            }

            try
            {
                var member = GetCachedSpecialMember(skillMultiplierMemberCache, verbInstance.GetType(), type => FindMemberRecursive(type, "PenetrationSkillMultiplier"));
                var value = ReadMemberValue(member, verbInstance);
                return value != null ? Convert.ToSingle(value) : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        public static object GetEquipmentSource(object verbInstance)
        {
            if (verbInstance == null)
            {
                return null;
            }

            try
            {
                var member = GetCachedSpecialMember(equipmentSourceMemberCache, verbInstance.GetType(), type => FindMemberRecursive(type, "EquipmentSource"));
                return ReadMemberValue(member, verbInstance);
            }
            catch
            {
                return null;
            }
        }

        public static float GetEquipmentPenetrationFactor(object equipmentSource)
        {
            if (equipmentSource == null || meleePenetrationFactorStat == null)
            {
                return 1f;
            }

            try
            {
                var type = equipmentSource.GetType();
                var withIndex = GetCachedMethod(type, getStatValueWithIndexCache, new[] { typeof(StatDef), typeof(bool), typeof(int) });
                if (withIndex != null)
                {
                    var value = withIndex.Invoke(equipmentSource, new object[] { meleePenetrationFactorStat, true, -1 });
                    if (value != null) return Convert.ToSingle(value);
                }

                var withoutIndex = GetCachedMethod(type, getStatValueCache, new[] { typeof(StatDef), typeof(bool) });
                if (withoutIndex != null)
                {
                    var value = withoutIndex.Invoke(equipmentSource, new object[] { meleePenetrationFactorStat, true });
                    if (value != null) return Convert.ToSingle(value);
                }
            }
            catch
            {
            }

            return 1f;
        }

        public static Pawn GetCasterPawn(object verbInstance)
        {
            if (verbInstance == null)
            {
                return null;
            }

            try
            {
                var type = verbInstance.GetType();
                var member = GetCachedSpecialMember(casterMemberCache, type, FindCasterMember);
                var pawn = ReadMemberValue(member, verbInstance) as Pawn;
                if (pawn != null)
                {
                    return pawn;
                }

                var verbPropsMember = GetCachedSpecialMember(verbPropsMemberCache, type, t => FindMemberRecursive(t, "verbProps"));
                var verbProps = ReadMemberValue(verbPropsMember, verbInstance);
                if (verbProps == null)
                {
                    return null;
                }

                var ownerMember = GetCachedSpecialMember(verbPropsOwnerMemberCache, verbProps.GetType(), t => FindMemberRecursive(t, "owner"));
                return ReadMemberValue(ownerMember, verbProps) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryReadSharpToolPenetration(object toolObj, out float value)
        {
            return TryReadFloatFromToolMember(toolObj, SharpPenetrationMemberNames, out value);
        }

        public static bool TryReadBluntToolPenetration(object toolObj, out float value)
        {
            return TryReadFloatFromToolMember(toolObj, BluntPenetrationMemberNames, out value);
        }

        public static float ReadSharpToolPenetration(object toolObj, float defaultValue)
        {
            return TryReadSharpToolPenetration(toolObj, out var value) ? value : defaultValue;
        }

        public static float ReadBluntToolPenetration(object toolObj, float defaultValue)
        {
            return TryReadBluntToolPenetration(toolObj, out var value) ? value : defaultValue;
        }

        public static Pawn GetPawnFromRequestThing(Thing thing)
        {
            if (thing is Pawn pawn)
            {
                return pawn;
            }

            return (thing?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
        }

        public static float GetMeleeDamageFactorStatPow(Pawn pawn)
        {
            if (pawn == null)
            {
                return 1f;
            }

            try
            {
                return UnityEngine.Mathf.Pow(pawn.GetStatValue(StatDefOf.MeleeDamageFactor, true, -1), 0.75f);
            }
            catch
            {
                return 1f;
            }
        }

        public static string GetManeuverString(Tool tool)
        {
            if (tool == null)
            {
                return string.Empty;
            }

            if (maneuverStringCache.TryGetValue(tool, out var cached))
            {
                return cached;
            }

            var maneuvers = new List<string>();
            var capacities = tool.capacities;
            if (capacities != null)
            {
                var allDefs = DefDatabase<ManeuverDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs.Count; i++)
                {
                    var maneuver = allDefs[i];
                    if (maneuver != null && capacities.Contains(maneuver.requiredCapacity))
                    {
                        maneuvers.Add(maneuver.ToString());
                    }
                }
            }

            cached = "(" + string.Join("/", maneuvers) + ")";
            maneuverStringCache[tool] = cached;
            return cached;
        }

        private static bool TryReadFloatFromToolMember(object toolObj, string[] candidateNames, out float value)
        {
            value = 0f;
            if (toolObj == null || candidateNames == null)
            {
                return false;
            }

            var type = toolObj.GetType();
            for (int i = 0; i < candidateNames.Length; i++)
            {
                try
                {
                    var member = GetCachedNamedMember(type, candidateNames[i]);
                    if (member == null)
                    {
                        continue;
                    }

                    var rawValue = ReadMemberValue(member, toolObj);
                    if (rawValue != null)
                    {
                        value = Convert.ToSingle(rawValue);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static MethodInfo GetCachedMethod(Type type, string methodName)
        {
            var key = new TypeMemberKey(type, methodName);
            if (methodCache.TryGetValue(key, out var method))
            {
                return method;
            }

            method = FindMethodRecursive(type, methodName);
            methodCache[key] = method;
            return method;
        }

        private static MethodInfo GetCachedMethod(Type type, Dictionary<Type, MethodInfo> cache, Type[] parameters)
        {
            if (cache.TryGetValue(type, out var method))
            {
                return method;
            }

            method = FindMethodRecursive(type, "GetStatValue", parameters);
            cache[type] = method;
            return method;
        }

        private static MemberInfo GetCachedNamedMember(Type type, string memberName)
        {
            var key = new TypeMemberKey(type, memberName);
            if (memberCache.TryGetValue(key, out var member))
            {
                return member;
            }

            member = FindMemberRecursive(type, memberName);
            memberCache[key] = member;
            return member;
        }

        private static MemberInfo GetCachedSpecialMember(Dictionary<Type, MemberInfo> cache, Type type, Func<Type, MemberInfo> factory)
        {
            if (cache.TryGetValue(type, out var member))
            {
                return member;
            }

            member = factory(type);
            cache[type] = member;
            return member;
        }

        private static MemberInfo FindToolMember(Type type)
        {
            return FindMemberRecursive(type, "ToolCE") ?? FindMemberRecursive(type, "tool");
        }

        private static MemberInfo FindCasterMember(Type type)
        {
            for (int i = 0; i < CasterCandidateNames.Length; i++)
            {
                var member = FindMemberRecursive(type, CasterCandidateNames[i]);
                if (member != null)
                {
                    return member;
                }
            }

            return null;
        }

        private static MemberInfo FindMemberRecursive(Type type, string memberName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                var property = current.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    return property;
                }
            }

            return null;
        }

        private static MethodInfo FindMethodRecursive(Type type, string methodName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindMethodRecursive(Type type, string methodName, Type[] parameters)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, parameters, null);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static object ReadMemberValue(MemberInfo member, object instance)
        {
            if (member is FieldInfo field)
            {
                return field.GetValue(instance);
            }

            if (member is PropertyInfo property && property.GetGetMethod(true) != null)
            {
                return property.GetValue(instance, null);
            }

            return null;
        }

        private readonly struct TypeMemberKey : IEquatable<TypeMemberKey>
        {
            private readonly Type type;
            private readonly string memberName;

            public TypeMemberKey(Type type, string memberName)
            {
                this.type = type;
                this.memberName = memberName;
            }

            public bool Equals(TypeMemberKey other)
            {
                return type == other.type && string.Equals(memberName, other.memberName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is TypeMemberKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int typeHash = type != null ? type.GetHashCode() : 0;
                    int nameHash = memberName != null ? StringComparer.Ordinal.GetHashCode(memberName) : 0;
                    return (typeHash * 397) ^ nameHash;
                }
            }
        }
    }
}
