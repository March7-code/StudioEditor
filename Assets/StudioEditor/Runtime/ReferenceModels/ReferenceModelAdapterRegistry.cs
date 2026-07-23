using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioEditor.ReferenceModels
{
    public static class ReferenceModelAdapterRegistry
    {
        private static readonly List<Func<IReferenceModelFormatAdapter>> factories =
            new List<Func<IReferenceModelFormatAdapter>>();

        public static void Register<TAdapter>()
            where TAdapter : IReferenceModelFormatAdapter, new()
        {
            var adapterType = typeof(TAdapter);
            for (var index = 0; index < factories.Count; index++)
            {
                if (factories[index]().GetType() == adapterType)
                {
                    return;
                }
            }

            factories.Add(() => new TAdapter());
        }

        public static IReadOnlyList<IReferenceModelFormatAdapter> CreateAdapters()
        {
            var adapters = new IReferenceModelFormatAdapter[factories.Count];
            for (var index = 0; index < factories.Count; index++)
            {
                adapters[index] = factories[index]();
            }

            return Array.AsReadOnly(adapters);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            factories.Clear();
        }
    }
}
