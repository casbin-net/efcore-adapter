using System;

namespace Casbin.Persist.Adapter.EFCore.Entities
{
    public class EFCorePersistPolicy<TKey> : PersistPolicy, IEFCorePersistPolicy<TKey> where TKey : IEquatable<TKey>
    {
        public TKey Id { get; set; }
    }
}