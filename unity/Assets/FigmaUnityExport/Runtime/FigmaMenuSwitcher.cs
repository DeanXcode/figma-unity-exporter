using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Codex.FigmaUnity
{
    public sealed class FigmaMenuSwitcher : MonoBehaviour
    {
        public string defaultKey = string.Empty;
        public bool disableSelectedButton = true;
        public List<FigmaMenuItem> items = new List<FigmaMenuItem>();
        private readonly Dictionary<Button, UnityAction> boundListeners = new Dictionary<Button, UnityAction>();

        private void Awake()
        {
            BindButtons();
            if (!string.IsNullOrEmpty(defaultKey))
            {
                SetSelected(defaultKey);
            }
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        public void BindButtons()
        {
            UnbindButtons();
            foreach (FigmaMenuItem item in items)
            {
                if (item == null || item.button == null)
                {
                    continue;
                }

                string capturedKey = item.key;
                UnityAction action = () => SetSelected(capturedKey);
                item.button.onClick.AddListener(action);
                boundListeners[item.button] = action;
            }
        }

        public void UnbindButtons()
        {
            foreach (KeyValuePair<Button, UnityAction> entry in boundListeners)
            {
                if (entry.Key != null)
                {
                    entry.Key.onClick.RemoveListener(entry.Value);
                }
            }

            boundListeners.Clear();
        }

        public void SetSelected(string key)
        {
            foreach (FigmaMenuItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                bool selected = string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase);
                if (item.normalLayer != null)
                {
                    item.normalLayer.SetActive(!selected);
                }

                if (item.selectedLayer != null)
                {
                    item.selectedLayer.SetActive(selected);
                }

                if (item.button != null && disableSelectedButton)
                {
                    item.button.interactable = !selected;
                }
            }

            defaultKey = key;
        }
    }

    [Serializable]
    public sealed class FigmaMenuItem
    {
        public string key = string.Empty;
        public GameObject root;
        public Button button;
        public GameObject normalLayer;
        public GameObject selectedLayer;
    }
}
