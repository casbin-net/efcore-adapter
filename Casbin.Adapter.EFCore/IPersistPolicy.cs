using System;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore
{
    public interface IEFCorePersistPolicy<TKey> : IPersistPolicy where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; }
    }
}