using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace SuperliminalHeadTracking.Il2Cpp
{
    internal sealed class CameraCallbacks
    {
        private readonly PropertyInfo _property;
        private readonly object _callback;
        private readonly MethodInfo _subtract;

        public CameraCallbacks(string name, Action<Camera> callback)
        {
            _property = typeof(Camera).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMemberException(typeof(Camera).FullName, name);
            Type callbackType = _property.PropertyType;
            var convert = typeof(DelegateSupport).GetMethod("ConvertDelegate").MakeGenericMethod(callbackType);
            _callback = convert.Invoke(null, new object[] { callback });
            _subtract = callbackType.GetMethod("op_Subtraction");
            var add = callbackType.GetMethod("op_Addition");
            _property.SetValue(null, add.Invoke(null, new[] { _property.GetValue(null), _callback }));
        }

        public void Remove()
        {
            _property.SetValue(null, _subtract.Invoke(null, new[] { _property.GetValue(null), _callback }));
        }
    }
}
