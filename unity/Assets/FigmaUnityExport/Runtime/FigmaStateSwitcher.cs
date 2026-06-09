using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codex.FigmaUnity
{
    public sealed class FigmaStateSwitcher : MonoBehaviour
    {
        public List<FigmaStateLayer> states = new List<FigmaStateLayer>();

        private void Awake()
        {
            ResetToDefault();
        }

        public void ResetToDefault()
        {
            foreach (FigmaStateLayer state in states)
            {
                if (state != null && state.layer != null)
                {
                    state.layer.SetActive(state.visibleByDefault);
                }
            }
        }

        public void SetVisible(string key, bool visible)
        {
            FigmaStateLayer state = Find(key);
            if (state != null && state.layer != null)
            {
                state.layer.SetActive(visible);
            }
        }

        public void SetOnly(string key)
        {
            foreach (FigmaStateLayer state in states)
            {
                if (state == null || state.layer == null)
                {
                    continue;
                }

                state.layer.SetActive(string.Equals(state.key, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        private FigmaStateLayer Find(string key)
        {
            foreach (FigmaStateLayer state in states)
            {
                if (state != null && string.Equals(state.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class FigmaStateLayer
    {
        public string key = string.Empty;
        public GameObject layer;
        public bool visibleByDefault;
    }
}
