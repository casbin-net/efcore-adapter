using System;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.Entities
{
    public class EFCorePersistPolicy<TKey> : PersistPolicy, IEFCorePersistPolicy<TKey> where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; }
    }
}