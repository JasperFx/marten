using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Baseline;
using Xunit;

namespace Marten.Testing
{
    public abstract class IntegratedFixture : IDisposable
    {
        private Lazy<IDocumentStore> _store;
#if NET46
        private CultureInfo _originalCulture;
#endif

        protected IntegratedFixture()
        {
            _store = new Lazy<IDocumentStore>(TestingDocumentStore.Basic);

            if (GetType().GetTypeInfo().GetCustomAttribute<CollectionAttribute>(true) != null)
            {
                UseDefaultSchema();
            }

#if NET46
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
#endif
        }

        protected string toJson<T>(T doc)
        {
            return theStore.Options.Serializer().ToJson(doc);
        }

        protected DocumentStore theStore => _store.Value.As<DocumentStore>();

        protected void UseDefaultSchema()
        {
            _store = new Lazy<IDocumentStore>(TestingDocumentStore.DefaultSchema);
        }

        protected void StoreOptions(Action<StoreOptions> configure)
        {
            _store = new Lazy<IDocumentStore>(() => TestingDocumentStore.For(configure));
        }

        public virtual void Dispose()
        {
            if (_store.IsValueCreated)
            {
                _store.Value.Dispose();
            }
#if NET46
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
#endif
        }
    }

    public sealed class TestingContracts
    {
        public readonly string Value;

        public static readonly TestingContracts CamelCase = new TestingContracts("marten-testing-CamelCase");

        public TestingContracts(string value)
        {
            Value = value;
        }

        public static TestingContracts Is(string value)
        {
            return new TestingContracts(value);
        }

        private bool Equals(TestingContracts other)
        {
            return string.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TestingContracts && Equals((TestingContracts)obj);
        }

        public override int GetHashCode()
        {
            return Value?.GetHashCode() ?? 0;
        }

        public static implicit operator TestingContracts(string item)
        {
            return new TestingContracts(item);
        }

        public static implicit operator string(TestingContracts item)
        {
            return item.Value;
        }
    }

    public static class IntegratedFixtureMixins
    {
        public static IntegratedFixtureProfile InProfile(this IntegratedFixture fixture, TestingContracts profile, Action inProfile)
        {
            if (Environment.GetEnvironmentVariable(profile.Value) != null)
            {
                inProfile();
                return new IntegratedFixtureProfile(fixture, true);
            }
            return new IntegratedFixtureProfile(fixture, false);
        }

        public static void Otherwise(this IntegratedFixtureProfile fixture, Action inProfile)
        {
            if (!fixture)
            {
                inProfile();
            }
        }

        public sealed class IntegratedFixtureProfile
        {
            private readonly IntegratedFixture _fixture;
            private readonly bool _enabled;

            public IntegratedFixtureProfile(IntegratedFixture fixture, bool enabled)
            {
                _fixture = fixture;
                _enabled = enabled;
            }

            public static implicit operator IntegratedFixture(IntegratedFixtureProfile profile)
            {
                return profile._fixture;
            }

            public static implicit operator bool(IntegratedFixtureProfile profile)
            {
                return profile._enabled;
            }
        }
    }
}