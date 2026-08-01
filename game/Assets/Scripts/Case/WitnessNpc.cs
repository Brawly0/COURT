using System.Collections.Generic;
using UnityEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// A witness NPC (graybox: a capsule with a name). Each interaction yields
    /// the next line of their opening statement; after the statement runs dry,
    /// they want to get back to work. Follow-up questioning depth arrives with
    /// the interview UI phase — this is the spine.
    /// </summary>
    public class WitnessNpc : MonoBehaviour, IInteractable
    {
        private string _name;
        private List<string> _statement;
        private int _next;
        private static readonly string[] DoneLines =
        {
            "That's all I know.",
            "I have work to do.",
            "Ask someone else.",
        };

        public void Init(string witnessName, List<string> statement)
        {
            _name = witnessName;
            _statement = statement;
            Labels.Attach(transform, witnessName, 1.4f);
        }

        public string Prompt => _next < _statement.Count
            ? $"Question {_name}"
            : $"{_name} is done talking";

        public void Interact()
        {
            if (CaseRuntime.Instance.BellRung)
            {
                CaseRuntime.Instance.AddLog($"{_name}: \"Court is in session. Go.\"");
                return;
            }
            if (_next < _statement.Count)
                CaseRuntime.Instance.AddLog($"{_name}: \"{_statement[_next++]}\"");
            else
                CaseRuntime.Instance.AddLog($"{_name}: \"{DoneLines[_next++ % DoneLines.Length]}\"");
        }
    }

    /// <summary>World-space floating labels for graybox objects.</summary>
    public static class Labels
    {
        public static TextMesh Attach(Transform parent, string text, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = 0.08f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            go.AddComponent<FaceCamera>();
            return tm;
        }
    }
}
