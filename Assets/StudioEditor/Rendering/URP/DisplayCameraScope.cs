using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StudioEditor.Rendering
{
    // Multiple schemes can request display processing and release in any order.
    public sealed class DisplayCameraScope : IDisposable
    {
        private sealed class State
        {
            public UniversalAdditionalCameraData Data;
            public bool OwnsData;
            public bool PostProcessing;
            public LayerMask Layers;
            public readonly List<DisplayCameraScope> Scopes = new();
        }
        private static readonly Dictionary<Camera, State> states = new();
        private readonly Camera camera;
        private readonly int layers;
        private State state;
        public Camera Camera => camera;

        public DisplayCameraScope(Camera camera, int volumeLayers)
        {
            this.camera = camera;
            layers = volumeLayers;
            if (!states.TryGetValue(camera, out state))
            {
                bool owns = !camera.TryGetComponent<UniversalAdditionalCameraData>(out var data);
                if (owns) data = camera.GetUniversalAdditionalCameraData();
                state = new State { Data = data, OwnsData = owns,
                    PostProcessing = data.renderPostProcessing, Layers = data.volumeLayerMask };
                states.Add(camera, state);
            }
            state.Scopes.Add(this);
            Apply(state);
        }
        private static void Apply(State state)
        {
            if (state.Data == null) return;
            int mask = state.Layers;
            foreach (var scope in state.Scopes) mask |= scope.layers;
            state.Data.volumeLayerMask = mask;
            state.Data.renderPostProcessing = state.PostProcessing || state.Scopes.Count != 0;
        }
        public void Dispose()
        {
            if (state == null) return;
            state.Scopes.Remove(this);
            Apply(state);
            if (state.Scopes.Count == 0)
            {
                states.Remove(camera);
                if (state.OwnsData && state.Data != null) CoreUtils.Destroy(state.Data);
            }
            state = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => states.Clear();
    }
}
