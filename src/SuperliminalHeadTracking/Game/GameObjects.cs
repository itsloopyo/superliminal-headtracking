using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SuperliminalHeadTracking.Game
{
    internal static class GameObjects
    {
        public static Component GetComponent(Component owner, Type type)
        {
#if IL2CPP
            return (Component)GenericMethod(ComponentMethods, typeof(Component), "GetComponent", type).Invoke(owner, null);
#else
            return owner.GetComponent(type);
#endif
        }

        public static UnityEngine.Object Find(Type type)
        {
#if IL2CPP
            return (UnityEngine.Object)GenericMethod(FindMethods, typeof(UnityEngine.Object), "FindObjectOfType", type).Invoke(null, null);
#else
            return UnityEngine.Object.FindObjectOfType(type);
#endif
        }

#if IL2CPP
        private static readonly Dictionary<Type, MethodInfo> ComponentMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> FindMethods = new Dictionary<Type, MethodInfo>();

        private static MethodInfo GenericMethod(Dictionary<Type, MethodInfo> cache, Type owner, string name, Type argument)
        {
            if (cache.TryGetValue(argument, out var cached)) return cached;
            foreach (var method in owner.GetMethods())
                if (method.Name == name && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                {
                    var resolved = method.MakeGenericMethod(argument);
                    cache.Add(argument, resolved);
                    return resolved;
                }
            throw new MissingMethodException(owner.FullName, name);
        }
#endif
    }
}
