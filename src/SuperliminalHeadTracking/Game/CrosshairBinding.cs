using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SuperliminalHeadTracking.Game
{
    /// <summary>
    /// Superliminal draws its own crosshair, so the mod moves that rather than
    /// drawing a second one over it.
    ///
    /// Which properties a mod may own here is decided by what the game writes every
    /// frame, not by what looks convenient. DrawCursorScriptHand.Update picks a
    /// texture and a size and calls DrawCenteredGUI, which writes the RawImage's
    /// SIZE; it also writes cursor.gameObject.SetActive. It never writes
    /// anchoredPosition and never writes the Graphic's enabled flag, so those two
    /// are the mod's, and the two next to them are the game's - reaching for
    /// SetActive to hide the crosshair would be overwritten on the next Update.
    ///
    /// The grab-progress ring and the console button prompt sit on the same spot and
    /// have to travel with the cursor, or the reticle separates from its own progress
    /// indicator the moment the head moves.
    ///
    /// Each element's own resting anchoredPosition is captured on first bind and
    /// every offset is applied relative to that, so the mod never has to know where
    /// the game chose to put them, and Release puts them back exactly.
    /// </summary>
    public class CrosshairBinding
    {
        private const float SearchInterval = 0.5f;

        private readonly List<RectTransform> _elements = new List<RectTransform>();
        private readonly List<Graphic> _graphics = new List<Graphic>();
        private readonly List<Vector2> _restingPositions = new List<Vector2>();
        private readonly List<bool> _restingEnabled = new List<bool>();

        private Component _cursorScript;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private bool _offsetApplied;
        private bool _hidden;
        private float _nextSearchTime;

        /// <summary>The crosshair elements were found and can be moved.</summary>
        public bool IsBound { get { return _elements.Count > 0 && _canvasRect != null; } }

        /// <summary>The offset last written, in canvas units.</summary>
        public Vector2 AppliedOffset { get; private set; }

        /// <summary>Half the canvas width and height, in canvas units.</summary>
        public Vector2 CanvasHalfExtent
        {
            get
            {
                Rect r = _canvasRect.rect;
                return new Vector2(r.width * 0.5f, r.height * 0.5f);
            }
        }

        /// <summary>
        /// Finds the live crosshair. Cheap to call repeatedly - it returns immediately
        /// once bound, and Superliminal recreates the HUD on every level load, so the
        /// binding has to be re-established rather than taken once at startup.
        /// </summary>
        public bool TryBind()
        {
            if (IsBound && _cursorScript != null) return true;

            Release();

            if (GameReflection.CursorScriptType == null) return false;

            // FindObjectOfType walks the scene. Between levels there is nothing to
            // find and this is called from the render hook, so an unthrottled search
            // would be a scene walk every frame of every loading screen.
            if (Time.realtimeSinceStartup < _nextSearchTime) return false;
            _nextSearchTime = Time.realtimeSinceStartup + SearchInterval;

            Object found = Object.FindObjectOfType(GameReflection.CursorScriptType);
            if (found == null) return false;

            _cursorScript = (Component)found;

            Canvas canvas = _cursorScript.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                _cursorScript = null;
                return false;
            }
            _canvas = canvas;
            _canvasRect = canvas.GetComponent<RectTransform>();
            if (_canvasRect == null)
            {
                _cursorScript = null;
                return false;
            }

            // The cursor itself is the first RawImage under the canvas, which is how
            // DrawCursorScriptHand.Start finds it too.
            RawImage cursor = canvas.GetComponentInChildren<RawImage>(true);
            AddElement(cursor);

            AddElement(GetRawImageField("GrabProgress"));
            AddElement(GetRawImageField("ButtonPrompt_Consoles"));

            if (_elements.Count == 0)
            {
                Release();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Moves the crosshair to the given offset from its resting place, in canvas
        /// units, +x right and +y up.
        /// </summary>
        public void SetOffset(Vector2 offset)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                RectTransform rt = _elements[i];
                if (rt == null) continue;
                rt.anchoredPosition = _restingPositions[i] + offset;
            }
            AppliedOffset = offset;
            _offsetApplied = offset.x != 0f || offset.y != 0f;
        }

        /// <summary>
        /// Puts the crosshair back where the game left it. Called whenever tracking
        /// stops applying, so a paused game or a disabled mod does not leave the
        /// crosshair parked off centre.
        /// </summary>
        public void ClearOffset()
        {
            if (!_offsetApplied) return;
            SetOffset(Vector2.zero);
            _offsetApplied = false;
        }

        /// <summary>
        /// Shows or hides the crosshair, through the Graphic's own enabled flag.
        ///
        /// Deliberately not GameObject.SetActive: DrawCursorScriptHand.Update owns
        /// that and writes it every frame, so a mod that reached for it would be
        /// fighting the game. The enabled flag, like anchoredPosition, is untouched
        /// by the game and is therefore the mod's to own.
        ///
        /// This is for the one case where centring the crosshair would be a lie -
        /// the aim point behind the tracked camera at an extreme head turn, where
        /// there is no honest screen position for it and centre is the single most
        /// convincing wrong answer, because centre is where an uncompensated
        /// crosshair sits.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_hidden == !visible) return;
            _hidden = !visible;

            for (int i = 0; i < _graphics.Count; i++)
            {
                Graphic graphic = _graphics[i];
                // Unhiding restores what was captured at bind, not a blanket true.
                // Nothing in DrawCursorScriptHand ever writes the enabled flag on
                // ButtonPrompt_Consoles, so whatever the prefab ships with is what
                // stands - and writing true would leave a console button prompt drawn
                // on a PC HUD with nothing left to undo it.
                if (graphic != null) graphic.enabled = visible && _restingEnabled[i];
            }
        }

        public void Release()
        {
            ClearOffset();
            SetVisible(true);
            _elements.Clear();
            _graphics.Clear();
            _restingPositions.Clear();
            _restingEnabled.Clear();
            _canvasRect = null;
            _canvas = null;
            _cursorScript = null;
        }

        /// <summary>What was bound, for the startup log.</summary>
        public string Describe()
        {
            if (!IsBound) return "not bound";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _elements.Count; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(_elements[i].name)
                  .Append(" at (").Append(_restingPositions[i].x.ToString("F0"))
                  .Append(',').Append(_restingPositions[i].y.ToString("F0")).Append(')');
            }
            Rect r = _canvasRect.rect;
            sb.Append(" on a ").Append(r.width.ToString("F0")).Append('x')
              .Append(r.height.ToString("F0")).Append(' ').Append(_canvas.renderMode)
              .Append(" canvas");

            // The crosshair is written from the player camera's render hook, so the
            // canvas has to be drawn after that camera or the move lands a frame late.
            // ScreenSpaceOverlay always is; a camera-space canvas is only safe while
            // its camera has the higher depth, and that is a scene setting a patch can
            // change, so both depths go in the log rather than an assumption.
            Camera worldCamera = _canvas.worldCamera;
            Camera playerCamera = GameReflection.PlayerCamera;
            if (worldCamera != null && playerCamera != null)
            {
                sb.Append(" rendered by ").Append(worldCamera.name)
                  .Append(" at depth ").Append(worldCamera.depth.ToString("F0"))
                  .Append(" against the player camera's ")
                  .Append(playerCamera.depth.ToString("F0"));
            }
            return sb.ToString();
        }

        private RawImage GetRawImageField(string fieldName)
        {
            FieldInfo field = _cursorScript.GetType().GetField(
                fieldName, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(_cursorScript) as RawImage;
        }

        /// <summary>
        /// Adds an element unless it is already covered - a descendant of one already
        /// in the list would otherwise be moved twice, once by its parent and once by
        /// itself, and land at double the offset.
        /// </summary>
        private void AddElement(Graphic graphic)
        {
            if (graphic == null) return;

            RectTransform rt = graphic.rectTransform;
            if (rt == null) return;

            for (int i = 0; i < _elements.Count; i++)
            {
                if (IsSelfOrDescendant(rt, _elements[i])) return;
            }

            _elements.Add(rt);
            _graphics.Add(graphic);
            _restingPositions.Add(rt.anchoredPosition);
            _restingEnabled.Add(graphic.enabled);
        }

        private static bool IsSelfOrDescendant(Transform candidate, Transform ancestor)
        {
            for (Transform t = candidate; t != null; t = t.parent)
            {
                if (t == ancestor) return true;
            }
            return false;
        }
    }
}
