using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Replaces the standard OS mouse pointer with a shotgun sprite.
/// Lives on a Screen Space - Overlay canvas (so it draws above the game UI) and
/// simply follows the mouse every frame. Its pivot IS the muzzle, so the aim point
/// under the pointer is the gun's tip rather than the middle of the artwork.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ShotgunCursor : MonoBehaviour {
  [Header("Cursor")]
  [Tooltip("Where the muzzle sits inside the sprite, normalised 0-1 (x right, y up). " +
           "This is the point that ends up exactly under the mouse.")]
  [SerializeField] private Vector2 hotspot = new Vector2(0.16f, 0.84f);
  [Tooltip("Length of the sprite's longest side, in screen pixels.")]
  [SerializeField] private float screenSize = 240f;
  [SerializeField] private bool hideSystemCursor = true;

  private RectTransform rectTransform;

  private void Awake() {
    rectTransform = GetComponent<RectTransform>();

    // The pivot doubles as the cursor hotspot, so the muzzle lines up with the pointer.
    rectTransform.pivot = hotspot;
    FitToSprite();

    if (hideSystemCursor) {
      Cursor.visible = false;
    }
  }

  // Size the image from its own sprite so the shotgun keeps its aspect ratio.
  private void FitToSprite() {
    Image image = GetComponent<Image>();
    if (image == null || image.sprite == null || screenSize <= 0f) return;

    Vector2 spriteSize = new Vector2(image.sprite.rect.width, image.sprite.rect.height);
    float longest = Mathf.Max(spriteSize.x, spriteSize.y);
    if (longest <= 0f) return;

    rectTransform.sizeDelta = spriteSize * (screenSize / longest);
  }

  private void Update() {
    // Overlay canvas at constant pixel size: canvas position == screen pixels.
    rectTransform.position = Input.mousePosition;
  }

  // Give the normal pointer back if this cursor ever goes away.
  private void OnDisable() {
    if (hideSystemCursor) Cursor.visible = true;
  }

  private void OnDestroy() {
    if (hideSystemCursor) Cursor.visible = true;
  }
}
