using UnityEngine;

namespace CaseClosed.Game.Prototype.Net
{
    /// <summary>
    /// Which build this is, readable from the menu.
    ///
    /// WHY IT MATTERS MORE THAN IT LOOKS: two players on mismatched builds fail in
    /// ways that imitate real bugs — a client connects and then desyncs, or an RPC
    /// signature has changed and messages are silently dropped. NGO has no version
    /// handshake here (connection approval is off), so the only defence is both
    /// people reading the same line off their own screen before blaming the game.
    ///
    /// Written at build time into Resources by PrototypeBuildScript. Falls back to
    /// an honest "(editor)" rather than a stale value when that file is absent.
    /// </summary>
    public static class BuildStamp
    {
        public const string ResourceName = "build_stamp";

        private static string _line;

        /// <summary>One line: version, git hash, build time.</summary>
        public static string Line
        {
            get
            {
                if (_line != null) return _line;

                var asset = Resources.Load<TextAsset>(ResourceName);
                _line = asset != null && !string.IsNullOrWhiteSpace(asset.text)
                    ? asset.text.Trim()
                    : $"v{Application.version} · (editor, unstamped)";

                return _line;
            }
        }
    }
}
