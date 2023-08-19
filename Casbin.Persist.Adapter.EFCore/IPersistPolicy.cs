using System;

namespace Casbin.Persist.Adapter.EFCore
{
    public interface IEFCorePersistPolicy<TKey> : IPersistPolicy where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; }
    }
}