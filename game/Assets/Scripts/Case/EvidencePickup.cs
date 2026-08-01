using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>A collectible evidence item in the world (graybox: a small cube).</summary>
    public class EvidencePickup : MonoBehaviour, IInteractable
    {
        private int _index;
        private string _itemName;

        public void Init(int index, string itemName)
        {
            _index = index;
            _itemName = itemName;
            var label = Labels.Attach(transform, itemName, 0.9f);
            label.characterSize = 0.06f;
        }

        public string Prompt => $"Collect: {_itemName}";

        public void Interact()
            => CaseNetSync.Instance.RequestCollect(_index, _itemName);
    }
}
