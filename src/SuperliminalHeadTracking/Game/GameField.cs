using System;
using System.Reflection;

namespace SuperliminalHeadTracking.Game
{
    internal sealed class GameField
    {
#if IL2CPP
        private PropertyInfo _member;
        public Type FieldType { get { return _member.PropertyType; } }
        public object GetValue(object instance) { return _member.GetValue(instance); }
#else
        private FieldInfo _member;
        public Type FieldType { get { return _member.FieldType; } }
        public object GetValue(object instance) { return _member.GetValue(instance); }
#endif

        public static GameField Find(Type owner, string name, BindingFlags flags)
        {
#if IL2CPP
            // Il2CppInterop exposes native fields as public properties.
            var member = owner.GetProperty(name, (flags & ~BindingFlags.NonPublic) | BindingFlags.Public);
#else
            var member = owner.GetField(name, flags);
#endif
            return member == null ? null : new GameField { _member = member };
        }
    }
}
