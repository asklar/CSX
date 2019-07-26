using System;
using System.Collections.Generic;

namespace CSX
{
    public static class Extension
    {
        public static T AndThen<T>(this T _this, Action<T> andThen)
        {
            andThen.Invoke(_this);
            return _this;
        }
    }
    public class LocalNameService
    {
        public Dictionary<string, object> Objects { get; } = new Dictionary<string, object>();
    }
    public class TopLevelObject<T> : LocalNameService where T : new()
    {
        public T Instance { get; } = new T();
        public TopLevelObject<T> AndThen(Action<T, LocalNameService> andThen)
        {
            andThen.Invoke(Instance, this);
            return this;
        }
    }

}
